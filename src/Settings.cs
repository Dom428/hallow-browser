// Hallow Browser settings: %APPDATA%\HallowBrowser\settings.json (and session.json: the open tabs, for session restore).
// The browser profile itself (cookies, cache, site data - Microsoft's WebView2 runtime keeps it) is in %LOCALAPPDATA%\HallowBrowser\Profile.
// Nothing here is ever sent anywhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace HallowBrowser
{
    [DataContract]
    public partial class BrowserSettings
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
        // 1.1 tests: HALLOW_BROWSER_LIVE_TWITCH / _YOUTUBE = a local fake page instead of twitch.tv / youtube.com, _LIVE_SECONDS = how often
        public static readonly string LiveTwitchBase = TestDir != null ? Env("HALLOW_BROWSER_LIVE_TWITCH") : null;
        public static readonly string LiveYouTubeBase = TestDir != null ? Env("HALLOW_BROWSER_LIVE_YOUTUBE") : null;
        public static readonly string LiveSeconds = TestDir != null ? Env("HALLOW_BROWSER_LIVE_SECONDS") : null;
        // 1.3 tests: HALLOW_BROWSER_IMPORT_ROOT = a folder with fake Local and Roaming browser profiles (never the real ones)
        public static readonly string ImportRoot = TestDir != null ? Env("HALLOW_BROWSER_IMPORT_ROOT") : null;
        public static bool Frozen;   // --snap: never saved
        static string Env(string n) { var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrWhiteSpace(v) ? null : v; }

        // 1.6: profiles. The "Default" profile keeps the folders it always had (nothing moves); another profile keeps its own files in
        // Profiles<id> of both folders. The page engine's folder (ProfileDir) is shared: each profile is a WebView2 profile in it.
        public static string RootDir { get { return TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowBrowser"); } }
        public static string RootLocalDir { get { return TestDir != null ? Path.Combine(TestDir, "Local") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HallowBrowser"); } }
        public static string Dir { get { return IsDefaultProfile ? RootDir : Path.Combine(RootDir, "Profiles", ProfileId); } }
        public static string LocalDir { get { return IsDefaultProfile ? RootLocalDir : Path.Combine(RootLocalDir, "Profiles", ProfileId); } }
        public static string ProfileDir { get { return Path.Combine(RootLocalDir, "Profile"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }
        static string SessionPath { get { return Path.Combine(Dir, "session.json"); } }

        public static readonly string[] Engines = { "duckduckgo", "google", "bing", "brave" };
        public static readonly int[] SleepChoices = { 0, 5, 15, 30, 60 };
        public static readonly int[] RamChoices = { 0, 2048, 4096, 8192 };
        public static readonly int[] PanelSleepChoices = { 0, 1, 5, 10, 30 };
        public static readonly int[] ReaderSizes = { 14, 16, 18, 20, 22, 24, 28 };
        public static readonly int[] BandwidthChoices = { 0, 256, 1024, 4096, 10240 };   // KB/s per tab, 0 = off
        public static readonly string[] DnsChoices = { "off", "cloudflare", "quad9", "google" };
        // 1.4 tests: HALLOW_BROWSER_DOH_TEMPLATE = a local DoH stand-in, HALLOW_BROWSER_IGNORE_CERTS=1 = the stand-ins' self-signed
        // certificates are accepted, HALLOW_BROWSER_HTTPS_PORTS = "httpPort=httpsPort,..." for the local http/https pair,
        // HALLOW_BROWSER_STEAM_NEWS / _EPIC_FREE = local stand-ins for Steam's news API and Epic's free games list
        public static readonly string DohTemplateTest = TestDir != null ? Env("HALLOW_BROWSER_DOH_TEMPLATE") : null;
        public static readonly bool IgnoreCerts = TestDir != null && Env("HALLOW_BROWSER_IGNORE_CERTS") == "1";
        public static readonly string HttpsPortsTest = TestDir != null ? Env("HALLOW_BROWSER_HTTPS_PORTS") : null;
        public static readonly string SteamNewsTest = TestDir != null ? Env("HALLOW_BROWSER_STEAM_NEWS") : null;
        public static readonly string EpicFreeTest = TestDir != null ? Env("HALLOW_BROWSER_EPIC_FREE") : null;
        // 1.5 tests: HALLOW_BROWSER_SUGGEST_URL = a local stand-in for the search engine's suggestions (never a real engine),
        // HALLOW_BROWSER_MPCMDRUN = a stand-in for Defender's MpCmdRun.exe (never a real scan)
        public static readonly string SuggestTest = TestDir != null ? Env("HALLOW_BROWSER_SUGGEST_URL") : null;
        public static readonly string MpCmdRunTest = TestDir != null ? Env("HALLOW_BROWSER_MPCMDRUN") : null;
        public static readonly int[] DlLimitChoices = { 0, 256, 1024, 4096, -1 };   // KB/s for all downloads together while a game runs; -1 = pause them

        [DataMember] public string Search = "duckduckgo";
        [DataMember] public bool RestoreSession = true;
        [DataMember] public bool ClearOnExit;
        [DataMember] public bool BlockThirdPartyCookies = true;
        [DataMember] public bool DoNotTrack = true;            // sends "DNT: 1" and "Sec-GPC: 1" (Global Privacy Control)
        [DataMember] public bool StrictTracking;               // Microsoft's tracking prevention: Balanced (default) or Strict
        [DataMember] public string PageColors = "follow";      // follow (the browser's light/dark look) | dark | light | auto (Windows' app mode)
        [DataMember] public string Look = "follow";            // 1.1: follow (Hallow's mode in theme.json) | light | dark
        [DataMember] public int SettingsVersion;               // 2 = 1.1
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
        // 1.1: the gamer sidebar, live notifications, the bandwidth limiter
        [DataMember] public bool SidebarOn = true;             // the sidebar's icon rail (Ctrl+Shift+S)
        [DataMember] public List<SidebarSite> SidebarSites;    // null = the defaults (Discord, Twitch, YouTube, Spotify, WhatsApp)
        [DataMember] public string SidebarOpenId;              // the panel that was open at the last close
        [DataMember] public int SidebarSleepMinutes = 10;      // hidden panels sleep after this (0 = never; in game mode at once)
        [DataMember] public bool LiveOn;                       // Twitch / YouTube live notifications (off by default)
        [DataMember] public List<string> LiveChannels;         // "twitch:<name>" / "youtube:@<handle>"
        [DataMember] public int BandwidthKBps;                 // per-tab download cap (DevTools network emulation), 0 = off
        [DataMember] public bool BandwidthGameOnly = true;     // only while a game runs
        // 1.2: power features
        [DataMember] public bool VerticalTabs;                 // tabs in a column on the left instead of the title bar
        [DataMember] public bool VTabsCollapsed;               // that column shows icons only
        [DataMember] public string SidebarSide = "auto";       // auto (right with vertical tabs, else left) | left | right
        [DataMember] public int ReaderFontSize = 18;           // reader mode text size (px)
        [DataMember] public string ReaderWidth = "medium";     // narrow | medium | wide
        [DataMember] public bool KeepHistory = true;           // the local history store (Ctrl+K searches it)
        // 1.3: the bookmarks bar, the new tab page, sync through a folder
        [DataMember] public bool BookmarksBar;                 // the bookmarks bar under the toolbar (Ctrl+Shift+B)
        [DataMember] public bool NewTabTopSites = true;        // speed dial: your most visited pages (from the local history) and pinned ones
        [DataMember] public bool NewTabClock = true;
        [DataMember] public bool NewTabFamily = true;          // the Hallow strip: Guard score, Shield blocked today, now playing
        [DataMember] public List<PinnedSite> NewTabPinned;     // pinned speed-dial tiles
        [DataMember] public List<string> NewTabHidden;         // top sites you took off the new tab page
        [DataMember] public bool SyncOn;                       // off by default
        [DataMember] public string SyncFolder;                 // the folder you picked (OneDrive, Dropbox, Syncthing, a NAS share...)
        [DataMember] public bool SyncEncrypt;                  // the password itself is kept apart (DPAPI, sync-password.bin)
        [DataMember] public string SyncDevice;                 // this PC's random id in version vectors
        [DataMember] public string SyncNote;                   // the last round's result, for Settings
        // 1.4: HTTPS-only, secure DNS, site settings, your own extensions, Shield's family relay, the game corner
        [DataMember] public bool HttpsOnly = true;             // http addresses are tried as https first; a page that can't do https asks first
        [DataMember] public string SecureDns = "off";          // off | cloudflare | quad9 | google (DNS over HTTPS; applies after a restart)
        [DataMember] public List<SiteRule> Sites;              // per-site choices the browser keeps itself (pop-ups, sound, JavaScript, http allowed)
        [DataMember] public List<UserExtension> Extensions;    // unpacked extensions you loaded (copies in LocalAppData\HallowBrowser\Extensions)
        [DataMember] public bool ShareShieldCounts = true;     // Shield's blocked counts to %APPDATA%\HallowShield\status.json for Hallow Hub / Remote (counts only)
        [DataMember] public bool GameCorner;                   // off by default: news for your Hub games (Steam) and free games (Epic) on the new tab page
        // 1.5: address bar, saving logins to Hallow Keys, dark pages, downloads, tabs, media
        [DataMember] public List<SearchKeyword> Keywords;      // "yt cats" -> YouTube; null = the defaults (yt, w, gh)
        [DataMember] public bool SearchSuggest;                // OFF by default: what you type goes to the search engine for suggestions
        [DataMember] public bool SaveLogins = true;            // offer "Save to Hallow Keys?" after you sign in (Keys stores it; the browser never does)
        [DataMember] public List<string> NeverSave;            // sites you said "Never" for
        [DataMember] public bool ForceDark;                    // make every page dark (Chromium's own auto dark mode); per site in Sites
        [DataMember] public bool DownloadScan = true;          // scan finished downloads with Microsoft Defender (low priority)
        [DataMember] public int DownloadGameKBps;              // 0 = no limit while gaming; -1 = pause downloads while gaming
        [DataMember] public bool TabPreviews = true;           // a small picture of the tab when you hover it
        [DataMember] public bool Gestures;                     // mouse gestures (right-drag): off by default
        [DataMember] public bool AutoPip;                      // pop out a playing video when you switch away from its tab: off by default

        [OnDeserializing] void Defaults(StreamingContext c)
        {
            Search = "duckduckgo"; RestoreSession = true; BlockThirdPartyCookies = true; DoNotTrack = true; PageColors = "dark"; UseHallowAccent = true;
            GameLight = true; SleepMinutes = 15; Preconnect = true; KeysOn = true; ShieldAutoUpdate = true; Left = Top = double.NaN; Width = 1280; Height = 820;
            Look = "follow"; SidebarOn = true; SidebarSleepMinutes = 10; BandwidthGameOnly = true;
            SidebarSide = "auto"; ReaderFontSize = 18; ReaderWidth = "medium"; KeepHistory = true; NewTabTopSites = true; NewTabClock = true; NewTabFamily = true;   // PageColors stays "dark" here: Fix() moves 1.0's default to "follow"
            HttpsOnly = true; SecureDns = "off"; ShareShieldCounts = true;
            SaveLogins = true; DownloadScan = true; TabPreviews = true;
            Defaults16();
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
            // 1.0 saved "dark" as its default: 1.1 makes that "follow" (the same look until Hallow's mode is light)
            if (SettingsVersion < 2) { if (PageColors == "dark" || PageColors == null) PageColors = "follow"; SettingsVersion = 2; }
            if (PageColors != "light" && PageColors != "auto" && PageColors != "dark") PageColors = "follow";
            Look = HallowBrowser.Look.Normalize(Look);
            if (SidebarSites == null) SidebarSites = SidebarRules.Defaults();
            SidebarSites.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Url) || !Omni.IsWeb(x.Url));
            foreach (var x in SidebarSites) { if (string.IsNullOrWhiteSpace(x.Name)) x.Name = Omni.HostOf(x.Url) ?? "Panel"; if (string.IsNullOrEmpty(x.Id)) x.Id = SidebarRules.NewId(x.Name, SidebarSites); x.Width = SidebarRules.ClampWidth(x.Width); }
            if (Array.IndexOf(PanelSleepChoices, SidebarSleepMinutes) < 0) SidebarSleepMinutes = 10;
            if (LiveChannels == null) LiveChannels = new List<string>();
            LiveChannels = LiveChannels.Select(LiveCheck.Normalize).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
            if (Array.IndexOf(BandwidthChoices, BandwidthKBps) < 0) BandwidthKBps = 0;
            if (Array.IndexOf(SleepChoices, SleepMinutes) < 0) SleepMinutes = 15;
            if (Array.IndexOf(RamChoices, RamLimitMb) < 0) RamLimitMb = 0;
            if (SidebarSide != "left" && SidebarSide != "right") SidebarSide = "auto";
            if (Array.IndexOf(ReaderSizes, ReaderFontSize) < 0) ReaderFontSize = 18;
            if (ReaderWidth != "narrow" && ReaderWidth != "wide") ReaderWidth = "medium";
            if (NewTabPinned == null) NewTabPinned = new List<PinnedSite>();
            NewTabPinned.RemoveAll(x => x == null || !Omni.IsWeb(x.Url));
            if (NewTabPinned.Count > 24) NewTabPinned.RemoveRange(24, NewTabPinned.Count - 24);
            if (NewTabHidden == null) NewTabHidden = new List<string>();
            if (NewTabHidden.Count > 200) NewTabHidden.RemoveRange(0, NewTabHidden.Count - 200);
            if (string.IsNullOrEmpty(SyncDevice)) SyncDevice = Guid.NewGuid().ToString("N").Substring(0, 12);
            if (Array.IndexOf(DnsChoices, SecureDns) < 0) SecureDns = "off";
            if (Sites == null) Sites = new List<SiteRule>();
            Sites.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Host));
            if (Extensions == null) Extensions = new List<UserExtension>();
            Extensions.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Folder));
            if (Keywords == null) Keywords = HallowBrowser.Keywords.Defaults();
            Keywords.RemoveAll(x => x == null || HallowBrowser.Keywords.Clean(x.Key) == null || !HallowBrowser.Keywords.ValidUrl(x.Url));
            foreach (var k in Keywords) k.Key = HallowBrowser.Keywords.Clean(k.Key);
            Keywords = Keywords.GroupBy(k => k.Key).Select(g => g.First()).Take(50).ToList();
            if (NeverSave == null) NeverSave = new List<string>();
            NeverSave = NeverSave.Where(h => !string.IsNullOrWhiteSpace(h)).Select(SiteRules.Key).Distinct().Take(500).ToList();
            if (Array.IndexOf(DlLimitChoices, DownloadGameKBps) < 0) DownloadGameKBps = 0;
            if (Typed == null) Typed = new List<string>();
            Typed.RemoveAll(string.IsNullOrWhiteSpace);
            if (Typed.Count > 200) Typed.RemoveRange(200, Typed.Count - 200);
            Fix16();
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
        // 1.2: workspaces (each with its own session) and named saved sessions, next to session.json
        static string WorkspacesPath { get { return Path.Combine(Dir, "workspaces.json"); } }
        static string SavedPath { get { return Path.Combine(Dir, "saved-sessions.json"); } }
        public static WorkspaceStore LoadWorkspaces() { return Read<WorkspaceStore>(WorkspacesPath) ?? new WorkspaceStore(); }
        public static void SaveWorkspaces(WorkspaceStore s) { Write(WorkspacesPath, s, typeof(WorkspaceStore)); }
        public static List<SavedSession> LoadSaved() { var l = Read<SavedList>(SavedPath); return l == null || l.Items == null ? new List<SavedSession>() : l.Items.Where(x => x != null && x.Session != null).ToList(); }
        public static void SaveSaved(List<SavedSession> items) { Write(SavedPath, new SavedList { Items = items }, typeof(SavedList)); }
        public static T Read<T>(string path) where T : class
        {
            try { if (File.Exists(path)) using (var f = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f); } catch { }
            return null;
        }
        public static void WriteFile(string path, object o, Type t) { Write(path, o, t); }
    }

    [DataContract] public class PinnedSite { [DataMember] public string Url; [DataMember] public string Title; }
    [DataContract] public class SessionTab { [DataMember] public string Url; [DataMember] public string Title; [DataMember] public string Group; }
    [DataContract] public class SessionGroup { [DataMember] public string Id; [DataMember] public string Name; [DataMember] public string Color; [DataMember] public bool Collapsed; }
    [DataContract]
    public class Session
    {
        [DataMember] public List<SessionTab> Tabs = new List<SessionTab>(); [DataMember] public int Active;
        [DataMember] public List<SessionGroup> Groups;   // 1.2: tab groups (tabs name theirs by Id)
        [DataMember] public string Split;                // 1.2: "left,right" tab indexes of the split view, or null
        [DataMember] public List<WindowSession> Windows; // 1.4: the other windows (this session's own tabs are the first window's)
    }
    // 1.2: workspaces - separate sets of tabs and groups on the same profile (same sign-ins)
    [DataContract] public class Workspace { [DataMember] public string Id; [DataMember] public string Name; [DataMember] public Session Session; [DataMember] public bool Auto; }   // 1.4: Auto = a window's own "Window n"
    [DataContract] public class WorkspaceStore { [DataMember] public string Current; [DataMember] public List<Workspace> Items = new List<Workspace>(); }
    [DataContract] public class SavedSession { [DataMember] public string Name; [DataMember] public string Saved; [DataMember] public Session Session; }
    [DataContract] public class SavedList { [DataMember] public List<SavedSession> Items = new List<SavedSession>(); }
}
