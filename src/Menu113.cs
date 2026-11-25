// Hallow Browser 1.13 - a cleaner main menu (real use: "this dropdown got way too much going", and Settings ran off the bottom of the
// screen past Zoom - the 1.12 menu was about 30 rows). Brave / Chrome style now: a short top level that always fits the window (it
// scrolls if the window is too short, so Settings is always reachable), with the rest in submenus that open to the side (hover, click,
// Enter or the right arrow; the left arrow or Esc goes back). Keyboard shortcuts are unchanged and shown in the submenus. Ctrl+K still
// finds every action. A Settings (gear) button sits on the toolbar next to the menu, so Settings is one click away.
// The menu is built from a model (MItem) so tests can check its shape: at most 14 entries on top (plus the zoom row), and every item
// of the 1.12 menu reachable in a submenu or the command palette.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace HallowBrowser
{
    public class MItem
    {
        public string Icon, Text, Keys, Id;
        public Action Run;                       // null for a submenu or the zoom row
        public bool Enabled = true, Separator, Zoom;
        public List<MItem> Kids;                 // a submenu
        public Func<Action<Action>, List<UIElement>> Custom;   // rows drawn by someone else (the profiles)
        public List<string> CustomLabels;        // what those rows say (tests)
        public static MItem Sep() { return new MItem { Separator = true }; }
    }

    public static class Menu113
    {
        public const int MaxTop = 14;
        // every row of the 1.12 menu (alternatives with |): each has to be in the new menu or in Ctrl+K
        public static readonly string[] OldLabels = {
            "New tab", "New window", "New private window", "Reopen closed tab", "Reopen closed window", "Downloads", "Find in page", "Pop out video",
            "Hide the sidebar|Show the sidebar", "Hot tabs (CPU and memory)", "Make pages dark|Show pages as sites draw them", "Command palette", "Group tabs",
            "Vertical tabs|Tabs in the title bar", "Split view with the next tab|Close split view", "Screenshot", "Reader mode|Leave reader mode", "Bookmarks manager",
            "Show the bookmarks bar|Hide the bookmarks bar", "Import from another browser...", "Workspaces and saved sessions", "Save for later", "Reading list",
            "Notes for this page", "Install this site...", "Apps", "Add a profile...", "Manage profiles", "Zoom", "Print...", "Hallow Shield settings", "Extensions",
            "Site settings", "Clear browsing data...", "Settings", "Exit" };

        public static List<MItem> Model(BrowserWindow w)
        {
            var a = w.Active; bool web = a != null && Omni.IsWeb(a.Url);
            var top = new List<MItem>();
            Func<string, string, string, Action, MItem> R = (icon, text, keys, run) => new MItem { Icon = icon, Text = text, Keys = keys, Run = run };
            top.Add(R("plus", "New tab", "Ctrl+T", () => w.NewTab(Omni.NewTabUrl, true)));
            top.Add(R("window-restore", "New window", "Ctrl+N", () => Program.OpenWindow(false, null, null)));
            top.Add(R("mask", "New private window", "Ctrl+Shift+N", () => Program.OpenWindow(true, null, null)));
            top.Add(MItem.Sep());
            top.Add(new MItem { Icon = "clock-rotate-left", Text = "History", Id = "history", Kids = new List<MItem> {
                R("clock-rotate-left", "Show all history", "Ctrl+H", w.ShowHistory),
                new MItem { Icon = "rotate-left", Text = "Reopen closed tab", Keys = "Ctrl+Shift+T", Run = w.ReopenClosed, Enabled = w.ClosedCount > 0 },
                new MItem { Icon = "window-restore", Text = "Reopen closed window", Run = () => Program.ReopenClosedWindow(), Enabled = Program.ClosedWindows.Count > 0 },
                MItem.Sep(),
                R("broom", "Clear browsing data...", "Ctrl+Shift+Del", () => Panels.ClearData(w)) } });
            top.Add(R("download", "Downloads", "Ctrl+J", w.ShowDownloads));
            top.Add(new MItem { Icon = "bookmark", Text = "Bookmarks", Id = "bookmarks", Kids = new List<MItem> {
                new MItem { Icon = "bookmark", Text = w.IsBookmarked(a) ? "Remove bookmark" : "Bookmark this page", Keys = "Ctrl+D", Run = () => w.ToggleBookmark(w.Active), Enabled = web },
                R("bookmark", "Bookmarks manager", "Ctrl+Shift+O", w.ShowBookmarks),
                R("bookmark", w.S.BookmarksBar ? "Hide the bookmarks bar" : "Show the bookmarks bar", "Ctrl+Shift+B", w.ToggleBookmarksBar),
                MItem.Sep(),
                R("file-import", "Import from another browser...", null, () => w.ShowImport(w.MenuButton)) } });
            top.Add(new MItem { Icon = "book-bookmark", Text = "Reading list & notes", Id = "reading", Kids = new List<MItem> {
                new MItem { Icon = "book-bookmark", Text = "Save for later", Keys = "Ctrl+Shift+D", Run = () => w.SaveForLater(w.Active), Enabled = web },
                R("book-bookmark", "Reading list", null, () => w.Side.ShowNative("reading")),
                R("note-sticky", "Notes for this page", null, () => w.Side.ShowNative("notes")) } });
            top.Add(R("magnifying-glass", "Find in page", "Ctrl+F", w.ShowFind));
            top.Add(new MItem { Zoom = true, Text = "Zoom" });
            top.Add(MItem.Sep());
            var view = new List<MItem> {
                R("table-columns", w.S.SidebarOn ? "Hide the sidebar" : "Show the sidebar", "Ctrl+Shift+S", () => w.Side.Toggle()),
                R("table-list", w.S.VerticalTabs ? "Tabs in the title bar" : "Vertical tabs", null, w.ToggleVerticalTabs),
                R("layer-group", "Group tabs", null, () => w.GroupTabsFlyout(w.MenuButton)),
                R("table-columns", w.SplitOn ? "Close split view" : "Split view with the next tab", "Ctrl+click", () => { if (w.SplitOn) w.CloseSplit(); else if (w.Active != null && w.Tabs.Count > 1) w.AddToSplit(w.Tabs[(w.Tabs.IndexOf(w.Active) + 1) % w.Tabs.Count]); }),
                MItem.Sep(),
                R("moon", w.S.ForceDark ? "Show pages as sites draw them" : "Make pages dark", null, w.ToggleForceDark) };
            if (web) { var host = Omni.HostOf(a.Url); view.Add(R("circle-half-stroke", w.WantsDark(a.Url) ? "Don't make " + host + " dark" : "Make " + host + " dark", null, () => w.ToggleSiteDark(w.Active))); }
            view.Add(new MItem { Icon = "book-open", Text = a != null && a.ReaderOn ? "Leave reader mode" : "Reader mode", Keys = "F9", Run = () => w.ToggleReader(w.Active), Enabled = a != null && (a.Readable || a.ReaderOn) });
            view.Add(R("expand", "Full screen", "F11", () => w.SetFullscreen(true)));
            view.Add(R("sliders", "Customize toolbar...", null, () => w.CustomizeToolbar(null)));   // 1.18 (also: right-click the toolbar)
            top.Add(new MItem { Icon = "eye", Text = "View", Id = "view", Kids = view });
            var more = new List<MItem> {
                R("up-right-from-square", "Pop out video", null, () => { if (w.Active != null) Pip.PopOut(w, w.Active); }),
                R("fire", "Hot tabs (CPU and memory)", "Shift+Esc", () => HotTabs.Flyout(w, w.MenuButton)),
                R("camera", "Screenshot", "Ctrl+Shift+X", w.ShowScreenshot),
                R("terminal", "Command palette", "Ctrl+K", () => Palette.Show(w)),
                new MItem { Icon = "star", Text = "Print...", Keys = "Ctrl+P", Run = () => { if (w.Active != null && w.Active.Core != null) w.Active.Core.ShowPrintUI(); }, Enabled = web } };
            if (web) { more.Add(R("language", a.Translated ? "Show original" : "Translate...", null, () => w.TranslateFlyout(w.Active))); more.Add(R("tv", "Cast... (open in Edge)", null, () => w.OpenInEdgeToCast(w.Active))); }
            more.Add(MItem.Sep());
            more.Add(new MItem { Icon = "square-plus", Text = "Install this site...", Run = () => { var _ = w.InstallSite(w.Active); }, Enabled = !w.Private && web });
            more.Add(R("table-cells-large", "Apps", null, () => w.NewTab(Omni.AppsUrl, true)));
            more.Add(R("briefcase", "Workspaces and saved sessions", null, () => w.ShowWorkspaces(false)));
            more.Add(MItem.Sep());
            more.Add(R("shield-halved", "Hallow Shield settings", null, () => Panels.OpenShieldSettings(w)));
            more.Add(R("puzzle-piece", "Extensions", null, () => w.NewTab(Omni.ExtensionsUrl, true)));
            more.Add(R("sliders", "Site settings", null, () => w.NewTab(Omni.SitesUrl, true)));
            if (w.CanReportTwitch(a)) more.Add(R("twitch", "Report a Twitch problem", TwitchReport.Keys, () => w.ReportTwitchProblem(w.Active)));   // 1.14.2
            more.Add(new MItem { Icon = "terminal", Text = "Developer tools", Keys = "F12", Run = () => { if (w.Active != null && w.Active.Core != null) w.Active.Core.OpenDevToolsWindow(); }, Enabled = a != null && a.Core != null });
            top.Add(new MItem { Icon = "terminal", Text = "More tools", Id = "more", Kids = more });
            var cur = Program.Profiles == null ? null : Program.Profiles.Items.FirstOrDefault(p => p.Id == BrowserSettings.ProfileId);
            top.Add(new MItem { Icon = "user", Text = "Profiles" + (cur != null ? ": " + cur.Name : ""), Id = "profiles", Kids = new List<MItem>(),
                Custom = run => w.ProfileMenuRows(run), CustomLabels = (Program.Profiles == null ? new List<string>() : Program.Profiles.Items.Select(p => p.Name).ToList()).Concat(new[] { "Add a profile...", "Manage profiles" }).ToList() });
            top.Add(MItem.Sep());
            top.Add(R("gear", "Settings", "Ctrl+,", () => w.NewTab(Omni.SettingsUrl, true)));
            if (w.Update != null) top.Add(R("download", "Update to " + w.Update.Version.ToString(), null, () => Panels.DoUpdate(w)));
            top.Add(new MItem { Icon = "circle-info", Text = "Help & about", Id = "help", Kids = new List<MItem> {
                R("circle-info", "About Hallow Browser", null, () => w.OpenSettingsAt("About")),
                R("arrow-right", "What's new (release notes)", null, () => w.NewTab("https://github.com/Dom428/hallow-browser/releases/latest", true)),
                R("terminal", "Keyboard shortcuts and every action", "Ctrl+K", () => Palette.Show(w)) } });
            top.Add(R("xmark", "Exit", null, Program.Quit));
            return top;
        }

        // ---- shape (tests)
        public static int TopEntries(List<MItem> m) { return m.Count(x => !x.Separator && !x.Zoom); }
        public static IEnumerable<MItem> Leaves(IEnumerable<MItem> m) { foreach (var x in m) { if (x.Separator || x.Zoom) continue; if (x.Kids != null) { foreach (var k in Leaves(x.Kids)) yield return k; } else yield return x; } }
        public static IEnumerable<string> Labels(IEnumerable<MItem> m)
        {
            foreach (var x in m)
            {
                if (x.Separator) continue;
                yield return x.Text;
                if (x.CustomLabels != null) foreach (var l in x.CustomLabels) yield return l;
                if (x.Kids != null) foreach (var l in Labels(x.Kids)) yield return l;
            }
        }
        public static List<string> Missing(IEnumerable<string> have)
        {
            var h = new HashSet<string>(have, StringComparer.OrdinalIgnoreCase);
            return OldLabels.Where(o => !o.Split('|').Any(x => h.Contains(x))).ToList();
        }

        // ---- drawing
        public static double MenuWidth = 300, SubWidth = 280;
        static Popup sub; static MItem subOf; static Button subRow;
        public static Popup Current { get; private set; }
        public static ScrollViewer CurrentScroll { get; private set; }
        public static ScrollViewer SubScroll { get; private set; }
        public static string OpenSub { get { return sub != null && sub.IsOpen && subOf != null ? subOf.Id : null; } }

        // the most a menu may be tall: the window (and the screen) below the toolbar
        public static double MaxHeight(BrowserWindow w)
        {
            double win = w.ActualHeight > 0 ? w.ActualHeight : w.Height;
            if (double.IsNaN(win) || win <= 0) win = 800;
            double screen = BrowserSettings.Offscreen ? win : SystemParameters.WorkArea.Height;
            return Math.Max(220, Math.Min(win - 70, screen - 90));
        }

        public static void Open(BrowserWindow w, UIElement at)
        {
            var model = Model(w);
            Popup p = null;
            Action<Action> run = act => { CloseSub(); if (p != null) p.IsOpen = false; act(); };
            var host = new Grid();
            var sp = Rows(w, model, run, false);
            var sv = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = MaxHeight(w), Focusable = false };
            host.Children.Add(sv);
            sub = new Popup { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Left, PopupAnimation = PopupAnimation.None, HorizontalOffset = -2 };
            host.Children.Add(sub);   // inside the menu: a click in the submenu doesn't close the menu
            subOf = null; subRow = null;
            p = Panels.ShowFlyout(at, host, MenuWidth);
            p.Closed += (s, e) => CloseSub();
            Current = p; CurrentScroll = sv;
            sp.Loaded += (s, e) => FocusFirst(sp);
        }
        public static void CloseSub() { if (sub != null) sub.IsOpen = false; subOf = null; }
        static void FocusFirst(Panel sp) { var b = sp.Children.OfType<Button>().FirstOrDefault(x => x.IsEnabled); if (b != null) b.Focus(); }

        // the rows of a level; isSub: no further submenus
        public static StackPanel Rows(BrowserWindow w, List<MItem> items, Action<Action> run, bool isSub)
        {
            var sp = new StackPanel();
            foreach (var it in items)
            {
                var item = it;
                if (item.Separator) { var b = new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5) }; b.SetResourceReference(Border.BackgroundProperty, "LineDim"); sp.Children.Add(b); continue; }
                if (item.Zoom) { sp.Children.Add(ZoomRow(w)); continue; }
                if (item.Kids != null && !isSub)
                {
                    Button row = null;
                    row = Ui.MenuRow(item.Icon, item.Text, "▸", () => ShowSub(w, row, item, run, true));
                    AutomationProperties.SetName(row, item.Text + " (submenu)");
                    row.MouseEnter += (s, e) => Hover(w, row, item, run);
                    row.PreviewKeyDown += (s, e) => { if (e.Key == Key.Right) { ShowSub(w, row, item, run, true); e.Handled = true; } };
                    sp.Children.Add(row);
                    continue;
                }
                var r = Ui.MenuRow(item.Icon, item.Text, item.Keys, () => run(item.Run));
                r.IsEnabled = item.Enabled;
                if (!isSub) r.MouseEnter += (s, e) => Hover(w, null, null, run);
                sp.Children.Add(r);
            }
            return sp;
        }
        static DispatcherTimer hoverTimer;
        static void Hover(BrowserWindow w, Button row, MItem item, Action<Action> run)
        {
            if (hoverTimer == null) hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            hoverTimer.Stop(); hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            hoverTimer.Tick += (s, e) =>
            {
                hoverTimer.Stop();
                if (sub != null && sub.IsOpen && sub.Child != null && sub.Child.IsMouseOver) return;
                if (item == null) CloseSub(); else if (subOf != item) ShowSub(w, row, item, run, false);
            };
            hoverTimer.Start();
        }
        public static void ShowSub(BrowserWindow w, Button row, MItem item, Action<Action> run, bool focus)
        {
            if (sub == null) return;
            CloseSub();
            var sp = item.Custom != null ? new StackPanel() : Rows(w, item.Kids, run, true);
            if (item.Custom != null) foreach (var e in item.Custom(run)) sp.Children.Add(e);
            var sv = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = MaxHeight(w), Focusable = false };
            var fl = Ui.Flyout(sv, SubWidth);
            fl.PreviewKeyDown += (s, e) => { if (e.Key == Key.Left || e.Key == Key.Escape) { CloseSub(); if (row != null) row.Focus(); e.Handled = true; } };
            sub.Child = fl; sub.PlacementTarget = row; sub.IsOpen = true;
            subOf = item; subRow = row; SubScroll = sv;
            if (focus) sp.Loaded += (s, e) => FocusFirst(sp);
        }
        static UIElement ZoomRow(BrowserWindow w)
        {
            var zg = new Grid { Margin = new Thickness(12, 4, 8, 4) };
            zg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); zg.ColumnDefinitions.Add(new ColumnDefinition()); zg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zg.Children.Add(Ui.Icon("magnifying-glass-plus", 13, "Lilac"));
            var zl = new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(zl, 1); zg.Children.Add(zl);
            var zv = new TextBlock { Text = Math.Round(w.CurrentZoom * 100) + "%", VerticalAlignment = VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Center };
            var full = Ui.Tool("expand", "Full screen (F11)", () => { Panels.CloseFlyouts(); w.SetFullscreen(true); }, 11); full.Margin = new Thickness(6, 0, 0, 0);
            var zr = Ui.Row(Ui.Tool("minus", "Zoom out (Ctrl+-)", () => { w.Zoom(-1); zv.Text = Math.Round(w.CurrentZoom * 100) + "%"; }, 11), zv, Ui.Tool("plus", "Zoom in (Ctrl++)", () => { w.Zoom(1); zv.Text = Math.Round(w.CurrentZoom * 100) + "%"; }, 11), full);
            Grid.SetColumn(zr, 2); zg.Children.Add(zr);
            return zg;
        }

        // --snap: the menu (and one submenu beside it) drawn into the window as they look when open (popups can't be captured)
        public static void Snap(BrowserWindow w, string subId)
        {
            var model = Model(w);
            Action<Action> run = a => { };
            var top = Ui.Flyout(Rows(w, model, run, false), MenuWidth);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            var it = subId == null ? null : model.FirstOrDefault(x => x.Id == subId);
            if (it != null)
            {
                var sp = it.Custom != null ? new StackPanel() : Rows(w, it.Kids, run, true);
                if (it.Custom != null) foreach (var e in it.Custom(run)) sp.Children.Add(e);
                var f = Ui.Flyout(sp, SubWidth); f.VerticalAlignment = VerticalAlignment.Top;
                int idx = model.Where(x => !x.Separator).ToList().IndexOf(it);
                f.Measure(new Size(SubWidth, double.PositiveInfinity)); top.Measure(new Size(MenuWidth, double.PositiveInfinity));
                double rowY = 6; foreach (var x in model) { if (x == it) break; rowY += x.Separator ? 11 : x.Zoom ? 34 : 31; }
                f.Margin = new Thickness(0, Math.Max(0, Math.Min(rowY, 800 - 110 - f.DesiredSize.Height)), 2, 0);
                row.Children.Add(f);
            }
            top.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(top);
            Grid.SetColumnSpan(row, 3);
            w.ContentGrid.Children.Add(row);
        }
    }

    public partial class BrowserWindow
    {
        public void ShowHistory() { NewTab(Omni.HistoryUrl, true); }
        // 1.13: Settings, one click away on the toolbar
        Button settingsBtn;
        public Button SettingsButton { get { return settingsBtn; } }
        Button BuildSettingsButton() { settingsBtn = Ui.Tool("gear", "Settings (Ctrl+,)", () => NewTab(Omni.SettingsUrl, true), 14); return settingsBtn; }
    }
}
