// Hallow Browser 1.13 - a real History page (hallow://history, Ctrl+H, Menu > History). Until now history only fed the address bar's
// suggestions and Ctrl+K - imported history was there but nowhere to see. The page lists every page you visited, newest first and
// grouped by day (imported pages keep their own dates), with a search box, open (click / Enter), remove one (the x / Delete), and
// "Clear history..." for the last hour, 24 hours, 7 days, 4 weeks or all time. The list is virtualized (only the rows on screen are
// drawn), so the 5,000 pages the store keeps stay fast.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowBrowser
{
    public class HistRow
    {
        public bool Day { get; set; }
        public string DayText { get; set; }
        public string Time { get; set; }
        public string Title { get; set; }
        public string Host { get; set; }
        public string Url { get; set; }
        public string Letter { get; set; }
        object iconView;   // 1.14: the site's logo (cached icon -> brand icon -> letter tile), made when the row is first drawn
        public object IconView { get { if (Day) return null; return iconView ?? (iconView = SiteIcons.Element(Url, 16)); } }
        public Visibility DayVis { get { return Day ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility RowVis { get { return Day ? Visibility.Collapsed : Visibility.Visible; } }
        public override string ToString() { return Day ? DayText : (Title ?? Url); }   // screen readers
    }

    public static class HistoryPageRules
    {
        public static DateTime? When(HistoryEntry e)
        {
            DateTime d; return e != null && e.Last != null && DateTime.TryParse(e.Last, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
        }
        public static string DayLabel(DateTime d, DateTime now)
        {
            if (d.Date == now.Date) return "Today - " + d.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);
            if (d.Date == now.Date.AddDays(-1)) return "Yesterday - " + d.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);
            return d.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);
        }
        public static bool Matches(HistoryEntry e, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return true;
            foreach (var w in q.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if ((e.Title ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0 && (e.Url ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }
        // newest first, a day header before each day's pages; pages without a date go last under "Earlier"
        public static List<HistRow> Rows(IEnumerable<HistoryEntry> all, string q, DateTime now)
        {
            var list = (all ?? new HistoryEntry[0]).Where(e => e != null && Matches(e, q)).Select(e => new { e, d = When(e) })
                .OrderByDescending(x => x.d.HasValue).ThenByDescending(x => x.d ?? DateTime.MinValue).ToList();
            var res = new List<HistRow>(list.Count + 64); string lastDay = null;
            foreach (var x in list)
            {
                var day = x.d.HasValue ? DayLabel(x.d.Value, now) : "Earlier";
                if (day != lastDay) { res.Add(new HistRow { Day = true, DayText = day }); lastDay = day; }
                var host = Omni.HostOf(x.e.Url) ?? "";
                var title = string.IsNullOrWhiteSpace(x.e.Title) ? host : x.e.Title;
                res.Add(new HistRow { Url = x.e.Url, Title = title, Host = host, Time = x.d.HasValue ? x.d.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "", Letter = Initial(host) });
            }
            return res;
        }
        public static string Initial(string host) { var h = (host ?? "").StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : (host ?? ""); return h.Length > 0 ? h.Substring(0, 1).ToUpperInvariant() : "?"; }
        public static readonly string[][] Ranges = { new[] { "hour", "The last hour" }, new[] { "day", "The last 24 hours" }, new[] { "week", "The last 7 days" }, new[] { "4weeks", "The last 4 weeks" }, new[] { "all", "All time" } };
        // the oldest moment a range clears from (null = everything)
        public static DateTime? Since(string range, DateTime now)
        {
            switch (range)
            {
                case "hour": return now.AddHours(-1);
                case "day": return now.AddHours(-24);
                case "week": return now.AddDays(-7);
                case "4weeks": return now.AddDays(-28);
                default: return null;
            }
        }
    }

    public partial class HistoryStore
    {
        // 1.13: the History page - one page out, or every page visited since a moment (a page's last visit counts)
        public bool Remove(string url)
        {
            bool done; lock (gate) { done = items.RemoveAll(x => x.Url == url) > 0; if (done) dirty = true; }
            if (done) Save();
            return done;
        }
        public int RemoveSince(DateTime from)
        {
            int n;
            lock (gate)
            {
                n = items.RemoveAll(x => { var d = HistoryPageRules.When(x); return d.HasValue && d.Value >= from; });
                if (n > 0) dirty = true;
            }
            if (n > 0) Save();
            return n;
        }
    }

    public partial class BrowserWindow
    {
        ListBox histList; TextBox histSearch; TextBlock histCount; List<HistRow> histRows = new List<HistRow>(); DispatcherTimer histTimer;
        public List<HistoryEntry> SnapHistoryItems;   // --snap: made-up pages
        public double HistoryBuildMs { get; private set; }
        public int HistoryRowCount { get { return histRows.Count(r => !r.Day); } }
        public int HistoryDayCount { get { return histRows.Count(r => r.Day); } }
        public int HistoryRealized { get { return histList == null ? -1 : CountItems(histList); } }
        static int CountItems(DependencyObject d) { int n = 0; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var c = VisualTreeHelper.GetChild(d, i); if (c is ListBoxItem) n++; else n += CountItems(c); } return n; }
        public List<HistRow> HistoryRows { get { return histRows; } }

        static DataTemplate histTemplate; static Style histItemStyle;
        static void HistTemplates()
        {
            if (histTemplate != null) return;
            const string ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";
            histTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate " + ns + "><Grid>" +
                "<TextBlock Text='{Binding DayText}' Visibility='{Binding DayVis}' FontWeight='SemiBold' FontSize='13' Margin='6,16,0,6' Foreground='{DynamicResource AccentLight}'/>" +
                "<Grid Visibility='{Binding RowVis}' Height='34'>" +
                "<Grid.ColumnDefinitions><ColumnDefinition Width='54'/><ColumnDefinition Width='30'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='34'/></Grid.ColumnDefinitions>" +
                "<TextBlock Text='{Binding Time}' Foreground='{DynamicResource Muted}' FontSize='12' VerticalAlignment='Center' Margin='8,0,0,0'/>" +
                "<ContentControl Grid.Column='1' Width='18' Height='18' Focusable='False' IsTabStop='False' VerticalAlignment='Center' HorizontalAlignment='Center' Content='{Binding IconView}'/>" +
                "<TextBlock Grid.Column='2' Text='{Binding Title}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center' Margin='4,0,8,0' Foreground='{DynamicResource Text}'/>" +
                "<TextBlock Grid.Column='3' Text='{Binding Host}' Foreground='{DynamicResource Muted}' FontSize='12' VerticalAlignment='Center' MaxWidth='240' TextTrimming='CharacterEllipsis' Margin='0,0,8,0'/>" +
                "<Button Grid.Column='4' Tag='del' Style='{DynamicResource Tool}' Width='28' Height='26' ToolTip='Remove from history' AutomationProperties.Name='Remove from history' Content='&#x2715;' FontSize='11'/>" +
                "</Grid></Grid></DataTemplate>");
            histItemStyle = (Style)XamlReader.Parse("<Style " + ns + " TargetType='ListBoxItem'><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='0'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='b' Background='Transparent' CornerRadius='6'><ContentPresenter/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource TabHover}'/></Trigger>" +
                "<Trigger Property='IsSelected' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource PanelHi}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");
        }

        FrameworkElement HistoryPage()
        {
            HistTemplates();
            if (SnapHistoryItems == null) EnsureStores();
            var root = new DockPanel { LastChildFill = true };
            root.SetResourceReference(Panel.BackgroundProperty, "Bg");
            var head = new StackPanel { MaxWidth = 900, Margin = new Thickness(28, 26, 28, 6) };
            head.Children.Add(Ui.Row(Ui.Icon("clock-rotate-left", 18, "AccentBrush"), Ui.T("  HISTORY", 20, true, "Text")));
            histCount = Ui.Muted("", 12, 10); head.Children.Add(histCount);
            histSearch = new TextBox { Style = Ui.S("Field"), Width = 360, Height = 30, ToolTip = "Search history (title or address)", Margin = new Thickness(0, 0, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(histSearch, "Search history");
            histSearch.TextChanged += (s, e) => { if (histTimer == null) { histTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) }; histTimer.Tick += (a, b) => { histTimer.Stop(); FillHistory(); }; } histTimer.Stop(); histTimer.Start(); };
            Button clear = null;
            clear = Ui.Btn("Clear history...", () => HistoryClearFlyout(clear), false, "broom");
            var hint = Ui.Muted("Search history", 12.5); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(10, 0, 0, 0);
            histSearch.TextChanged += (s, e) => hint.Visibility = histSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var sg = new Grid(); sg.Children.Add(histSearch); sg.Children.Add(hint);
            head.Children.Add(Ui.Row(sg, clear));
            DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
            histList = new ListBox { ItemTemplate = histTemplate, ItemContainerStyle = histItemStyle, BorderThickness = new Thickness(0), Background = Brushes.Transparent, MaxWidth = 900, Margin = new Thickness(22, 0, 22, 18), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            histList.SetResourceReference(Control.ForegroundProperty, "Text");
            VirtualizingStackPanel.SetIsVirtualizing(histList, true); VirtualizingStackPanel.SetVirtualizationMode(histList, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(histList, true); ScrollViewer.SetHorizontalScrollBarVisibility(histList, ScrollBarVisibility.Disabled);
            System.Windows.Automation.AutomationProperties.SetName(histList, "History");
            histList.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) => { var b = e.OriginalSource as Button; var r = b == null ? null : b.DataContext as HistRow; if (r != null && !r.Day && (b.Tag as string) == "del") { RemoveHistory(r.Url); e.Handled = true; } }));
            histList.MouseLeftButtonUp += (s, e) =>
            {
                var src = e.OriginalSource as DependencyObject; while (src != null && !(src is ListBoxItem)) { if (src is Button) return; src = VisualTreeHelper.GetParent(src); }
                var r = src == null ? null : ((ListBoxItem)src).DataContext as HistRow; if (r != null && !r.Day) OpenHistory(r);
            };
            histList.PreviewKeyDown += (s, e) =>
            {
                var r = histList.SelectedItem as HistRow; if (r == null || r.Day) return;
                if (e.Key == Key.Enter) { OpenHistory(r); e.Handled = true; } else if (e.Key == Key.Delete) { RemoveHistory(r.Url); e.Handled = true; }
            };
            root.Children.Add(histList);
            FillHistory();
            return root;
        }
        void OpenHistory(HistRow r) { if (r != null && Omni.IsWeb(r.Url)) NewTab(r.Url, true); }
        public void FillHistory()
        {
            if (histList == null) return;
            var sw = Stopwatch.StartNew();
            var all = SnapHistoryItems ?? (History != null ? (History.Loaded ? History.All() : LoadHistoryNow()) : new List<HistoryEntry>());
            histRows = HistoryPageRules.Rows(all, histSearch == null ? "" : histSearch.Text, DateTime.Now);
            histList.ItemsSource = histRows;
            var n = HistoryRowCount;
            histCount.Text = all.Count == 0 ? "No history yet. Pages you visit (and history you import from another browser) show up here, newest first." :
                (histSearch != null && histSearch.Text.Trim().Length > 0 ? n.ToString("N0") + " of " + all.Count.ToString("N0") + " pages match" : all.Count.ToString("N0") + " pages, newest first (the browser keeps the last " + HistoryStore.Max.ToString("N0") + "). Click to open, x to remove.");
            HistoryBuildMs = sw.Elapsed.TotalMilliseconds;
        }
        List<HistoryEntry> LoadHistoryNow() { History.Load(); return History.All(); }
        public void RefreshHistoryPage() { foreach (var w in Program.Windows.Concat(new[] { this }).Distinct().ToList()) w.FillHistory(); }
        public void HistorySearchFor(string q) { if (histSearch != null) { histSearch.Text = q; if (histTimer != null) histTimer.Stop(); FillHistory(); } }
        public bool RemoveHistory(string url)
        {
            if (History == null) return false;
            var ok = History.Remove(url);
            RefreshHistoryPage(); RefreshNewTabData();
            return ok;
        }
        public int ClearHistory(string range)
        {
            if (History == null) return 0;
            if (!History.Loaded) History.Load();
            var from = HistoryPageRules.Since(range, DateTime.Now);
            int n;
            if (from == null) { n = History.Count; History.Clear(); } else n = History.RemoveSince(from.Value);
            RefreshHistoryPage(); RefreshNewTabData();
            return n;
        }
        void HistoryClearFlyout(UIElement at)
        {
            var sp = new StackPanel();
            var h = Ui.Label("Clear history"); h.Margin = new Thickness(12, 8, 12, 4); sp.Children.Add(h);
            var note = Ui.Muted("Pages you visited in that time leave this list and the address bar's suggestions. Bookmarks, downloads, cookies and passwords stay (Clear browsing data... for those).", 11.5, 6); note.Margin = new Thickness(12, 0, 12, 6); sp.Children.Add(note);
            foreach (var r in HistoryPageRules.Ranges)
            {
                var range = r[0];
                sp.Children.Add(Ui.MenuRow(range == "all" ? "trash-can" : "clock-rotate-left", r[1], null, () => { Panels.CloseFlyouts(); var n = ClearHistory(range); Toast(n == 0 ? "Nothing to clear in that time." : n.ToString("N0") + (n == 1 ? " page" : " pages") + " cleared from history.", 4); }));
            }
            Panels.ShowFlyout(at, sp, 330);
        }
    }
}
