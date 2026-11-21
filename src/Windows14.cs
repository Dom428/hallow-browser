// Hallow Browser 1.4 - real windows: Ctrl+N opens another window, a tab dragged out of the strip becomes its own window (or goes
// into another window's strip), each window keeps its own tabs (and its own workspace) in the session, and the last window closing
// quits. Private windows (Ctrl+Shift+N) run on a separate InPrivate WebView2 profile and keep nothing: no history, no session,
// no bookmark or sync writes (downloads are still kept).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    // Shield's state for a profile (all normal windows share one; private windows share another)
    public class ShieldBox { public string State = "starting", Id, Version, Folder, Note; public ShieldHost.Rules Fallback; public int Blocked; public bool ProfileDone; }

    // one more window in session.json (the first window's tabs stay at the top, as in 1.3)
    [DataContract]
    public class WindowSession
    {
        [DataMember] public string Space;        // its workspace
        [DataMember] public Session Session;
        [DataMember] public double Left = double.NaN, Top = double.NaN, Width, Height;
        [DataMember] public bool Maximized;
    }

    // where a dragged tab goes when the mouse button is let go (all in screen pixels)
    public static class TearRules
    {
        public const double Margin = 40;
        // -2 = it stays in its own strip (a reorder), -1 = a new window, n = into window n's strip
        public static int Decide(Point p, Rect own, IList<Rect> others, double margin = Margin)
        {
            if (!own.IsEmpty) { var near = own; near.Inflate(margin, margin); if (near.Contains(p)) return -2; }
            for (int i = 0; i < others.Count; i++) { var r = others[i]; if (r.IsEmpty) continue; r.Inflate(8, 14); if (r.Contains(p)) return i; }
            return -1;
        }
        // the slot for a drop at x (or y, for vertical tabs) among the tab headers (their screen rects, in order)
        public static int Slot(double at, IList<Rect> heads, bool vertical)
        {
            for (int i = 0; i < heads.Count; i++) { var r = heads[i]; if (r.IsEmpty) continue; if (at < (vertical ? r.Top + r.Height / 2 : r.Left + r.Width / 2)) return i; }
            return heads.Count;
        }
    }

    public static partial class Program
    {
        public static readonly List<BrowserWindow> Windows = new List<BrowserWindow>();
        static BrowserWindow lastActive;
        // the window commands from the family pipe and Hallow apps go to: the last one you used (a normal one first)
        public static BrowserWindow Win
        {
            get
            {
                if (lastActive != null && !lastActive.Gone && Windows.Contains(lastActive) && !lastActive.Private) return lastActive;
                return First ?? Windows.FirstOrDefault(x => !x.Gone) ?? Windows.FirstOrDefault();
            }
        }
        // the first normal window: its tabs are session.json's own, it runs sync and live notifications
        public static BrowserWindow First { get { return Windows.FirstOrDefault(x => !x.Private && !x.Gone); } }
        public static readonly ShieldBox MainShield = new ShieldBox(), PrivateShield = new ShieldBox();
        public static WorkspaceStore SharedSpaces; public static HistoryStore SharedHistory; public static BookmarkStore SharedBookmarks;
        public static bool Quitting, SessionFrozen;

        public static void Register(BrowserWindow w)
        {
            Windows.Add(w);
            w.Activated += (a, b) => { if (!w.Gone) lastActive = w; };
            w.Closed += (a, b) =>
            {
                w.Gone = true; Windows.Remove(w); if (lastActive == w) lastActive = null;
                if (Windows.Count == 0) { Application.Current.Shutdown(); return; }
                var m = First; if (m != null && !m.IsPrimary) m.Promote();
                var s = Win; if (s != null) s.WriteStatus();
            };
        }
        public static BrowserWindow WindowOf(CoreWebView2 c)
        {
            if (c == null) return null;
            foreach (var w in Windows) foreach (var t in w.Tabs) if (t.View != null && t.Core == c) return w;
            return null;
        }

        // Ctrl+N / Ctrl+Shift+N, a torn-off tab, a window from last time
        public static BrowserWindow OpenWindow(bool priv, string url, Tab moved, BrowserWindow from = null, Point? atDip = null, Session session = null, string space = null, WindowSession place = null)
        {
            var cur = from ?? Win; var s = cur != null ? cur.S : BrowserSettings.Load();
            var w = new BrowserWindow(s, false, priv);
            if (!BrowserSettings.Offscreen)
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                Rect b = cur == null ? new Rect(100, 100, s.Width, s.Height) : cur.WindowState == WindowState.Normal ? new Rect(cur.Left, cur.Top, cur.Width, cur.Height) : cur.RestoreBounds;
                if (place != null && !double.IsNaN(place.Left) && !double.IsNaN(place.Top) && place.Width >= 480 && place.Height >= 360) b = new Rect(place.Left, place.Top, place.Width, place.Height);
                else if (atDip != null) b = new Rect(atDip.Value.X - 120, atDip.Value.Y - 18, b.Width, b.Height);
                else b.Offset(32, 32);
                var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (b.Left > vs.Right - 120 || b.Top > vs.Bottom - 80 || b.Right < vs.Left + 120 || b.Top < vs.Top - 10) { b.X = vs.Left + 80; b.Y = vs.Top + 60; }
                w.Left = b.Left; w.Top = b.Top; w.Width = Math.Max(480, b.Width); w.Height = Math.Max(360, b.Height);
                if (place != null && place.Maximized) w.WindowState = WindowState.Maximized;
            }
            if (!priv) w.SpaceId = space ?? NewAutoSpace();
            Register(w);
            var urls = url == null ? new List<string>() : new List<string> { url };
            w.Loaded += async (a, b) => await w.StartExtra(session, urls, moved);
            w.Show();
            if (!BrowserSettings.Offscreen) w.Activate();
            return w;
        }
        // a new window's own workspace ("Window 2"): it goes away with the window unless you switch it to another workspace first
        public static string NewAutoSpace()
        {
            var sp = SharedSpaces; if (sp == null) return null;
            var ws = new Workspace { Id = WorkspaceRules.NewId(), Name = WorkspaceRules.UniqueName("Window " + (Windows.Count(x => !x.Private) + 1), sp.Items.Select(x => x.Name)), Auto = true };
            sp.Items.Add(ws); BrowserSettings.SaveWorkspaces(sp);
            return ws.Id;
        }
        public static BrowserWindow WindowWithSpace(string id, BrowserWindow except) { return Windows.FirstOrDefault(x => x != except && !x.Private && !x.Gone && x.SpaceId == id); }

        // session.json: the first window's tabs (as 1.3 wrote them) and the other windows; each window's workspace keeps its copy
        public static void SaveAll()
        {
            if (SessionFrozen) return;
            var normals = Windows.Where(x => !x.Private && !x.Gone && !x.IsSnap && x.Tabs.Count > 0).ToList(); if (normals.Count == 0) return;
            var sessions = normals.Select(w => w.BuildSession()).ToList();
            var file = new Session { Tabs = sessions[0].Tabs, Active = sessions[0].Active, Groups = sessions[0].Groups, Split = sessions[0].Split };
            if (normals.Count > 1)
                file.Windows = normals.Skip(1).Select((w, i) => { var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.Width, w.Height) : w.RestoreBounds; bool off = BrowserSettings.Offscreen;
                    return new WindowSession { Space = w.SpaceId, Session = sessions[i + 1], Left = off ? double.NaN : r.Left, Top = off ? double.NaN : r.Top, Width = r.Width, Height = r.Height, Maximized = w.WindowState == WindowState.Maximized }; }).ToList();
            BrowserSettings.SaveSession(file);
            var sp = SharedSpaces;
            if (sp != null)
            {
                for (int i = 0; i < normals.Count; i++) { var ws = normals[i].CurrentSpace; if (ws != null) ws.Session = sessions[i]; }
                if (normals[0].SpaceId != null) sp.Current = normals[0].SpaceId;
                BrowserSettings.SaveWorkspaces(sp);
            }
        }
        // a window (not the last) closes: its tabs go with it, and its own "Window n" workspace too
        public static void Forget(BrowserWindow w)
        {
            var sp = SharedSpaces; var ws = w.CurrentSpace;
            if (sp != null && ws != null) { if (ws.Auto) sp.Items.Remove(ws); else ws.Session = w.BuildSession(); }
            SaveAll();
            if (sp != null) BrowserSettings.SaveWorkspaces(sp);
        }
        public static void RestoreWindows(List<WindowSession> list)
        {
            if (list == null || SharedSpaces == null) return;
            foreach (var x in list.Where(x => x != null && x.Session != null && x.Session.Tabs != null && x.Session.Tabs.Count > 0).Take(12))
            {
                var id = x.Space;
                if (id == null || !SharedSpaces.Items.Any(s => s.Id == id) || WindowWithSpace(id, null) != null) id = null;
                OpenWindow(false, null, null, First, null, x.Session, id, x);
            }
        }
        // the menu's Exit: every window's tabs are kept for next time, then all close
        public static void Quit()
        {
            SaveAll(); SessionFrozen = true; Quitting = true;
            foreach (var w in Windows.ToList()) try { w.Close(); } catch { }
        }

        // a tab from one window into another (never between private and normal windows: different profiles)
        public static bool MoveTab(Tab t, BrowserWindow from, BrowserWindow to, int index)
        {
            if (t == null || from == null || to == null || from == to || from.Private != to.Private || !from.Tabs.Contains(t)) return false;
            from.Detach(t);
            to.Adopt(t, index);
            if (from.Tabs.Count == 0) from.Close();
            SaveAll(); return true;
        }
        public static BrowserWindow TearOff(Tab t, BrowserWindow from, Point? atDip)
        {
            if (t == null || from == null || from.Tabs.Count < 2 || !from.Tabs.Contains(t)) return null;
            from.Detach(t);
            return OpenWindow(from.Private, null, t, from, atDip);
        }
    }

    public partial class BrowserWindow
    {
        public bool Gone, IsPrimary;
        public bool IsSnap { get { return snap; } }
        public const string PrivateProfile = "HallowPrivate";
        bool privApplied;
        static ShieldHost.Rules privateRules;

        // the WebView's engine: one environment; normal windows on the browser's profile, private ones on a separate InPrivate profile
        Task EnsureCore(WebView2 v)
        {
            if (!Private) return v.EnsureCoreWebView2Async(Engine.Env);
            var o = Engine.Env.CreateCoreWebView2ControllerOptions(); o.ProfileName = PrivateProfile; o.IsInPrivateModeEnabled = true;
            return v.EnsureCoreWebView2Async(Engine.Env, o);
        }

        // a window opened after the first one (Ctrl+N, Ctrl+Shift+N, a torn-off tab, one from last time)
        public async Task StartExtra(Session session, List<string> urls, Tab moved)
        {
            if (snap) return;
            try { await (Engine.Starting ?? Engine.Create(S)); }
            catch (Exception e) { Toast("The page engine (Microsoft Edge WebView2) didn't start: " + e.Message, 15); return; }
            if (Private) await PrivateShieldAsync();
            else LoadSpaces();
            if (moved != null) Adopt(moved, 0);
            else await OpenSession(session, urls ?? new List<string>(), true);
            tick.Start(); Tick(true);
            var _ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(900); MakePrewarm(); if (!Private) { await Task.Delay(300); EnsureStores(); } }), DispatcherPriority.ApplicationIdle);
            SaveSession(); WriteStatus();
        }
        // the first normal window closed while others are open: the next one takes over sync and live notifications
        public void Promote()
        {
            if (IsPrimary || Private || snap) return;
            IsPrimary = true;
            if (Live == null) { Live = new LiveWatcher(this); Live.Start(); }
            if (Bookmarks != null && Bookmarks.Loaded) StartSync();
        }

        // ---- a tab leaves this window / arrives from another
        public void Detach(Tab t)
        {
            if (t == null || !Tabs.Contains(t)) return;
            if (t.PoppedOut) Pip.Close(this, t, true);
            if (t.ReaderOn) ReaderOff(t);
            if (t == SplitL || t == SplitR) SplitL = SplitR = null;
            int i = Tabs.IndexOf(t); Tabs.Remove(t); TabPanel.Children.Remove(t.Header);
            if (t.View != null) { content.Children.Remove(t.View); var c = t.Core; if (c != null) { owner.Remove(c); capped.Remove(c); } }
            if (t.Page != null) { content.Children.Remove(t.Page); t.Page = null; t.Discarded = true; }   // internal pages are rebuilt by the window they go to
            if (t.ReaderView != null) { content.Children.Remove(t.ReaderView); t.ReaderView = null; }
            t.Group = null;
            if (Tabs.Count > 0) { if (Active == t) Activate(Tabs[Math.Min(i, Tabs.Count - 1)]); }
            else Active = null;
            RenderStrip(); LayoutSplit(); Changed();
        }
        public void Adopt(Tab t, int index)
        {
            int at = index < 0 || index > Tabs.Count ? Tabs.Count : index;
            AddHeader(t, at); Tabs.Insert(at, t);
            if (t.View != null)
            {
                var v = t.View; Grid.SetColumn(v, 0); content.Children.Add(v);
                v.GotFocus += (a, b) => ViewFocused(Tabs.FirstOrDefault(x => x.View == v));
                var c = t.Core; if (c != null) Rehook(t, c);
            }
            else if (t.Internal) t.Discarded = true;
            RenderStrip(); Activate(t); LayoutSplit(); Changed();
        }
        public int SlotAt(Point screen)
        {
            var heads = Tabs.Select(x => x.Header.Visibility == Visibility.Visible ? ScreenRect(x.Header) : Rect.Empty).ToList();
            return TearRules.Slot(S.VerticalTabs ? screen.Y : screen.X, heads, S.VerticalTabs);
        }
        FrameworkElement TabsArea { get { return S.VerticalTabs ? (FrameworkElement)vtabs : stripHost; } }
        public Rect TabsScreenRect { get { return ScreenRect(TabsArea); } }
        static Rect ScreenRect(FrameworkElement e)
        {
            try { if (e == null || !e.IsVisible || PresentationSource.FromVisual(e) == null) return Rect.Empty; var a = e.PointToScreen(new Point(0, 0)); var b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight)); return new Rect(a, b); }
            catch { return Rect.Empty; }
        }
        Point ToDip(Point screen) { try { var src = PresentationSource.FromVisual(this); return src == null ? screen : src.CompositionTarget.TransformFromDevice.Transform(screen); } catch { return screen; } }
        // while dragging: outside the strip there's nothing to reorder
        bool OutsideStrip(MouseEventArgs e)
        {
            try { var p = ((UIElement)e.Source).PointToScreen(e.GetPosition((UIElement)e.Source)); var r = TabsScreenRect; if (r.IsEmpty) return false; r.Inflate(TearRules.Margin, TearRules.Margin); return !r.Contains(p); } catch { return false; }
        }
        // the drop: into another window's strip, a new window, or (inside the strip) the usual reorder
        bool DropOutside(Tab t, MouseEventArgs e)
        {
            Point p; try { p = t.Header.PointToScreen(e.GetPosition(t.Header)); } catch { return false; }
            var others = Program.Windows.Where(x => x != this && !x.Gone && x.Private == Private && x.IsVisible && x.WindowState != WindowState.Minimized).ToList();
            int d = TearRules.Decide(p, TabsScreenRect, others.Select(x => x.TabsScreenRect).ToList());
            if (d == -2) return false;
            if (d >= 0) { dragActive = false; dropChip = null; var to = others[d]; Program.MoveTab(t, this, to, to.SlotAt(p)); to.Bring(); return true; }
            if (Tabs.Count < 2) return false;   // the only tab: nothing to tear off
            dragActive = false; dropChip = null;
            Program.TearOff(t, this, ToDip(p));
            return true;
        }

        // ================================================================ private windows
        // a dark purple look of their own (whatever the light / dark mode), a mask in the title bar
        public static readonly Dictionary<string, string> PrivatePalette = new Dictionary<string, string> {
            { "Bg", "#1B1029" }, { "Strip", "#130A1F" }, { "Bar", "#251538" }, { "Panel", "#251538" }, { "PanelHi", "#34204D" }, { "TabOn", "#251538" }, { "TabHover", "#1F1230" },
            { "FieldBg", "#130A1F" }, { "FieldFocus", "#0F0719" }, { "Line", "#4C3270" }, { "LineDim", "#382354" }, { "Text", "#F2EBFC" }, { "Muted", "#B9A9D2" }, { "Lilac", "#CDAEFF" }, { "Rail", "#130A1F" } };
        void PrivateLook()
        {
            foreach (var kv in PrivatePalette) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(kv.Value)); b.Freeze(); Resources[kv.Key] = b; }
            Resources["PageBg"] = (Color)ColorConverter.ConvertFromString(PrivatePalette["Bg"]);
        }
        Border privChip;
        void PrivateChrome()
        {
            wsBtn.Visibility = Visibility.Collapsed;
            var top = (Grid)root.Children[0];
            privChip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 10, 2), Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), ToolTip = "A private window: no history, no session, nothing saved or synced - only downloads are kept. Pages here don't see your sign-ins. Shield runs as its network-only fallback here." };
            privChip.Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x23, 0x7A)); privChip.BorderBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0x5C, 0xD6));
            var ic = Ui.Icon("mask", 13, "Text"); ic.Fill = Brushes.White; ic.Margin = new Thickness(0, 0, 6, 0);
            var tx = new TextBlock { Text = "PRIVATE", FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, FontFamily = (FontFamily)FindResource("MonoFont") };
            privChip.Child = Ui.Row(ic, tx);
            var logoRow = top.Children.OfType<StackPanel>().FirstOrDefault(); if (logoRow != null) logoRow.Children.Add(privChip);
            Side.ApplyOn(); ApplyBookmarksBar();
        }
        public void PrivateShield() { /* rules are loaded before the first page (StartExtra) */ }
        // WebView2 doesn't run browser extensions in InPrivate profiles (measured: Shield loads there but blocks nothing), so private
        // windows get Shield's network-only fallback - its block rules through WebResourceRequested
        async Task PrivateShieldAsync()
        {
            SB = Program.PrivateShield;
            if (SB.State == "fallback" && SB.Fallback != null) return;
            Version v; var folder = ShieldHost.Pick(out v);
            ShieldFolder = folder; ShieldVersion = v == null ? null : v.ToString(2);
            if (folder == null) { ShieldState = "missing"; ShieldNote = "Hallow Shield's files are missing - reinstall Hallow Browser."; return; }
            if (privateRules == null) privateRules = await Task.Run(() => ShieldHost.LoadRules(folder));
            Fallback = privateRules; ShieldState = "fallback";
            ShieldNote = "In private windows Shield runs as its network-only fallback (" + privateRules.Count + " block rules; no cosmetic filtering or per-site settings) - WebView2 doesn't run browser extensions in InPrivate profiles.";
        }
    }
}

