// Hallow Browser 1.7 - a cleaner Settings page: the cards grouped into sections (General, Privacy & security, Shield, Gaming,
// Appearance, Sync, Profiles, Advanced) with a row of section links, and a search box that filters the settings as you type -
// cards whose title matches stay whole, otherwise only the matching rows stay bright - and jumps to the first match (Enter).
// Ctrl+K finds settings too: the same index (every card and every switch / choice in it), opening Settings at that setting.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowBrowser
{
    public class SettingEntry { public string Section, Card, Label; public override string ToString() { return Section + " > " + Card + (Label != null ? " > " + Label : ""); } }

    public static class SettingsLayout
    {
        public static readonly string[] Sections = { "General", "Privacy & security", "Shield", "Gaming", "Appearance", "Sync", "Profiles", "Advanced" };
        static readonly Dictionary<string, string> sectionOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Search", "General" }, { "Start-up", "General" }, { "Address bar", "General" }, { "Tabs and workspaces", "General" }, { "Bookmarks and new tab", "General" },
            { "Downloads", "General" }, { "Tab previews, gestures and media", "General" }, { "Translate pages", "General" }, { "Cast to a TV", "General" }, { "Spell check", "General" }, { "Sidebar", "General" },
            { "Privacy", "Privacy & security" }, { "Security", "Privacy & security" }, { "Sites, extensions and windows", "Privacy & security" }, { "Hallow Keys", "Privacy & security" },
            { "Hallow Shield", "Shield" },
            { "Speed and gaming", "Gaming" }, { "Memory saver and battery", "Gaming" }, { "Game corner", "Gaming" }, { "Hallow apps on the new tab page", "Gaming" }, { "Live notifications", "Gaming" }, { "YouTube and Twitch", "Gaming" },
            { "Look", "Appearance" }, { "Accessibility", "Appearance" }, { "Sounds and ambience", "Appearance" }, { "Taskbar", "Appearance" },
            { "Sync", "Sync" }, { "Profiles", "Profiles" }, { "About", "Advanced" } };
        public static string SectionOf(string card) { string s; return sectionOf.TryGetValue(card ?? "", out s) ? s : "Advanced"; }

        // ---- the words a row / card can be found by
        public static string TextOf(DependencyObject e)
        {
            var parts = new List<string>(); Collect(e, parts, 0);
            return string.Join(" ", parts);
        }
        static void Collect(DependencyObject e, List<string> parts, int depth)
        {
            if (e == null || depth > 12) return;
            var tb = e as TextBlock; if (tb != null && !string.IsNullOrEmpty(tb.Text)) parts.Add(tb.Text);
            var cc = e as ContentControl; if (cc != null && cc.Content is string) parts.Add((string)cc.Content);
            var tx = e as TextBox; if (tx != null && tx.ToolTip is string) parts.Add((string)tx.ToolTip);
            foreach (var c in LogicalTreeHelper.GetChildren(e)) { var d = c as DependencyObject; if (d != null) Collect(d, parts, depth + 1); }
        }
        // every word of the query somewhere in the text (case and accents as typed: simple, predictable)
        public static bool Matches(string text, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var w in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }
        // the label a row is known by in Ctrl+K: a switch's title, or the short text over a row of choices
        public static string LabelOf(UIElement row, UIElement next)
        {
            var cb = row as CheckBox;
            if (cb != null) { var sp = cb.Content as StackPanel; var first = sp == null ? null : sp.Children.OfType<TextBlock>().FirstOrDefault(); return first != null ? first.Text : cb.Content as string; }
            var tb = row as TextBlock;
            if (tb != null && (next is WrapPanel || next is ComboBox) && !string.IsNullOrEmpty(tb.Text) && tb.Text.Length <= 90) return tb.Text;
            return null;
        }

        public class CardInfo { public string Title, Section; public Border Card; public List<UIElement> Rows = new List<UIElement>(); public List<string> RowText = new List<string>(); public string Text; }
        public class Page { public ScrollViewer Scroll; public TextBox Box; public TextBlock Count; public List<CardInfo> Cards = new List<CardInfo>(); public Dictionary<string, FrameworkElement> Heads = new Dictionary<string, FrameworkElement>(); public FrameworkElement Empty; public StackPanel Col; public int Shown; }

        public static List<CardInfo> Infos(List<Tuple<string, Border>> cards)
        {
            var res = new List<CardInfo>();
            foreach (var c in cards)
            {
                var ci = new CardInfo { Title = c.Item1, Section = SectionOf(c.Item1), Card = c.Item2 };
                var sp = c.Item2.Child as StackPanel;
                if (sp != null) foreach (UIElement r in sp.Children) { if (r is TextBlock && sp.Children.IndexOf(r) == 0) continue; ci.Rows.Add(r); ci.RowText.Add(TextOf(r)); }
                ci.Text = ci.Title + " " + ci.Section + " " + string.Join(" ", ci.RowText);
                res.Add(ci);
            }
            return res;
        }
        public static List<SettingEntry> Entries(List<CardInfo> infos)
        {
            var res = new List<SettingEntry>();
            foreach (var ci in infos)
            {
                res.Add(new SettingEntry { Section = ci.Section, Card = ci.Title });
                for (int i = 0; i < ci.Rows.Count; i++) { var l = LabelOf(ci.Rows[i], i + 1 < ci.Rows.Count ? ci.Rows[i + 1] : null); if (!string.IsNullOrWhiteSpace(l) && !res.Any(x => x.Card == ci.Title && x.Label == l)) res.Add(new SettingEntry { Section = ci.Section, Card = ci.Title, Label = l }); }
            }
            return res;
        }

        // ---- the page: header, search, section links, then each section's cards
        public static Page Build(BrowserWindow w, List<UIElement> header, List<Tuple<string, Border>> cards)
        {
            var pg = new Page();
            var col = new StackPanel { MaxWidth = 760, Margin = new Thickness(28, 26, 28, 40), HorizontalAlignment = HorizontalAlignment.Center }; pg.Col = col;
            foreach (var h in header) col.Children.Add(h);
            // search
            var sb = new Border { CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), Height = 34, Margin = new Thickness(0, 0, 0, 10) };
            sb.SetResourceReference(Border.BackgroundProperty, "FieldBg"); sb.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            var sg = new Grid(); sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); sg.ColumnDefinitions.Add(new ColumnDefinition()); sg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sg.Children.Add(Ui.Icon("magnifying-glass", 12, "Muted"));
            var box = new TextBox { Style = Ui.S("Address"), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Search settings" }; Grid.SetColumn(box, 1); sg.Children.Add(box);
            var hint = Ui.Muted("Search settings - e.g. translate, cookies, sleep, sync", 12.5); hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(3, 0, 0, 0); Grid.SetColumn(hint, 1); sg.Children.Add(hint);
            var count = Ui.Muted("", 11); count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(8, 0, 14, 0); Grid.SetColumn(count, 2); sg.Children.Add(count);
            sb.Child = sg; col.Children.Add(sb); pg.Box = box; pg.Count = count;
            box.GotKeyboardFocus += (a, b) => sb.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            box.LostKeyboardFocus += (a, b) => sb.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            // section links
            var nav = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) }; col.Children.Add(nav);
            pg.Cards = Infos(cards).OrderBy(c => Array.IndexOf(Sections, c.Section)).ToList();   // in the order shown (stable within a section)
            foreach (var sec in Sections)
            {
                var mine = pg.Cards.Where(c => c.Section == sec).ToList(); if (mine.Count == 0) continue;
                var head = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
                head.Children.Add(Ui.T(sec, 18, true, "Text"));
                var line = new Border { Height = 2, Width = 36, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), CornerRadius = new CornerRadius(1) }; line.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); head.Children.Add(line);
                col.Children.Add(head); pg.Heads[sec] = head;
                foreach (var c in mine) col.Children.Add(c.Card);
                var s2 = sec; var link = new Button { Content = sec, Style = Ui.S("Btn"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3), ToolTip = "Go to " + sec };
                link.Click += (a, b) => { box.Text = ""; ScrollTo(pg, pg.Heads[s2]); var first = mine[0].Card; link.Dispatcher.BeginInvoke(new Action(() => A11y.FocusFirst(first)), DispatcherPriority.Loaded); };   // 1.9: the keyboard goes to the section too
                nav.Children.Add(link);
            }
            var empty = Ui.Muted("No settings match.", 13, 0); empty.Visibility = Visibility.Collapsed; empty.Margin = new Thickness(0, 12, 0, 0); col.Children.Add(empty); pg.Empty = empty;
            var sv = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            sv.SetResourceReference(Control.BackgroundProperty, "Bg"); pg.Scroll = sv;
            box.TextChanged += (a, b) => { hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Filter(pg, box.Text); w.SettingsQuery = box.Text; if (box.Text.Length > 0) sv.ScrollToTop(); };
            box.PreviewKeyDown += (a, b) => { if (b.Key == Key.Enter) { var tg = JumpFirst(pg); if (tg != null) box.Dispatcher.BeginInvoke(new Action(() => A11y.FocusFirst(tg)), DispatcherPriority.Loaded); b.Handled = true; } else if (b.Key == Key.Escape) { box.Text = ""; b.Handled = true; } };
            return pg;
        }
        public static void Filter(Page pg, string q)
        {
            q = (q ?? "").Trim(); int shown = 0;
            foreach (var c in pg.Cards)
            {
                bool all = q.Length == 0 || Matches(c.Title + " " + c.Section, q);
                bool any = all;
                for (int i = 0; i < c.Rows.Count; i++)
                {
                    var hit = all || Matches(c.RowText[i], q);
                    c.Rows[i].Opacity = hit ? 1 : 0.35;
                    if (hit) any = true;
                }
                c.Card.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                c.Card.SetResourceReference(Border.BorderBrushProperty, q.Length > 0 && any ? "AccentBrush" : "Line");
                if (any) shown++;
            }
            foreach (var kv in pg.Heads) kv.Value.Visibility = pg.Cards.Any(c => c.Section == kv.Key && c.Card.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            pg.Empty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
            pg.Count.Text = q.Length == 0 ? "" : shown == 0 ? "nothing" : shown == 1 ? "1 card" : shown + " cards";
            pg.Shown = shown;
        }
        // the first matching row (or card) scrolled to the top
        public static FrameworkElement JumpFirst(Page pg)
        {
            var q = (pg.Box.Text ?? "").Trim();
            foreach (var c in pg.Cards)
            {
                if (c.Card.Visibility != Visibility.Visible) continue;
                FrameworkElement target = c.Card;
                if (q.Length > 0 && !Matches(c.Title + " " + c.Section, q)) for (int i = 0; i < c.Rows.Count; i++) if (Matches(c.RowText[i], q)) { target = c.Rows[i] as FrameworkElement ?? c.Card; break; }
                ScrollTo(pg, target == c.Card ? c.Card : target);
                return target;
            }
            return null;
        }
        public static void ScrollTo(Page pg, FrameworkElement e)
        {
            if (e == null) return;
            Action go = () => { try { var p = e.TransformToAncestor(pg.Col).Transform(new Point(0, 0)); pg.Scroll.ScrollToVerticalOffset(Math.Max(0, p.Y + pg.Col.Margin.Top - 12)); } catch { e.BringIntoView(); } };
            if (pg.Col.IsLoaded) { pg.Col.UpdateLayout(); go(); } else pg.Col.Loaded += (a, b) => pg.Col.Dispatcher.BeginInvoke(go, DispatcherPriority.Loaded);
        }
    }

    public partial class BrowserWindow
    {
        public string SettingsQuery;                    // kept while the page is rebuilt (a setting changed, the look changed)
        public SettingsLayout.Page SettingsPg;          // the settings page now shown (tests read it)
        public static List<SettingEntry> SettingsIndexCache;
        // Settings, opened (or brought forward) at the setting that matches the words
        public void OpenSettingsAt(string query)
        {
            SettingsQuery = query;
            var t = Tabs.FirstOrDefault(x => x.Url == Omni.SettingsUrl);
            if (t == null) NewTab(Omni.SettingsUrl, true); else { Activate(t); }
            var pg = SettingsPg; if (pg == null) return;
            if (pg.Box.Text != (query ?? "")) pg.Box.Text = query ?? ""; else SettingsLayout.Filter(pg, query);
            SettingsLayout.JumpFirst(pg);
        }
        // every card and setting, for Ctrl+K (made once from a settings page built off to the side; the live page isn't touched)
        public List<SettingEntry> SettingsIndex()
        {
            if (SettingsIndexCache != null) return SettingsIndexCache;
            var keepNote = syncNoteBlock; var keepPg = SettingsPg; var keepQ = SettingsQuery; var keepKey = fillKeyAction;
            try { SettingsQuery = null; Panels.SettingsPage(this); SettingsIndexCache = SettingsLayout.Entries(SettingsPg.Cards); }
            catch { SettingsIndexCache = new List<SettingEntry>(); }
            finally { syncNoteBlock = keepNote; SettingsPg = keepPg; SettingsQuery = keepQ; fillKeyAction = keepKey; }
            return SettingsIndexCache;
        }
    }
}
