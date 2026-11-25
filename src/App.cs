// Hallow Browser - a fast Chromium browser for gamers in the Hallow style, on Microsoft Edge WebView2 (the Evergreen runtime
// that comes with Windows 11). Hallow Shield is built in, Hallow Keys fills logins when you click, and gaming-light mode puts
// background tabs to sleep while you play. It never makes itself the default browser (1.12: you can choose it in Windows' Default
// apps - Settings > General > Default browser takes you there) and has no server of its own.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

[assembly: AssemblyTitle("Hallow Browser")]
[assembly: AssemblyProduct("Hallow Browser")]
[assembly: AssemblyDescription("A fast Chromium browser for gamers, with Hallow Shield built in")]
[assembly: AssemblyVersion("1.19.0.0")]
[assembly: AssemblyFileVersion("1.19.0.0")]

namespace HallowBrowser
{
    public static partial class Program
    {
        public const string Version = "1.19";
        public const string SdkVersion = "1.0.4314.35";
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
        public static string AccentHex = Accent.Default;
        static FileSystemWatcher accentWatch;

        [STAThread]
        public static int Main(string[] args)
        {
            Engine.Mark("main");
            // 1.12: "--single-argument" (what the ProgIDs pass, like Chrome): everything after it is ONE address - a crafted link can't
            // add switches (not even --cmd); only what comes before it is read as switches
            string singleArg = null;
            { int sa = Array.IndexOf(args, "--single-argument"); if (sa >= 0) { singleArg = DefaultBrowser.SingleArgument(Environment.CommandLine); args = args.Take(sa).ToArray(); } }
            int ci = Array.IndexOf(args, "--cmd");
            if (ci >= 0) return Family.RunCmd(ci + 1 < args.Length ? args[ci + 1] : "");
            // 1.12: the installer / uninstaller / Windows' InstallInfo commands (registration only - never the default browser itself)
            foreach (var rc in new[] { "--register", "--unregister", "--hide-icons", "--show-icons" }) if (Array.IndexOf(args, rc) >= 0) return DefaultBrowser.RunCli(rc);
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length) { BrowserSettings.Frozen = true; A11y.ForceTextScale = 100; A11y.ForceHighContrast = false; var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa); SnapAll(args[si + 1]); return 0; }

