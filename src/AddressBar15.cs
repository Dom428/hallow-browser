// Hallow Browser 1.5 - the address bar's list: tab search ("% name"), the calculator / unit answer, search keywords, addresses you
// typed before, the engine's suggestions (only when you switched them on - never in private windows) and "Search ... for".
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowBrowser
{
    public class SuggestRow { public string Kind, Text, Fill; public Action Go; }   // Kind: tab | calc | keyword | history | engine | search; Fill = what the box shows when picked

    public partial class BrowserWindow
    {
        List<SuggestRow> rows = new List<SuggestRow>(); int suggestIndex = -1;
        public static int SuggestFetches; public static string LastSuggestQuery;
        DispatcherTimer suggestTimer; string suggestFor; List<string> engineRows = new List<string>(); string engineRowsFor;
        bool suggestFilling; string suggestTyped = "";   // 1.17: a highlighted row's text in the box must not rebuild the list (Up / Down kept starting over)

        // the rows for what's typed (no UI - the tests read this)
        public List<SuggestRow> BuildSuggestions(string t)
        {
            var list = new List<SuggestRow>(); t = t ?? ""; var trimmed = t.Trim();
            if (trimmed.Length == 0) return list;
            // "% name": your open tabs (this window first, then the others of the same kind)
            var tq = TabSearch.Query(t);
            if (tq != null)
            {
                foreach (var w in new[] { this }.Concat(Program.Windows.Where(x => x != this && x.Private == Private && !x.Gone)))
                    foreach (var tab in w.Tabs.Where(x => TabSearch.Matches(tq, x.Title, x.Url)))
                    {
                        var ww = w; var tt = tab;
                        list.Add(new SuggestRow { Kind = "tab", Text = tab.Title + (Omni.IsWeb(tab.Url) ? "  -  " + Omni.HostOf(tab.Url) : "") + (w != this ? "  (another window)" : ""), Fill = t, Go = () => { ww.Activate(tt); if (ww != this) ww.Bring(); } });
                        if (list.Count >= 8) break;
                    }
                if (list.Count == 0) list.Add(new SuggestRow { Kind = "tab", Text = "No open tab matches \"" + tq + "\"", Fill = t, Go = () => { } });
                return list;
            }
            var ans = Calc.Answer(trimmed);
            if (ans != null) { var a = ans; list.Add(new SuggestRow { Kind = "calc", Text = "= " + a, Fill = t, Go = () => { try { Clipboard.SetText(a.Replace(",", "")); } catch { } Toast("Copied " + a, 2); } }); }
            SearchKeyword kw; var kurl = Keywords.Expand(S.Keywords, trimmed, out kw);
            if (kurl != null) { var u = kurl; list.Add(new SuggestRow { Kind = "keyword", Text = "Search " + kw.Name + " for \"" + trimmed.Substring(trimmed.IndexOf(' ') + 1).Trim() + "\"", Fill = t, Go = () => Navigate(u, false) }); }
            List<string> hist; lock (S.Typed) hist = S.Typed.ToList();
            foreach (var h in Omni.Suggest(hist, t, 6)) { var hh = h; list.Add(new SuggestRow { Kind = "history", Text = h.Length > 90 ? h.Substring(0, 90) + "..." : h, Fill = h, Go = () => Navigate(hh, false) }); }
            if (SuggestionsAllowed && engineRowsFor == trimmed)
                foreach (var s in engineRows.Take(5)) { var ss = s; if (!list.Any(x => x.Fill == s)) list.Add(new SuggestRow { Kind = "engine", Text = s, Fill = s, Go = () => Navigate(Omni.SearchUrl(S.Search, ss), false) }); }
            list.Add(new SuggestRow { Kind = "search", Text = "Search " + Omni.EngineName(S.Search) + " for \"" + trimmed + "\"", Fill = t, Go = () => Navigate(Omni.SearchUrl(S.Search, trimmed), false) });
            return list;
        }
        public bool SuggestionsAllowed { get { return S.SearchSuggest && !Private; } }
        static bool LooksLikeAddress(string t) { var u = Omni.Interpret(t, "duckduckgo"); return u != null && !u.StartsWith("https://duckduckgo.com/?q=", StringComparison.Ordinal); }

        void Suggest()
        {
            if (suggestFilling) return;
            var t = Address.Text; suggestTyped = t;
            rows = BuildSuggestions(t); suggestIndex = -1;
            suggestList.Children.Clear();
            if (rows.Count == 0) { suggest.IsOpen = false; return; }
            foreach (var r in rows)
            {
                var icon = r.Kind == "tab" ? "window-restore" : r.Kind == "calc" ? "calculator" : r.Kind == "keyword" ? "magnifying-glass" : r.Kind == "history" ? "globe" : "magnifying-glass";
                var rr = r; Action go = () => { suggest.IsOpen = false; rr.Go(); }; var keys = r.Kind == "calc" ? "copy" : r.Kind == "tab" ? "switch" : null;
                var row = r.Kind == "history" && Omni.IsWeb(r.Fill) ? SiteIcons.MenuRow(r.Fill, r.Text, keys, go) : Ui.MenuRow(icon, r.Text, keys, go);   // 1.14: the site's logo
                if (r.Kind == "calc") ((Grid)row.Content).Children.OfType<TextBlock>().First().FontWeight = FontWeights.SemiBold;
                suggestList.Children.Add(row);
            }
            suggestList.Width = Math.Max(300, addressBox.ActualWidth - 12);
            suggest.IsOpen = true;
            // warm up the connection to where Enter would go
            var first = rows.FirstOrDefault(x => x.Kind == "history");
            Preconnect(first != null ? first.Fill : Omni.Interpret(t, S.Search));
            // the engine's suggestions: only when switched on, never in private windows, never for addresses, sums or tab searches
            var q = t.Trim();
            if (SuggestionsAllowed && q.Length >= 2 && q.Length <= 120 && TabSearch.Query(q) == null && !LooksLikeAddress(q) && engineRowsFor != q)
            {
                suggestFor = q;
                if (suggestTimer == null) { suggestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) }; suggestTimer.Tick += (a, b) => { suggestTimer.Stop(); FetchSuggestions(suggestFor); }; }
                suggestTimer.Stop(); suggestTimer.Start();
            }
        }
        public void FetchSuggestions(string q)
        {
            if (!SuggestionsAllowed || string.IsNullOrEmpty(q)) return;
            var engine = S.Search; SuggestFetches++; LastSuggestQuery = q;
            Task.Run(() => Suggestions.Fetch(engine, q, 6)).ContinueWith(x => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (x.Result == null || !SuggestionsAllowed) return;
                engineRows = x.Result; engineRowsFor = q;
                if (Address.IsKeyboardFocusWithin && Address.Text.Trim() == q) Suggest();
            })));
        }
        public List<string> EngineRows { get { return engineRows; } }
        void MoveSuggest(int d)
        {
            if (rows.Count == 0) return;
            suggestIndex = Math.Max(-1, Math.Min(rows.Count - 1, suggestIndex + d));
            for (int i = 0; i < suggestList.Children.Count; i++) { var b = suggestList.Children[i] as Button; if (b != null) { if (i == suggestIndex) b.SetResourceReference(BackgroundProperty, "PanelHi"); else b.Background = Brushes.Transparent; } }
            // the row's text in the box (back to what you typed above the first row) - without rebuilding the list
            var fill = suggestIndex < 0 ? suggestTyped : rows[suggestIndex].Kind != "calc" && rows[suggestIndex].Kind != "tab" ? rows[suggestIndex].Fill : null;
            if (fill != null && Address.Text != fill) { suggestFilling = true; try { Address.Text = fill; Address.CaretIndex = fill.Length; } finally { suggestFilling = false; } }
            if (suggestIndex >= 0 && suggestIndex < suggestList.Children.Count) { var fe = suggestList.Children[suggestIndex] as FrameworkElement; if (fe != null) fe.BringIntoView(); }
        }
        public int SuggestIndex { get { return suggestIndex; } }
        // tests: type into the address bar and press keys (Down,Down,Enter...) through WPF's own key events - the real keyboard is never used
        public Dictionary<string, object> OmniKeys(string text, string keys)
        {
            Address.Focus(); Address.Text = text; if (!suggest.IsOpen) Suggest();
            var src = PresentationSource.FromVisual(Address);
            var log = new List<object>();
            foreach (var k in (keys ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Key key; if (!Enum.TryParse(k.Trim(), true, out key)) continue;
                var ev = new KeyEventArgs(Keyboard.PrimaryDevice, src, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                Address.RaiseEvent(ev);
                log.Add(new Dictionary<string, object> { { "key", key.ToString() }, { "handled", ev.Handled }, { "index", suggestIndex }, { "text", Address.Text }, { "open", suggest.IsOpen } });
            }
            return new Dictionary<string, object> { { "rows", rows.Select(r => r.Kind + ":" + r.Fill).ToList() }, { "index", suggestIndex }, { "text", Address.Text }, { "open", suggest.IsOpen }, { "steps", log } };
        }
        public bool SuggestOpen { get { return suggest.IsOpen; } }
        // 1.17: Up / Down / Enter / Esc reach the list before the text box uses them (PreviewKeyDown)
        void AddressKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && suggest.IsOpen && suggestIndex >= 0) { suggestIndex = 0; MoveSuggest(-1); e.Handled = true; return; }   // first Esc: back to what you typed
            if (e.Key == Key.Down && !suggest.IsOpen && Address.Text.Trim().Length > 0) { Suggest(); if (suggest.IsOpen) MoveSuggest(1); e.Handled = true; return; }
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (suggest.IsOpen && suggestIndex >= 0 && suggestIndex < rows.Count) { var r = rows[suggestIndex]; suggest.IsOpen = false; r.Go(); return; }
                Navigate(Address.Text, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            }
            else if (e.Key == Key.Escape) { suggest.IsOpen = false; Address.Text = Omni.Display(Active == null ? null : Active.Url); Address.SelectAll(); if (Active != null && Active.View != null) Active.View.Focus(); e.Handled = true; }
            else if (e.Key == Key.Down && suggest.IsOpen) { MoveSuggest(1); e.Handled = true; }
            else if (e.Key == Key.Up && suggest.IsOpen) { MoveSuggest(-1); e.Handled = true; }
        }
        // typed text that isn't an address: a tab search ("% name" switches to the first match) or a keyword ("yt cats")
        string Special(string typed, out bool handled)
        {
            handled = false;
            var tq = TabSearch.Query(typed);
            if (tq != null)
            {
                handled = true; suggest.IsOpen = false;
                var hit = BuildSuggestions(typed).FirstOrDefault(r => r.Kind == "tab" && !r.Text.StartsWith("No open tab"));
                if (hit != null) hit.Go(); else Toast("No open tab matches \"" + tq + "\".", 3);
                return null;
            }
            SearchKeyword kw; return Keywords.Expand(S.Keywords, typed, out kw);
        }
    }
}
