// Hallow Browser 1.5 - tabs and pages: hover previews (a small picture taken with CapturePreviewAsync while the tab is on screen,
// kept 240 px wide in memory), a tab picture following the mouse while you tear a tab off, "Reopen closed window", mouse gestures
// (optional, off by default: right-drag left = back, right = forward, down then right = close), "This tab crashed - Reload"
// instead of a dead page, "Restore your tabs" after the browser itself didn't close properly, private windows' flyouts in the
// private palette, and dark pages (Chromium's own auto dark mode, per tab through DevTools - see ApplyDark).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    // mouse gestures: the page's script sends the directions it saw (L, R, U, D - repeats folded), the browser decides
    public static class Gestures
    {
        public static string Action(string dirs)
        {
            switch (dirs) { case "L": return "back"; case "R": return "forward"; case "DR": return "close"; }
            return null;
        }
        // right button held: each move of 24 px or more counts in its main direction; the same direction twice in a row counts once
        public const string Script = "(function(){try{if(window.top!==window||window.__hallowGest)return;window.__hallowGest=1;var wv=window.chrome&&window.chrome.webview;if(!wv)return;" +
            "var on=true;window.__hallowGestOff=function(){on=false;};window.__hallowGestOn=function(){on=true;};var x0,y0,dirs=null,done=false;" +
            "addEventListener('mousedown',function(e){if(!on||e.button!==2||!e.isTrusted)return;x0=e.screenX;y0=e.screenY;dirs='';done=false;},true);" +
            "addEventListener('mousemove',function(e){if(dirs===null)return;if(!(e.buttons&2)){dirs=null;return;}var dx=e.screenX-x0,dy=e.screenY-y0;if(Math.abs(dx)<24&&Math.abs(dy)<24)return;" +
            "var d=Math.abs(dx)>Math.abs(dy)?(dx>0?'R':'L'):(dy>0?'D':'U');if(dirs.slice(-1)!==d)dirs+=d;x0=e.screenX;y0=e.screenY;},true);" +
            "addEventListener('mouseup',function(e){if(dirs===null||e.button!==2)return;var g=dirs;dirs=null;if(g.length&&g.length<=4){done=true;wv.postMessage('hallow:gesture\\t'+g);}},true);" +
            "addEventListener('contextmenu',function(e){if(done){done=false;e.preventDefault();e.stopImmediatePropagation();}},true);" +
            "}catch(e){}})();";
    }

    public static partial class Program
    {
        // 1.5: the last windows you closed (not the last one - that quits), for "Reopen closed window" - in memory only
        public static readonly List<WindowSession> ClosedWindows = new List<WindowSession>();
        public static void RememberClosed(BrowserWindow w)
        {
            if (w == null || w.Private || w.IsSnap || w.Tabs.Count == 0) return;
            var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds; bool off = BrowserSettings.Offscreen;
            ClosedWindows.Add(new WindowSession { Session = w.BuildSession(), Left = off ? double.NaN : r.Left, Top = off ? double.NaN : r.Top, Width = r.Width, Height = r.Height, Maximized = w.WindowState == WindowState.Maximized });
            if (ClosedWindows.Count > 5) ClosedWindows.RemoveAt(0);
        }
        public static BrowserWindow ReopenClosedWindow()
        {
            if (ClosedWindows.Count == 0) { var s = Win; if (s != null) s.Toast("No closed windows to bring back.", 3); return null; }
            var ws = ClosedWindows[ClosedWindows.Count - 1]; ClosedWindows.RemoveAt(ClosedWindows.Count - 1);
            return OpenWindow(false, null, null, First, null, ws.Session, null, ws);
        }

        // 1.5: did the browser close properly last time? A marker exists while it runs; a normal exit removes it
        public static string RunningMarker { get { return Path.Combine(BrowserSettings.Dir, "running.flag"); } }
        public static bool CrashedLastTime;
        public static Session CrashedSession;   // the tabs from then, when they aren't restored by themselves (session restore off)
        public static void MarkRunning(BrowserSettings s)
        {
            try
            {
                CrashedLastTime = File.Exists(RunningMarker);
                if (CrashedLastTime && !s.RestoreSession) CrashedSession = BrowserSettings.LoadSession();
                Directory.CreateDirectory(BrowserSettings.Dir); File.WriteAllText(RunningMarker, DateTime.Now.ToString("s"));
            }
            catch { }
        }
        public static void MarkClosed() { try { File.Delete(RunningMarker); } catch { } }

        // 1.5: flyouts (pop-ups) aren't in a window's tree, so they took the normal look in private windows - they now get the private palette
        public static void HookPrivateFlyouts()
        {
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                var fe = s as FrameworkElement; if (fe == null || e.OriginalSource != s) return;
                var pop = fe.Parent as Popup;
                if (pop != null) { PrivateFlyouts.Fix(pop); return; }
                var win = fe as Window; if (win != null && !(win is BrowserWindow)) { var o = win.Owner as BrowserWindow; if (o != null && o.Private) PrivateFlyouts.Apply(win.Resources); }
            }));
        }
    }

    public static class PrivateFlyouts
    {
        // the window a pop-up belongs to (its placement target's window), and the private palette on its content if that's a private window
        public static bool Fix(Popup p)
        {
            var child = p == null ? null : p.Child as FrameworkElement; if (child == null) return false;
            var target = p.PlacementTarget as DependencyObject; if (target == null) return false;
            BrowserWindow w = null; try { w = Window.GetWindow(target) as BrowserWindow; } catch { }
            if (w == null) { var ow = Window.GetWindow(target); if (ow != null) w = ow.Owner as BrowserWindow; }
            if (w == null || !w.Private) return false;
            Apply(child.Resources); return true;
        }
        public static void Apply(ResourceDictionary rd)
        {
            if (rd == null || rd.Contains("HallowPrivate")) return;
            foreach (var kv in BrowserWindow.PrivatePalette) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value)); b.Freeze(); rd[kv.Key] = b; }
            rd["HallowPrivate"] = true;
        }
    }

    public partial class BrowserWindow
    {
        // ================================================================ hover previews
        public static int PreviewsTaken;
        void SchedulePreview(Tab t, int ms)
        {
            if (snap || !S.TabPreviews || t == null || t.View == null || t.Internal || Omni.IsNewTab(t.Url)) return;
            var _ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(ms); await TakePreview(t); }), DispatcherPriority.Background);
        }
        public async Task<bool> TakePreview(Tab t)
        {
            var c = t.Core; if (c == null || !IsShown(t) || t.CrashView != null || t.PoppedOut || t.Loading || Game != null) return false;
            if (DateTime.Now - t.PreviewAt < TimeSpan.FromSeconds(2)) return t.Preview != null;
            try
            {
                var ms = new MemoryStream();
                await c.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, ms);
                if (ms.Length == 0) return false;
                ms.Position = 0;
                var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.DecodePixelWidth = 240; img.EndInit(); img.Freeze();
                t.Preview = Speed19.Detach(img); t.PreviewAt = DateTime.Now; PreviewsTaken++;   // 1.9: without the JPEG it came from
                return true;
            }
            catch { return false; }
        }
        Popup hoverPop; DispatcherTimer hoverTimer; Tab hoverTab;
        void HoverEnter(Tab t)
        {
            if (!S.TabPreviews || snap) return;
            hoverTab = t;
            if (hoverTimer == null) { hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) }; hoverTimer.Tick += (a, b) => { hoverTimer.Stop(); ShowHover(hoverTab); }; }
            hoverTimer.Stop(); hoverTimer.Start();
        }
        void HoverLeave() { if (hoverTimer != null) hoverTimer.Stop(); hoverTab = null; if (hoverPop != null) hoverPop.IsOpen = false; }
        // the preview's content (tests build it without showing it)
        public FrameworkElement HoverCard(Tab t)
        {
            var sp = new StackPanel { Width = 240 };
            if (t.Preview != null && t != Active) { var im = new Image { Source = t.Preview, Width = 240, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) }; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality); sp.Children.Add(new Border { Child = im, CornerRadius = new CornerRadius(4), ClipToBounds = true }); }
            sp.Children.Add(new TextBlock { Text = t.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 2) });
            var host = t.Internal ? "Hallow Browser" : Omni.IsNewTab(t.Url) ? "New tab" : Omni.HostOf(t.Url) ?? "";
            var extra = t.Suspended || t.Discarded ? "  -  asleep" : t.Audible ? "  -  playing" : t.CrashView != null ? "  -  crashed" : "";
            sp.Children.Add(Ui.Muted(host + extra, 11, 0));
            return Ui.Flyout(sp, double.NaN);
        }
        void ShowHover(Tab t)
        {
            if (t == null || !Tabs.Contains(t) || dragActive || !t.Header.IsMouseOver) return;
            if (hoverPop != null) hoverPop.IsOpen = false;
            hoverPop = new Popup { Child = HoverCard(t), PlacementTarget = t.Header, Placement = S.VerticalTabs ? PlacementMode.Right : PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false, VerticalOffset = 2 };
            if (!BrowserSettings.Offscreen) hoverPop.IsOpen = true;
        }

        // ================================================================ the drag picture while tearing a tab off
        Popup ghost; public bool GhostShown;
        public FrameworkElement GhostCard(Tab t)
        {
            var sp = new StackPanel { Width = 200 };
            if (t.Preview != null) sp.Children.Add(new Image { Source = t.Preview, Width = 200, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) });
            else { var ph = new Border { Height = 110, Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(4) }; ph.SetResourceReference(Border.BackgroundProperty, "Bg"); ph.Child = Ui.Icon("window-restore", 28, "Lilac"); sp.Children.Add(ph); }
            sp.Children.Add(new TextBlock { Text = t.Title, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11.5 });
            var b = Ui.Flyout(sp, double.NaN); b.Opacity = 0.88; b.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); return b;
        }
        void GhostMove(Tab t, MouseEventArgs e)
        {
            GhostShown = true;
            if (BrowserSettings.Offscreen || snap) return;   // never on screen in tests
            if (ghost == null) { ghost = new Popup { Child = GhostCard(t), Placement = PlacementMode.AbsolutePoint, AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false }; PrivateFlyouts.Apply(Private ? ((FrameworkElement)ghost.Child).Resources : null); }
            try
            {
                var p = ToDip(t.Header.PointToScreen(e.GetPosition(t.Header)));
                ghost.HorizontalOffset = p.X + 12; ghost.VerticalOffset = p.Y + 10;
                if (!ghost.IsOpen) ghost.IsOpen = true;
            }
            catch { }
        }
        void GhostHide() { GhostShown = false; if (ghost != null) { ghost.IsOpen = false; ghost = null; } }

        // ================================================================ "This tab crashed"
        public static int CrashPagesShown;
        void ShowCrashed(Tab t, string kind)
        {
            if (t == null || t.CrashView != null) return;
            t.Crashed = true; CrashPagesShown++;
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
            var ic = Ui.Icon("heart-crack", 44, "Lilac"); ic.Margin = new Thickness(0, 0, 0, 14); sp.Children.Add(ic);
            var h = Ui.T(kind == "hung" ? "This tab stopped responding" : "This tab crashed", 20, true, "Text", 6); h.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(h);
            var m = Ui.Muted((Omni.HostOf(t.Url) ?? "The page") + (kind == "hung" ? " isn't answering. Reload it, or wait a moment." : "'s page stopped working. Your other tabs are fine."), 12.5, 16); m.TextAlignment = TextAlignment.Center; m.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(m);
            var tt = t; var b = Ui.Btn("Reload", () => ReloadCrashed(tt), true, "arrow-rotate-right"); b.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(b);
            var bg = new Grid(); bg.SetResourceReference(Panel.BackgroundProperty, "Bg"); bg.Children.Add(sp);
            t.CrashView = bg; bg.Visibility = IsShown(t) ? Visibility.Visible : Visibility.Collapsed;
            content.Children.Add(bg);
            if (t.View != null) t.View.Visibility = Visibility.Hidden;   // a WebView draws over anything WPF puts on top of it
            LayoutSplit(); SetTabIcon(t);
        }
        public void ReloadCrashed(Tab t)
        {
            if (t == null) return;
            if (t.CrashView != null) { content.Children.Remove(t.CrashView); t.CrashView = null; }
            t.Crashed = false;
            if (t.View != null && IsShown(t) && !t.ReaderOn && !t.PoppedOut) t.View.Visibility = Visibility.Visible;
            if (t.Core != null) try { t.Core.Reload(); } catch { }
            else if (!t.Internal) { t.PendingUrl = t.Url; var _ = CreateView(t, null); }
            LayoutSplit(); SetTabIcon(t);
        }
        void DropCrashView(Tab t) { if (t != null && t.CrashView != null) { content.Children.Remove(t.CrashView); t.CrashView = null; } }

        // ================================================================ the browser didn't close properly: restore the tabs
        Border infoBar; TextBlock infoText; StackPanel infoBtns;
        public string InfoShown;
        void BuildInfoBar(StackPanel host)
        {
            infoBar = new Border { Visibility = Visibility.Collapsed, Padding = new Thickness(12, 6, 8, 6), BorderThickness = new Thickness(0, 1, 0, 0) };
            infoBar.SetResourceReference(Border.BackgroundProperty, "Panel"); infoBar.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            var g = new DockPanel();
            var close = Ui.Tool("xmark", "Dismiss", () => HideInfo(), 10); close.Width = 24; close.Height = 22; DockPanel.SetDock(close, Dock.Right); g.Children.Add(close);
            infoBtns = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(infoBtns, Dock.Right); g.Children.Add(infoBtns);
            var ic = Ui.Icon("triangle-exclamation", 13, "AmberText"); ic.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(ic, Dock.Left); g.Children.Add(ic);
            infoText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; g.Children.Add(infoText);
            infoBar.Child = g; host.Children.Add(infoBar);
        }
        public void ShowInfo(string text, params Tuple<string, Action>[] buttons)
        {
            if (infoBar == null) return;
            infoText.Text = text; InfoShown = text; infoBtns.Children.Clear();
            for (int i = 0; i < buttons.Length; i++) { var a = buttons[i].Item2; var b = Ui.Btn(buttons[i].Item1, () => { HideInfo(); a(); }, i == 0); b.Padding = new Thickness(10, 3, 10, 3); infoBtns.Children.Add(b); }
            infoBar.Visibility = Visibility.Visible;
        }
        public void HideInfo() { if (infoBar != null) infoBar.Visibility = Visibility.Collapsed; InfoShown = null; }
        public bool ClickInfoButton(int i) { if (infoBtns == null || i >= infoBtns.Children.Count) return false; ((Button)infoBtns.Children[i]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); return true; }
        public Button MenuButton { get { return menuBtn; } }
        void CrashRecovery()
        {
            if (!Program.CrashedLastTime || Private || snap) return;
            Program.CrashedLastTime = false;
            var s = Program.CrashedSession; Program.CrashedSession = null;
            if (S.RestoreSession) { ShowInfo("Hallow Browser didn't close properly last time - your tabs are back."); return; }
            int n = s == null || s.Tabs == null ? 0 : s.Tabs.Count(x => x != null && Omni.IsWeb(x.Url));
            if (n == 0) return;
            ShowInfo("Hallow Browser didn't close properly last time.", Tuple.Create<string, Action>("Restore your tabs (" + n + ")", () => RestoreTabs(s)));
        }
        // adds a session's tabs to this window (asleep until clicked) and the other windows it had
        public int RestoreTabs(Session s)
        {
            if (s == null || s.Tabs == null) return 0;
            Tab first = null; int n = 0;
            foreach (var st in s.Tabs.Where(x => x != null && Omni.IsWeb(x.Url)))
            {
                var t = new Tab { Url = st.Url, Title = st.Title ?? Omni.HostOf(st.Url) ?? "Tab", Discarded = true };
                AddHeader(t, Tabs.Count); Tabs.Add(t); n++; if (first == null) first = t;
            }
            // the empty new tab you started with goes away
            if (first != null && Tabs.Count == n + 1 && Omni.IsNewTab(Tabs[0].Url)) { var nt = Tabs[0]; Activate(first); CloseTab(nt); } else if (first != null) Activate(first);
            RenderStrip(); Changed();
            if (s.Windows != null) Program.RestoreWindows(s.Windows);
            return n;
        }

        // ================================================================ dark pages (Chromium's auto dark mode, per tab)
        // Tested on runtime 158: DevTools Emulation.setAutoDarkModeOverride turns Chromium's own force-dark on for one WebView, at once
        // (no restart), and it stays on through reloads and same- / cross-site navigations; "{}" turns it off again. (The
        // --enable-features=WebContentsForceDark flag works too, but only for the whole engine and only after a restart.)
        public bool WantsDark(string url)
        {
            if (!Omni.IsWeb(url)) return false;
            var r = SiteRules.Find(S, Omni.HostOf(url));
            if (r != null && r.Dark == "on") return true;
            if (r != null && r.Dark == "off") return false;
            return S.ForceDark;
        }
        void ApplyDark(Tab t, CoreWebView2 c, string url)
        {
            if (c == null) return;
            bool want = WantsDark(url);
            if (t != null && t.DarkApplied == want) return;
            if (t == null && !want) return;
            if (t != null) t.DarkApplied = want;
            try { var _ = c.CallDevToolsProtocolMethodAsync("Emulation.setAutoDarkModeOverride", want ? "{\"enabled\":true}" : "{}"); } catch { }
        }
        public void DarkChanged() { foreach (var w in Program.Windows) foreach (var t in w.Tabs) if (t.Core != null) w.ApplyDark(t, t.Core, t.Url); }
        public void ToggleSiteDark(Tab t)
        {
            if (t == null || !Omni.IsWeb(t.Url)) return;
            var host = Omni.HostOf(t.Url); bool now = WantsDark(t.Url);
            var r = SiteRules.Get(S, host, true); r.Dark = now == S.ForceDark ? (now ? "off" : "on") : null;
            SiteRules.Tidy(S); S.Save(); DarkChanged();
            Toast(host + (WantsDark(t.Url) ? ": pages made dark." : ": pages as the site draws them."), 3);
        }
        public void ToggleForceDark() { S.ForceDark = !S.ForceDark; S.Save(); DarkChanged(); Toast(S.ForceDark ? "Pages are made dark (sites you set otherwise stay as they are)." : "Pages are shown as the sites draw them.", 4); }

        // ================================================================ mouse gestures
        public static int GesturesDone; public static string LastGesture;
        readonly Dictionary<CoreWebView2, string> gestureScripts = new Dictionary<CoreWebView2, string>();
        async void AddGestures(CoreWebView2 c)
        {
            if (c == null || gestureScripts.ContainsKey(c)) return;
            gestureScripts[c] = "";
            try { gestureScripts[c] = await c.AddScriptToExecuteOnDocumentCreatedAsync(Gestures.Script); var _ = c.CallDevToolsProtocolMethodAsync("Runtime.evaluate", new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "expression", Gestures.Script + ";window.__hallowGestOn&&__hallowGestOn()" } })); } catch { }
        }
        public void GesturesChanged()
        {
            foreach (var w in Program.Windows)
                foreach (var c in w.Tabs.Select(t => t.Core).Where(x => x != null).ToList())
                {
                    if (S.Gestures) w.AddGestures(c);
                    else { string id; if (w.gestureScripts.TryGetValue(c, out id)) { try { if (id.Length > 0) c.RemoveScriptToExecuteOnDocumentCreated(id); var _ = c.ExecuteScriptAsync("window.__hallowGestOff&&__hallowGestOff()"); } catch { } w.gestureScripts.Remove(c); } }
                }
        }
        void OnGesture(Tab t, string dirs)
        {
            if (!S.Gestures || t == null) return;
            var a = Gestures.Action(dirs); LastGesture = dirs + "=" + (a ?? "none"); if (a == null) return;
            GesturesDone++;
            var c = t.Core;
            if (a == "back" && c != null && c.CanGoBack) c.GoBack();
            else if (a == "forward" && c != null && c.CanGoForward) c.GoForward();
            else if (a == "close") CloseTab(t);
        }
        public static int MenusRequested;   // tests: the engine's context menu was asked for (off-screen it's never shown)

        // ================================================================ the per-view part of 1.5 (with Hook)
        // a new view can start its first page before the scripts above are registered: put them in once more (each runs only once a page)
        void EnsurePageScripts(CoreWebView2 c)
        {
            try
            {
                var js = (Private ? "" : KeysLink.CaptureScript) + Media.Script + (S.Gestures ? Gestures.Script : "");
                // through DevTools without a user gesture: ExecuteScriptAsync counts as one, which let the page open a pop-up right after (measured)
                var _ = c.CallDevToolsProtocolMethodAsync("Runtime.evaluate", new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "expression", js }, { "userGesture", false } }));
            }
            catch { }
        }
        void Hook15(CoreWebView2 c)
        {
            if (!Private) { var _ = c.AddScriptToExecuteOnDocumentCreatedAsync(KeysLink.CaptureScript); }
            var __ = c.AddScriptToExecuteOnDocumentCreatedAsync(Media.Script);
            if (S.Gestures) AddGestures(c);
            if (BrowserSettings.Offscreen) c.ContextMenuRequested += (a, e) => { MenusRequested++; e.Handled = true; };   // tests: never a menu on screen
        }
    }
}

