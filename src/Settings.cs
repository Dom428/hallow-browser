// Hallow Browser settings: %APPDATA%\HallowBrowser\settings.json (and session.json: the open tabs, for session restore).
// The browser profile itself (cookies, cache, site data - Microsoft's WebView2 runtime keeps it) is in %LOCALAPPDATA%\HallowBrowser\Profile.
// Nothing here is ever sent anywhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace HallowBrowser
{
    [DataContract]
    public class BrowserSettings
    {
        // developer tests: HALLOW_BROWSER_TESTDIR = a folder for everything (settings, profile, Shield updates) and separate pipes;
        // HALLOW_BROWSER_OFFSCREEN=1 = the window lives off-screen and never takes focus; HALLOW_BROWSER_HOSTRULES = host mapping for
        // local test pages; HALLOW_BROWSER_FAKEGAME = a file whose text is the "running game"; HALLOW_BROWSER_FORCE_FALLBACK=1 = Shield's
        // network-only fallback instead of the extension. None of these do anything outside a test folder.
        public static readonly string TestDir = Env("HALLOW_BROWSER_TESTDIR");
        public static readonly bool Offscreen = TestDir != null && Env("HALLOW_BROWSER_OFFSCREEN") == "1";
        public static readonly string HostRules = TestDir != null ? Env("HALLOW_BROWSER_HOSTRULES") : null;
        public static readonly string FakeGameFile = TestDir != null ? Env("HALLOW_BROWSER_FAKEGAME") : null;
        public static readonly bool ForceFallback = TestDir != null && Env("HALLOW_BROWSER_FORCE_FALLBACK") == "1";
        public static bool Frozen;   // --snap: never saved
        static string Env(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrWhiteSpace(v) ? null : v; }

        public static string Dir { get { return TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowBrowser"); } }
        public static string LocalDir { get { return TestDir != null ? Path.Combine(TestDir, "Local") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HallowBrowser"); } }
        public static string ProfileDir { get { return Path.Combine(LocalDir, "Profile"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }
        static string SessionPath { get { return Path.Combine(Dir, "session.json"); } }

        public static readonly string[] Engines = { "duckduckgo", "google", "bing", "brave" };
        public static readonly int[] SleepChoices = { 0, 5, 15, 30, 60 };
        public static readonly int[] RamChoices = { 0, 2048, 4096, 8192 };

        [DataMember] public string Search = "duckduckgo";
        [DataMember] public bool RestoreSession = true;
        [DataMember] public bool ClearOnExit;
        [DataMember] public bool BlockThirdPartyCookies = true;
        [DataMember] public bool DoNotTrack = true;            // sends "DNT: 1" and "Sec-GPC: 1" (Global Privacy Control)
        [DataMember] public bool StrictTracking;               // Microsoft's tracking prevention: Balanced (default) or Strict
        [DataMember] public string PageColors = "dark";        // dark | light | auto (Windows' app mode)
        [DataMember] public bool UseHallowAccent = true;
        [DataMember] public bool GameLight = true;             // gaming-light mode
        [DataMember] public int SleepMinutes = 15;             // background tabs sleep after this (0 = never)
        [DataMember] public int RamLimitMb;                    // 0 = off
        [DataMember] public bool CpuLimiter;                   // the browser always runs below normal priority
        [DataMember] public bool Preconnect = true;            // warm up the connection to the top address-bar suggestion
        [DataMember] public bool KeysOn = true;                // Hallow Keys autofill (only ever after a click)
        [DataMember] public bool ShieldAutoUpdate = true;      // new Shield versions from Shield's GitHub release (sha256-checked)
        [DataMember] public string ShieldLoaded;               // "<version>|<folder>" of the Shield the profile has loaded
        [DataMember] public string LastUpdateCheck;
        [DataMember] public string LastShieldCheck;
        [DataMember] public string LastCrashShown;
        [DataMember] public List<string> Typed;                // addresses you typed or visited from the bar (for suggestions; cleared with browsing data)
        [DataMember] public double Left = double.NaN, Top = double.NaN, Width = 1280, Height = 820;
        [DataMember] public bool Maximized;

        [OnDeserializing] void Defaults(StreamingContext c)
        {
            Search = "duckduckgo"; RestoreSession = true; BlockThirdPartyCookies = true; DoNotTrack = true; PageColors = "dark"; UseHallowAccent = true;
            GameLight = true; SleepMinutes = 15; Preconnect = true; KeysOn = true; ShieldAutoUpdate = true; Left = Top = double.NaN; Width = 1280; Height = 820;
        }

        public static BrowserSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var f = File.OpenRead(FilePath))
                    {
                        var s = (BrowserSettings)new DataContractJsonSerializer(typeof(BrowserSettings)).ReadObject(f);
                        if (s != null) { s.Fix(); return s; }
                    }
            }
            catch { }
            var d = new BrowserSettings(); d.Fix(); return d;
        }
        public void Fix()
        {
            if (Array.IndexOf(Engines, Search) < 0) Search = "duckduckgo";
            if (PageColors != "light" && PageColors != "auto") PageColors = "dark";
            if (Array.IndexOf(SleepChoices, SleepMinutes) < 0) SleepMinutes = 15;
            if (Array.IndexOf(RamChoices, RamLimitMb) < 0) RamLimitMb = 0;
            if (Typed == null) Typed = new List<string>();
            Typed.RemoveAll(string.IsNullOrWhiteSpace);
            if (Typed.Count > 200) Typed.RemoveRange(200, Typed.Count - 200);
            if (double.IsNaN(Width) || Width < 480) Width = 1280;
            if (double.IsNaN(Height) || Height < 360) Height = 820;
        }
        // newest first, no duplicates, at most 200
        public void Remember(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Omni.IsWeb(url)) return;
            lock (Typed) { Typed.RemoveAll(u => string.Equals(u, url, StringComparison.OrdinalIgnoreCase)); Typed.Insert(0, url); if (Typed.Count > 200) Typed.RemoveRange(200, Typed.Count - 200); }
        }

        static readonly object saveLock = new object();
        public void Save() { Write(FilePath, this, typeof(BrowserSettings)); }
        static void Write(string path, object o, Type t)
        {
            if (Frozen) return;
            lock (saveLock)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    var tmp = path + ".tmp";
                    using (var f = File.Create(tmp)) new DataContractJsonSerializer(t).WriteObject(f, o);
                    for (int i = 0; ; i++) { try { if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path); break; } catch { if (i >= 5) throw; Thread.Sleep(50); } }
                }
                catch { }
            }
        }

        // ---- session restore
        public static Session LoadSession()
        {
            try { if (File.Exists(SessionPath)) using (var f = File.OpenRead(SessionPath)) return (Session)new DataContractJsonSerializer(typeof(Session)).ReadObject(f); } catch { }
            return null;
        }
        public static void SaveSession(Session s) { Write(SessionPath, s, typeof(Session)); }
        public static void DeleteSession() { try { File.Delete(SessionPath); } catch { } }
    }

    [DataContract] public class SessionTab { [DataMember] public string Url; [DataMember] public string Title; }
    [DataContract] public class Session { [DataMember] public List<SessionTab> Tabs = new List<SessionTab>(); [DataMember] public int Active; }
}
