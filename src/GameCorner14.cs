// Hallow Browser 1.4 - the game corner on the new tab page (OFF by default: it contacts Steam and Epic).
// News: the games in your Hallow Hub library (Hub's hub.json, read only - the Steam app ids) through Steam's public news API, 3 items
// each, no key. Free games: Epic's public free-games promotions list. Fetched at most every 6 hours (and never while a game runs),
// kept in LocalAppData\HallowBrowser\gamecorner.json; the new tab page only reads that local copy - no pictures or scripts from
// either site. Only the app ids go out (and Epic's list is the same for everyone): nothing about you beyond the requests themselves.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace HallowBrowser
{
    public class HubGame { public string AppId, Name; }
    public class CornerNews { public string Game, Title, Url; public long Date; }
    public class CornerFree { public string Title, Url, When; public bool Now; }

    public static class GameCorner
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
        public const int MaxGames = 8, NewsPerGame = 3, MaxNews = 9;
        public static readonly TimeSpan Every = TimeSpan.FromHours(6), RetryAfter = TimeSpan.FromHours(1);
        public const string SteamApi = "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/";
        public const string EpicApi = "https://store-site-backend-static.epicgames.com/freeGamesPromotions";
        public static string CachePath { get { return Path.Combine(BrowserSettings.LocalDir, "gamecorner.json"); } }
        static Dictionary<string, object> O(object o) { return o as Dictionary<string, object>; }
        static IEnumerable<object> A(object o) { if (o == null || o is string || o is IDictionary) return Enumerable.Empty<object>(); var a = o as IEnumerable; return a == null ? Enumerable.Empty<object>() : a.Cast<object>(); }   // DeserializeObject gives object[], Deserialize ArrayList
        static object G(object o, params string[] path) { foreach (var k in path) { var d = O(o); object v; if (d == null || !d.TryGetValue(k, out v)) return null; o = v; } return o; }
        static string S(object o, params string[] path) { return G(o, path) as string; }

        // Hub's library (hub.json "Games": "steam:<appid>" -> playtime etc.) - your most played / last played Steam games, not hidden ones
        public static List<HubGame> HubGames(string hubJson, string hubStatus)
        {
            var res = new List<HubGame>();
            try
            {
                var games = O(G(hubJson == null ? null : js.DeserializeObject(hubJson), "Games"));
                if (games != null)
                {
                    var rows = new List<Tuple<string, string, string, double>>();
                    foreach (var kv in games)
                    {
                        if (!kv.Key.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)) continue;
                        var id = kv.Key.Substring(6); if (!id.All(char.IsDigit) || id.Length == 0 || id.Length > 10) continue;
                        var g = O(kv.Value); if (g != null && G(g, "Hidden") is bool && (bool)G(g, "Hidden")) continue;
                        double min = 0; try { min = Convert.ToDouble(G(g, "Minutes") ?? 0, CultureInfo.InvariantCulture); } catch { }
                        var name = S(g, "CustomName"); if (string.IsNullOrWhiteSpace(name)) name = S(g, "Name");
                        rows.Add(Tuple.Create(id, name, S(g, "LastPlayed") ?? "", min));
                    }
                    foreach (var r in rows.OrderByDescending(r => r.Item3, StringComparer.Ordinal).ThenByDescending(r => r.Item4).Take(MaxGames)) res.Add(new HubGame { AppId = r.Item1, Name = r.Item2 });
                }
                // names (and more games) from Hub's status.json "recent"
                foreach (var x in A(G(hubStatus == null ? null : js.DeserializeObject(hubStatus), "recent")))
                {
                    var id = S(x, "id"); if (id == null || !id.StartsWith("steam:")) continue; id = id.Substring(6); if (!id.All(char.IsDigit)) continue;
                    var have = res.FirstOrDefault(h => h.AppId == id);
                    if (have == null && res.Count < MaxGames) res.Add(new HubGame { AppId = id, Name = S(x, "name") });
                    else if (have != null && string.IsNullOrWhiteSpace(have.Name)) have.Name = S(x, "name");
                }
            }
            catch { }
            foreach (var h in res) if (string.IsNullOrWhiteSpace(h.Name)) h.Name = "App " + h.AppId;
            return res;
        }
        // Steam: {"appnews":{"appid":..,"newsitems":[{"title","url","date"(unix),"feedlabel",...}]}}
        public static List<CornerNews> ParseNews(string json, string game)
        {
            var res = new List<CornerNews>();
            try
            {
                foreach (var it in A(G(js.DeserializeObject(json), "appnews", "newsitems")))
                {
                    var t = S(it, "title"); var u = S(it, "url"); if (string.IsNullOrWhiteSpace(t) || !Omni.IsWeb(u)) continue;
                    long d = 0; try { d = Convert.ToInt64(G(it, "date") ?? 0, CultureInfo.InvariantCulture); } catch { }
                    res.Add(new CornerNews { Game = game, Title = t.Trim().Length > 140 ? t.Trim().Substring(0, 140) : t.Trim(), Url = u, Date = d });
                }
            }
            catch { }
            return res.Take(NewsPerGame).ToList();
        }
        static DateTime? Time(object o) { DateTime t; var s = o as string; return s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t) ? (DateTime?)t : null; }
        // Epic: data.Catalog.searchStore.elements[] - free now (a current 100% promotion) and coming next
        public static List<CornerFree> ParseEpic(string json, DateTime nowUtc)
        {
            var now = new List<CornerFree>(); var next = new List<CornerFree>();
            try
            {
                foreach (var el in A(G(js.DeserializeObject(json), "data", "Catalog", "searchStore", "elements")))
                {
                    var title = S(el, "title"); if (string.IsNullOrWhiteSpace(title)) continue;
                    var slug = S(el, "productSlug");
                    if (string.IsNullOrWhiteSpace(slug) || slug == "[]") { var m = A(G(el, "catalogNs", "mappings")).FirstOrDefault(); slug = S(m, "pageSlug"); }
                    if (string.IsNullOrWhiteSpace(slug)) { var m = A(G(el, "offerMappings")).FirstOrDefault(); slug = S(m, "pageSlug"); }
                    if (!string.IsNullOrWhiteSpace(slug)) slug = slug.Split('/')[0];
                    var url = string.IsNullOrWhiteSpace(slug) ? "https://store.epicgames.com/free-games" : "https://store.epicgames.com/p/" + Uri.EscapeDataString(slug);
                    Func<string, IEnumerable<object>> offers = k => A(G(el, "promotions", k)).SelectMany(p => A(G(p, "promotionalOffers")));
                    Func<object, bool> free = o => { try { return Convert.ToDouble(G(o, "discountSetting", "discountPercentage") ?? -1, CultureInfo.InvariantCulture) == 0; } catch { return false; } };
                    var cur = offers("promotionalOffers").FirstOrDefault(o => free(o) && Time(G(o, "startDate")) <= nowUtc && Time(G(o, "endDate")) > nowUtc);
                    if (cur != null) { var end = Time(G(cur, "endDate")); now.Add(new CornerFree { Title = title, Url = url, Now = true, When = end == null ? "FREE NOW" : "FREE UNTIL " + end.Value.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture).ToUpperInvariant() }); continue; }
                    var up = offers("upcomingPromotionalOffers").FirstOrDefault(o => free(o) && Time(G(o, "startDate")) > nowUtc);
                    if (up != null) { var st = Time(G(up, "startDate")); next.Add(new CornerFree { Title = title, Url = url, When = st == null ? "SOON" : "FROM " + st.Value.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture).ToUpperInvariant() }); }
                }
            }
            catch { }
            return now.Concat(next).Take(6).ToList();
        }

        // ---- the local copy
        public class Cache { public string Fetched, Tried; public List<CornerNews> News = new List<CornerNews>(); public List<CornerFree> Free = new List<CornerFree>(); public string Error; }
        public static Cache Load() { try { var f = CachePath; return File.Exists(f) ? js.Deserialize<Cache>(File.ReadAllText(f)) : null; } catch { return null; } }
        public static void Save(Cache c) { try { Directory.CreateDirectory(Path.GetDirectoryName(CachePath)); var tmp = CachePath + ".tmp"; File.WriteAllText(tmp, js.Serialize(c), new UTF8Encoding(false)); if (File.Exists(CachePath)) File.Replace(tmp, CachePath, null); else File.Move(tmp, CachePath); } catch { } }
        public static bool Due(Cache c, DateTime now)
        {
            DateTime f, t;
            if (c != null && DateTime.TryParse(c.Fetched, CultureInfo.InvariantCulture, DateTimeStyles.None, out f) && now - f < Every) return false;
            if (c != null && DateTime.TryParse(c.Tried, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && now - t < RetryAfter) return false;
            return true;
        }
        // what data.json gets for the page
        public static Dictionary<string, object> ForPage(Cache c)
        {
            if (c == null) return null;
            return new Dictionary<string, object> {
                { "news", c.News.OrderByDescending(n => n.Date).Take(MaxNews).Select(n => new Dictionary<string, object> { { "g", n.Game }, { "t", n.Title }, { "u", n.Url } }).ToList() },
                { "free", c.Free.Select(n => new Dictionary<string, object> { { "t", n.Title }, { "u", n.Url }, { "w", n.When } }).ToList() },
                { "note", "From Steam and Epic" + (c.Fetched != null ? ", " + c.Fetched.Replace('T', ' ').Substring(0, Math.Min(16, c.Fetched.Length)) : "") + " - only your games' Steam ids were sent." } };
        }

        // ---- fetching (a background thread; tests point the two addresses at local stand-ins)
        public static string SteamUrl(string appid) { return (BrowserSettings.SteamNewsTest ?? SteamApi) + "?appid=" + Uri.EscapeDataString(appid) + "&count=" + NewsPerGame + "&maxlength=1&format=json"; }
        public static string EpicUrl { get { return BrowserSettings.EpicFreeTest ?? EpicApi; } }
        static string Get(string url)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "HallowBrowser/" + Program.Version; r.Timeout = 12000; r.ReadWriteTimeout = 12000; r.Accept = "application/json";
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            r.CookieContainer = null; r.AllowAutoRedirect = true;
            using (var resp = (HttpWebResponse)r.GetResponse()) using (var s = resp.GetResponseStream()) using (var rd = new StreamReader(s, Encoding.UTF8))
            {
                var sb = new StringBuilder(); var buf = new char[16384]; int n;
                while ((n = rd.Read(buf, 0, buf.Length)) > 0) { sb.Append(buf, 0, n); if (sb.Length > 8 * 1024 * 1024) throw new IOException("too big"); }
                return sb.ToString();
            }
        }
        public static Cache Fetch(List<HubGame> games, DateTime now)
        {
            var c = Load() ?? new Cache(); c.Tried = now.ToString("s", CultureInfo.InvariantCulture);
            var news = new List<CornerNews>(); var errs = new List<string>(); int ok = 0;
            foreach (var g in games.Take(MaxGames)) { try { news.AddRange(ParseNews(Get(SteamUrl(g.AppId)), g.Name)); ok++; } catch (Exception e) { errs.Add("Steam: " + e.Message); } }
            List<CornerFree> free = null;
            try { free = ParseEpic(Get(EpicUrl), now.ToUniversalTime()); ok++; } catch (Exception e) { errs.Add("Epic: " + e.Message); }
            if (ok > 0)
            {
                if (games.Count == 0 || news.Count > 0 || errs.All(x => !x.StartsWith("Steam"))) c.News = news;
                if (free != null) c.Free = free;
                c.Fetched = now.ToString("s", CultureInfo.InvariantCulture);
            }
            c.Error = errs.Count == 0 ? null : errs.Distinct().First();
            Save(c);
            return c;
        }
    }

    public partial class BrowserWindow
    {
        static bool cornerBusy; public static int CornerFetches;
        // called with the new tab data refresh: fetches when it's on, due and no game runs
        public void GameCornerTick(bool force)
        {
            if (!S.GameCorner || snap || cornerBusy) return;
            if (!force && (Game != null || !GameCorner.Due(GameCorner.Load(), DateTime.Now))) return;
            cornerBusy = true; var s = S;
            Task.Run(() =>
            {
                try
                {
                    var hubDir = Family.DataDir("Hub"); string hub = null, st = null;
                    try { var f = Path.Combine(hubDir, "hub.json"); if (File.Exists(f)) hub = Encoding.UTF8.GetString(Importer.ReadShared(f)); } catch { }
                    try { var f = Path.Combine(hubDir, "status.json"); if (File.Exists(f)) st = Encoding.UTF8.GetString(Importer.ReadShared(f)); } catch { }
                    GameCorner.Fetch(GameCorner.HubGames(hub, st), DateTime.Now);
                    CornerFetches++;
                }
                finally { cornerBusy = false; }
            }).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(NewTabChanged)));
        }
        public UIElement[] GameCornerSettings()
        {
            var c = GameCorner.Load();
            var info = c == null || c.Fetched == null ? (c != null && c.Error != null ? "Last try failed: " + c.Error : "Nothing fetched yet.") : "Last fetched " + c.Fetched.Replace('T', ' ') + ": " + c.News.Count + " news items, " + c.Free.Count + " free games." + (c.Error != null ? " (" + c.Error + ")" : "");
            return new UIElement[] {
                Ui.Switch("Game corner on the new tab page - this contacts Steam and Epic", "News for the games in your Hallow Hub library (Steam's public news API, 3 items each) and Epic's free games. At most every 6 hours, never while a game runs; only your games' Steam app ids are sent. The page shows text and links only - no pictures or scripts from either site.", S.GameCorner, on => { S.GameCorner = on; S.Save(); if (on) GameCornerTick(true); else NewTabChanged(); }),
                Ui.Muted(info, 11.5, 0) };
        }
    }
}
