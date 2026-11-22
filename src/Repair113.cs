// Hallow Browser 1.13 - two fixes from real use:
//  1. A one-time repair of an import made with 1.3 - 1.12: those put another browser's bookmarks bar into Other bookmarks > "Imported
//     from <browser>" > "Bookmarks bar", and with Hallow's own bar empty (and switched off) nothing showed - "I see nothing". When
//     Hallow's bar is empty and such a folder exists, its bookmarks move onto the bar once (same order), the folders left empty are
//     removed and the bar is switched on. bookmarks.json is copied first (bookmarks.before-1.13.json); a bookmark is never deleted.
//  2. The new tab page's "make Hallow your default browser" bar now goes away as soon as Hallow is the default: Windows' choice
//     (UserChoiceLatest, then UserChoice) is read again whenever a browser window gets the focus back (e.g. after Windows Settings)
//     and every minute, and the new tab pages are told; the page also reads its data again whenever it is shown.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        [DataMember] public bool BarRepair113;   // the one-time bookmarks bar repair ran (or had nothing to do)
    }

    public static class BarRepair
    {
        public class Result { public int Moved; public string From, Backup, Skipped; }
        public static string BackupPath(string file) { return Path.Combine(Path.GetDirectoryName(file) ?? "", "bookmarks.before-1.13.json"); }

        // the folder whose bookmarks belong on the bar: Other bookmarks > "Imported from ..." > "Bookmarks bar" (not empty)
        public static Bookmark Candidate(BookmarkStore store, out Bookmark import)
        {
            import = null;
            foreach (var f in store.Children(BookmarkStore.OtherId).Where(x => x.Folder && (x.Title ?? "").StartsWith("Imported from ", StringComparison.OrdinalIgnoreCase)))
            {
                var bar = store.Children(f.Id).FirstOrDefault(x => x.Folder && string.Equals(x.Title, "Bookmarks bar", StringComparison.OrdinalIgnoreCase) && store.Children(x.Id).Count > 0);
                if (bar != null) { import = f; return bar; }
            }
            return null;
        }

        // idempotent: once the bar has anything on it, nothing happens
        public static Result Run(BookmarkStore store, string file)
        {
            var r = new Result();
            if (!store.Loaded) store.Load();
            if (store.Children(BookmarkStore.BarId).Count > 0) { r.Skipped = "the bookmarks bar already has bookmarks"; return r; }
            Bookmark import; var src = Candidate(store, out import);
            if (src == null) { r.Skipped = "no imported bookmarks bar"; return r; }
            // a copy of the file first - no copy, no change
            try
            {
                var bk = BackupPath(file);
                if (File.Exists(file) && !File.Exists(bk)) File.Copy(file, bk);
                if (File.Exists(file) && !File.Exists(bk)) { r.Skipped = "couldn't back up bookmarks.json"; return r; }
                r.Backup = File.Exists(bk) ? bk : null;
            }
            catch (Exception e) { r.Skipped = "couldn't back up bookmarks.json: " + e.Message; return r; }
            var before = store.Count;
            foreach (var k in store.Children(src.Id)) if (store.Move(k.Id, BookmarkStore.BarId, -1)) r.Moved++;
            if (store.Count != before) throw new InvalidOperationException("bar repair changed the number of bookmarks");   // never happens: Move only moves
            if (store.Children(src.Id).Count == 0) store.Delete(src.Id);
            if (import != null && store.Children(import.Id).Count == 0) store.Delete(import.Id);
            r.From = import != null ? import.Title : src.Title;
            return r;
        }
    }

    public partial class BrowserWindow
    {
        static bool barRepairDone;
        public string LastBarRepair;
        // after the stores load (first window of the profile): the repair, once
        void RepairBarOnce()
        {
            if (snap || barRepairDone || S.BarRepair113 || Bookmarks == null || Private) return;
            barRepairDone = true;
            BarRepair.Result r;
            try { r = BarRepair.Run(Bookmarks, BookmarkStore.DefaultPath); }
            catch (Exception e) { LastBarRepair = "error: " + e.Message; return; }   // left to try again next start
            if (r.Skipped != null && r.Skipped.StartsWith("couldn't")) { LastBarRepair = r.Skipped; return; }
            S.BarRepair113 = true; S.Save();
            LastBarRepair = r.Moved > 0 ? "moved " + r.Moved + " from " + r.From : r.Skipped;
            if (r.Moved > 0)
            {
                SetBookmarksBar(true);
                ToastWith(r.Moved + (r.Moved == 1 ? " bookmark" : " bookmarks") + " from your " + (r.From ?? "import").Replace("Imported from ", "") + " bookmarks bar are on Hallow's bookmarks bar now (they were three folders deep in Other bookmarks). A copy of the old list is kept.", 14,
                    Tuple.Create("Show bookmarks", "bookmark", (Action)(() => ShowBookmarksAt(BookmarkStore.BarId))));
            }
        }

        // ---- the default-browser hint follows Windows' choice
        bool? lastIsDefault;
        public bool DefaultCheck()
        {
            bool now; try { now = DefaultBrowser.Status().IsHallow; } catch { return false; }
            if (lastIsDefault == now) return false;
            bool first = lastIsDefault == null; lastIsDefault = now;
            if (!first) DefaultChanged();
            return !first;
        }
        public int DefaultChecks;   // tests
        void HookDefaultCheck()
        {
            Activated += (a, b) => { if (!snap) { DefaultChecks++; DefaultCheck(); } };
        }
    }
}
