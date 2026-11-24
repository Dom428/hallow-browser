// Hallow Browser 1.15.1 - unit tests: Steam names from a FAKE Steam folder (library folders, app manifests, a made-up appinfo.vdf in
// Steam's binary formats 27 / 28 / 29, the cache, the "Steam game <id>" fallback, Hub's game list), speed-dial duplicates (normalised
// addresses, gmail.com = mail.google.com, pins never removed), Shield's count on the new tab page (Hallow Browser's own entry, its
// local day) and the page's layout rules (nothing fixed over the content). Test folder only; never the real Steam or Shield files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class V1151
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }
        public static void All() { SteamTests(); DedupeTests(); ShieldTests(); LayoutTests(); SearchTests(); }

        // 1.15.1: Google for new installs; a saved choice (any engine, DuckDuckGo too) is kept
        static void SearchTests()
        {
            var f = Path.Combine(BrowserSettings.Dir, "settings.json"); var keep = File.Exists(f) ? File.ReadAllText(f) : null;
            try
            {
                if (File.Exists(f)) File.Delete(f);
                Eq(BrowserSettings.Load().Search, "google", "search 1.15.1: a new install searches with Google");
                Eq(new BrowserSettings().Search, "google", "search 1.15.1: the default is Google");
                foreach (var e in new[] { "duckduckgo", "bing", "brave", "google" })
                {
                    File.WriteAllText(f, "{\"Search\":\"" + e + "\",\"SleepMinutes\":15}");
                    Eq(BrowserSettings.Load().Search, e, "search 1.15.1: a saved choice is kept (" + e + ")");
                }
                File.WriteAllText(f, "{\"SleepMinutes\":15}");
                Eq(BrowserSettings.Load().Search, "duckduckgo", "search 1.15.1: settings from before without a search engine keep DuckDuckGo (what they had)");
                Ok(Engine.NewTabHtml("google", "#7C3AED").Contains("placeholder=\"Search Google or type an address\""), "search 1.15.1: the new tab says \"Search Google or type an address\"");
                Ok(BrowserSettings.Engines.Contains("duckduckgo") && BrowserSettings.Engines.Contains("bing") && BrowserSettings.Engines.Contains("brave"), "search 1.15.1: DuckDuckGo, Bing and Brave are still there to pick");
            }
            finally { if (keep != null) File.WriteAllText(f, keep); else if (File.Exists(f)) File.Delete(f); }
        }

        // ---- a made-up appinfo.vdf
        class KV { public byte Type; public string Key; public object Value; public KV(byte t, string k, object v) { Type = t; Key = k; Value = v; } }
        static void WriteKv(BinaryWriter w, List<KV> map, List<string> table)
        {
            foreach (var e in map)
            {
                w.Write(e.Type);
                if (table != null) { var i = table.IndexOf(e.Key); if (i < 0) { table.Add(e.Key); i = table.Count - 1; } w.Write(i); }
                else { w.Write(Encoding.UTF8.GetBytes(e.Key)); w.Write((byte)0); }
                switch (e.Type)
                {
                    case 0: WriteKv(w, (List<KV>)e.Value, table); break;
                    case 1: w.Write(Encoding.UTF8.GetBytes((string)e.Value)); w.Write((byte)0); break;
                    case 2: w.Write((int)e.Value); break;
                    case 7: w.Write((ulong)e.Value); break;
                    case 5: w.Write(Encoding.Unicode.GetBytes((string)e.Value)); w.Write((short)0); break;
                }
            }
            w.Write((byte)8);
        }
        public static byte[] AppInfo(int ver, params Tuple<uint, string>[] apps)
        {
            var table = ver == 29 ? new List<string>() : null;
            using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms))
            {
                w.Write(ver == 29 ? 0x07564429u : ver == 28 ? 0x07564428u : 0x07564427u); w.Write(1u);
                if (ver == 29) w.Write(0L);   // the string table's offset, set at the end
                foreach (var a in apps)
                {
                    var kv = new List<KV> { new KV(0, "appinfo", new List<KV> { new KV(2, "appid", (int)a.Item1),
                        new KV(0, "common", new List<KV> { new KV(7, "gameid", (ulong)a.Item1), new KV(0, "small_capsule", new List<KV> { new KV(1, "english", "x.jpg"), new KV(1, "name", "not this one") }),
                            new KV(5, "wide", "w"), new KV(1, "type", "Game"), new KV(1, "name", a.Item2) }),
                        new KV(0, "extended", new List<KV> { new KV(1, "developer", "Someone") }) }) };
                    byte[] body; using (var bm = new MemoryStream()) using (var bw = new BinaryWriter(bm))
                    {
                        bw.Write(2u); bw.Write(1700000000u); bw.Write(0UL); bw.Write(new byte[20]); bw.Write(12345u); if (ver >= 28) bw.Write(new byte[20]);
                        WriteKv(bw, kv, table); bw.Flush(); body = bm.ToArray();
                    }
                    w.Write(a.Item1); w.Write((uint)body.Length); w.Write(body);
                }
                w.Write(0u);
                if (ver == 29)
                {
                    var off = ms.Position; w.Write((uint)table.Count); foreach (var s in table) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
                    w.Flush(); ms.Position = 8; w.Write(off);
                }
                w.Flush(); return ms.ToArray();
            }
        }

        static void SteamTests()
        {
            var root = Path.Combine(BrowserSettings.TestDir, "FakeSteam"); var lib2 = Path.Combine(BrowserSettings.TestDir, "FakeLibrary");
            if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(lib2)) Directory.Delete(lib2, true);
            Directory.CreateDirectory(Path.Combine(root, "steamapps")); Directory.CreateDirectory(Path.Combine(root, "appcache")); Directory.CreateDirectory(Path.Combine(lib2, "steamapps"));
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + root.Replace("\\", "\\\\") + "\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"0\"\n\t\t}\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib2.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
            File.WriteAllText(Path.Combine(lib2, "steamapps", "appmanifest_9000001.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"9000001\"\n\t\"name\"\t\t\"Made-up \\\"Quoted\\\" Game\"\n\t\"installdir\"\t\t\"x\"\n}\n");
            File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_9000002.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"9000002\"\n\t\"name\"\t\t\"Root Library Game\"\n}\n");
            File.WriteAllBytes(Path.Combine(root, "appcache", "appinfo.vdf"), AppInfo(29, Tuple.Create(9000005u, "Skipped Game"), Tuple.Create(9000003u, "Cached Only Game é"), Tuple.Create(9000006u, "Other")));
            var libs = SteamNames.Libraries(root);
            Ok(libs.Count == 2 && libs.Any(x => x == lib2), "steam names 1.15.1: every library folder from libraryfolders.vdf (" + string.Join(" ; ", libs) + ")");
            Eq(SteamNames.FromManifest(lib2, "9000001"), "Made-up \"Quoted\" Game", "steam names 1.15.1: the name from appmanifest_<id>.acf (escaped quotes)");
            foreach (var v in new[] { 27, 28, 29 })
            {
                var r = SteamNames.FromAppInfo(AppInfo(v, Tuple.Create(1u, "A"), Tuple.Create(1422450u, "Deadlock"), Tuple.Create(1966720u, "Lethal Company")), new HashSet<string> { "1422450", "1966720", "77" });
                Ok(r.Count == 2 && r["1422450"] == "Deadlock" && r["1966720"] == "Lethal Company", "steam names 1.15.1: appinfo.vdf version " + v + " - appinfo > common > name (not a nested 'name'), other apps skipped");
            }
            Ok(SteamNames.FromAppInfo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 }, new HashSet<string> { "1" }).Count == 0 && SteamNames.FromAppInfo(AppInfo(29, Tuple.Create(5u, "X")).Take(40).ToArray(), new HashSet<string> { "5" }).Count == 0, "steam names 1.15.1: a broken or cut appinfo.vdf gives nothing (no crash)");
            SteamNames.Forget(); try { File.Delete(SteamNames.CachePath); } catch { }
            var names = SteamNames.Resolve(new[] { "9000001", "9000002", "9000003", "9000004" }, root);
            Ok(names.Count == 3 && names["9000001"].StartsWith("Made-up") && names["9000002"] == "Root Library Game" && names["9000003"] == "Cached Only Game é", "steam names 1.15.1: manifests in any library, then Steam's app cache; an unknown id left out");
            var reads = SteamNames.AppInfoReads; SteamNames.Resolve(new[] { "9000004" }, root); SteamNames.Resolve(new[] { "9000004" }, root);
            Ok(SteamNames.AppInfoReads - reads <= 1, "steam names 1.15.1: a game Steam has no name for doesn't make appinfo.vdf be read again and again");
            File.Delete(Path.Combine(root, "appcache", "appinfo.vdf")); SteamNames.Forget();
            Eq(SteamNames.Resolve(new[] { "9000003" }, root).ContainsKey("9000003") ? SteamNames.Resolve(new[] { "9000003" }, root)["9000003"] : null, "Cached Only Game é", "steam names 1.15.1: a name found before is kept (steamnames.json) when Steam's files no longer have it");
            Ok(File.Exists(SteamNames.CachePath) && SteamNames.CachePath.StartsWith(BrowserSettings.TestDir), "steam names 1.15.1: the cache is in the browser's own folder (tests: the test folder)");
            // Hub's list: a game known only by its id gets its name; none anywhere = "Steam game <id>"
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_STEAM_ROOT", root);
            try
            {
                var hub = "{\"Games\":{\"steam:9000001\":{\"Name\":\"\",\"CustomName\":\"\",\"Hidden\":false,\"LastPlayed\":\"\"},\"steam:9000099\":{\"Hidden\":false},\"steam:2507950\":{\"Name\":\"Delta Force\",\"LastPlayed\":\"2026-11-20T21:00:00\"}}}";
                var g = GameCorner.HubGames(hub, null);
                Eq(string.Join(",", g.Select(x => x.AppId + ":" + x.Name)), "2507950:Delta Force,9000001:Made-up \"Quoted\" Game,9000099:Steam game 9000099", "steam names 1.15.1: Hub's games without a name get Steam's (no more \"App 1422450\"); none anywhere = \"Steam game <id>\"");
                Ok(!g.Any(x => x.Name.StartsWith("App ")), "steam names 1.15.1: never \"App <id>\"");
                var hd = Path.Combine(BrowserSettings.TestDir, "HubFake"); Directory.CreateDirectory(Path.Combine(hd, "meta", "9000099")); File.WriteAllBytes(Path.Combine(hd, "meta", "9000099", "shot0.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
                var fg = NewTabFamily.Games(hub, null, hd);
                Ok(fg.Any(x => x.Id == "9000099" && x.Name == "Steam game 9000099" && x.PicSrc != null), "steam names 1.15.1: a game without a name still shows its cover");
            }
            finally { Environment.SetEnvironmentVariable("HALLOW_BROWSER_STEAM_ROOT", null); }
        }

        static void DedupeTests()
        {
            Eq(TileKeys.Key("https://mail.google.com/mail/u/0/#inbox"), TileKeys.Key("https://gmail.com/"), "tiles 1.15.1: gmail.com and Gmail's inbox are one tile");
            Eq(TileKeys.Key("https://mail.google.com/mail/u/0/?tab=rm&ogbl#inbox/FMfcg"), TileKeys.Key("https://mail.google.com/mail/u/0/#inbox"), "tiles 1.15.1: a mail page's query and #fragment don't matter");
            Ok(TileKeys.Key("https://mail.google.com/mail/u/1/#inbox") != TileKeys.Key("https://mail.google.com/mail/u/0/#inbox"), "tiles 1.15.1: a second Google account stays its own tile");
            Eq(TileKeys.Key("http://www.example.com/a/"), TileKeys.Key("https://example.com/a#top"), "tiles 1.15.1: www., http / https, a trailing / and the #fragment don't matter");
            Ok(TileKeys.Key("https://example.com/watch?v=1") != TileKeys.Key("https://example.com/watch?v=2") && TileKeys.Key("https://example.com/a") != TileKeys.Key("https://example.com/b"), "tiles 1.15.1: other pages of an ordinary site (path or query) stay apart");
            Ok(TileKeys.Key("http://192.168.68.62:8096/web/index.html#!/home") == TileKeys.Key("http://192.168.68.62:8096/web/") && TileKeys.Key("http://192.168.68.62:8096/") != TileKeys.Key("http://192.168.68.62:9443/"), "tiles 1.15.1: index.html and #routes are one page; another port is another site");

            var now = DateTime.Now; var t = now.ToString("s");
            var h = new List<HistoryEntry> {
                new HistoryEntry { Url = "https://mail.google.com/mail/u/0/#inbox", Title = "Inbox (121) - someone@example.com - Gmail", Visits = 50, Last = t },
                new HistoryEntry { Url = "https://gmail.com/", Title = "Inbox (121) - someone@example.com - Gmail", Visits = 30, Last = t },
                new HistoryEntry { Url = "https://ext.example/", Title = "Ext", Visits = 20, Last = t },
                new HistoryEntry { Url = "https://dash.example/dashboard", Title = "Dash", Visits = 10, Last = t } };
            var s = new BrowserSettings(); s.Fix(); s.NewTabFamily = false;
            Func<string, List<Dictionary<string, object>>> tiles = j => ((System.Collections.ArrayList)new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(j)["tiles"]).Cast<Dictionary<string, object>>().ToList();
            var a = tiles(NewTabData.Build(s, h, null, now));
            Ok(a.Count == 3 && a.Count(x => ((string)x["t"]).StartsWith("Inbox")) == 1 && (string)a[0]["u"] == "https://mail.google.com/mail/u/0/#inbox", "tiles 1.15.1: two most-visited Gmail tiles (gmail.com and the inbox) become one, the more visited address kept (" + string.Join(" | ", a.Select(x => x["u"])) + ")");
            s.NewTabPinned.Add(new PinnedSite { Url = "https://gmail.com/", Title = "My mail" }); s.NewTabPinned.Add(new PinnedSite { Url = "https://mail.google.com/mail/u/0/", Title = "Mail again" });
            s.NewTabPinned.Add(new PinnedSite { Url = "https://dash.example/dashboard?x=1", Title = "Dash pinned" });
            var b = tiles(NewTabData.Build(s, h, null, now));
            Ok(b.Count(x => (bool)x["pin"]) == 2 && (string)b[0]["t"] == "My mail" && !b.Any(x => ((string)x["t"]).StartsWith("Inbox")) && b.Count(x => ((string)x["t"]).StartsWith("Dash")) == 2, "tiles 1.15.1: a pinned tile wins over a most-visited duplicate; two pins of one page shown once; a pin with another ?query is another page (" + string.Join(" | ", b.Select(x => x["t"])) + ")");
            Eq(s.NewTabPinned.Count, 3, "tiles 1.15.1: pinned tiles are never removed from your settings by the merge");
            var s2 = new BrowserSettings(); s2.Fix(); s2.NewTabFamily = false; s2.NewTabHidden.Add("https://mail.google.com/mail/u/0/#inbox");
            var c = tiles(NewTabData.Build(s2, h, null, now));
            Ok(!c.Any(x => ((string)x["t"]).StartsWith("Inbox")), "tiles 1.15.1: a removed tile doesn't come back under its other address (gmail.com)");
        }

        static string Status(params string[] entries) { return "{\"version\":\"1.7\",\"updated\":\"" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "\",\"browsers\":[" + string.Join(",", entries) + "],\"blockedToday\":999,\"blockedWeek\":5000,\"bannersToday\":0}"; }
        static string Entry(string name, string id, long today, DateTime updatedUtc, string day) { return "{\"name\":\"" + name + "\",\"profile\":\"Default\",\"version\":\"1.8\",\"level\":\"optimal\",\"blockedToday\":" + today + ",\"blockedWeek\":473,\"bannersToday\":0,\"updated\":\"" + updatedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "\",\"day\":\"" + day + "\",\"id\":\"" + id + "\"}"; }
        static void ShieldTests()
        {
            var nowUtc = DateTime.UtcNow; var today = DateTime.Now.ToString("yyyy-MM-dd"); var yday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            var st = Status(Entry("Hallow Browser", "923191748e460448", 37, nowUtc.AddMinutes(-3), today), Entry("Chrome", "c0ffee00c0ffee00", 962, nowUtc.AddMinutes(-1), today));
            Eq(NewTabShield.BlockedToday(st, nowUtc), 37L, "shield 1.15.1: the new tab page shows Hallow Browser's own count, not every browser's sum (999)");
            var strip = NewTabData.Strip(null, st, null, DateTime.Now);
            Ok(strip.Count == 1 && strip[0]["v"] == "37 blocked today", "shield 1.15.1: the SHIELD chip reads Hallow Browser's entry (" + (strip.Count > 0 ? strip[0]["v"] : "none") + ")");
            Eq(NewTabShield.BlockedToday(Status(Entry("Hallow Browser", "a1", 120, nowUtc.AddHours(-3), yday)), nowUtc), 0L, "shield 1.15.1: a count from yesterday (this PC's local day) is 0 today");
            Eq(NewTabShield.BlockedToday(Status(Entry("Hallow Browser", "a1", 120, nowUtc.AddDays(-3), yday)), nowUtc), null, "shield 1.15.1: no word for days - not shown rather than a wrong number");
            Eq(NewTabShield.BlockedToday(Status(Entry("Chrome", "c1", 5, nowUtc, today)), nowUtc), null, "shield 1.15.1: no Hallow Browser entry = no chip");
            Ok(NewTabData.Strip(null, Status(Entry("Chrome", "c1", 5, nowUtc, today)), null, DateTime.Now).Count == 0, "shield 1.15.1: other browsers' counts aren't shown as the browser's");
            var rep = "{\"blockedToday\":41,\"blockedWeek\":500,\"bannersToday\":1,\"level\":\"optimal\",\"version\":\"1.8\",\"browser\":\"Hallow Browser\",\"profile\":\"923191748e460448\",\"day\":\"" + today + "\"}";
            Eq(NewTabShield.FromReport(rep, nowUtc), 41L, "shield 1.15.1: the browser's own latest report (fresher than the file)");
            Ok(NewTabData.Strip(null, st, null, DateTime.Now, rep)[0]["v"] == "41 blocked today", "shield 1.15.1: the chip prefers that report");
            Eq(NewTabShield.FromReport(rep.Replace(today, yday), nowUtc), 0L, "shield 1.15.1: yesterday's report = 0 today");
            Eq(NewTabShield.FromReport("{\"blockedToday\":null,\"day\":\"" + today + "\"}", nowUtc), null, "shield 1.15.1: blocked counts off = no number");
        }

        static void LayoutTests()
        {
            foreach (var light in new[] { false, true })
            {
                var html = Engine.NewTabHtml("duckduckgo", "#7C3AED", light);
                var css = html.Substring(html.IndexOf("<style>"), html.IndexOf("</style>") - html.IndexOf("<style>"));
                Ok(!css.Contains("position:fixed") && !css.Contains("position:absolute;top:14px"), "layout 1.15.1 (" + (light ? "light" : "dark") + "): nothing on the new tab page is pinned over the content");
                Ok(html.Contains("<footer><div id=fam></div><div id=t></div></footer>") && html.IndexOf("<footer>") > html.IndexOf("<div id=gc></div></main>"), "layout 1.15.1: the Hallow strip and the time are a footer after everything");
                Ok(css.Contains("main{margin:auto 0") && css.Contains("min-height:100%") && !css.Contains("justify-content:center;background"), "layout 1.15.1: centred when it fits, scrolls when it doesn't (no clipped top)");
                Ok(css.Contains("@media (max-height:760px)") && css.Contains("@media (min-width:2400px)"), "layout 1.15.1: tighter when short, larger on 4K");
                Ok(css.Contains(".hr{display:flex;gap:10px;overflow-x:auto") && css.Contains("-webkit-line-clamp:2"), "layout 1.15.1: the games row scrolls sideways, titles get two lines");
            }
        }
    }
}
