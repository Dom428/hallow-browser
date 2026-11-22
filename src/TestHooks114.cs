// Hallow Browser 1.14 - developer test commands for site logos (only with HALLOW_BROWSER_TESTDIR, like the others): the cache, what each
// surface shows (favicon / brand / letter), the one-time import from a FAKE browser profile (HALLOW_BROWSER_IMPORT_ROOT - never the
// user's real Brave) and the quiet fetch against local test sites. Test profiles only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V114(BrowserWindow w, string cmd, string arg)
        {
            switch (cmd)
            {
                case "status":   // <url>: what the site shows and whether its own icon is cached
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "pick", SiteIcons.Pick(arg) }, { "exact", SiteIcons.HasExact(arg) }, { "count", SiteIcons.Count() }, { "writes", SiteIcons.Writes }, { "dir", SiteIcons.Dir } }));
                case "wins":   // every window: private?, the active tab's address and whether it has an icon
                    return Program.OnUi(w, () => "ok " + js.Serialize(Program.Windows.Select(x => new Dictionary<string, object> { { "private", x.Private }, { "url", x.Active != null ? x.Active.Url : null }, { "favicon", x.Active != null && x.Active.Favicon != null } }).ToList()));
                case "bar": return Program.OnUi(w, () => { w.RenderBookmarksBar(); return "ok " + js.Serialize(w.BarIconKinds()); });
                case "manager": return Program.OnUi(w, () => { w.ShowBookmarksAt(BookmarkStore.BarId); return "ok " + js.Serialize(w.ManagerIconKinds()); });
                case "history":
                    return Async(w, async () =>
                    {
                        w.ShowHistory(); await Task.Delay(300); w.FillHistory();
                        return "ok " + js.Serialize(w.HistoryRows.Where(r => !r.Day).Take(20).Select(r => new Dictionary<string, object> { { "url", r.Url }, { "kind", Kind(r.IconView as FrameworkElement) } }).ToList());
                    });
                case "palette":   // <query>: Ctrl+K's rows - which show a site logo
                    return Program.OnUi(w, () => "ok " + js.Serialize(Palette.Search(w, arg, 12).Select(x => new Dictionary<string, object> { { "kind", x.Kind }, { "url", x.Url }, { "pick", x.Url != null ? SiteIcons.Pick(x.Url) : null } }).ToList()));
                case "candidates": return Program.OnUi(w, () => "ok " + js.Serialize(w.LogoCandidates(20)));
                case "fetch":   // <origin>: the quiet fetch for one site, now
                    return Async(w, async () => { var got = await w.FetchLogos(new List<string> { arg }); return "ok " + js.Serialize(new Dictionary<string, object> { { "got", got }, { "last", BrowserWindow.LastLogoFetch } }); });
                case "tick":   // the fetcher's own step (candidates, game check), not waiting for idle
                    return Async(w, async () => { var got = await w.LogoFetchTick(true); return "ok " + js.Serialize(new Dictionary<string, object> { { "got", got }, { "allowed", got != null } }); });
                case "once":   // 0|1: the one-time import (1 = the Settings button)
                    return Async(w, async () =>
                    {
                        w.EnsureStoresNow();
                        var r = await w.LogosOnce(arg == "1"); await Task.Delay(300);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "ran", r != null }, { "saved", r == null ? 0 : r.Saved }, { "from", r == null ? null : r.From }, { "skipped", r == null ? null : r.Skipped }, { "inUse", r != null && r.InUse }, { "toast", w.LastToast } });
                    });
                case "state":
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "logos114", w.S.Logos114 }, { "tries", w.S.Logos114Tries }, { "fetchLogos", w.S.FetchLogos }, { "lastImport", BrowserWindow.LastLogoImport }, { "lastFetch", BrowserWindow.LastLogoFetch }, { "fetchTries", BrowserWindow.LogoFetchTries }, { "count", SiteIcons.Count() } }));
            }
            return "error unknown 1.14 test command";
        }
        static string Kind(FrameworkElement e) { return e == null ? null : e.Tag as string; }
    }

    public partial class BrowserWindow
    {
        // the bookmarks bar: each bookmark's title and what its picture is (favicon / brand / letter / folder)
        public List<Dictionary<string, object>> BarIconKinds()
        {
            var res = new List<Dictionary<string, object>>(); if (bmItems == null) return res;
            foreach (var btn in bmItems.Children.OfType<Button>().Where(x => x.Tag is Bookmark))
            {
                var b = (Bookmark)btn.Tag; var bd = btn.Content as Border; var row = bd == null ? null : bd.Child as StackPanel;
                var pic = row == null ? null : row.Children.OfType<FrameworkElement>().FirstOrDefault();
                res.Add(new Dictionary<string, object> { { "title", b.Title }, { "url", b.Url }, { "kind", b.Folder ? "folder" : pic == null ? null : pic.Tag as string } });
            }
            return res;
        }
        public List<Dictionary<string, object>> ManagerIconKinds()
        {
            var res = new List<Dictionary<string, object>>(); if (bmList == null) return res;
            foreach (var g in bmList.Children.OfType<Grid>().Where(x => x.Tag is Bookmark))
            {
                var b = (Bookmark)g.Tag; var pic = g.Children.OfType<FrameworkElement>().FirstOrDefault();
                res.Add(new Dictionary<string, object> { { "title", b.Title }, { "kind", b.Folder ? "folder" : pic == null ? null : pic.Tag as string } });
            }
            return res;
        }
    }
}
