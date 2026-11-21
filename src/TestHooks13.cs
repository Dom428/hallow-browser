// Hallow Browser 1.3 - developer test commands for moving in and syncing (only with HALLOW_BROWSER_TESTDIR, like the others):
// they call the same code the buttons call. Imports only ever see HALLOW_BROWSER_IMPORT_ROOT's fake profiles in tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string Root(string s) { return s == "bar" ? BookmarkStore.BarId : s == "other" ? BookmarkStore.OtherId : s; }
        static string MoveIn(BrowserWindow w, string cmd, string arg)
        {
            string rest; var p = arg.Split(new[] { ' ' }, 3);
            switch (cmd)
            {
                case "bm-state":
                    return Async(w, async () =>
                    {
                        var b = w.Bookmarks; if (b == null) return "ok {\"loaded\":false}";
                        if (!b.Loaded) await Task.Run(() => b.Load());
                        var tree = new List<string>(); Action<string, string> walk = null;
                        walk = (id, path) => { foreach (var x in b.Children(id)) { tree.Add(path + "|" + x.Title + "|" + (x.Url ?? "") + "|" + x.Id); if (x.Folder) walk(x.Id, path + "/" + x.Title); } };
                        walk(BookmarkStore.BarId, "bar"); walk(BookmarkStore.OtherId, "other");
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "loaded", b.Loaded }, { "count", b.Count }, { "barSetting", w.S.BookmarksBar }, { "barShown", w.BookmarksBarShown }, { "barItems", w.BarItemCount }, { "tree", tree } });
                    });
                case "bm-add": return Program.OnUi(w, () => { var x = w.Bookmarks.Add(Root(p[0]), p.Length > 2 ? p[2] : null, p[1]); return x == null ? "error" : "ok " + x.Id; });
                case "bm-folder": { var parent = Split(arg, out rest); return Program.OnUi(w, () => "ok " + w.Bookmarks.AddFolder(Root(parent), rest).Id); }
                case "bm-move": return Program.OnUi(w, () => "ok " + w.Bookmarks.Move(p[0], Root(p[1]), int.Parse(p[2])));
                case "bm-rename": { var id = Split(arg, out rest); return Program.OnUi(w, () => "ok " + w.Bookmarks.Rename(id, rest)); }
                case "bm-delete": return Program.OnUi(w, () => "ok " + w.Bookmarks.Delete(arg));
                case "bm-bar": return Program.OnUi(w, () => { if ((arg == "on") != w.S.BookmarksBar) w.ToggleBookmarksBar(); return "ok " + w.BookmarksBarShown; });
                case "bm-export": return Program.OnUi(w, () => "ok " + w.ExportBookmarksTo(arg));
                case "bm-import": return Program.OnUi(w, () => "ok " + w.ImportBookmarksFrom(arg));
                case "bm-click": return Program.OnUi(w, () => { var b = w.Bookmarks.Find(arg); if (b == null) return "error"; w.OpenBookmark(b, true); return "ok"; });
                case "import-find":
                {
                    var found = OnUiT(w, () => w.FindBrowsers());
                    foreach (var f in found) Importer.Count(f);
                    lastFound = found;
                    return "ok " + js.Serialize(found.Select(f => new Dictionary<string, object> { { "label", f.Label }, { "bookmarks", f.Bookmarks }, { "history", f.History }, { "note", f.Note } }).ToList());
                }
                case "import-run":
                {
                    int i = int.Parse(p[0]); if (lastFound == null || i >= lastFound.Count) return "error find first";
                    var prof = lastFound[i];
                    var stores = OnUiT(w, () => { w.EnsureStoresPublic(); return Tuple.Create(w.Bookmarks, w.History); });
                    if (!stores.Item1.Loaded) stores.Item1.Load(); if (!stores.Item2.Loaded) stores.Item2.Load();
                    var r = Importer.Run(prof, p.Length > 1 && p[1] == "1", p.Length > 2 && p[2] == "1", stores.Item1, stores.Item2);
                    return "ok " + js.Serialize(new Dictionary<string, object> { { "bookmarks", r.Bookmarks }, { "skipped", r.SkippedBookmarks }, { "history", r.History }, { "notes", r.Notes } });
                }
                case "nt-data": return Program.OnUi(w, () => "ok " + w.NewTabJson());
                case "nt-refresh": return Program.OnUi(w, () => { w.RefreshNewTabData(); return "ok"; });
                case "settings": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "pinned", w.S.NewTabPinned.Select(x => x.Url).ToList() }, { "hidden", w.S.NewTabHidden.ToList() }, { "bar", w.S.BookmarksBar }, { "search", w.S.Search }, { "syncNote", w.S.SyncNote } }));
                case "sync-set":
                    return Program.OnUi(w, () =>
                    {
                        if (p[0] == "off") { w.S.SyncOn = false; w.S.Save(); w.StartSync(); return "ok"; }
                        w.S.SyncFolder = p[0]; w.S.SyncOn = true; w.S.SyncEncrypt = p.Length > 1;
                        SyncCrypto.SavePassword(p.Length > 1 ? arg.Substring(p[0].Length + 1) : null);
                        w.S.Save(); w.StartSync(); return "ok";
                    });
                case "sync-now":
                    return Async(w, async () =>
                    {
                        for (int i = 0; i < 100 && w.SyncBusy; i++) await Task.Delay(100);
                        var o = await w.SyncNow(false);
                        if (o == null) return "error not run";
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "state", o.State }, { "message", o.Message }, { "wrote", o.Wrote }, { "encrypted", o.Encrypted }, { "incoming", o.Incoming }, { "conflicts", o.Conflicts }, { "kinds", o.ChangedKinds.ToList() } });
                    });
            }
            return "error unknown move-in test command";
        }
        static List<FoundProfile> lastFound;
        static T OnUiT<T>(BrowserWindow w, Func<T> f) { return w.Dispatcher.Invoke(f); }
    }
}
