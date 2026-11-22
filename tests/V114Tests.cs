// Hallow Browser 1.14 - unit tests: the site logo cache (write / read / identical icons / the LRU limit / private windows never write /
// --snap never writes), the fallback order (cached icon -> parent domain -> brand -> letter tile), reading Chromium's "Favicons" file
// (a made-up fixture written with TinySqlite, the real table layout), the one-time "logos from the browser your bookmarks came from"
// against FAKE browser profiles in a temp folder (a copy is read; a file in use is reported, never forced), the fetcher's origins and
// its once-per-site memory, the new tab tiles and the setting's default. Temp folders only - never the user's real Brave.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowBrowser.Tests
{
    public static class V114
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All()
        {
            var d = Temp();
            try { KeyTests(); CacheTests(d); FallbackTests(d); FaviconsFileTests(d); OnceTests(d); FetchRuleTests(d); NewTabTests(d); SettingTests(d); }
            finally { SiteIcons.UseDir(null); SiteIcons.MaxFiles = 2000; SiteIcons.ReadOnly = false; SiteIcons.Fake = null; LogoTried.Reset(); try { Directory.Delete(d, true); } catch { } }
        }
        static string Temp() { var d = Path.Combine(Path.GetTempPath(), "hb114-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(d); return d; }
        static string Fresh(string root, string name) { var d = Path.Combine(root, name); Directory.CreateDirectory(d); SiteIcons.UseDir(d); LogoTried.Reset(); return d; }

        // a made-up icon: one colour, size x size
        public static byte[] Png(int size, byte r, byte g, byte b)
        {
            var px = new byte[size * size * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
            var bs = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bs));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }
        static int[] Info(byte[] png)   // width, height, r, g, b of the centre pixel
        {
            var dec = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var f = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
            var px = new byte[4]; f.CopyPixels(new Int32Rect(f.PixelWidth / 2, f.PixelHeight / 2, 1, 1), px, 4, 0);
            return new[] { f.PixelWidth, f.PixelHeight, px[2], px[1], px[0] };
        }

        static void KeyTests()
        {
            Eq(SiteIcons.Key("https://www.Example.com/a?b"), "example.com", "logos: the key is the host, lower case, without www.");
            // 1.14.1: one home server runs several apps (Emby on :8096, the NAS's own page on :9443) - each keeps its own logo
            Eq(SiteIcons.Key("http://192.168.1.20:8096/web/index.html"), "192.168.1.20:8096", "logos: a NAS address keys by its IP and port");
            Ok(SiteIcons.Key("https://192.168.1.20:9443/") != SiteIcons.Key("http://192.168.1.20:8096/"), "logos: two apps on one NAS (different ports) get different logos");
            Eq(SiteIcons.Key("http://192.168.1.20/"), "192.168.1.20", "logos: a NAS on the default port is just its IP");
            Eq(SiteIcons.Key("https://example.com:8443/x"), "example.com", "logos: an internet site's port doesn't matter");
            Eq(string.Join(",", SiteIcons.Chain("192.168.1.20:8096")), "192.168.1.20:8096", "logos: an IP and port is only itself");
            Eq(SiteIcons.Key("mail.google.com"), "mail.google.com", "logos: a bare host works too");
            Ok(SiteIcons.Key("hallow://bookmarks") == null && SiteIcons.Key("file:///C:/x.html") == null && SiteIcons.Key(null) == null && SiteIcons.Key(Omni.NewTabUrl) == null, "logos: no key for internal pages, files or the new tab page");
            Eq(string.Join(",", SiteIcons.Chain("a.mail.google.com")), "a.mail.google.com,mail.google.com,google.com", "logos: the lookup goes host -> parent domains (down to two labels)");
            Eq(string.Join(",", SiteIcons.Chain("10.0.0.5")), "10.0.0.5", "logos: an IP address is only itself");
            Ok(SiteIcons.FileName("example.com").Length == 24 && SiteIcons.FileName("example.com") == SiteIcons.FileName("example.com") && SiteIcons.FileName("example.com") != SiteIcons.FileName("example.org"), "logos: a file per site, named by a hash of the host (no address in the file name)");
        }

        static void CacheTests(string root)
        {
            var dir = Fresh(root, "cache");
            var red = Png(16, 220, 30, 30);
            Ok(SiteIcons.Save("https://www.red.example/page", red, false), "logos cache: a tab's icon is saved");
            var f = SiteIcons.PathFor("red.example");
            Ok(File.Exists(f) && f.StartsWith(dir), "logos cache: written as <hash>.png in the profile's favicons folder");
            var back = SiteIcons.Png("https://red.example/other");
            Ok(back != null && Info(back)[2] == 220 && Info(back)[0] == 16, "logos cache: read back for any page of the site (16 px kept as is)");
            Ok(SiteIcons.Image("https://red.example/") != null && SiteIcons.Image("https://red.example/").IsFrozen, "logos cache: an image for the UI (frozen, any thread)");
            var big = Png(128, 10, 120, 240);
            Ok(SiteIcons.Save("https://big.example/", big, false), "logos cache: a 128 px icon is saved ...");
            var bi = Info(SiteIcons.Png("https://big.example/"));
            Ok(bi[0] == 32 && bi[1] == 32 && bi[4] == 240, "logos cache: ... scaled to 32 px (" + bi[0] + " px)");
            Ok(new FileInfo(SiteIcons.PathFor("big.example")).Length <= SiteIcons.MaxBytes, "logos cache: files are size-capped (" + new FileInfo(SiteIcons.PathFor("big.example")).Length + " bytes)");
            var w0 = SiteIcons.Writes;
            Ok(!SiteIcons.Save("https://red.example/again", red, false) && SiteIcons.Writes == w0, "logos cache: the same icon again isn't written again");
            Ok(SiteIcons.Save("https://red.example/", Png(16, 0, 200, 0), false) && Info(SiteIcons.Png("https://red.example/"))[3] == 200, "logos cache: a changed icon replaces the old one");
            Ok(!SiteIcons.Save("https://junk.example/", Encoding.UTF8.GetBytes("<html>not found</html>"), false) && !SiteIcons.HasExact("junk.example"), "logos cache: something that isn't an image is never saved");
            Ok(!SiteIcons.Save("https://tiny.example/", Png(2, 1, 2, 3), false), "logos cache: a 2 px 'icon' (tracking pixel) is not a logo");

            // private windows never write
            var n0 = SiteIcons.Count(); var w1 = SiteIcons.Writes;
            Ok(!SiteIcons.Save("https://secret.example/", Png(16, 1, 2, 3), true), "logos cache: a private window's icon is NOT saved");
            Ok(!SiteIcons.HasExact("secret.example") && SiteIcons.Count() == n0 && SiteIcons.Writes == w1 && !File.Exists(SiteIcons.PathFor("secret.example")), "logos cache: ... no file, nothing counted");
            // --snap never writes
            SiteIcons.ReadOnly = true;
            Ok(!SiteIcons.Save("https://snap.example/", Png(16, 1, 2, 3), false) && SiteIcons.AddMissing(new Dictionary<string, byte[]> { { "snap2.example", Png(16, 1, 2, 3) } }) == 0 && !File.Exists(SiteIcons.PathFor("snap.example")), "logos cache: --snap writes nothing");
            SiteIcons.ReadOnly = false;
            // icons from another browser never replace one a tab gave
            var n = SiteIcons.AddMissing(new Dictionary<string, byte[]> { { "red.example", Png(16, 9, 9, 9) }, { "new.example", Png(16, 50, 60, 70) } });
            Ok(n == 1 && Info(SiteIcons.Png("https://red.example/"))[3] == 200 && SiteIcons.HasExact("new.example"), "logos cache: imported icons only fill in sites without one (" + n + ")");

            // LRU: at most MaxFiles; the least recently used go first (reading a site's icon counts as a use)
            var lru = Fresh(root, "lru"); SiteIcons.MaxFiles = 5;
            for (int i = 0; i < 5; i++) { SiteIcons.Save("https://s" + i + ".example/", Png(16, (byte)(i * 40), 1, 1), false); File.SetLastWriteTimeUtc(SiteIcons.PathFor("s" + i + ".example"), DateTime.UtcNow.AddHours(-10 + i)); }
            SiteIcons.Forget();
            Ok(SiteIcons.Image("https://s0.example/") != null, "logos LRU: the oldest site is used again (drawn)");
            SiteIcons.Save("https://s5.example/", Png(16, 7, 7, 7), false);
            var left = Enumerable.Range(0, 6).Where(i => SiteIcons.HasExact("s" + i + ".example")).ToList();
            Ok(SiteIcons.Count() == 4 && left.Count == 4, "logos LRU: over the limit -> down to 90 % (" + SiteIcons.Count() + " files)");
            Ok(left.Contains(0) && left.Contains(5) && !left.Contains(1) && !left.Contains(2), "logos LRU: the least recently used went (s1, s2), the one just used (s0) and the newest stayed: " + string.Join(",", left));
            SiteIcons.MaxFiles = 2000;
            Ok(Directory.GetFiles(lru, "*.tmp").Length == 0, "logos cache: no temp files left behind");
        }

        static void FallbackTests(string root)
        {
            Fresh(root, "fallback");
            Ok(SiteIcons.Pick("https://unknown-site.example/") == "letter:U:" + NewTabData.Color("https://unknown-site.example/"), "fallback: no icon, no brand -> a letter tile (first letter, colour from a hash): " + SiteIcons.Pick("https://unknown-site.example/"));
            Ok(SiteIcons.Pick("https://www.twitch.tv/somebody") == "brand:twitch" && SiteIcons.Pick("https://github.com/x") == "brand:github", "fallback: well-known sites -> the Font Awesome brand icon (Twitch, GitHub)");
            Ok(SiteIcons.Pick("http://192.168.1.20:8096/") .StartsWith("letter:1:"), "fallback: a NAS address -> a letter tile");
            SiteIcons.Save("https://www.twitch.tv/", Png(16, 145, 70, 255), false);
            Eq(SiteIcons.Pick("https://www.twitch.tv/somebody"), "favicon", "fallback: once the site's own icon is cached it wins over the brand icon");
            SiteIcons.Save("https://google.com/", Png(16, 66, 133, 244), false);
            Ok(SiteIcons.Pick("https://mail.google.com/mail/u/0/") == "favicon" && !SiteIcons.HasExact("mail.google.com"), "fallback: a subdomain without its own icon shows its parent domain's (mail.google.com -> google.com)");
            Ok(SiteIcons.Pick("https://google.com.evil.example/") != "favicon", "fallback: ... but never another site's (google.com.evil.example)");
            var e1 = SiteIcons.Element("https://www.twitch.tv/", 14); var e2 = SiteIcons.Element("https://discord.com/", 14); var e3 = SiteIcons.Element("https://emby.example.net/", 14);
            Ok((string)e1.Tag == "favicon" && e1 is System.Windows.Controls.Image && (string)e2.Tag == "brand" && (string)e3.Tag == "letter", "fallback: the drawn element follows the same order (favicon / brand / letter)");
            var tile = e3 as System.Windows.Controls.Border; var tt = tile == null ? null : tile.Child as System.Windows.Controls.TextBlock;
            Ok(tt != null && tt.Text == "E" && tile.Width == 14, "fallback: the letter tile shows the site's first letter");
            SiteIcons.Fake = new Dictionary<string, byte[]> { { "made.example", Png(16, 1, 1, 1) } };
            Ok(SiteIcons.Pick("https://made.example/") == "favicon" && SiteIcons.Pick("https://www.twitch.tv/") == "brand:twitch", "fallback: --snap uses only its made-up icons (the cache isn't read)");
            SiteIcons.Fake = null; SiteIcons.Forget();
        }

        // Chromium's Favicons layout (made-up rows)
        public static byte[] FaviconsDb(List<Tuple<long, string>> mapping, List<Tuple<long, int, byte[]>> bitmaps)
        {
            var fav = new TinyTable { Name = "favicons", Sql = "CREATE TABLE favicons(id INTEGER PRIMARY KEY,url LONGVARCHAR NOT NULL,icon_type INTEGER DEFAULT 1)" };
            foreach (var id in bitmaps.Select(b => b.Item1).Distinct()) fav.Rows.Add(new KeyValuePair<long, object[]>(id, new object[] { null, "https://icon" + id + ".example/favicon.ico", 1L }));
            var map = new TinyTable { Name = "icon_mapping", Sql = "CREATE TABLE icon_mapping(id INTEGER PRIMARY KEY,page_url LONGVARCHAR NOT NULL,icon_id INTEGER,page_url_type INTEGER NOT NULL DEFAULT 0)" };
            long mid = 1; foreach (var m in mapping) map.Rows.Add(new KeyValuePair<long, object[]>(mid++, new object[] { null, m.Item2, m.Item1, 0L }));
            var bmp = new TinyTable { Name = "favicon_bitmaps", Sql = "CREATE TABLE favicon_bitmaps(id INTEGER PRIMARY KEY,icon_id INTEGER NOT NULL,last_updated INTEGER DEFAULT 0,image_data BLOB,width INTEGER DEFAULT 0,height INTEGER DEFAULT 0,last_requested INTEGER NOT NULL DEFAULT 0)" };
            long bid = 1; foreach (var b in bitmaps) bmp.Rows.Add(new KeyValuePair<long, object[]>(bid++, new object[] { null, b.Item1, 13370000000000000L, b.Item3, (long)b.Item2, (long)b.Item2, 0L }));
            var meta = new TinyTable { Name = "meta", Sql = "CREATE TABLE meta(key LONGVARCHAR NOT NULL UNIQUE PRIMARY KEY, value LONGVARCHAR)" };
            meta.Rows.Add(new KeyValuePair<long, object[]>(1, new object[] { "version", "8" }));
            return TinySqlite.Write(4096, new List<TinyTable> { meta, fav, map, bmp });
        }
        public static byte[] SampleFavicons()
        {
            return FaviconsDb(
                new List<Tuple<long, string>> { Tuple.Create(1L, "https://www.wanted.example/"), Tuple.Create(2L, "https://wanted.example/some/deep/page?x=1"), Tuple.Create(2L, "https://other.example/x"),
                    Tuple.Create(3L, "https://notwanted.example/"), Tuple.Create(4L, "https://emptyicon.example/") },
                new List<Tuple<long, int, byte[]>> { Tuple.Create(1L, 16, Png(16, 200, 0, 0)), Tuple.Create(1L, 32, Png(32, 0, 200, 0)), Tuple.Create(1L, 64, Png(64, 0, 0, 200)),
                    Tuple.Create(2L, 16, Png(16, 10, 20, 30)), Tuple.Create(3L, 32, Png(32, 99, 99, 99)), Tuple.Create(4L, 0, new byte[0]) });
        }

        static void FaviconsFileTests(string root)
        {
            var r = new SqliteReader(SampleFavicons());
            var got = Importer.ChromiumFavicons(r, new[] { "wanted.example", "other.example", "emptyicon.example" });
            Ok(got.ContainsKey("wanted.example") && got.ContainsKey("other.example") && !got.ContainsKey("notwanted.example"), "Favicons file: icons for the wanted sites only (" + string.Join(", ", got.Keys) + ")");
            Ok(got.ContainsKey("wanted.example") && Info(got["wanted.example"])[0] == 32 && Info(got["wanted.example"])[3] == 200, "Favicons file: the 32 px bitmap is picked of 16 / 32 / 64, from the home page's icon");
            Ok(got.ContainsKey("other.example") && Info(got["other.example"])[2] == 10, "Favicons file: a site whose icon is shared with another page");
            Ok(!got.ContainsKey("emptyicon.example"), "Favicons file: an empty bitmap row is skipped");
            Eq(Importer.ChromiumFavicons(r, new string[0]).Count, 0, "Favicons file: nothing wanted -> nothing read");
            var big = FaviconsDb(Enumerable.Range(0, 400).Select(i => Tuple.Create((long)(i + 1), "https://site" + i + ".example/")).ToList(), Enumerable.Range(0, 400).Select(i => Tuple.Create((long)(i + 1), 16, Png(16, (byte)i, 1, 1))).ToList());
            var g2 = Importer.ChromiumFavicons(new SqliteReader(big), Enumerable.Range(0, 400).Where(i => i % 2 == 0).Select(i => "site" + i + ".example").ToList());
            Ok(g2.Count == 200, "Favicons file: 400 sites over many pages (b-tree interior pages), 200 wanted -> " + g2.Count);
        }

        // fake browser profiles: Brave (the user's bookmarks came from it) and Chrome (other bookmarks)
        static string FakeBrowsers(string root, out string braveFavicons)
        {
            var b = Path.Combine(root, "Local", "BraveSoftware", "Brave-Browser", "User Data", "Default"); Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(b, "Bookmarks"), "{\"roots\":{\"bookmark_bar\":{\"type\":\"folder\",\"name\":\"Bookmarks bar\",\"children\":[{\"type\":\"url\",\"name\":\"Wanted\",\"url\":\"https://www.wanted.example/\"},{\"type\":\"url\",\"name\":\"Other\",\"url\":\"https://other.example/x\"}]},\"other\":{\"type\":\"folder\",\"name\":\"Other bookmarks\",\"children\":[]}}}");
            braveFavicons = Path.Combine(b, "Favicons"); File.WriteAllBytes(braveFavicons, SampleFavicons());
            var c = Path.Combine(root, "Local", "Google", "Chrome", "User Data", "Default"); Directory.CreateDirectory(c);
            File.WriteAllText(Path.Combine(c, "Bookmarks"), "{\"roots\":{\"bookmark_bar\":{\"type\":\"folder\",\"name\":\"Bookmarks bar\",\"children\":[{\"type\":\"url\",\"name\":\"Elsewhere\",\"url\":\"https://elsewhere.example/\"}]}}}");
            File.WriteAllBytes(Path.Combine(c, "Favicons"), FaviconsDb(new List<Tuple<long, string>> { Tuple.Create(1L, "https://notwanted.example/") }, new List<Tuple<long, int, byte[]>> { Tuple.Create(1L, 16, Png(16, 5, 5, 5)) }));
            return root;
        }

        static void OnceTests(string root)
        {
            Fresh(root, "once");
            string braveFav; var fake = FakeBrowsers(Path.Combine(root, "browsers"), out braveFav);
            var before = File.ReadAllBytes(braveFav); var stamp = File.GetLastWriteTimeUtc(braveFav);
            var found = Importer.Find(Path.Combine(fake, "Local"), Path.Combine(fake, "Roaming"));
            Ok(found.Count == 2 && found.Any(p => p.Browser == "Brave") && found.Any(p => p.Browser == "Chrome"), "logos once: the fake Brave and Chrome profiles are found");
            var bm = new List<string> { "https://www.wanted.example/", "https://other.example/x", "https://mine-only.example/" };
            var hist = new List<string> { "https://notwanted.example/" };   // in Hallow's history: wanted too
            var tempsBefore = Directory.GetDirectories(Path.GetTempPath(), "HallowBrowserImport-*").Length;

            // a file in use is reported, not forced (Brave open = next start)
            using (new FileStream(braveFav, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var busy = Importer.LogosFromBrowsers(found, bm, hist, true, true);
                Ok(busy.InUse && busy.Saved == 0 && SiteIcons.Count() == 0, "logos once: Brave's icon file in use -> nothing read, \"in use\" (tried again next start)");
            }
            var r = Importer.LogosFromBrowsers(found, bm, hist, true, true);
            Ok(r.Saved == 3 && r.From.SequenceEqual(new[] { "Brave" }) && !r.InUse, "logos once: 3 logos from Brave (where the bookmarks came from): " + r.Saved + " from " + string.Join(",", r.From) + " " + r.Skipped);
            Ok(SiteIcons.HasExact("wanted.example") && SiteIcons.HasExact("other.example") && SiteIcons.HasExact("notwanted.example") && !SiteIcons.HasExact("mine-only.example") && !SiteIcons.HasExact("emptyicon.example"), "logos once: only Hallow's bookmark and history sites (Brave's other icons are left alone)");
            Ok(Info(SiteIcons.Png("https://notwanted.example/"))[2] == 99, "logos once: only from the matching browser (Chrome's bookmarks didn't match: its icon file wasn't used)");
            Ok(File.ReadAllBytes(braveFav).SequenceEqual(before) && File.GetLastWriteTimeUtc(braveFav) == stamp, "logos once: Brave's own Favicons file is untouched (a copy was read)");
            Ok(Directory.GetDirectories(Path.GetTempPath(), "HallowBrowserImport-*").Length <= tempsBefore, "logos once: the temp copy is deleted afterwards");
            var again = Importer.LogosFromBrowsers(found, bm, hist, true, true);
            Ok(again.Saved == 0 && again.Skipped == "the logo cache isn't empty", "logos once: not again once the cache has logos (" + again.Skipped + ")");
            Ok(Importer.LogosFromBrowsers(found, new List<string>(), hist, true, false).Skipped == "no bookmarks", "logos once: no bookmarks -> nothing");
            Fresh(root, "once2");
            var nomatch = Importer.LogosFromBrowsers(found, new List<string> { "https://never.example/" }, hist, true, true);
            Ok(nomatch.Saved == 0 && nomatch.Skipped == "your bookmarks didn't come from a browser on this PC", "logos once: bookmarks that came from no browser here -> no browser's icons are read");
            var button = Importer.LogosFromBrowsers(found, new List<string> { "https://never.example/" }, hist, false, false);
            Ok(button.Saved == 1 && SiteIcons.HasExact("notwanted.example"), "logos: the Settings button takes any browser's icons, still only for Hallow's sites (" + button.Saved + ")");
            Ok(!File.Exists(Path.Combine(Path.GetDirectoryName(braveFav), "Login Data")) && !found.Any(p => File.Exists(Path.Combine(p.Dir, "Cookies"))), "logos: the fake profiles have no passwords or cookies to read (and the code reads only Bookmarks and Favicons)");

            // a normal import brings the logos too
            Fresh(root, "import");
            var store = new BookmarkStore(Path.Combine(root, "import-bm.json")); store.Load(); store.Frozen = true;
            var hs = new HistoryStore(Path.Combine(root, "import-h.json"));
            var brave = found.First(p => p.Browser == "Brave");
            var ir = Importer.Run(brave, true, false, store, hs, true);
            Ok(ir.Bookmarks == 2 && ir.Logos == 2 && SiteIcons.HasExact("wanted.example"), "import: bookmarks and their 2 logos (" + ir.Bookmarks + " bookmarks, " + ir.Logos + " logos)");
        }

        static void FetchRuleTests(string root)
        {
            Fresh(root, "fetch");
            Eq(BrowserWindow.LogoOrigin("http://example.com/a/b?c"), "https://example.com", "fetch: an internet site is asked over https only");
            Eq(BrowserWindow.LogoOrigin("http://192.168.1.20:8096/web/index.html"), "http://192.168.1.20:8096", "fetch: a NAS on the LAN keeps its http address and port");
            Eq(BrowserWindow.LogoOrigin("https://supabase.com/dashboard/project/x"), "https://supabase.com", "fetch: the site's root, nothing of the page address");
            Ok(BrowserWindow.LogoOrigin("hallow://history") == null, "fetch: never an internal page");
            var now = DateTime.Now;
            LogoTried.Mark("nothing.example", false, now);
            Ok(LogoTried.Recent("nothing.example", now.AddDays(29)) && !LogoTried.Recent("nothing.example", now.AddDays(31)), "fetch: a site without an icon is tried again after 30 days at the earliest (once per site)");
            LogoTried.Reset();
            Ok(LogoTried.Recent("nothing.example", now.AddDays(1)), "fetch: ... remembered across restarts (favicons\\tried.txt)");
            LogoTried.Mark("nothing.example", true, now);
            Ok(!LogoTried.Recent("nothing.example", now), "fetch: found later -> forgotten");
            var src = File.ReadAllText(Path.Combine(FindRepo(), "src", "Logos114.cs"));
            Ok(!src.Contains("s2/favicons") && !src.Contains("icons.duckduckgo") && !src.Contains("favicon.yandex") && !src.Contains("faviconkit") && !src.Contains("icon.horse"), "fetch: no third-party favicon service anywhere in the code");
        }
        static string FindRepo() { var d = AppDomain.CurrentDomain.BaseDirectory; for (int i = 0; i < 4 && d != null; i++) { if (Directory.Exists(Path.Combine(d, "src"))) return d; d = Path.GetDirectoryName(d.TrimEnd('\\')); } return "."; }

        static void NewTabTests(string root)
        {
            Fresh(root, "newtab");
            SiteIcons.Save("https://tile.example/", Png(16, 1, 99, 1), false);
            var s = new BrowserSettings(); s.Fix(); s.NewTabFamily = false; s.GameCorner = false; s.DefaultHintDismissed = true;
            s.NewTabPinned = new List<PinnedSite> { new PinnedSite { Url = "https://tile.example/", Title = "Tile" }, new PinnedSite { Url = "https://noicon.example/", Title = "No icon" } };
            var json = NewTabData.Build(s, new List<HistoryEntry>(), null, DateTime.Now);
            Ok(json.Contains("\"i\":\"ico/" + SiteIcons.FileName("tile.example") + "?v=") && File.Exists(Path.Combine(Engine.NewTabDir, "ico", SiteIcons.FileName("tile.example"))), "new tab: a tile gets its logo, copied beside the page");
            Ok(json.Split(new[] { "\"u\":" }, StringSplitOptions.None).Length == 3 && json.IndexOf("\"i\":", json.IndexOf("noicon.example")) < 0, "new tab: a site without one keeps its letter tile");
            var html = Engine.NewTabHtml("duckduckgo", "#7C3AED");
            Ok(html.Contains("s.i.indexOf('ico/')===0") && html.Contains("im.onerror"), "new tab: the page draws the logo - only from its own ico/ folder - and the letter if it can't");
        }

        static void SettingTests(string root)
        {
            var f = Path.Combine(root, "settings-old.json"); File.WriteAllText(f, "{\"Search\":\"duckduckgo\"}");
            var s = BrowserSettings.LoadFrom(f);
            Ok(s.FetchLogos && !s.Logos114 && s.Logos114Tries == 0, "setting: \"Fetch missing site logos\" is on by default for a 1.13 settings file; the one-time import hasn't run");
        }
    }
}
