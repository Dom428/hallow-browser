// Hallow Browser 1.3 - unit tests: bookmarks with folders (order, moves, deletes, 1.2's file), the Netscape HTML round trip, the
// SQLite reader on real-format files written by TinySqlite, LZ4 / mozLz4, importing from FAKE browser profiles in a temp folder
// (Chromium Bookmarks JSON + History, Firefox jsonlz4 + places.sqlite; dedupe; the profiles are never changed), sync between two
// test profiles through a temp folder (conflicts, edit vs delete, cloud conflict copies, encryption, per-PC settings), and the
// new tab page's data. Never touches real browser profiles.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class V13
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }
        static string root;
        static string Tmp(string name) { var d = Path.Combine(root, Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(d); return Path.Combine(d, name); }

        public static void All()
        {
            root = Path.Combine(Path.GetTempPath(), "HallowBrowser13-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
            try { BookmarkTests(); HtmlTests(); SqliteTests(); Lz4Tests(); ImportTests(); SyncTests(); NewTabTests(); SettingsTests(); LightContrastTests(); }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        // the tree as "path|title|url" lines, in order
        static List<string> Tree(BookmarkStore s)
        {
            var res = new List<string>();
            Action<string, string> walk = null;
            walk = (id, path) => { foreach (var b in s.Children(id)) { res.Add(path + "|" + b.Title + "|" + (b.Url ?? "")); if (b.Folder) walk(b.Id, path + "/" + b.Title); } };
            walk(BookmarkStore.BarId, "bar"); walk(BookmarkStore.OtherId, "other");
            return res;
        }

        static void BookmarkTests()
        {
            var f = Tmp("bookmarks.json");
            var s = new BookmarkStore(f); s.Load();
            var a = s.Add(BookmarkStore.BarId, "Alpha", "https://a.example/");
            var b = s.Add(BookmarkStore.BarId, "Bravo", "https://b.example/");
            var games = s.AddFolder(BookmarkStore.BarId, "Games");
            var c = s.Add(games.Id, "Charlie", "https://c.example/");
            Eq(string.Join(",", s.Children(BookmarkStore.BarId).Select(x => x.Title)), "Alpha,Bravo,Games", "bookmarks: added in order to the bar");
            Ok(s.Move(b.Id, BookmarkStore.BarId, 0), "bookmarks: move to the front");
            Eq(string.Join(",", s.Children(BookmarkStore.BarId).Select(x => x.Title)), "Bravo,Alpha,Games", "bookmarks: reordered");
            s.Move(b.Id, BookmarkStore.BarId, 2);
            Eq(string.Join(",", s.Children(BookmarkStore.BarId).Select(x => x.Title)), "Alpha,Games,Bravo", "bookmarks: moved to the end (index past the others)");
            s.Move(a.Id, BookmarkStore.BarId, 1);
            Eq(string.Join(",", s.Children(BookmarkStore.BarId).Select(x => x.Title)), "Games,Alpha,Bravo", "bookmarks: moved between two");
            var sub = s.AddFolder(games.Id, "Shooters");
            Ok(!s.Move(games.Id, sub.Id, 0) && !s.Move(games.Id, games.Id, 0), "bookmarks: a folder can't go inside itself");
            Ok(s.Move(a.Id, sub.Id, -1) && s.PathOf(sub.Id) == "Bookmarks bar / Games / Shooters", "bookmarks: into a nested folder, path shown");
            Ok(s.Rename(games.Id, "  My games ") && s.Find(games.Id).Title == "My games", "bookmarks: rename (trimmed)");
            Ok(s.SetUrl(c.Id, "https://c2.example/") && s.Has("https://c2.example/") && !s.SetUrl(c.Id, "javascript:alert(1)"), "bookmarks: edit address (web only)");
            // many moves into the same gap: positions get renumbered, order stays right
            var many = Enumerable.Range(0, 70).Select(i => s.Add(BookmarkStore.OtherId, "m" + i, "https://m" + i + ".example/")).ToList();
            for (int i = 0; i < 60; i++) s.Move(many[69 - i].Id, BookmarkStore.OtherId, 1);
            var order = s.Children(BookmarkStore.OtherId).Select(x => x.Title).ToList();
            Ok(order[0] == "m0" && order.Count == 70 && order.Distinct().Count() == 70, "bookmarks: 60 moves into the same gap keep a clean order");
            var s2 = new BookmarkStore(f); s2.Load();
            Ok(Tree(s2).SequenceEqual(Tree(s)), "bookmarks: folders, order and titles saved");
            Eq(s.Delete(games.Id), 4, "bookmarks: deleting a folder takes what's in it");
            Ok(!s.Has("https://a.example/") && !s.Has("https://c2.example/"), "bookmarks: ...its bookmarks too");
            Ok(!s.Toggle("https://b.example/", "x") && !s.Has("https://b.example/") && s.Toggle("https://b.example/", "Bravo") && s.Children(BookmarkStore.BarId).Last().Title == "Bravo", "bookmarks: Ctrl+D removes, adds to the end of the bar");
            // an orphan (its folder deleted on another PC) shows in Other bookmarks
            s.Replace(s.Everything().Concat(new[] { new Bookmark { Id = "orph", Parent = "gone-folder", Url = "https://orphan.example/", Title = "Orphan" } }));
            Ok(s.Children(BookmarkStore.OtherId).Any(x => x.Id == "orph") && s.All().Any(x => x.Id == "orph"), "bookmarks: orphan shows under Other bookmarks");
            // 1.2's flat file
            var old = Tmp("bookmarks.json");
            File.WriteAllText(old, "{\"Items\":[{\"Url\":\"https://new.example/\",\"Title\":\"Newest\",\"Added\":\"2026-11-02T10:00:00\"},{\"Url\":\"https://old.example/\",\"Title\":\"Oldest\",\"Added\":\"2026-11-01T10:00:00\"},{\"Url\":\"ftp://x\",\"Title\":\"no\"}]}");
            var m = new BookmarkStore(old); m.Load();
            Eq(string.Join(",", m.Children(BookmarkStore.BarId).Select(x => x.Title)), "Newest,Oldest", "bookmarks: 1.2's list loads into the bar in its order");
            Ok(m.Everything().All(x => !string.IsNullOrEmpty(x.Id)), "bookmarks: 1.2 entries get ids");
        }

        static void HtmlTests()
        {
            var s = new BookmarkStore(Tmp("b.json")); s.Load();
            s.Add(BookmarkStore.BarId, "Hallow <News> & \"more\"", "https://news.example/?a=1&b=2");
            var f = s.AddFolder(BookmarkStore.BarId, "Games");
            s.Add(f.Id, "Delta", "https://delta.example/");
            var g = s.AddFolder(f.Id, "Deep");
            s.Add(g.Id, "Münster", "https://xn--mnster-3ya.example/");
            s.AddFolder(BookmarkStore.BarId, "Empty");
            s.Add(BookmarkStore.OtherId, "Other one", "https://other.example/");
            var html = s.ExportHtml();
            Ok(html.StartsWith("<!DOCTYPE NETSCAPE-Bookmark-file-1>") && html.Contains("PERSONAL_TOOLBAR_FOLDER=\"true\""), "HTML export: Netscape format with the toolbar folder");
            Ok(html.Contains("&lt;News&gt; &amp; &quot;more&quot;") && html.Contains("a=1&amp;b=2"), "HTML export: titles and addresses escaped");
            var t = new BookmarkStore(Tmp("t.json")); t.Load();
            var n = t.ImportHtml(html, "Imported from file");
            Eq(n, 4, "HTML round trip: every bookmark back");
            var want = Tree(s).Where(x => !x.StartsWith("bar|Empty|")).ToList();   // an empty folder isn't made on import
            Ok(Tree(t).SequenceEqual(want), "HTML round trip: same folders, order, titles and addresses" + (Tree(t).SequenceEqual(want) ? "" : " (" + string.Join(" ; ", Tree(t)) + ")"));
            Eq(t.ImportHtml(html, "Imported from file"), 0, "HTML import again: nothing twice");
            // a typical Chrome / Firefox export into a store that has bookmarks: its own folder, duplicates skipped
            var chrome = "<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE>\n<H1>Bookmarks</H1>\n<DL><p>\n" +
                "    <DT><H3 ADD_DATE=\"1700000000\" LAST_MODIFIED=\"1700000001\" PERSONAL_TOOLBAR_FOLDER=\"true\">Bookmarks bar</H3>\n    <DL><p>\n        <DT><A HREF=\"https://delta.example/\" ADD_DATE=\"1700000000\" ICON=\"data:image/png;base64,AAA\">Delta again</A>\n" +
                "        <DT><A HREF=\"https://fresh.example/\" ADD_DATE=\"1700000000\">Fresh &amp; new</A>\n        <DT><A HREF=\"javascript:alert(1)\">Bookmarklet</A>\n    </DL><p>\n</DL><p>\n";
            var added = t.ImportHtml(chrome, "Imported from chrome-export");
            var folder = t.Children(BookmarkStore.OtherId).FirstOrDefault(x => x.Title == "Imported from chrome-export");
            Ok(added == 1 && folder != null && t.All().Any(x => x.Title == "Fresh & new"), "HTML import: into its own folder, entities decoded, duplicates and javascript: skipped");
            Ok(BookmarkHtml.Parse(chrome)[0].Toolbar, "HTML import: the toolbar folder is recognised");
        }

        static void SqliteTests()
        {
            var now = DateTime.UtcNow;
            var pages = new List<Tuple<string, string, int, DateTime, bool>>();
            for (int i = 0; i < 600; i++) pages.Add(Tuple.Create("https://site" + i + ".example/page?i=" + i, "Page number " + i, i % 50 + 1, now.AddMinutes(-i), i == 7));
            pages.Add(Tuple.Create("https://long.example/", "A very long title " + new string('x', 9000) + " end", 3, now, false));
            pages.Add(Tuple.Create("https://unicode.example/", "Ünïcødé ✓ 日本", 2, now, false));
            pages.Add(Tuple.Create("chrome://settings/", "Settings", 9, now, false));
            var bytes = TinySqlite.ChromeHistory(pages);
            Ok(bytes.Length > 4096 * 10, "SQLite fixture: a multi-page History file (" + bytes.Length / 1024 + " KB)");
            var r = new SqliteReader(bytes);
            var urls = r.Table("urls");
            Ok(urls != null && urls.Columns.Count == 7 && urls.RowidColumn == 0, "SQLite: schema read (urls, 7 columns, id = rowid)");
            var rows = r.Read("urls").ToList();
            Eq(rows.Count, 603, "SQLite: every row across leaf pages (interior root)");
            Eq(rows[5]["url"], "https://site5.example/page?i=5", "SQLite: text value");
            Eq(rows[5]["id"], 6L, "SQLite: id from the rowid");
            Eq(rows[49]["visit_count"], 50L, "SQLite: integer value");
            Eq(TinySqlite.ChromeTime(now.AddMinutes(-5)), rows[5]["last_visit_time"], "SQLite: 48/64-bit integers");
            Ok(((string)rows[600]["title"]).EndsWith(" end") && ((string)rows[600]["title"]).Length > 9000, "SQLite: a value on overflow pages");
            Eq(rows[601]["title"], "Ünïcødé ✓ 日本", "SQLite: UTF-8 text");
            var visits = r.Read("visits").First();
            Ok(visits["visit_duration"] is double && (double)visits["visit_duration"] == 1234.5, "SQLite: float value");
            var hist = Importer.ChromiumHistory(r);
            Ok(hist.Count == 601 && !hist.Any(h => h.Url.StartsWith("chrome:")) && !hist.Any(h => h.Url.Contains("site7.")), "Chromium history: web pages only, hidden ones left out");
            var h5 = hist.First(h => h.Url.Contains("site5."));
            Ok(h5.Visits == 6 && h5.Title == "Page number 5" && Math.Abs((DateTime.Parse(h5.Last) - now.AddMinutes(-5).ToLocalTime()).TotalSeconds) < 2, "Chromium history: title, visits, last visit (1601 epoch)");
            // 512-byte and 64 KB pages
            Eq(new SqliteReader(TinySqlite.ChromeHistory(pages.Take(50).ToList(), 512, false)).Read("urls").Count(), 50, "SQLite: 512-byte pages");
            Eq(new SqliteReader(TinySqlite.ChromeHistory(pages, 65536)).Read("urls").Count(), 603, "SQLite: 64 KB pages");
            // Firefox
            var ff = TinySqlite.FirefoxPlaces(new List<Tuple<string, string, int, DateTime?>> { Tuple.Create("https://mozilla.example/", "Moz", 4, (DateTime?)now), Tuple.Create("https://never.example/", "Bookmarked only", 0, (DateTime?)null), Tuple.Create("place:sort=8", "Smart", 0, (DateTime?)now) });
            var fh = Importer.FirefoxHistory(new SqliteReader(ff));
            Ok(fh.Count == 1 && fh[0].Title == "Moz" && fh[0].Visits == 4, "Firefox history: moz_places, visited web pages only");
            // broken files
            bool threw = false; try { new SqliteReader(Encoding.ASCII.GetBytes(new string('x', 1000))); } catch (InvalidDataException) { threw = true; }
            Ok(threw, "SQLite: not a database -> refused");
            var cut = bytes.Take(4096 * 3).ToArray(); threw = false; try { new SqliteReader(cut).Read("urls").ToList(); } catch (InvalidDataException) { threw = true; } catch (IndexOutOfRangeException) { threw = true; } catch (ArgumentException) { threw = true; }
            Ok(threw, "SQLite: a cut-short file -> an error, not a hang");
        }

        static void Lz4Tests()
        {
            var rnd = new Random(7);
            var samples = new List<byte[]> { new byte[0], Encoding.ASCII.GetBytes("abc"), Encoding.ASCII.GetBytes(new string('a', 5000)), Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 400).Select(i => "{\"title\":\"Bookmark " + (i % 17) + "\",\"uri\":\"https://example.com/" + (i % 23) + "\"},"))) };
            var r = new byte[3000]; rnd.NextBytes(r); samples.Add(r);
            var mixed = new List<byte>(); for (int i = 0; i < 20; i++) { var x = new byte[rnd.Next(1, 300)]; rnd.NextBytes(x); mixed.AddRange(x); mixed.AddRange(Encoding.ASCII.GetBytes(new string('z', rnd.Next(4, 600)))); } samples.Add(mixed.ToArray());
            int good = 0;
            foreach (var s in samples) { var c = TinyLz4.Compress(s); var d = Lz4.DecodeBlock(c, 0, c.Length, s.Length); if (d.SequenceEqual(s)) good++; }
            Eq(good, samples.Count, "LZ4: blocks decode exactly (literals, long runs, overlapping matches, random data)");
            Ok(TinyLz4.Compress(samples[2]).Length < 100, "LZ4 fixture compressor really compresses (5000 a's)");
            var moz = TinyLz4.MozLz4(samples[3]);
            Ok(Lz4.DecodeMozLz4(moz).SequenceEqual(samples[3]), "mozLz4: header + size + block");
            bool threw = false; try { Lz4.DecodeMozLz4(Encoding.ASCII.GetBytes("notmozlz4data")); } catch (InvalidDataException) { threw = true; }
            Ok(threw, "mozLz4: wrong magic refused");
            threw = false; try { Lz4.DecodeMozLz4(new byte[] { 0x6d, 0x6f, 0x7a, 0x4c, 0x7a, 0x34, 0x30, 0, 10, 0, 0, 0, 0x10, 0x41, 0x00, 0x00 }); } catch (InvalidDataException) { threw = true; }
            Ok(threw, "LZ4: a bad offset is refused");
        }

        // ---- fake profiles
        static string Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); return path; }
        static string Write(string path, byte[] b) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, b); return path; }
        public static string ChromeBookmarksJson(params Tuple<string, string, string>[] items)   // (root, title, url); root: bar | other | bar/Folder
        {
            var js = new JavaScriptSerializer();
            Func<string, object> folder = null; var all = items.ToList();
            Func<string, string, List<object>> kids = null;
            kids = (rootName, sub) =>
            {
                var res = new List<object>();
                foreach (var it in all.Where(x => x.Item1 == rootName + (sub == null ? "" : "/" + sub)))
                    res.Add(new Dictionary<string, object> { { "type", "url" }, { "name", it.Item2 }, { "url", it.Item3 }, { "date_added", "13340000000000000" }, { "id", "5" }, { "guid", Guid.NewGuid().ToString() } });
                if (sub == null)
                    foreach (var fname in all.Where(x => x.Item1.StartsWith(rootName + "/")).Select(x => x.Item1.Substring(rootName.Length + 1)).Distinct())
                        res.Add(new Dictionary<string, object> { { "type", "folder" }, { "name", fname }, { "children", kids(rootName, fname) }, { "date_added", "13340000000000000" } });
                return res;
            };
            var o = new Dictionary<string, object> { { "checksum", "x" }, { "version", 1 }, { "roots", new Dictionary<string, object> {
                { "bookmark_bar", new Dictionary<string, object> { { "type", "folder" }, { "name", "Bookmarks bar" }, { "children", kids("bar", null) } } },
                { "other", new Dictionary<string, object> { { "type", "folder" }, { "name", "Other bookmarks" }, { "children", kids("other", null) } } },
                { "synced", new Dictionary<string, object> { { "type", "folder" }, { "name", "Mobile bookmarks" }, { "children", new List<object>() } } } } } };
            return js.Serialize(o);
        }
        public static string FirefoxBackupJson()
        {
            Func<string, string, object> place = (t, u) => new Dictionary<string, object> { { "guid", Guid.NewGuid().ToString("N").Substring(0, 12) }, { "title", t }, { "type", "text/x-moz-place" }, { "uri", u }, { "dateAdded", 1700000000000000L } };
            var o = new Dictionary<string, object> { { "guid", "root________" }, { "title", "" }, { "type", "text/x-moz-place-container" }, { "children", new List<object> {
                new Dictionary<string, object> { { "guid", "menu________" }, { "title", "menu" }, { "type", "text/x-moz-place-container" }, { "children", new List<object> { place("Firefox menu page", "https://ffmenu.example/"), place("Recent tags", "place:type=6&sort=14") } } },
                new Dictionary<string, object> { { "guid", "toolbar_____" }, { "title", "toolbar" }, { "type", "text/x-moz-place-container" }, { "children", new List<object> { place("Fox toolbar", "https://foxbar.example/"),
                    new Dictionary<string, object> { { "guid", "f1" }, { "title", "Fox folder" }, { "type", "text/x-moz-place-container" }, { "children", new List<object> { place("In folder", "https://foxfolder.example/") } } },
                    new Dictionary<string, object> { { "guid", "s1" }, { "type", "text/x-moz-place-separator" } } } } },
                new Dictionary<string, object> { { "guid", "unfiled_____" }, { "title", "unfiled" }, { "type", "text/x-moz-place-container" }, { "children", new List<object>() } } } } };
            return new JavaScriptSerializer().Serialize(o);
        }
        // a fake %LOCALAPPDATA% / %APPDATA% with Chrome (2 profiles), Edge, Opera GX and Firefox
        public static string MakeFakeProfiles(string dir)
        {
            var now = DateTime.UtcNow;
            var L = Path.Combine(dir, "Local"); var R = Path.Combine(dir, "Roaming");
            var chrome = Path.Combine(L, "Google", "Chrome", "User Data");
            Write(Path.Combine(chrome, "Local State"), "{\"profile\":{\"info_cache\":{\"Default\":{\"name\":\"Person 1\"},\"Profile 1\":{\"name\":\"Work\"}}}}");
            Write(Path.Combine(chrome, "Default", "Bookmarks"), ChromeBookmarksJson(Tuple.Create("bar", "Chrome bar page", "https://chromebar.example/"), Tuple.Create("bar/Games", "Delta Force", "https://deltaforce.example/"), Tuple.Create("bar/Games", "Shared page", "https://shared.example/"), Tuple.Create("other", "Chrome other", "https://chromeother.example/")));
            Write(Path.Combine(chrome, "Default", "History"), TinySqlite.ChromeHistory(Enumerable.Range(0, 40).Select(i => Tuple.Create("https://chist" + i + ".example/", "Chrome history " + i, i + 1, now.AddHours(-i), false)).ToList()));
            Write(Path.Combine(chrome, "Default", "Login Data"), "fake passwords file - must never be read");
            Write(Path.Combine(chrome, "Default", "Cookies"), "fake cookies file - must never be read");
            Write(Path.Combine(chrome, "Profile 1", "Bookmarks"), ChromeBookmarksJson(Tuple.Create("bar", "Work page", "https://work.example/")));
            Write(Path.Combine(chrome, "System Profile", "Bookmarks"), ChromeBookmarksJson(Tuple.Create("bar", "never", "https://system.example/")));
            var edge = Path.Combine(L, "Microsoft", "Edge", "User Data");
            Write(Path.Combine(edge, "Default", "Bookmarks"), ChromeBookmarksJson(Tuple.Create("bar", "Shared page from Edge", "https://shared.example/"), Tuple.Create("other", "Edge only", "https://edge.example/")));
            var gx = Path.Combine(R, "Opera Software", "Opera GX Stable");
            Write(Path.Combine(gx, "Bookmarks"), ChromeBookmarksJson(Tuple.Create("bar", "GX page", "https://gx.example/")));
            Write(Path.Combine(gx, "History"), TinySqlite.ChromeHistory(new List<Tuple<string, string, int, DateTime, bool>> { Tuple.Create("https://chist1.example/", "Chrome history 1 (from GX)", 50, now.AddHours(2), false), Tuple.Create("https://gxonly.example/", "GX only", 1, now, false) }));
            var ff = Path.Combine(R, "Mozilla", "Firefox");
            Write(Path.Combine(ff, "profiles.ini"), "[Profile0]\nName=default-release\nIsRelative=1\nPath=Profiles/abcd1234.default-release\nDefault=1\n");
            var fp = Path.Combine(ff, "Profiles", "abcd1234.default-release");
            Write(Path.Combine(fp, "bookmarkbackups", "bookmarks-2026-11-01_4_old.jsonlz4"), TinyLz4.MozLz4(Encoding.UTF8.GetBytes("{\"guid\":\"root________\",\"children\":[]}")));
            Write(Path.Combine(fp, "bookmarkbackups", "bookmarks-2026-11-20_4_newest.jsonlz4"), TinyLz4.MozLz4(Encoding.UTF8.GetBytes(FirefoxBackupJson())));
            Write(Path.Combine(fp, "places.sqlite"), TinySqlite.FirefoxPlaces(new List<Tuple<string, string, int, DateTime?>> { Tuple.Create("https://fxhist.example/", "Firefox history", 3, (DateTime?)now) }));
            Write(Path.Combine(fp, "logins.json"), "fake - must never be read");
            return dir;
        }
        static Dictionary<string, string> Hashes(string dir)
        {
            using (var sha = SHA256.Create())
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => f.Substring(dir.Length), f => Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(f))) + File.GetLastWriteTimeUtc(f).Ticks);
        }

        static void ImportTests()
        {
            var dir = MakeFakeProfiles(Path.Combine(root, "fake-profiles"));
            var before = Hashes(dir);
            var found = Importer.Find(Path.Combine(dir, "Local"), Path.Combine(dir, "Roaming"));
            Eq(string.Join(", ", found.Select(f => f.Label)), "Chrome, Chrome (Work), Edge, Opera GX, Firefox", "import: finds the browsers and profiles (names from Local State / profiles.ini)");
            foreach (var p in found) Importer.Count(p);
            var ch = found[0];
            Ok(ch.Bookmarks == 4 && ch.History == 40, "import: counts for the picker (Chrome: 4 bookmarks, 40 pages)");
            var fx = found.First(x => x.Browser == "Firefox");
            Ok(fx.Bookmarks == 3 && fx.History == 1, "import: Firefox counts from the newest jsonlz4 backup and places.sqlite (" + fx.Bookmarks + ", " + fx.History + ")");
            var store = new BookmarkStore(Tmp("b.json")); store.Load();
            var hist = new HistoryStore(Tmp("h.json")); hist.Load();
            hist.Add("https://chist1.example/", "Mine", DateTime.Now.AddDays(-30));
            var r1 = Importer.Run(ch, true, true, store, hist);
            Ok(r1.Bookmarks == 4 && r1.History == 39, "import Chrome: 4 bookmarks, 39 new pages (one was already in the history)");
            var folder = store.Children(BookmarkStore.OtherId).FirstOrDefault(x => x.Folder && x.Title == "Imported from Chrome");
            Ok(folder != null, "import: into \"Imported from Chrome\" under Other bookmarks");
            var paths = Tree(store);
            Ok(paths.Contains("other/Imported from Chrome/Bookmarks bar/Games|Delta Force|https://deltaforce.example/") && paths.Contains("other/Imported from Chrome/Other bookmarks|Chrome other|https://chromeother.example/"), "import: the other browser's folders kept inside");
            Ok(!paths.Any(p => p.Contains("Mobile bookmarks")), "import: empty roots aren't made");
            var r2 = Importer.Run(ch, true, true, store, hist);
            Ok(r2.Bookmarks == 0 && r2.SkippedBookmarks == 4 && r2.History == 0 && store.Count == 4, "import again: no duplicates (bookmarks or history)");
            var edge = found.First(x => x.Browser == "Edge");
            var r3 = Importer.Run(edge, true, false, store, hist);
            Ok(r3.Bookmarks == 1 && r3.SkippedBookmarks == 1 && store.All().Count(b => b.Url == "https://shared.example/") == 1, "import Edge: a page already imported from Chrome is skipped");
            var gx = found.First(x => x.Browser == "Opera GX");
            var r4 = Importer.Run(gx, true, true, store, hist);
            var c1 = hist.All().First(h => h.Url == "https://chist1.example/");
            Ok(r4.History == 1 && c1.Visits == 50, "import Opera GX (profile in its root folder): history merged - most visits kept");
            var r5 = Importer.Run(fx, true, true, store, hist);
            Ok(r5.Bookmarks == 3 && Tree(store).Contains("other/Imported from Firefox/Bookmarks Toolbar/Fox folder|In folder|https://foxfolder.example/") && !store.All().Any(b => b.Url.StartsWith("place:")), "import Firefox: jsonlz4 bookmarks (toolbar, folders; place: queries left out)");
            var after = Hashes(dir);
            Ok(before.Count == after.Count && before.All(kv => after.ContainsKey(kv.Key) && after[kv.Key] == kv.Value), "import: the browser profiles are unchanged (nothing written, nothing added)");
            Ok(!Directory.GetDirectories(Path.GetTempPath(), "HallowBrowserImport-*").Any(d => Directory.GetLastWriteTimeUtc(d) > DateTime.UtcNow.AddMinutes(-2)), "import: the temp copies are deleted");
            var src = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "src", "Import13.cs"));
            Ok(!src.Contains("\"Login Data\"") && !src.Contains("\"Cookies\"") && !src.Contains("logins.json") && !src.Contains("cookies.sqlite") && !src.Contains("key4.db"), "import: the importer never names a password or cookie file");
            // a locked file: the error is a note, nothing breaks
            using (new FileStream(Path.Combine(ch.Dir, "Bookmarks"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var r6 = Importer.Run(ch, true, false, store, hist);
                Ok(r6.Bookmarks == 0 && r6.Notes.Count == 1 && r6.Notes[0].Contains("close it"), "import: a locked file -> \"close Chrome and try again\"");
            }
        }

        // ---- sync between two test profiles
        class Pc
        {
            public string Name, Dir; public BrowserSettings S; public BookmarkStore B; public List<SavedSession> Saved = new List<SavedSession>(); public WorkspaceStore Ws = new WorkspaceStore(); public SyncState St = new SyncState(); public string Pw;
            public SyncInput Input { get { return new SyncInput { Settings = S, Bookmarks = B, Saved = Saved, Spaces = Ws, CurrentSpace = "default" }; } }
            public SyncOutcome Sync(string folder)
            {
                var o = SyncCore.Run(SyncData.Gather(Input), St, folder, new SyncOptions { Dev = S.SyncDevice, DevName = Name, Password = Pw, CopyData = SyncData.CopyData, KeepDir = Dir });
                if (o.State2 != null) { if (o.ChangedKinds.Count > 0) SyncData.Apply(Input, o.Result, o.ChangedKinds); St = o.State2; }
                return o;
            }
        }
        static Pc NewPc(string name)
        {
            var d = Path.Combine(root, "pc-" + name); Directory.CreateDirectory(d);
            var s = new BrowserSettings(); s.Fix();
            var pc = new Pc { Name = name, Dir = d, S = s, B = new BookmarkStore(Path.Combine(d, "bookmarks.json")) };
            pc.B.Load(); pc.Ws.Items.Add(new Workspace { Id = "default", Name = "Default", Session = new Session() }); pc.Ws.Current = "default";
            return pc;
        }
        static string Titles(Pc p) { return string.Join(",", p.B.All().Select(b => b.Title).OrderBy(x => x, StringComparer.Ordinal)); }

        static void SyncTests()
        {
            SyncCrypto.Iterations = 2000;   // fast in tests (the file says how many rounds; the app uses 600,000)
            var folder = Path.Combine(root, "SyncFolder"); Directory.CreateDirectory(folder);
            var A = NewPc("DESKTOP-A"); var B = NewPc("LAPTOP-B");
            Ok(A.S.SyncDevice != B.S.SyncDevice, "sync: each PC has its own id");
            A.S.Search = "brave"; A.S.Width = 1500; A.S.RamLimitMb = 8192; A.S.Typed.Add("https://typed.example/"); A.S.NewTabPinned.Add(new PinnedSite { Url = "https://pin.example/", Title = "Pin" });
            var x = A.B.Add(BookmarkStore.BarId, "Xray", "https://x.example/");
            var y = A.B.Add(BookmarkStore.BarId, "Yankee", "https://y.example/");
            var z = A.B.Add(BookmarkStore.BarId, "Zulu", "https://z.example/");
            var fa = A.B.AddFolder(BookmarkStore.OtherId, "Folder A");
            A.Saved.Add(new SavedSession { Name = "Raid night", Saved = "2026-11-20T20:00:00", Session = new Session { Tabs = new List<SessionTab> { new SessionTab { Url = "https://raid.example/" } } } });
            B.S.Search = "google";
            B.B.Add(BookmarkStore.BarId, "From B", "https://fromb.example/");
            var oa = A.Sync(folder);
            Ok(oa.State == "synced" && oa.Wrote && File.Exists(Path.Combine(folder, SyncCore.FileName)), "sync: the first PC writes the folder's copy");
            var ob = B.Sync(folder);
            Ok(ob.State == "synced" && Titles(B) == "From B,Xray,Yankee,Zulu", "sync: the second PC gets the bookmarks (" + Titles(B) + ")");
            Ok(B.B.Everything().Any(b => b.Folder && b.Title == "Folder A"), "sync: folders too");
            Eq(B.S.Search, "brave", "sync: a PC joining takes the folder's settings");
            Ok(B.S.Width == 1280 && B.S.RamLimitMb == 0 && B.S.Typed.Count == 0, "sync: per-PC settings (window size, RAM limiter, typed addresses) stay on their PC");
            Ok(B.S.NewTabPinned.Any(p => p.Url == "https://pin.example/") && B.Saved.Any(s => s.Name == "Raid night"), "sync: pinned tiles and saved sessions");
            A.Sync(folder);
            Ok(Titles(A) == Titles(B), "sync: both PCs end the same (" + Titles(A) + ")");
            var bz = B.B.All().First(b => b.Title == "Zulu");
            // edits and deletes on different PCs
            B.B.Rename(B.B.All().First(b => b.Title == "Yankee").Id, "Yankee (renamed on B)");
            A.B.Delete(x.Id);
            A.B.Move(z.Id, BookmarkStore.BarId, 0);
            A.Sync(folder); B.Sync(folder); A.Sync(folder);
            Ok(Titles(A) == Titles(B) && Titles(A).Contains("Yankee (renamed on B)") && !Titles(A).Contains("Xray"), "sync: a rename on one PC and a delete on the other both arrive");
            Eq(B.B.Children(BookmarkStore.BarId).First().Title, "Zulu", "sync: a move (the new order) arrives");
            // the same bookmark changed on both: the newer wins, the other is kept as a conflict copy - the same on both PCs
            var ya = A.B.All().First(b => b.Title.StartsWith("Yankee")); var yb = B.B.All().First(b => b.Title.StartsWith("Yankee"));
            A.B.Rename(ya.Id, "Yankee from A"); System.Threading.Thread.Sleep(20); B.B.Rename(yb.Id, "Yankee from B");
            var c1 = A.Sync(folder); var c2 = B.Sync(folder); A.Sync(folder); B.Sync(folder);
            Ok(c2.Conflicts >= 1, "sync: a real conflict is noticed");
            Ok(Titles(A) == Titles(B) && Titles(A).Contains("Yankee from B") && Titles(A).Contains("Yankee from A (conflict copy from DESKTOP-A)"), "sync: newer wins, the other kept as a conflict copy, the same on both (" + Titles(A) + ")");
            Eq(A.B.All().Count(b => b.Title.Contains("conflict copy")), 1, "sync: the conflict copy appears once");
            // an edit on one PC and a delete on the other: the edit is kept
            var za = A.B.All().First(b => b.Title == "Zulu"); var zb = B.B.All().First(b => b.Title == "Zulu");
            A.B.Delete(za.Id); B.B.Rename(zb.Id, "Zulu edited");
            A.Sync(folder); B.Sync(folder); A.Sync(folder);
            Ok(Titles(A).Contains("Zulu edited") && Titles(B).Contains("Zulu edited"), "sync: an edit wins over a delete it hadn't seen");
            // a cloud client's conflict copy ("... (1).hbsync") is merged in and moved out of the folder
            var main = Path.Combine(folder, SyncCore.FileName);
            B.B.Add(BookmarkStore.OtherId, "Only in the cloud copy", "https://cloudcopy.example/"); B.Sync(folder);
            var cloudCopy = Path.Combine(folder, "Hallow Browser sync (1).hbsync"); File.Copy(main, cloudCopy);
            B.B.Delete(B.B.All().First(b => b.Title == "Only in the cloud copy").Id);   // B goes on; its next write loses nothing because the copy is merged
            var syncthing = Path.Combine(folder, "Hallow Browser sync.sync-conflict-20261121-101010-ABCDEFG.hbsync"); File.Copy(main, syncthing);
            var oc = A.Sync(folder);
            Ok(oc.ConflictFiles == 2 && !File.Exists(cloudCopy) && !File.Exists(syncthing), "sync: cloud conflict files (OneDrive \"(1)\", Syncthing \".sync-conflict-\") merged and taken out of the folder");
            Ok(Directory.GetFiles(Path.Combine(A.Dir, "sync-merged")).Length == 2, "sync: ...and kept on this PC (sync-merged)");
            Ok(Titles(A).Contains("Only in the cloud copy"), "sync: what was only in the conflict copy arrives");
            var dropbox = Path.Combine(folder, "Hallow Browser sync (LAPTOP-B's conflicted copy 2026-11-21).hbsync");
            var none = SyncCore.ConflictFiles(folder).Count == 0; File.WriteAllText(dropbox, "x");
            Ok(none && SyncCore.ConflictFiles(folder).Contains(dropbox), "sync: Dropbox's \"conflicted copy\" name is recognised");
            File.Delete(dropbox);
            B.Sync(folder); A.Sync(folder);
            Ok(Titles(A) == Titles(B), "sync: in step again");
            // encryption
            A.Pw = "correct horse battery"; var oe = A.Sync(folder);
            var raw = File.ReadAllBytes(main); var rawText = Encoding.UTF8.GetString(raw);
            Ok(oe.Wrote && oe.Encrypted && SyncCore.IsEncrypted(raw), "sync: with a password the folder's copy is encrypted");
            Ok(!rawText.Contains("Yankee") && !rawText.Contains("example") && !rawText.Contains("brave") && !rawText.Contains("hallow-browser-sync"), "sync: the encrypted file shows no titles, addresses or settings");
            var len = raw.Length; var bNo = B.Sync(folder);
            Ok(bNo.State == "error" && bNo.Message.Contains("encrypted") && File.ReadAllBytes(main).SequenceEqual(raw), "sync: without the password the file can't be read (and isn't overwritten)");
            B.Pw = "wrong password"; var bWrong = B.Sync(folder);
            Ok(bWrong.State == "error" && bWrong.Message.Contains("password") && File.ReadAllBytes(main).SequenceEqual(raw), "sync: a wrong password can't read it either");
            bool threw = false; try { SyncCore.Read(raw, "wrong password"); } catch (CryptographicException) { threw = true; }
            Ok(threw, "sync: AES-GCM refuses the wrong key");
            var tampered = (byte[])raw.Clone(); tampered[tampered.Length - 20] ^= 1; threw = false; try { SyncCore.Read(tampered, "correct horse battery"); } catch (CryptographicException) { threw = true; }
            Ok(threw, "sync: a changed byte is detected (GCM tag)");
            B.Pw = "correct horse battery"; B.B.Add(BookmarkStore.BarId, "Encrypted hello", "https://enc.example/");
            var bOk = B.Sync(folder); A.Sync(folder);
            Ok(bOk.State == "synced" && Titles(A).Contains("Encrypted hello") && SyncCore.IsEncrypted(File.ReadAllBytes(main)), "sync: with the right password both PCs sync, still encrypted");
            Ok(SyncCrypto.SaltOf(File.ReadAllBytes(main)).SequenceEqual(SyncCrypto.SaltOf(raw)), "sync: the salt is kept (each PC derives the key once)");
            // the folder isn't there (cloud not mounted): waiting, nothing breaks
            var w = A.Sync(Path.Combine(root, "not-there"));
            Ok(w.State == "waiting", "sync: a missing folder = waiting");
            // a file that isn't ours is left alone
            var other = Path.Combine(root, "OtherFolder"); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, SyncCore.FileName), "{\"Format\":\"something else\"}");
            var C = NewPc("PC-C"); var oo = C.Sync(other);
            Ok(oo.State == "error" && File.ReadAllText(Path.Combine(other, SyncCore.FileName)).Contains("something else"), "sync: someone else's file isn't overwritten");
            // no history in the sync file
            var plainFolder = Path.Combine(root, "Plain"); Directory.CreateDirectory(plainFolder);
            var D = NewPc("PC-D"); D.B.Add(BookmarkStore.BarId, "Dee", "https://dee.example/"); D.Sync(plainFolder);
            var doc = SyncCore.Read(File.ReadAllBytes(Path.Combine(plainFolder, SyncCore.FileName)), null);
            Ok(doc.Records.All(r => SyncData.Kinds.Contains(r.Kind)) && !doc.Records.Any(r => r.Id == "set:Typed" || r.Id == "set:Width" || r.Id.StartsWith("set:Sync")), "sync: only bookmarks, settings that travel, sessions, workspaces and panels (no history, no per-PC settings)");
            // VV basics
            Eq(Vv.Compare("a:2,b:1", "a:1,b:1"), 1, "version vectors: newer");
            Eq(Vv.Compare("a:1", "b:1"), 2, "version vectors: concurrent");
            Eq(Vv.Max("a:2,b:1", "b:3"), "a:2,b:3", "version vectors: max");
        }

        static void NewTabTests()
        {
            var now = DateTime.Now;
            var h = new List<HistoryEntry> {
                new HistoryEntry { Url = "https://www.game.example/a", Title = "Game A", Visits = 40, Last = now.ToString("s") },
                new HistoryEntry { Url = "https://game.example/b", Title = "Game B", Visits = 5, Last = now.ToString("s") },
                new HistoryEntry { Url = "https://news.example/", Title = "News", Visits = 20, Last = now.AddDays(-1).ToString("s") },
                new HistoryEntry { Url = "https://old.example/", Title = "Old", Visits = 30, Last = now.AddDays(-300).ToString("s") },
                new HistoryEntry { Url = "https://hidden.example/", Title = "Hidden", Visits = 99, Last = now.ToString("s") },
                new HistoryEntry { Url = "https://pinned.example/", Title = "Pinned", Visits = 99, Last = now.ToString("s") } };
            var top = NewTabData.TopSites(h, new HashSet<string> { "https://hidden.example/" }, new HashSet<string> { "https://pinned.example/" }, 8, now);
            Eq(string.Join(",", top.Select(x => x.Title)), "Game A,News,Old", "new tab: top sites - one per site, recent visits count more, hidden and pinned left out");
            var s = new BrowserSettings(); s.Fix(); s.NewTabPinned.Add(new PinnedSite { Url = "https://pinned.example/", Title = "My pin" }); s.NewTabHidden.Add("https://hidden.example/");
            var json = NewTabData.Build(s, h, null, now);
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var tiles = (System.Collections.ArrayList)d["tiles"];
            Ok(tiles.Count == 4 && (string)((Dictionary<string, object>)tiles[0])["t"] == "My pin" && (bool)((Dictionary<string, object>)tiles[0])["pin"], "new tab: pinned tiles first, then top sites");
            var fam = NewTabData.Strip("{\"app\":\"Guard\",\"score\":91,\"scoreLabel\":\"Looking good\"}", "{\"blockedToday\":1234,\"updated\":\"" + now.ToString("s") + "\"}", "Lo-fi beats", now);
            Ok(fam.Count == 3 && fam[0]["v"] == "91/100 - Looking good" && fam[1]["v"] == "1,234 blocked today" && fam[2]["k"] == "NOW PLAYING", "new tab: the Hallow strip (Guard score, Shield blocked today, now playing)");
            Eq(NewTabData.Strip(null, "{\"blockedToday\":5,\"updated\":\"" + now.AddDays(-2).ToString("s") + "\"}", null, now).Count, 0, "new tab: yesterday's Shield count and missing apps are left out");
            Eq(NewTabData.Strip("not json", "{", "", now).Count, 0, "new tab: broken status files are ignored");
            Eq(NewTabData.MixerPlaying("{\"nowPlaying\":{\"title\":\"Song\",\"artist\":\"Band\"}}"), "Song - Band", "new tab: Mixer's now playing");
            s.NewTabTopSites = false; s.NewTabFamily = false;
            Ok(NewTabData.Build(s, h, null, now).Contains("\"tiles\":[]"), "new tab: speed dial off = no tiles");
            var html = Engine.NewTabHtml("duckduckgo", "#F07820");
            Ok(html.Contains("fetch('data.json"), "new tab: data from data.json next to the page");
            Ok(!System.Text.RegularExpressions.Regex.IsMatch(html, "src=|href=\"http|fetch\\('http|<link"), "new tab: nothing from the network (no src, links or remote fetches)");
            var script = html.Substring(html.IndexOf("<script>"));
            Ok(script.IndexOf('\n') < 0 && script.IndexOf('\t') < 0 && script.IndexOf('\r') < 0, "new tab: no raw line breaks or tabs inside the page script (JS strings)");
            Eq(NewTabData.Letter("https://www.youtube.com/", "x"), "Y", "new tab: tile letter from the site");
        }

        // light mode: the text / background pairs the browser really draws, at 4.5:1, for several Hallow accents (hover included)
        static void LightContrastTests()
        {
            var x = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("HallowBrowser.Theme.xaml")).ReadToEnd();
            var bgs = new[] { Look.LBg, Look.LPanel, Look.LPanel2 };   // page / bar, panels and flyouts, strip / hover / rail
            int bad = 0; var misses = new List<string>();
            Action<string, string, string> pair = (what, fg, bg) => { var c = Look.Contrast(fg, bg); if (c < 4.5) { bad++; misses.Add(what + " " + fg + " on " + bg + " = " + c.ToString("0.00")); } };
            foreach (var accent in new[] { "#F07820", "#8B5CF6", "#3B82F6", "#22C55E", "#EAB308", "#EC4899", "#06B6D4", "#EF4444" })
            {
                var light = Accent.ApplyTo(Look.LightXaml(x, accent), accent);
                var rd = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Parse(light);
                Func<string, string> col = k => { var o = rd[k]; var c = o is System.Windows.Media.SolidColorBrush ? ((System.Windows.Media.SolidColorBrush)o).Color : (System.Windows.Media.Color)o; return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); };
                // accent buttons and the game chip: filled, with their text; hover lays a 10% accent veil over the fill
                var fill = col("AccentBtnBg"); var txt = col("AccentBtnText");
                pair(accent + " accent button", txt, fill);
                var f = Look.C(fill); var a = Look.C(accent);
                Func<byte, byte, byte> mix = (p, q) => (byte)Math.Round(p * 0.9 + q * 0.1);
                pair(accent + " accent button (hover)", txt, Look.H(System.Windows.Media.Color.FromRgb(mix(f.R, a.R), mix(f.G, a.G), mix(f.B, a.B))));
                foreach (var bg in bgs)
                {
                    pair(accent + " accent text", col("AccentLight"), bg); pair(accent + " accent", col("AccentBrush"), bg);
                    foreach (var k in new[] { "Text", "Muted", "Lilac", "GreenText", "AmberText", "Red" }) pair(k, col(k), bg);
                }
                foreach (var g in GroupRules.Colors) pair("group " + g, col(GroupRules.Key(g)), Look.LPanel2);
            }
            Ok(bad == 0, "light mode: every text colour the browser uses reaches 4.5:1 on the light surfaces, accent buttons and the game chip filled (8 accents)" + (bad == 0 ? "" : " - " + string.Join("; ", misses.Distinct().Take(8))));
            var ota = Look.TextAccent("#F07820");
            Ok(Look.Contrast(ota, Look.LPanel2) >= 4.5 && ota.StartsWith("#A"), "light mode: orange text accent darkened against #EEEAF5 too (" + ota + ")");
            Ok(Look.FillOf("#8B5CF6") != "#8B5CF6" && Look.TextOnFill(Look.FillOf("#8B5CF6")) == "#FFFFFF", "light mode: violet fills darken to take white text (Toolkit 5.16 rule)");
            Eq(Look.FillOf("#F07820"), "#F07820", "light mode: orange stays a real orange fill (near-black text)");
        }

        static void SettingsTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(!s.BookmarksBar && s.NewTabTopSites && s.NewTabClock && s.NewTabFamily && !s.SyncOn && !s.SyncEncrypt, "1.3 settings: defaults (bar off, sync off, new tab parts on)");
            var f = Tmp("settings.json");
            File.WriteAllText(f, "{\"Search\":\"bing\",\"SettingsVersion\":2,\"VerticalTabs\":true}");
            BrowserSettings l; using (var fs = File.OpenRead(f)) l = (BrowserSettings)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(BrowserSettings)).ReadObject(fs); l.Fix();
            Ok(l.Search == "bing" && l.VerticalTabs && l.NewTabTopSites && l.NewTabClock && l.NewTabPinned != null && l.SyncDevice != null && !l.SyncOn, "1.3 settings: a 1.2 file loads with the new defaults");
            Ok(SyncData.SettingKeys.All(k => typeof(BrowserSettings).GetField(k) != null), "sync: every synced setting exists");
            Ok(!SyncData.SettingKeys.Any(k => k.StartsWith("Sync") || k == "Typed" || k == "Width" || k == "Left" || k == "RamLimitMb" || k == "CpuLimiter" || k == "ShieldLoaded" || k == "SidebarOpenId"), "sync: per-PC and sync settings never travel");
            Eq(Omni.Interpret("hallow://bookmarks", "duckduckgo"), Omni.BookmarksUrl, "address bar: hallow://bookmarks opens the manager");
            Ok(Omni.FixNewTab("https://newtab.hallow/") == Omni.NewTabUrl && Omni.NewTabUrl.EndsWith("/index.html") && Omni.FixNewTab("https://a.example/") == "https://a.example/", "new tab: the page by name (a 1.2 session's folder address is moved to it)");
            Ok(Omni.IsNewTab(Omni.NewTabUrl) && Omni.Display(Omni.NewTabUrl) == "", "new tab: still shows an empty address bar");
            Ok(Engine.ExtraArgs(s).Contains("--host-resolver-rules=\"MAP newtab.hallow ~NOTFOUND"), "new tab: its made-up host never goes to DNS (no wait, no lookup leaves the PC)");
        }
    }
}
