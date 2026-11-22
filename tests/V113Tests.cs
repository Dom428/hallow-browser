// Hallow Browser 1.13 - unit tests: importing puts bookmarks where the other browser had them (fixtures in Brave's "Bookmarks" JSON
// shape with bar / other / synced roots - made-up data), the one-time bar repair (idempotent, a backup first, nothing lost), the
// History page's rows (grouped by day, search, ranges, remove) and the menu's list of 1.12 items. Temp folders only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HallowBrowser.Tests
{
    public static class V113
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All() { ImportTests(); RepairTests(); HistoryTests(); MenuTests(); }

        static string Url(string name, string url, int i) { return "{\"type\":\"url\",\"name\":\"" + name + "\",\"url\":\"" + url + "\",\"date_added\":\"" + (13370000000000000L + i * 1000000L) + "\",\"guid\":\"g" + i + "\",\"id\":\"" + i + "\"}"; }
        static string Folder(string name, IEnumerable<string> kids) { return "{\"type\":\"folder\",\"name\":\"" + name + "\",\"children\":[" + string.Join(",", kids) + "],\"date_added\":\"13370000000000000\"}"; }
        // Brave's Bookmarks file shape (made-up bookmarks): the bar (with a folder), other bookmarks and mobile (synced)
        public static string BraveJson(int bar)
        {
            var barKids = Enumerable.Range(0, bar).Select(i => Url("Bar site " + i, "https://bar" + i + ".example/", i)).ToList();
            barKids.Add(Folder("Games", new[] { Url("Game news", "https://games.example/news", 100), Url("Patch notes", "https://games.example/patches", 101) }));
            return "{\"checksum\":\"0\",\"roots\":{\"bookmark_bar\":" + Folder("Bookmarks bar", barKids) + ",\"other\":" + Folder("Other bookmarks", new[] { Url("Other one", "https://other.example/1", 200), Url("Other two", "https://other.example/2", 201) }) +
                ",\"synced\":" + Folder("Mobile bookmarks", new[] { Url("Phone page", "https://phone.example/", 300) }) + "},\"version\":1}";
        }
        static string Temp() { var d = Path.Combine(Path.GetTempPath(), "hb113-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(d); return d; }
        static List<string> Titles(BookmarkStore s, string parent) { return s.Children(parent).Select(x => (x.Folder ? "[" + x.Title + "]" : x.Title)).ToList(); }

        static void ImportTests()
        {
            var d = Temp();
            try
            {
                // an empty Hallow: Brave's bar straight onto the bar, other -> Other bookmarks, mobile -> a folder in Other bookmarks
                var s = new BookmarkStore(Path.Combine(d, "a.json")); s.Load();
                var tree = Importer.ChromiumBookmarks(BraveJson(17));
                var r = new ImportResult(); Importer.Place(s, tree, "Brave", r);
                var bar = Titles(s, BookmarkStore.BarId);
                Ok(bar.Count == 18 && bar[0] == "Bar site 0" && bar[16] == "Bar site 16" && bar[17] == "[Games]", "import 1.13: Brave's bookmarks bar goes straight onto an empty Hallow bar, in order (" + bar.Count + ")");
                Eq(string.Join("|", Titles(s, BookmarkStore.OtherId)), "Other one|Other two|[Mobile bookmarks]", "import 1.13: Brave's other bookmarks go to Other bookmarks, mobile ones into a folder there");
                Ok(r.OnBar == 19 && r.InOther == 3 && r.Bookmarks == 22 && r.BarFolder == null && r.ShowAt == BookmarkStore.BarId, "import 1.13: the result says where they went (19 on the bar, 3 in Other bookmarks)");
                Ok(!Titles(s, BookmarkStore.OtherId).Any(t => t.StartsWith("[Imported from")), "import 1.13: no \"Imported from ...\" folder any more");
                // again: nothing twice
                var r2 = new ImportResult(); Importer.Place(s, Importer.ChromiumBookmarks(BraveJson(17)), "Brave", r2);
                Ok(r2.Bookmarks == 0 && r2.SkippedBookmarks == 22 && s.Count == 22 && Titles(s, BookmarkStore.BarId).Count == 18, "import 1.13: importing again adds nothing (22 skipped)");

                // a Hallow bar with bookmarks already: Brave's bar goes into one folder on the bar, "From Brave"
                var s2 = new BookmarkStore(Path.Combine(d, "b.json")); s2.Load();
                s2.Add(BookmarkStore.BarId, "Mine", "https://mine.example/");
                var r3 = new ImportResult(); Importer.Place(s2, Importer.ChromiumBookmarks(BraveJson(3)), "Brave", r3);
                var bar2 = Titles(s2, BookmarkStore.BarId);
                Ok(bar2.Count == 2 && bar2[0] == "Mine" && bar2[1] == "[From Brave]", "import 1.13: with bookmarks on the bar already, Brave's bar becomes one \"From Brave\" folder on it");
                Ok(r3.BarFolder != null && r3.ShowAt == r3.BarFolder && Titles(s2, r3.BarFolder).Count == 4, "import 1.13: ... and the result points at that folder");
                // only other bookmarks (no bar items): Other bookmarks, the bar untouched
                var s3 = new BookmarkStore(Path.Combine(d, "c.json")); s3.Load();
                var noBar = "{\"roots\":{\"bookmark_bar\":" + Folder("Bookmarks bar", new string[0]) + ",\"other\":" + Folder("Other bookmarks", new[] { Url("Only other", "https://only.example/", 1) }) + "}}";
                var r4 = new ImportResult(); Importer.Place(s3, Importer.ChromiumBookmarks(noBar), "Brave", r4);
                Ok(r4.OnBar == 0 && r4.InOther == 1 && Titles(s3, BookmarkStore.BarId).Count == 0 && r4.ShowAt == BookmarkStore.OtherId, "import 1.13: only other bookmarks -> Other bookmarks; nothing on the bar (so the bar isn't switched on)");
                // Firefox's toolbar folder counts as the bar too
                Ok(Importer.IsOtherRoot("Other Bookmarks") && Importer.IsOtherRoot("Other bookmarks") && !Importer.IsOtherRoot("Bookmarks Menu"), "import 1.13: Firefox's Other Bookmarks go to Other bookmarks; its menu folder stays a folder");
            }
            finally { try { Directory.Delete(d, true); } catch { } }
        }

        // the store as 1.3 - 1.12 left it after an import from Brave: Other > "Imported from Brave (Personal)" > "Bookmarks bar" > 17 links
        static BookmarkStore OldImport(string file)
        {
            var s = new BookmarkStore(file); s.Load();
            var imp = s.AddFolder(BookmarkStore.OtherId, "Imported from Brave (Personal)");
            var bar = s.AddFolder(imp.Id, "Bookmarks bar");
            for (int i = 0; i < 16; i++) s.Add(bar.Id, "Old bar " + i, "https://old" + i + ".example/");
            var sub = s.AddFolder(bar.Id, "Clips"); s.Add(sub.Id, "Clip page", "https://clips.example/1");
            s.Add(BookmarkStore.OtherId, "Kept elsewhere", "https://kept.example/");
            return s;
        }
        static void RepairTests()
        {
            var d = Temp();
            try
            {
                var f = Path.Combine(d, "bookmarks.json");
                var s = OldImport(f); s.Save();
                var before = s.Count; var urls = s.All().Select(x => x.Url).OrderBy(x => x).ToList();
                Bookmark imp; Ok(BarRepair.Candidate(s, out imp) != null && imp.Title == "Imported from Brave (Personal)", "bar repair 1.13: finds the imported \"Bookmarks bar\" folder three levels down");
                var r = BarRepair.Run(s, f);
                var bar = Titles(s, BookmarkStore.BarId);
                Ok(r.Moved == 17 && bar.Count == 17 && bar[0] == "Old bar 0" && bar[15] == "Old bar 15" && bar[16] == "[Clips]", "bar repair 1.13: its 17 items move onto the bar in the same order (" + r.Moved + ")");
                Ok(s.Count == before && s.All().Select(x => x.Url).OrderBy(x => x).SequenceEqual(urls), "bar repair 1.13: no bookmark is lost or added (" + s.Count + ")");
                Eq(string.Join("|", Titles(s, BookmarkStore.OtherId)), "Kept elsewhere", "bar repair 1.13: the folders left empty are removed; other bookmarks stay");
                Ok(r.Backup != null && File.Exists(r.Backup) && File.ReadAllText(r.Backup).Contains("Imported from Brave (Personal)"), "bar repair 1.13: bookmarks.json was copied first (bookmarks.before-1.13.json, the old layout)");
                var stamp = File.GetLastWriteTimeUtc(r.Backup);
                var again = BarRepair.Run(s, f);
                Ok(again.Moved == 0 && again.Skipped != null && s.Count == before && Titles(s, BookmarkStore.BarId).Count == 17 && File.GetLastWriteTimeUtc(r.Backup) == stamp, "bar repair 1.13: idempotent - a second run changes nothing (" + again.Skipped + ") and keeps the first backup");
                // reloaded from disk: the same
                var s2 = new BookmarkStore(f); s2.Load();
                Ok(Titles(s2, BookmarkStore.BarId).Count == 17 && s2.Count == before, "bar repair 1.13: saved (read back from the file)");
                // a bar with bookmarks already: never touched
                var f3 = Path.Combine(d, "b3", "bookmarks.json"); Directory.CreateDirectory(Path.GetDirectoryName(f3));
                var s3 = OldImport(f3); s3.Add(BookmarkStore.BarId, "Mine", "https://mine.example/"); s3.Save();
                var r3 = BarRepair.Run(s3, f3);
                Ok(r3.Moved == 0 && Titles(s3, BookmarkStore.BarId).Count == 1 && !File.Exists(BarRepair.BackupPath(f3)), "bar repair 1.13: a bar that has bookmarks is left alone (no backup made, nothing moved)");
                // nothing imported: nothing to do
                var s4 = new BookmarkStore(Path.Combine(d, "b4.json")); s4.Load(); s4.Add(BookmarkStore.OtherId, "x", "https://x.example/");
                Ok(BarRepair.Run(s4, Path.Combine(d, "b4.json")).Moved == 0, "bar repair 1.13: no import folder - nothing to do");
            }
            finally { try { Directory.Delete(d, true); } catch { } }
        }

        static void HistoryTests()
        {
            var now = new DateTime(2026, 11, 22, 15, 0, 0);
            var list = new List<HistoryEntry> {
                new HistoryEntry { Url = "https://a.example/today", Title = "Today page", Last = now.AddMinutes(-5).ToString("s") },
                new HistoryEntry { Url = "https://b.example/yesterday", Title = "Yesterday page", Last = now.AddDays(-1).ToString("s") },
                new HistoryEntry { Url = "https://c.example/imported", Title = "Imported in March", Last = new DateTime(2026, 3, 4, 9, 30, 0).ToString("s") },
                new HistoryEntry { Url = "https://d.example/nodate", Title = "No date" } };
            var rows = HistoryPageRules.Rows(list, "", now);
            Eq(string.Join("|", rows.Select(r => r.Day ? "#" + r.DayText.Split(' ')[0] : r.Title)), "#Today|Today page|#Yesterday|Yesterday page|#Wednesday,|Imported in March|#Earlier|No date".Replace("Wednesday,", "Wednesday"), "history 1.13: newest first, a header per day (imported pages under their own date), undated last");
            Ok(rows.First(r => r.Title == "Imported in March").Time == "09:30" && rows[0].DayText.Contains("2026"), "history 1.13: times and full dates");
            Eq(HistoryPageRules.Rows(list, "march", now).Count(r => !r.Day), 1, "history 1.13: search in titles");
            Eq(HistoryPageRules.Initial("www.twitch.tv") + HistoryPageRules.Initial("wiki.example") + HistoryPageRules.Initial(""), "TW?", "history 1.13: the site letter (www. left out)");
            Eq(HistoryPageRules.Rows(list, "b.example", now).Count(r => !r.Day), 1, "history 1.13: ... and addresses");
            Ok(HistoryPageRules.Since("hour", now) == now.AddHours(-1) && HistoryPageRules.Since("4weeks", now) == now.AddDays(-28) && HistoryPageRules.Since("all", now) == null, "history 1.13: the clear ranges");
            var d = Temp();
            try
            {
                var h = new HistoryStore(Path.Combine(d, "h.json")); h.Load();
                h.Merge(list);
                Ok(h.Remove("https://b.example/yesterday") && h.Count == 3 && !h.All().Any(x => x.Url.Contains("yesterday")), "history 1.13: one page removed");
                var n = h.RemoveSince(DateTime.Now.AddYears(-50));
                Ok(n == 3 && h.Count == 0, "history 1.13: clear a range removes the pages visited in it (" + n + "; a page with no date got the import's time)");
                var big = Enumerable.Range(0, 5000).Select(i => new HistoryEntry { Url = "https://s" + i + ".example/", Title = "Page " + i, Last = now.AddMinutes(-i * 13).ToString("s") }).ToList();
                var sw = System.Diagnostics.Stopwatch.StartNew(); var br = HistoryPageRules.Rows(big, "", now); var ms = sw.Elapsed.TotalMilliseconds;
                Ok(br.Count(r => !r.Day) == 5000 && br.Count(r => r.Day) >= 40 && ms < 400, "history 1.13: 5,000 pages grouped in " + ms.ToString("0") + " ms (" + br.Count(r => r.Day) + " days)");
            }
            finally { try { Directory.Delete(d, true); } catch { } }
        }

        static void MenuTests()
        {
            Ok(Menu113.OldLabels.Length >= 35 && Menu113.Missing(Menu113.OldLabels.SelectMany(x => x.Split('|'))).Count == 0, "menu 1.13: the list of the 1.12 menu's items is complete and checkable");
            Ok(Menu113.Missing(new[] { "New tab" }).Count == Menu113.OldLabels.Length - 1, "menu 1.13: a missing item is reported");
            Ok(Omni.IsInternal(Omni.HistoryUrl) && Omni.Interpret("hallow://history", "duckduckgo") == Omni.HistoryUrl, "history 1.13: hallow://history is a page of the browser");
        }
    }
}
