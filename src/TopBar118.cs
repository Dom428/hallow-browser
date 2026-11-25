// Hallow Browser 1.18 - a Brave-style top bar, in Hallow's look:
//  - the toolbar's buttons are yours to arrange: right-click the toolbar > "Customize toolbar" shows every button with a switch and
//    up / down (Home, Bookmarks, Downloads, Sidebar, Extensions, Shield, Game mode, Screenshot, Split view, Reader, Profile, Settings),
//    kept in settings.json (Toolbar: the shown buttons in order; null = the default, close to Brave's);
//  - the address bar shows a clean address while you aren't in it ("example.com/path" - no "https://", no leading "www."), the full one
//    when you click in; http pages say "Not secure"; page actions sit on its right only when they apply (zoom %, reader, translate,
//    install as app, share with copy-link and a QR code drawn here, pop out a playing video);
//  - Shield's button carries the number it blocked on this page (Shield's own per-tab count, read from a hidden page of Shield's);
//  - the puzzle piece lists your extensions with a pin: pinned ones get their own toolbar button that opens their popup;
//  - the downloads button draws a progress ring while downloads run, and its pop-up shows the latest five;
//  - a profile button (the profile's initial in its colour) opens the profile switcher; a tab-search button on the tab strip;
//  - the tab strip: tabs shrink evenly to a minimum, a narrow tab shows its close button on hover (and when active), and arrows scroll
//    the strip when the tabs don't fit.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        [DataMember] public List<string> Toolbar;            // 1.18: the toolbar's shown buttons in order (null or empty = the default)
        [DataMember] public List<string> PinnedExtensions;   // 1.18: extensions pinned to the toolbar (their IDs); null = none
    }

    public partial class Tab
    {
        public int ShieldBlocked;          // 1.18: what Shield blocked on this page (its per-tab count), 0 = nothing / not known yet
        public int FallbackBlockedHere;    // 1.18: blocked by the network-only fallback on this page
        public string ShieldCountedUrl;    // the address the count was last read for
        public bool Installable, InstalledApp;   // 1.18: the page has a web app manifest / it is installed already
    }

    // the toolbar's buttons: names, icons, the default layout and the edits the Customize panel makes
    public static class Toolbar118
    {
        public static readonly string[] All = { "home", "bookmarks", "downloads", "sidebar", "extensions", "shield", "game", "screenshot", "split", "reader", "profile", "settings" };
        public static readonly string[] Default = { "shield", "extensions", "downloads", "game", "settings", "profile" };
        public const string None = "none";   // saved when you hide every button (an empty list means "the default")
        public static string Name(string id)
        {
            switch (id)
            {
                case "home": return "Home"; case "bookmarks": return "Bookmarks"; case "downloads": return "Downloads"; case "sidebar": return "Sidebar";
                case "extensions": return "Extensions"; case "shield": return "Hallow Shield"; case "game": return "Game mode and Hallow panel"; case "screenshot": return "Screenshot";
                case "split": return "Split view"; case "reader": return "Reader mode"; case "profile": return "Profile"; case "settings": return "Settings";
            }
            return id;
        }
        public static string Icon(string id)
        {
            switch (id)
            {
                case "home": return "house"; case "bookmarks": return "bookmark"; case "downloads": return "download"; case "sidebar": return "rectangle-list";
                case "extensions": return "puzzle-piece"; case "shield": return "shield-halved"; case "game": return "gamepad"; case "screenshot": return "camera";
                case "split": return "table-columns"; case "reader": return "book-open"; case "profile": return "user"; case "settings": return "gear";
            }
            return "circle";
        }
        // the shown buttons, in order: unknown and repeated IDs dropped; null / empty = the default
        public static List<string> Layout(List<string> saved)
        {
            if (saved == null || saved.Count == 0) return Default.ToList();
            var r = new List<string>();
            foreach (var id in saved) if (id != null && Array.IndexOf(All, id) >= 0 && !r.Contains(id)) r.Add(id);
            if (r.Count == 0 && !saved.Contains(None)) return Default.ToList();
            return r;
        }
        public static List<string> Hidden(List<string> layout) { return All.Where(x => !layout.Contains(x)).ToList(); }
        public static List<string> Move(List<string> layout, string id, int dir)
        {
            var r = layout.ToList(); int i = r.IndexOf(id), j = i + dir;
            if (i < 0 || j < 0 || j >= r.Count) return r;
            r.RemoveAt(i); r.Insert(j, id); return r;
        }
        public static List<string> Show(List<string> layout, string id, bool on)
        {
            var r = layout.ToList();
            if (on && !r.Contains(id) && Array.IndexOf(All, id) >= 0) r.Add(id);
            if (!on) r.Remove(id);
            return r;
        }
        // what goes into settings.json: null for the default, ["none"] when nothing is shown
        public static List<string> ToSave(List<string> layout)
        {
            if (layout.SequenceEqual(Default)) return null;
            return layout.Count == 0 ? new List<string> { None } : layout.ToList();
        }
    }

    // the address bar's text while you aren't editing it
    public static class AddressText
    {
        // "https://www.example.com/a?b" -> "example.com/a?b"; "http://127.0.0.1:8080/" -> "127.0.0.1:8080"; other schemes as they are
        public static string Clean(string url)
        {
            var full = Omni.Display(url); if (full.Length == 0) return full;
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https") || !Omni.IsWeb(url)) return full;
            var host = u.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && host.Count(c => c == '.') >= 2) host = host.Substring(4);
            var auth = u.HostNameType == UriHostNameType.IPv6 ? RawAuthority(url) : host + (u.IsDefaultPort ? "" : ":" + u.Port);   // never the user name / password part
            var rest = u.PathAndQuery + u.Fragment;
            if (rest == "/") rest = "";
            return auth + rest;
        }
        // as typed: .NET writes IPv6 hosts out in full ("[0000:...:0001]")
        static string RawAuthority(string url)
        {
            var i = url.IndexOf("://", StringComparison.Ordinal) + 3; var e = url.IndexOfAny(new[] { '/', '?', '#' }, i); var a = e < 0 ? url.Substring(i) : url.Substring(i, e - i);
            var at = a.LastIndexOf('@'); if (at >= 0) a = a.Substring(at + 1);
            if (a.EndsWith(":80") && url.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || a.EndsWith(":443") && url.StartsWith("https:", StringComparison.OrdinalIgnoreCase)) a = a.Substring(0, a.LastIndexOf(':'));
            return a.ToLowerInvariant();
        }
        public static bool NotSecure(string url) { Uri u; return Omni.IsWeb(url) && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == "http"; }
        // the overall share of the downloads running now (0..1), -1 when none runs (or none has a known size)
        public static double Progress(IEnumerable<DownloadItem> items)
        {
            long got = 0, total = 0; bool any = false;
            foreach (var d in items) { if (d.State != "running" && d.State != "paused") continue; any = true; if (d.Total > 0) { total += d.Total; got += Math.Min(d.Got, d.Total); } }
            if (!any) return -1;
            return total <= 0 ? 0 : Math.Max(0, Math.Min(1, got / (double)total));
        }
    }

    // the tab strip's width rule (unit-tested): tabs share the room evenly between a minimum and a maximum; below the minimum the
    // strip scrolls
    public static class TabFit
    {
        public const double Max = 230, Min = 64, Narrow = 104, IconOnly = 76, Arrows = 26;
        public static double Width(double room, int tabs, double chips, out bool overflow)
        {
            overflow = false; if (tabs <= 0) return 200;
            double w = (room - chips) / tabs - 1;
            if (w < Min) { overflow = true; return Min; }
            return Math.Min(Max, w);
        }
    }

    public partial class BrowserWindow
    {
        StackPanel tbLeft, tbRight, extHost, pinHost; StackPanel lastTbMenu;
        readonly Dictionary<string, FrameworkElement> tbItems = new Dictionary<string, FrameworkElement>();
        Button homeBtn, profBtn, installBtn, shareBtn, pipBtn, tabSearchBtn, scrollL, scrollR;
        Border shieldBadge; TextBlock shieldBadgeText, notSecureText;
        Path dlRing; System.Windows.Shapes.Ellipse dlRingBg; Path dlIcon;
        ScrollViewer stripScroll; bool narrowTabs, stripOverflow;
        public static string LastCopied;   // tests: what "Copy link" would have put on the clipboard (tests never touch your clipboard)

        // ================================================================ building (from Build)
        FrameworkElement ShieldButtonContent()
        {
            var g = new Grid { Width = 18, Height = 18 };
            g.Children.Add(Ui.Icon("shield-halved", 14, "AccentBrush"));
            shieldBadgeText = new TextBlock { FontSize = 8.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
            shieldBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
            shieldBadge = new Border { MinWidth = 13, Height = 12, CornerRadius = new CornerRadius(6), Padding = new Thickness(3, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -9, -5), Child = shieldBadgeText, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            shieldBadge.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); shieldBadge.SetResourceReference(Border.BorderBrushProperty, "Bar"); shieldBadge.BorderThickness = new Thickness(1);
            g.Children.Add(shieldBadge);
            return g;
        }
        FrameworkElement DownloadButtonContent()
        {
            var g = new Grid { Width = 22, Height = 22 };
            dlRingBg = new System.Windows.Shapes.Ellipse { Width = 22, Height = 22, StrokeThickness = 2, Visibility = Visibility.Collapsed }; dlRingBg.SetResourceReference(Shape.StrokeProperty, "LineDim");
            dlRing = new Path { StrokeThickness = 2, Width = 22, Height = 22, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed }; dlRing.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            dlIcon = Ui.Icon("download", 14); dlIcon.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(dlRingBg); g.Children.Add(dlRing); g.Children.Add(dlIcon);
            dlBadge = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, -2, 0), Visibility = Visibility.Collapsed };
            dlBadge.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); g.Children.Add(dlBadge);
            return g;
        }
        void BuildToolbar118(StackPanel left, StackPanel right)
        {
            tbLeft = left; tbRight = right;
            homeBtn = Ui.Tool("house", "Home (Alt+Home)", GoHome, 14);
            var bmBtn = Ui.Tool("bookmark", "Bookmarks (Ctrl+Shift+O)", ShowBookmarks, 13);
            var sideBtn = Ui.Tool("rectangle-list", "Show or hide the sidebar (Ctrl+Shift+S)", () => Side.Toggle(), 14);
            Button shotBtn = null; shotBtn = Ui.Tool("camera", "Screenshot (Ctrl+Shift+X)", () => Shots.Flyout(this, shotBtn), 14);
            var splitBtn = Ui.Tool("table-columns", "Split view with the next tab (Ctrl+click a tab)", ToggleSplit118, 14);
            var readBtn = Ui.Tool("book-open", "Reader mode (F9)", () => ToggleReader(Active), 14);
            profBtn = new Button { Style = Ui.S("Tool"), ToolTip = "Profile - switch or add a profile" }; profBtn.Click += (a, b) => ProfileFlyout(profBtn);
            pinHost = new StackPanel { Orientation = Orientation.Horizontal };
            extHost = Ui.Row(pinHost, extBtn);
            tbItems["home"] = homeBtn; tbItems["bookmarks"] = bmBtn; tbItems["downloads"] = dlBtn; tbItems["sidebar"] = sideBtn; tbItems["extensions"] = extHost;
            tbItems["shield"] = shieldBtn; tbItems["game"] = quickBtn; tbItems["screenshot"] = shotBtn; tbItems["split"] = splitBtn; tbItems["reader"] = readBtn;
            tbItems["profile"] = profBtn; tbItems["settings"] = settingsBtn;
            ApplyToolbar();
        }
        void GoHome() { var home = Private ? Omni.PrivateTabUrl : Omni.NewTabUrl; if (Active == null || Active.Internal || AppMode) NewTab(home, true); else Go(Active, home); }
        void ToggleSplit118() { if (SplitOn) CloseSplit(); else if (Active != null && Tabs.Count > 1) AddToSplit(Tabs[(Tabs.IndexOf(Active) + 1) % Tabs.Count]); else Toast("Open a second tab to see two side by side.", 3); }
        bool ToolbarAllowed(string id)
        {
            if (id == "extensions") return !Private && !AppMode;   // extensions don't run in private windows
            if (id == "profile") return !Private;
            return true;
        }
        // the toolbar as the layout says: Home on the left after reload, the rest on the right (media first, the menu always last)
        public void ApplyToolbar()
        {
            if (tbRight == null) return;
            var layout = Toolbar118.Layout(S.Toolbar);
            if (homeBtn.Parent == null) tbLeft.Children.Add(homeBtn);
            homeBtn.Visibility = layout.Contains("home") ? Visibility.Visible : Visibility.Collapsed;
            tbRight.Children.Clear();
            tbRight.Children.Add(mediaBtn);
            foreach (var id in layout) { if (id == "home" || !ToolbarAllowed(id)) continue; var e = tbItems[id]; e.Visibility = Visibility.Visible; tbRight.Children.Add(e); }
            tbRight.Children.Add(menuBtn);
            extBtn.Visibility = Visibility.Visible;
            RefreshPins(); RefreshProfileButton();
        }
        public static void ApplyToolbarAll() { foreach (var w in Program.Windows.ToList()) if (!w.Gone) w.ApplyToolbar(); }
        public List<string> ToolbarShown()
        {
            var r = new List<string>();
            if (homeBtn != null && homeBtn.Visibility == Visibility.Visible && homeBtn.Parent != null) r.Add("home");
            foreach (UIElement e in tbRight.Children) { var id = tbItems.FirstOrDefault(kv => kv.Value == e).Key; if (id != null && e.Visibility == Visibility.Visible) r.Add(id); }
            return r;
        }
        // a flyout's anchor: the button when it's on the toolbar, else the menu button
        UIElement Anchor(FrameworkElement e) { return e != null && e.IsVisible ? (UIElement)e : menuBtn; }

        // ---- right-click on the toolbar: hide this button / Customize toolbar
        void ToolbarRightClick(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            for (var d = src; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d)) if (d == addressBox) return;   // the address bar has its own menu
            string id = null;
            for (var d = src; d != null && id == null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d)) foreach (var kv in tbItems) if (kv.Value == d) id = kv.Key;
            var sp = new StackPanel(); Popup pop = null;
            Action<Action> run = a => { pop.IsOpen = false; a(); };
            if (id != null) { var hid = id; sp.Children.Add(Ui.MenuRow("eye-slash", "Hide " + Toolbar118.Name(id), null, () => run(() => SetToolbarShown(hid, false)))); }
            sp.Children.Add(Ui.MenuRow("sliders", "Customize toolbar...", null, () => run(() => CustomizeToolbar(null))));
            Panels.CloseFlyouts();
            lastTbMenu = sp; LastToolbarMenu = SettingsLayout.TextOf(sp);
            pop = new Popup { Child = Ui.Flyout(sp, 250), PlacementTarget = (UIElement)sender, Placement = PlacementMode.MousePoint, StaysOpen = false, AllowsTransparency = true };
            pop.IsOpen = true; e.Handled = true;
        }
        public void SetToolbarShown(string id, bool on) { SaveToolbar(Toolbar118.Show(Toolbar118.Layout(S.Toolbar), id, on)); }
        public void MoveToolbarButton(string id, int dir) { SaveToolbar(Toolbar118.Move(Toolbar118.Layout(S.Toolbar), id, dir)); }
        public void ResetToolbar() { SaveToolbar(Toolbar118.Default.ToList()); }
        void SaveToolbar(List<string> layout)
        {
            S.Toolbar = Toolbar118.ToSave(layout); S.Save();
            foreach (var w in Program.Windows.ToList()) if (w != this && !w.Gone) w.S.Toolbar = S.Toolbar == null ? null : S.Toolbar.ToList();
            ApplyToolbarAll(); if (!Program.Windows.Contains(this)) ApplyToolbar();
        }

        // ---- Customize toolbar: every button, a switch and up / down; Reset to default
        public void CustomizeToolbar(UIElement at)
        {
            var host = new StackPanel();
            FillCustomize(host);
            Panels.ShowFlyout(at ?? Anchor(menuBtn), host, 340);
        }
        public StackPanel CustomizePanel() { var host = new StackPanel(); FillCustomize(host); return host; }
        void FillCustomize(StackPanel host)
        {
            host.Children.Clear();
            var head = Ui.Label("Customize toolbar"); head.Margin = new Thickness(12, 8, 12, 2); host.Children.Add(head);
            var sub = Ui.Muted("Switch buttons on or off and move them. The menu stays at the end.", 11); sub.Margin = new Thickness(12, 0, 12, 8); sub.TextWrapping = TextWrapping.Wrap; host.Children.Add(sub);
            var layout = Toolbar118.Layout(S.Toolbar);
            Action refill = () => FillCustomize(host);
            foreach (var id in layout.Concat(Toolbar118.Hidden(layout)))
            {
                var iid = id; bool on = layout.Contains(id); int at = layout.IndexOf(id);
                var g = new Grid { Margin = new Thickness(12, 2, 6, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = Ui.Row(Ui.Icon(Toolbar118.Icon(id), 13, on ? "Lilac" : "Muted"), new TextBlock { Text = "  " + Toolbar118.Name(id) + (id == "home" ? " (left of the address bar)" : ""), VerticalAlignment = VerticalAlignment.Center });
                var sw = new CheckBox { Content = label, IsChecked = on, Style = Ui.S("Switch"), Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Automation.AutomationProperties.SetName(sw, "Show " + Toolbar118.Name(id));
                sw.Click += (a, b) => { SetToolbarShown(iid, sw.IsChecked == true); refill(); };
                g.Children.Add(sw);
                var up = Ui.Tool("chevron-up", "Move " + Toolbar118.Name(id) + " left", () => { MoveToolbarButton(iid, -1); refill(); }, 10); up.Width = 26; up.Height = 24; up.IsEnabled = on && at > 0; Grid.SetColumn(up, 1); g.Children.Add(up);
                var dn = Ui.Tool("chevron-down", "Move " + Toolbar118.Name(id) + " right", () => { MoveToolbarButton(iid, 1); refill(); }, 10); dn.Width = 26; dn.Height = 24; dn.IsEnabled = on && at >= 0 && at < layout.Count - 1; Grid.SetColumn(dn, 2); g.Children.Add(dn);
                g.Tag = "tb:" + id;
                host.Children.Add(g);
            }
            var line = new Border { Height = 1, Margin = new Thickness(6, 6, 6, 6) }; line.SetResourceReference(Border.BackgroundProperty, "LineDim"); host.Children.Add(line);
            var reset = Ui.Btn("Reset to default", () => { ResetToolbar(); refill(); }, false, "rotate-left"); reset.Margin = new Thickness(12, 0, 12, 8); reset.HorizontalAlignment = HorizontalAlignment.Left;
            host.Children.Add(reset);
        }

        // ================================================================ the address bar: clean when you aren't in it
        void AddressFull()
        {
            if (Active == null) return;
            var clean = AddressText.Clean(Active.Url);
            if (Address.Text == clean) { suggestFilling = true; try { Address.Text = Omni.Display(Active.Url); } finally { suggestFilling = false; } }
        }
        void AddressClean()
        {
            if (Active == null) return;
            if (Address.Text == Omni.Display(Active.Url)) { suggestFilling = true; try { Address.Text = AddressText.Clean(Active.Url); } finally { suggestFilling = false; } }
        }
        public string AddressShown { get { return Address.Text; } }
        public Dictionary<string, object> AddressState(string what)
        {
            if (what == "focus") { Address.Focus(); if (!Address.IsKeyboardFocusWithin) AddressFull(); }   // off-screen windows never get the keyboard: the same step the focus event runs
            if (what == "blur") { if (Address.IsKeyboardFocusWithin && Active != null && Active.View != null) Active.View.Focus(); if (!Address.IsKeyboardFocusWithin) AddressClean(); suggest.IsOpen = false; }
            return new Dictionary<string, object> { { "text", Address.Text }, { "focused", Address.IsKeyboardFocusWithin }, { "notSecure", notSecureText != null && notSecureText.Visibility == Visibility.Visible }, { "url", Active == null ? null : Active.Url } };
        }

        // ================================================================ page actions (inside the address bar, only when they apply)
        static Button PageAction(string icon, string tip, Action click, double size = 13) { var b = Ui.Tool(icon, tip, click, size); b.Height = 24; b.Width = 28; b.Visibility = Visibility.Collapsed; return b; }
        void BuildPageActions118(Panel inBar)
        {
            installBtn = PageAction("square-plus", "Install this site as an app", () => { var _ = InstallFromBar(); });
            shareBtn = PageAction("share-nodes", "Share this page: copy the link or show a QR code", () => ShareFlyout(shareBtn), 12);
            pipBtn = PageAction("picture-in-picture", "Pop out the video (stays on top)", () => { if (Active != null) Pip.PopOut(this, Active); });
            int at = inBar.Children.IndexOf(starBtn); if (at < 0) at = inBar.Children.Count;
            inBar.Children.Insert(at, pipBtn); inBar.Children.Insert(at, shareBtn); inBar.Children.Insert(at, installBtn);
            zoomChip.ToolTip = "Zoom - click to reset to 100%";
        }
        async Task InstallFromBar()
        {
            var t = Active; if (t == null) return;
            var app = await InstallSite(t);
            if (app != null) { t.InstalledApp = true; if (t == Active) UpdatePageActions(t); }
        }
        void UpdatePageActions(Tab t)
        {
            if (installBtn == null || t == null) return;
            bool web = Omni.IsWeb(t.Url) && !t.Internal && !AppMode;
            installBtn.Visibility = web && !Private && t.Installable && !t.InstalledApp ? Visibility.Visible : Visibility.Collapsed;
            shareBtn.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
            pipBtn.Visibility = web && (t.Audible || t.MediaSeen) && !t.PoppedOut && t.Core != null ? Visibility.Visible : Visibility.Collapsed;
            var ns = AddressText.NotSecure(t.Url) && t.HttpsBlocked == null;
            if (notSecureText != null) notSecureText.Visibility = ns ? Visibility.Visible : Visibility.Collapsed;
        }
        public List<string> PageActionsShown()
        {
            var r = new List<string>();
            if (zoomChip.Visibility == Visibility.Visible) r.Add("zoom");
            if (readerBtn.Visibility == Visibility.Visible) r.Add("reader");
            if (TranslateButtonVisible) r.Add("translate");
            if (installBtn.Visibility == Visibility.Visible) r.Add("install");
            if (shareBtn.Visibility == Visibility.Visible) r.Add("share");
            if (pipBtn.Visibility == Visibility.Visible) r.Add("pip");
            if (starBtn.Visibility == Visibility.Visible) r.Add("bookmark");
            return r;
        }
        public void ClickZoomAction() { zoomChip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        // the manifest check after a page loads (one small script; never in a private window or an app window)
        async Task CheckInstallable(Tab t, CoreWebView2 c)
        {
            if (Private || AppMode || snap) return;
            var url = t.Url;
            try
            {
                var r = await Panels.Eval(c, "!!document.querySelector('link[rel~=\"manifest\"]')");
                if (t.Url != url) return;
                t.Installable = r == "true";
                t.InstalledApp = t.Installable && await Task.Run(() => SiteApps.ForUrl(url) != null);
            }
            catch { }
            if (t == Active) UpdatePageActions(t);
        }

        // ---- Share: the link to copy and a QR code drawn here
        public FrameworkElement SharePanel(Tab t, Action close)
        {
            var sp = new StackPanel { Margin = new Thickness(6, 4, 6, 6) };
            sp.Children.Add(Ui.Label("Share this page"));
            var title = Ui.T(t.Title ?? "", 13, true, "Text", 4); title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; sp.Children.Add(title);
            var box = new TextBox { Style = Ui.S("Field"), Text = t.Url, IsReadOnly = true, Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(box, "The page's address");
            sp.Children.Add(box);
            var copy = Ui.Btn("Copy link", () => { CopyText(t.Url); Toast("Link copied.", 2); if (close != null) close(); }, true, "copy");
            copy.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(copy);
            var q = Qr.Encode(t.Url);
            if (q != null)
            {
                var img = new Image { Source = Qr.Image(q), Width = 172, Height = 172, Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Center, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor); RenderOptions.SetEdgeMode(img, EdgeMode.Aliased);
                img.Tag = "qr"; System.Windows.Automation.AutomationProperties.SetName(img, "QR code of the page's address");
                sp.Children.Add(new Border { Child = img, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center });
                var cap = Ui.Muted("Scan it with a phone's camera. Made on this PC - the address isn't sent anywhere.", 11); cap.TextAlignment = TextAlignment.Center; cap.Margin = new Thickness(4, 2, 4, 2); sp.Children.Add(cap);
            }
            else sp.Children.Add(Ui.Muted("The address is too long for a QR code - copy the link instead.", 11, 4));
            return sp;
        }
        public void ShareFlyout(UIElement at)
        {
            var t = Active; if (t == null || !Omni.IsWeb(t.Url)) return;
            Popup pop = null;
            pop = Panels.ShowFlyout(at ?? Anchor(shareBtn), SharePanel(t, () => { if (pop != null) pop.IsOpen = false; }), 300);
        }
        public static void CopyText(string text)
        {
            LastCopied = text;
            if (BrowserSettings.TestDir != null) return;   // tests never touch your clipboard
            try { Clipboard.SetText(text ?? ""); } catch { }
        }

        // ================================================================ Shield's count on its button
        WebView2 shCount; bool shCountBusy; public string ShieldCountNote; public int ShieldCountReads;
        public int ShieldBadgeNumber { get { return shieldBadge != null && shieldBadge.Visibility == Visibility.Visible ? BadgeNumber(Active) : 0; } }
        int BadgeNumber(Tab t)
        {
            if (t == null || !Omni.IsWeb(t.Url)) return 0;
            return ShieldState == "fallback" ? t.FallbackBlockedHere : t.ShieldBlocked;
        }
        void UpdateShieldBadge()
        {
            if (shieldBadge == null) return;
            var n = BadgeNumber(Active);
            shieldBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            shieldBadgeText.Text = n > 99 ? "99+" : n.ToString();
            shieldBtn.ToolTip = n > 0 ? "Hallow Shield - " + n + " blocked on this page" : "Hallow Shield - this site, levels, filters";
        }
        // Shield keeps a count per tab (Chromium's action count for its blocking rules); a hidden page of Shield's own reads it -
        // getBadgeText has no quota (getMatchedRules has: 20 calls in 10 minutes)
        static string CountScript(string url)
        {
            var u = new JavaScriptSerializer().Serialize(url);
            return "(async()=>{try{if(!window.__hc){window.__hc={};try{await chrome.declarativeNetRequest.setExtensionActionOptions({displayActionCountAsBadgeText:true});}catch(e){}" +
                "try{chrome.declarativeNetRequest.onRuleMatchedDebug.addListener(function(i){var r=i.request||{};var k=r.tabId;if(k==null||k<0)return;if(r.type==='main_frame')return;window.__hc[k]=(window.__hc[k]||0)+1;});window.__hcDbg=1;}catch(e){window.__hcDbg=0;}" +
                "try{chrome.tabs.onUpdated.addListener(function(id,info){if(info.status==='loading'&&info.url)window.__hc[id]=0;});}catch(e){}}" +
                "var U=" + u + ";var ts=await chrome.tabs.query({});var t=ts.find(function(x){return (x.url||x.pendingUrl)===U;});if(!t)return JSON.stringify({tab:null,tabs:ts.length});" +
                "var b='';try{b=await chrome.action.getBadgeText({tabId:t.id});}catch(e){b='err '+e.message;}" +
                "return JSON.stringify({tab:t.id,badge:b,dbg:window.__hc[t.id]||0,hasDbg:window.__hcDbg});}catch(e){return JSON.stringify({err:String(e)});}})()";
        }
        public async Task CountShield(Tab t)
        {
            if (snap || t == null || !Omni.IsWeb(t.Url) || Gone) return;
            if (ShieldState == "fallback") { if (t == Active) UpdateShieldBadge(); return; }
            if (ShieldState != "extension" || ShieldId == null || shCountBusy) return;
            shCountBusy = true; var url = t.Url;
            try
            {
                if (shCount == null || shCount.CoreWebView2 == null)
                {
                    // a new WebView can take the keyboard when it starts (seen in tests: the address bar lost it while typing) - given back after
                    var had = Keyboard.FocusedElement;
                    var v = new WebView2 { Visibility = Visibility.Hidden, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Focusable = false };
                    content.Children.Add(v); shCount = v;
                    try { await Engine.Ensure(v); await Panels.NavigateAndWait(v.CoreWebView2, ShieldPage("family.js")); }
                    finally { if (had != null && !ReferenceEquals(Keyboard.FocusedElement, had)) try { Keyboard.Focus(had); } catch { } }
                }
                var raw = await Panels.Eval(shCount.CoreWebView2, CountScript(url));
                var js = new JavaScriptSerializer(); var json = js.Deserialize<string>(raw); ShieldCountNote = json; ShieldCountReads++;
                var d = js.Deserialize<Dictionary<string, object>>(json ?? "{}");
                int badge = -1, dbg = 0; object o;
                if (d.TryGetValue("badge", out o) && o is string) { var digits = new string(((string)o).TakeWhile(char.IsDigit).ToArray()); if (digits.Length > 0) int.TryParse(digits, out badge); }
                if (d.TryGetValue("dbg", out o) && o != null) try { dbg = Convert.ToInt32(o); } catch { }
                if (t.Url == url && (badge >= 0 || d.ContainsKey("tab") && d["tab"] != null)) { t.ShieldBlocked = Math.Max(badge, dbg); t.ShieldCountedUrl = url; }
            }
            catch (Exception e)
            {
                ShieldCountNote = "failed: " + e.Message;
                if (shCount != null) { content.Children.Remove(shCount); try { shCount.Dispose(); } catch { } shCount = null; }
            }
            finally { shCountBusy = false; }
            if (t == Active) UpdateShieldBadge();
        }
        void ShieldCountSoon(Tab t)
        {
            if (snap || t == null) return;
            foreach (var ms in new[] { 400, 1800, 4500 })
            {
                var delay = ms;
                var _ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(delay); if (t == Active && Tabs.Contains(t)) await CountShield(t); }), DispatcherPriority.Background);
            }
        }

        // the page's events for 1.18 (from Hook)
        void Hook118(CoreWebView2 c)
        {
            c.NavigationStarting += (a, b) =>
            {
                Tab x; if (!owner.TryGetValue(c, out x)) return;
                x.ShieldBlocked = 0; x.FallbackBlockedHere = 0; x.ShieldCountedUrl = null; x.Installable = false; x.InstalledApp = false;
                if (x == Active) { UpdateShieldBadge(); UpdatePageActions(x); }
            };
            c.NavigationCompleted += (a, b) =>
            {
                Tab x; if (!owner.TryGetValue(c, out x) || !b.IsSuccess || !Omni.IsWeb(x.Url)) return;
                var _ = CheckInstallable(x, c);
                if (x == Active) ShieldCountSoon(x);
            };
        }
        // every 5 s (the window's tick): the shown tab's count again (late requests count too)
        void ShieldTick()
        {
            var t = Active;
            if (t != null && Omni.IsWeb(t.Url) && t.Ready && WindowState != WindowState.Minimized && ShieldState == "extension" && Game == null) { var _ = CountShield(t); }
        }

        // ================================================================ downloads: the progress ring
        DateTime ringAt;
        public double DownloadRing { get { return dlRing != null && dlRing.Visibility == Visibility.Visible ? ringValue : -1; } }
        double ringValue = -1;
        void UpdateDownloadRing(bool force = false)
        {
            if (dlRing == null) return;
            if (!force && (DateTime.Now - ringAt).TotalMilliseconds < 120) return;
            ringAt = DateTime.Now;
            var p = AddressText.Progress(Downloads); ringValue = p;
            var on = p >= 0;
            dlRing.Visibility = dlRingBg.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            dlIcon.Width = dlIcon.Height = on ? 11 : 14;
            if (on)
            {
                double r = 10, cx = 11, cy = 11, a = Math.Max(0.02, Math.Min(0.9999, p)) * 2 * Math.PI;
                var end = new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
                var fig = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = false };
                fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
                var g = new PathGeometry(); g.Figures.Add(fig); dlRing.Data = g;
                dlBtn.ToolTip = "Downloads (Ctrl+J) - " + Math.Round(p * 100) + "%";
            }
            else dlBtn.ToolTip = "Downloads (Ctrl+J)";
        }

        // ================================================================ extensions: the puzzle piece's list and pinned buttons
        static readonly Dictionary<string, ImageSource> extIcons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        public static ImageSource ExtIcon(UserExtension x)
        {
            if (x == null || string.IsNullOrEmpty(x.Folder)) return null;
            ImageSource img;
            lock (extIcons) if (extIcons.TryGetValue(x.Folder, out img)) return img;
            img = null;
            try
            {
                var f = ExtStore.IconFile(x.Folder);
                if (f != null)
                {
                    var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(f); bi.DecodePixelWidth = 32; bi.EndInit(); bi.Freeze(); img = bi;
                }
            }
            catch { img = null; }
            lock (extIcons) extIcons[x.Folder] = img;
            return img;
        }
        static UIElement ExtGlyph(UserExtension x, double size)
        {
            var img = ExtIcon(x);
            if (img != null) return new Image { Source = img, Width = size, Height = size, Stretch = Stretch.Uniform };
            // no icon of its own: its initial on a small tile (two puzzle pieces side by side read as one button)
            var tx = new TextBlock { Text = string.IsNullOrWhiteSpace(x.Name) ? "?" : x.Name.Trim().Substring(0, 1).ToUpperInvariant(), FontSize = size * 0.62, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tx.SetResourceReference(TextBlock.ForegroundProperty, "Bg");
            var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(4), Child = tx }; tile.SetResourceReference(Border.BackgroundProperty, "Lilac");
            return tile;
        }
        List<string> Pins { get { return S.PinnedExtensions ?? new List<string>(); } }
        public bool IsPinned(UserExtension x) { return x != null && Pins.Contains(x.Id); }
        public void SetPinned(UserExtension x, bool on)
        {
            var l = Pins.Where(id => S.Extensions.Any(e => e.Id == id)).ToList();
            if (on && !l.Contains(x.Id)) l.Add(x.Id); if (!on) l.Remove(x.Id);
            S.PinnedExtensions = l.Count == 0 ? null : l; S.Save();
            foreach (var w in Program.Windows.ToList()) if (!w.Gone) { if (w != this) w.S.PinnedExtensions = S.PinnedExtensions == null ? null : S.PinnedExtensions.ToList(); w.RefreshPins(); }
            if (!Program.Windows.Contains(this)) RefreshPins();
        }
        public readonly List<Button> PinButtons = new List<Button>();
        public void RefreshPins()
        {
            if (pinHost == null) return;
            pinHost.Children.Clear(); PinButtons.Clear();
            if (Private || AppMode) return;
            foreach (var id in Pins)
            {
                var x = S.Extensions.FirstOrDefault(e => e.Id == id && e.Enabled); if (x == null) continue;
                var xx = x; Button b = null;
                b = new Button { Style = Ui.S("Tool"), Content = ExtGlyph(x, 16), ToolTip = x.Name, Tag = x.Id };
                b.Click += (a, e) => OpenExtension(xx, b);
                pinHost.Children.Add(b); PinButtons.Add(b);
            }
        }
        public void OpenExtension(UserExtension x, UIElement at)
        {
            Panels.CloseFlyouts();
            if (x.Popup != null) { var _ = ExtPopup(x, at); }
            else if (x.Options != null) NewTab("chrome-extension://" + x.Id + "/" + x.Options.TrimStart('/'), true);
            else Toast(x.Name + " has no popup.", 3);
        }
        public FrameworkElement ExtPanel(Action close)
        {
            var sp = new StackPanel();
            var head = Ui.Label("Extensions"); head.Margin = new Thickness(12, 8, 12, 6); sp.Children.Add(head);
            var list = S.Extensions.Where(e => e.Enabled).ToList();
            if (list.Count == 0) { var m = Ui.Muted("None yet. Add one from the Chrome Web Store or load an unpacked folder.", 11.5); m.TextWrapping = TextWrapping.Wrap; m.Margin = new Thickness(12, 0, 12, 8); sp.Children.Add(m); }
            foreach (var x in list)
            {
                var xx = x; var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var row = new Button { Style = Ui.S("MenuRow"), ToolTip = xx.Popup != null ? "Open " + xx.Name : xx.Options != null ? xx.Name + "'s options" : xx.Name };
                var rg = new Grid(); rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); rg.ColumnDefinitions.Add(new ColumnDefinition());
                var glyph = ExtGlyph(xx, 16); ((FrameworkElement)glyph).HorizontalAlignment = HorizontalAlignment.Left; rg.Children.Add(glyph);
                var nm = new TextBlock { Text = xx.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(nm, 1); rg.Children.Add(nm);
                row.Content = rg; System.Windows.Automation.AutomationProperties.SetName(row, xx.Name);
                row.Click += (a, b) => { if (close != null) close(); OpenExtension(xx, Anchor(extBtn)); };
                g.Children.Add(row);
                bool pinned = IsPinned(xx);
                var pin = new System.Windows.Controls.Primitives.ToggleButton { Style = Ui.S("Tool"), Width = 30, Height = 28, IsChecked = pinned, Content = Ui.Icon("thumbtack", 12, pinned ? "AccentBrush" : "Muted"), ToolTip = pinned ? "Unpin " + xx.Name + " from the toolbar" : "Pin " + xx.Name + " to the toolbar", Tag = "pin:" + xx.Id };
                pin.Click += (a, b) => { SetPinned(xx, pin.IsChecked == true); var on = pin.IsChecked == true; pin.Content = Ui.Icon("thumbtack", 12, on ? "AccentBrush" : "Muted"); pin.ToolTip = (on ? "Unpin " : "Pin ") + xx.Name + (on ? " from the toolbar" : " to the toolbar"); };
                Grid.SetColumn(pin, 1); g.Children.Add(pin);
                g.Margin = new Thickness(0, 0, 4, 0);
                sp.Children.Add(g);
            }
            var line = new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5) }; line.SetResourceReference(Border.BackgroundProperty, "LineDim"); sp.Children.Add(line);
            sp.Children.Add(Ui.MenuRow("gear", "Manage extensions", null, () => { if (close != null) close(); NewTab(Omni.ExtensionsUrl, true); }));
            return sp;
        }
        void ExtFlyout(UIElement at)
        {
            Popup pop = null;
            pop = Panels.ShowFlyout(at, ExtPanel(() => { if (pop != null) pop.IsOpen = false; }), 300);
        }

        // ================================================================ the profile button
        void RefreshProfileButton()
        {
            if (profBtn == null) return;
            var cur = Program.CurrentProfile;
            if (cur == null || Program.Profiles == null) { profBtn.Content = Ui.Icon("user", 13); profBtn.ToolTip = "Profiles - switch or add a profile"; return; }
            var col = ProfileColor(cur);
            var letter = (cur.Name ?? "?").Trim(); letter = letter.Length == 0 ? "?" : letter.Substring(0, 1).ToUpperInvariant();
            var tx = new TextBlock { Text = letter, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
            profBtn.Content = new Border { Width = 21, Height = 21, CornerRadius = new CornerRadius(10.5), Background = new SolidColorBrush(col), Child = tx };
            profBtn.ToolTip = "Profile: " + cur.Name + " - switch or add a profile";
            profBtn.Tag = letter;
        }
        public string ProfileButtonLetter { get { return profBtn == null || profBtn.Parent == null ? null : profBtn.Tag as string ?? "icon"; } }

        // ================================================================ the tab strip: arrows when it overflows, the tab-search button
        void BuildStrip118(StackPanel sp)
        {
            stripScroll = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, CanContentScroll = false, Background = Brushes.Transparent };
            stripScroll.PreviewMouseWheel += (a, b) => { if (!stripOverflow) return; stripScroll.ScrollToHorizontalOffset(stripScroll.HorizontalOffset - b.Delta / 2.0); b.Handled = true; };
            scrollL = Ui.Tool("chevron-left", "Scroll the tabs left", () => ScrollTabs(-1), 10); scrollR = Ui.Tool("chevron-right", "Scroll the tabs right", () => ScrollTabs(1), 10);
            foreach (var b in new[] { scrollL, scrollR }) { b.Width = 24; b.Height = 28; b.Margin = new Thickness(0, 6, 0, 0); b.VerticalAlignment = VerticalAlignment.Center; b.Visibility = Visibility.Collapsed; Chrome(b); }
            sp.Children.Add(scrollL); sp.Children.Add(stripScroll); sp.Children.Add(scrollR);
            tabSearchBtn = Ui.Tool("chevron-down", "Search your tabs", OpenTabSearch, 10); tabSearchBtn.Width = 30; tabSearchBtn.Height = 28; tabSearchBtn.VerticalAlignment = VerticalAlignment.Center; tabSearchBtn.Margin = new Thickness(4, 2, 2, 0); Chrome(tabSearchBtn);
        }
        public void ScrollTabs(int dir) { if (stripScroll == null) return; stripScroll.ScrollToHorizontalOffset(stripScroll.HorizontalOffset + dir * Math.Max(TabFit.Min * 2, stripScroll.ViewportWidth * 0.6)); }
        // the strip's layout: even widths between TabFit.Min and TabFit.Max; the arrows and a scrolling strip when they don't fit
        void FitStrip118(int vis, double chips)
        {
            double room = Math.Max(100, stripHost.ActualWidth - 40);
            bool over; var w = TabFit.Width(room, vis, chips, out over);
            stripOverflow = over;
            scrollL.Visibility = scrollR.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
            stripScroll.Width = over ? Math.Max(80, room - 2 * TabFit.Arrows) : double.NaN;
            narrowTabs = w < TabFit.Narrow;
            foreach (var t in Tabs)
            {
                t.Header.Width = w;
                var mine = t == Active || (t.Header.IsMouseOver && narrowTabs);
                t.CloseBtn.Visibility = !narrowTabs || mine ? Visibility.Visible : Visibility.Collapsed;
                t.TitleBlock.Visibility = w < TabFit.IconOnly ? Visibility.Collapsed : Visibility.Visible;   // very narrow: the site's icon only
            }
            if (over && Active != null && Active.Header.Visibility == Visibility.Visible) { var h = Active.Header; Dispatcher.BeginInvoke(new Action(() => { try { h.BringIntoView(); } catch { } }), DispatcherPriority.Loaded); }
        }
        void TabHover118(Tab t, bool over)
        {
            if (!narrowTabs || S.VerticalTabs || t.CloseBtn == null) return;
            t.CloseBtn.Visibility = over || t == Active ? Visibility.Visible : Visibility.Collapsed;
        }
        public Dictionary<string, object> StripState()
        {
            return new Dictionary<string, object> { { "widths", Tabs.Select(t => Math.Round(t.Header.ActualWidth > 0 ? t.Header.ActualWidth : t.Header.Width, 1)).ToList() }, { "overflow", stripOverflow }, { "arrows", scrollL.Visibility == Visibility.Visible && scrollR.Visibility == Visibility.Visible },
                { "narrow", narrowTabs }, { "closeShown", Tabs.Select(t => t.CloseBtn.Visibility == Visibility.Visible).ToList() }, { "active", Tabs.IndexOf(Active) }, { "offset", stripScroll.HorizontalOffset }, { "viewport", stripScroll.ViewportWidth }, { "extent", stripScroll.ExtentWidth },
                { "activeInView", ActiveInView() }, { "window", ActualWidth } };
        }
        bool ActiveInView()
        {
            if (Active == null) return false;
            try { var r = Active.Header.TransformToAncestor(stripScroll).TransformBounds(new Rect(0, 0, Active.Header.ActualWidth, Active.Header.ActualHeight)); return r.Left >= -1 && r.Right <= stripScroll.ActualWidth + 1; } catch { return false; }
        }
        public void HoverTabForTest(Tab t, bool over) { TabHover118(t, over); }
        // the tab search: the address bar's "% " (your open tabs, typed to filter)
        public void OpenTabSearch()
        {
            Panels.CloseFlyouts();
            Address.Focus();
            suggestFilling = true; try { Address.Text = "% "; Address.CaretIndex = 2; } finally { suggestFilling = false; }
            Suggest();
        }
        public List<string> SuggestRowsShown() { return rows.Select(r => r.Kind + ":" + r.Text).ToList(); }

        // ================================================================ --snap: the new bar's parts (made-up)
        void SnapClearMock() { foreach (var m in content.Children.OfType<FrameworkElement>().Where(x => (x.Tag as string) == "mock").ToList()) content.Children.Remove(m); }
        void SnapAddRight(FrameworkElement f, double right)
        {
            f.HorizontalAlignment = HorizontalAlignment.Right; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(0, 4, right, 0); f.Tag = "mock";
            Grid.SetColumnSpan(f, 3); content.Children.Add(f);
        }
        public void SnapTopBar(string what)
        {
            var t = Active;
            switch (what)
            {
                case "customize": SnapAddRight(Ui.Flyout(CustomizePanel(), 340), 8); break;
                case "site-info": { var sp = SiteInfoPanel(t, null); var f = Ui.Flyout(sp, 430); f.HorizontalAlignment = HorizontalAlignment.Left; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(112, 4, 0, 0); f.Tag = "mock"; Grid.SetColumnSpan(f, 3); content.Children.Add(f); break; }
                case "extensions":
                {
                    S.Extensions = new List<UserExtension> {
                        new UserExtension { Id = "aaaabbbbccccddddeeeeffffgggghhhh", Name = "Dark Reader", Version = "4.9", Folder = "snap-a", Popup = "popup.html", Enabled = true },
                        new UserExtension { Id = "iiiijjjjkkkkllllmmmmnnnnoooopppp", Name = "Return YouTube Dislike", Version = "3.0", Folder = "snap-b", Popup = "popup.html", Enabled = true },
                        new UserExtension { Id = "qqqqrrrrssssttttuuuuvvvvwwwwxxxx", Name = "Tab counter", Version = "1.2", Folder = "snap-c", Options = "options.html", Enabled = true } };
                    S.PinnedExtensions = new List<string> { "aaaabbbbccccddddeeeeffffgggghhhh" };
                    ApplyToolbar();
                    var f = Ui.Flyout(ExtPanel(null), 300);
                    SnapAddRight(f, ToolbarRightOf(extBtn));
                    break;
                }
                case "downloads":
                {
                    var dir = @"C:\Users\you\Downloads\";
                    Downloads.Clear();
                    Downloads.Add(new DownloadItem { Name = "season-4-map-pack.zip", File = dir + "season-4-map-pack.zip", State = "running", Got = 401604608, Total = 734003200 });
                    Downloads.Add(new DownloadItem { Name = "patch-notes-4.2.pdf", File = dir + "patch-notes-4.2.pdf", State = "done", Got = 1258291, Total = 1258291, Scan = "clean" });
                    Downloads.Add(new DownloadItem { Name = "clip-ace-round.mp4", File = dir + "clip-ace-round.mp4", State = "done", Got = 48234496, Total = 48234496, Scan = "clean" });
                    Downloads.Add(new DownloadItem { Name = "controller-layout.png", File = dir + "controller-layout.png", State = "done", Got = 312000, Total = 312000, Scan = "clean" });
                    Downloads.Add(new DownloadItem { Name = "old-beta-client.zip", File = dir + "old-beta-client.zip", State = "cancelled" });
                    Downloads.Add(new DownloadItem { Name = "sixth-file-not-shown.txt", File = dir + "sixth.txt", State = "done", Got = 10, Total = 10 });
                    dlBadge.Visibility = Visibility.Collapsed; UpdateDownloadRing(true);
                    var f = Ui.Flyout(Panels.DownloadsPanel(this), 360);
                    SnapAddRight(f, ToolbarRightOf(dlBtn));
                    break;
                }
                case "page-actions":
                {
                    t.Readable = true; t.Installable = true; t.MediaSeen = false; t.Title = "Hallow News - patch notes"; t.TitleBlock.Text = t.Title;
                    UpdateBar(); zoomChip.Visibility = Visibility.Visible; zoomChip.Content = new TextBlock { Text = "125%", FontSize = 11 };
                    pipBtn.Visibility = Visibility.Visible;
                    SnapAddRight(Ui.Flyout(SharePanel(t, null), 300), PageActionRightOf(shareBtn));
                    break;
                }
                case "profile":
                {
                    RefreshProfileButton();
                    var sp = new StackPanel(); foreach (var e in ProfileMenuRows(a => { })) sp.Children.Add(e);
                    var f = Ui.Flyout(sp, 280);
                    SnapAddRight(f, ToolbarRightOf(profBtn));
                    break;
                }
                case "http":
                    t.Url = "http://lan-party.example.com/signup"; t.Title = "LAN party sign-up"; t.TitleBlock.Text = t.Title; UpdateBar();
                    break;
                case "overflow":
                    for (int i = 0; i < 18; i++) { var nt = new Tab { Url = "https://example.com/" + i, Title = "Guide part " + (i + 1) }; AddHeader(nt, Tabs.Count); Tabs.Add(nt); }
                    RenderStrip(); Activate(Tabs[6]);
                    break;
            }
        }
        // --snap draws before any layout pass: widths come from the buttons' own sizes (toolbar buttons are 32 px, page actions 28)
        static double WidthOf(UIElement e)
        {
            var fe = e as FrameworkElement; if (fe == null || fe.Visibility != Visibility.Visible) return 0;
            if (!double.IsNaN(fe.Width)) return fe.Width + fe.Margin.Left + fe.Margin.Right;
            var p = fe as Panel; if (p != null) return p.Children.Cast<UIElement>().Sum(x => WidthOf(x));
            return 32;
        }
        // the distance from the window's right edge to a toolbar button's right edge
        double ToolbarRightOf(FrameworkElement b)
        {
            UIElement top = b; while (top != null && VisualTreeHelper.GetParent(top) != tbRight && LogicalTreeHelper.GetParent(top) != tbRight) top = (LogicalTreeHelper.GetParent(top) ?? VisualTreeHelper.GetParent(top)) as UIElement;
            if (top == null) return 8;
            double sum = 6; bool after = false;
            foreach (UIElement e in tbRight.Children) { if (after) sum += WidthOf(e); if (e == top) after = true; }
            return sum;
        }
        // ... and to a page action's right edge (inside the address bar)
        double PageActionRightOf(FrameworkElement b)
        {
            var inBar = (Panel)LogicalTreeHelper.GetParent(b);
            double sum = 6 + WidthOf(tbRight) + 6 + 4; bool after = false;
            foreach (UIElement e in inBar.Children) { if (after) sum += WidthOf(e); if (e == b) after = true; }
            return sum;
        }
        public void SnapShield(int n) { if (Active != null) { Active.ShieldBlocked = n; UpdateShieldBadge(); } }
    }
}