            // 1.6: --profile <id or name> (a profile's own process), --app <url> (an installed site in its own window)
            // 1.7: --jump new-tab | private-window | reading-list (the taskbar / Start jump list)
            string jump = null; { int ji = Array.IndexOf(args, "--jump"); if (ji >= 0) { if (ji + 1 < args.Length) jump = JumpInvoke(args[ji + 1]); var al = args.ToList(); al.RemoveRange(ji, Math.Min(2, args.Length - ji)); args = al.ToArray(); } }
            PendingJump = jump;
            string wantProfile, appArg; List<string> rest; ParseArgs(args, out wantProfile, out appArg, out rest);
            // 1.12: http / https addresses, local files (-> file:///, also a path split at its spaces) and file: addresses; the rest is
            // refused, never searched (1.11 searched a local path like "C:\x.html" - its scheme looked like "c:")
            string cwd = null; try { cwd = Environment.CurrentDirectory; } catch { }
            var urls = DefaultBrowser.UrlsFromArgs(rest, cwd, File.Exists);
            if (singleArg != null) { var one = DefaultBrowser.ArgToUrl(singleArg, cwd, File.Exists); if (one != null) urls.Add(one); }
            var appUrl = appArg == null ? null : Omni.Interpret(appArg, "duckduckgo"); if (appUrl != null && !Omni.IsWeb(appUrl)) appUrl = null;
            Profiles = ProfileStore.EnsureMigrated();   // 1.6: the data from before profiles is the "Default" profile (nothing moves)
            var prof = (wantProfile != null ? Profiles.Find(wantProfile) : null) ?? Profiles.Find(Profiles.StartWith) ?? Profiles.Items[0];
            BrowserSettings.ProfileId = prof.Id;
            var leaf = BrowserSettings.TestDir != null ? "Test" + Path.GetFileName(BrowserSettings.TestDir.TrimEnd('\\')) : "";
            bool created;
            var mutex = new Mutex(true, MutexName(prof.Id, leaf), out created);
            if (!created)
            {
                // already running: hand the addresses to it (or just bring it forward)
                var pipe = PipeApp(prof.Id);
                for (int i = 0; i < 20; i++)
                {
                    bool ok = true;
                    if (jump != null) ok = Family.Send(pipe, "invoke " + jump, 3000) != null;
                    else if (appUrl != null) ok = Family.Send(pipe, "app " + appUrl, 3000) != null;
                    else if (urls.Count == 0) ok = Family.Send(pipe, "open", 3000) != null;
                    else foreach (var u in urls) ok &= Family.Send(pipe, "open " + u, 3000) != null;
                    if (ok) return 0;
                    Thread.Sleep(250);
                }
                return 1;
            }
            Crash.Install();
            var st0 = BrowserSettings.Load(); MarkRunning(st0); Engine.Mark("settings");   // 1.5: did it close properly last time?
            // 1.9: the engine's preference files are written on another core while the theme and the window are made (they used to be
            // written on the UI thread just before the engine started); the theme reuses these settings instead of reading them again
            if (!Speed19.Off("prefs")) Engine.PrefsTask = Task.Run(() => { Engine.WritePrefs(st0); Engine.Mark("prefs-written"); });
            if (!Speed19.Off("theme")) StartSettings = st0;
            // speed: the JIT compiles start-up code on spare cores, guided by a profile of the last start (no admin, no install step)
            if (BrowserSettings.TestDir == null || Environment.GetEnvironmentVariable("HALLOW_BROWSER_NOMCJ") != "1")   // 1.9 tests: the benchmark measures it off too
            try { Directory.CreateDirectory(BrowserSettings.LocalDir); System.Runtime.ProfileOptimization.SetProfileRoot(BrowserSettings.LocalDir); System.Runtime.ProfileOptimization.StartProfile("startup.jit"); } catch { }
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };   // 1.4: more than one window - the last one closing quits
            A11y.Install();   // 1.9: names, help text and keyboard for every control; high contrast and "Show animations" followed live
            LoadTheme(app); Engine.Mark("theme");
            HookPrivateFlyouts();   // 1.5: pop-ups of private windows in the private palette
            app.DispatcherUnhandledException += (s, e) => { Crash.Write(e.Exception); e.Handled = true; try { if (Win != null) Win.SaveSession(); } catch { } Environment.Exit(1); };

            var runtime = Engine.FindRuntime(); Engine.Mark("runtime-found");
            if (runtime == null) { NoRuntime(app); GC.KeepAlive(mutex); return 2; }
            var st = st0;
            Engine.Starting = Engine.Create(st);   // the page engine starts while the window is being built
            Engine.Mark("engine-started");
            var w = new BrowserWindow(st, false); Register(w); Engine.Mark("window-built");
            var app0 = appUrl == null ? null : SiteApps.ForUrl(appUrl);   // 1.6: an installed site opens as its app window
            if (appUrl != null) w.MakeAppWindow(app0 ?? SiteApps.Adhoc(appUrl));
            Family.Serve(PipeApp(prof.Id), line => Command(Win ?? w, line));
            DefaultBrowser.EnsureAtStart();   // 1.12: registered as a browser you can pick (a few reads on a worker; writes only when needed)
            accentWatch = Accent.Watch(() => app.Dispatcher.BeginInvoke(new Action(ReloadTheme)));
            WatchMode(app);
            w.Loaded += async (s, e) => { if (w.AppMode) await w.StartApp(); else { await w.Start(urls); CrashNotice(w); } };
            app.Run(w);
            MarkClosed();   // 1.5: a normal exit (a crash leaves the marker, and the next start offers your tabs back)
            GC.KeepAlive(mutex);
            return 0;
        }

        // ---- the theme (Theme.xaml with the Hallow accent swapped in)
        // 1.1: light or dark (Look: the browser's own choice, else Hallow's mode in theme.json, "system" = Windows' app mode)
        public static bool? SnapLight;   // --snap renders both
        public static void LoadTheme(Application app)
        {
            var s = Win != null ? Win.S : StartSettings ?? BrowserSettings.Load(); StartSettings = null;
            AccentHex = Accent.Current(s.UseHallowAccent);
            Look.IsLight = SnapLight ?? Look.Resolve(s.Look, Accent.FilePath);
            var x = Res("HallowBrowser.Theme.xaml");
            // 1.9: Windows' high-contrast theme = the system colours (nothing of the Hallow palette), else light or dark as before
            ThemeIsHighContrast = A11y.HighContrast && SnapLight == null;
            if (ThemeIsHighContrast) x = A11y.HighContrastXaml(x); else if (Look.IsLight) x = Look.LightXaml(x, AccentHex);
            var old = app.Resources.MergedDictionaries.Count > 0 ? app.Resources.MergedDictionaries[0] : null;
            var nw = (ResourceDictionary)XamlReader.Parse(Accent.ApplyTo(x, AccentHex));
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(nw);
            ApplyScale(s);   // 1.9: the UI scale (Windows' text size unless set)
            if (old != null) A11y.Restyle(old, nw, app.Windows.Cast<Window>());
        }
        static BrowserSettings StartSettings;
        public static bool ThemeIsHighContrast; static int lastTextScale = -1, lastUiScale = -1;
        public static void ApplyScale(BrowserSettings s)
        {
            int wts = A11y.WindowsTextScale(); lastTextScale = wts; lastUiScale = s.UiScale;
            var sc = A11y.ScaleFor(s.UiScale, wts);
            if (Math.Abs(sc - A11y.Scale) < 0.001 && Application.Current.Resources.Contains(A11y.ScaleKey)) return;
            A11y.SetScale(sc);
            foreach (var x in Windows.ToList()) x.ApplyUiScale();
        }
        public static void ReloadTheme()
        {
            try { LoadTheme(Application.Current); if (Win != null) { Engine.WriteNewTab(Win.S, AccentHex); foreach (var x in Windows.ToList()) { x.ReloadSettings(); x.ThemeChanged(); } } } catch { }
        }
        // every 3 s: did the light/dark answer change? (theme.json's mode, Windows' setting for "system"; also when Hallow's folder
        // didn't exist yet, so the accent watcher isn't running). Reading a tiny file and one registry value - nothing else.
        static void WatchMode(Application app)
        {
            var t = new DispatcherTimer(DispatcherPriority.Background, app.Dispatcher) { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (a, b) =>
            {
                if (Win == null) return;
                bool light; try { light = Look.Resolve(Win.S.Look, Accent.FilePath); } catch { return; }
                if (light != Look.IsLight || A11y.HighContrast != ThemeIsHighContrast) ReloadTheme();
                if (A11y.WindowsTextScale() != lastTextScale || Win.S.UiScale != lastUiScale) ApplyScale(Win.S);   // 1.9: Windows' text size changed (a registry value)
                if (accentWatch == null && Directory.Exists(Accent.Folder)) accentWatch = Accent.Watch(() => app.Dispatcher.BeginInvoke(new Action(ReloadTheme)));
            };
            t.Start();
        }
        static string Res(string name) { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) using (var r = new StreamReader(s)) return r.ReadToEnd(); }

        // ---- status.json / pipe "status": no addresses or titles, ever
        public static string StatusJson(BrowserWindow w)
        {
            var c = Crash.LastTime();
            return Family.Json(new Dictionary<string, object> {
                { "app", "Browser" }, { "version", Version }, { "updated", Family.Time(DateTime.Now) }, { "running", true }, { "profiles", Profiles == null ? 1 : Profiles.Items.Count }, { "boosted", Boosted }, { "appWindows", Windows.Count(x => x.AppMode) }, { "memorySaver", w.S.MemorySaver }, { "onBattery", Power16.OnBattery }, { "memorySavedMb", Windows.Sum(x => x.Tabs.Where(t => t.Suspended || t.Discarded).Sum(t => t.SavedMb)) }, { "readingList", ReadingList.Count(false) }, { "sounds", w.S.Sounds },
                { "tabs", Windows.Sum(x => x.Tabs.Count) }, { "asleep", Windows.Sum(x => x.Tabs.Count(t => t.Suspended || t.Discarded)) }, { "windows", Windows.Count(x => !x.Private) }, { "privateWindows", Windows.Count(x => x.Private) }, { "gameMode", w.Game != null }, { "game", w.Game },
                { "shield", w.ShieldState }, { "twitchAds", w.S.TwitchAds }, { "shieldVersion", w.ShieldVersion }, { "runtime", Engine.RuntimeVersion }, { "look", Look.IsLight ? "light" : "dark" }, { "sidebar", w.S.SidebarOn }, { "panels", w.Side.Panels.Count }, { "panelsAwake", w.Side.Panels.Count(p => p.View != null && !p.Asleep) }, { "liveNotifications", w.S.LiveOn }, { "liveChannels", w.S.LiveChannels.Count }, { "bandwidthCapKBps", w.CapApplied > 0 ? (object)(int)(w.CapApplied / 1024) : null }, { "verticalTabs", w.S.VerticalTabs }, { "workspaces", w.Spaces == null ? 1 : w.Spaces.Items.Count }, { "groups", w.Groups.Count }, { "split", w.SplitOn }, { "bookmarksBar", w.S.BookmarksBar }, { "sync", w.S.SyncOn ? (w.LastSync == null ? "on" : w.LastSync.State) : "off" }, { "httpsOnly", w.S.HttpsOnly }, { "secureDns", w.S.SecureDns }, { "extensions", w.S.Extensions.Count(x => x.Enabled) }, { "gameCorner", w.S.GameCorner }, { "forceDark", w.S.ForceDark }, { "searchSuggestions", w.S.SearchSuggest }, { "mediaTabs", BrowserWindow.MediaTabs().Count }, { "downloadsRunning", Windows.Sum(x => x.Downloads.Count(d => d.State == "running" || d.State == "paused")) }, { "lastCrash", c == null ? null : Family.Time(c.Value) } });
        }

        // ---- the family pipe
        public static string Command(BrowserWindow w, string line)
        {
            var sp = line.IndexOf(' '); var cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant(); var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            // 1.6: startup boost - no window is open: the first "open" (a new start of the browser) makes one at once
            if (Boosted && (cmd == "open" || cmd == "app" || cmd == "invoke"))
            {
                string bn = null;
                if (cmd == "open" && arg.Length > 0 && !DefaultBrowser.Openable(arg, out bn)) return "error only http, https and file addresses can be opened";
                if (cmd == "app" && !Omni.IsWeb(arg)) return "error not a web address";
                return OnUi(w, () => { Wake(cmd == "open" && arg.Length > 0 ? new List<string> { bn } : new List<string>(), cmd == "app" ? arg : null); return "ok"; });
            }
            switch (cmd)
            {
                case "app":   // 1.6: an installed site in its own window (HallowBrowser.exe --app <url> while this profile runs)
                    if (!Omni.IsWeb(arg)) return "error not a web address";
                    return OnUi(w, () => { OpenApp(arg); return "ok"; });
                case "ping": return "ok " + Family.Json(new Dictionary<string, object> { { "app", "Browser" }, { "version", Version } });
                case "status": return "ok " + OnUi(w, () => StatusJson(w));
                case "open":
                    if (arg.Length == 0) return OnUi(w, () => { ShowWindow(w); return "ok"; });
                    string on;   // 1.12: file: too (a local file Windows handed to a second start of the browser)
                    if (!DefaultBrowser.Openable(arg, out on)) return "error only http, https and file addresses can be opened";
                    return OnUi(w, () => { w.NewTab(on, true); ShowWindow(w); return "ok"; });
                case "menu":
                    return "ok " + OnUi(w, () => Family.Json(new Dictionary<string, object> { { "app", "Browser" }, { "items", new object[] {
                        new Dictionary<string, object> { { "id", "new-tab" }, { "label", "New tab" } },
                        new Dictionary<string, object> { { "id", "new-window" }, { "label", "New window" } },
                        new Dictionary<string, object> { { "id", "private-window" }, { "label", "New private window" } },
                        new Dictionary<string, object> { { "id", "reopen" }, { "label", "Reopen closed tab" }, { "enabled", w.ClosedCount > 0 } },
                        new Dictionary<string, object> { { "id", "reopen-window" }, { "label", "Reopen closed window" }, { "enabled", ClosedWindows.Count > 0 } },
                        new Dictionary<string, object> { { "id", "downloads" }, { "label", "Downloads" } },
                        new Dictionary<string, object> { { "id", "-" } },
                        new Dictionary<string, object> { { "id", "game-light" }, { "label", "Gaming-light mode" }, { "checked", w.S.GameLight } },
                        new Dictionary<string, object> { { "id", "cpu-limiter" }, { "label", "CPU limiter" }, { "checked", w.S.CpuLimiter } },
                        new Dictionary<string, object> { { "id", "sidebar" }, { "label", "Sidebar" }, { "checked", w.S.SidebarOn } },
                        new Dictionary<string, object> { { "id", "vertical-tabs" }, { "label", "Vertical tabs" }, { "checked", w.S.VerticalTabs } } }.Concat(ProfileMenuItems()).ToArray() } }));
                case "invoke":
                    return OnUi(w, () =>
                    {
                        switch (arg)
                        {
                            case "new-tab": w.NewTab(Omni.NewTabUrl, true); ShowWindow(w); return "ok";
                            case "new-window": OpenWindow(false, null, null); return "ok";
                            case "private-window": OpenWindow(true, null, null); return "ok";
                            case "reopen": w.ReopenClosed(); ShowWindow(w); return "ok";
                            case "reopen-window": ReopenClosedWindow(); return "ok";
                            case "downloads": w.NewTab(Omni.DownloadsUrl, true); ShowWindow(w); return "ok";
                            case "reading-list": w.Side.ShowNative("reading"); ShowWindow(w); return "ok";   // 1.7: the jump list
                            case "game-light": w.S.GameLight = !w.S.GameLight; w.S.Save(); w.Tick(true); return "ok";
                            case "cpu-limiter": w.S.CpuLimiter = !w.S.CpuLimiter; w.S.Save(); w.Tick(true); return "ok";
                            case "sidebar": w.Side.Toggle(); return "ok";
                            case "vertical-tabs": w.ToggleVerticalTabs(); return "ok";
                        }
                        if (arg.StartsWith("profile-")) return LaunchProfile(arg.Substring(8), null) ? "ok" : "error no such profile";   // 1.6
                        return "error unknown item";
                    });
                case "quit": RealQuit = true; w.Dispatcher.BeginInvoke(new Action(() => { if (Boosted) EndBoost(); else Quit(); })); return "ok";   // 1.6: really exits (no startup boost)
            }
            if (BrowserSettings.TestDir != null && cmd.StartsWith("test-")) return TestHooks.Run(w, cmd, arg);
            return "error unknown command";
        }
        public static string OnUi(BrowserWindow w, Func<string> f)
        {
            string r = null;
            var op = w.Dispatcher.BeginInvoke(new Action(() => { r = f(); }));
            if (op.Wait(TimeSpan.FromSeconds(5)) != DispatcherOperationStatus.Completed) return "error busy";
            return r;
        }
        static void ShowWindow(BrowserWindow w)
        {
            if (BrowserSettings.Offscreen) return;
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Show(); w.Activate();
        }

        // ---- clear browsing data (also on exit when that's on)
        public static async Task ClearAll(BrowserWindow w, CoreWebView2Profile p)
        {
            var kinds = CoreWebView2BrowsingDataKinds.BrowsingHistory | CoreWebView2BrowsingDataKinds.DownloadHistory | CoreWebView2BrowsingDataKinds.Cookies |
                CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.AllDomStorage | CoreWebView2BrowsingDataKinds.GeneralAutofill | CoreWebView2BrowsingDataKinds.PasswordAutosave;
            try { await Task.WhenAny(p.ClearBrowsingDataAsync(kinds), Task.Delay(8000)); } catch { }
            lock (w.S.Typed) w.S.Typed.Clear();
            if (w.History != null) w.History.Clear(); else try { File.Delete(HistoryStore.DefaultPath); } catch { }   // 1.2: the local history store
            w.Downloads.Clear(); DownloadHistory.Clear(); w.S.Save();   // 1.5: the downloads list too
        }

        // ---- the WebView2 runtime is missing: say where to get it (never downloaded by Hallow Browser)
        static void NoRuntime(Application app)
        {
            var win = new Window { Title = "Hallow Browser", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize, Icon = Ui.AppIcon() };
            win.SetResourceReference(Window.BackgroundProperty, "Panel"); win.SetResourceReference(Window.ForegroundProperty, "Text"); Ui.DarkTitle(win);
            var sp = new StackPanel { Margin = new Thickness(24) };
            sp.Children.Add(Ui.T("Hallow Browser needs Microsoft Edge WebView2", 17, true, null, 8));
            sp.Children.Add(Ui.Muted("Hallow Browser shows pages with Microsoft's WebView2 runtime, which comes with Windows 11 - but it isn't on this PC. Hallow Browser never downloads it by itself: get the \"Evergreen Bootstrapper\" or \"Evergreen Standalone Installer\" from Microsoft's page, run it, then start Hallow Browser again.", 12.5, 16));
            sp.Children.Add(Ui.Row(Ui.Btn("Open Microsoft's WebView2 page", () => { try { Process.Start(Engine.RuntimePage); } catch { } }, true, "up-right-from-square"), Ui.Btn("Close", win.Close)));
            win.Content = sp; app.Run(win);
        }

        // ---- "closed unexpectedly last time" (once per crash file)
        static void CrashNotice(BrowserWindow w)
        {
            var last = Crash.Last(); if (last == null || BrowserSettings.Offscreen) return;
            var name = Path.GetFileName(last); if (w.S.LastCrashShown == name) return;
            var t = Crash.LastTime(); if (t == null || DateTime.Now - t.Value > TimeSpan.FromDays(7)) { w.S.LastCrashShown = name; w.S.Save(); return; }
            w.S.LastCrashShown = name; w.S.Save();
            var dlg = new Window { Owner = w, Title = "Hallow Browser", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
            dlg.SetResourceReference(Window.BackgroundProperty, "Panel"); dlg.SetResourceReference(Window.ForegroundProperty, "Text"); Ui.DarkTitle(dlg);
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(Ui.T("Hallow Browser closed unexpectedly last time", 15, true, null, 6));
            sp.Children.Add(Ui.Muted("A crash report was kept on this PC (nothing is sent anywhere). Your tabs were restored.", 12, 14));
            sp.Children.Add(Ui.Row(Ui.Btn("Show details", () => { try { Process.Start("notepad.exe", "\"" + last + "\""); } catch { } }), Ui.Btn("Open folder", () => { try { Process.Start(Crash.Dir); } catch { } }), Ui.Btn("OK", dlg.Close, true)));
            dlg.Content = sp; dlg.ShowDialog();
        }

        // ---- --snap <folder>: the window rendered off-screen with made-up tabs and a made-up page (no WebView, no real pages)
        static void SnapAll(string dir)
        {
            Directory.CreateDirectory(dir);
            Snap.On = true;
            Profiles = null;   // 1.6: made-up profiles only where a picture shows them (never yours)
            ReadingList.UseForSnap(new List<ReadItem>()); PageNotes.UseForSnap(new List<PageNote>()); SiteApps.SnapItems = new List<InstalledApp>();
            SiteIcons.ReadOnly = true; SiteIcons.Fake = SiteIcons.SnapIcons();   // 1.14: made-up site logos (the real cache isn't read)
            var s = new BrowserSettings(); s.Fix();
            s.LiveChannels = new List<string> { "twitch:hallowgg", "youtube:@HallowGaming" };
            // 1.1: every picture in both looks (dark = the names as before, light = "-light")
            foreach (var light in new[] { false, true })
            {
                SnapLight = light; LoadTheme(Application.Current);
                var sfx = light ? "-light" : "";
                Action<string, Action<BrowserWindow>> shot = (name, setup) =>
                {
                    s.VerticalTabs = false; s.VTabsCollapsed = false;
                    var w = new BrowserWindow(s, true) { Width = 1280, Height = 800 };
                    w.SnapFill("news", name.Contains("game"), name.Contains("keys"));
                    if (setup != null) setup(w);
                    Snap.Save(w, Path.Combine(dir, name + sfx + ".png"), 1280, 800);
                    w.Close();
                };
                shot("browser", null);
                shot("browser-game", null);
                shot("browser-keys", null);
                shot("sidebar", w => w.Side.SnapShow("discord", Panels.MockChat(), new Dictionary<string, string> { { "discord", "3" }, { "whatsapp", "12" } }));
                shot("hot-tabs", w => w.ContentGrid.Children.Add(HotTabs.SnapPanel()));
                // 1.2: groups, vertical tabs (and icons only, beside the sidebar on the right), split view, reader, Ctrl+K, workspaces
                shot("groups", w => w.SnapPower("groups"));
                shot("vertical-tabs", w => { w.SnapPower("vertical"); w.Side.SnapShow("discord", Panels.MockChat(), new Dictionary<string, string> { { "discord", "3" } }); });
                shot("vertical-icons", w => w.SnapPower("vertical-icons"));
                shot("split", w => w.SnapPower("split"));
                shot("reader", w => w.SnapPower("reader"));
                shot("palette", w => w.SnapPower("palette"));
                shot("workspaces", w => w.SnapPower("workspaces"));
                shot("bookmarks", w => w.SnapBookmarks(true));          // 1.3: the bookmarks bar and the manager (made-up bookmarks)
                shot("bookmarks-bar", w => w.SnapBookmarks(false));
                // 1.4: a private window, the extensions page, site settings and a permission prompt (made-up)
                var pw = new BrowserWindow(s, true, true) { Width = 1280, Height = 800 }; pw.SnapFill("news", false, false); pw.SnapPrivate();
                Snap.Save(pw, Path.Combine(dir, "private" + sfx + ".png"), 1280, 800); pw.Close();
                shot("extensions", w => w.SnapPage("extensions"));
                shot("sites", w => w.SnapPage("sites"));
                shot("omni", w => w.SnapOmni());              // 1.5: the address bar's answer, keywords and suggestions (made-up)
                shot("downloads", w => w.SnapDownloads());    // 1.5: the downloads page with Defender's badges (made-up)
                // 1.6: the profile chip and switcher, the reading list and notes, apps, an app window, sleeping tabs (all made-up)
                shot("profiles", w => { Profiles = BrowserWindow.SnapProfiles(); w.SnapProfileSwitcher(); }); Profiles = null;
                shot("reading-list", w => w.SnapSide("reading"));
                shot("notes", w => w.SnapSide("notes"));
                shot("apps", w => { w.SnapApps(); });
                shot("app-window", w => w.SnapAppWindow());
                shot("sleeping-tabs", w => w.SnapSleeping());
                // 1.13: the cleaner menu, two of its submenus and the History page (made-up)
                shot("menu", w => Menu113.Snap(w, null));
                shot("menu-view", w => Menu113.Snap(w, "view"));
                shot("menu-more", w => Menu113.Snap(w, "more"));
                shot("history", w => w.SnapHistory());
                Profiles = BrowserWindow.SnapProfiles();
                var ws = new BrowserWindow(s, true) { Width = 1280, Height = 800 };
                ws.SnapFill("news", false, false);
                var set = Panels.SettingsPage(ws); ws.ContentGrid.Children.Add(set);
                Snap.Save(ws, Path.Combine(dir, "settings" + sfx + ".png"), 1280, 5600);
                ws.Close(); Profiles = null;
                // 1.7: Settings filtered by a search, with a translation service set up (a made-up NAS address)
                s.TranslateProvider = "libre"; s.TranslateLibreUrl = "http://192.168.1.20:5000";
                var ws2 = new BrowserWindow(s, true) { Width = 1280, Height = 800 }; ws2.SnapFill("news", false, false); ws2.SettingsQuery = "translate";
                ws2.ContentGrid.Children.Add(Panels.SettingsPage(ws2));
                Snap.Save(ws2, Path.Combine(dir, "settings-search" + sfx + ".png"), 1280, 800); ws2.Close();
                s.TranslateProvider = ""; s.TranslateLibreUrl = null;
                // 1.8: the YouTube and Twitch card (SponsorBlock and the Twitch chat switched on - made-up, nothing is contacted in --snap)
                s.SponsorSkip = true; s.TwitchChat = true;
                var ws3 = new BrowserWindow(s, true) { Width = 1280, Height = 800 }; ws3.SnapFill("news", false, false); ws3.SettingsQuery = "SponsorBlock";
                ws3.ContentGrid.Children.Add(Panels.SettingsPage(ws3));
                Snap.Save(ws3, Path.Combine(dir, "stream-settings" + sfx + ".png"), 1280, 800); ws3.Close();
                s.SponsorSkip = false; s.TwitchChat = false;
                // 1.9: Settings > Accessibility, and the browser at a 150% UI scale (made-up page; pages keep their own zoom)
                var ws4 = new BrowserWindow(s, true) { Width = 1280, Height = 800 }; ws4.SnapFill("news", false, false); ws4.SettingsQuery = "Accessibility";
                ws4.ContentGrid.Children.Add(Panels.SettingsPage(ws4));
                Snap.Save(ws4, Path.Combine(dir, "accessibility" + sfx + ".png"), 1280, 800); ws4.Close();
                // 1.10: Settings > Gaming > Twitch ads with two made-up channels on the allow list
                s.TwitchAllow = new List<string> { "hallowfriend", "smallstreamer_42" };
                var ws5 = new BrowserWindow(s, true) { Width = 1280, Height = 800 }; ws5.SnapFill("news", false, false); ws5.SettingsQuery = "streamer is paid";
                ws5.ContentGrid.Children.Add(Panels.SettingsPage(ws5));
                Snap.Save(ws5, Path.Combine(dir, "twitch-ads" + sfx + ".png"), 1280, 800); ws5.Close();
                s.TwitchAllow = new List<string>();
                // 1.12: Settings > General > Default browser - made-up answers (Brave is the default / Hallow is), nothing read or opened
                foreach (var pid in new[] { "BraveHTML", "HallowBrowserURL" })
                {
                    DefaultBrowser.ProgIdOverride = pid;
                    var ws6 = new BrowserWindow(s, true) { Width = 1280, Height = 800 }; ws6.SnapFill("news", false, false); ws6.SettingsQuery = "Default browser";
                    ws6.ContentGrid.Children.Add(Panels.SettingsPage(ws6));
                    Snap.Save(ws6, Path.Combine(dir, (pid == "BraveHTML" ? "default-browser" : "default-browser-on") + sfx + ".png"), 1280, 800); ws6.Close();
                }
                DefaultBrowser.ProgIdOverride = null;
                A11y.SetScale(1.5);
                shot("ui-scale-150", null);
                A11y.SetScale(1.0);
                // 1.18: the Brave-style top bar - each at 1280 px and at a narrow 900 px window (all made-up)
                Action<string, double, Action<BrowserWindow>> shotW = (name, width, setup) =>
                {
                    s.VerticalTabs = false; s.VTabsCollapsed = false;
                    var w = new BrowserWindow(s, true) { Width = width, Height = 800 };
                    w.SnapFill("news", false, false);
                    if (setup != null) setup(w);
                    Snap.Save(w, Path.Combine(dir, name + sfx + ".png"), width, 800);
                    w.Close();
                };
                foreach (var width in new[] { 1280.0, 900.0 })
                {
                    var wsfx = width < 1000 ? "-900" : "";
                    if (wsfx.Length > 0) shotW("browser-900", width, null);
                    shotW("toolbar-customize" + wsfx, width, w => w.SnapTopBar("customize"));
                    shotW("site-info" + wsfx, width, w => w.SnapTopBar("site-info"));
                    shotW("extensions-flyout" + wsfx, width, w => w.SnapTopBar("extensions"));
                    s.Extensions = new List<UserExtension>(); s.PinnedExtensions = null;
                    shotW("downloads-flyout" + wsfx, width, w => w.SnapTopBar("downloads"));
                    shotW("page-actions" + wsfx, width, w => w.SnapTopBar("page-actions"));
                    Profiles = BrowserWindow.SnapProfiles();
                    shotW("profile-button" + wsfx, width, w => w.SnapTopBar("profile"));
                    Profiles = null;
                    shotW("not-secure" + wsfx, width, w => w.SnapTopBar("http"));
                    shotW("tabs-overflow" + wsfx, width, w => w.SnapTopBar("overflow"));
                }
                // 1.19: pinned tabs (title bar and column), what the padlock says about cookie banners, share with Copy clean link, the
                // Ask Claude panel with a made-up conversation (no key, nothing sent), and the new settings - each at 1280 and 900 px
                foreach (var width in new[] { 1280.0, 900.0 })
                {
                    var wsfx = width < 1000 ? "-900" : "";
                    shotW("pinned-tabs" + wsfx, width, w => w.SnapExtras("pinned"));
                    shotW("pinned-vertical" + wsfx, width, w => w.SnapExtras("pinned-vertical"));
                    s.VerticalTabs = false;
                    shotW("cookie-banner-site-info" + wsfx, width, w => w.SnapExtras("cookie"));
                    shotW("share-clean-link" + wsfx, width, w => w.SnapExtras("share-clean"));
                    shotW("ask-claude" + wsfx, width, w => w.SnapAi());
                    s.AiOn = false; AiKey.SnapHas = null;
                    foreach (var q in new[] { Tuple.Create("settings-privacy-119", "cookie banners"), Tuple.Create("settings-ask-claude", "Ask Claude") })
                    {
                        if (q.Item1 == "settings-ask-claude") { s.AiOn = true; AiKey.SnapHas = true; }
                        var sw = new BrowserWindow(s, true) { Width = width, Height = 800 }; sw.SnapFill("news", false, false); sw.SettingsQuery = q.Item2;
                        sw.ContentGrid.Children.Add(Panels.SettingsPage(sw));
                        Snap.Save(sw, Path.Combine(dir, q.Item1 + wsfx + sfx + ".png"), width, 800); sw.Close();
                        s.AiOn = false; AiKey.SnapHas = null;
                    }
                }
            }
        }
    }
}
