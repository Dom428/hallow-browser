// Hallow Browser 1.2 - the command palette (Ctrl+K): one box that finds open tabs, history (the local history store), bookmarks,
// every menu action and setting, sidebar panels, workspaces and saved sessions - with fuzzy matching. Enter runs the top one.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowBrowser
{
    public class PaletteItem
    {
        public string Kind, Title, Sub, Icon, Keys; public Action Run; public int Score;
        public override string ToString() { return Kind + ": " + Title; }
    }

    public static class Palette
    {
        static readonly Dictionary<string, int> weight = new Dictionary<string, int> { { "Tab", 40 }, { "Bookmark", 30 }, { "Panel", 25 }, { "Workspace", 22 }, { "Command", 20 }, { "Setting", 18 }, { "Session", 15 }, { "History", 0 } };

        // ---- the search (the same code the box uses; tests call it directly)
        public static List<PaletteItem> Search(BrowserWindow w, string q, int max = 12)
        {
            q = (q ?? "").Trim();
            var all = new List<PaletteItem>();
            foreach (var t in w.Tabs.ToList())
            {
                var tt = t;
                all.Add(new PaletteItem { Kind = "Tab", Title = t.Title, Sub = Omni.Display(t.Url) + (t.Group != null ? "  -  group " + t.Group.Name : ""), Icon = t == w.Active ? "eye" : "globe", Run = () => w.Activate(tt) });
            }
            foreach (var p in w.Side.Panels.ToList())
            {
                var pp = p;
                all.Add(new PaletteItem { Kind = "Panel", Title = p.Site.Name, Sub = "Sidebar panel  -  " + Omni.Display(p.Site.Url), Icon = SidebarRules.Brand(p.Site.Url) ?? "table-columns", Run = () => w.Side.Show(pp) });
            }
            if (w.Bookmarks != null)
                foreach (var b in w.Bookmarks.All())
                {
                    var u = b.Url;
                    all.Add(new PaletteItem { Kind = "Bookmark", Title = b.Title ?? u, Sub = Omni.Display(u), Icon = "bookmark", Run = () => w.Navigate(u, false) });
                }
            all.AddRange(Commands(w));
            // 1.7: every card and setting of the Settings page (opens Settings there, filtered to it)
            if (q.Length > 0) foreach (var e in w.SettingsIndex()) { var ee = e; all.Add(new PaletteItem { Kind = "Setting", Title = e.Label ?? e.Card, Sub = "Settings > " + e.Section + " > " + e.Card, Icon = "sliders", Run = () => w.OpenSettingsAt(ee.Label ?? ee.Card) }); }
            if (q.Length > 0) foreach (var it in all) it.Score = Score(q, it);
            else foreach (var it in all) it.Score = it.Kind == "Tab" ? 0 : -1;   // empty box: your tabs
            var res = all.Where(x => x.Score >= 0).ToList();
            if (q.Length > 0 && w.History != null)
            {
                var open = new HashSet<string>(w.Tabs.Select(t => t.Url ?? ""));
                foreach (var h in w.History.Search(q, 40))
                {
                    if (open.Contains(h.Key.Url)) continue;
                    var u = h.Key.Url;
                    res.Add(new PaletteItem { Kind = "History", Title = string.IsNullOrWhiteSpace(h.Key.Title) ? Omni.Display(u) : h.Key.Title, Sub = Omni.Display(u), Icon = "clock-rotate-left", Score = h.Value, Run = () => w.Navigate(u, false) });
                }
            }
            res = res.OrderByDescending(x => x.Score + (q.Length > 0 ? weight[x.Kind] : 0)).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
            if (q.Length > 0)
            {
                var go = Omni.Interpret(q, w.S.Search);
                var isSearch = go != null && go.StartsWith(Omni.SearchUrl(w.S.Search, ""), StringComparison.OrdinalIgnoreCase);
                res.Add(new PaletteItem { Kind = "Search", Title = isSearch ? "Search " + Omni.EngineName(w.S.Search) + " for \"" + q + "\"" : "Go to " + Omni.Display(go), Sub = isSearch ? null : go, Icon = isSearch ? "magnifying-glass" : "arrow-right", Score = 0, Run = () => w.Navigate(q, false) });
            }
            return res;
        }
        static int Score(string q, PaletteItem it)
        {
            var a = Fuzzy.Score(q, it.Title); var b = it.Sub == null ? -1 : Fuzzy.Score(q, it.Sub) - 20;
            var c = Fuzzy.Score(q, it.Kind) - 40;   // "tab", "command"...
            return Math.Max(a, Math.Max(b, c));
        }

        // ---- every menu action and setting as a command
        public static List<PaletteItem> Commands(BrowserWindow w)
        {
            var S = w.S; var l = new List<PaletteItem>();
            Action<string, string, string, Action> C = (title, keys, icon, run) => l.Add(new PaletteItem { Kind = "Command", Title = title, Keys = keys, Icon = icon, Run = run });
            Action after = () => { S.Save(); w.ReloadSettings(); };
            Action<string, bool, Action<bool>, Action> T = (name, on, set, then) => l.Add(new PaletteItem { Kind = "Command", Title = "Setting: " + name + (on ? " - turn off" : " - turn on"), Sub = on ? "On now" : "Off now", Icon = on ? "check" : "circle-half-stroke", Run = () => { set(!on); after(); if (then != null) then(); } });
            var a = w.Active;
            // tabs and pages
            C("New tab", "Ctrl+T", "plus", () => w.NewTab(Omni.NewTabUrl, true));
            C("Reopen closed tab", "Ctrl+Shift+T", "rotate-left", w.ReopenClosed);
            if (a != null)
            {
                C("Close tab", "Ctrl+W", "xmark", () => w.CloseTab(a));
                C("Duplicate tab", null, "copy", () => w.Duplicate(a));
                C(a.Muted ? "Unmute tab" : "Mute tab", "Ctrl+Shift+M", a.Muted ? "volume-high" : "volume-xmark", () => w.ToggleMute(a));
                C("Mute all other tabs", null, "volume-xmark", () => w.MuteOthers(a));
                C("Close other tabs", null, "xmark", () => w.CloseOthers(a));
                C("Put tab to sleep", null, "moon", () => w.Discard(a));
                C("Pop out video", null, "up-right-from-square", () => Pip.PopOut(w, a));
                if (Omni.IsWeb(a.Url))   // 1.7: translate, and casting through Edge (WebView2 can't cast)
                {
                    C(a.Translated ? "Show original (undo translation)" : "Translate this page", null, "language", () => { if (a.Translated) { var _ = w.ShowOriginal(a); } else if (Translator.Ready(S)) { var _ = w.TranslateTab(a, false); } else w.OpenSettingsAt("translate"); });
                    C("Open in Edge to cast", null, "tv", () => w.OpenInEdgeToCast(a));
                    if (BrowserWindow.IsStream(a.Url)) { C("Clip it (Hallow Clips saves the last 30 s)", null, "clapperboard", () => { var _ = w.ClipIt(a); }); C(w.Theatre ? "Leave theatre mode" : "Theatre mode", "Alt+Shift+T", "tv", w.ToggleTheatre); }   // 1.8
                }
                C("Reload", "F5", "arrow-rotate-right", () => { if (a.Core != null) a.Core.Reload(); });
                C(w.IsBookmarked(a) ? "Remove bookmark" : "Bookmark this page", "Ctrl+D", "bookmark", () => w.ToggleBookmark(a));
                C(a.ReaderOn ? "Leave reader mode" : "Reader mode", "F9", "book-open", () => w.ToggleReader(a));
                C("Add tab to a new group", null, "layer-group", () => w.GroupFromMenu(a));
            C("Group tabs: auto-group by site", null, "layer-group", () => w.AutoGroupBySite());   // 1.9
            C("Group tabs...", null, "layer-group", () => w.GroupTabsFlyout(null));
                if (a.Group != null) C("Remove tab from its group", null, "layer-group", () => w.RemoveFromGroup(a));
                foreach (var g in w.Groups.ToList()) { var gg = g; if (a.Group != g) C("Add tab to group " + g.Name, null, "layer-group", () => w.AddToGroup(a, gg)); C((g.Collapsed ? "Expand" : "Collapse") + " group " + g.Name, null, "layer-group", () => w.ToggleCollapse(gg)); C("Put group " + g.Name + " to sleep", null, "moon", () => w.SleepGroup(gg)); }
            }
            C("Downloads", "Ctrl+J", "download", () => w.ShowDownloads());
            C("History (hallow://history)", "Ctrl+H", "clock-rotate-left", w.ShowHistory);   // 1.13
            foreach (var r in HistoryPageRules.Ranges) { var rr = r; C("Clear history: " + r[1].ToLowerInvariant(), null, "broom", () => { var n = w.ClearHistory(rr[0]); w.Toast(n.ToString("N0") + " pages cleared from history.", 4); }); }
            // 1.5
            C("All downloads (hallow://downloads)", null, "download", () => w.NewTab(Omni.DownloadsUrl, true));
            C("Reopen closed window", null, "window-restore", () => Program.ReopenClosedWindow());
            C("Media playing in your tabs", null, "music", () => { var _ = w.MediaFlyoutAtButton(); });
            C(S.ForceDark ? "Show pages as sites draw them" : "Make pages dark", null, "moon", w.ToggleForceDark);
            if (a != null && Omni.IsWeb(a.Url)) C(w.WantsDark(a.Url) ? "Don't make this site dark" : "Make this site dark", null, "circle-half-stroke", () => w.ToggleSiteDark(a));
            T("Search suggestions (sends what you type to " + Omni.EngineName(S.Search) + ")", S.SearchSuggest, v => S.SearchSuggest = v, null);
            T("Mouse gestures", S.Gestures, v => S.Gestures = v, w.GesturesChanged);
            T("Pop out a playing video when I switch tabs", S.AutoPip, v => S.AutoPip = v, null);
            T("Check downloads with Microsoft Defender", S.DownloadScan, v => S.DownloadScan = v, null);
            T("Offer to save logins in Hallow Keys", S.SaveLogins, v => S.SaveLogins = v, null);
            C("Find in page", "Ctrl+F", "magnifying-glass", w.ShowFind);
            C("Print...", "Ctrl+P", "star", () => { if (a != null && a.Core != null) a.Core.ShowPrintUI(); });
            C("Zoom in", "Ctrl++", "magnifying-glass-plus", () => w.Zoom(1));
            C("Zoom out", "Ctrl+-", "magnifying-glass-minus", () => w.Zoom(-1));
            C("Reset zoom", "Ctrl+0", "magnifying-glass", () => w.SetZoom(1.0));
            C("Full screen", "F11", "expand", () => w.SetFullscreen(true));
            C("Developer tools", "F12", "terminal", () => { if (a != null && a.Core != null) a.Core.OpenDevToolsWindow(); });
            // power features
            C(S.VerticalTabs ? "Horizontal tabs (in the title bar)" : "Vertical tabs", null, "table-list", w.ToggleVerticalTabs);
            if (S.VerticalTabs) C(S.VTabsCollapsed ? "Expand the tab column" : "Collapse the tab column (icons only)", null, S.VTabsCollapsed ? "angles-right" : "angles-left", w.ToggleVTabsCollapsed);
            C("Split view with the next tab", "Ctrl+click a tab", "table-columns", () => { if (a != null) { var i = w.Tabs.IndexOf(a); if (w.Tabs.Count > 1) w.AddToSplit(w.Tabs[(i + 1) % w.Tabs.Count]); } });
            if (w.SplitOn) { C("Swap the split sides", null, "right-left", w.SwapSplit); C("Close split view", null, "xmark", w.CloseSplit); }
            C("Screenshot: visible area - copy", "Ctrl+Shift+X", "desktop", () => { var _ = Shots.Take(w, "visible", false, null); });
            C("Screenshot: visible area - save", null, "desktop", () => { var _ = Shots.Take(w, "visible", true, null); });
            C("Screenshot: full page - copy", null, "expand", () => { var _ = Shots.Take(w, "full", false, null); });
            C("Screenshot: full page - save", null, "expand", () => { var _ = Shots.Take(w, "full", true, null); });
            C("Screenshot: drag a region - copy", null, "crop-simple", () => { var _ = Shots.Take(w, "region", false, null); });
            C("Screenshot: drag a region - save", null, "crop-simple", () => { var _ = Shots.Take(w, "region", true, null); });
            C("Bookmarks manager", "Ctrl+Shift+O", "bookmark", () => w.ShowBookmarks());
            C(w.S.BookmarksBar ? "Hide the bookmarks bar" : "Show the bookmarks bar", "Ctrl+Shift+B", "bookmark", () => w.ToggleBookmarksBar());   // 1.3
            C("Import bookmarks and history from another browser", null, "file-import", () => w.ShowImport(w.Address));
            C("Sync now", null, "arrows-rotate", () => { if (w.SyncReady) { var _ = w.SyncNow(true); } else w.Toast("Sync is off - turn it on in Settings > Sync.", 4); });
            if (w.Active != null && Omni.IsWeb(w.Active.Url)) C("Pin this page to the new tab page", null, "thumbtack", () => w.PinToNewTab(w.Active.Url, w.Active.Title));
            C("Clear history", null, "clock-rotate-left", () => w.ClearHistory());
            C("New workspace", null, "briefcase", () => w.NewWorkspace(null, null));
            C("Save this session...", null, "floppy-disk", () => w.ShowWorkspaces(true));
            // the rest of the menu
            C(S.SidebarOn ? "Hide the sidebar" : "Show the sidebar", "Ctrl+Shift+S", "table-columns", () => w.Side.Toggle());
            C("Hot tabs (CPU and memory)", "Shift+Esc", "fire", () => w.ShowHotTabs());
            C("Hallow quick panel", null, "gamepad", () => w.ShowQuick());
            C("Hallow Shield settings", null, "shield-halved", () => Panels.OpenShieldSettings(w));
            C("Clear browsing data...", "Ctrl+Shift+Del", "broom", () => Panels.ClearData(w));
            C("Settings", "Ctrl+,", "gear", () => w.NewTab(Omni.SettingsUrl, true));
            if (w.Update != null) C("Update to " + w.Update.Version.ToString(2), null, "download", () => Panels.DoUpdate(w));
            C("Exit", null, "xmark", w.Close);
            // workspaces and saved sessions
            foreach (var ws in w.Spaces == null ? new List<Workspace>() : w.Spaces.Items.ToList())
            {
                var id = ws.Id; if (id == w.SpaceId) continue;
                l.Add(new PaletteItem { Kind = "Workspace", Title = "Switch to " + ws.Name, Sub = WorkspaceRules.TabCount(ws.Session) + " tabs", Icon = "briefcase", Run = () => { var _ = w.SwitchWorkspace(id); } });
            }
            foreach (var s in BrowserSettings.LoadSaved())
            {
                var n = s.Name;
                l.Add(new PaletteItem { Kind = "Session", Title = "Restore session " + s.Name, Sub = WorkspaceRules.TabCount(s.Session) + " tabs, saved " + (s.Saved ?? "").Replace('T', ' '), Icon = "floppy-disk", Run = () => { var _ = w.RestoreSaved(n); } });
            }
            // settings
            foreach (var e in BrowserSettings.Engines) { var ee = e; if (S.Search != e) C("Setting: search with " + Omni.EngineName(e), null, "magnifying-glass", () => { S.Search = ee; after(); Engine.WriteNewTab(S, Program.AccentHex); }); }
            foreach (var lk in new[] { "follow", "light", "dark" }) { var v = lk; if (S.Look != v) C("Setting: look - " + (v == "follow" ? "follow Hallow" : v), null, v == "light" ? "sun" : v == "dark" ? "moon" : "circle-half-stroke", () => { S.Look = v; after(); w.Dispatcher.BeginInvoke(new Action(Program.ReloadTheme)); }); }
            foreach (var pc in new[] { "follow", "dark", "auto", "light" }) { var v = pc; if (S.PageColors != v) C("Setting: pages - " + (v == "follow" ? "follow the browser" : v == "auto" ? "like Windows" : v), null, "circle-half-stroke", () => { S.PageColors = v; after(); w.ApplyPageColors(); }); }
            foreach (var m in BrowserSettings.SleepChoices) { var v = m; if (S.SleepMinutes != v) C("Setting: background tabs sleep after " + (v == 0 ? "never" : v + " min"), null, "moon", () => { S.SleepMinutes = v; after(); }); }
            foreach (var m in BrowserSettings.RamChoices) { var v = m; if (S.RamLimitMb != v) C("Setting: RAM limiter " + (v == 0 ? "off" : v / 1024 + " GB"), null, "memory", () => { S.RamLimitMb = v; after(); w.Tick(true); }); }
            foreach (var k in BrowserSettings.BandwidthChoices) { var v = k; if (S.BandwidthKBps != v) C("Setting: bandwidth limiter " + (v == 0 ? "off" : v >= 1024 ? v / 1024 + " MB/s" : v + " KB/s"), null, "gauge-high", () => { S.BandwidthKBps = v; after(); w.Tick(true); }); }
            foreach (var m in BrowserSettings.PanelSleepChoices) { var v = m; if (S.SidebarSleepMinutes != v) C("Setting: hidden panels sleep after " + (v == 0 ? "never" : v + " min"), null, "moon", () => { S.SidebarSleepMinutes = v; after(); }); }
            foreach (var sd in new[] { "auto", "left", "right" }) { var v = sd; if (S.SidebarSide != v) C("Setting: sidebar on the " + (v == "auto" ? "side away from vertical tabs" : v), null, "table-columns", () => { S.SidebarSide = v; after(); w.ApplyTabLayout(); }); }
            T("Open the tabs from last time", S.RestoreSession, v => S.RestoreSession = v, null);
            T("Block third-party cookies", S.BlockThirdPartyCookies, v => S.BlockThirdPartyCookies = v, null);
            T("Global Privacy Control and Do Not Track", S.DoNotTrack, v => S.DoNotTrack = v, null);
            T("Strict tracking prevention", S.StrictTracking, v => S.StrictTracking = v, w.ApplyPageColors);
            T("Clear browsing data when I close the browser", S.ClearOnExit, v => S.ClearOnExit = v, null);
            T("Keep a history (Ctrl+K searches it)", S.KeepHistory, v => S.KeepHistory = v, null);
            T("Use the Hallow accent", S.UseHallowAccent, v => S.UseHallowAccent = v, Program.ReloadTheme);
            T("Gaming-light mode", S.GameLight, v => S.GameLight = v, () => w.Tick(true));
            T("CPU limiter", S.CpuLimiter, v => S.CpuLimiter = v, () => w.Tick(true));
            T("Warm up the connection to the top suggestion", S.Preconnect, v => S.Preconnect = v, null);
            T("Bandwidth limiter only while a game runs", S.BandwidthGameOnly, v => S.BandwidthGameOnly = v, () => w.Tick(true));
            T("Live notifications", S.LiveOn, v => S.LiveOn = v, null);
            T("Get new Shield versions between browser updates", S.ShieldAutoUpdate, v => S.ShieldAutoUpdate = v, null);
            T("Fill logins from Hallow Keys", S.KeysOn, v => S.KeysOn = v, null);
            // 1.13: every action of the main menu is here too (the ones not listed above under their own name)
            var have = new HashSet<string>(l.Select(x => x.Title), StringComparer.OrdinalIgnoreCase);
            foreach (var it in Menu113.Leaves(Menu113.Model(w))) if (it.Run != null && it.Enabled && have.Add(it.Text)) C(it.Text, it.Keys, it.Icon, it.Run);
            return l;
        }

        // ---- the box
        static Popup pop; static TextBox box; static StackPanel list; static List<PaletteItem> shown = new List<PaletteItem>(); static int sel;
        public static bool IsOpen { get { return pop != null && pop.IsOpen; } }
        public static void Show(BrowserWindow w)
        {
            if (IsOpen) { pop.IsOpen = false; return; }
            Panels.CloseFlyouts();
            var root = (FrameworkElement)w.Content;
            var fly = Build(w);
            pop = new Popup { Child = fly, PlacementTarget = root, Placement = PlacementMode.Relative, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None,
                HorizontalOffset = Math.Max(8, (root.ActualWidth - 640) / 2), VerticalOffset = 54 };
            pop.Opened += (a, b) => w.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); Keyboard.Focus(box); }), System.Windows.Threading.DispatcherPriority.Input);
            pop.Closed += (a, b) => { if (w.Active != null && w.Active.View != null && !BrowserSettings.Offscreen) try { w.Active.View.Focus(); } catch { } };
            pop.IsOpen = true;
        }
        // --snap: the same panel with a made-up query
        public static FrameworkElement SnapPanel(BrowserWindow w, string query) { var f = Build(w); box.Text = query; return f; }
        static Border Build(BrowserWindow w)
        {
            var g = new StackPanel();
            box = new TextBox { Style = Ui.S("Field"), Height = 36, FontSize = 14, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(2, 2, 2, 6), VerticalContentAlignment = VerticalAlignment.Center };
            var hint = Ui.Muted("Tabs, history, bookmarks, commands, settings, panels...", 11); hint.Margin = new Thickness(10, 0, 10, 6);
            list = new StackPanel();
            g.Children.Add(box); g.Children.Add(hint); g.Children.Add(list);
            var fly = Ui.Flyout(g, 640); fly.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            box.TextChanged += (a, b) => Fill(w);
            box.PreviewKeyDown += (a, b) =>
            {
                if (b.Key == Key.Down) { Move(1); b.Handled = true; }
                else if (b.Key == Key.Up) { Move(-1); b.Handled = true; }
                else if (b.Key == Key.Enter) { RunSel(); b.Handled = true; }
                else if (b.Key == Key.Escape) { if (pop != null) pop.IsOpen = false; b.Handled = true; }
            };
            Fill(w);
            return fly;
        }
        static void Fill(BrowserWindow w)
        {
            shown = Search(w, box.Text, 12); sel = 0; list.Children.Clear();
            for (int i = 0; i < shown.Count; i++)
            {
                var it = shown[i]; int idx = i;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(Ui.Icon(it.Icon ?? "globe", 13, it.Kind == "Command" ? "Lilac" : "Muted"));
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                tx.Children.Add(new TextBlock { Text = it.Title, TextTrimming = TextTrimming.CharacterEllipsis });
                if (!string.IsNullOrEmpty(it.Sub)) { var s = Ui.Muted(it.Sub, 11); s.TextWrapping = TextWrapping.NoWrap; s.TextTrimming = TextTrimming.CharacterEllipsis; tx.Children.Add(s); }
                Grid.SetColumn(tx, 1); g.Children.Add(tx);
                var k = Ui.Muted((it.Keys != null ? it.Keys + "   " : "") + it.Kind.ToUpperInvariant(), 10); k.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"); k.VerticalAlignment = VerticalAlignment.Center; k.Margin = new Thickness(12, 0, 4, 0);
                Grid.SetColumn(k, 2); g.Children.Add(k);
                var b = new Button { Content = g, Style = Ui.S("MenuRow") };
                b.Click += (a, e) => { sel = idx; RunSel(); };
                list.Children.Add(b);
            }
            Mark();
        }
        static void Move(int d) { if (shown.Count == 0) return; sel = (sel + d + shown.Count) % shown.Count; Mark(); }
        static void Mark() { for (int i = 0; i < list.Children.Count; i++) { var b = (Button)list.Children[i]; if (i == sel) b.SetResourceReference(Control.BackgroundProperty, "PanelHi"); else b.ClearValue(Control.BackgroundProperty); } }
        static void RunSel()
        {
            if (sel < 0 || sel >= shown.Count) return;
            var it = shown[sel]; if (pop != null) pop.IsOpen = false;
            try { it.Run(); } catch { }
        }
    }
}
