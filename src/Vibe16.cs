// Hallow Browser 1.6 - the optional "vibe" (OFF by default): subtle UI sounds (a tab opens / closes, a download is done, game mode
// turns on) and a calm ambience loop. Every sound is synthesised when the browser is built (tools\gen-sounds.ps1: sine tones with
// envelopes) and embedded - nothing recorded, nothing downloaded. A volume slider for each; while a game runs everything is quiet
// (only the short game-mode cue plays as game mode turns on) and the ambience pauses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowBrowser
{
    public static class Vibe
    {
        public static readonly string[] Names = { "open", "close", "download", "game" };
        public static int Played, Opened, Skipped; public static string Last;
        static readonly Dictionary<string, MediaPlayer> players = new Dictionary<string, MediaPlayer>();
        static MediaPlayer amb; public static bool AmbiencePlaying;
        static string Dir { get { return Path.Combine(BrowserSettings.RootLocalDir, "sounds"); } }
        public static byte[] Wav(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowBrowser.Sounds." + name + ".wav"))
            { if (s == null) return null; var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
        }
        // the embedded sound as a file (MediaPlayer plays files), written once
        static string FileOf(string name)
        {
            var f = Path.Combine(Dir, name + ".wav");
            try
            {
                var b = Wav(name); if (b == null) return null;
                if (!File.Exists(f) || new FileInfo(f).Length != b.Length) { Directory.CreateDirectory(Dir); File.WriteAllBytes(f, b); }
                return f;
            }
            catch { return File.Exists(f) ? f : null; }
        }
        // the rule: sounds on, a volume, not in --snap; during a game only the game-mode cue
        public static bool ShouldPlay(string name, bool soundsOn, int volume, bool game, bool test)
        {
            if (!soundsOn || Array.IndexOf(Names, name) < 0) return false;
            if (volume <= 0 && !test) return false;
            if (game && name != "game") return false;
            return true;
        }
        public static void Play(string name, BrowserWindow w)
        {
            try
            {
                if (w == null || w.IsSnap || w.S == null) return;
                if (!ShouldPlay(name, w.S.Sounds, w.S.SoundVolume, w.Game != null, BrowserSettings.TestDir != null)) { if (w.S.Sounds) Skipped++; return; }
                MediaPlayer p;
                if (!players.TryGetValue(name, out p))
                {
                    var f = FileOf(name); if (f == null) return;
                    p = new MediaPlayer(); p.MediaOpened += (a, b) => Opened++; p.Open(new Uri(f)); players[name] = p;
                }
                p.Volume = Math.Max(0, Math.Min(1, w.S.SoundVolume / 100.0 * 0.6));   // "subtle": the top of the slider is still soft
                p.Position = TimeSpan.Zero; p.Play();
                Played++; Last = name;
            }
            catch { }
        }
        // ambience: on when it's switched on and no game runs; follows its own volume
        public static void Ambience(BrowserWindow w)
        {
            try
            {
                bool want = w != null && !w.IsSnap && w.S.Ambience && w.Game == null && !Program.Boosted && (w.S.AmbienceVolume > 0 || BrowserSettings.TestDir != null);
                if (!want) { if (amb != null && AmbiencePlaying) { amb.Pause(); AmbiencePlaying = false; } return; }
                if (amb == null)
                {
                    var f = FileOf("ambience"); if (f == null) return;
                    amb = new MediaPlayer(); amb.MediaOpened += (a, b) => Opened++;
                    amb.MediaEnded += (a, b) => { try { amb.Position = TimeSpan.Zero; amb.Play(); } catch { } };   // the loop is seamless (whole cycles)
                    amb.Open(new Uri(f));
                }
                amb.Volume = Math.Max(0, Math.Min(1, w.S.AmbienceVolume / 100.0 * 0.5));
                if (!AmbiencePlaying) { amb.Play(); AmbiencePlaying = true; }
            }
            catch { }
        }
        public static void GameChanged(BrowserWindow w) { Ambience(w); }
        public static void StopAll()
        {
            try { if (amb != null) { amb.Stop(); amb.Close(); amb = null; } AmbiencePlaying = false; foreach (var p in players.Values) try { p.Close(); } catch { } players.Clear(); } catch { }
        }
    }

    public partial class BrowserWindow
    {
        public UIElement[] VibeSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Muted("Optional, and off by default. All the sounds are made by the browser itself (synthesised tones - nothing recorded or downloaded). While a game runs they stay quiet: only a short cue as game mode turns on, and the ambience pauses.", 12, 8));
            items.Add(Ui.Switch("UI sounds", "A soft blip when a tab opens or closes, a chime when a download is done, a cue when game mode turns on.", S.Sounds, on => { S.Sounds = on; S.Save(); if (on) Vibe.Play("open", this); }));
            items.Add(VolumeRow("Sounds volume", S.SoundVolume, v => { S.SoundVolume = v; S.Save(); }, () => Vibe.Play("download", this)));
            items.Add(Ui.Switch("Background ambience", "A calm, slow pad on a 12-second loop while the browser is open.", S.Ambience, on => { S.Ambience = on; S.Save(); Vibe.Ambience(this); }));
            items.Add(VolumeRow("Ambience volume", S.AmbienceVolume, v => { S.AmbienceVolume = v; S.Save(); Vibe.Ambience(this); }, null));
            return items.ToArray();
        }
        FrameworkElement VolumeRow(string label, int value, Action<int> changed, Action test)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = Ui.Muted(label, 12); t.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(t);
            var sl = new Slider { Minimum = 0, Maximum = 100, Value = value, SmallChange = 5, LargeChange = 10, IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center, ToolTip = value + "%" };
            sl.ValueChanged += (a, b) => { sl.ToolTip = (int)sl.Value + "%"; changed((int)sl.Value); };
            Grid.SetColumn(sl, 1); g.Children.Add(sl);
            if (test != null) { var b = Ui.Btn("Try", test, false, "volume-low"); b.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(b, 2); g.Children.Add(b); }
            return g;
        }
    }
}
