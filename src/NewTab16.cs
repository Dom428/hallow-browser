// Hallow Browser 1.6 - the Hallow family on the new tab page: "Your games" (Hallow Hub's library, read only; Play asks Hub first
// and otherwise uses the game's own launch link - steam://rungameid/<id> - only when you click), "Recent clips" (Hallow Clips'
// status.json: thumbnails, a click opens the clip) and a quick Overlay on/off (Overlay's "toggle" over its pipe). Everything is
// local: other Hallow apps' files and pipes. Each row hides when that app isn't installed. The page draws first and fetches
// family.json afterwards, so the rows never slow the new tab down.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace HallowBrowser
{
    public class FamGame { public string Id, Name, Pic, PicSrc; public bool Playing; }
    public class FamClip { public string Id, File, Name, Game, When, Pic, PicSrc; public double Secs; }

    public static class NewTabFamily
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        public const int MaxGames = 8, MaxClips = 6;
        public static readonly string[] ClipTypes = { ".mp4", ".mkv", ".webm", ".mov" };
        // tests: HALLOW_BROWSER_LAUNCH_LOG = a file that gets the launch links / clip files instead of opening them (never a real game)
        public static readonly string LaunchLog = BrowserSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_BROWSER_LAUNCH_LOG") : null;
        public static List<FamGame> LastGames = new List<FamGame>(); public static List<FamClip> LastClips = new List<FamClip>();
        public static int Launches, HubPlays, ClipOpens, OverlayToggles;
        public static string LastLaunch;

        // is a Hallow app installed? (tests: its stand-in status.json in the test folder - never the real apps)
        public static bool Installed(string app)
        {
            if (BrowserSettings.TestDir != null) return File.Exists(Path.Combine(Family.DataDir(app), "status.json"));
            try { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Hallow " + app, "Hallow" + app + ".exe")); } catch { return false; }
        }
        static string Read(string path) { try { var f = new FileInfo(path); return f.Exists && f.Length < 8 * 1024 * 1024 ? Encoding.UTF8.GetString(Importer.ReadShared(path)) : null; } catch { return null; } }
        static string Hash(string s) { using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""))).Replace("-", "").Substring(0, 12).ToLowerInvariant(); }

        // Hub: your Steam games (most recently played first), with Hub's own screenshot of each when it has one; playing ones marked
        public static List<FamGame> Games(string hubJson, string hubStatus, string hubDir)
        {
            var res = new List<FamGame>();
            var playing = new HashSet<string>();
            try
            {
                var st = hubStatus == null ? null : js.DeserializeObject(hubStatus) as Dictionary<string, object>; object pl;
                if (st != null && st.TryGetValue("playing", out pl) && pl is System.Collections.IEnumerable)
                    foreach (var o in (System.Collections.IEnumerable)pl) { var d = o as Dictionary<string, object>; object id; if (d != null && d.TryGetValue("id", out id) && id is string) playing.Add((string)id); else if (o is string) playing.Add((string)o); }
            }
            catch { }
            foreach (var g in GameCorner.HubGames(hubJson, hubStatus).Take(MaxGames))
            {
                var pic = hubDir == null ? null : Path.Combine(hubDir, "meta", g.AppId, "shot0.jpg");
                res.Add(new FamGame { Id = g.AppId, Name = g.Name, PicSrc = pic != null && File.Exists(pic) ? pic : null, Playing = playing.Contains("steam:" + g.AppId) });
            }
            return res;
        }
        // Clips: the newest clips (status.json "clips", newest first) whose file is a video
        public static List<FamClip> Clips(string clipsStatus)
        {
            var res = new List<FamClip>();
            try
            {
                var st = clipsStatus == null ? null : js.DeserializeObject(clipsStatus) as Dictionary<string, object>; object cl;
                if (st == null || !st.TryGetValue("clips", out cl) || !(cl is System.Collections.IEnumerable)) return res;
                foreach (var o in (System.Collections.IEnumerable)cl)
                {
                    var d = o as Dictionary<string, object>; if (d == null) continue;
                    object f, g, t, th, du; d.TryGetValue("file", out f); d.TryGetValue("game", out g); d.TryGetValue("time", out t); d.TryGetValue("thumb", out th); d.TryGetValue("durationSec", out du);
                    var file = f as string; if (string.IsNullOrEmpty(file) || !ClipTypes.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                    double secs = 0; try { secs = Convert.ToDouble(du ?? 0, CultureInfo.InvariantCulture); } catch { }
                    var thumb = th as string;
                    res.Add(new FamClip { Id = Hash(file), File = file, Name = Path.GetFileNameWithoutExtension(file), Game = g as string, When = t as string, Secs = secs, PicSrc = thumb != null && File.Exists(thumb) ? thumb : null });
                    if (res.Count >= MaxClips) break;
                }
            }
            catch { }
            return res;
        }
        public static bool? OverlayVisible(string overlayStatus)
        {
            try { var st = overlayStatus == null ? null : js.Deserialize<Dictionary<string, object>>(overlayStatus); object v; if (st != null && st.TryGetValue("visible", out v) && v is bool) return (bool)v; } catch { }
            return null;
        }
        public static string Ago(string iso, DateTime now)
        {
            DateTime t; if (!DateTime.TryParse(iso ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return "";
            var d = now - t; if (d.TotalMinutes < 1) return "just now"; if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago"; if (d.TotalDays < 1) return (int)d.TotalHours + " h ago"; if (d.TotalDays < 2) return "yesterday";
            return (int)d.TotalDays + " days ago";
        }
        // pictures go next to the page (fam\), so the page shows them from its own folder; only when they changed
        static string CopyPic(string src, string name)
        {
            if (src == null) return null;
            try
            {
                var fi = new FileInfo(src); if (!fi.Exists || fi.Length > 3 * 1024 * 1024) return null;
                var dir = Path.Combine(Engine.NewTabDir, "fam"); Directory.CreateDirectory(dir);
                var to = Path.Combine(dir, name + Path.GetExtension(src).ToLowerInvariant());
                var ti = new FileInfo(to); if (!ti.Exists || ti.Length != fi.Length || ti.LastWriteTimeUtc < fi.LastWriteTimeUtc) File.Copy(src, to, true);
                return "fam/" + Path.GetFileName(to);
            }
            catch { return null; }
        }
        // the whole family.json (on a background thread)
        public static string Build(BrowserSettings s, DateTime now)
        {
            if (!s.NewTabHallow) return js.Serialize(new Dictionary<string, object> { { "on", false } });
            object games = null, clips = null, overlay = null;
            if (Installed("Hub"))
            {
                var hd = Family.DataDir("Hub");
                var g = Games(Read(Path.Combine(hd, "hub.json")), Read(Path.Combine(hd, "status.json")), hd);
                foreach (var x in g) x.Pic = CopyPic(x.PicSrc, "g-" + x.Id);
                LastGames = g;
                games = g.Select(x => new Dictionary<string, object> { { "id", x.Id }, { "n", x.Name }, { "p", x.Pic }, { "on", x.Playing } }).ToList();
            }
            if (Installed("Clips"))
            {
                var c = Clips(Read(Path.Combine(Family.DataDir("Clips"), "status.json")));
                foreach (var x in c) x.Pic = CopyPic(x.PicSrc, "c-" + x.Id);
                LastClips = c;
                clips = c.Select(x => new Dictionary<string, object> { { "id", x.Id }, { "n", x.Game ?? x.Name }, { "w", Ago(x.When, now) }, { "s", x.Secs > 0 ? Math.Round(x.Secs) + " s" : "" }, { "p", x.Pic } }).ToList();
            }
            if (Installed("Overlay"))
            {
                // 1.9: Overlay's own answer over its pipe first (its status.json can lag behind a toggle - the label stayed "off"), the file if it
                // doesn't answer
                bool running = Family.Running("Overlay"); bool? v = null;
                if (running) { var r = OverlayPipe(); v = OverlayVisible(r != null && r.StartsWith("ok ") ? r.Substring(3) : null); }
                if (v == null) v = OverlayVisible(Read(Path.Combine(Family.DataDir("Overlay"), "status.json")));
                overlay = new Dictionary<string, object> { { "visible", v ?? false }, { "running", running } };
            }
            return js.Serialize(new Dictionary<string, object> { { "on", true }, { "games", games }, { "clips", clips }, { "overlay", overlay } });
        }
        static string last;
        public static bool Write(string json)
        {
            try
            {
                if (json == last) return false;
                Directory.CreateDirectory(Engine.NewTabDir);
                var f = Path.Combine(Engine.NewTabDir, "family.json"); var tmp = f + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
                last = json; return true;
            }
            catch { return false; }
        }

        // ---- the clicks (only from the new tab page itself)
        static void Launch(string what)
        {
            LastLaunch = what; Launches++;
            if (LaunchLog != null) { try { File.AppendAllText(LaunchLog, what + "\r\n"); } catch { } return; }   // tests: written down, never opened
            try { Process.Start(new ProcessStartInfo(what) { UseShellExecute = true }); } catch { }
        }
        // Play: Hub first, when its pipe takes a "play" command (Hub 2.16 has none: it answers "error unknown command"); then the
        // game's own launch link. Only for a game that's in the list the page was given.
        public static string Play(string id)
        {
            var g = LastGames.FirstOrDefault(x => x.Id == id); if (g == null || !id.All(char.IsDigit)) return "not in the list";
            if (Family.Running("Hub"))
            {
                var r = Family.Send("Hub", "play steam:" + id, 3000);
                if (r != null && (r == "ok" || r.StartsWith("ok "))) { HubPlays++; LastLaunch = "hub:steam:" + id; return "hub"; }
            }
            Launch("steam://rungameid/" + id);
            return "link";
        }
        public static string OpenClip(string id)
        {
            var c = LastClips.FirstOrDefault(x => x.Id == id); if (c == null) return "not in the list";
            if (!File.Exists(c.File) || !ClipTypes.Contains(Path.GetExtension(c.File).ToLowerInvariant())) return "missing";
            ClipOpens++; Launch(c.File);
            return "opened";
        }
        public static Func<string> OverlayPipe = () => Family.Send("Overlay", "status", 1500);
        public static string ToggleOverlay()
        {
            if (!Installed("Overlay")) return "not installed";
            var r = Family.Send("Overlay", "toggle", 3000); OverlayToggles++;
            return r == null ? "not running" : r;
        }

        // the page's part: styles and the script that fetches family.json after the first paint
        public static string Css(string fg, string muted, string line, string tile, string tileHover, string head)
        {
            return "#hf{width:min(720px,90vw);margin-top:20px;display:flex;flex-direction:column;gap:14px}#hf:empty{display:none}" +
                "#hf h2{font:600 11px 'Cascadia Mono',Consolas,monospace;letter-spacing:.12em;color:" + head + ";margin:0 0 8px;display:flex;align-items:center;gap:10px}" +
                // 1.15.1: the row scrolls sideways (snapping to whole cards), card titles get two lines
                ".hr{display:flex;gap:10px;overflow-x:auto;overflow-y:hidden;padding:2px 2px 8px;scroll-snap-type:x proximity;scrollbar-width:thin}.hr>.hc{scroll-snap-align:start}#hf section{min-width:0}" +
                "#hf section.ovs{display:flex;align-items:center}#hf section.ovs h2{margin:0;flex-wrap:wrap}" +
                ".hc{flex:0 0 auto;width:128px;border-radius:10px;background:" + tile + ";border:1px solid " + line + ";overflow:hidden;color:" + fg + ";text-decoration:none;font-size:12px;cursor:pointer;position:relative}" +
                ".hc:hover,.hc:focus{background:" + tileHover + ";border-color:" + head + ";outline:none}.hc .pic{height:62px;background:" + line + " center/cover no-repeat;display:flex;align-items:center;justify-content:center;font:700 22px 'Segoe UI',sans-serif;color:" + muted + "}" +
                ".hc .t{padding:6px 8px 2px;line-height:16px;height:32px;overflow:hidden;display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:2;overflow-wrap:anywhere}.hc .m{padding:0 8px 7px;color:" + muted + ";font-size:11px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}" +
                ".hc .pl{position:absolute;right:6px;top:40px;width:26px;height:26px;border-radius:13px;border:0;background:" + Look.FillOf(head) + ";color:" + Look.TextOnFill(Look.FillOf(head)) + ";font-size:11px;cursor:pointer}" +
                ".ov{border:1px solid " + line + ";background:" + tile + ";color:" + fg + ";border-radius:12px;padding:3px 12px;font:11.5px 'Segoe UI',sans-serif;cursor:pointer}.ov.on{border-color:" + head + ";color:" + head + "}";
        }
        public const string Script =
            "function hfDraw(F){var h=document.getElementById('hf');h.textContent='';if(!F||!F.on)return;" +
            "function sec(title,extra){var s=el('section');var t=el('h2',null,title);if(extra)t.appendChild(extra);s.appendChild(t);return s;}" +
            "if(F.games&&F.games.length){var s=sec('YOUR GAMES');var r=el('div','hr');F.games.forEach(function(g){var c=el('div','hc');c.tabIndex=0;c.title=g.n+(g.on?' (playing)':'');var p=el('div','pic',g.p?'':(g.n||'?').charAt(0));if(g.p)p.style.backgroundImage='url(\"'+g.p+'\")';c.appendChild(p);c.appendChild(el('div','t',g.n));c.appendChild(el('div','m',g.on?'Playing now':'Steam'));" +
            "var b=el('button','pl','\\u25B6');b.title='Play '+g.n;b.onclick=function(e){e.stopPropagation();post('hallow:nt-play\\t'+g.id);};c.appendChild(b);r.appendChild(c);});s.appendChild(r);h.appendChild(s);}" +
            "if(F.clips&&F.clips.length){var s2=sec('RECENT CLIPS');var r2=el('div','hr');F.clips.forEach(function(c0){var c=el('div','hc');c.tabIndex=0;c.title='Open this clip';var p=el('div','pic',c0.p?'':'\\u25B6');if(c0.p)p.style.backgroundImage='url(\"'+c0.p+'\")';c.appendChild(p);c.appendChild(el('div','t',c0.n));c.appendChild(el('div','m',[c0.w,c0.s].filter(Boolean).join(' - ')));" +
            "c.onclick=function(){post('hallow:nt-clip\\t'+c0.id);};c.onkeydown=function(e){if(e.key==='Enter')c.onclick();};r2.appendChild(c);});s2.appendChild(r2);h.appendChild(s2);}" +
            "if(F.overlay){var b2=el('button','ov'+(F.overlay.visible?' on':''),'Overlay: '+(F.overlay.visible?'on':'off'));b2.title=F.overlay.running?'Turn Hallow Overlay on or off':'Hallow Overlay isn\\'t running';b2.disabled=!F.overlay.running;b2.onclick=function(){post('hallow:nt-overlay');};var s3=sec('HALLOW OVERLAY',b2);s3.className='ovs';h.appendChild(s3);}}" +
            "window.hallowFamily=function(){return fetch('family.json?'+Date.now(),{cache:'no-store'}).then(function(r){return r.json();}).then(function(F){hfDraw(F);return F&&F.on?1:0;}).catch(function(){return -1;});};" +
            // only once the engine says the page's first paint is done (a pre-warmed page that is never shown fetches it when shown)
            "(function(){var done=false;function go(){if(done)return;done=true;setTimeout(window.hallowFamily,0);}" +
            "try{new PerformanceObserver(function(l){if(l.getEntries().some(function(e){return e.name==='first-contentful-paint';}))go();}).observe({type:'paint',buffered:true});}catch(e){}" +
            "setTimeout(function(){if(!document.hidden)go();},1500);})();";
    }

    public partial class BrowserWindow
    {
        static readonly object famLock = new object(); static bool famBusy, famAgain;
        public static int FamilyWrites;
        // family.json in the background; at start a little after the first page is on its way
        public void RefreshFamily(int delayMs)
        {
            if (snap || Private) return;
            if (famBusy) { famAgain = true; return; }   // 1.9: asked again while busy (e.g. right after the Overlay switch) = once more after, not dropped
            famBusy = true; var s = S;
            Task.Run(async () =>
            {
                try { if (delayMs > 0) await Task.Delay(delayMs); lock (famLock) if (NewTabFamily.Write(NewTabFamily.Build(s, DateTime.Now))) FamilyWrites++; }
                catch { }
                finally { famBusy = false; }
            }).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (famAgain) { famAgain = false; RefreshFamily(0); }
                foreach (var t in Tabs) if (Omni.IsNewTab(t.Url) && t.Core != null) try { var __ = t.Core.ExecuteScriptAsync("window.hallowFamily&&hallowFamily()"); } catch { }
            })));
        }
        public UIElement[] FamilyRowSettings()
        {
            return new UIElement[] {
                Ui.Switch("Your games, recent clips and the Overlay switch", "Under the tiles: your games from Hallow Hub (Play asks Hub, or opens the game's own Steam link - only when you click), your newest clips from Hallow Clips (click to open) and Hallow Overlay on / off. Read from those apps' own files on this PC; each row is only there when that app is installed. The page draws first and adds these afterwards.", S.NewTabHallow, on => { S.NewTabHallow = on; S.Save(); RefreshFamily(0); }) };
        }
        // the page's buttons (only from the new tab page itself, never from a private window)
        bool FamilyMessage(string m, string source)
        {
            if (!m.StartsWith("hallow:nt-play\t") && !m.StartsWith("hallow:nt-clip\t") && m != "hallow:nt-overlay") return false;
            if (source == null || !Omni.IsNewTab(source) || Private) return true;
            var arg = m.Contains("\t") ? m.Substring(m.IndexOf('\t') + 1) : "";
            if (m.StartsWith("hallow:nt-play\t")) PlayFromNewTab(arg, null);   // 1.8: and game mode at once
            else if (m.StartsWith("hallow:nt-clip\t")) { var r = NewTabFamily.OpenClip(arg); if (r == "missing") Toast("That clip isn't there any more.", 4); }
            else Task.Run(() => NewTabFamily.ToggleOverlay()).ContinueWith(r => Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(400); RefreshFamily(0); })));
            return true;
        }
    }
}
