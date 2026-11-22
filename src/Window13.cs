// Hallow Browser 1.3 - the window's parts for "moving in and syncing": the bookmarks bar (Ctrl+Shift+B), the bookmarks manager
// (hallow://bookmarks, Ctrl+Shift+O: folders, rename, drag to reorder, HTML export / import), the import picker (other browsers on
// this PC - bookmarks and history only), sync through a folder (Settings > Sync) and the new tab page's tiles and Hallow strip.
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
using System.Windows.Threading;

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        Border bmBar; StackPanel bmItems; Button bmMore; DockPanel bmDock;
        const string DragFormat = "HallowBrowserBookmark"; bool snapBar;

        void StoresLoaded()
        {
            AutoBookmarksBar(); RenderBookmarksBar(); RefreshNewTabData(); StartSync();
            if (ntTimer == null) { ntTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) }; ntTimer.Tick += (a, b) => RefreshNewTabData(); ntTimer.Start(); }
        }
        DispatcherTimer ntTimer;
        void BookmarksChanged()
        {
            AutoBookmarksBar(); RenderBookmarksBar(); RefreshBookmarksPage();
            if (Active != null) UpdateBar();
            SyncSoon(8);
        }
        public void EnsureStoresPublic() { EnsureStores(); }
        public bool SyncBusy { get { return syncBusy; } }
        void EnsureBookmarksLoaded() { EnsureStores(); if (!Bookmarks.Loaded && !snap) Bookmarks.Load(); }

        // ================================================================ the bookmarks bar
        FrameworkElement BuildBookmarksBar()
        {
            bmBar = new Border { Height = 30, BorderThickness = new Thickness(0, 1, 0, 0), Visibility = S.BookmarksBar ? Visibility.Visible : Visibility.Collapsed, AllowDrop = true };
            bmBar.SetResourceReference(Border.BackgroundProperty, "Bar"); bmBar.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            bmDock = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 0, 6, 0) };
            bmMore = Ui.Tool("angles-right", "More bookmarks", () => BarOverflow(), 10); bmMore.Width = 26; bmMore.Height = 24; bmMore.Visibility = Visibility.Collapsed; DockPanel.SetDock(bmMore, Dock.Right);
            bmDock.Children.Add(bmMore);
            bmItems = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            bmDock.Children.Add(bmItems);
            bmBar.Child = bmDock;
            bmBar.SizeChanged += (a, b) => FitBookmarksBar();
            bmBar.MouseRightButtonUp += (a, b) => { if (!b.Handled) { BarMenu(null, bmBar); b.Handled = true; } };
            bmBar.DragOver += (a, b) => { b.Effects = b.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None; b.Handled = true; };
            bmBar.Drop += (a, b) => { var id = b.Data.GetData(DragFormat) as string; if (id != null && Bookmarks != null) Bookmarks.Move(id, BookmarkStore.BarId, -1); b.Handled = true; };
            return bmBar;
        }
        public void ToggleBookmarksBar() { if (Private) { Toast("The bookmarks bar can't be changed from a private window.", 3); return; } S.BookmarksBar = !S.BookmarksBar; S.BookmarksBarHidden = !S.BookmarksBar; /* 1.9: hidden on purpose = never shown by itself again */ S.Save(); ApplyBookmarksBar(); ApplyBookmarksBarAll(); Toast(S.BookmarksBar ? "Bookmarks bar on (Ctrl+Shift+B hides it)." : "Bookmarks bar off (Ctrl+Shift+B shows it).", 3); SyncSoon(8); }
        public void ApplyBookmarksBar() { if (bmBar == null) return; bmBar.Visibility = (S.BookmarksBar || snapBar) && !fullscreen && !Private ? Visibility.Visible : Visibility.Collapsed; RenderBookmarksBar(); }
        public void ApplyBookmarksBarAll() { foreach (var w in Program.Windows) if (w != this) w.ApplyBookmarksBar(); }
        public bool BookmarksBarShown { get { return bmBar != null && bmBar.Visibility == Visibility.Visible; } }
        public int BarItemCount { get { return bmItems == null ? 0 : bmItems.Children.OfType<FrameworkElement>().Count(x => x.Tag is Bookmark); } }

        public void RenderBookmarksBar()
        {
            if (bmItems == null) return;
            bmItems.Children.Clear();
            if (!(S.BookmarksBar || snapBar) || Bookmarks == null || (!Bookmarks.Loaded && !snap)) { bmMore.Visibility = Visibility.Collapsed; return; }
            var kids = Bookmarks.Children(BookmarkStore.BarId);
            if (kids.Count == 0)
            {
                var hint = Ui.Muted("Ctrl+D puts the page here. ", 11.5); hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(6, 0, 4, 0);
                var imp = new Button { Style = Ui.S("Tool"), Width = double.NaN, Height = 22, Padding = new Thickness(6, 0, 6, 0), Content = Ui.IconText("file-import", "Import bookmarks from another browser", 11, "Lilac") };
                imp.Click += (a, b) => ShowImport(bmBar);
                bmItems.Children.Add(hint); bmItems.Children.Add(imp);
            }
            foreach (var b in kids) bmItems.Children.Add(BarButton(b));
            Dispatcher.BeginInvoke(new Action(FitBookmarksBar), DispatcherPriority.Loaded);
        }
        Button BarButton(Bookmark b)
        {
            var title = string.IsNullOrWhiteSpace(b.Title) ? (b.Folder ? "Folder" : Omni.HostOf(b.Url)) : b.Title;
            var tb = new TextBlock { Text = title, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            var ic = Ui.Icon(b.Folder ? "folder" : "globe", 11, b.Folder ? "Lilac" : "Muted"); ic.Margin = new Thickness(0, 0, 6, 0);
            var btn = new Button { Style = Ui.S("Tool"), Width = double.NaN, Height = 24, Margin = new Thickness(0, 0, 4, 0), Content = new Border { Padding = new Thickness(7, 0, 9, 0), Child = Ui.Row(ic, tb) }, Tag = b, ToolTip = b.Folder ? title : title + "\n" + b.Url, AllowDrop = true };
            btn.Click += (s, e) => { if (b.Folder) FolderMenu(b, btn); else OpenBookmark(b, (Keyboard.Modifiers & ModifierKeys.Control) != 0); };
            btn.MouseUp += (s, e) => { if (e.ChangedButton == MouseButton.Middle && !b.Folder) { OpenBookmark(b, true); e.Handled = true; } };
            btn.MouseRightButtonUp += (s, e) => { BarMenu(b, btn); e.Handled = true; };
            Draggable(btn, b.Id);
            btn.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            btn.Drop += (s, e) =>
            {
                var id = e.Data.GetData(DragFormat) as string; e.Handled = true; if (id == null || id == b.Id) return;
                var x = e.GetPosition(btn).X; var w = btn.ActualWidth;
                if (b.Folder && x > w * 0.25 && x < w * 0.75) { Bookmarks.Move(id, b.Id, -1); return; }
                var kids = Bookmarks.Children(BookmarkStore.BarId).Where(k => k.Id != id).ToList();
                var at = kids.FindIndex(k => k.Id == b.Id); if (at < 0) at = kids.Count; else if (x > w / 2) at++;
                Bookmarks.Move(id, BookmarkStore.BarId, at);
            };
            return btn;
        }
        // drag a bookmark (bar, manager): starts after the mouse moves a few pixels with the button down
        void Draggable(FrameworkElement e, string id)
        {
            Point? start = null;
            e.PreviewMouseLeftButtonDown += (s, a) => start = a.GetPosition(null);
            e.PreviewMouseLeftButtonUp += (s, a) => start = null;
            e.PreviewMouseMove += (s, a) =>
            {
                if (start == null || a.LeftButton != MouseButtonState.Pressed) return;
                var p = a.GetPosition(null);
                if (Math.Abs(p.X - start.Value.X) < 5 && Math.Abs(p.Y - start.Value.Y) < 5) return;
                start = null;
                try { DragDrop.DoDragDrop(e, new DataObject(DragFormat, id), DragDropEffects.Move); } catch { }
            };
        }
        void FitBookmarksBar()
        {
            if (bmItems == null || bmBar.Visibility != Visibility.Visible) return;
            double avail = Math.Max(0, bmBar.ActualWidth - 12 - 30), used = 0; bool over = false;
            foreach (FrameworkElement c in bmItems.Children)
            {
                c.Visibility = Visibility.Visible; c.Measure(new Size(double.PositiveInfinity, 30));
                used += c.DesiredSize.Width + c.Margin.Left + c.Margin.Right;
                if (over || used > avail) { c.Visibility = Visibility.Collapsed; over = true; }
            }
            bmMore.Visibility = over ? Visibility.Visible : Visibility.Collapsed;
        }
        void BarOverflow()
        {
            var hidden = bmItems.Children.OfType<FrameworkElement>().Where(x => x.Visibility != Visibility.Visible && x.Tag is Bookmark).Select(x => (Bookmark)x.Tag).ToList();
            ShowList(hidden, bmMore, null);
        }
        public void OpenBookmark(Bookmark b, bool newTab) { if (b != null && !b.Folder && Omni.IsWeb(b.Url)) Navigate(b.Url, newTab); }
        void FolderMenu(Bookmark folder, UIElement at) { ShowList(Bookmarks.Children(folder.Id), at, folder); }
        // a folder's contents as a flyout; a folder inside opens in the same flyout (with a way back)
        void ShowList(List<Bookmark> items, UIElement at, Bookmark folder, Stack<Bookmark> trail = null)
        {
            var sp = new StackPanel(); Popup pop = null; trail = trail ?? new Stack<Bookmark>();
            if (folder != null)
            {
                var head = Ui.Row(Ui.Icon("folder-open", 12, "Lilac"), new TextBlock { Text = "  " + folder.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                head.Margin = new Thickness(10, 6, 10, 6); sp.Children.Add(head);
                if (trail.Count > 0) { var up = trail.Peek(); sp.Children.Add(Ui.MenuRow("arrow-left", "Back to " + up.Title, null, () => { pop.IsOpen = false; var t2 = new Stack<Bookmark>(trail.Reverse()); t2.Pop(); ShowList(Bookmarks.Children(up.Id), at, up, t2); })); }
            }
            foreach (var b in items.Take(60))
            {
                var x = b;
                var r = Ui.MenuRow(x.Folder ? "folder" : "globe", Cut(x.Title ?? x.Url, 56), x.Folder ? ">" : null, () =>
                {
                    pop.IsOpen = false;
                    if (x.Folder) { var t2 = new Stack<Bookmark>(trail.Reverse()); if (folder != null) t2.Push(folder); ShowList(Bookmarks.Children(x.Id), at, x, t2); }
                    else OpenBookmark(x, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
                });
                if (!x.Folder) r.ToolTip = x.Url;
                r.MouseRightButtonUp += (s, e) => { pop.IsOpen = false; BarMenu(x, at); e.Handled = true; };
                sp.Children.Add(r);
            }
            if (items.Count == 0) { var m = Ui.Muted("Empty folder.", 11.5); m.Margin = new Thickness(12, 2, 12, 8); sp.Children.Add(m); }
            if (folder != null && items.Any(b => !b.Folder))
                sp.Children.Add(Ui.MenuRow("up-right-from-square", "Open all in new tabs", null, () => { pop.IsOpen = false; foreach (var b in items.Where(b => !b.Folder).Take(30)) NewTab(b.Url, false); }));
            pop = Panels.ShowFlyout(at, new ScrollViewer { Content = sp, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 330);
            pop.Placement = PlacementMode.Bottom; pop.HorizontalOffset = 0;
        }
        static string Cut(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) + "..." : s; }
        void BarMenu(Bookmark b, UIElement at)
        {
            var sp = new StackPanel(); Popup pop = null;
            Action<Action> run = a => { pop.IsOpen = false; a(); };
            if (b != null && !b.Folder)
            {
                sp.Children.Add(Ui.MenuRow("plus", "Open in a new tab", "Ctrl+click", () => run(() => OpenBookmark(b, true))));
                sp.Children.Add(Ui.MenuRow("pen", "Edit...", null, () => run(() => EditBookmark(b, at))));
            }
            if (b != null && b.Folder)
            {
                sp.Children.Add(Ui.MenuRow("up-right-from-square", "Open all in new tabs", null, () => run(() => { foreach (var x in Bookmarks.Children(b.Id).Where(x => !x.Folder).Take(30)) NewTab(x.Url, false); })));
                sp.Children.Add(Ui.MenuRow("pen", "Rename...", null, () => run(() => EditBookmark(b, at))));
            }
            if (b != null) sp.Children.Add(Ui.MenuRow("trash-can", b.Folder ? "Delete the folder and what's in it" : "Delete", null, () => run(() => { var n = Bookmarks.Delete(b.Id); Toast(n > 1 ? "Deleted " + n + " items." : "Deleted.", 3); })));
            sp.Children.Add(Ui.MenuRow("folder-plus", "New folder on the bar...", null, () => run(() => Prompt(at, "New folder", "New folder", v => Bookmarks.AddFolder(BookmarkStore.BarId, v)))));
            if (Active != null && Omni.IsWeb(Active.Url) && !Bookmarks.Has(Active.Url)) sp.Children.Add(Ui.MenuRow("star", "Add this page", "Ctrl+D", () => run(() => ToggleBookmark(Active))));
            sp.Children.Add(Ui.MenuRow("bookmark", "Bookmarks manager", "Ctrl+Shift+O", () => run(() => NewTab(Omni.BookmarksUrl, true))));
            sp.Children.Add(Ui.MenuRow("file-import", "Import from another browser...", null, () => run(() => ShowImport(at))));
            sp.Children.Add(Ui.MenuRow("xmark", "Hide the bookmarks bar", "Ctrl+Shift+B", () => run(ToggleBookmarksBar)));
            pop = Panels.ShowFlyout(at, sp, 300); pop.HorizontalOffset = 0;
        }
        // a small box to type a name (and, for a bookmark, its address)
        void Prompt(UIElement at, string title, string value, Action<string> ok, string url = null, Action<string> okUrl = null)
        {
            var sp = new StackPanel { Margin = new Thickness(8) }; Popup pop = null;
            sp.Children.Add(Ui.Label(title));
            var box = new TextBox { Style = Ui.S("Field"), Text = value ?? "", Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(box);
            TextBox ub = null;
            if (url != null) { sp.Children.Add(Ui.Muted("Address", 11, 4)); ub = new TextBox { Style = Ui.S("Field"), Text = url, Margin = new Thickness(0, 0, 0, 8) }; sp.Children.Add(ub); }
            Action done = () =>
            {
                pop.IsOpen = false;
                if (ub != null && okUrl != null && ub.Text.Trim() != url) { var u = Omni.Interpret(ub.Text, S.Search); if (Omni.IsWeb(u)) okUrl(u); else Toast("That isn't a web address - kept the old one.", 4); }
                if (box.Text.Trim().Length > 0) ok(box.Text.Trim());
            };
            KeyEventHandler keys = (s, e) => { if (e.Key == Key.Enter) { done(); e.Handled = true; } else if (e.Key == Key.Escape) { pop.IsOpen = false; e.Handled = true; } };
            box.KeyDown += keys; if (ub != null) ub.KeyDown += keys;
            sp.Children.Add(Ui.Row(Ui.Btn("OK", done, true), Ui.Btn("Cancel", () => pop.IsOpen = false)));
            pop = Panels.ShowFlyout(at, sp, 340); pop.HorizontalOffset = 0;
            pop.Opened += (s, e) => { box.Focus(); box.SelectAll(); };
            Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), DispatcherPriority.Input);
        }
        void EditBookmark(Bookmark b, UIElement at)
        {
            if (b.Folder) Prompt(at, "Rename folder", b.Title, v => Bookmarks.Rename(b.Id, v));
            else Prompt(at, "Edit bookmark", b.Title, v => Bookmarks.Rename(b.Id, v), b.Url, u => Bookmarks.SetUrl(b.Id, u));
        }

        // ================================================================ the bookmarks manager (hallow://bookmarks)
        string bmFolder = BookmarkStore.BarId; string bmQuery = ""; Grid bmPage; StackPanel bmTree, bmList; TextBlock bmPath; TextBox bmSearch;
        FrameworkElement BookmarksPage()
        {
            EnsureBookmarksLoaded();
            bmPage = new Grid { Margin = new Thickness(24, 20, 24, 20) };
            bmPage.SetResourceReference(Panel.BackgroundProperty, "Bg");
            bmPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bmPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bmPage.RowDefinitions.Add(new RowDefinition());
            bmPage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); bmPage.ColumnDefinitions.Add(new ColumnDefinition());
            var h = Ui.Row(Ui.Icon("bookmark", 18, "AccentBrush"), Ui.T("  BOOKMARKS", 20, true, "Text")); h.Margin = new Thickness(0, 0, 0, 4); Grid.SetColumnSpan(h, 2); bmPage.Children.Add(h);
            var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 12) }; Grid.SetRow(tools, 1); Grid.SetColumnSpan(tools, 2);
            bmSearch = new TextBox { Style = Ui.S("Field"), Width = 240, Height = 30, Margin = new Thickness(0, 0, 10, 6), ToolTip = "Search all bookmarks" };
            bmSearch.TextChanged += (a, b) => { bmQuery = bmSearch.Text; RefreshBookmarksList(); };
            tools.Children.Add(bmSearch);
            Button nf = null, im = null, ex = null, ih = null;
            nf = Ui.Btn("New folder", () => Prompt(nf, "New folder", "New folder", v => { var f = Bookmarks.AddFolder(CurFolder, v); }), false, "folder-plus");
            im = Ui.Btn("Import from a browser...", () => ShowImport(im), true, "file-import", "Chrome, Edge, Brave, Opera, Opera GX, Vivaldi or Firefox on this PC - bookmarks and history only");
            ih = Ui.Btn("Import HTML file...", PickImportHtml, false, "file-import", "A bookmarks file exported from any browser (the Netscape HTML format)");
            ex = Ui.Btn("Export HTML file...", PickExportHtml, false, "file-export", "All bookmarks as an HTML file any browser can import");
            foreach (var b in new[] { nf, im, ih, ex }) { b.Margin = new Thickness(0, 0, 8, 6); tools.Children.Add(b); }
            var bar = Ui.Switch("Show the bookmarks bar", null, S.BookmarksBar, on => { if (on != S.BookmarksBar) ToggleBookmarksBar(); }); bar.Margin = new Thickness(6, 4, 0, 6); bar.VerticalAlignment = VerticalAlignment.Center;
            tools.Children.Add(bar);
            bmPage.Children.Add(tools);
            bmTree = new StackPanel();
            var tv = new ScrollViewer { Content = bmTree, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Margin = new Thickness(0, 0, 16, 0) };
            Grid.SetRow(tv, 2); bmPage.Children.Add(tv);
            var right = new Grid(); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition());
            bmPath = Ui.T("", 14, true, "Text", 8); right.Children.Add(bmPath);
            bmList = new StackPanel { AllowDrop = true, Background = Brushes.Transparent, MinHeight = 120 };
            bmList.DragOver += (a, b) => { b.Effects = b.Data.GetDataPresent(DragFormat) && bmQuery.Trim().Length == 0 ? DragDropEffects.Move : DragDropEffects.None; b.Handled = true; };
            bmList.Drop += (a, b) => { var id = b.Data.GetData(DragFormat) as string; if (id != null) Bookmarks.Move(id, CurFolder, -1); b.Handled = true; };
            var lv = new ScrollViewer { Content = bmList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false }; Grid.SetRow(lv, 1); right.Children.Add(lv);
            Grid.SetRow(right, 2); Grid.SetColumn(right, 1); bmPage.Children.Add(right);
            RefreshBookmarksPage();
            return bmPage;
        }
        string CurFolder { get { return BookmarkStore.IsRoot(bmFolder) || (Bookmarks != null && Bookmarks.Find(bmFolder) != null) ? bmFolder : BookmarkStore.BarId; } }
        void RefreshBookmarksPage()
        {
            if (bmPage == null || Bookmarks == null) return;
            if (!Bookmarks.Loaded && !snap) { var _ = Task.Run(() => Bookmarks.Load()); return; }
            bmTree.Children.Clear();
            Action<string, string, int> node = null;
            node = (id, title, depth) =>
            {
                var isCur = id == CurFolder;
                var row = new Button { Style = Ui.S("MenuRow"), Padding = new Thickness(8 + depth * 14, 5, 8, 5), AllowDrop = true, Tag = id };
                row.Content = Ui.Row(Ui.Icon(BookmarkStore.IsRoot(id) ? (id == BookmarkStore.BarId ? "bookmark" : "folder") : isCur ? "folder-open" : "folder", 12, isCur ? "AccentBrush" : "Lilac"), new TextBlock { Text = "  " + title, FontWeight = isCur ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 - depth * 14 });
                row.Click += (a, b) => { bmFolder = id; bmQuery = ""; if (bmSearch != null) bmSearch.Text = ""; RefreshBookmarksPage(); };
                row.DragOver += (a, b) => { b.Effects = b.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None; b.Handled = true; };
                row.Drop += (a, b) => { var mid = b.Data.GetData(DragFormat) as string; if (mid != null && mid != id) { if (!Bookmarks.Move(mid, id, -1)) Toast("A folder can't go inside itself.", 3); } b.Handled = true; };
                if (!BookmarkStore.IsRoot(id)) { Draggable(row, id); var bk = Bookmarks.Find(id); row.MouseRightButtonUp += (a, b) => { if (bk != null) ItemMenu(bk, row); b.Handled = true; }; }
                bmTree.Children.Add(row);
                if (depth < 12) foreach (var f in Bookmarks.Children(id).Where(x => x.Folder)) node(f.Id, f.Title, depth + 1);
            };
            node(BookmarkStore.BarId, BookmarkStore.RootName(BookmarkStore.BarId), 0);
            node(BookmarkStore.OtherId, BookmarkStore.RootName(BookmarkStore.OtherId), 0);
            RefreshBookmarksList();
        }
        void RefreshBookmarksList()
        {
            if (bmList == null || Bookmarks == null) return;
            bmList.Children.Clear();
            var q = (bmQuery ?? "").Trim();
            List<Bookmark> items;
            if (q.Length > 0)
            {
                bmPath.Text = "Search: " + q;
                items = Bookmarks.All().Select(b => new { b, s = Math.Max(Fuzzy.Score(q, b.Title), Fuzzy.Score(q, b.Url) - 10) }).Where(x => x.s >= 0).OrderByDescending(x => x.s).Take(200).Select(x => x.b).ToList();
            }
            else { bmPath.Text = Bookmarks.PathOf(CurFolder); items = Bookmarks.Children(CurFolder); }
            foreach (var b in items) bmList.Children.Add(ItemRow(b, q.Length == 0));
            if (items.Count == 0)
            {
                var m = Ui.Muted(q.Length > 0 ? "Nothing found." : CurFolder == BookmarkStore.BarId ? "Nothing on the bookmarks bar yet. Ctrl+D adds the page you're on; drag bookmarks here from other folders." : "This folder is empty - drag bookmarks onto it.", 12);
                m.Margin = new Thickness(4, 8, 4, 8); bmList.Children.Add(m);
            }
            var note = Ui.Muted("Drag to reorder, or onto a folder on the left. Right-click for more. Imported bookmarks go into \"Imported from ...\" folders under Other bookmarks - passwords and cookies are never imported.", 11, 0);
            note.Margin = new Thickness(4, 14, 4, 0); bmList.Children.Add(note);
        }
        FrameworkElement ItemRow(Bookmark b, bool canDrag)
        {
            var g = new Grid { Height = 36, Background = Brushes.Transparent, AllowDrop = true, Tag = b };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Ui.Icon(b.Folder ? "folder" : "globe", 13, b.Folder ? "Lilac" : "Muted"); ic.HorizontalAlignment = HorizontalAlignment.Center; g.Children.Add(ic);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(tx, 1);
            tx.Children.Add(new TextBlock { Text = b.Title ?? "", TextTrimming = TextTrimming.CharacterEllipsis });
            if (!b.Folder) { var u = Ui.Muted(b.Url, 11); u.TextWrapping = TextWrapping.NoWrap; u.TextTrimming = TextTrimming.CharacterEllipsis; tx.Children.Add(u); }
            else { var n = Bookmarks.Children(b.Id).Count; tx.Children.Add(Ui.Muted(n == 1 ? "1 item" : n + " items", 11)); }
            g.Children.Add(tx);
            var acts = Ui.Row(Ui.Tool("pen", b.Folder ? "Rename" : "Edit", () => EditBookmark(b, g), 11), Ui.Tool("trash-can", "Delete", () => { var n = Bookmarks.Delete(b.Id); Toast(n > 1 ? "Deleted " + n + " items." : "Deleted.", 3); }, 11));
            Grid.SetColumn(acts, 2); g.Children.Add(acts);
            g.MouseEnter += (s, e) => g.SetResourceReference(Panel.BackgroundProperty, "TabHover");
            g.MouseLeave += (s, e) => g.Background = Brushes.Transparent;
            g.MouseLeftButtonUp += (s, e) => { if (e.ClickCount > 1) return; if (b.Folder) { bmFolder = b.Id; bmQuery = ""; if (bmSearch != null) bmSearch.Text = ""; RefreshBookmarksPage(); } else OpenBookmark(b, true); };
            g.MouseRightButtonUp += (s, e) => { ItemMenu(b, g); e.Handled = true; };
            g.ToolTip = b.Folder ? "Open the folder" : "Open in a new tab\n" + b.Url;
            if (canDrag)
            {
                Draggable(g, b.Id);
                g.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                g.Drop += (s, e) =>
                {
                    var id = e.Data.GetData(DragFormat) as string; e.Handled = true; if (id == null || id == b.Id) return;
                    var y = e.GetPosition(g).Y; var hgt = g.ActualHeight;
                    if (b.Folder && y > hgt * 0.25 && y < hgt * 0.75) { if (!Bookmarks.Move(id, b.Id, -1)) Toast("A folder can't go inside itself.", 3); return; }
                    var kids = Bookmarks.Children(CurFolder).Where(k => k.Id != id).ToList();
                    var at = kids.FindIndex(k => k.Id == b.Id); if (at < 0) at = kids.Count; else if (y > hgt / 2) at++;
                    if (!Bookmarks.Move(id, CurFolder, at)) Toast("A folder can't go inside itself.", 3);
                };
            }
            return g;
        }
        void ItemMenu(Bookmark b, UIElement at)
        {
            var sp = new StackPanel(); Popup pop = null; Action<Action> run = a => { pop.IsOpen = false; a(); };
            if (!b.Folder) { sp.Children.Add(Ui.MenuRow("globe", "Open", null, () => run(() => OpenBookmark(b, false)))); sp.Children.Add(Ui.MenuRow("plus", "Open in a new tab", null, () => run(() => OpenBookmark(b, true)))); }
            else sp.Children.Add(Ui.MenuRow("up-right-from-square", "Open all in new tabs", null, () => run(() => { foreach (var x in Bookmarks.Children(b.Id).Where(x => !x.Folder).Take(30)) NewTab(x.Url, false); })));
            sp.Children.Add(Ui.MenuRow("pen", b.Folder ? "Rename..." : "Edit...", null, () => run(() => EditBookmark(b, at))));
            if (b.Parent != BookmarkStore.BarId) sp.Children.Add(Ui.MenuRow("bookmark", "Move to the bookmarks bar", null, () => run(() => Bookmarks.Move(b.Id, BookmarkStore.BarId, -1))));
            if (b.Parent != BookmarkStore.OtherId) sp.Children.Add(Ui.MenuRow("folder", "Move to Other bookmarks", null, () => run(() => Bookmarks.Move(b.Id, BookmarkStore.OtherId, -1))));
            if (!b.Folder) sp.Children.Add(Ui.MenuRow("thumbtack", "Pin to the new tab page", null, () => run(() => PinToNewTab(b.Url, b.Title))));
            sp.Children.Add(Ui.MenuRow("trash-can", b.Folder ? "Delete the folder and what's in it" : "Delete", null, () => run(() => Bookmarks.Delete(b.Id))));
            pop = Panels.ShowFlyout(at, sp, 290); pop.HorizontalOffset = 0;
        }

        // ---- HTML files (the Netscape bookmark format)
        public string ExportBookmarksTo(string path) { EnsureBookmarksLoaded(); File.WriteAllText(path, Bookmarks.ExportHtml(), new System.Text.UTF8Encoding(false)); return path; }
        public int ImportBookmarksFrom(string path)
        {
            EnsureBookmarksLoaded();
            var html = File.ReadAllText(path);
            return Bookmarks.ImportHtml(html, "Imported from " + Path.GetFileNameWithoutExtension(path));
        }
        void PickExportHtml()
        {
            var d = new Microsoft.Win32.SaveFileDialog { Title = "Export bookmarks", Filter = "Bookmarks file (*.html)|*.html", FileName = "bookmarks_" + DateTime.Now.ToString("yyyy-MM-dd") + ".html" };
            if (d.ShowDialog(this) != true) return;
            try { ExportBookmarksTo(d.FileName); Toast("Exported " + Bookmarks.Count + " bookmarks to " + Path.GetFileName(d.FileName) + " - any browser can import it.", 5); }
            catch (Exception e) { Toast("Couldn't export: " + e.Message, 6); }
        }
        void PickImportHtml()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Import a bookmarks file", Filter = "Bookmarks file (*.html;*.htm)|*.html;*.htm|All files|*.*" };
            if (d.ShowDialog(this) != true) return;
            try { var n = ImportBookmarksFrom(d.FileName); Toast(n == 0 ? "Nothing new in that file (bookmarks you already have are skipped)." : "Imported " + n + " bookmarks.", 5); }
            catch (Exception e) { Toast("Couldn't import: " + e.Message, 6); }
        }

        // ================================================================ moving in: the import picker
        public List<FoundProfile> FindBrowsers()
        {
            var root = BrowserSettings.ImportRoot;   // tests: a fake set of profiles (never your real ones)
            if (root != null) return Importer.Find(Path.Combine(root, "Local"), Path.Combine(root, "Roaming"));
            if (BrowserSettings.TestDir != null) return new List<FoundProfile>();
            return Importer.FindOnThisPC();
        }
        public void ShowImport(UIElement at)
        {
            EnsureStores();
            var sp = new StackPanel { Margin = new Thickness(6) }; Popup pop = null;
            sp.Children.Add(Ui.Label("Import from another browser"));
            sp.Children.Add(Ui.Muted("Bookmarks go into an \"Imported from ...\" folder (nothing twice); history joins the pages Ctrl+K finds. Read from copies - the other browser is never changed.", 11.5, 8));
            var warn = Ui.T("Passwords and cookies are never imported. Passwords belong in Hallow Keys - it has its own importer.", 11.5, true, "AccentLight", 10);
            sp.Children.Add(warn);
            var list = new StackPanel(); sp.Children.Add(list);
            var looking = Ui.Muted("Looking for browsers on this PC...", 12, 6); list.Children.Add(looking);
            pop = Panels.ShowFlyout(at, new ScrollViewer { Content = sp, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 470);
            Task.Run(() => FindBrowsers()).ContinueWith(r => Dispatcher.BeginInvoke(new Action(() =>
            {
                list.Children.Clear();
                var found = r.Result ?? new List<FoundProfile>();
                if (found.Count == 0) { list.Children.Add(Ui.Muted("No Chrome, Edge, Brave, Opera, Opera GX, Vivaldi or Firefox profiles found. You can still import a bookmarks HTML file from the Bookmarks manager.", 12, 6)); return; }
                foreach (var p in found)
                {
                    var prof = p;
                    var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8) };
                    card.SetResourceReference(Border.BorderBrushProperty, "Line");
                    var g = new StackPanel();
                    g.Children.Add(Ui.T(prof.Label, 13, true, "Text", 4));
                    var cb = new CheckBox { Content = "Bookmarks (counting...)", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
                    var ch = new CheckBox { Content = "History (counting...)", IsChecked = true, Margin = new Thickness(0, 2, 0, 6) };
                    cb.SetResourceReference(Control.ForegroundProperty, "Text"); ch.SetResourceReference(Control.ForegroundProperty, "Text");
                    var note = Ui.Muted("", 11, 4); note.Visibility = Visibility.Collapsed;
                    Button go = null;
                    go = Ui.Btn("Import", () =>
                    {
                        go.IsEnabled = false; go.Content = "Importing...";
                        bool wb = cb.IsChecked == true, wh = ch.IsChecked == true;
                        Task.Run(() => { if (!Bookmarks.Loaded) Bookmarks.Load(); if (!History.Loaded) History.Load(); return Importer.Run(prof, wb, wh, Bookmarks, History); }).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var res = t.Exception != null ? new ImportResult { Notes = { "Import failed: " + t.Exception.GetBaseException().Message } } : t.Result;
                            go.Content = "Done"; ShowImportResult(prof, res);
                            RefreshNewTabData();
                        })));
                    }, true, "file-import");
                    g.Children.Add(cb); g.Children.Add(ch); g.Children.Add(note); g.Children.Add(go);
                    card.Child = g; list.Children.Add(card);
                    Task.Run(() => Importer.Count(prof)).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        cb.Content = "Bookmarks (" + (prof.Bookmarks < 0 ? "?" : prof.Bookmarks.ToString("N0")) + ")"; cb.IsEnabled = prof.Bookmarks != 0; if (prof.Bookmarks == 0) cb.IsChecked = false;
                        ch.Content = "History (" + (prof.History < 0 ? "?" : prof.History.ToString("N0") + " pages") + ")"; ch.IsEnabled = prof.History != 0; if (prof.History == 0) ch.IsChecked = false;
                        if (prof.Note != null) { note.Text = prof.Note; note.Visibility = Visibility.Visible; }
                    })));
                }
            })));
        }
        public string LastImport;
        void ShowImportResult(FoundProfile p, ImportResult r)
        {
            var parts = new List<string>();
            if (r.Bookmarks > 0) parts.Add(r.Bookmarks + " bookmarks into \"" + p.FolderName + "\"");
            if (r.SkippedBookmarks > 0) parts.Add(r.SkippedBookmarks + " you already had skipped");
            if (r.History > 0) parts.Add(r.History + " pages of history");
            var msg = (parts.Count > 0 ? "Imported from " + p.Label + ": " + string.Join(", ", parts) + "." : "Nothing new to import from " + p.Label + ".") + (r.Notes.Count > 0 ? " " + string.Join(" ", r.Notes) : "");
            LastImport = msg; Toast(msg, 8);
        }

        // ================================================================ the new tab page's tiles and strip
        static readonly object ntLock = new object();
        public void RefreshNewTabData()
        {
            if (snap) return;
            string playing = null;
            try { var p = Side == null ? null : Side.Panels.FirstOrDefault(x => x.Audible && x.View != null); if (p != null) { var t = p.View.CoreWebView2 == null ? null : p.View.CoreWebView2.DocumentTitle; if (!string.IsNullOrWhiteSpace(t)) playing = t + " (" + p.Site.Name + ")"; } } catch { }
            var hist = History; var s = S; bool inGame = Game != null;   // 1.12: no default-browser hint while a game runs
            Task.Run(() => { lock (ntLock) NewTabData.Write(NewTabData.Build(s, hist != null && hist.Loaded ? hist.All() : new List<HistoryEntry>(), playing, DateTime.Now, inGame)); });
            RefreshFamily(Engine.SinceStart() < 5000 ? 1500 : 0);   // 1.6: your games, clips and the Overlay switch - after the first paint
            GameCornerTick(false);   // 1.4: when it is on and due
        }
        void NewTabMessage(string m, string source)
        {
            if (FamilyMessage(m, source)) return;   // 1.6: Play, a clip, the Overlay switch
            if (source == null || !Omni.IsNewTab(source) || Private) return;   // only the new tab page itself (1.4: never from a private window)
            if (DefaultHintMessage(m)) return;   // 1.12: the "make Hallow your default" hint
            var p = m.Split('\t'); var url = p.Length > 1 ? p[1] : null; if (!Omni.IsWeb(url)) return;
            if (p[0] == "hallow:nt-hide") { if (!S.NewTabHidden.Contains(url)) S.NewTabHidden.Add(url); }
            else if (p[0] == "hallow:nt-pin") { PinToNewTab(url, p.Length > 2 ? p[2] : null); return; }
            else if (p[0] == "hallow:nt-unpin") S.NewTabPinned.RemoveAll(x => x.Url == url);
            else return;
            S.Save(); NewTabChanged();
        }
        public void PinToNewTab(string url, string title)
        {
            if (!Omni.IsWeb(url)) return;
            if (!S.NewTabPinned.Any(x => x.Url == url)) S.NewTabPinned.Add(new PinnedSite { Url = url, Title = string.IsNullOrWhiteSpace(title) ? Omni.HostOf(url) : title });
            S.NewTabHidden.Remove(url); S.Fix(); S.Save(); NewTabChanged();
            Toast("Pinned to the new tab page.", 3);
        }
        void NewTabChanged()
        {
            RefreshNewTabData();
            var _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(250);
                foreach (var t in Tabs) if (Omni.IsNewTab(t.Url) && t.Core != null) try { var __ = t.Core.ExecuteScriptAsync("window.hallowRefresh&&hallowRefresh()"); } catch { }
            }));
            SyncSoon(8);
        }
        // 1.6: a note changed: it goes to the sync folder soon (when sync is on)
        public void NotesChanged() { SyncSoon(8); }
        public static string SyncFolderFor(string folder)
        {
            if (string.IsNullOrEmpty(folder) || BrowserSettings.IsDefaultProfile) return folder;
            var sub = System.IO.Path.Combine(folder, "profile-" + BrowserSettings.ProfileId);
            try { if (System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(sub); } catch { }   // only inside a folder that's there (a sync drive that's away stays away)
            return sub;
        }
        public string NewTabJson() { return NewTabData.Build(S, History != null && History.Loaded ? History.All() : new List<HistoryEntry>(), null, DateTime.Now, Game != null); }

        // --snap: made-up bookmarks (never yours) on the bar, and the manager page
        public void SnapBookmarks(bool manager)
        {
            EnsureStores();
            var items = new List<Bookmark>(); double p = 0;
            Action<string, string, string, bool, string> add = (id, parent, title, folder, url) => items.Add(new Bookmark { Id = id, Parent = parent, Title = title, Folder = folder, Url = url, Pos = p++, Added = "2026-11-20T20:00:00" });
            add("g", BookmarkStore.BarId, "Games", true, null); add("n", BookmarkStore.BarId, "Hallow News", false, "https://news.example.com/");
            add("d", BookmarkStore.BarId, "Delta Force guide", false, "https://guides.example.com/delta"); add("s", BookmarkStore.BarId, "Patch notes", false, "https://patch.example.com/");
            add("m", BookmarkStore.BarId, "Map planner", false, "https://maps.example.com/"); add("r", BookmarkStore.BarId, "Raid night", true, null);
            add("g1", "g", "Season pass tracker", false, "https://season.example.com/"); add("g2", "g", "Loadout builder", false, "https://loadout.example.com/"); add("g3", "g", "Server status", false, "https://status.example.com/");
            add("i", BookmarkStore.OtherId, "Imported from Chrome", true, null); add("i1", "i", "Bookmarks bar", true, null); add("i2", "i1", "Recipes", false, "https://recipes.example.com/");
            Bookmarks.Replace(items);
            snapBar = true; ApplyBookmarksBar();   // the shared --snap settings stay as they are
            if (manager) { bmFolder = "g"; var pg = BookmarksPage(); content.Children.Add(pg); Grid.SetColumnSpan(pg, 3); }
        }

        // ================================================================ sync through a folder
        DispatcherTimer syncTimer, syncSoonTimer; bool syncBusy, syncDirty;
        public SyncOutcome LastSync;
        static string SyncStatePath { get { return Path.Combine(BrowserSettings.Dir, "sync-state.json"); } }
        public bool SyncReady { get { return S.SyncOn && !string.IsNullOrEmpty(S.SyncFolder); } }
        public void StartSync()
        {
            if (snap || Private || !IsPrimary) return;   // 1.4: the first window syncs for all of them
            if (syncTimer == null) { syncTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(2) }; syncTimer.Tick += (a, b) => { var _ = SyncNow(false); }; }
            if (SyncReady) { syncTimer.Start(); SyncSoon(3); } else syncTimer.Stop();
        }
        void SyncSoon(int seconds)
        {
            if (!SyncReady || snap || Private) return;   // 1.4: never from a private window
            if (!IsPrimary) { var m = Program.First; if (m != null && m != this && m.IsPrimary) m.SyncSoon(seconds); return; }
            syncDirty = true;
            if (syncSoonTimer == null) { syncSoonTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) }; syncSoonTimer.Tick += (a, b) => { syncSoonTimer.Stop(); var _ = SyncNow(false); }; }
            syncSoonTimer.Stop(); syncSoonTimer.Interval = TimeSpan.FromSeconds(seconds); syncSoonTimer.Start();
        }
        SyncInput MakeSyncInput()
        {
            var hist = HistorySyncing && History != null && History.Loaded;   // 1.7: history only encrypted; My filters as last read from Shield
            return new SyncInput { Settings = S, Bookmarks = Bookmarks, Saved = BrowserSettings.LoadSaved(), Spaces = Spaces, CurrentSpace = SpaceId, CurrentLive = Tabs.Count > 0 ? BuildSession() : null, Notes = PageNotes.All,
                ShieldFilters = S.SyncShieldFilters ? ShieldFiltersCache : null, History = hist ? History : null, HistoryMax = hist ? S.SyncHistoryMax : 0 };
        }
        SyncOptions MakeSyncOptions(string pw) { return new SyncOptions { Dev = S.SyncDevice, DevName = Environment.MachineName, Password = pw, CopyData = SyncData.CopyData, KeepDir = BrowserSettings.Dir, HistoryMax = pw != null && HistorySyncing ? S.SyncHistoryMax : 0 }; }
        DateTime filtersReadAt = DateTime.MinValue;
        public async Task<SyncOutcome> SyncNow(bool manual)
        {
            if (!SyncReady || syncBusy || snap) return null;
            syncBusy = true;
            try
            {
                EnsureStores();
                if (!Bookmarks.Loaded) { var b = Bookmarks; await Task.Run(() => b.Load()); }
                string pw = null;
                if (S.SyncEncrypt) { pw = SyncCrypto.LoadPassword(); if (pw == null) { SyncNoted(new SyncOutcome { State = "error", Message = "Encryption is on but no sync password is set on this PC - set it in Settings > Sync" }, manual); return LastSync; } }
                // 1.7: Shield's My filters, read through Shield at most every 10 minutes (not while a game runs), or when asked
                if (S.SyncShieldFilters && ShieldState == "extension" && (ShieldFiltersCache == null || manual || (DateTime.Now - filtersReadAt).TotalMinutes >= 10) && (Game == null || BrowserSettings.Offscreen))
                { filtersReadAt = DateTime.Now; await ReadShieldFilters(); }
                if (HistorySyncing && History != null && !History.Loaded) { var h = History; await Task.Run(() => h.Load()); }
                var input = MakeSyncInput(); var local = SyncData.Gather(input); var hashes = SyncData.KindHashes(local);
                var st = BrowserSettings.Read<SyncState>(SyncStatePath) ?? new SyncState();
                var opts = MakeSyncOptions(pw); var folder = SyncFolderFor(S.SyncFolder);   // 1.6: a profile other than Default syncs in its own sub-folder
                syncDirty = false;
                var o = await Task.Run(() => { try { return SyncCore.Run(local, st, folder, opts); } catch (Exception e) { return new SyncOutcome { State = "error", Message = e.Message }; } });
                if (o.State2 != null)
                {
                    bool saveState = true;
                    if (o.ChangedKinds.Count > 0)
                    {
                        var input2 = MakeSyncInput(); var now2 = SyncData.KindHashes(SyncData.Gather(input2));
                        if (o.ChangedKinds.Any(k => k != "hist" && now2[k] != hashes[k])) { saveState = false; SyncSoon(3); }   // changed here meanwhile: merge again (1.7: history merges in either way)
                        else ApplySync(input2, o);
                    }
                    if (saveState) BrowserSettings.WriteFile(SyncStatePath, o.State2, typeof(SyncState));
                }
                SyncNoted(o, manual);
                return o;
            }
            finally { syncBusy = false; }
        }
        void ApplySync(SyncInput input, SyncOutcome o)
        {
            var before = new { v = S.VerticalTabs, look = S.Look, acc = S.UseHallowAccent, search = S.Search, side = S.SidebarOn, bar = S.BookmarksBar, sites = string.Join(",", S.SidebarSites.Select(x => x.Id + x.Url + x.Name + x.Mobile)) };
            var done = SyncData.Apply(input, o.Result, o.ChangedKinds);
            if (o.ChangedKinds.Contains("set") || o.ChangedKinds.Contains("side"))
            {
                S.Save();
                if (before.v != S.VerticalTabs) ApplyTabLayout();
                if (before.side != S.SidebarOn) Side.ApplyOn();
                if (before.sites != string.Join(",", S.SidebarSites.Select(x => x.Id + x.Url + x.Name + x.Mobile))) Side.Rebuild();
                if (before.bar != S.BookmarksBar) ApplyBookmarksBar();
                if (before.look != S.Look || before.acc != S.UseHallowAccent) Dispatcher.BeginInvoke(new Action(Program.ReloadTheme));
                else { Engine.WriteNewTab(S, Program.AccentHex); }
                RefreshNewTabData();
            }
            if (o.ChangedKinds.Contains("saved")) BrowserSettings.SaveSaved(input.Saved);
            if (o.ChangedKinds.Contains("note")) { PageNotes.Replace(input.Notes); Side.RefreshNative(); }   // 1.6
            if (input.ShieldFiltersIn != null && S.SyncShieldFilters) { var f = input.ShieldFiltersIn; ShieldFiltersCache = f; var _ = WriteShieldFilters(f); }   // 1.7: into Shield (its background recompiles them)
            if (o.ChangedKinds.Contains("ws")) { BrowserSettings.SaveWorkspaces(Spaces); UpdateSpaceButton(); }
            if (done.Count > 0 && o.Incoming > 0) Toast("Synced: " + string.Join(", ", done) + " came in from your other PC" + (o.Conflicts > 0 ? " (" + o.Conflicts + " changed on both - the newer one kept, the other bookmark kept as a conflict copy)" : "") + ".", 6);
        }
        void SyncNoted(SyncOutcome o, bool manual)
        {
            LastSync = o;
            S.SyncNote = DateTime.Now.ToString("HH:mm") + " - " + (o.State == "synced" ? (o.Message.Length > 0 ? o.Message : "Synced") + (o.Encrypted ? " (encrypted)" : "") : o.Message);
            S.Save();
            if (manual) Toast(S.SyncNote, 5);
            if (syncNoteBlock != null) { syncNoteBlock.Text = "Last sync: " + S.SyncNote; syncNoteBlock.SetResourceReference(TextBlock.ForegroundProperty, o.State == "synced" ? "GreenText" : "AmberText"); }
        }
        void SyncOnClose()
        {
            if (!SyncReady || snap || !syncDirty || syncBusy || Bookmarks == null || !Bookmarks.Loaded) return;
            try
            {
                var pw = S.SyncEncrypt ? SyncCrypto.LoadPassword() : null; if (S.SyncEncrypt && pw == null) return;
                var local = SyncData.Gather(MakeSyncInput()); var st = BrowserSettings.Read<SyncState>(SyncStatePath) ?? new SyncState();
                var opts = MakeSyncOptions(pw); var folder = SyncFolderFor(S.SyncFolder);
                var t = Task.Run(() => SyncCore.Run(local, st, folder, opts));
                // the folder now has this PC's changes; what came in is merged next time (the state is kept only if nothing came in)
                if (t.Wait(5000) && t.Result.State2 != null && t.Result.ChangedKinds.Count == 0) BrowserSettings.WriteFile(SyncStatePath, t.Result.State2, typeof(SyncState));
            }
            catch { }
        }

        // Settings > Sync
        TextBlock syncNoteBlock;
        public UIElement[] SyncSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Muted("Your bookmarks, saved sessions and workspaces, settings and sidebar panels on all your PCs - through a folder you pick: OneDrive, Dropbox, a Syncthing folder or a NAS share. There's no Hallow server; each PC reads and writes one file there.", 12, 8));
            var folderText = Ui.Muted(string.IsNullOrEmpty(S.SyncFolder) ? "No folder picked yet." : "Folder: " + S.SyncFolder, 12, 6);
            var warn = Ui.T("", 11.5, true, "AccentLight", 6);
            Action refreshWarn = () =>
            {
                var cloud = !string.IsNullOrEmpty(S.SyncFolder) && SyncCore.LooksLikeCloud(S.SyncFolder);
                warn.Text = cloud && !S.SyncEncrypt ? "This looks like a cloud folder (" + SyncCore.Provider(S.SyncFolder) + "): turn on encryption so the provider can't read your bookmarks." : "";
                warn.Visibility = warn.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            CheckBox on = null;
            on = Ui.Switch("Sync through a folder", "Off by default. Turn it on after picking the folder.", S.SyncOn, v =>
            {
                if (v && string.IsNullOrEmpty(S.SyncFolder)) { on.IsChecked = false; Toast("Pick the sync folder first.", 4); return; }
                S.SyncOn = v; S.Save(); StartSync(); if (v) { var _ = SyncNow(true); }
            });
            items.Add(on);
            items.Add(folderText);
            items.Add(Ui.Row(Ui.Btn("Pick the folder...", () =>
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "The folder Hallow Browser syncs through (on every PC: the same OneDrive / Dropbox / Syncthing folder or NAS share)", ShowNewFolderButton = true })
                {
                    if (!string.IsNullOrEmpty(S.SyncFolder) && Directory.Exists(S.SyncFolder)) d.SelectedPath = S.SyncFolder;
                    if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    S.SyncFolder = d.SelectedPath; S.Save(); folderText.Text = "Folder: " + S.SyncFolder; refreshWarn(); StartSync();
                }
            }, false, "folder-open"), Ui.Btn("Sync now", () => { var _ = SyncNow(true); }, false, "arrows-rotate")));
            items.Add(new Border { Height = 10 });
            var pwBox = new PasswordBox { Width = 220, Height = 28, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            pwBox.SetResourceReference(Control.BackgroundProperty, "FieldBg"); pwBox.SetResourceReference(Control.ForegroundProperty, "Text"); pwBox.SetResourceReference(Control.BorderBrushProperty, "LineDim");
            var pwNote = Ui.Muted(SyncCrypto.LoadPassword() != null ? "A sync password is set on this PC." : "No sync password set on this PC.", 11.5, 6);
            items.Add(Ui.Switch("Encrypt the sync file", "Recommended for cloud folders. A sync password (the same on every PC) -> PBKDF2 -> AES-256-GCM, like Hallow Keys. Without it the file is unreadable. Kept on this PC only, protected by Windows.", S.SyncEncrypt, v => { S.SyncEncrypt = v; S.Save(); refreshWarn(); SyncSoon(2); }));
            items.Add(Ui.Row(pwBox, Ui.Btn("Set password", () =>
            {
                if (pwBox.Password.Length < 8) { Toast("Use at least 8 characters for the sync password.", 4); return; }
                SyncCrypto.SavePassword(pwBox.Password); pwBox.Clear(); pwNote.Text = "A sync password is set on this PC."; Toast("Sync password set on this PC. Use the same one on your other PCs.", 5); SyncSoon(2);
            }, false, "lock")));
            items.Add(pwNote);
            items.Add(warn); refreshWarn();
            syncNoteBlock = Ui.Muted("Last sync: " + (S.SyncNote ?? "never"), 12, 8); items.Add(syncNoteBlock);
            if (LastSync != null) syncNoteBlock.SetResourceReference(TextBlock.ForegroundProperty, LastSync.State == "synced" ? "GreenText" : "AmberText");   // status text: green / amber text shades
            items.AddRange(SyncSettings17());   // 1.7: Shield's My filters, history (encrypted only)
            items.Add(Ui.Muted("Synced: bookmarks and folders, saved sessions, workspaces (the one you're in keeps its open tabs), settings (not window places or per-PC choices like the RAM and CPU limiters), sidebar panels, page notes, Shield's My filters, and - only when you turn it on, and only encrypted - history. Changed on two PCs at once: the newer change wins and the other bookmark is kept as a \"conflict copy\". Never synced: cookies, passwords (Hallow Keys has its own sync), translation keys, open tabs of other windows.", 11.5, 0));
            return items.ToArray();
        }
        // Settings > Bookmarks and new tab
        public UIElement[] BookmarkSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Switch("Show the bookmarks bar", "Under the address bar (Ctrl+Shift+B). Drag bookmarks to reorder them.", S.BookmarksBar, v => { if (v != S.BookmarksBar) ToggleBookmarksBar(); }));
            items.Add(Ui.Row(Ui.Btn("Bookmarks manager", () => NewTab(Omni.BookmarksUrl, true), false, "bookmark"), Ui.Btn("Import from another browser...", () => ShowImport(menuBtn), false, "file-import")));
            items.Add(Ui.Muted("Import reads Chrome, Edge, Brave, Opera, Opera GX, Vivaldi and Firefox profiles on this PC - bookmarks and history only, never passwords or cookies (passwords: Hallow Keys has its own importer).", 11.5, 12));
            items.Add(Ui.Muted("New tab page", 12, 6));
            items.Add(Ui.Switch("Speed dial", "Your most visited pages (from this PC's history) and pages you pin. Hover a tile to pin it or take it off.", S.NewTabTopSites, v => { S.NewTabTopSites = v; S.Save(); NewTabChanged(); }));
            items.Add(Ui.Switch("Clock", null, S.NewTabClock, v => { S.NewTabClock = v; S.Save(); NewTabChanged(); }));
            items.Add(Ui.Switch("Hallow strip", "Guard's score, what Shield blocked today and what's playing - from the other Hallow apps' status files on this PC.", S.NewTabFamily, v => { S.NewTabFamily = v; S.Save(); NewTabChanged(); }));
            items.Add(Ui.Row(Ui.Btn("Show hidden tiles again", () => { S.NewTabHidden.Clear(); S.Save(); NewTabChanged(); Toast("Hidden tiles are back.", 3); }, false, "rotate-left")));
            return items.ToArray();
        }
    }
}
