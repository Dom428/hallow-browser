// Hallow Browser 1.2 - the window's power features: vertical tabs, tab groups, workspaces and saved sessions, split view,
// history and bookmarks, reader mode and muting. (The command palette is Palette.cs, screenshots Shots.cs, the reader view Reader.cs.)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        public readonly List<TabGroup> Groups = new List<TabGroup>();
        public HistoryStore History; public BookmarkStore Bookmarks;
        public WorkspaceStore Spaces; public string SpaceId;
        public Tab SplitL, SplitR; double splitRatio = 0.5;
        Border vtabs; StackPanel vlist; ColumnDefinition vtabsCol; Button vCollapseBtn; TextBlock vHead; ContentControl vNewHost;
        Button wsBtn; TextBlock wsText;
        ColumnDefinition splitC0, splitC1, splitC2; GridSplitter splitBar;
        Button readerBtn, starBtn;
        bool dragActive, building, switching; TabGroup dropChip;
        DispatcherTimer historyTimer;

        Panel TabPanel { get { return S.VerticalTabs ? (Panel)vlist : strip; } }
        public bool SidebarRight { get { return S.SidebarSide == "right" || (S.SidebarSide == "auto" && S.VerticalTabs); } }

        // ================================================================ built by Build()
        FrameworkElement BuildWorkspaceButton()
        {
            wsText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6, 0, 6, 0), Text = "Default" };
            wsText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            wsBtn = new Button { Style = Ui.S("Tool"), Width = double.NaN, Height = 28, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                Content = Ui.Row(Ui.Icon("briefcase", 12, "Lilac"), wsText, Ui.Icon("chevron-down", 8, "Muted")), ToolTip = "Workspaces and saved sessions" };
            wsBtn.Click += (a, b) => ShowWorkspaces(false); Chrome(wsBtn);
            return wsBtn;
        }
        void BuildVerticalTabs(Grid body)
        {
            vtabsCol = body.ColumnDefinitions[0]; vtabsCol.Width = new GridLength(0);
            vtabs = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Visibility = Visibility.Collapsed };
            vtabs.SetResourceReference(Border.BackgroundProperty, "Strip"); vtabs.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new DockPanel { Margin = new Thickness(6, 6, 6, 2) };
            vCollapseBtn = Ui.Tool("angles-left", "Icons only", ToggleVTabsCollapsed, 11); vCollapseBtn.Width = 28; vCollapseBtn.Height = 26; DockPanel.SetDock(vCollapseBtn, Dock.Right);
            head.Children.Add(vCollapseBtn);
            vHead = Ui.Label("Tabs"); vHead.Margin = new Thickness(8, 0, 0, 0); vHead.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(vHead);
            g.Children.Add(head);
            vlist = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            var sv = new ScrollViewer { Content = vlist, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Background = Brushes.Transparent };
            sv.MouseRightButtonUp += (a, b) => { if (!b.Handled) { StripMenu(sv); b.Handled = true; } };
            Grid.SetRow(sv, 1); g.Children.Add(sv);
            vNewHost = new ContentControl { Focusable = false, IsTabStop = false, Margin = new Thickness(4, 0, 4, 6) }; Grid.SetRow(vNewHost, 2); g.Children.Add(vNewHost);
            vtabs.Child = g;
            Grid.SetColumn(vtabs, 0); body.Children.Add(vtabs);
        }
        void BuildSplit()
        {
            splitC0 = new ColumnDefinition(); splitC1 = new ColumnDefinition { Width = new GridLength(0) }; splitC2 = new ColumnDefinition { Width = new GridLength(0) };
            content.ColumnDefinitions.Add(splitC0); content.ColumnDefinitions.Add(splitC1); content.ColumnDefinitions.Add(splitC2);
            splitBar = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Columns, Visibility = Visibility.Collapsed, ToolTip = "Drag to resize the two sides" };
            splitBar.SetResourceReference(Control.BackgroundProperty, "LineDim");
            splitBar.DragCompleted += (a, b) => { var t = splitC0.ActualWidth + splitC2.ActualWidth; if (t > 0) splitRatio = Math.Max(0.15, Math.Min(0.85, splitC0.ActualWidth / t)); LayoutSplit(); };
            Grid.SetColumn(splitBar, 1); content.Children.Add(splitBar);
        }
        void BuildAddressExtras(Panel inBar)
        {
            readerBtn = Ui.Tool("book-open", "Reader mode (F9)", () => ToggleReader(Active), 13); readerBtn.Height = 24; readerBtn.Width = 28; readerBtn.Visibility = Visibility.Collapsed;
            starBtn = Ui.Tool("star", "Bookmark this page (Ctrl+D)", () => ToggleBookmark(Active), 12); starBtn.Height = 24; starBtn.Width = 28; starBtn.Visibility = Visibility.Collapsed;
            inBar.Children.Add(readerBtn); inBar.Children.Add(starBtn);
        }
        void UpdateBarPower(Tab t)
        {
            readerBtn.Visibility = t.Readable || t.ReaderOn ? Visibility.Visible : Visibility.Collapsed;
            ((Path)readerBtn.Content).SetResourceReference(Shape.FillProperty, t.ReaderOn ? "AccentBrush" : "Text");
            readerBtn.ToolTip = t.ReaderOn ? "Leave reader mode (F9)" : "Reader mode (F9)";
            starBtn.Visibility = Omni.IsWeb(t.Url) && !t.Internal ? Visibility.Visible : Visibility.Collapsed;
            var on = IsBookmarked(t);
            ((Path)starBtn.Content).SetResourceReference(Shape.FillProperty, on ? "AccentBrush" : "Muted");
            starBtn.ToolTip = on ? "Bookmarked - click to remove (Ctrl+D)" : "Bookmark this page (Ctrl+D)";
        }

        // ================================================================ the strip / column: tabs and group labels in order
        public void RenderStrip()
        {
            if (building) return;
            NormalizeGroups();
            var host = TabPanel; var other = host == strip ? (Panel)vlist : strip;
            if (other.Children.Count > 0) other.Children.Clear();
            var want = new List<UIElement>(); var seen = new HashSet<TabGroup>(); TabGroup cur = null;
            foreach (var t in Tabs)
            {
                if (t.Group != cur) { cur = t.Group; if (cur != null && seen.Add(cur)) { StyleChip(cur); want.Add(cur.Chip); } }
                t.Header.Visibility = t.Group != null && t.Group.Collapsed ? Visibility.Collapsed : Visibility.Visible;
                StyleHeader(t);
                want.Add(t.Header);
            }
            Sync(host, want);
            FitTabs();
        }
        // puts the panel's children in the wanted order with as few moves as possible - and never moves the tab being dragged
        static void Sync(Panel p, List<UIElement> want)
        {
            for (int i = 0; i < want.Count; i++)
            {
                var e = want[i];
                if (i < p.Children.Count && p.Children[i] == e) continue;
                if (e.IsMouseCaptured && p.Children.Contains(e)) { while (i < p.Children.Count && p.Children[i] != e) p.Children.RemoveAt(i); continue; }
                if (p.Children.Contains(e)) p.Children.Remove(e);
                p.Children.Insert(Math.Min(i, p.Children.Count), e);
            }
            while (p.Children.Count > want.Count) p.Children.RemoveAt(p.Children.Count - 1);
        }
        void NormalizeGroups()
        {
            if (dragActive) return;
            var order = GroupRules.Contiguous(Tabs, t => t.Group);
            if (!order.SequenceEqual(Tabs)) { Tabs.Clear(); Tabs.AddRange(order); }
            Groups.RemoveAll(g => !Tabs.Any(t => t.Group == g));
            foreach (var t in Tabs) if (t.Group != null && !Groups.Contains(t.Group)) Groups.Add(t.Group);
        }
        void StyleHeader(Tab t)
        {
            var g = (Grid)t.Header.Child; bool v = S.VerticalTabs, ic = v && S.VTabsCollapsed;
            var key = t.Group != null ? GroupRules.Key(t.Group.Color) : null;
            if (!v)
            {
                t.Header.CornerRadius = new CornerRadius(8, 8, 0, 0); t.Header.Margin = new Thickness(0, 6, 1, 0); t.Header.HorizontalAlignment = HorizontalAlignment.Left;
                t.GroupLine.HorizontalAlignment = HorizontalAlignment.Stretch; t.GroupLine.VerticalAlignment = VerticalAlignment.Bottom; t.GroupLine.Width = double.NaN; t.GroupLine.Height = 2; t.GroupLine.Margin = new Thickness(8, 0, 8, 0);
            }
            else
            {
                t.Header.Width = double.NaN; t.Header.HorizontalAlignment = HorizontalAlignment.Stretch; t.Header.CornerRadius = new CornerRadius(6);
                t.Header.Margin = new Thickness(ic ? 6 : t.Group != null ? 16 : 6, 1, 6, 1);
                t.GroupLine.HorizontalAlignment = HorizontalAlignment.Left; t.GroupLine.VerticalAlignment = VerticalAlignment.Stretch; t.GroupLine.Width = 3; t.GroupLine.Height = double.NaN; t.GroupLine.Margin = new Thickness(0, 7, 0, 7);
            }
            t.TitleBlock.Visibility = ic ? Visibility.Collapsed : Visibility.Visible;
            t.RightBox.Visibility = ic ? Visibility.Collapsed : Visibility.Visible;
            t.IconHost.Margin = ic ? new Thickness(12, 0, 0, 0) : new Thickness(10, 0, 8, 0);
            t.GroupLine.Visibility = key != null ? Visibility.Visible : Visibility.Collapsed;
            if (key != null) t.GroupLine.SetResourceReference(Border.BackgroundProperty, key);
            t.SplitMark.Visibility = SplitOn && (t == SplitL || t == SplitR) ? Visibility.Visible : Visibility.Collapsed;
        }
        void StyleChip(TabGroup g)
        {
            if (g.Chip == null)
            {
                var c = new Border { Cursor = Cursors.Hand, Background = Brushes.Transparent }; g.Chip = c; Chrome(c);
                c.MouseLeftButtonDown += (a, b) => b.Handled = true;
                c.MouseLeftButtonUp += (a, b) => { ToggleCollapse(g); b.Handled = true; };
                c.MouseRightButtonUp += (a, b) => { GroupMenu(g); b.Handled = true; };
            }
            var chip = g.Chip; var key = GroupRules.Key(g.Color); int n = Tabs.Count(t => t.Group == g);
            var name = new TextBlock { Text = g.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };
            name.SetResourceReference(TextBlock.ForegroundProperty, key);
            chip.SetResourceReference(Border.BorderBrushProperty, key);
            if (dropChip == g) chip.SetResourceReference(Border.BackgroundProperty, "PanelHi"); else chip.Background = Brushes.Transparent;
            if (!S.VerticalTabs)
            {
                chip.CornerRadius = new CornerRadius(7); chip.BorderThickness = new Thickness(1.5); chip.Padding = new Thickness(8, 1, 8, 1); chip.Margin = new Thickness(4, 6, 4, 0); chip.Height = 22; chip.VerticalAlignment = VerticalAlignment.Center;
                var cnt = Ui.Muted(g.Collapsed ? "  " + n : "", 11); cnt.VerticalAlignment = VerticalAlignment.Center;
                chip.Child = Ui.Row(name, cnt);
            }
            else if (S.VTabsCollapsed)
            {
                chip.CornerRadius = new CornerRadius(5); chip.BorderThickness = new Thickness(1.5); chip.Padding = new Thickness(0); chip.Margin = new Thickness(8, 6, 8, 2); chip.Height = 20;
                var t = new TextBlock { Text = g.Collapsed ? n.ToString() : g.Name.Substring(0, 1).ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                t.SetResourceReference(TextBlock.ForegroundProperty, key); chip.Child = t;
            }
            else
            {
                chip.CornerRadius = new CornerRadius(6); chip.BorderThickness = new Thickness(0); chip.Padding = new Thickness(8, 4, 8, 4); chip.Margin = new Thickness(6, 8, 6, 2); chip.Height = double.NaN;
                var gr = new Grid();
                gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); gr.ColumnDefinitions.Add(new ColumnDefinition()); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }; dot.SetResourceReference(Shape.FillProperty, key); gr.Children.Add(dot);
                Grid.SetColumn(name, 1); gr.Children.Add(name);
                var cnt = Ui.Muted(n.ToString(), 11); cnt.VerticalAlignment = VerticalAlignment.Center; cnt.Margin = new Thickness(6, 0, 6, 0); Grid.SetColumn(cnt, 2); gr.Children.Add(cnt);
                var chev = Ui.Icon(g.Collapsed ? "chevron-right" : "chevron-down", 9, "Muted"); Grid.SetColumn(chev, 3); gr.Children.Add(chev);
                chip.Child = gr;
            }
            chip.ToolTip = g.Name + " (" + n + (n == 1 ? " tab" : " tabs") + ")\nClick: " + (g.Collapsed ? "expand" : "collapse") + "  -  right-click: rename, colour, sleep, ungroup\nDrag a tab here to add it";
        }

        // ---- dragging a tab (reorder, into a group)
        static Rect Bounds(FrameworkElement e, Visual host) { try { return new Rect(e.TranslatePoint(new Point(0, 0), (UIElement)host), e.RenderSize); } catch { return Rect.Empty; } }
        void DragTo(Tab t, Point p)
        {
            var host = TabPanel; bool v = S.VerticalTabs;
            TabGroup over = null;
            foreach (var g in Groups) if (g.Chip != null && g.Chip.Parent == host && Bounds(g.Chip, host).Contains(p)) over = g;
            if (over != dropChip) { var old = dropChip; dropChip = over; if (old != null) StyleChip(old); if (over != null) StyleChip(over); }
            if (over != null) return;
            int slot = -1;
            for (int i = 0; i < Tabs.Count; i++)
            {
                var x = Tabs[i]; if (x == t || x.Header.Visibility != Visibility.Visible) continue;
                var r = Bounds(x.Header, host); if (r.IsEmpty) continue;
                if ((v ? p.Y : p.X) < (v ? r.Top + r.Height / 2 : r.Left + r.Width / 2)) { slot = i; break; }
            }
            int cur = Tabs.IndexOf(t);
            int to = slot < 0 ? Tabs.Count - 1 : slot > cur ? slot - 1 : slot;
            if (to != cur) { Tabs.RemoveAt(cur); Tabs.Insert(to, t); RenderStrip(); }
        }
        // the drop: which group the tab is in now (GroupRules.Drop), then the groups are tidied
        public void DropTab(Tab t)
        {
            dragActive = false;
            int i = Tabs.IndexOf(t); if (i < 0) { dropChip = null; return; }
            var chip = dropChip; dropChip = null;
            var prev = i > 0 ? Tabs[i - 1].Group : null; var next = i < Tabs.Count - 1 ? Tabs[i + 1].Group : null;
            bool others = t.Group != null && Tabs.Any(x => x != t && x.Group == t.Group);
            var g = GroupRules.Drop(prev, next, t.Group, chip, others);
            if (chip != null)
            {
                // to the end of that group
                Tabs.Remove(t); var last = Tabs.FindLastIndex(x => x.Group == chip); Tabs.Insert(last + 1, t);
                if (chip.Collapsed) chip.Collapsed = false;
                StyleChip(chip);
            }
            t.Group = g;
            RenderStrip(); Changed();
        }

        // ---- groups
        public TabGroup NewGroup(Tab t, string name)
        {
            if (t == null) return null;
            var g = new TabGroup { Name = GroupRules.Name(name, Groups.Count + 1), Color = GroupRules.NextColor(Groups.Select(x => x.Color)) };
            Groups.Add(g); t.Group = g; RenderStrip(); Changed(); return g;
        }
        public void AddToGroup(Tab t, TabGroup g)
        {
            if (t == null || g == null || t.Group == g) return;
            Tabs.Remove(t); var last = Tabs.FindLastIndex(x => x.Group == g); Tabs.Insert(last + 1, t);
            t.Group = g; if (g.Collapsed && t == Active) g.Collapsed = false;
            RenderStrip(); Changed();
        }
        public void RemoveFromGroup(Tab t)
        {
            if (t == null || t.Group == null) return;
            var g = t.Group; Tabs.Remove(t); var last = Tabs.FindLastIndex(x => x.Group == g); Tabs.Insert(last + 1, t);
            t.Group = null; RenderStrip(); Changed();
        }
        public void ToggleCollapse(TabGroup g)
        {
            if (g == null) return;
            if (!g.Collapsed && Active != null && Active.Group == g)
            {
                // the tab you're on goes out of sight: go to the nearest tab outside the group
                int i = Tabs.IndexOf(Active);
                var other = Tabs.Skip(i).FirstOrDefault(x => x.Group != g) ?? Tabs.Take(i).LastOrDefault(x => x.Group != g);
                if (other == null) { Toast("Every tab is in this group - open another tab to collapse it.", 4); return; }
                g.Collapsed = true; Activate(other);
            }
            else g.Collapsed = !g.Collapsed;
            RenderStrip(); Changed();
        }
        // every tab of the group sleeps (unloaded: they load again when you click them) - not the one you're on, not one playing sound
        public int SleepGroup(TabGroup g)
        {
            int n = 0;
            foreach (var t in Tabs.Where(x => x.Group == g).ToList()) { if (t == Active || t.Audible || t.PoppedOut || t.View == null) continue; Discard(t); n++; }
            Toast(n == 0 ? "Nothing in " + g.Name + " to put to sleep." : n + (n == 1 ? " tab" : " tabs") + " of " + g.Name + " asleep - they load again when you click them.", 4);
            Changed(); return n;
        }
        public void Ungroup(TabGroup g) { foreach (var t in Tabs.Where(x => x.Group == g)) t.Group = null; Groups.Remove(g); RenderStrip(); Changed(); }
        public void CloseGroup(TabGroup g) { foreach (var t in Tabs.Where(x => x.Group == g).ToList()) CloseTab(t); }
        public void NewTabInGroup(TabGroup g)
        {
            var last = Tabs.FindLastIndex(x => x.Group == g);
            var t = NewTab(Omni.NewTabUrl, true, last + 1); t.Group = g; g.Collapsed = false; RenderStrip(); Changed();
        }
        void GroupMenu(TabGroup g)
        {
            var sp = new StackPanel(); Popup pop = null;
            Action<Action> run = a => { pop.IsOpen = false; a(); };
            var box = new TextBox { Style = Ui.S("Field"), Height = 28, Text = g.Name, Margin = new Thickness(6, 6, 6, 8) };
            Action rename = () => { var n = GroupRules.Name(box.Text, Groups.IndexOf(g) + 1); if (n != g.Name) { g.Name = n; RenderStrip(); Changed(); } };
            box.KeyDown += (a, b) => { if (b.Key == Key.Enter) { rename(); pop.IsOpen = false; b.Handled = true; } };
            box.LostKeyboardFocus += (a, b) => rename();
            sp.Children.Add(box);
            var colors = new WrapPanel { Margin = new Thickness(8, 0, 6, 6) };
            foreach (var c in GroupRules.Colors)
            {
                var cc = c;
                var dot = new Ellipse { Width = 16, Height = 16, StrokeThickness = g.Color == c ? 2 : 0 }; dot.SetResourceReference(Shape.FillProperty, GroupRules.Key(c)); dot.SetResourceReference(Shape.StrokeProperty, "Text");
                var b = new Button { Style = Ui.S("Tool"), Width = 24, Height = 24, Content = dot, ToolTip = GroupRules.Label(c) };
                b.Click += (a, e) => { g.Color = cc; rename(); pop.IsOpen = false; RenderStrip(); Changed(); };
                colors.Children.Add(b);
            }
            sp.Children.Add(colors);
            sp.Children.Add(Ui.MenuRow(g.Collapsed ? "chevron-down" : "chevron-right", g.Collapsed ? "Expand group" : "Collapse group", null, () => run(() => ToggleCollapse(g))));
            sp.Children.Add(Ui.MenuRow("plus", "New tab in group", null, () => run(() => NewTabInGroup(g))));
            sp.Children.Add(Ui.MenuRow("moon", "Put the whole group to sleep", null, () => run(() => SleepGroup(g))));
            sp.Children.Add(Ui.MenuRow("layer-group", "Ungroup", null, () => run(() => Ungroup(g))));
            sp.Children.Add(Ui.MenuRow("xmark", "Close group", null, () => run(() => CloseGroup(g))));
            Panels.CloseFlyouts();
            pop = new Popup { Child = Ui.Flyout(sp, 250), PlacementTarget = g.Chip, Placement = S.VerticalTabs ? PlacementMode.Right : PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
            pop.Opened += (a, b) => { box.Focus(); box.SelectAll(); };
            pop.IsOpen = true;
        }
        // right-click on the strip / column (not on a tab)
        void StripMenu(UIElement at)
        {
            var sp = new StackPanel(); Popup pop = null;
            Action<Action> run = a => { pop.IsOpen = false; a(); };
            sp.Children.Add(Ui.MenuRow("plus", "New tab", "Ctrl+T", () => run(() => NewTab(Omni.NewTabUrl, true))));
            sp.Children.Add(Ui.MenuRow("table-list", S.VerticalTabs ? "Tabs in the title bar" : "Vertical tabs", null, () => run(ToggleVerticalTabs)));
            if (S.VerticalTabs) sp.Children.Add(Ui.MenuRow(S.VTabsCollapsed ? "angles-right" : "angles-left", S.VTabsCollapsed ? "Show tab names" : "Icons only", null, () => run(ToggleVTabsCollapsed)));
            var re = Ui.MenuRow("rotate-left", "Reopen closed tab", "Ctrl+Shift+T", () => run(ReopenClosed)); re.IsEnabled = ClosedCount > 0; sp.Children.Add(re);
            sp.Children.Add(Ui.MenuRow("briefcase", "Workspaces and sessions...", null, () => run(() => ShowWorkspaces(false))));
            Panels.CloseFlyouts();
            pop = new Popup { Child = Ui.Flyout(sp, 260), PlacementTarget = at, Placement = PlacementMode.MousePoint, StaysOpen = false, AllowsTransparency = true };
            pop.IsOpen = true;
        }

        // ---- vertical tabs
        public void ToggleVerticalTabs() { S.VerticalTabs = !S.VerticalTabs; S.Save(); ApplyTabLayout(); Dispatcher.BeginInvoke(new Action(ReloadSettings)); Changed(); }
        public void ToggleVTabsCollapsed() { S.VTabsCollapsed = !S.VTabsCollapsed; S.Save(); ApplyTabLayout(); Changed(); }
        public void ApplyTabLayout()
        {
            bool v = S.VerticalTabs, ic = S.VTabsCollapsed;
            vtabs.Visibility = v && !fullscreen ? Visibility.Visible : Visibility.Collapsed;
            vtabsCol.Width = v && !fullscreen ? new GridLength(ic ? 52 : 240) : new GridLength(0);
            strip.Visibility = v ? Visibility.Collapsed : Visibility.Visible; newBtn.Visibility = strip.Visibility;
            vCollapseBtn.Content = Ui.Icon(ic ? "angles-right" : "angles-left", 11); vCollapseBtn.ToolTip = ic ? "Show tab names" : "Icons only";
            vHead.Visibility = ic ? Visibility.Collapsed : Visibility.Visible;
            if (ic) { var b = Ui.Tool("plus", "New tab (Ctrl+T)", () => NewTab(Omni.NewTabUrl, true), 12); b.HorizontalAlignment = HorizontalAlignment.Center; vNewHost.Content = b; }
            else vNewHost.Content = Ui.MenuRow("plus", "New tab", "Ctrl+T", () => NewTab(Omni.NewTabUrl, true));
            Side.Dock(SidebarRight);
            RenderStrip();
        }

        // ================================================================ split view
        public bool SplitOn { get { return SplitL != null && SplitR != null && SplitL != SplitR && Tabs.Contains(SplitL) && Tabs.Contains(SplitR) && !SplitL.PoppedOut && !SplitR.PoppedOut; } }
        bool SplitShown { get { return SplitOn && (Active == SplitL || Active == SplitR); } }
        bool IsShown(Tab x) { return x == Active || (SplitShown && (x == SplitL || x == SplitR)); }
        // Ctrl+click: the tab goes beside the one you're on (or replaces the other side)
        public void AddToSplit(Tab t)
        {
            if (t == null || Active == null) return;
            if (t == Active) { if (!SplitShown) Toast("Ctrl+click another tab to put it beside this one.", 3); return; }
            if (t.PoppedOut || Active.PoppedOut) { Toast("Bring the popped-out video back first.", 3); return; }
            if (SplitShown) { if (Active == SplitL) SplitR = t; else SplitL = t; }
            else { SplitL = Active; SplitR = t; }
            Activate(Active); RenderStrip(); Changed();
        }
        public void SwapSplit() { if (!SplitOn) return; var x = SplitL; SplitL = SplitR; SplitR = x; LayoutSplit(); Changed(); }
        public void CloseSplit() { SplitL = SplitR = null; if (Active != null) Activate(Active); RenderStrip(); Changed(); }
        void LayoutSplit()
        {
            if (splitC0 == null) return;
            bool on = SplitShown;
            splitC0.Width = on ? new GridLength(splitRatio, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
            splitC1.Width = on ? new GridLength(5) : new GridLength(0);
            splitC2.Width = on ? new GridLength(1 - splitRatio, GridUnitType.Star) : new GridLength(0);
            splitBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement e in content.Children)
            {
                if (e == splitBar) continue;
                var o = Tabs.FirstOrDefault(t => t.View == e || t.Page == e || t.ReaderView == e);
                int col = 0, span = on ? 3 : 1;
                if (on && o != null && o == SplitL) span = 1;
                else if (on && o != null && o == SplitR) { col = 2; span = 1; }
                Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span);
            }
        }
        // the side you click in becomes the active tab (the address bar follows it)
        void ViewFocused(Tab x) { if (x != null && x != Active && SplitShown && (x == SplitL || x == SplitR)) Activate(x); }

        // ================================================================ workspaces and saved sessions
        public Workspace CurrentSpace { get { return Spaces == null ? null : Spaces.Items.FirstOrDefault(x => x.Id == SpaceId); } }
        void LoadSpaces()
        {
            Spaces = BrowserSettings.LoadWorkspaces();
            if (Spaces.Items == null) Spaces.Items = new List<Workspace>();
            Spaces.Items.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Id));
            if (Spaces.Items.Count == 0) { Spaces.Items.Add(new Workspace { Id = "default", Name = "Default" }); Spaces.Current = "default"; }
            if (!Spaces.Items.Any(x => x.Id == Spaces.Current)) Spaces.Current = Spaces.Items[0].Id;
            foreach (var x in Spaces.Items) if (string.IsNullOrWhiteSpace(x.Name)) x.Name = "Workspace";
            SpaceId = Spaces.Current; UpdateSpaceButton();
        }
        void UpdateSpaceButton() { var c = CurrentSpace; if (wsText != null) wsText.Text = c == null ? "Default" : c.Name; }

        // opens a session's tabs and groups (the start, a workspace switch); the tab you'll see is made first, the others wait
        async Task OpenSession(Session session, List<string> urls, bool focusAddress)
        {
            var first = new List<SessionTab>();
            if (session != null && session.Tabs != null) first.AddRange(session.Tabs.Where(t => t != null && t.Url != null && (Omni.IsWeb(t.Url) || Omni.IsNewTab(t.Url) || Omni.IsInternal(t.Url))));
            int activeIdx = session != null ? Math.Max(0, Math.Min(session.Active, first.Count - 1)) : 0;
            int sl = -1, sr = -1; bool hasSplit = session != null && WorkspaceRules.ParseSplit(session.Split, first.Count, out sl, out sr);
            foreach (var u in urls) { first.Add(new SessionTab { Url = u }); activeIdx = first.Count - 1; }
            if (first.Count == 0) first.Add(new SessionTab { Url = Omni.NewTabUrl });
            var gmap = new Dictionary<string, TabGroup>();
            if (session != null && session.Groups != null)
                foreach (var sg in session.Groups)
                    if (sg != null && sg.Id != null && !gmap.ContainsKey(sg.Id)) { var g = new TabGroup { Id = sg.Id, Name = GroupRules.Name(sg.Name, gmap.Count + 1), Color = GroupRules.Normalize(sg.Color), Collapsed = sg.Collapsed }; gmap[sg.Id] = g; Groups.Add(g); }
            building = true;
            try
            {
                for (int i = 0; i < first.Count; i++)
                {
                    var t = new Tab { Url = first[i].Url, Title = first[i].Title ?? (Omni.IsNewTab(first[i].Url) ? "New tab" : Omni.HostOf(first[i].Url) ?? "Tab") };
                    TabGroup tg; if (first[i].Group != null && gmap.TryGetValue(first[i].Group, out tg)) t.Group = tg;
                    AddHeader(t, Tabs.Count); Tabs.Add(t);
                    if (i != activeIdx) { t.Discarded = true; t.LastActive = DateTime.Now.AddSeconds(-i); }
                }
            }
            finally { building = false; }
            var act = Tabs[activeIdx];
            if (hasSplit) { SplitL = Tabs[sl]; SplitR = Tabs[sr]; }
            if (act.Group != null) act.Group.Collapsed = false;
            RenderStrip();
            act.PendingUrl = act.Url;
            if (Omni.IsInternal(act.Url)) { act.Discarded = false; ShowSettingsIn(act); Activate(act); }
            else { await CreateView(act, null); Activate(act); }
            if (focusAddress && Omni.IsNewTab(act.Url) && !BrowserSettings.Offscreen) Address.Focus();
        }
        public Session BuildSession()
        {
            var s = new Session(); var kept = new List<Tab>();
            foreach (var t in Tabs) if (Omni.IsWeb(t.Url) || Omni.IsNewTab(t.Url) || t.Internal) { kept.Add(t); s.Tabs.Add(new SessionTab { Url = t.Url, Title = t.Title, Group = t.Group == null ? null : t.Group.Id }); }
            s.Active = Math.Max(0, kept.IndexOf(Active));
            var used = Groups.Where(g => kept.Any(t => t.Group == g)).ToList();
            if (used.Count > 0) s.Groups = used.Select(g => new SessionGroup { Id = g.Id, Name = g.Name, Color = g.Color, Collapsed = g.Collapsed }).ToList();
            if (SplitOn && kept.Contains(SplitL) && kept.Contains(SplitR)) s.Split = kept.IndexOf(SplitL) + "," + kept.IndexOf(SplitR);
            return s;
        }
        // this workspace's pages close quietly (not into "reopen closed tab")
        void CloseAllQuiet()
        {
            foreach (var t in Tabs.Where(x => x.PoppedOut).ToList()) Pip.Close(this, t, true);
            SplitL = SplitR = null;
            foreach (var t in Tabs)
            {
                if (t.View != null) { var core = t.Core; content.Children.Remove(t.View); if (core != null) { owner.Remove(core); hooked.Remove(core); capped.Remove(core); } try { t.View.Dispose(); } catch { } t.View = null; }
                if (t.Page != null) content.Children.Remove(t.Page);
                if (t.ReaderView != null) content.Children.Remove(t.ReaderView);
            }
            Tabs.Clear(); Groups.Clear(); strip.Children.Clear(); vlist.Children.Clear(); Active = null;
        }
        public async Task SwitchWorkspace(string id)
        {
            if (Spaces == null || id == SpaceId || switching || snap) return;
            var ws = Spaces.Items.FirstOrDefault(x => x.Id == id); if (ws == null) return;
            switching = true;
            try
            {
                SaveSession();
                CloseAllQuiet();
                SpaceId = Spaces.Current = id; BrowserSettings.SaveWorkspaces(Spaces); UpdateSpaceButton();
                await OpenSession(ws.Session, new List<string>(), false);
                SaveSession(); WriteStatus();
                Toast("Workspace " + ws.Name + " (" + Tabs.Count + (Tabs.Count == 1 ? " tab)" : " tabs)"), 2);
            }
            finally { switching = false; }
        }
        public Workspace NewWorkspace(string name, Session s)
        {
            if (Spaces == null) return null;
            var ws = new Workspace { Id = WorkspaceRules.NewId(), Name = WorkspaceRules.UniqueName(name ?? "Workspace " + (Spaces.Items.Count + 1), Spaces.Items.Select(x => x.Name)), Session = s };
            Spaces.Items.Add(ws); BrowserSettings.SaveWorkspaces(Spaces);
            var _ = SwitchWorkspace(ws.Id);
            return ws;
        }
        public void RenameWorkspace(string id, string name)
        {
            var ws = Spaces.Items.FirstOrDefault(x => x.Id == id); if (ws == null) return;
            ws.Name = WorkspaceRules.UniqueName(name, Spaces.Items.Where(x => x != ws).Select(x => x.Name)); BrowserSettings.SaveWorkspaces(Spaces); UpdateSpaceButton();
        }
        public async Task DeleteWorkspace(string id)
        {
            if (Spaces == null || Spaces.Items.Count < 2) { Toast("This is the only workspace.", 3); return; }
            var ws = Spaces.Items.FirstOrDefault(x => x.Id == id); if (ws == null) return;
            if (id == SpaceId) await SwitchWorkspace(Spaces.Items.First(x => x.Id != id).Id);
            Spaces.Items.Remove(ws); BrowserSettings.SaveWorkspaces(Spaces);
            Toast("Workspace " + ws.Name + " deleted.", 3);
        }
        public void MoveToWorkspace(Tab t, string id)
        {
            var ws = Spaces == null ? null : Spaces.Items.FirstOrDefault(x => x.Id == id); if (ws == null || t == null || id == SpaceId || !(Omni.IsWeb(t.Url) || Omni.IsNewTab(t.Url))) return;
            if (ws.Session == null) ws.Session = new Session();
            ws.Session.Tabs.Add(new SessionTab { Url = t.Url, Title = t.Title });
            BrowserSettings.SaveWorkspaces(Spaces);
            CloseTab(t); Toast("Moved to " + ws.Name + ".", 3);
        }
        public string SaveNamed(string name)
        {
            var list = BrowserSettings.LoadSaved();
            var cs = CurrentSpace;
            var n = string.IsNullOrWhiteSpace(name) ? (cs == null ? "Session" : cs.Name) + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm") : name.Trim();
            if (n.Length > 60) n = n.Substring(0, 60).Trim();
            var s = BuildSession();
            list.RemoveAll(x => string.Equals(x.Name, n, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new SavedSession { Name = n, Saved = DateTime.Now.ToString("s"), Session = s });
            if (list.Count > 50) list.RemoveRange(50, list.Count - 50);
            BrowserSettings.SaveSaved(list);
            Toast("Session saved: " + n + " (" + s.Tabs.Count + (s.Tabs.Count == 1 ? " tab)" : " tabs)"), 3);
            return n;
        }
        // a saved session comes back as its own workspace (your current tabs stay where they are)
        public async Task RestoreSaved(string name)
        {
            var s = BrowserSettings.LoadSaved().FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); if (s == null || Spaces == null) return;
            var ws = new Workspace { Id = WorkspaceRules.NewId(), Name = WorkspaceRules.UniqueName(s.Name, Spaces.Items.Select(x => x.Name)), Session = s.Session };
            Spaces.Items.Add(ws); BrowserSettings.SaveWorkspaces(Spaces);
            await SwitchWorkspace(ws.Id);
        }
        public void DeleteSaved(string name) { var list = BrowserSettings.LoadSaved(); list.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); BrowserSettings.SaveSaved(list); }

        // the switcher (top left)
        public void ShowWorkspaces(bool saveNow)
        {
            Popup pop = null;
            var sp = WorkspacesPanel(() => { if (pop != null) pop.IsOpen = false; }, () => { if (pop != null) pop.IsOpen = false; ShowWorkspaces(false); }, saveNow);
            pop = Panels.ShowFlyout(wsBtn, sp, 340); pop.HorizontalOffset = 0;
        }
        public StackPanel WorkspacesPanel(Action close, Action refresh, bool saveNow)
        {
            var sp = new StackPanel();
            Func<string, TextBlock> head = s => { var t = Ui.Label(s); t.Margin = new Thickness(12, 8, 12, 6); return t; };
            Func<FrameworkElement> sep = () => { var b = new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5) }; b.SetResourceReference(Border.BackgroundProperty, "LineDim"); return b; };
            Func<string, string, string, string, Action, UIElement[], FrameworkElement> row = (icon, fg, title, sub, click, tools) =>
            {
                var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var ig = new Grid(); ig.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); ig.ColumnDefinitions.Add(new ColumnDefinition());
                ig.Children.Add(Ui.Icon(icon, 13, fg));
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; tx.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
                if (sub != null) tx.Children.Add(Ui.Muted(sub, 11)); Grid.SetColumn(tx, 1); ig.Children.Add(tx);
                var b = new Button { Content = ig, Style = Ui.S("MenuRow") }; if (click != null) b.Click += (a, e) => click(); else b.IsHitTestVisible = false;
                g.Children.Add(b);
                if (tools != null && tools.Length > 0) { var r = Ui.Row(tools); r.VerticalAlignment = VerticalAlignment.Center; r.Margin = new Thickness(0, 0, 4, 0); Grid.SetColumn(r, 1); g.Children.Add(r); }
                return g;
            };
            Func<string, string, Action<string>, FrameworkElement> input = (initial, ok, done) =>
            {
                var box = new TextBox { Style = Ui.S("Field"), Height = 28, Width = 214, Text = initial ?? "", Margin = new Thickness(0, 0, 8, 0) };
                Action go = () => done(box.Text);
                box.KeyDown += (a, b) => { if (b.Key == Key.Enter) { go(); b.Handled = true; } else if (b.Key == Key.Escape) { refresh(); b.Handled = true; } };
                box.Loaded += (a, b) => { box.Focus(); box.SelectAll(); };
                var r = Ui.Row(box, Ui.Btn(ok, go, true)); r.Margin = new Thickness(12, 4, 8, 6); return r;
            };
            Func<string, string, Action, Button> tool = (icon, tip, act) => { var b = Ui.Tool(icon, tip, act, 11); b.Width = 26; b.Height = 26; return b; };
            sp.Children.Add(head("Workspaces"));
            foreach (var ws in Spaces.Items.ToList())
            {
                var id = ws.Id; bool cur = id == SpaceId; int n = cur ? Tabs.Count : WorkspaceRules.TabCount(ws.Session);
                FrameworkElement r = null; var holder = new ContentControl { Focusable = false, IsTabStop = false };
                var tools = new List<UIElement> { tool("pen", "Rename", () => holder.Content = input(ws.Name, "Rename", v => { RenameWorkspace(id, v); refresh(); })) };
                if (Spaces.Items.Count > 1) tools.Add(tool("trash-can", "Delete this workspace (closes its " + n + (n == 1 ? " tab)" : " tabs)"), () => { close(); var _ = DeleteWorkspace(id); }));
                r = row("briefcase", cur ? "AccentBrush" : "Lilac", ws.Name, n + (n == 1 ? " tab" : " tabs") + (cur ? "  -  here now" : ""), cur ? null : (Action)(() => { close(); var _ = SwitchWorkspace(id); }), tools.ToArray());
                holder.Content = r; sp.Children.Add(holder);
            }
            var nw = new ContentControl { Focusable = false, IsTabStop = false };
            nw.Content = row("plus", "Lilac", "New workspace", null, () => nw.Content = input("Workspace " + (Spaces.Items.Count + 1), "Create", v => { close(); NewWorkspace(v, null); }), null);
            sp.Children.Add(nw);
            sp.Children.Add(sep());
            sp.Children.Add(head("Saved sessions"));
            var sv = new ContentControl { Focusable = false, IsTabStop = false };
            Action saveRow = () => sv.Content = input((CurrentSpace == null ? "Session" : CurrentSpace.Name) + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm"), "Save", v => { SaveNamed(v); refresh(); });
            sv.Content = row("floppy-disk", "Lilac", "Save this session...", Tabs.Count + " tabs" + (Groups.Count > 0 ? ", " + Groups.Count + (Groups.Count == 1 ? " group" : " groups") : ""), saveRow, null);
            sp.Children.Add(sv);
            if (saveNow) saveRow();
            var saved = BrowserSettings.LoadSaved();
            foreach (var s in saved)
            {
                var name = s.Name;
                sp.Children.Add(row("clock-rotate-left", "Muted", s.Name, WorkspaceRules.TabCount(s.Session) + " tabs  -  " + (s.Saved ?? "").Replace('T', ' '), () => { close(); var _ = RestoreSaved(name); },
                    new UIElement[] { tool("trash-can", "Delete this saved session", () => { DeleteSaved(name); refresh(); }) }));
            }
            if (saved.Count == 0) { var m = Ui.Muted("None yet.", 11.5); m.Margin = new Thickness(38, 0, 12, 4); sp.Children.Add(m); }
            var note = Ui.Muted("Workspaces share one profile, so you stay signed in everywhere. Switching closes this workspace's pages and opens the other's (like a restart: a page playing sound stops). A restored session opens as its own workspace.", 11);
            note.Margin = new Thickness(12, 8, 12, 6); sp.Children.Add(note);
            return sp;
        }

        // ================================================================ history and bookmarks
        void EnsureStores()
        {
            if (History != null) return;
            History = new HistoryStore(HistoryStore.DefaultPath); Bookmarks = new BookmarkStore(BookmarkStore.DefaultPath);
            Bookmarks.Changed += () => Dispatcher.BeginInvoke(new Action(BookmarksChanged));   // 1.3: the bar, the manager, sync
            if (snap) { Bookmarks.Frozen = true; return; }
            var h = History; var b = Bookmarks;
            Task.Run(() => { h.Load(); b.Load(); }).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => { if (Active != null) UpdateBar(); StoresLoaded(); })));
        }
        void RecordHistory(Tab x)
        {
            if (!S.KeepHistory || snap || x == null) return;
            EnsureStores();
            History.Add(x.Url, x.Title == "New tab" ? null : x.Title, DateTime.Now);
            if (historyTimer == null) { historyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) }; historyTimer.Tick += (a, b) => { historyTimer.Stop(); var h = History; Task.Run(() => h.Save()); RefreshNewTabData(); }; }
            historyTimer.Stop(); historyTimer.Start();
        }
        public void ClearHistory()
        {
            EnsureStores(); History.Clear();
            lock (S.Typed) S.Typed.Clear(); S.Save();
            var p = Profile; if (p != null) try { var _ = p.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.BrowsingHistory); } catch { }
            Toast("History cleared (the pages Ctrl+K knew and the address-bar suggestions).", 4);
        }
        public bool IsBookmarked(Tab t) { return t != null && Bookmarks != null && Bookmarks.Loaded && Bookmarks.Has(t.Url); }
        public void ToggleBookmark(Tab t)
        {
            if (t == null || !Omni.IsWeb(t.Url)) { Toast("Only web pages can be bookmarked.", 3); return; }
            EnsureStores(); if (!Bookmarks.Loaded) Bookmarks.Load();
            var on = Bookmarks.Toggle(t.Url, t.Title);
            Toast(on ? "Bookmarked: " + t.Title + " (Ctrl+K finds it)" : "Bookmark removed.", 3);
            if (t == Active) UpdateBar();
        }
        public void ShowBookmarks() { NewTab(Omni.BookmarksUrl, true); }   // 1.3: the bookmarks manager
        void ShowBookmarksFlyout12()
        {
            EnsureStores(); if (!Bookmarks.Loaded) Bookmarks.Load();
            var sp = new StackPanel(); Popup pop = null;
            var h = Ui.Label("Bookmarks"); h.Margin = new Thickness(12, 8, 12, 6); sp.Children.Add(h);
            var all = Bookmarks.All();
            foreach (var b in all.Take(40))
            {
                var u = b.Url;
                var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var r = Ui.MenuRow("bookmark", (b.Title ?? u).Length > 60 ? (b.Title ?? u).Substring(0, 60) + "..." : (b.Title ?? u), null, () => { pop.IsOpen = false; Navigate(u, false); }); r.ToolTip = u;
                g.Children.Add(r);
                var x = Ui.Tool("xmark", "Remove", () => { Bookmarks.Remove(u); pop.IsOpen = false; ShowBookmarks(); if (Active != null) UpdateBar(); }, 9); x.Width = 24; x.Height = 24; Grid.SetColumn(x, 1); g.Children.Add(x);
                sp.Children.Add(g);
            }
            if (all.Count == 0) { var m = Ui.Muted("No bookmarks yet. Ctrl+D bookmarks the page you're on; Ctrl+K finds them.", 11.5); m.Margin = new Thickness(12, 0, 12, 8); sp.Children.Add(m); }
            else if (all.Count > 40) { var m = Ui.Muted("And " + (all.Count - 40) + " more - Ctrl+K finds them all.", 11); m.Margin = new Thickness(12, 4, 12, 6); sp.Children.Add(m); }
            pop = Panels.ShowFlyout(menuBtn, sp, 360);
        }

        // ================================================================ reader mode
        public void ToggleReader(Tab t) { var _ = ToggleReaderAsync(t); }
        public async Task<ReaderDoc> ToggleReaderAsync(Tab t)
        {
            if (t == null) return null;
            if (t.ReaderOn) { ReaderOff(t); return t.ReaderDoc; }
            var c = t.Core; if (c == null || !Omni.IsWeb(t.Url)) { Toast("Reader mode works on web pages.", 3); return null; }
            string r; try { r = await c.ExecuteScriptAsync(Reader.Extract); } catch (Exception e) { Toast("Reader mode didn't work here: " + e.Message, 5); return null; }
            var d = ReaderDoc.Parse(r);
            if (!d.Ok) { Toast("Reader mode: this page doesn't look like an article.", 4); return d; }
            if (!Tabs.Contains(t) || t.Core != c) return d;
            if (t.ReaderView != null) content.Children.Remove(t.ReaderView);
            t.ReaderDoc = d; t.ReaderView = Reader.View(this, t, d); t.ReaderView.Visibility = Visibility.Collapsed; content.Children.Add(t.ReaderView); t.ReaderOn = true;
            Activate(Active ?? t); UpdateBar();
            return d;
        }
        void ReaderOff(Tab t)
        {
            if (t.ReaderView != null) content.Children.Remove(t.ReaderView);
            t.ReaderView = null; t.ReaderOn = false;
            if (Tabs.Contains(t) && Active != null) Activate(Active);
            if (t == Active) UpdateBar();
        }
        async void ProbeReader(Tab t)
        {
            var c = t.Core; if (c == null || !Omni.IsWeb(t.Url)) { t.Readable = false; return; }
            var url = t.Url; t.ProbedUrl = url;
            string r; try { r = await c.ExecuteScriptAsync(Reader.Probe); } catch { return; }
            if (t.Url != url) return;
            t.Readable = r == "true"; if (t == Active) UpdateBar();
        }

        // ================================================================ sound, small wrappers
        public int MuteOthers(Tab t)
        {
            int n = 0;
            foreach (var x in Tabs) if (x != t && x.Core != null) try { if (!x.Core.IsMuted) { x.Core.IsMuted = true; n++; } } catch { }
            Toast(n == 0 ? "No other tabs to mute." : "Muted " + n + " other " + (n == 1 ? "tab." : "tabs."), 3);
            return n;
        }
        public void ShowDownloads() { Panels.DownloadsFlyout(this, dlBtn); }
        public void ShowHotTabs() { HotTabs.Flyout(this, menuBtn); }
        public void ShowQuick() { Panels.Quick(this, quickBtn); }
        public void ShowScreenshot() { Shots.Flyout(this, menuBtn); }

        // ================================================================ --snap: made-up groups, splits, reader, palette
        public void SnapPower(string kind)
        {
            Func<string, string, Tab> add = (title, url) => { var t = new Tab { Url = url, Title = title }; AddHeader(t, Tabs.Count); Tabs.Add(t); return t; };
            if (kind == "groups" || kind == "vertical" || kind == "vertical-icons" || kind == "split")
            {
                var raid = new TabGroup { Name = "Raid night", Color = "purple" }; var patch = new TabGroup { Name = "Patch notes", Color = "green", Collapsed = kind != "vertical" };
                Groups.Add(raid); Groups.Add(patch);
                Tabs[1].Group = raid; Tabs[2].Group = raid;
                var x = add("Build guide - Delta Force wiki", "https://wiki.example.com/builds"); x.Group = raid;
                add("Season 7 changes", "https://example.com/s7").Group = patch; add("Known issues", "https://example.com/issues").Group = patch;
                add("Hallow Hub - docs", "https://docs.example.com/");
                Tabs[2].Audible = true; SetTabIcon(Tabs[2]);
                RenderStrip(); Activate(Tabs[0]);
            }
            if (kind == "vertical" || kind == "vertical-icons") { S.VerticalTabs = true; S.VTabsCollapsed = kind == "vertical-icons"; }
            ApplyTabLayout();
            if (kind == "split")
            {
                var right = Tabs[1]; right.Title = "Lo-fi beats to game to"; right.TitleBlock.Text = right.Title;
                SplitL = Tabs[0]; SplitR = right; right.ReaderOn = true; right.ReaderView = Reader.View(this, right, Reader.Mock()); content.Children.Add(right.ReaderView);
                var mock = content.Children.OfType<FrameworkElement>().LastOrDefault(e => e.Tag as string == "mock"); if (mock != null) { content.Children.Remove(mock); Tabs[0].Page = mock; content.Children.Add(mock); }
                RenderStrip(); Activate(Tabs[0]);
            }
            if (kind == "reader")
            {
                var t = Active; t.ReaderOn = true; t.Readable = true; t.ReaderView = Reader.View(this, t, Reader.Mock()); content.Children.Add(t.ReaderView);
                Activate(t); UpdateBar();
            }
            if (kind == "palette" || kind == "workspaces")
            {
                Spaces = new WorkspaceStore { Current = "default", Items = new List<Workspace> { new Workspace { Id = "default", Name = "Gaming" }, new Workspace { Id = "work", Name = "Work", Session = new Session { Tabs = new List<SessionTab> { new SessionTab { Url = "https://mail.example.com/" }, new SessionTab { Url = "https://docs.example.com/" } } } } } };
                SpaceId = "default"; UpdateSpaceButton();
                History = new HistoryStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hallow-snap-none.json")); Bookmarks = new BookmarkStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hallow-snap-none-b.json"));
                History.Add("https://example.com/raid-planner", "Raid planner - weekly reset times", DateTime.Now.AddHours(-3));
                History.Add("https://forum.example.com/t/raid-loot", "Raid loot table (season 7)", DateTime.Now.AddDays(-1));
                FrameworkElement panel = kind == "palette" ? Palette.SnapPanel(this, "raid") : (FrameworkElement)Ui.Flyout(WorkspacesPanel(() => { }, () => { }, false), 340);
                panel.VerticalAlignment = VerticalAlignment.Top; panel.HorizontalAlignment = kind == "palette" ? HorizontalAlignment.Center : HorizontalAlignment.Left; panel.Margin = new Thickness(kind == "palette" ? 0 : 8, 12, 0, 0);
                Grid.SetColumnSpan(panel, 3); content.Children.Add(panel);
            }
        }
    }
}
