// Hallow Browser 1.13 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others) and the --snap pictures: the
// menu's shape, opening it and its submenus at a given window height, the History page, the import mapping, the bar repair and the
// default-browser check. Test profiles only: never the user's real bookmarks, history or another browser's profile.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V113(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                case "model":   // the menu's shape, and what Ctrl+K lists
                    return Program.OnUi(w, () =>
                    {
                        var m = Menu113.Model(w);
                        var labels = Menu113.Labels(m).ToList();
                        var pal = Palette.Commands(w).Select(x => x.Title).ToList();
                        return "ok " + js.Serialize(new Dictionary<string, object> {
                            { "top", m.Where(x => !x.Separator).Select(x => x.Zoom ? "[zoom]" : x.Text + (x.Kids != null ? " >" : "")).ToList() }, { "topEntries", Menu113.TopEntries(m) },
                            { "missing", Menu113.Missing(labels.Concat(pal)) }, { "missingInMenu", Menu113.Missing(labels) },
                            { "leavesNotInPalette", Menu113.Leaves(m).Where(x => x.Run != null && x.Enabled && !pal.Contains(x.Text, StringComparer.OrdinalIgnoreCase)).Select(x => x.Text).ToList() },
                            { "subs", m.Where(x => x.Kids != null).ToDictionary(x => x.Id, x => (object)(x.CustomLabels ?? Menu113.Labels(x.Kids).ToList())) },
                            { "keys", Menu113.Leaves(m).Where(x => x.Keys != null).GroupBy(x => x.Text).ToDictionary(g => g.Key, g => (object)g.First().Keys) }, { "keysTop", m.Where(x => x.Keys != null && x.Kids == null).ToDictionary(x => x.Text, x => (object)x.Keys) },
                            { "settingsButton", w.SettingsButton == null ? null : w.SettingsButton.ToolTip as string } });
                    });
                case "open":   // <window height> [submenu id]: opens the menu (and a submenu) and measures them
                    return Async(w, async () =>
                    {
                        double h; double.TryParse(p.Length > 0 ? p[0] : "0", out h);
                        if (h > 0) { w.Height = h; w.UpdateLayout(); }
                        Panels.Menu(w, w.MenuButton);
                        await Task.Delay(400);
                        var sv = Menu113.CurrentScroll; if (sv == null) return "error no menu";
                        string sub = null; double subExtent = 0, subViewport = 0;
                        if (p.Length > 1)
                        {
                            var panel = sv.Content as StackPanel;
                            var lbl = Label(w, p[1]); var row = panel.Children.OfType<Button>().FirstOrDefault(b => Text(b) == lbl);
                            if (row == null) return "error no submenu row " + p[1];
                            row.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            await Task.Delay(300);
                            sub = Menu113.OpenSub;
                            if (Menu113.SubScroll != null) { subExtent = Menu113.SubScroll.ExtentHeight; subViewport = Menu113.SubScroll.ViewportHeight; }
                        }
                        var content = sv.Content as StackPanel;
                        var settings = content.Children.OfType<Button>().FirstOrDefault(b => Text(b) == "Settings");
                        double settingsBottom = -1;
                        if (settings != null) { var pt = settings.TransformToAncestor(content).Transform(new Point(0, settings.ActualHeight)); settingsBottom = pt.Y; }
                        var res = new Dictionary<string, object> { { "windowHeight", w.ActualHeight }, { "maxHeight", sv.MaxHeight }, { "viewport", sv.ViewportHeight }, { "extent", sv.ExtentHeight },
                            { "scrollable", sv.ExtentHeight > sv.ViewportHeight + 0.5 }, { "settingsBottom", settingsBottom }, { "sub", sub }, { "subExtent", subExtent }, { "subViewport", subViewport },
                            { "rows", content.Children.OfType<Button>().Count() } };
                        if (settings != null) { settings.BringIntoView(); await Task.Delay(150); res["settingsVisibleAfterScroll"] = Visible(settings, sv); }
                        return "ok " + js.Serialize(res);
                    });
                case "close": return Program.OnUi(w, () => { Menu113.CloseSub(); Panels.CloseFlyouts(); return "ok"; });
                case "history-fill":   // <n>: n made-up pages over the last 60 days into this (test) profile's history
                    return Program.OnUi(w, () =>
                    {
                        int n; int.TryParse(arg, out n); var now = DateTime.Now;
                        var list = Enumerable.Range(0, n).Select(i => new HistoryEntry { Url = "https://site" + (i % 400) + ".example/page/" + i, Title = "Made-up page " + i + (i % 7 == 0 ? " - patch notes" : ""), Last = now.AddMinutes(-i * 17).ToString("s"), Visits = 1 + i % 5 }).ToList();
                        w.EnsureStoresNow(); var added = w.History.Merge(list);
                        return "ok " + added + " " + w.History.Count;
                    });
                case "history-open":   // the History page: build time, rows, days, rows drawn
                    return Async(w, async () =>
                    {
                        var sw = Stopwatch.StartNew();
                        w.ShowHistory(); await Task.Delay(50);
                        w.FillHistory(); var build = w.HistoryBuildMs; w.UpdateLayout(); var shown = sw.Elapsed.TotalMilliseconds;
                        await Task.Delay(300);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "buildMs", build }, { "shownMs", shown }, { "rows", w.HistoryRowCount }, { "days", w.HistoryDayCount }, { "realized", w.HistoryRealized },
                            { "first", w.HistoryRows.Take(3).Select(r => r.Day ? "#" + r.DayText : r.Time + " " + r.Title).ToList() } });
                    });
                case "history-search": return Program.OnUi(w, () => { var sw = Stopwatch.StartNew(); w.HistorySearchFor(arg); return "ok " + js.Serialize(new Dictionary<string, object> { { "rows", w.HistoryRowCount }, { "ms", sw.Elapsed.TotalMilliseconds } }); });
                case "history-remove": return Program.OnUi(w, () => "ok " + w.RemoveHistory(arg) + " " + w.History.Count);
                case "history-clear": return Program.OnUi(w, () => "ok " + w.ClearHistory(arg) + " " + w.History.Count);
                case "import-json":   // <file>: a Chromium "Bookmarks" file (made-up) placed like an import from "Brave"
                    return Async(w, async () =>
                    {
                        w.EnsureStoresNow();
                        var tree = Importer.ChromiumBookmarks(File.ReadAllText(arg));
                        var res = new ImportResult();
                        await Task.Run(() => Importer.Place(w.Bookmarks, tree, "Brave", res));
                        w.ShowImportResultForTest(new FoundProfile { Browser = "Brave", Profile = "Test", Kind = "chromium" }, res);
                        await Task.Delay(200);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "onBar", res.OnBar }, { "inOther", res.InOther }, { "barFolder", res.BarFolder != null }, { "showAt", res.ShowAt }, { "toast", w.LastToast }, { "actions", w.LastToastActions }, { "barOn", w.S.BookmarksBar } });
                    });
                case "toast-press": return Program.OnUi(w, () => "ok " + w.PressToastAction(arg) + " " + (w.Active != null ? w.Active.Url : "") + " " + w.BookmarksPageFolder);
                case "bm-tree":   // the bookmarks as a tree of titles (bar, other)
                    return Program.OnUi(w, () =>
                    {
                        w.EnsureStoresNow(); var b = w.Bookmarks;
                        Func<string, int, List<object>> walk = null;
                        walk = (id, d) => b.Children(id).Select(x => (object)(x.Folder ? (object)new Dictionary<string, object> { { "f", x.Title }, { "k", d > 6 ? new List<object>() : walk(x.Id, d + 1) } } : x.Title)).ToList();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "bar", walk(BookmarkStore.BarId, 0) }, { "other", walk(BookmarkStore.OtherId, 0) }, { "count", b.Count }, { "barOn", w.S.BookmarksBar }, { "repair", w.LastBarRepair } });
                    });
                case "progid":   // a made-up Windows answer for the default browser (never written anywhere), then the check a window does when it gets the focus
                    return Program.OnUi(w, () => { DefaultBrowser.ProgIdOverride = arg == "-" ? null : arg; var changed = w.DefaultCheck(); return "ok " + changed; });
            }
            return "error unknown 1.13 test command";
        }
        static string Text(Button b) { var g = b.Content as Grid; var t = g == null ? null : g.Children.OfType<TextBlock>().FirstOrDefault(); return t == null ? null : t.Text; }
        static string Label(BrowserWindow w, string id) { var it = Menu113.Model(w).FirstOrDefault(x => x.Id == id); return it == null ? id : it.Text; }
        static bool Visible(FrameworkElement e, ScrollViewer sv)
        {
            try { var r = e.TransformToAncestor(sv).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)); return r.Top >= -0.5 && r.Bottom <= sv.ViewportHeight + 0.5; } catch { return false; }
        }
    }

    public partial class BrowserWindow
    {
        public void EnsureStoresNow() { EnsureStores(); if (!History.Loaded) History.Load(); if (!Bookmarks.Loaded) Bookmarks.Load(); }
        public void ShowImportResultForTest(FoundProfile p, ImportResult r) { ShowImportResult(p, r); }

        // --snap: the History page with made-up pages
        public void SnapHistory()
        {
            var now = DateTime.Now.Date.AddHours(21).AddMinutes(14);
            var sites = new[] { new[] { "Patch notes - season 3", "https://news.hallow.example/patch-notes-s3" }, new[] { "Best loadouts this week", "https://loadouts.example/week" }, new[] { "hallowfriend - Twitch", "https://www.twitch.tv/hallowfriend" },
                new[] { "Graphics driver 610.12 release notes", "https://drivers.example/610-12" }, new[] { "Speedrun leaderboard", "https://speedrun.example/hallow" }, new[] { "Map pack download", "https://maps.example/pack-3" },
                new[] { "Tournament bracket", "https://bracket.example/finals" }, new[] { "Weekly deals", "https://store.example/deals" }, new[] { "Keyboard shortcuts cheat sheet", "https://wiki.example/shortcuts" } };
            SnapHistoryItems = new List<HistoryEntry>();
            for (int i = 0; i < 24; i++) { var s = sites[i % sites.Length]; SnapHistoryItems.Add(new HistoryEntry { Title = s[0], Url = s[1] + (i >= sites.Length ? "?p=" + i : ""), Last = now.AddMinutes(-i * 95).ToString("s"), Visits = 1 }); }
            var page = HistoryPage(); page.Tag = "mock"; content.Children.Add(page);
            var t = Tabs[0]; t.Url = Omni.HistoryUrl; t.Title = "History"; t.TitleBlock.Text = t.Title; SetTabIcon(t); Activate(t); Address.Text = Omni.HistoryUrl;
        }
    }
}