namespace HallowBrowser
{
    // --snap: made-up pictures of 1.5's parts (never real pages, never the real downloads list)
    public partial class BrowserWindow
    {
        public void SnapOmni()
        {
            foreach (var m in content.Children.OfType<FrameworkElement>().Where(x => (x.Tag as string) == "mock").ToList()) content.Children.Remove(m);
            var mock = Panels.MockPage("news"); mock.Tag = "mock"; content.Children.Add(mock);
            Address.Text = "5 km in miles";
            S.Typed = new List<string> { "https://www.example.com/5k-training-plan" };
            var list = new StackPanel();
            foreach (var r in BuildSuggestions("5 km in miles"))
            {
                var icon = r.Kind == "calc" ? "calculator" : r.Kind == "history" ? "globe" : "magnifying-glass";
                var row = r.Kind == "history" && Omni.IsWeb(r.Fill) ? SiteIcons.MenuRow(r.Fill, r.Text, null, null) : Ui.MenuRow(icon, r.Text, r.Kind == "calc" ? "copy" : null, null);
                if (r.Kind == "calc") ((Grid)row.Content).Children.OfType<TextBlock>().First().FontWeight = FontWeights.SemiBold;
                list.Children.Add(row);
            }
            foreach (var s in new[] { "5 km in miles and minutes", "5 km run time" }) list.Children.Insert(list.Children.Count - 1, Ui.MenuRow("magnifying-glass", s, null, null));
            var fl = Ui.Flyout(list, 620); fl.HorizontalAlignment = HorizontalAlignment.Left; fl.VerticalAlignment = VerticalAlignment.Top; fl.Margin = new Thickness(118, 4, 0, 0); fl.Tag = "mock";
            content.Children.Add(fl);
        }
        public void SnapDownloads()
        {
            foreach (var m in content.Children.OfType<FrameworkElement>().Where(x => (x.Tag as string) == "mock").ToList()) content.Children.Remove(m);
            var dir = @"C:\Users\you\Downloads\";
            DownloadHistory.Use(new List<DownloadRecord> {
                new DownloadRecord { Name = "season-3-map-pack.zip", File = dir + "season-3-map-pack.zip", State = "done", Scan = "clean", Size = 734003200 },
                new DownloadRecord { Name = "free-trainer-v2.exe", File = dir + "free-trainer-v2.exe", State = "done", Scan = "threat", ScanNote = "Trojan:Win32/Example (made up)", Size = 3355443 },
                new DownloadRecord { Name = "patch-notes.pdf", File = dir + "patch-notes.pdf", State = "done", Scan = "clean", Size = 1258291 },
                new DownloadRecord { Name = "old-beta-client.zip", File = dir + "old-beta-client.zip", State = "cancelled", Size = 0 } });
            var page = DownloadsPage(); page.Tag = "mock"; content.Children.Add(page);
            var t = Tabs[0]; t.Url = Omni.DownloadsUrl; t.Title = "Downloads"; t.TitleBlock.Text = t.Title; SetTabIcon(t); Activate(t); Address.Text = Omni.DownloadsUrl;
            mediaBtn.Visibility = Visibility.Visible;
        }
    }
}
