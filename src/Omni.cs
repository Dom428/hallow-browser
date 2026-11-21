// Hallow Browser - the address bar: is it an address or a search? And origins (scheme://host:port), used for Hallow Keys fills.
// Rules (in order): a web address with http/https (or file:) is used as typed; "localhost", IP addresses and [IPv6] get http://;
// a single word or anything with a space is a search; a dotted name whose last part is a known top-level domain (or any two
// letters) gets https://; everything else is a search. javascript:, data: and other schemes are never run from the bar - searched.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace HallowBrowser
{
    public static class Omni
    {
        // 1.3: the page by name - newer WebView2 runtimes answer a folder address ("https://newtab.hallow/") of a mapped folder with
        // ERR_ACCESS_DENIED instead of its index.html, so 1.2's new tab page showed an error page there
        public const string NewTabUrl = "https://newtab.hallow/index.html";
        public static string FixNewTab(string url) { Uri u; return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == "https" && u.Host == NewTabHost && u.AbsolutePath == "/" ? NewTabUrl : url; }
        public const string NewTabHost = "newtab.hallow";
        public const string SettingsUrl = "hallow://settings";
        public const string BookmarksUrl = "hallow://bookmarks";   // 1.3: the bookmarks manager
        public const string ExtensionsUrl = "hallow://extensions";        // 1.4: your own extensions
        public const string SitesUrl = "hallow://settings/sites";         // 1.4: site settings (permissions, pop-ups, sound, JavaScript, cookies)
        public const string PrivateTabUrl = "https://newtab.hallow/private.html";   // 1.4: a private window's new tab page
        public const string DownloadsUrl = "hallow://downloads";            // 1.5: every download, with Show in folder and Defender's answer
        public const string AppsUrl = "hallow://apps";                      // 1.6: sites installed as apps
        public static bool IsInternal(string url) { return url == SettingsUrl || url == BookmarksUrl || url == ExtensionsUrl || url == SitesUrl || url == DownloadsUrl || url == AppsUrl; }

        public static string EngineName(string e)
        {
            switch (e) { case "google": return "Google"; case "bing": return "Bing"; case "brave": return "Brave Search"; default: return "DuckDuckGo"; }
        }
        public static string SearchUrl(string engine, string q)
        {
            var e = Uri.EscapeDataString(q ?? "");
            switch (engine)
            {
                case "google": return "https://www.google.com/search?q=" + e;
                case "bing": return "https://www.bing.com/search?q=" + e;
                case "brave": return "https://search.brave.com/search?q=" + e;
                default: return "https://duckduckgo.com/?q=" + e;
            }
        }

        static readonly HashSet<string> Tlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "com", "net", "org", "edu", "gov", "mil", "int", "io", "dev", "app", "gg", "tv", "co", "me", "info", "biz", "xyz", "ai", "tech", "online", "site",
            "store", "blog", "shop", "cloud", "page", "link", "live", "news", "art", "games", "game", "wiki", "pro", "fm", "ly", "to", "sh", "so", "is", "im",
            "club", "top", "fun", "space", "website", "world", "today", "zone", "media", "studio", "design", "digital", "network", "services", "software",
            "codes", "tools", "chat", "social", "email", "mobi", "name", "travel", "museum", "aero", "jobs", "localhost", "test", "local", "onion", "lan", "home" };

        // what the address bar should open for what was typed; null = nothing (empty)
        public static string Interpret(string input, string engine)
        {
            if (input == null) return null;
            var s = input.Trim();
            if (s.Length == 0) return null;
            if (s.Equals("about:blank", StringComparison.OrdinalIgnoreCase)) return "about:blank";
            if (s.Equals(SettingsUrl, StringComparison.OrdinalIgnoreCase)) return SettingsUrl;
            if (s.Equals(BookmarksUrl, StringComparison.OrdinalIgnoreCase)) return BookmarksUrl;
            if (s.Equals(ExtensionsUrl, StringComparison.OrdinalIgnoreCase) || s.Equals("hallow://extensions/", StringComparison.OrdinalIgnoreCase)) return ExtensionsUrl;
            if (s.Equals(SitesUrl, StringComparison.OrdinalIgnoreCase) || s.Equals("hallow://sites", StringComparison.OrdinalIgnoreCase)) return SitesUrl;
            if (s.Equals(DownloadsUrl, StringComparison.OrdinalIgnoreCase) || s.Equals("hallow://downloads/", StringComparison.OrdinalIgnoreCase)) return DownloadsUrl;
            if (s.Equals(AppsUrl, StringComparison.OrdinalIgnoreCase) || s.Equals("hallow://apps/", StringComparison.OrdinalIgnoreCase)) return AppsUrl;
            var m = Regex.Match(s, @"^([a-zA-Z][a-zA-Z0-9+.\-]*):");
            if (m.Success && !LooksLikeHostPort(s))
            {
                var scheme = m.Groups[1].Value.ToLowerInvariant();
                if ((scheme == "http" || scheme == "https") && s.IndexOf(' ') < 0) { Uri u; if (Uri.TryCreate(s, UriKind.Absolute, out u) && u.Host.Length > 0) return u.AbsoluteUri; }
                if (scheme == "file") { Uri u; if (Uri.TryCreate(s, UriKind.Absolute, out u)) return u.AbsoluteUri; }
                return SearchUrl(engine, s);   // javascript:, data:, mailto: ... never run from the bar
            }
            if (s.IndexOf(' ') >= 0 || s.IndexOf('\t') >= 0) return SearchUrl(engine, s);
            // host[:port][/path...]
            var hostPart = s; int cut = hostPart.IndexOfAny(new[] { '/', '?', '#' }); var rest = "";
            if (cut >= 0) { rest = hostPart.Substring(cut); hostPart = hostPart.Substring(0, cut); }
            if (hostPart.Length == 0) return SearchUrl(engine, s);
            string host = hostPart, port = null;
            if (host.StartsWith("["))
            {
                int end = host.IndexOf(']'); if (end < 0) return SearchUrl(engine, s);
                var ip6 = host.Substring(1, end - 1); IPAddress a6;
                if (!IPAddress.TryParse(ip6, out a6)) return SearchUrl(engine, s);
                return "http://" + hostPart + (rest.Length == 0 ? "/" : rest);
            }
            int colon = host.LastIndexOf(':');
            if (colon >= 0) { port = host.Substring(colon + 1); host = host.Substring(0, colon); if (!Regex.IsMatch(port, "^[0-9]{1,5}$") || int.Parse(port) > 65535) return SearchUrl(engine, s); }
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return Make("http", hostPart, rest);
            IPAddress ip;
            if (Regex.IsMatch(host, @"^\d{1,3}(\.\d{1,3}){3}$")) return IPAddress.TryParse(host, out ip) ? Make("http", hostPart, rest) : SearchUrl(engine, s);
            if (host.IndexOf('.') < 0) return port != null ? Make("http", hostPart, rest) : SearchUrl(engine, s);   // "intranet:8080" yes, "cats" is a search
            var labels = host.TrimEnd('.').Split('.');
            if (labels.Any(l => l.Length == 0 || l.Length > 63 || !Regex.IsMatch(l, @"^[\p{L}\p{N}]([\p{L}\p{N}\-]*[\p{L}\p{N}])?$"))) return SearchUrl(engine, s);
            var tld = labels[labels.Length - 1];
            bool knownTld = Tlds.Contains(tld) || Regex.IsMatch(tld, "^[a-zA-Z]{2}$") || tld.StartsWith("xn--", StringComparison.OrdinalIgnoreCase);
            if (!knownTld) return SearchUrl(engine, s);   // "notes.txt", "file.exe" are searched, not opened
            return Make(port != null && (tld == "local" || tld == "lan" || tld == "home" || tld == "test") ? "http" : "https", hostPart, rest);
        }
        static bool LooksLikeHostPort(string s) { return Regex.IsMatch(s, @"^[a-zA-Z0-9.\-]+:\d{1,5}(/|$|\?|#)"); }
        static string Make(string scheme, string hostPort, string rest)
        {
            Uri u; return Uri.TryCreate(scheme + "://" + hostPort + (rest.Length == 0 ? "/" : rest), UriKind.Absolute, out u) ? u.AbsoluteUri : null;
        }

        // the text the address bar shows for a page ("" on the new tab page, so you can type at once)
        public static string Display(string url)
        {
            if (string.IsNullOrEmpty(url) || IsNewTab(url) || url == "about:blank") return "";
            return url;
        }
        public static bool IsNewTab(string url) { Uri u; return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == "https" && u.Host == NewTabHost; }
        public static bool IsWeb(string url) { Uri u; return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https") && u.Host != NewTabHost; }

        // "https://Example.com:443/x" -> "https://example.com"; "http://127.0.0.1:8080/a" -> "http://127.0.0.1:8080"; null if not http/https
        public static string Origin(string url)
        {
            Uri u; if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            if (u.Scheme != "http" && u.Scheme != "https") return null;
            string host; try { host = u.HostNameType == UriHostNameType.Dns ? new IdnMapping().GetAscii(u.Host).ToLowerInvariant() : u.Host.ToLowerInvariant(); } catch { return null; }
            if (host.Length == 0) return null;
            return u.Scheme + "://" + host + (u.IsDefaultPort ? "" : ":" + u.Port);
        }
        public static bool SameOrigin(string a, string b) { var x = Origin(a); return x != null && x == Origin(b); }
        public static string HostOf(string url) { Uri u; return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : null; }

        // address-bar suggestions: what you typed before that starts with (or contains) the text, best first
        public static List<string> Suggest(IEnumerable<string> history, string typed, int max)
        {
            var t = (typed ?? "").Trim(); if (t.Length == 0) return new List<string>();
            Func<string, string> bare = u => Regex.Replace(u, @"^https?://(www\.)?", "", RegexOptions.IgnoreCase);
            var list = history.Where(u => u != null).ToList();
            var starts = list.Where(u => bare(u).StartsWith(t, StringComparison.OrdinalIgnoreCase) || u.StartsWith(t, StringComparison.OrdinalIgnoreCase));
            var contains = list.Where(u => u.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            return starts.Concat(contains).Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList();
        }
    }
}
