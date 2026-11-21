// Hallow Browser 1.4 - unit tests: where a dragged tab goes, HTTPS-only rules and its page, secure DNS in Local State, site rules and
// permission names, Shield's patched manifest and the status.json relay (merge with other browsers, sums), unpacked extension
// manifests, the game corner's Hub library / Steam news / Epic free games parsing (stand-in data in the real shapes), the private
// window's palette contrast and new tab page, sessions with more windows, and 1.4's settings. Temp folders only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser.Tests
{
    public static class V14
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static string root;

        public static void All()
        {
            root = Path.Combine(Path.GetTempPath(), "HallowBrowser14-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
            try { TearTests(); HttpsTests(); DnsTests(); SiteTests(); ShieldPatchTests(); ShieldStatusTests(); ExtTests(); CornerTests(); PrivateTests(); SessionTests(); SettingsTests(); }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        static void TearTests()
        {
            var own = new Rect(100, 0, 800, 38); var others = new List<Rect> { new Rect(1200, 300, 700, 38), Rect.Empty };
            Eq(TearRules.Decide(new Point(400, 20), own, others), -2, "drag: inside the strip = a reorder");
            Eq(TearRules.Decide(new Point(400, 70), own, others), -2, "drag: just under the strip (within 40 px) is still a reorder");
            Eq(TearRules.Decide(new Point(400, 300), own, others), -1, "drag: far below the strip = a new window");
            Eq(TearRules.Decide(new Point(1300, 320), own, others), 0, "drag: over another window's strip = into that window");
            Eq(TearRules.Decide(new Point(1300, 345), own, others), 0, "drag: a little under the other strip still counts (14 px)");
            Eq(TearRules.Decide(new Point(1300, 420), own, others), -1, "drag: over the other window's page (not its strip) = a new window");
            Eq(TearRules.Decide(new Point(10, 10), Rect.Empty, new List<Rect>()), -1, "drag: no strip rect (hidden) = a new window");
            var heads = new List<Rect> { new Rect(0, 0, 100, 30), new Rect(100, 0, 100, 30), new Rect(200, 0, 100, 30) };
            Eq(TearRules.Slot(10, heads, false), 0, "drop slot: left half of the first tab = before it");
            Eq(TearRules.Slot(160, heads, false), 2, "drop slot: right half of the second tab = after it");
            Eq(TearRules.Slot(900, heads, false), 3, "drop slot: past the last tab = at the end");
            var vh = new List<Rect> { new Rect(0, 0, 200, 32), new Rect(0, 32, 200, 32) };
            Eq(TearRules.Slot(40, vh, true), 1, "drop slot (vertical tabs): by y");
        }

        static void HttpsTests()
        {
            Func<string, bool> none = h => false;
            Ok(HttpsRules.ShouldUpgrade("http://example.com/a?b=1", none), "https-only: http://example.com is tried as https");
            Ok(!HttpsRules.ShouldUpgrade("https://example.com/", none), "https-only: https stays");
            foreach (var u in new[] { "http://localhost:3000/", "http://192.168.1.1/", "http://10.0.0.5/admin", "http://[::1]/", "http://router/", "http://nas.local/", "http://box.lan/", "http://pi.home.arpa/", "http://app.localhost/" })
                Ok(!HttpsRules.ShouldUpgrade(u, none), "https-only: left alone - " + u);
            Ok(!HttpsRules.ShouldUpgrade("http://example.com:8080/", none), "https-only: a non-default port isn't guessed");
            Ok(HttpsRules.ShouldUpgrade("http://plain.test:8080/", none, "8080=8443"), "https-only: the test port pair is");
            Ok(!HttpsRules.ShouldUpgrade("http://old.example/", h => h == "old.example"), "https-only: a site you allowed over http stays http");
            Eq(HttpsRules.Upgrade("http://example.com/a?b=1#c"), "https://example.com/a?b=1#c", "https-only: the address keeps its path, query and fragment");
            Eq(HttpsRules.Upgrade("http://plain.test:8080/x", "8080=8443,81=444"), "https://plain.test:8443/x", "https-only: test port map");
            Eq(HttpsRules.MapPort(81, "8080=8443, 81=444"), 444, "https-only: port map parsing");
            Ok(HttpsRules.IsHttpsFailure(CoreWebView2WebErrorStatus.CertificateIsInvalid) && HttpsRules.IsHttpsFailure(CoreWebView2WebErrorStatus.CannotConnect) && HttpsRules.IsHttpsFailure(CoreWebView2WebErrorStatus.ConnectionReset), "https-only: certificate / connection errors ask");
            Ok(!HttpsRules.IsHttpsFailure(CoreWebView2WebErrorStatus.OperationCanceled) && !HttpsRules.IsHttpsFailure(CoreWebView2WebErrorStatus.HostNameNotResolved), "https-only: going elsewhere / no such host doesn't ask");
            var page = HttpsRules.Interstitial("evil<b>.example", "http://evil<b>.example/x?a=<script>", "it doesn't answer on a secure connection", false);
            Ok(page.Contains("This site doesn't support a secure connection") && page.Contains("hallow:https-continue") && page.Contains("hallow:https-back") && page.Contains("hallow:https-always"), "https-only: the page asks, with go back / continue / always");
            Ok(!page.Contains("<script>\"") && !page.Contains("evil<b>") && page.Contains("evil&lt;b&gt;"), "https-only: the address on the page is HTML-escaped");
            Ok(HttpsRules.Interstitial("a.example", "http://a.example/", "x", true).Contains("color-scheme content=light"), "https-only: the page follows light mode");
        }

        static void DnsTests()
        {
            Eq(Engine.DohTemplate("off"), null, "secure DNS: off = no template");
            Ok(Engine.DohTemplate("cloudflare").StartsWith("https://") && Engine.DohTemplate("quad9").Contains("quad9") && Engine.DohTemplate("google").Contains("dns.google"), "secure DNS: the three providers' templates");
            var j = Engine.LocalStateJson("{\"browser\":{\"x\":1},\"edge\":{\"y\":true}}", "https://dns.quad9.net/dns-query");
            var d = js.Deserialize<Dictionary<string, object>>(j); var doh = (Dictionary<string, object>)d["dns_over_https"];
            Ok((string)doh["mode"] == "secure" && (string)doh["templates"] == "https://dns.quad9.net/dns-query" && d.ContainsKey("browser") && d.ContainsKey("edge"), "secure DNS: Local State gets mode=secure + the template, the rest kept");
            Eq(Engine.LocalStateJson(j, "https://dns.quad9.net/dns-query"), null, "secure DNS: unchanged = not rewritten");
            var off = Engine.LocalStateJson(j, null); Ok(off != null && !off.Contains("dns_over_https") && off.Contains("browser"), "secure DNS: off removes the key (the runtime's own default again)");
            Eq(Engine.LocalStateJson("{\"a\":1}", null), null, "secure DNS: off with nothing set = file untouched");
            Eq(Engine.LocalStateJson("{broken", "https://x/"), null, "secure DNS: a broken Local State is left alone");
            Ok(Engine.LocalStateJson(null, "https://x/dns-query").Contains("secure"), "secure DNS: no Local State yet = a new one");
        }

        static void SiteTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(SiteRules.Find(s, "Example.COM") == null, "site rules: none at first");
            var r = SiteRules.Get(s, "Example.COM.", true); r.Popups = "allow";
            Ok(SiteRules.Find(s, "example.com") == r && r.Host == "example.com", "site rules: host keys are lower-case, trailing dot dropped");
            SiteRules.Get(s, "b.example", true); SiteRules.Tidy(s);
            Eq(s.Sites.Count, 1, "site rules: empty rules are tidied away");
            SiteRules.Reset(s, "EXAMPLE.com"); Eq(s.Sites.Count, 0, "site rules: reset removes the site");
            Ok(SiteRules.Prompted(CoreWebView2PermissionKind.Camera) && SiteRules.Prompted(CoreWebView2PermissionKind.Microphone) && SiteRules.Prompted(CoreWebView2PermissionKind.Geolocation) && SiteRules.Prompted(CoreWebView2PermissionKind.Notifications) && SiteRules.Prompted(CoreWebView2PermissionKind.ClipboardRead) && SiteRules.Prompted(CoreWebView2PermissionKind.MidiSystemExclusiveMessages), "permissions: camera, mic, location, notifications, clipboard, MIDI get the Hallow prompt");
            Ok(!SiteRules.Prompted(CoreWebView2PermissionKind.Autoplay) && !SiteRules.Prompted(CoreWebView2PermissionKind.PersistentStorage) && !SiteRules.Prompted(CoreWebView2PermissionKind.UnknownPermission), "permissions: autoplay / storage / unknown keep the engine's default");
            Eq(SiteRules.Wants(CoreWebView2PermissionKind.Geolocation), "know your location", "permissions: the prompt's words");
            Ok(Enum.GetValues(typeof(CoreWebView2PermissionKind)).Cast<CoreWebView2PermissionKind>().Where(SiteRules.Prompted).All(k => Icons.Has(SiteRules.Icon(k)) && SiteRules.Name(k) != "Other"), "permissions: every prompted kind has a name and an icon");
            Eq(SiteRules.OriginKey("HTTP://127.0.0.1:5000/"), "http://127.0.0.1:5000", "permissions: origin keys (the engine lists them with a slash)");
            Ok(Omni.IsInternal(Omni.SitesUrl) && Omni.IsInternal(Omni.ExtensionsUrl) && Omni.Interpret("hallow://settings/sites", "duckduckgo") == Omni.SitesUrl && Omni.Interpret("HALLOW://EXTENSIONS", "duckduckgo") == Omni.ExtensionsUrl, "hallow://settings/sites and hallow://extensions are browser pages");
        }

        static void ShieldPatchTests()
        {
            var mf = "{\"manifest_version\":3,\"name\":\"Hallow Shield\",\"version\":\"1.7\",\"key\":\"abc\",\"permissions\":[\"declarativeNetRequest\",\"storage\"],\"optional_permissions\":[\"declarativeNetRequestFeedback\",\"nativeMessaging\"]}";
            var p = js.Deserialize<Dictionary<string, object>>(ShieldHost.PatchManifest(mf));
            var perms = ((System.Collections.ArrayList)p["permissions"]).Cast<string>().ToList(); var opt = ((System.Collections.ArrayList)p["optional_permissions"]).Cast<string>().ToList();
            Ok(perms.Contains(ShieldHost.Feedback) && perms.Contains("storage") && !opt.Contains(ShieldHost.Feedback) && opt.Contains("nativeMessaging") && (string)p["key"] == "abc", "Shield's copy: the feedback permission becomes a required one, the rest (and the key = same id) unchanged");
            var p2 = js.Deserialize<Dictionary<string, object>>(ShieldHost.PatchManifest("{\"permissions\":[\"x\"],\"optional_permissions\":[\"declarativeNetRequestFeedback\"]}"));
            Ok(!p2.ContainsKey("optional_permissions"), "Shield's copy: an empty optional list is dropped");
            Eq(ShieldHost.PatchManifest("{not json"), null, "Shield's copy: a broken manifest = load the original");
            // the copy itself (from a fake Shield folder in a temp dir)
            var src = Path.Combine(root, "shield-src"); Directory.CreateDirectory(Path.Combine(src, "rules")); Directory.CreateDirectory(Path.Combine(src, "_metadata"));
            File.WriteAllText(Path.Combine(src, "manifest.json"), mf); File.WriteAllText(Path.Combine(src, "rules", "a.json"), "[]"); File.WriteAllText(Path.Combine(src, "_metadata", "x"), "1");
        }

        static ShieldEntry E(string name, string id, long? today, long? week, DateTime upd, string day) { return new ShieldEntry { Name = name, Profile = "Default", Id = id, Version = "1.7", Level = "optimal", BlockedToday = today, BlockedWeek = week, BannersToday = 1, Updated = upd, Day = day }; }
        static void ShieldStatusTests()
        {
            var now = DateTime.UtcNow; var today = now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var rep = ShieldStatus.FromReport("{\"blockedToday\":12,\"blockedWeek\":340,\"bannersToday\":2,\"level\":\"optimal\",\"version\":\"1.7\",\"browser\":\"Hallow Browser\",\"profile\":\"0a1b2c3d4e5f6071\",\"day\":\"" + today + "\",\"url\":\"https://secret.example/\",\"site\":\"x\"}", now);
            Ok(rep != null && rep.Name == "Hallow Browser" && rep.Id == "0a1b2c3d4e5f6071" && rep.BlockedToday == 12 && rep.BlockedWeek == 340 && rep.BannersToday == 2 && rep.Level == "optimal", "relay: Shield's report read (counts, level, profile id)");
            var bad = ShieldStatus.FromReport("{\"blockedToday\":-5,\"level\":\"evil\",\"profile\":\"../x\",\"day\":\"tomorrow\"}", now);
            Ok(bad.BlockedToday == null && bad.Level == null && bad.Id == "" && bad.Day == null, "relay: odd values are dropped, never passed on");
            var chrome = E("Chrome", "aaaa", 100, 1000, now.AddMinutes(-3), today); var edgeOld = E("Edge", "bbbb", 50, 500, now.AddDays(-3), null);
            var merged = ShieldStatus.Merge(new List<ShieldEntry> { chrome, edgeOld, E("Firefox", "zz", 1, 1, now.AddDays(-40), null) }, rep, now);
            Ok(merged.Count == 3 && merged.Any(x => x.Name == "Chrome") && merged.Any(x => x.Name == "Edge") && merged.Any(x => x.Name == "Hallow Browser"), "relay: merged with the other browsers' entries (a 40-day-old one forgotten)");
            var again = ShieldStatus.Merge(merged, ShieldStatus.FromReport("{\"blockedToday\":20,\"profile\":\"0a1b2c3d4e5f6071\",\"day\":\"" + today + "\"}", now), now);
            Ok(again.Count(x => x.Id == "0a1b2c3d4e5f6071") == 1 && again.First(x => x.Id == "0a1b2c3d4e5f6071").BlockedToday == 20, "relay: the next report replaces ours (same profile id), no duplicates");
            var renamed = ShieldStatus.Merge(new List<ShieldEntry> { E("Chrome", "0a1b2c3d4e5f6071", 1, 1, now, today), chrome }, rep, now);
            Ok(renamed.Count(x => x.Id == "0a1b2c3d4e5f6071") == 1 && renamed.Count == 2, "relay: our entry under another name (the bridge renames unknown browsers) is replaced, not doubled");
            var json = ShieldStatus.Render(again, "1.7", now); var d = js.Deserialize<Dictionary<string, object>>(json);
            Ok((string)d["version"] == "1.7" && d.ContainsKey("updated") && ((System.Collections.ArrayList)d["browsers"]).Count == 3, "relay: status.json in the bridge's format (version, updated, browsers)");
            Eq(Convert.ToInt64(d["blockedToday"]), 120L, "relay: blockedToday = the entries reported today (Chrome 100 + ours 20; Edge is 3 days old)");
            Eq(Convert.ToInt64(d["blockedWeek"]), 1000L, "relay: blockedWeek = entries heard from in 2 days (ours had no week count this time)");
            string ver; var back = ShieldStatus.Load(json, out ver);
            Ok(back.Count == 3 && ver == "1.7" && back.Any(x => x.Name == "Hallow Browser" && x.BlockedToday == 20), "relay: the file reads back");
            var b0 = js.Deserialize<Dictionary<string, object>>(ShieldStatus.Render(new List<ShieldEntry>(), null, now));
            Ok(b0["blockedToday"] == null && (string)b0["version"] == "1.7", "relay: no counts = null sums");
            var keys = ((System.Collections.ArrayList)d["browsers"]).Cast<Dictionary<string, object>>().First().Keys.OrderBy(k => k).ToList();
            Eq(string.Join(",", keys), "bannersToday,blockedToday,blockedWeek,day,id,level,name,profile,updated,version", "relay: each entry has exactly the bridge's fields");
        }

        static void ExtTests()
        {
            string err;
            var none = Path.Combine(root, "noext"); Directory.CreateDirectory(none);
            Ok(ExtStore.Read(none, out err) == null && err.Contains("manifest.json"), "extensions: a folder without manifest.json is refused, with why");
            var bad = Path.Combine(root, "badext"); Directory.CreateDirectory(bad); File.WriteAllText(Path.Combine(bad, "manifest.json"), "{\"name\":\"x\",\"manifest_version\":1}");
            Ok(ExtStore.Read(bad, out err) == null && err.Contains("manifest_version"), "extensions: manifest_version 1 is refused");
            var ok = Path.Combine(root, "goodext"); Directory.CreateDirectory(Path.Combine(ok, "_locales", "en"));
            File.WriteAllText(Path.Combine(ok, "manifest.json"), "{\"manifest_version\":3,\"name\":\"__MSG_appName__\",\"version\":\"2.1\",\"default_locale\":\"en\",\"action\":{\"default_popup\":\"pop.html\"},\"options_ui\":{\"page\":\"opt.html\"},\"description\":\"__MSG_desc__\"}");
            File.WriteAllText(Path.Combine(ok, "_locales", "en", "messages.json"), "{\"appName\":{\"message\":\"Dark Reader Test\"},\"desc\":{\"message\":\"Makes things dark\"}}");
            var x = ExtStore.Read(ok, out err);
            Ok(x != null && x.Name == "Dark Reader Test" && x.Version == "2.1" && x.Popup == "pop.html" && x.Options == "opt.html" && x.Description == "Makes things dark", "extensions: name from _locales, version, popup and options page");
            var mv2 = Path.Combine(root, "mv2"); Directory.CreateDirectory(mv2); File.WriteAllText(Path.Combine(mv2, "manifest.json"), "{\"manifest_version\":2,\"name\":\"Old\",\"version\":\"1\",\"browser_action\":{\"default_popup\":\"p.html\"}}");
            var y = ExtStore.Read(mv2, out err); Ok(y != null && y.Popup == "p.html", "extensions: manifest v2's browser_action popup too");
            Eq(ExtStore.Slug("Dark Reader: Test!"), "dark-reader-test", "extensions: folder names from the extension's name");
            Ok(!ExtStore.Ours(@"C:\Windows") && !ExtStore.Ours(null), "extensions: only the browser's own copies are ever deleted");
            var ue = js.Deserialize<UserExtension>("{\"Id\":\"a\"}");
            var dc = new DataContractJsonSerializer(typeof(UserExtension)); UserExtension z; using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"Id\":\"a\"}"))) z = (UserExtension)dc.ReadObject(ms);
            Ok(z.Enabled, "extensions: one saved without the switch reads as on");
        }

        static void CornerTests()
        {
            var hub = "{\"Games\":{\"steam:2507950\":{\"Name\":\"Delta Force\",\"Hidden\":false,\"LastPlayed\":\"2026-11-20T21:00:00\",\"Minutes\":900},\"steam:1422450\":{\"Name\":\"Deadlock\",\"Hidden\":true,\"LastPlayed\":\"2026-11-21T00:00:00\"}," +
                "\"epic:Fortnite\":{\"Name\":\"Fortnite\"},\"steam:2073850\":{\"Name\":\"THE FINALS\",\"CustomName\":\"Finals\",\"LastPlayed\":\"2026-11-19T10:00:00\",\"Minutes\":50},\"steam:evil\":{\"Name\":\"x\"}}}";
            var st = "{\"recent\":[{\"id\":\"steam:2357570\",\"name\":\"Overwatch\"},{\"id\":\"bnet:x\",\"name\":\"y\"}]}";
            var g = GameCorner.HubGames(hub, st);
            Eq(string.Join(",", g.Select(x => x.AppId + ":" + x.Name)), "2507950:Delta Force,2073850:Finals,2357570:Overwatch", "game corner: Hub's Steam games (last played first, hidden / non-Steam / odd ids left out, recent ones added)");
            Eq(GameCorner.HubGames(null, null).Count, 0, "game corner: no Hub = no games");
            var steam = "{\"appnews\":{\"appid\":2507950,\"newsitems\":[{\"gid\":\"1\",\"title\":\"The Rally event is live\",\"url\":\"https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/1\",\"is_external_url\":true,\"author\":\"\",\"contents\":\"x\",\"feedlabel\":\"Community Announcements\",\"date\":1795096248,\"feedname\":\"steam_community_announcements\",\"feed_type\":1,\"appid\":2507950}," +
                "{\"gid\":\"2\",\"title\":\"Patch 1.2\",\"url\":\"https://store.steampowered.com/news/app/2507950/view/2\",\"date\":1795000000},{\"gid\":\"3\",\"title\":\"\",\"url\":\"https://x/\"},{\"gid\":\"4\",\"title\":\"js\",\"url\":\"javascript:alert(1)\"}],\"count\":120}}";
            var n = GameCorner.ParseNews(steam, "Delta Force");
            Ok(n.Count == 2 && n[0].Title == "The Rally event is live" && n[0].Date == 1795096248 && n[0].Game == "Delta Force" && n.All(x => x.Url.StartsWith("https://")), "game corner: Steam's news (the real response shape; empty titles and non-web links dropped)");
            Eq(GameCorner.ParseNews("{oops", "x").Count, 0, "game corner: a broken Steam answer = nothing");
            var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
            var epic = "{\"data\":{\"Catalog\":{\"searchStore\":{\"elements\":[" +
                "{\"title\":\"Free Now Game\",\"productSlug\":\"free-now-game\",\"price\":{\"totalPrice\":{\"discountPrice\":0}},\"promotions\":{\"promotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"2026-11-20T16:00:00.000Z\",\"endDate\":\"2026-11-27T16:00:00.000Z\",\"discountSetting\":{\"discountType\":\"PERCENTAGE\",\"discountPercentage\":0}}]}],\"upcomingPromotionalOffers\":[]}}," +
                "{\"title\":\"Next Week Game\",\"productSlug\":null,\"catalogNs\":{\"mappings\":[{\"pageSlug\":\"next-week\",\"pageType\":\"productHome\"}]},\"promotions\":{\"promotionalOffers\":[],\"upcomingPromotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"2026-11-27T16:00:00.000Z\",\"endDate\":\"2026-12-04T16:00:00.000Z\",\"discountSetting\":{\"discountType\":\"PERCENTAGE\",\"discountPercentage\":0}}]}]}}," +
                "{\"title\":\"Half Price\",\"productSlug\":\"half\",\"promotions\":{\"promotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"2026-11-20T16:00:00.000Z\",\"endDate\":\"2026-11-27T16:00:00.000Z\",\"discountSetting\":{\"discountType\":\"PERCENTAGE\",\"discountPercentage\":50}}]}]}}," +
                "{\"title\":\"No Promo\",\"promotions\":null}]}}}}";
            var f = GameCorner.ParseEpic(epic, now);
            Ok(f.Count == 2 && f[0].Title == "Free Now Game" && f[0].Now && f[0].Url == "https://store.epicgames.com/p/free-now-game" && f[0].When.StartsWith("FREE UNTIL"), "game corner: Epic's free-now game (100% off now, store link)");
            Ok(f[1].Title == "Next Week Game" && !f[1].Now && f[1].When.StartsWith("FROM") && f[1].Url.EndsWith("/p/next-week"), "game corner: Epic's next free game (upcoming, slug from the mappings); half-price and no-promo ones left out");
            var c = new GameCorner.Cache { Fetched = DateTime.Now.AddHours(-1).ToString("s"), News = n, Free = f };
            Ok(!GameCorner.Due(c, DateTime.Now), "game corner: fetched an hour ago = not again (6 hours)");
            c.Fetched = DateTime.Now.AddHours(-7).ToString("s"); Ok(GameCorner.Due(c, DateTime.Now), "game corner: after 6 hours = due");
            c.Tried = DateTime.Now.AddMinutes(-20).ToString("s"); Ok(!GameCorner.Due(c, DateTime.Now), "game corner: a failed try 20 min ago = wait (1 hour)");
            Ok(GameCorner.Due(null, DateTime.Now), "game corner: never fetched = due");
            var page = GameCorner.ForPage(c); var news = (List<Dictionary<string, object>>)page["news"];
            Ok(news.Count == 2 && (string)news[0]["g"] == "Delta Force" && ((List<Dictionary<string, object>>)page["free"]).Count == 2 && ((string)page["note"]).Contains("Steam ids"), "game corner: the new tab page's data (news newest first, free games, the note)");
            Ok(GameCorner.SteamUrl("2507950").StartsWith("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=2507950&count=3"), "game corner: Steam's public news API with the app id only (no key, nothing else)");
            Ok(Engine.NewTabHtml("duckduckgo", "#7C3AED").Contains("id=gc") && Engine.NewTabHtml("duckduckgo", "#7C3AED").Contains("GAME NEWS"), "game corner: the new tab page has its section");
        }

        static void PrivateTests()
        {
            var p = BrowserWindow.PrivatePalette; int bad = 0; var misses = new List<string>();
            foreach (var fg in new[] { "Text", "Muted", "Lilac" }) foreach (var bg in new[] { "Bg", "Strip", "Bar", "Panel", "PanelHi", "TabOn", "TabHover", "FieldBg" })
                { var c = Look.Contrast(p[fg], p[bg]); if (c < 4.5) { bad++; misses.Add(fg + " on " + bg + " " + c.ToString("0.00")); } }
            Ok(bad == 0, "private windows: text, muted and lilac on every private background >= 4.5:1" + (bad > 0 ? " - " + string.Join("; ", misses) : ""));
            Ok(Look.Contrast("#FFFFFF", "#4A237A") >= 4.5, "private windows: the PRIVATE chip (white on purple) >= 4.5:1");
            var html = Engine.PrivateTabHtml("duckduckgo");
            Ok(html.Contains("PRIVATE") && html.Contains("Downloads stay") && html.Contains("duckduckgo.com") && !html.Contains("chrome.webview") && !html.Contains("data.json"), "private windows: their own new tab page (no tiles, no history, nothing posted to the browser)");
            Ok(Omni.IsNewTab(Omni.PrivateTabUrl) && !Omni.IsWeb(Omni.PrivateTabUrl), "private windows: their new tab page is a new tab page (never saved, never in history)");
            Ok(Icons.Has("mask"), "private windows: the mask icon");
        }

        static void SessionTests()
        {
            var s = new Session { Tabs = new List<SessionTab> { new SessionTab { Url = "https://a.example/" } }, Windows = new List<WindowSession> { new WindowSession { Space = "w2", Session = new Session { Tabs = new List<SessionTab> { new SessionTab { Url = "https://b.example/" } } }, Left = 10, Top = 20, Width = 900, Height = 700 } } };
            var ser = new DataContractJsonSerializer(typeof(Session)); var ms = new MemoryStream(); ser.WriteObject(ms, s); ms.Position = 0;
            var back = (Session)ser.ReadObject(ms);
            Ok(back.Tabs[0].Url == "https://a.example/" && back.Windows.Count == 1 && back.Windows[0].Space == "w2" && back.Windows[0].Session.Tabs[0].Url == "https://b.example/" && back.Windows[0].Width == 900, "windows: the session keeps every window (first window's tabs at the top, the others in Windows)");
            ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"Tabs\":[{\"Url\":\"https://old.example/\"}],\"Active\":0}"));
            var old = (Session)ser.ReadObject(ms);
            Ok(old.Tabs.Count == 1 && old.Windows == null, "windows: a 1.3 session.json still reads (one window)");
            var ws = new WorkspaceStore(); ws.Items.Add(new Workspace { Id = "x", Name = "Window 2", Auto = true });
            var ser2 = new DataContractJsonSerializer(typeof(WorkspaceStore)); ms = new MemoryStream(); ser2.WriteObject(ms, ws); ms.Position = 0;
            Ok(((WorkspaceStore)ser2.ReadObject(ms)).Items[0].Auto, "windows: a window's own workspace is marked (it goes away with the window)");
        }

        static void SettingsTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(s.HttpsOnly && s.SecureDns == "off" && !s.GameCorner && s.ShareShieldCounts && s.Sites.Count == 0 && s.Extensions.Count == 0, "1.4 defaults: HTTPS-only on, secure DNS off, game corner OFF, Shield counts shared, no site rules, no extensions");
            var ser = new DataContractJsonSerializer(typeof(BrowserSettings));
            var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"Search\":\"google\"}")); var old = (BrowserSettings)ser.ReadObject(ms); old.Fix();
            Ok(old.HttpsOnly && old.SecureDns == "off" && !old.GameCorner && old.ShareShieldCounts, "1.4 defaults also for a 1.3 settings.json");
            ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"SecureDns\":\"evil\",\"Sites\":[null,{\"Host\":\"\"},{\"Host\":\"a.example\",\"Script\":\"block\"}],\"Extensions\":[{\"Id\":\"x\"}]}"));
            var odd = (BrowserSettings)ser.ReadObject(ms); odd.Fix();
            Ok(odd.SecureDns == "off" && odd.Sites.Count == 1 && odd.Extensions.Count == 0, "1.4 settings: an unknown DNS provider = off, broken site rules / extensions without a folder dropped");
        }
    }
}
