// Hallow Browser 1.6 - unit tests: the migration of 1.5's data into the "Default" profile (on a fake 1.5 folder: nothing moved or
// changed, a backup of the settings files), profiles (ids, names, folders, WebView2 profile names, pipes, arguments), spell check
// words and Chromium's dictionary file, memory saver levels and the battery rule, the synthesised sounds (valid WAVs, no clipping,
// a seamless loop) and when they play, installed sites (manifest parsing, scope, icons, .ico, a Start menu shortcut in the test
// folder), the Hallow family rows from stand-in Hub / Clips / Overlay files, the reading list and notes, notes through sync between
// two test PCs (encrypted), and the contrast of the new colours in the dark, light and private looks.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowBrowser.Tests
{
    public static class V16
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }
        static string root;

        public static void All()
        {
            root = BrowserSettings.TestDir;
            MigrationTests(); ProfileTests(); SpellTests(); MemoryTests(); SoundTests(); AppTests(); FamilyTests(); ReadingTests(); NotesSyncTests(); ContrastTests();
        }

        static string Sha(string f) { using (var s = SHA256.Create()) using (var x = File.OpenRead(f)) return BitConverter.ToString(s.ComputeHash(x)); }
        static Dictionary<string, string> Hashes(string dir) { return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "backup-1.5" + Path.DirectorySeparatorChar) && !f.EndsWith("profiles.json")).ToDictionary(f => f.Substring(dir.Length), Sha); }

        // ---- 1.5's data becomes the Default profile: a fake 1.5 folder (roaming + local), migrated, compared file by file
        static void MigrationTests()
        {
            BrowserSettings.ProfileId = ProfileStore.DefaultId;
            var rd = BrowserSettings.RootDir; var ld = BrowserSettings.RootLocalDir;
            foreach (var f in new[] { "profiles.json", "settings.json", "session.json", "workspaces.json", "bookmarks.json" }) try { File.Delete(Path.Combine(rd, f)); } catch { }
            try { Directory.Delete(Path.Combine(rd, "backup-1.5"), true); } catch { }
            Directory.CreateDirectory(rd);
            File.WriteAllText(Path.Combine(rd, "settings.json"), "{\"Search\":\"google\",\"SleepMinutes\":30,\"SettingsVersion\":2,\"SidebarSites\":[{\"Id\":\"discord\",\"Name\":\"Discord\",\"Url\":\"https://discord.com/app\",\"Width\":500}],\"SyncOn\":true,\"SyncFolder\":\"D:\\\\Sync\"}");
            File.WriteAllText(Path.Combine(rd, "session.json"), "{\"Tabs\":[{\"Url\":\"https://one.example/\",\"Title\":\"One\"},{\"Url\":\"https://two.example/\",\"Title\":\"Two\"}],\"Active\":1}");
            File.WriteAllText(Path.Combine(rd, "workspaces.json"), "{\"Current\":\"w1\",\"Items\":[{\"Id\":\"w1\",\"Name\":\"Gaming\"}]}");
            File.WriteAllText(Path.Combine(rd, "bookmarks.json"), "{\"Items\":[]}");
            var eng = Path.Combine(ld, "Profile", "EBWebView", "Default"); Directory.CreateDirectory(eng);
            File.WriteAllText(Path.Combine(eng, "Preferences"), "{\"profile\":{\"name\":\"fake 1.5 engine profile\"}}");
            File.WriteAllText(Path.Combine(eng, "Cookies"), "fake cookie jar - never read");
            File.WriteAllText(Path.Combine(ld, "Profile", "hallow-history.json"), "{\"Items\":[{\"Url\":\"https://hist.example/\",\"Title\":\"H\",\"Visits\":3}]}");
            Directory.CreateDirectory(Path.Combine(ld, "ShieldLoaded")); File.WriteAllText(Path.Combine(ld, "ShieldLoaded", "manifest.json"), "{}");
            var beforeR = Hashes(rd); var beforeL = Hashes(ld);
            Ok(!File.Exists(ProfileStore.FilePath), "migration: a 1.5 folder has no profiles.json");
            var st = ProfileStore.EnsureMigrated();
            Ok(File.Exists(ProfileStore.FilePath) && st.Items.Count == 1 && st.Items[0].Id == "default" && st.Items[0].Name == "Default" && st.StartWith == "default", "migration: profiles.json lists one profile, \"Default\", which opens at start");
            Ok(st.Migrated != null && st.Backup != null && Directory.Exists(st.Backup), "migration: when, and where the backup is (" + (st.Backup == null ? "" : st.Backup.Replace(root, "<test>")) + ")");
            Ok(new[] { "settings.json", "session.json", "workspaces.json", "bookmarks.json" }.All(f => File.Exists(Path.Combine(st.Backup, f)) && Sha(Path.Combine(st.Backup, f)) == Sha(Path.Combine(rd, f))), "migration: the backup holds exact copies of 1.5's settings files");
            var afterR = Hashes(rd); var afterL = Hashes(ld);
            Ok(beforeR.Count == afterR.Count && beforeR.All(kv => afterR.ContainsKey(kv.Key) && afterR[kv.Key] == kv.Value), "migration: every roaming file unchanged (" + beforeR.Count + " files)");
            Ok(beforeL.Count == afterL.Count && beforeL.All(kv => afterL.ContainsKey(kv.Key) && afterL[kv.Key] == kv.Value), "migration: every local file unchanged - the engine's profile, cookies, history, Shield (" + beforeL.Count + " files)");
            Ok(BrowserSettings.Dir == rd && BrowserSettings.LocalDir == ld && BrowserSettings.ProfileDir == Path.Combine(ld, "Profile"), "migration: the Default profile uses 1.5's folders");
            Ok(BrowserSettings.WebViewProfile == null && BrowserSettings.PrivateWebViewProfile == "HallowPrivate" && BrowserSettings.EngineProfileFolder(null) == eng, "migration: ... and the engine's default WebView2 profile (the one with 1.5's sign-ins), private windows as before");
            Ok(HistoryStore.DefaultPath == Path.Combine(ld, "Profile", "hallow-history.json"), "migration: the history store where 1.5 kept it");
            var s = BrowserSettings.Load();
            Ok(s.Search == "google" && s.SidebarSites.Count == 1 && s.SidebarSites[0].Width == 500 && s.SyncOn && s.SyncFolder == "D:\\Sync", "migration: 1.5's settings load as they were (search, sidebar, sync)");
            Ok(s.MemorySaver == "custom" && s.SleepMinutes == 30 && s.SettingsVersion == 3, "migration: a 1.5 \"sleep after 30 min\" stays (memory saver: Custom)");
            var ses = BrowserSettings.LoadSession(); Ok(ses != null && ses.Tabs.Count == 2 && ses.Active == 1, "migration: the open tabs from 1.5 come back");
            var again = ProfileStore.EnsureMigrated(); Ok(again.Migrated == st.Migrated && again.Items.Count == 1, "migration: runs once (a second start finds profiles.json)");
            var mod = File.ReadAllText(Path.Combine(rd, "settings.json")); File.WriteAllText(Path.Combine(rd, "settings.json"), "{\"Search\":\"bing\"}");
            ProfileStore.EnsureMigrated(); Ok(File.ReadAllText(Path.Combine(st.Backup, "settings.json")) == mod, "migration: the backup is never overwritten later");
            File.WriteAllText(Path.Combine(rd, "settings.json"), mod);
            // a 1.5 with the usual 15 minutes becomes Balanced
            var ser = new DataContractJsonSerializer(typeof(BrowserSettings));
            var b = (BrowserSettings)ser.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes("{\"SleepMinutes\":15,\"SettingsVersion\":2}"))); b.Fix();
            Ok(b.MemorySaver == "balanced" && !b.StartupBoost && !b.Sounds && !b.Ambience && b.SpellCheck && b.BatterySaver && b.NewTabHallow, "1.6 defaults on a 1.5 file: Balanced, boost / sounds / ambience OFF, spell check, battery saver and the family rows on");
            var n = new BrowserSettings(); n.Fix(); Ok(n.MemorySaver == "balanced" && !n.StartupBoost && !n.Sounds && n.SoundVolume == 40, "1.6 defaults on a new install");
        }

        static void ProfileTests()
        {
            var st = ProfileStore.Load();
            var g = st.Add("Gaming", "#22C55E"); var p2 = st.Add("Gaming PC!", null); var dup = st.Add("gaming", null); var bad = st.Add("   ", null);
            Ok(g != null && g.Id == "gaming" && g.Color == "#22C55E" && p2.Id == "gamingpc" && dup == null && bad == null, "profiles: ids from names, unique; a used name or an empty one is refused");
            Eq(ProfileStore.NewId("Default", new[] { "default" }), "profile", "profiles: \"default\" is never a new id");
            Eq(ProfileStore.NewId("Work", new[] { "default", "work", "work2" }), "work3", "profiles: a number when the id is taken");
            Ok(st.Find("GAMING") == g && st.Find("gaming pc!") == p2 && st.Find("nope") == null, "profiles: found by id or name");
            st.Save(); var back = ProfileStore.Load(); Ok(back.Items.Count == 3 && back.Items[0].Id == "default", "profiles: saved, Default first");
            var odd = new ProfileStore { Items = new List<ProfileInfo> { new ProfileInfo { Id = "x y" }, null, new ProfileInfo { Id = "ok", Name = new string('n', 50), Color = "red" } }, StartWith = "gone" }; odd.Fix();
            Ok(odd.Items.Count == 2 && odd.Items[0].Id == "default" && odd.Items[1].Name.Length == 32 && odd.Items[1].Color == ProfileStore.Colors[0] && odd.StartWith == "default", "profiles: a broken list is tidied (bad ids dropped, Default added first, names cut, colours fixed)");
            // another profile: its own folders, WebView2 profile, private profile, pipe and mutex
            BrowserSettings.ProfileId = "gaming";
            try
            {
                Ok(BrowserSettings.Dir == Path.Combine(root, "Profiles", "gaming") && BrowserSettings.LocalDir == Path.Combine(root, "Local", "Profiles", "gaming"), "profiles: Gaming's files in Profiles\\gaming");
                Ok(BrowserSettings.ProfileDir == Path.Combine(root, "Local", "Profile"), "profiles: one engine folder for all of them");
                Ok(BrowserSettings.WebViewProfile == "Hallow_gaming" && BrowserSettings.PrivateWebViewProfile == "HallowPrivate_gaming" && BrowserSettings.EngineProfileFolder("Hallow_gaming").EndsWith("WV2Profile_hallow_gaming"), "profiles: its own WebView2 profile (and private one)");
                Ok(HistoryStore.DefaultPath == Path.Combine(root, "Local", "Profiles", "gaming", "hallow-history.json"), "profiles: its own history");
                var s = BrowserSettings.Load(); Ok(s.Search == "duckduckgo" && !s.SyncOn, "profiles: its own settings (a new profile starts from the defaults)");
                s.Search = "brave"; s.Save(); BrowserSettings.ProfileId = "default"; Ok(BrowserSettings.Load().Search == "google", "profiles: changing Gaming leaves Default alone"); BrowserSettings.ProfileId = "gaming";
                Ok(BrowserWindow.SyncFolderFor(@"D:\Sync") == @"D:\Sync\profile-gaming", "profiles: Gaming syncs in a sub-folder of the sync folder");
            }
            finally { BrowserSettings.ProfileId = "default"; }
            Ok(BrowserWindow.SyncFolderFor(@"D:\Sync") == @"D:\Sync", "profiles: Default syncs in the folder itself (as 1.5)");
            Ok(Program.PipeApp("default") == "Browser" && Program.PipeApp("gaming") == "Browser-gaming" && Program.PipeApp("gaming").Contains("-"), "profiles: Default answers on \"Browser\", Gaming on \"Browser-gaming\" (the hyphen keeps it out of Hub's app list)");
            Ok(Program.MutexName("default", "") == @"Local\HallowBrowser" && Program.MutexName("gaming", "") == @"Local\HallowBrowser.p.gaming", "profiles: a process per profile (1.5's mutex for Default)");
            string pr, app; List<string> rest;
            Program.ParseArgs(new[] { "--profile", "gaming", "https://a.example/", "--app", "https://app.example/x", "--tray" }, out pr, out app, out rest);
            Ok(pr == "gaming" && app == "https://app.example/x" && rest.Count == 1 && rest[0] == "https://a.example/", "arguments: --profile and --app take their values; the rest are addresses");
            Program.ParseArgs(new[] { "example.com" }, out pr, out app, out rest); Ok(pr == null && app == null && rest.Count == 1, "arguments: a plain address");
            Eq(SiteApps.ShortcutArgs("https://app.example/?a=\"b\"", "gaming"), "--app \"https://app.example/?a=%22b%22\" --profile gaming", "arguments: the shortcut's --app <url> --profile <p>");
            File.Delete(ProfileStore.FilePath); ProfileStore.EnsureMigrated();
        }

        static void SpellTests()
        {
            Ok(SpellRules.Word("Hallow") == "Hallow" && SpellRules.Word(" don't ") == "don't" && SpellRules.Word("Grüße") == "Grüße" && SpellRules.Word("x-ray") == "x-ray", "spell: words (letters, apostrophes, hyphens, any script)");
            Ok(SpellRules.Word("two words") == null && SpellRules.Word("1abc") == null && SpellRules.Word("<b>") == null && SpellRules.Word(new string('a', 65)) == null && SpellRules.Word("") == null, "spell: not words (spaces, a leading digit, markup, too long)");
            var f = SpellRules.DictionaryFile(new[] { "wrods", "Hallow" });
            string md5; using (var m = MD5.Create()) md5 = BitConverter.ToString(m.ComputeHash(Encoding.UTF8.GetBytes("wrods\nHallow\n"))).Replace("-", "").ToLowerInvariant();
            Eq(f, "wrods\nHallow\nchecksum_v1 = " + md5, "spell: Chromium's Custom Dictionary.txt (words, then the MD5 checksum line)");
            Ok(SpellRules.WordsIn(f).SequenceEqual(new[] { "wrods", "Hallow" }), "spell: read back");
            var s = new BrowserSettings(); s.SpellWords = new List<string> { "ok", "ok", "bad word", null }; s.Fix(); Ok(s.SpellWords.Count == 1, "spell: the word list is tidied");
            BrowserSettings.ProfileId = "default"; s.SpellWords = new List<string> { "Hallowgg" }; Engine.WriteSpellWords(s);
            var file = Path.Combine(BrowserSettings.EngineProfileFolder(null), "Custom Dictionary.txt");
            Ok(File.Exists(file) && SpellRules.WordsIn(File.ReadAllText(file)).SequenceEqual(new[] { "Hallowgg" }), "spell: written into the profile's own folder before the engine starts");
            Ok(SpellRules.ValidTag("en-US") && SpellRules.ValidTag("de") && !SpellRules.ValidTag("en US") && !SpellRules.ValidTag(""), "spell: language tags");
            var langs = SpellRules.WindowsLanguages(); Ok(langs != null, "spell: Windows' spell checker languages on this PC: " + (langs.Count == 0 ? "none" : string.Join(", ", langs)));
        }

        static void MemoryTests()
        {
            Ok(Power16.LevelMinutes("off", 0) == 30 && Power16.LevelMinutes("balanced", 0) == 10 && Power16.LevelMinutes("max", 0) == 2 && Power16.LevelMinutes("custom", 15) == 15, "memory saver: Off / Balanced / Max = 30 / 10 / 2 minutes (Custom keeps yours)");
            Ok(Power16.SleepMinutes("balanced", 0, true, true) == 5 && Power16.SleepMinutes("max", 0, true, true) == 1 && Power16.SleepMinutes("off", 0, true, false) == 30, "battery saver: on battery tabs sleep twice as soon (at least a minute); off = no change");
            Ok(Power16.SleepMinutes("custom", 0, true, true) == 0 && Power16.SleepMinutes("balanced", 0, false, true) == 10, "battery saver: \"never\" stays never; on mains nothing changes");
            var states = new List<TabState> { new TabState { Id = 1, Active = true, LastActive = DateTime.Now.AddMinutes(-60) }, new TabState { Id = 2, LastActive = DateTime.Now.AddMinutes(-3) }, new TabState { Id = 3, LastActive = DateTime.Now.AddMinutes(-11) }, new TabState { Id = 4, Audible = true, LastActive = DateTime.Now.AddMinutes(-60) } };
            Ok(Planner.ToSuspend(states, false, DateTime.Now, Power16.LevelMinutes("balanced", 0)).SequenceEqual(new[] { 3 }), "memory saver Balanced: the tab idle 11 min sleeps (not the active one, not one playing sound)");
            Ok(Planner.ToSuspend(states, false, DateTime.Now, Power16.LevelMinutes("max", 0)).SequenceEqual(new[] { 2, 3 }), "memory saver Max: idle 3 min is enough");
            Ok(Planner.ToSuspend(states, false, DateTime.Now, Power16.LevelMinutes("off", 0)).Count == 0, "memory saver Off: 30 minutes");
            Eq(Power16.SavedText(85), "85 MB", "memory saved: MB"); Eq(Power16.SavedText(1536), "1.5 GB", "memory saved: GB"); Eq(Power16.SavedText(0), "", "memory saved: nothing to say");
            var odd = new BrowserSettings { MemorySaver = "turbo", SoundVolume = 300, AmbienceVolume = -5 }; odd.Fix();
            Ok(odd.MemorySaver == "balanced" && odd.SoundVolume == 100 && odd.AmbienceVolume == 0, "1.6 settings: an unknown level = Balanced, volumes 0-100");
        }

        static short[] Samples(byte[] wav, out int rate, out int channels, out int bits)
        {
            rate = BitConverter.ToInt32(wav, 24); channels = BitConverter.ToInt16(wav, 22); bits = BitConverter.ToInt16(wav, 34);
            int n = BitConverter.ToInt32(wav, 40) / 2; var s = new short[n]; for (int i = 0; i < n; i++) s[i] = BitConverter.ToInt16(wav, 44 + i * 2); return s;
        }
        static void SoundTests()
        {
            foreach (var name in new[] { "open", "close", "download", "game", "ambience" })
            {
                var w = Vibe.Wav(name);
                if (w == null) { Ok(false, "sounds: " + name + ".wav is embedded"); continue; }
                int rate, ch, bits; var s = Samples(w, out rate, out ch, out bits);
                var secs = (double)s.Length / rate; var peak = s.Max(x => Math.Abs((int)x));
                Ok(Encoding.ASCII.GetString(w, 0, 4) == "RIFF" && Encoding.ASCII.GetString(w, 8, 4) == "WAVE" && BitConverter.ToInt16(w, 20) == 1 && ch == 1 && bits == 16 && w.Length == 44 + s.Length * 2, "sounds: " + name + ".wav is a plain 16-bit mono PCM WAV (" + rate + " Hz, " + secs.ToString("0.00") + " s)");
                Ok(peak > 2000 && peak <= 32000, "sounds: " + name + " is audible and never clips (peak " + peak + ")");
                if (name == "ambience")
                {
                    Ok(secs >= 10 && secs <= 14, "sounds: the ambience loop is about 12 s");
                    int jump = Math.Abs(s[s.Length - 1] - s[0]); int step = Enumerable.Range(1, 2000).Max(i => Math.Abs(s[i] - s[i - 1]));
                    Ok(jump <= step * 2, "sounds: the ambience loops without a click (end to start " + jump + ", a normal step up to " + step + ")");
                }
                else Ok(secs < 0.8 && Math.Abs((int)s[s.Length - 1]) < 400, "sounds: " + name + " is short and ends quietly (subtle)");
            }
            Ok(!Vibe.ShouldPlay("open", false, 50, false, false), "sounds: off = nothing (the default)");
            Ok(Vibe.ShouldPlay("open", true, 50, false, false) && !Vibe.ShouldPlay("open", true, 0, false, false), "sounds: on with a volume; volume 0 = silent");
            Ok(!Vibe.ShouldPlay("open", true, 50, true, false) && !Vibe.ShouldPlay("download", true, 50, true, false) && Vibe.ShouldPlay("game", true, 50, true, false), "sounds: during a game only the game-mode cue");
            Ok(!Vibe.ShouldPlay("nope", true, 50, false, false), "sounds: only the known ones");
            var src = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "tools", "gen-sounds.ps1"));
            Ok(src.Contains("Math.Sin") && !src.Contains("http") && !src.Contains("Invoke-WebRequest"), "sounds: synthesised from sine tones at build time, nothing downloaded");
        }

        static byte[] TestPng(int w, int h, Color c)
        {
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null); var px = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++) { px[i * 4] = c.B; px[i * 4 + 1] = c.G; px[i * 4 + 2] = c.R; px[i * 4 + 3] = 255; }
            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), px, w * 4, 0);
            var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(bmp)); var ms = new MemoryStream(); e.Save(ms); return ms.ToArray();
        }
        static void AppTests()
        {
            var man = "{\"name\":\"Hallow Raid Planner\",\"short_name\":\"Raids\",\"start_url\":\"../app/?source=pwa\",\"scope\":\"/app/\",\"icons\":[{\"src\":\"i/48.png\",\"sizes\":\"48x48\",\"type\":\"image/png\"},{\"src\":\"i/512.png\",\"sizes\":\"512x512\",\"type\":\"image/png\"},{\"src\":\"i/any.svg\",\"sizes\":\"any\",\"type\":\"image/svg+xml\"},{\"src\":\"i/mono.png\",\"sizes\":\"1024x1024\",\"purpose\":\"monochrome\"}]}";
            var m = SiteApps.ParseManifest(man, "https://raid.example/static/manifest.json", "https://raid.example/app/home");
            Ok(m != null && m.Name == "Hallow Raid Planner" && m.Start == "https://raid.example/app/?source=pwa" && m.Scope == "https://raid.example/app/", "apps: the manifest's name, start_url and scope (relative to the manifest)");
            Eq(m.IconUrl, "https://raid.example/static/i/512.png", "apps: the biggest PNG icon (not SVG, not monochrome)");
            var m2 = SiteApps.ParseManifest("{\"short_name\":\"Short\",\"start_url\":\"https://evil.example/\"}", "https://a.example/m.json", "https://a.example/");
            Ok(m2.Name == "Short" && m2.Start == null && m2.IconUrl == null, "apps: short_name when there's no name; a start_url on another site is ignored");
            Ok(SiteApps.ParseManifest("not json", "https://a.example/m.json", "https://a.example/") == null, "apps: a broken manifest = none");
            Eq(SiteApps.ScopeOf("https://raid.example/app/home", null), "https://raid.example/", "apps: without a manifest the scope is the site");
            var a = new InstalledApp { Url = "https://raid.example/app/", Scope = "https://raid.example/app/" };
            Ok(SiteApps.InScope(a, "https://raid.example/app/x?y") && !SiteApps.InScope(a, "https://raid.example/blog/") && !SiteApps.InScope(a, "https://other.example/app/") && !SiteApps.InScope(a, "http://raid.example/app/"), "apps: in its window only inside its scope");
            Eq(SiteApps.SafeName("Raid: Planner / \"v2\"?"), "Raid  Planner    v2", "apps: a name that can be a file name");
            var png = TestPng(16, 16, Color.FromRgb(0x8B, 0x5C, 0xF6));
            var sq = SiteApps.SquarePng(TestPng(40, 20, Colors.Orange), 256);
            using (var ms = new MemoryStream(sq)) { var d = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); Ok(d.Frames[0].PixelWidth == 40 && d.Frames[0].PixelHeight == 40, "apps: icons are made square (not stretched)"); }
            var ico = SiteApps.Ico(png);
            Ok(ico != null && BitConverter.ToInt16(ico, 0) == 0 && BitConverter.ToInt16(ico, 2) == 1 && BitConverter.ToInt16(ico, 4) == 4, "apps: an .ico with 4 sizes");
            var dec = new IconBitmapDecoder(new MemoryStream(ico), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Ok(dec.Frames.Count == 4 && dec.Frames.Max(f => f.PixelWidth) == 256 && dec.Frames.Min(f => f.PixelWidth) == 16, "apps: Windows reads it (16 to 256 px)");
            Ok(SiteApps.SquarePng(Encoding.UTF8.GetBytes("<svg/>"), 64) == null, "apps: a picture WPF can't read = no icon (the favicon is used instead)");
            Ok(SiteApps.StartMenuDir.StartsWith(root), "apps: in tests the Start menu folder is in the test folder");
            Directory.CreateDirectory(SiteApps.StartMenuDir); var lnk = Path.Combine(SiteApps.StartMenuDir, "Test app.lnk");
            var icoFile = Path.Combine(root, "t.ico"); File.WriteAllBytes(icoFile, ico);
            Ok(SiteApps.CreateShortcut(lnk, @"C:\Windows\notepad.exe", SiteApps.ShortcutArgs("https://raid.example/app/", "gaming"), icoFile, "test"), "apps: a Start menu shortcut is made");
            Ok(SiteApps.ReadShortcut(lnk, "Arguments") == "--app \"https://raid.example/app/\" --profile gaming" && (SiteApps.ReadShortcut(lnk, "IconLocation") ?? "").StartsWith(icoFile), "apps: the shortcut opens the app in its profile, with its icon");
            File.Delete(lnk);
        }

        static void FamilyTests()
        {
            var hub = "{\"Games\":{\"steam:2507950\":{\"Name\":\"Delta Force\",\"Minutes\":900,\"LastPlayed\":\"2026-11-20T21:00:00\"},\"steam:1422450\":{\"Name\":\"Deadlock\",\"Minutes\":50,\"LastPlayed\":\"2026-11-18T21:00:00\"},\"steam:111\":{\"Name\":\"Hidden one\",\"Hidden\":true},\"epic:Fortnite\":{\"Name\":\"Fortnite\"}}}";
            var hubSt = "{\"playing\":[{\"id\":\"steam:1422450\",\"name\":\"Deadlock\"}],\"recent\":[{\"id\":\"steam:2073850\",\"name\":\"THE FINALS\"}]}";
            var hd = Path.Combine(root, "HallowHub"); Directory.CreateDirectory(Path.Combine(hd, "meta", "2507950")); File.WriteAllBytes(Path.Combine(hd, "meta", "2507950", "shot0.jpg"), TestPng(8, 8, Colors.Red));
            var g = NewTabFamily.Games(hub, hubSt, hd);
            Ok(g.Count == 3 && g[0].Id == "2507950" && g[0].Name == "Delta Force" && g[0].PicSrc != null && g.Any(x => x.Id == "1422450" && x.Playing) && g.Any(x => x.Name == "THE FINALS"), "family: your Steam games from Hub's library (newest first, hidden ones left out, playing marked, Hub's picture)");
            var clips = "{\"clips\":[{\"file\":\"C:\\\\Clips\\\\a.mp4\",\"game\":\"Deadlock\",\"time\":\"2026-11-20T20:00:00\",\"durationSec\":31.9,\"thumb\":null},{\"file\":\"C:\\\\Clips\\\\notes.txt\"},{\"file\":\"C:\\\\Clips\\\\b.mkv\",\"game\":\"Delta Force\"}," + string.Join(",", Enumerable.Range(0, 8).Select(i => "{\"file\":\"C:\\\\Clips\\\\x" + i + ".mp4\"}")) + "]}";
            var c = NewTabFamily.Clips(clips);
            Ok(c.Count == NewTabFamily.MaxClips && c[0].Game == "Deadlock" && Math.Abs(c[0].Secs - 31.9) < 0.01 && c[1].Name == "b" && c.All(x => x.File.EndsWith(".mp4") || x.File.EndsWith(".mkv")), "family: recent clips from Clips' status.json (videos only, at most 6)");
            Ok(NewTabFamily.Clips("{\"clips\":[]}").Count == 0 && NewTabFamily.Clips("broken").Count == 0 && NewTabFamily.Games(null, null, null).Count == 0, "family: nothing (or a broken file) = an empty row, never an error");
            Ok(NewTabFamily.OverlayVisible("{\"visible\":true}") == true && NewTabFamily.OverlayVisible("{\"visible\":false}") == false && NewTabFamily.OverlayVisible("{}") == null, "family: Overlay's on / off from its status.json");
            Eq(NewTabFamily.Ago("2026-11-20T20:00:00", new DateTime(2026, 11, 20, 22, 30, 0)), "2 h ago", "family: when a clip was saved");
            // Build: only what's installed (stand-ins: status.json in the test folder)
            foreach (var app in new[] { "Hub", "Clips", "Overlay" }) try { File.Delete(Path.Combine(root, "Hallow" + app, "status.json")); } catch { }
            var s = new BrowserSettings(); s.Fix();
            var none = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(NewTabFamily.Build(s, DateTime.Now));
            Ok(none["games"] == null && none["clips"] == null && none["overlay"] == null, "family: no Hub, Clips or Overlay installed = no rows");
            File.WriteAllText(Path.Combine(hd, "hub.json"), hub); File.WriteAllText(Path.Combine(hd, "status.json"), hubSt);
            Directory.CreateDirectory(Path.Combine(root, "HallowOverlay")); File.WriteAllText(Path.Combine(root, "HallowOverlay", "status.json"), "{\"visible\":true}");
            var some = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(NewTabFamily.Build(s, DateTime.Now));
            Ok(some["games"] is System.Collections.ArrayList && ((System.Collections.ArrayList)some["games"]).Count == 3 && some["clips"] == null && some["overlay"] is Dictionary<string, object>, "family: Hub and Overlay installed, Clips not = games and the Overlay switch, no clips row");
            Ok(File.Exists(Path.Combine(Engine.NewTabDir, "fam", "g-2507950.jpg")), "family: Hub's picture is copied next to the page (the page shows only its own folder)");
            s.NewTabHallow = false; Ok(NewTabFamily.Build(s, DateTime.Now) == "{\"on\":false}", "family: switched off = nothing read");
            Ok(NewTabFamily.Play("999") == "not in the list" && NewTabFamily.OpenClip("nope") == "not in the list", "family: Play / open only for what the page was given");
            foreach (var app in new[] { "Hub", "Overlay" }) try { File.Delete(Path.Combine(root, "Hallow" + app, "status.json")); } catch { }
            var html = Engine.NewTabHtml("duckduckgo", "#F07820");
            Ok(html.Contains("id=hf") && html.Contains("first-contentful-paint") && html.IndexOf("hallowFamily") > html.IndexOf("id=dial"), "family: the page draws first and fetches family.json afterwards");
            Ok(!Engine.PrivateTabHtml("duckduckgo").Contains("hallowFamily"), "family: never on a private window's new tab page");
        }

        static void ReadingTests()
        {
            ReadingList.Reset(); PageNotes.Reset();
            try { File.Delete(Path.Combine(BrowserSettings.Dir, "reading-list.json")); File.Delete(Path.Combine(BrowserSettings.Dir, "notes.json")); } catch { }
            Ok(ReadingList.Add("https://a.example/1", "One") && ReadingList.Add("https://b.example/2", " Two ") && !ReadingList.Add("javascript:alert(1)", "x"), "reading list: pages are saved (web addresses only)");
            ReadingList.Add("https://a.example/1", "One again");
            var it = ReadingList.Items; Ok(it.Count == 2 && it[0].Url == "https://a.example/1" && it[0].Title == "One again" && it[1].Title == "Two", "reading list: saving again moves it to the top");
            ReadingList.SetRead("https://b.example/2", true); Ok(ReadingList.Count(true) == 1 && ReadingList.Count(false) == 2 && ReadingList.Items[1].Read && ReadingList.Items[1].ReadAt != null, "reading list: read / unread");
            ReadingList.Reset(); Ok(ReadingList.Count(true) == 1 && File.Exists(Path.Combine(BrowserSettings.Dir, "reading-list.json")), "reading list: kept in the profile's folder");
            ReadingList.Remove("https://b.example/2"); Ok(ReadingList.Count(false) == 1, "reading list: remove");
            Eq(PageNotes.Key("https://Example.com/path/?q=1#x"), "https://example.com/path", "notes: one per page (no query, no #part)");
            Ok(PageNotes.Key("hallow://settings") == null && PageNotes.Key(null) == null, "notes: only for web pages");
            Ok(PageNotes.Set("https://example.com/path?a", "Path", "Bring the keycard\r\nleft door") && PageNotes.Get("https://example.com/path#b").Text == "Bring the keycard\nleft door", "notes: saved, found again from the same page");
            Ok(!PageNotes.Set("https://example.com/path", null, "Bring the keycard\nleft door"), "notes: unchanged text isn't saved again");
            PageNotes.Set("https://other.example/", "Other", new string('x', 5000)); Ok(PageNotes.Get("https://other.example/").Text.Length == PageNotes.MaxText, "notes: short notes (at most 4,000 characters)");
            Ok(PageNotes.Set("https://other.example/", null, "  ") && PageNotes.Get("https://other.example/") == null, "notes: an empty note is removed");
        }

        // ---- notes through the 1.3 folder sync, between two test PCs, with and without the sync password
        static void NotesSyncTests()
        {
            SyncCrypto.Iterations = 2000;
            var folder = Path.Combine(root, "SyncNotes"); Directory.CreateDirectory(folder);
            Func<string, Tuple<BrowserSettings, List<PageNote>, SyncState>> pc = name => { var s = new BrowserSettings(); s.Fix(); return Tuple.Create(s, new List<PageNote>(), new SyncState()); };
            var A = pc("A"); var B = pc("B"); SyncState stA = A.Item3, stB = B.Item3;
            Func<Tuple<BrowserSettings, List<PageNote>, SyncState>, string, string, SyncState, SyncState> sync = (p, dev, pw, st) =>
            {
                var input = new SyncInput { Settings = p.Item1, Notes = p.Item2 };
                var o = SyncCore.Run(SyncData.Gather(input), st, folder, new SyncOptions { Dev = p.Item1.SyncDevice, DevName = dev, Password = pw, CopyData = SyncData.CopyData, KeepDir = root });
                if (o.State2 != null) { if (o.ChangedKinds.Count > 0) SyncData.Apply(input, o.Result, o.ChangedKinds); return o.State2; }
                return st;
            };
            A.Item2.Add(new PageNote { Key = "https://raid.example/plan", Url = "https://raid.example/plan", Title = "Plan", Text = "Bring smokes", Updated = "2026-11-21T10:00:00" });
            stA = sync(A, "PC-A", null, stA); stB = sync(B, "PC-B", null, stB);
            Ok(B.Item2.Count == 1 && B.Item2[0].Text == "Bring smokes" && B.Item2[0].Key == "https://raid.example/plan", "notes sync: a note from PC A arrives on PC B");
            B.Item2[0].Text = "Bring smokes and flashes"; stB = sync(B, "PC-B", null, stB); stA = sync(A, "PC-A", null, stA);
            Ok(A.Item2.Count == 1 && A.Item2[0].Text == "Bring smokes and flashes", "notes sync: an edit on B comes back to A");
            A.Item2.Clear(); stA = sync(A, "PC-A", null, stA); stB = sync(B, "PC-B", null, stB);
            Ok(B.Item2.Count == 0, "notes sync: a note deleted on A goes on B too");
            B.Item2.Add(new PageNote { Key = "https://secret.example/", Url = "https://secret.example/", Title = "S", Text = "the secret plan", Updated = "2026-11-21T11:00:00" });
            stB = sync(B, "PC-B", "correct horse", stB);
            var raw = File.ReadAllBytes(Path.Combine(folder, SyncCore.FileName));
            Ok(SyncCore.IsEncrypted(raw) && !Encoding.UTF8.GetString(raw).Contains("secret plan"), "notes sync: with the sync password the folder's copy is encrypted (the note text isn't readable in it)");
            stA = sync(A, "PC-A", "correct horse", stA);
            Ok(A.Item2.Count == 1 && A.Item2[0].Text == "the secret plan", "notes sync: ... and the other PC reads it with the password");
            Ok(SyncData.Kinds.Contains("note") && !SyncData.SettingKeys.Contains("StartupBoost") && !SyncData.SettingKeys.Contains("SpellWords"), "notes sync: notes are their own kind; the reading list and per-PC choices (startup boost) stay on the PC");
        }

        static string Blend(string fg, double alpha, string bg)
        {
            var a = (Color)ColorConverter.ConvertFromString(fg); var b = (Color)ColorConverter.ConvertFromString(bg);
            Func<byte, byte, byte> mix = (x, y) => (byte)Math.Round(x * alpha + y * (1 - alpha));
            return string.Format("#{0:X2}{1:X2}{2:X2}", mix(a.R, b.R), mix(a.G, b.G), mix(a.B, b.B));
        }
        static void ContrastTests()
        {
            // the profile chip: the name (Text) on a 15% tint of the profile's colour over the title strip, in all three looks
            var p = BrowserWindow.PrivatePalette;
            var looks = new[] { Tuple.Create("dark", "#ECE8F4", "#110E17"), Tuple.Create("light", Look.LText, Look.LPanel2), Tuple.Create("private", p["Text"], p["Strip"]) };
            double worst = 99; string where = "";
            foreach (var l in looks) foreach (var c in ProfileStore.Colors) { var bg = Blend(c, 0x26 / 255.0, l.Item3); var k = Look.Contrast(l.Item2, bg); if (k < worst) { worst = k; where = l.Item1 + " " + c; } }
            Ok(worst >= 4.5, "contrast: the profile chip's name on every profile colour, dark / light / private >= 4.5:1 (worst " + worst.ToString("0.00") + ", " + where + ")");
            // the colour dot and line are drawn shapes (3:1 against the strip)
            double wd = 99; foreach (var l in looks.Take(2)) foreach (var c in ProfileStore.Colors) wd = Math.Min(wd, Look.Contrast(c, l.Item3));
            Ok(wd >= 1.6, "contrast: profile colours stand out from the title strip (shapes, with the name next to them; worst " + wd.ToString("0.00") + ")");
            // the sleeping tab's leaf (GreenText) on the tab strip
            var leaf = new[] { Look.Contrast("#6ED28C", "#110E17"), Look.Contrast(Look.LGreenText, Look.LPanel2), Look.Contrast("#6ED28C", p["Strip"]) };
            Ok(leaf.All(x => x >= 3), "contrast: the leaf on a sleeping tab, dark / light / private >= 3:1 (" + string.Join(", ", leaf.Select(x => x.ToString("0.00"))) + ")");
            // the new tab page's Play button text on the accent (dark: the accent, light: the darkened text accent)
            foreach (var acc in new[] { "#F07820", "#8B5CF6", "#22C55E", "#3B82F6" })
            {
                var ta = Look.TextAccent(acc);
                var fd = Look.FillOf(acc); var fl = Look.FillOf(ta);
                Ok(Look.Contrast(Look.TextOnFill(fd), fd) >= 4.5 && Look.Contrast(Look.TextOnFill(fl), fl) >= 4.5, "contrast: the new tab page's Play button on accent " + acc + " (dark and light) >= 4.5:1");
            }
            Ok(Icons.Has("leaf") && Icons.Has("user-plus") && Icons.Has("note-sticky") && Icons.Has("book-bookmark") && Icons.Has("table-cells-large") && Icons.Has("square-plus"), "1.6 icons are there");
        }
    }
}
