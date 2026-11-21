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
            var t = Address.Text;
            rows = BuildSuggestions(t); suggestIndex = -1;
            suggestList.Children.Clear();
            if (rows.Count == 0) { suggest.IsOpen = false; return; }
            foreach (var r in rows)
            {
                var icon = r.Kind == "tab" ? "window-restore" : r.Kind == "calc" ? "calculator" : r.Kind == "keyword" ? "magnifying-glass" : r.Kind == "history" ? "globe" : "magnifying-glass";
                var rr = r; var row = Ui.MenuRow(icon, r.Text, r.Kind == "calc" ? "copy" : r.Kind == "tab" ? "switch" : null, () => { suggest.IsOpen = false; rr.Go(); });
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
            if (suggestIndex >= 0 && rows[suggestIndex].Kind != "calc" && rows[suggestIndex].Kind != "tab") { Address.Text = rows[suggestIndex].Fill; Address.CaretIndex = Address.Text.Length; }
        }
        void AddressKey(object s, KeyEventArgs e)
        {
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
