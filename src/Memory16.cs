// Hallow Browser 1.6 - memory saver levels, battery saver, the "memory saved" mark on sleeping tabs and startup boost.
//  - Memory saver: Off / Balanced / Max = background tabs sleep after 30 / 10 / 2 minutes (Custom keeps 1.5's own choice).
//  - Battery saver: on battery, tabs sleep twice as soon, and background tabs and hidden panels run slowed down (DevTools CPU
//    throttling x4: their timers and scripts - and so any drawing they still do - run at a quarter of the speed). Honestly: a tab you
//    can't see already draws no frames in Chromium; WebView2 has no frame-rate setting, so slowing them down is what the browser can do.
//  - Startup boost (OFF by default): when the last window closes, the browser's own process stays, hidden, with no page open, at
//    idle priority and with its memory trimmed - the next start shows a window at once. It does use some memory while closed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class Power16
    {
        // tests: HALLOW_BROWSER_FAKEBATTERY = a file whose text is "battery" or "ac"
        public static readonly string FakeBatteryFile = BrowserSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_BROWSER_FAKEBATTERY") : null;
        public static bool OnBattery
        {
            get
            {
                if (FakeBatteryFile != null) { try { return System.IO.File.ReadAllText(FakeBatteryFile).Trim() == "battery"; } catch { return false; } }
                try { return System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline; } catch { return false; }
            }
        }
        public static int LevelMinutes(string level, int custom)
        {
            switch (level) { case "off": return 30; case "max": return 2; case "custom": return custom; default: return 10; }
        }
        // what the tick uses: the level, halved on battery (at least a minute); "custom" Never stays never
        public static int SleepMinutes(string level, int custom, bool battery, bool batterySaver)
        {
            var m = LevelMinutes(level, custom);
            if (m > 0 && battery && batterySaver) m = Math.Max(1, m / 2);
            return m;
        }
        public static string LevelName(string level) { return level == "off" ? "Off" : level == "max" ? "Max" : level == "custom" ? "Custom" : "Balanced"; }
        public static string SavedText(long mb) { return mb <= 0 ? "" : mb >= 1024 ? (mb / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB" : mb + " MB"; }
        public const double BatteryThrottle = 4;
    }

    public partial class BrowserWindow
    {
        public int SleepMinutesNow() { return Power16.SleepMinutes(S.MemorySaver, S.SleepMinutes, Power16.OnBattery, S.BatterySaver); }
        public bool BatteryMode { get; private set; }
        readonly HashSet<CoreWebView2> slowed = new HashSet<CoreWebView2>();
        public int SlowedCount { get { return slowed.Count; } }
        // every tick: on battery (and battery saver on) the background tabs and hidden panels are throttled; the one you look at never
        void BatteryTick()
        {
            bool on = S.BatterySaver && Power16.OnBattery && !snap;
            var want = new HashSet<CoreWebView2>();
            if (on)
            {
                foreach (var t in Tabs) { var c = t.Core; if (c != null && !IsShown(t) && !t.Audible && !t.Suspended) want.Add(c); }
                foreach (var p in Side.Panels) { var c = p.Core; if (c != null && !p.Shown && !p.Audible && !p.Asleep) want.Add(c); }
            }
            foreach (var c in slowed.Where(c => !want.Contains(c)).ToList()) { slowed.Remove(c); try { var _ = c.CallDevToolsProtocolMethodAsync("Emulation.setCPUThrottlingRate", "{\"rate\":1}"); } catch { } }
            foreach (var c in want.Where(c => !slowed.Contains(c)).ToList()) { slowed.Add(c); try { var _ = c.CallDevToolsProtocolMethodAsync("Emulation.setCPUThrottlingRate", "{\"rate\":" + Power16.BatteryThrottle + "}"); } catch { } }   // 1.12.1: the memory target is ApplyMemoryTargets' (normal again once shown)
            if (on != BatteryMode) { BatteryMode = on; WriteStatus(); }
        }

        // ---- "memory saved": the tab's page process before it slept, minus after (an unloaded tab frees all of it)
        public async Task<long> TabMemoryMb(Tab t)
        {
            var c = t.Core; if (c == null || Engine.Env == null) return 0;
            try
            {
                uint fid = c.FrameId;
                foreach (var x in await Engine.Env.GetProcessExtendedInfosAsync())
                {
                    if (!x.AssociatedFrameInfos.Any(f => { try { return f.FrameId == fid; } catch { return false; } })) continue;
                    using (var pr = Process.GetProcessById(x.ProcessInfo.ProcessId)) { var mb = pr.PrivateMemorySize64 / (1024 * 1024); t.LastMemMb = mb; return mb; }
                }
            }
            catch { }
            return 0;
        }
        async Task MeasureSaved(Tab t, long before)
        {
            await Task.Delay(2500);
            if (!t.Suspended || t.Core == null) return;
            long after = await TabMemoryMb(t);
            // a sleeping page trims what it can; at least a little is always counted while it sleeps
            t.SavedMb = before > 0 ? Math.Max(1, before - after) : 0; t.LastMemMb = before;
            SetTabIcon(t); WriteStatus();
        }
        void SleepMark(Tab t)
        {
            if (t.SleepIcon == null)
            {
                t.SleepIcon = Ui.Icon("leaf", 10, "GreenText"); t.SleepIcon.Margin = new Thickness(4, 0, 2, 0); t.SleepIcon.VerticalAlignment = VerticalAlignment.Center;
                if (t.RightBox != null) t.RightBox.Children.Insert(0, t.SleepIcon);
            }
            bool asleep = t.Suspended || t.Discarded;
            t.SleepIcon.Visibility = asleep && !snap || (snap && asleep) ? Visibility.Visible : Visibility.Collapsed;
            t.SleepIcon.ToolTip = !asleep ? null : (t.Discarded ? "Unloaded to save memory" : "Asleep to save memory") + (t.SavedMb > 0 ? " - about " + Power16.SavedText(t.SavedMb) + " saved" : "") + ". Click the tab to wake it.";
        }
        public string SleepTip(Tab t) { return t.SleepIcon == null || t.SleepIcon.Visibility != Visibility.Visible ? null : t.SleepIcon.ToolTip as string; }

        public UIElement[] MemorySettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Muted("Memory saver: background tabs go to sleep after", 12, 6));
            items.Add(Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("off", "Off - 30 min"), new KeyValuePair<string, string>("balanced", "Balanced - 10 min"), new KeyValuePair<string, string>("max", "Max - 2 min"), new KeyValuePair<string, string>("custom", "Custom") }, S.MemorySaver, v => { S.MemorySaver = v; S.Save(); Tick(true); ReloadSettings(); }));
            if (S.MemorySaver == "custom")
            {
                items.Add(Ui.Muted("Custom: put background tabs to sleep after", 12, 6));
                items.Add(Ui.Chips(BrowserSettings.SleepChoices.Select(m => new KeyValuePair<int, string>(m, m == 0 ? "Never" : m + " min")).ToList(), S.SleepMinutes, v => { S.SleepMinutes = v; S.Save(); }));
            }
            items.Add(Ui.Muted("A sleeping tab shows a leaf; hover it for about how much memory it gave back. Tabs playing sound and the one you look at never sleep; a click wakes a tab where it was.", 11.5, 10));
            items.Add(Ui.Switch("Battery saver", "On battery: tabs sleep twice as soon, and background tabs and hidden sidebar panels run slowed down (a quarter of the speed). Honestly: a tab you can't see already draws no frames; WebView2 has no frame-rate setting, so slowing their scripts and timers is what the browser can do." + (Power16.OnBattery ? " (This PC is on battery now.)" : ""), S.BatterySaver, on => { S.BatterySaver = on; S.Save(); Tick(true); }));
            items.Add(Ui.Switch("Startup boost - uses some memory while the browser is closed", "Off by default. When you close the last window, a small hidden Hallow Browser process stays (no pages open, idle priority, its memory trimmed), so the next start shows a window at once. It does keep using some memory (about " + BoostNote + ") until you sign out, or until Hallow Hub's Exit / \"HallowBrowser.exe --cmd quit\". The page engine itself is started again when you open the browser.", S.StartupBoost, on => { S.StartupBoost = on; S.Save(); }));
            return items.ToArray();
        }
        public static string BoostNote = "40-80 MB";
    }

    public static partial class Program
    {
        public static bool Boosted, RealQuit;
        public static Stopwatch WakeClock;
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);
        // the last window closed: with startup boost on (and not quitting for real or for an update), stay as a small hidden process
        static bool Boost(BrowserWindow last)
        {
            if (RealQuit || last == null || last.IsSnap || !last.S.StartupBoost || Updating) return false;
            Boosted = true; MarkClosed();   // a boosted process that ends with Windows isn't a crash
            Vibe.StopAll();
            Quitting = false; SessionFrozen = false;
            MainShield.ProfileDone = false; PrivateShield.ProfileDone = false;
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; } catch { }
            WriteBoostStatus();
            var d = Application.Current.Dispatcher;
            d.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(1500); if (!Boosted) return;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1)); } catch { }
            }), System.Windows.Threading.DispatcherPriority.Background);
            return true;
        }
        public static bool Updating;
        static void WriteBoostStatus()
        {
            Family.WriteStatus(Family.Json(new Dictionary<string, object> { { "app", "Browser" }, { "version", Version }, { "updated", Family.Time(DateTime.Now) }, { "running", true }, { "boosted", true }, { "tabs", 0 }, { "windows", 0 }, { "gameMode", false }, { "game", null }, { "lastCrash", null } }));
        }
        // a new start while boosted: a window at once (the page engine starts again behind it)
        public static void Wake(List<string> urls, string app)
        {
            if (!Boosted) return;
            Boosted = false; WakeClock = Stopwatch.StartNew();
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }
            var s = BrowserSettings.Load(); MarkRunning(s);
            if (Engine.Env == null && Engine.Starting == null) Engine.Starting = Engine.Create(s);
            var w = new BrowserWindow(s, false); Register(w);
            if (app != null) w.MakeAppWindow(SiteApps.ForUrl(app) ?? SiteApps.Adhoc(app));
            w.ContentRendered += (a, b) => { if (WakeClock != null) lock (Engine.Perf) Engine.Perf["boost-wake"] = WakeClock.Elapsed.TotalMilliseconds; };
            w.Loaded += async (a, b) => { if (w.AppMode) await w.StartApp(); else await w.Start(urls); };
            w.Show(); if (!BrowserSettings.Offscreen) w.Activate();
        }
        // "quit" while boosted (Hub's Exit, --cmd quit): the hidden process goes
        public static void EndBoost() { Boosted = false; MarkClosed(); try { Application.Current.Shutdown(); } catch { Environment.Exit(0); } }
        public static long BoostWorkingSetMb() { try { using (var p = Process.GetCurrentProcess()) { p.Refresh(); return p.WorkingSet64 / (1024 * 1024); } } catch { return -1; } }
        public static long BoostMemoryMb()
        {
            long sum = 0;
            try { using (var p = Process.GetCurrentProcess()) { p.Refresh(); sum += p.PrivateMemorySize64; } } catch { }
            try { if (Engine.Env != null) foreach (var pi in Engine.Env.GetProcessInfos()) try { using (var pr = Process.GetProcessById(pi.ProcessId)) sum += pr.PrivateMemorySize64; } catch { } } catch { }
            return sum / (1024 * 1024);
        }

        // ---- the Default profile's "menu" lists the other profiles (Hallow Hub's tray menu shows them)
        static IEnumerable<object> ProfileMenuItems()
        {
            var s = Profiles; if (s == null || s.Items.Count < 2) return new object[0];
            return new object[] { new Dictionary<string, object> { { "id", "-" } } }.Concat(s.Items.Where(x => x.Id != BrowserSettings.ProfileId).Take(5).Select(x => (object)new Dictionary<string, object> { { "id", "profile-" + x.Id }, { "label", "Open profile: " + x.Name } }));
        }
    }
}
