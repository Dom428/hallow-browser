// Hallow Browser 1.15.1 - new tab page fixes.
//  - Steam names: Hub's library can list a Steam game by its app id only (Hub's hub.json has no name for games that aren't
//    installed or recently played), so the page showed "App 1422450". Names are now looked up on this PC only: the game's
//    appmanifest_<id>.acf ("name") in every Steam library folder (steamapps\libraryfolders.vdf), then Steam's own app cache
//    (appcache\appinfo.vdf, read only), then names found earlier (LocalAppData\HallowBrowser\steamnames.json). Nothing is asked
//    of the internet. No name anywhere: "Steam game <id>" (the cover still shows).
//  - Speed-dial duplicates: tiles are merged by a normalised address (same site + path; well-known mail / app pages by site and
//    account only, gmail.com = mail.google.com). Auto-added "most visited" duplicates go by themselves; pinned tiles are never
//    removed from your settings - a duplicate pin is just shown once (unpinning it unpins both).
//  - SHIELD on the new tab page: Hallow Browser's own entry in Shield's status.json (its "day" = this PC's local day), not the
//    sum of every browser on the PC; the browser relays a fresh count when a new tab opens (at most every 2 minutes).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowBrowser
{
    public static class SteamNames
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static readonly object gate = new object();
        static Dictionary<string, string> mem;
        public static int AppInfoReads;   // tests: how often appinfo.vdf was read
        static string triedStamp;

        // tests: HALLOW_BROWSER_STEAM_ROOT = a fake Steam folder (never the real one); otherwise Steam's own path from the registry
        public static string Root
        {
            get
            {
                if (BrowserSettings.TestDir != null) { var t = Environment.GetEnvironmentVariable("HALLOW_BROWSER_STEAM_ROOT"); return string.IsNullOrWhiteSpace(t) ? null : t; }
                try { var p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; if (!string.IsNullOrWhiteSpace(p)) return p.Replace('/', '\\'); } catch { }
                try { var p = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string; if (!string.IsNullOrWhiteSpace(p)) return p; } catch { }
                return null;
            }
        }
        public static string CachePath { get { return Path.Combine(BrowserSettings.LocalDir, "steamnames.json"); } }

        // the "key" "value" pairs of a text VDF / ACF file (nesting ignored - enough for "path" and "name")
        public static List<KeyValuePair<string, string>> Pairs(string text)
        {
            var res = new List<KeyValuePair<string, string>>();
            if (text == null) return res;
            foreach (Match m in Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\"[ \\t]+\"((?:[^\"\\\\]|\\\\.)*)\""))
                res.Add(new KeyValuePair<string, string>(Unescape(m.Groups[1].Value), Unescape(m.Groups[2].Value)));
            return res;
        }
        static string Unescape(string s) { return s.Replace("\\\\", "\u0001").Replace("\\\"", "\"").Replace("\u0001", "\\"); }

        // every library folder: Steam's own and the ones in libraryfolders.vdf ("path")
        public static List<string> Libraries(string steamRoot)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(steamRoot)) return res;
            res.Add(steamRoot);
            foreach (var f in new[] { Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), Path.Combine(steamRoot, "config", "libraryfolders.vdf") })
            {
                try
                {
                    if (!File.Exists(f)) continue;
                    foreach (var kv in Pairs(File.ReadAllText(f)))
                        if (kv.Key.Equals("path", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(kv.Key, @"^\d+$") && kv.Value.Contains(":\\"))
                            if (!res.Any(x => string.Equals(x.TrimEnd('\\', '/'), kv.Value.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))) res.Add(kv.Value);
                }
                catch { }
            }
            return res;
        }
        public static string FromManifest(string library, string appId)
        {
            try
            {
                var f = Path.Combine(library, "steamapps", "appmanifest_" + appId + ".acf");
                if (!File.Exists(f)) return null;
                var kv = Pairs(File.ReadAllText(f)).FirstOrDefault(x => x.Key.Equals("name", StringComparison.OrdinalIgnoreCase));
                return Clean(kv.Value);
            }
            catch { return null; }
        }
        static string Clean(string n) { if (string.IsNullOrWhiteSpace(n)) return null; n = n.Trim(); return n.Length > 120 ? n.Substring(0, 120) : n; }

        // ---- Steam's binary app cache (appcache\appinfo.vdf): versions 27, 28 and 29 (29 keeps its key names in a string table
        // at the end). Only appinfo > common > name of the wanted apps is read; every other app is skipped by its size.
        public static Dictionary<string, string> FromAppInfo(byte[] b, ICollection<string> wanted)
        {
            var res = new Dictionary<string, string>();
            try
            {
                if (b == null || b.Length < 16 || wanted.Count == 0) return res;
                uint magic = BitConverter.ToUInt32(b, 0);
                int ver = magic == 0x07564429 ? 29 : magic == 0x07564428 ? 28 : magic == 0x07564427 ? 27 : 0;
                if (ver == 0) return res;
                int pos = 8; string[] table = null;
                if (ver == 29)
                {
                    long off = BitConverter.ToInt64(b, 8); pos = 16;
                    if (off <= 16 || off >= b.Length) return res;
                    int p = (int)off; uint n = BitConverter.ToUInt32(b, p); p += 4;
                    if (n > 10000000) return res;
                    table = new string[n];
                    for (int i = 0; i < n && p < b.Length; i++) { int e = Array.IndexOf(b, (byte)0, p); if (e < 0) e = b.Length; table[i] = Encoding.UTF8.GetString(b, p, e - p); p = e + 1; }
                }
                int skipHead = 4 + 4 + 8 + 20 + 4 + (ver >= 28 ? 20 : 0);
                while (pos + 8 <= b.Length)
                {
                    uint id = BitConverter.ToUInt32(b, pos); if (id == 0) break;
                    uint size = BitConverter.ToUInt32(b, pos + 4); int start = pos + 8; int end = start + (int)size;
                    if (size > int.MaxValue || end > b.Length || end < start) break;
                    var sid = id.ToString(CultureInfo.InvariantCulture);
                    if (wanted.Contains(sid) && !res.ContainsKey(sid))
                    {
                        int p = start + skipHead; string name = null;
                        try { name = FindName(b, ref p, end, table, 0, ""); } catch { }
                        if (!string.IsNullOrWhiteSpace(name)) res[sid] = Clean(name);
                        if (res.Count == wanted.Count) break;
                    }
                    pos = end;
                }
            }
            catch { }
            return res;
        }
        // walks one binary KeyValues map; returns appinfo/common/name when it's met
        static string FindName(byte[] b, ref int p, int end, string[] table, int depth, string path)
        {
            if (depth > 12) throw new FormatException();
            string found = null;
            while (p < end)
            {
                byte type = b[p++];
                if (type == 8 || type == 11) return found;
                string key;
                if (table != null) { int ix = BitConverter.ToInt32(b, p); p += 4; key = ix >= 0 && ix < table.Length ? table[ix] : ""; }
                else { int e = Array.IndexOf(b, (byte)0, p, end - p); if (e < 0) throw new FormatException(); key = Encoding.UTF8.GetString(b, p, e - p); p = e + 1; }
                switch (type)
                {
                    case 0: { var f = FindName(b, ref p, end, table, depth + 1, path + "/" + key.ToLowerInvariant()); if (found == null) found = f; break; }
                    case 1: { int e = Array.IndexOf(b, (byte)0, p, end - p); if (e < 0) throw new FormatException(); var v = Encoding.UTF8.GetString(b, p, e - p); p = e + 1; if (found == null && path == "/appinfo/common" && key.Equals("name", StringComparison.OrdinalIgnoreCase)) found = v; break; }
                    case 2: case 3: case 4: case 6: p += 4; break;
                    case 7: case 10: p += 8; break;
                    case 5: { while (p + 1 < end && (b[p] != 0 || b[p + 1] != 0)) p += 2; p += 2; break; }
                    default: throw new FormatException();
                }
            }
            return found;
        }

        // the names found before (also fed by Hub's own names)
        static Dictionary<string, string> Cache()
        {
            if (mem != null) return mem;
            mem = new Dictionary<string, string>();
            try { if (File.Exists(CachePath)) { var d = js.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath)); if (d != null) foreach (var kv in d) if (Regex.IsMatch(kv.Key, @"^\d{1,10}$") && !string.IsNullOrWhiteSpace(kv.Value)) mem[kv.Key] = Clean(kv.Value); } } catch { }
            return mem;
        }
        static void SaveCache()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(CachePath)); var tmp = CachePath + ".tmp"; File.WriteAllText(tmp, js.Serialize(mem), new UTF8Encoding(false)); if (File.Exists(CachePath)) File.Replace(tmp, CachePath, null); else File.Move(tmp, CachePath); } catch { }
        }
        public static void Remember(string appId, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || appId == null || !Regex.IsMatch(appId, @"^\d{1,10}$") || IsPlaceholder(name, appId)) return;
            lock (gate) { var c = Cache(); string have; if (c.TryGetValue(appId, out have) && have == Clean(name)) return; c[appId] = Clean(name); SaveCache(); }
        }
        public static bool IsPlaceholder(string name, string appId) { return string.IsNullOrWhiteSpace(name) || name == "App " + appId || name == Fallback(appId); }
        public static string Fallback(string appId) { return "Steam game " + appId; }
        public static void Forget() { lock (gate) { mem = null; triedStamp = null; } }   // tests

        // names for these app ids (only those it could find): manifests, then appinfo.vdf, then the names found before
        public static Dictionary<string, string> Resolve(ICollection<string> appIds) { return Resolve(appIds, Root); }
        public static Dictionary<string, string> Resolve(ICollection<string> appIds, string steamRoot)
        {
            var res = new Dictionary<string, string>();
            var ids = appIds.Where(x => x != null && Regex.IsMatch(x, @"^\d{1,10}$")).Distinct().ToList();
            if (ids.Count == 0) return res;
            var libs = Libraries(steamRoot);
            foreach (var id in ids) foreach (var lib in libs) { var n = FromManifest(lib, id); if (n != null) { res[id] = n; break; } }
            lock (gate) { var c = Cache(); foreach (var id in ids) { string n; if (!res.ContainsKey(id) && c.TryGetValue(id, out n)) res[id] = n; } }   // names found before (no big read again)
            var left = ids.Where(x => !res.ContainsKey(x)).ToList();
            if (left.Count > 0 && steamRoot != null)
            {
                try
                {
                    var f = Path.Combine(steamRoot, "appcache", "appinfo.vdf"); var fi = new FileInfo(f);
                    var stamp = fi.Exists ? fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + string.Join(",", left.OrderBy(x => x)) : null;
                    if (fi.Exists && fi.Length < 512L * 1024 * 1024 && stamp != triedStamp)
                    {
                        triedStamp = stamp;   // the same file and ids aren't read again (a game Steam has no name for)
                        byte[] b; using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { b = new byte[s.Length]; int r = 0; while (r < b.Length) { int n = s.Read(b, r, b.Length - r); if (n <= 0) break; r += n; } }
                        AppInfoReads++;
                        foreach (var kv in FromAppInfo(b, new HashSet<string>(left))) res[kv.Key] = kv.Value;
                    }
                }
                catch { }
            }
            lock (gate)
            {
                var c = Cache(); bool changed = false;
                foreach (var id in ids) { string n; if (res.TryGetValue(id, out n)) { string h; if (!c.TryGetValue(id, out h) || h != n) { c[id] = n; changed = true; } } else if (c.TryGetValue(id, out n)) res[id] = n; }
                if (changed) SaveCache();
            }
            return res;
        }
    }

    // ---- speed-dial duplicates
    public static class TileKeys
    {
        // the same site under another name (a redirect): the history keeps both addresses
        static readonly Dictionary<string, string> alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "gmail.com", "mail.google.com" }, { "googlemail.com", "mail.google.com" }, { "inbox.google.com", "mail.google.com" },
            { "outlook.com", "outlook.live.com" }, { "hotmail.com", "outlook.live.com" }, { "live.com", "outlook.live.com" }, { "mail.live.com", "outlook.live.com" },
            { "outlook.office365.com", "outlook.office.com" }, { "proton.me", "mail.proton.me" }, { "mail.protonmail.com", "mail.proton.me" },
            { "m.youtube.com", "youtube.com" }, { "youtu.be", "youtube.com" }, { "twitter.com", "x.com" }, { "mobile.twitter.com", "x.com" },
            { "m.twitch.tv", "twitch.tv" }, { "web.whatsapp.com", "whatsapp.com" }, { "discordapp.com", "discord.com" } };
        // mail / app pages: one tile per site (and account), whatever the page, query or #fragment inside the app
        static readonly HashSet<string> apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "mail.google.com", "outlook.live.com", "outlook.office.com", "mail.yahoo.com", "mail.proton.me", "mail.aol.com", "mail.zoho.com", "icloud.com",
            "calendar.google.com", "drive.google.com", "keep.google.com", "photos.google.com", "contacts.google.com", "messages.google.com",
            "whatsapp.com", "web.telegram.org", "app.slack.com", "teams.microsoft.com", "teams.live.com", "discord.com", "messenger.com", "chatgpt.com", "claude.ai" };
        public static string Host(string url)
        {
            var h = (Omni.HostOf(url) ?? "").ToLowerInvariant().TrimEnd('.');
            if (h.StartsWith("www.")) h = h.Substring(4);
            string a; return alias.TryGetValue(h, out a) ? a : h;
        }
        public static bool IsApp(string url) { return apps.Contains(Host(url)); }
        // the normalised address: site (aliases merged, www. and http/https alike, the port kept) + path without the trailing "/";
        // never the #fragment; the ?query too for well-known mail / app pages (which key on site + account only)
        public static string Key(string url)
        {
            Uri u; if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out u)) return url ?? "";
            var host = Host(url); var port = u.IsDefaultPort ? "" : ":" + u.Port;
            if (apps.Contains(host))
            {
                var acct = Regex.Match(u.AbsolutePath, @"/u/(\d+)(/|$)");   // Google's account number (gmail.com = the first one)
                var a = host.EndsWith("google.com") ? "/u/" + (acct.Success ? acct.Groups[1].Value : "0") : "";
                return host + port + a;
            }
            var path = u.AbsolutePath; if (path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 10);
            path = path.TrimEnd('/');
            return host + port + path + (u.Query.Length > 1 ? u.Query : "");
        }
        // the tiles with duplicates merged: pinned first (a duplicate pin shown once), then auto tiles that aren't a pinned / earlier one
        public static List<T> Merge<T>(IEnumerable<T> pinned, IEnumerable<T> auto, Func<T, string> url)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var res = new List<T>();
            foreach (var p in pinned) if (seen.Add(Key(url(p)))) res.Add(p);
            foreach (var a in auto) if (seen.Add(Key(url(a)))) res.Add(a);
            return res;
        }
    }

    // ---- SHIELD on the new tab page: Hallow Browser's own count for today
    public static class NewTabShield
    {
        // the blocked-today count of Hallow Browser's entries (null = no entry from Hallow Browser at all, or nothing counted)
        public static long? BlockedToday(string statusJson, DateTime nowUtc, string profileId = null)
        {
            string ver; var list = ShieldStatus.Load(statusJson, out ver).Where(x => x.Name == "Hallow Browser").ToList();
            if (profileId != null && list.Any(x => x.Id == profileId)) list = list.Where(x => x.Id == profileId).ToList();
            if (list.Count == 0) return null;
            var fresh = list.Where(x => ShieldStatus.Fresh(x, nowUtc)).ToList();
            if (fresh.Count == 0) return null;   // two days without a word: not shown rather than a wrong number
            if (fresh.All(x => x.BlockedToday == null)) return null;   // blocked counts off
            return fresh.Where(x => ShieldStatus.Today(x, nowUtc)).Sum(x => x.BlockedToday ?? 0);   // a count from yesterday is 0 today
        }
        // the report the browser itself made last (same day), when it's newer than the file
        public static long? FromReport(string reportJson, DateTime nowUtc)
        {
            var e = reportJson == null ? null : ShieldStatus.FromReport(reportJson, nowUtc);
            if (e == null || e.BlockedToday == null) return null;
            return e.Day == nowUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? e.BlockedToday : 0;
        }
    }

    public partial class BrowserWindow
    {
        static DateTime ntRelayAt = DateTime.MinValue; public static int NewTabRelays;
        // a new tab page is open: Shield's count relayed now (Shield's own report, as every 5 minutes) unless that was under 2 minutes
        // ago; the page's strip is redrawn when the count changed. Not while a game runs, not in private windows.
        void ShieldForNewTab()
        {
            if (snap || Private || Program.First != this || !S.ShareShieldCounts || ShieldState != "extension") return;
            if (Game != null && !BrowserSettings.Offscreen) return;
            if (DateTime.UtcNow - ntRelayAt < TimeSpan.FromMinutes(2)) return;
            if (!Program.Windows.Any(w => !w.Private && w.Tabs.Any(t => Omni.IsNewTab(t.Url)))) return;
            ntRelayAt = DateTime.UtcNow;
            var before = NewTabShield.FromReport(LastReport, DateTime.UtcNow);
            var _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                var r = await RelayShield(); if (r == null) return;
                NewTabRelays++;
                if (NewTabShield.FromReport(r, DateTime.UtcNow) != before) NewTabChanged();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}
