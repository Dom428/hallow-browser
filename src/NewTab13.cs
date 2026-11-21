// Hallow Browser 1.3 - what the new tab page shows: speed-dial tiles (pinned pages first, then your most visited pages from the
// local history store) and the Hallow strip (Guard's score, Shield's "blocked today", what's playing) from the other apps'
// status files. Written to newtab\data.json beside the page (LocalAppData), only when it changed; the page reads it locally.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowBrowser
{
    public static class NewTabData
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        public const int MaxTiles = 8;
        static readonly string[] colors = { "#7C3AED", "#DB2777", "#2563EB", "#059669", "#D97706", "#DC2626", "#0891B2", "#4F46E5", "#65A30D", "#C026D3" };

        // the most visited pages, one per site (its most visited address), newer visits counting a little more
        public static List<HistoryEntry> TopSites(IEnumerable<HistoryEntry> history, ICollection<string> hidden, ICollection<string> pinned, int max, DateTime now)
        {
            var best = new Dictionary<string, KeyValuePair<HistoryEntry, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in history)
            {
                if (h == null || !Omni.IsWeb(h.Url) || hidden.Contains(h.Url) || pinned.Contains(h.Url)) continue;
                var host = Omni.HostOf(h.Url); if (host == null) continue;
                if (host.StartsWith("www.")) host = host.Substring(4);
                if (hidden.Contains(host)) continue;
                DateTime last; double age = DateTime.TryParse(h.Last, CultureInfo.InvariantCulture, DateTimeStyles.None, out last) ? Math.Max(0, (now - last).TotalDays) : 60;
                var score = Math.Max(1, h.Visits) / (1 + age / 14.0);
                KeyValuePair<HistoryEntry, double> cur;
                if (!best.TryGetValue(host, out cur) || score > cur.Value) best[host] = new KeyValuePair<HistoryEntry, double>(h, score);
            }
            return best.Values.OrderByDescending(x => x.Value).ThenBy(x => x.Key.Url, StringComparer.Ordinal).Take(max).Select(x => x.Key).ToList();
        }
        public static string Letter(string url, string title)
        {
            var host = Omni.HostOf(url) ?? ""; if (host.StartsWith("www.")) host = host.Substring(4);
            var s = host.Length > 0 ? host : (title ?? "?");
            return s.Substring(0, 1).ToUpperInvariant();
        }
        public static string Color(string url)
        {
            var host = Omni.HostOf(url) ?? url ?? ""; int h = 0; foreach (var ch in host) h = unchecked(h * 31 + ch);
            return colors[(h & 0x7fffffff) % colors.Length];
        }
        static string Short(string t, string url) { t = string.IsNullOrWhiteSpace(t) ? (Omni.HostOf(url) ?? url) : t.Trim(); return t.Length > 60 ? t.Substring(0, 60) : t; }

        // the strip: only what's there (an app that isn't installed, or an old status file, is simply left out)
        public static List<Dictionary<string, string>> Strip(string guardStatus, string shieldStatus, string playing, DateTime now)
        {
            var res = new List<Dictionary<string, string>>();
            try
            {
                var g = guardStatus == null ? null : js.Deserialize<Dictionary<string, object>>(guardStatus);
                object sc, lb;
                if (g != null && g.TryGetValue("score", out sc) && sc != null) { g.TryGetValue("scoreLabel", out lb); res.Add(Chip("GUARD", sc + "/100" + (lb != null ? " - " + lb : ""))); }
            }
            catch { }
            try
            {
                var s = shieldStatus == null ? null : js.Deserialize<Dictionary<string, object>>(shieldStatus);
                object bt, up;
                if (s != null && s.TryGetValue("blockedToday", out bt) && bt != null)
                {
                    DateTime u; bool fresh = !s.TryGetValue("updated", out up) || up == null || !DateTime.TryParse(up.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out u) || u.Date == now.Date;
                    if (fresh) res.Add(Chip("SHIELD", Convert.ToInt64(bt).ToString("N0", CultureInfo.InvariantCulture) + " blocked today"));
                }
            }
            catch { }
            if (!string.IsNullOrWhiteSpace(playing)) res.Add(Chip("NOW PLAYING", playing.Length > 70 ? playing.Substring(0, 70) + "..." : playing));
            return res;
        }
        static Dictionary<string, string> Chip(string k, string v) { return new Dictionary<string, string> { { "k", k }, { "v", v } }; }
        // Mixer's status.json (when it has one) says what's playing
        public static string MixerPlaying(string mixerStatus)
        {
            try
            {
                var m = mixerStatus == null ? null : js.Deserialize<Dictionary<string, object>>(mixerStatus); if (m == null) return null;
                foreach (var k in new[] { "nowPlaying", "playing", "track" })
                {
                    object v; if (!m.TryGetValue(k, out v) || v == null) continue;
                    var d = v as Dictionary<string, object>;
                    if (d != null) { object t, a; d.TryGetValue("title", out t); d.TryGetValue("artist", out a); if (t != null) return t + (a != null ? " - " + a : ""); }
                    else if (v is string && ((string)v).Length > 0) return (string)v;
                }
            }
            catch { }
            return null;
        }
        static string ReadSmall(string path) { try { var f = new FileInfo(path); return f.Exists && f.Length < 1024 * 1024 ? Encoding.UTF8.GetString(Importer.ReadShared(path)) : null; } catch { return null; } }

        // the whole data.json
        public static string Build(BrowserSettings s, IEnumerable<HistoryEntry> history, string playingFallback, DateTime now)
        {
            var tiles = new List<Dictionary<string, object>>();
            if (s.NewTabTopSites)
            {
                var pinned = s.NewTabPinned ?? new List<PinnedSite>();
                foreach (var p in pinned.Take(MaxTiles)) tiles.Add(new Dictionary<string, object> { { "u", p.Url }, { "t", Short(p.Title, p.Url) }, { "l", Letter(p.Url, p.Title) }, { "c", Color(p.Url) }, { "pin", true } });
                var hidden = new HashSet<string>(s.NewTabHidden ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                var pin = new HashSet<string>(pinned.Select(p => p.Url), StringComparer.Ordinal);
                foreach (var h in TopSites(history ?? new HistoryEntry[0], hidden, pin, Math.Max(0, MaxTiles - tiles.Count), now))
                    tiles.Add(new Dictionary<string, object> { { "u", h.Url }, { "t", Short(h.Title, h.Url) }, { "l", Letter(h.Url, h.Title) }, { "c", Color(h.Url) }, { "pin", false } });
            }
            List<Dictionary<string, string>> fam = null;
            if (s.NewTabFamily)
            {
                var playing = MixerPlaying(ReadSmall(Path.Combine(HallowBrowser.Family.DataDir("Mixer"), "status.json"))) ?? playingFallback;
                fam = Strip(ReadSmall(Path.Combine(HallowBrowser.Family.DataDir("Guard"), "status.json")), ReadSmall(Path.Combine(HallowBrowser.Family.DataDir("Shield"), "status.json")), playing, now);
            }
            // 1.4: the game corner (only when it's on; from its local copy - the page itself never contacts Steam or Epic)
            var games = s.GameCorner ? GameCorner.ForPage(GameCorner.Load()) : null;
            return js.Serialize(new Dictionary<string, object> { { "tiles", tiles }, { "clock", s.NewTabClock }, { "family", fam ?? new List<Dictionary<string, string>>() }, { "games", games } });
        }
        static string last;
        public static bool Write(string json)
        {
            try
            {
                if (json == last) return false;
                Directory.CreateDirectory(Engine.NewTabDir);
                var f = Path.Combine(Engine.NewTabDir, "data.json"); var tmp = f + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
                last = json; return true;
            }
            catch { return false; }
        }
    }
}