namespace HallowBrowser
{
    // --snap: made-up pictures of 1.4's parts (never real pages)
    public partial class BrowserWindow
    {
        public void SnapPrivate()
        {
            foreach (var m in content.Children.OfType<FrameworkElement>().Where(x => (x.Tag as string) == "mock").ToList()) content.Children.Remove(m);
            var t = Tabs[0]; t.Url = Omni.PrivateTabUrl; t.Title = "New private tab"; t.TitleBlock.Text = t.Title; Activate(t);
            ShieldState = "fallback";
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 600 };
            var ic = Ui.Icon("mask", 58, "Lilac"); ic.Margin = new Thickness(0, 0, 0, 16); sp.Children.Add(ic);
            var h = Ui.Row(Ui.T("PRIVATE", 28, true, "Lilac"), Ui.T(" WINDOW", 28, false, "Text")); h.HorizontalAlignment = HorizontalAlignment.Center; h.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(h);
            foreach (var line in new[] { "Nothing from this window is kept: no history, no session, no cookies or site data after you close it, no bookmark or sync changes. Downloads stay in your Downloads folder.", "Sites here don't see your sign-ins. Your network, your work or school and the sites themselves can still see what you visit." })
            { var m = Ui.Muted(line, 13, 8); m.TextAlignment = TextAlignment.Center; m.TextWrapping = TextWrapping.Wrap; sp.Children.Add(m); }
            var box = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 14, 0, 0), Width = 560, Child = Ui.Muted("Search DuckDuckGo or type an address", 14) };
            box.SetResourceReference(Border.BackgroundProperty, "FieldBg"); box.SetResourceReference(Border.BorderBrushProperty, "Line"); sp.Children.Add(box);
            var bg = new Grid { Tag = "mock" }; bg.SetResourceReference(Panel.BackgroundProperty, "Bg"); bg.Children.Add(sp); content.Children.Add(bg);
            Address.Text = "";
        }
        FrameworkElement pendingHolder;
        public void SnapPage(string kind)
        {
            foreach (var m in content.Children.OfType<FrameworkElement>().Where(x => (x.Tag as string) == "mock").ToList()) content.Children.Remove(m);
            FrameworkElement page;
            if (kind == "extensions")
            {
                S.Extensions = new List<UserExtension> { new UserExtension { Id = "abcdefghijklmnopabcdefghijklmnop", Name = "Dark Mode Everywhere", Version = "4.9.2", Description = "Dark pages everywhere (a made-up extension for this picture).", Source = @"C:\Users\you\Downloads\dark-mode-4.9.2", Popup = "popup.html", Enabled = true },
                    new UserExtension { Id = "ponmlkjihgfedcbaponmlkjihgfedcba", Name = "Tab Notes", Version = "1.2", Description = "Notes per tab (made-up).", Source = @"D:\extensions\tab-notes", Enabled = false } };
                ShieldCounts = true; page = ExtensionsPage(); extBtn.Visibility = Visibility.Visible;
                var t = Tabs[0]; t.Url = Omni.ExtensionsUrl; t.Title = "Extensions"; t.TitleBlock.Text = t.Title; SetTabIcon(t);
            }
            else
            {
                S.Sites = new List<SiteRule> { new SiteRule { Host = "clips.example.tv", Popups = "allow" }, new SiteRule { Host = "news.example.com", Script = "block", Sound = "mute" } };
                page = SitesPage();
                var t = Tabs[0]; t.Url = Omni.SitesUrl; t.Title = "Site settings"; t.TitleBlock.Text = t.Title; SetTabIcon(t);
                // the permission prompt as it shows under the address bar
                asks.Add(new PermAsk { Host = "meet.example.com", Origin = "https://meet.example.com", Kind = Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Camera, Done = c => { } });
                ShowAsk(); var fl = askPop.Child; askPop.Child = null; asks.Clear();
                var holder = new Border { Child = fl, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(150, 6, 0, 0), Tag = "mock" };
                Dispatcher.BeginInvoke(new Action(() => { }));
                pendingHolder = holder;
            }
            page.Tag = "mock"; content.Children.Add(page);
            if (pendingHolder != null) { content.Children.Add(pendingHolder); pendingHolder = null; }
            Activate(Tabs[0]); Address.Text = kind == "extensions" ? Omni.ExtensionsUrl : Omni.SitesUrl;
        }
    }
}
