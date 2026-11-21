// Hallow Browser - a fast Chromium browser for gamers in the Hallow style, on Microsoft Edge WebView2 (the Evergreen runtime
// that comes with Windows 11). Hallow Shield is built in, Hallow Keys fills logins when you click, and gaming-light mode puts
// background tabs to sleep while you play. It never makes itself the default browser and has no server of its own.
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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace HallowBrowser
{
    public static class Program
    {
        public const string Version = "1.0";
        public const string SdkVersion = "1.0.4314.35";
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
        public static BrowserWindow Win;
        public static string AccentHex = Accent.Default;
        static FileSystemWatcher accentWatch;

        [STAThread]
        public static int Main(string[] args)
        {
            Engine.Mark("main");
            int ci = Array.IndexOf(args, "--cmd");
            if (ci >= 0) return Family.RunCmd(ci + 1 < args.Length ? args[ci + 1] : "");
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            int si = Array.IndexOf(args, "--snap");
            if (si >= 0 && si + 1 < args.Length) { BrowserSettings.Frozen = true; var sa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; LoadTheme(sa); SnapAll(args[si + 1]); return 0; }

            var urls = args.Where(a => !a.StartsWith("--")).Select(a => Omni.Interpret(a, "duckduckgo")).Where(u => u != null && Omni.IsWeb(u)).ToList();
            var leaf = BrowserSettings.TestDir != null ? "Test" + Path.GetFileName(BrowserSettings.TestDir.TrimEnd('\\')) : "";
            bool created;
            var mutex = new Mutex(true, @"Local\HallowBrowser" + leaf, out created);
            if (!created)
            {
                // already running: hand the addresses to it (or just bring it forward)
                for (int i = 0; i < 20; i++)
                {
                    bool ok = true;
                    if (urls.Count == 0) ok = Family.Send("Browser", "open", 3000) != null;
                    else foreach (var u in urls) ok &= Family.Send("Browser", "open " + u, 3000) != null;
                    if (ok) return 0;
                    Thread.Sleep(250);
                }
                return 1;
            }
            Crash.Install();
            // speed: the JIT compiles start-up code on spare cores, guided by a profile of the last start (no admin, no install step)
            try { Directory.CreateDirectory(BrowserSettings.LocalDir); System.Runtime.ProfileOptimization.SetProfileRoot(BrowserSettings.LocalDir); System.Runtime.ProfileOptimization.StartProfile("startup.jit"); } catch { }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            LoadTheme(app); Engine.Mark("theme");
            app.DispatcherUnhandledException += (s, e) => { Crash.Write(e.Exception); e.Handled = true; try { if (Win != null) Win.SaveSession(); } catch { } Environment.Exit(1); };

            var runtime = Engine.FindRuntime(); Engine.Mark("runtime-found");
            if (runtime == null) { NoRuntime(app); GC.KeepAlive(mutex); return 2; }
            var st = BrowserSettings.Load();
            Engine.Starting = Engine.Create(st);   // the page engine starts while the window is being built
            var w = new BrowserWindow(st, false); Win = w;
            Family.Serve("Browser", line => Command(w, line));
            accentWatch = Accent.Watch(() => app.Dispatcher.BeginInvoke(new Action(ReloadTheme)));
            w.Loaded += async (s, e) => { await w.Start(urls); CrashNotice(w); };
            app.Run(w);
            GC.KeepAlive(mutex);
            return 0;
        }

        // ---- the theme (Theme.xaml with the Hallow accent swapped in)
        public static void LoadTheme(Application app)
        {
            var s = BrowserSettings.Load();
            AccentHex = Accent.Current(s.UseHallowAccent);
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Accent.ApplyTo(Res("HallowBrowser.Theme.xaml"), AccentHex)));
        }
        public static void ReloadTheme()
        {
            try { LoadTheme(Application.Current); if (Win != null) { Engine.WriteNewTab(Win.S, AccentHex); Win.ReloadSettings(); } } catch { }
        }
        static string Res(string name) { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) using (var r = new StreamReader(s)) return r.ReadToEnd(); }

        // ---- status.json / pipe "status": no addresses or titles, ever
        public static string StatusJson(BrowserWindow w)
        {
            var c = Crash.LastTime();
            return Family.Json(new Dictionary<string, object> {
                { "app", "Browser" }, { "version", Version }, { "updated", Family.Time(DateTime.Now) }, { "running", true },
                { "tabs", w.Tabs.Count }, { "asleep", w.Tabs.Count(t => t.Suspended || t.Discarded) }, { "gameMode", w.Game != null }, { "game", w.Game },
                { "shield", w.ShieldState }, { "shieldVersion", w.ShieldVersion }, { "runtime", Engine.RuntimeVersion }, { "lastCrash", c == null ? null : Family.Time(c.Value) } });
        }

        // ---- the family pipe
        public static string Command(BrowserWindow w, string line)
        {
            var sp = line.IndexOf(' '); var cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant(); var arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            switch (cmd)
            {
                case "ping": return "ok " + Family.Json(new Dictionary<string, object> { { "app", "Browser" }, { "version", Version } });
                case "status": return "ok " + OnUi(w, () => StatusJson(w));
                case "open":
                    if (arg.Length == 0) return OnUi(w, () => { ShowWindow(w); return "ok"; });
                    Uri u;
                    if (!Uri.TryCreate(arg, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) return "error only http and https addresses can be opened";
                    return OnUi(w, () => { w.NewTab(u.AbsoluteUri, true); ShowWindow(w); return "ok"; });
                case "menu":
                    return "ok " + OnUi(w, () => Family.Json(new Dictionary<string, object> { { "app", "Browser" }, { "items", new object[] {
                        new Dictionary<string, object> { { "id", "new-tab" }, { "label", "New tab" } },
                        new Dictionary<string, object> { { "id", "reopen" }, { "label", "Reopen closed tab" }, { "enabled", w.ClosedCount > 0 } },
                        new Dictionary<string, object> { { "id", "-" } },
                        new Dictionary<string, object> { { "id", "game-light" }, { "label", "Gaming-light mode" }, { "checked", w.S.GameLight } },
                        new Dictionary<string, object> { { "id", "cpu-limiter" }, { "label", "CPU limiter" }, { "checked", w.S.CpuLimiter } } } } }));
                case "invoke":
                    return OnUi(w, () =>
                    {
                        switch (arg)
                        {
                            case "new-tab": w.NewTab(Omni.NewTabUrl, true); ShowWindow(w); return "ok";
                            case "reopen": w.ReopenClosed(); ShowWindow(w); return "ok";
                            case "game-light": w.S.GameLight = !w.S.GameLight; w.S.Save(); w.Tick(true); return "ok";
                            case "cpu-limiter": w.S.CpuLimiter = !w.S.CpuLimiter; w.S.Save(); w.Tick(true); return "ok";
                        }
                        return "error unknown item";
                    });
                case "quit": w.Dispatcher.BeginInvoke(new Action(w.Close)); return "ok";
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
            w.Downloads.Clear(); w.S.Save();
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
            var s = new BrowserSettings(); s.Fix();
            Action<string, Action<BrowserWindow>> shot = (name, setup) =>
            {
                var w = new BrowserWindow(s, true) { Width = 1280, Height = 800 };
                w.SnapFill("news", name.Contains("game"), name.Contains("keys"));
                if (setup != null) setup(w);
                Snap.Save(w, Path.Combine(dir, name + ".png"), 1280, 800);
                w.Close();
            };
            shot("browser", null);
            shot("browser-game", null);
            shot("browser-keys", null);
            var ws = new BrowserWindow(s, true) { Width = 1280, Height = 800 };
            ws.SnapFill("news", false, false);
            var set = Panels.SettingsPage(ws); ws.ContentGrid.Children.Add(set);
            Snap.Save(ws, Path.Combine(dir, "settings.png"), 1280, 1400);
            ws.Close();
        }
    }
}
