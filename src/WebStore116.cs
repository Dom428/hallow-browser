// Hallow Browser 1.16 - Chrome Web Store: the request, the unpacking, what an extension asks for, and the browser's side (the
// "Add to Hallow Browser" button on store pages, the pending card on hallow://extensions, updates).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class WebStore
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public const string Endpoint = "https://clients2.google.com/service/update2/crx";
        public const string StoreHost = "chromewebstore.google.com";
        public const long MaxCrx = 150L * 1024 * 1024;
        // tests: HALLOW_BROWSER_CWS_URL = a local stand-in for the update endpoint (never Google's)
        public static string Base { get { return BrowserSettings.TestDir != null ? (Environment.GetEnvironmentVariable("HALLOW_BROWSER_CWS_URL") ?? "http://127.0.0.1:9/none") : Endpoint; } }
        public static string Prod { get { var v = Engine.RuntimeVersion ?? "130.0.0.0"; return Regex.IsMatch(v, @"^\d+(\.\d+){1,3}$") ? v : "130.0.0.0"; } }

        // a store link or a bare ID -> the ID (null when it isn't one)
        public static string IdFrom(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null; text = text.Trim();
            if (Crx.IdRx.IsMatch(text)) return text;
            Uri u; if (!Uri.TryCreate(text, UriKind.Absolute, out u) || (u.Scheme != "https" && u.Scheme != "http")) return null;
            var h = u.Host.ToLowerInvariant();
            if (h != StoreHost && h != "chrome.google.com") return null;
            if (h == "chrome.google.com" && !u.AbsolutePath.StartsWith("/webstore/", StringComparison.OrdinalIgnoreCase)) return null;
            var m = Regex.Match(u.AbsolutePath, "/detail/(?:[^/]+/)?([a-p]{32})(?:/|$)"); return m.Success ? m.Groups[1].Value : null;
        }
        public static string DownloadUrl(string id) { return Base + "?response=redirect&prodversion=" + Prod + "&acceptformat=crx3&x=" + Uri.EscapeDataString("id=" + id + "&installsource=ondemand&uc"); }
        public static string UpdateUrl(string id, string version) { return Base + "?response=updatecheck&prodversion=" + Prod + "&acceptformat=crx3&x=" + Uri.EscapeDataString("id=" + id + "&v=" + (version ?? "0.0.0.0") + "&uc"); }

        static HttpWebRequest Req(string url)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "HallowBrowser/" + Program.Version; r.Timeout = 30000; r.ReadWriteTimeout = 30000; r.AllowAutoRedirect = true; r.MaximumAutomaticRedirections = 6; r.CookieContainer = null;
            return r;
        }
        // the CRX for one ID (only from the endpoint and where it redirects: Google's own download hosts; tests: the stand-in)
        public static byte[] Fetch(string id, out string error)
        {
            error = null;
            try
            {
                using (var resp = (HttpWebResponse)Req(DownloadUrl(id)).GetResponse())
                {
                    var host = resp.ResponseUri.Host.ToLowerInvariant();
                    if (BrowserSettings.TestDir == null && !(host.EndsWith(".google.com") || host.EndsWith(".googleusercontent.com") || host.EndsWith(".gvt1.com") || host == "gvt1.com")) { error = "the store sent the file from an unexpected place (" + host + ")"; return null; }
                    if (resp.StatusCode == HttpStatusCode.NoContent || resp.ContentLength == 0) { error = "the store has no such extension (or it isn't available for this browser)"; return null; }
                    using (var s = resp.GetResponseStream()) using (var ms = new MemoryStream())
                    {
                        var buf = new byte[65536]; int n; while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > MaxCrx) { error = "the file is bigger than 150 MB"; return null; } }
                        if (ms.Length == 0) { error = "the store has no such extension (or it isn't available for this browser)"; return null; }
                        return ms.ToArray();
                    }
                }
            }
            catch (WebException e) { var r = e.Response as HttpWebResponse; error = r != null ? "the store answered " + (int)r.StatusCode + " " + r.StatusDescription : e.Message; return null; }
            catch (Exception e) { error = e.Message; return null; }
        }
        // "is there a newer version?" -> the version the store has (null = none / unknown)
        public static string CheckUpdate(string id, string have, out string error)
        {
            error = null;
            try
            {
                string xml; using (var resp = (HttpWebResponse)Req(UpdateUrl(id, have)).GetResponse()) using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) xml = rd.ReadToEnd();
                return ParseUpdate(xml, id);
            }
            catch (Exception e) { error = e.Message; return null; }
        }
        public static string ParseUpdate(string xml, string id)
        {
            try
            {
                var d = new XmlDocument { XmlResolver = null }; d.LoadXml(xml);
                foreach (XmlElement app in d.GetElementsByTagName("app"))
                {
                    if (app.GetAttribute("appid") != id) continue;
                    foreach (XmlElement uc in app.GetElementsByTagName("updatecheck")) { var v = uc.GetAttribute("version"); if (uc.GetAttribute("status") == "ok" && Regex.IsMatch(v, @"^\d+(\.\d+){0,3}$")) return v; }
                }
            }
            catch { }
            return null;
        }
        public static bool Newer(string a, string b)
        {
            Func<string, int[]> p = s => (s ?? "0").Split('.').Select(x => { int n; return int.TryParse(x, out n) ? n : 0; }).Concat(new int[4]).Take(4).ToArray();
            var x1 = p(a); var y1 = p(b); for (int i = 0; i < 4; i++) if (x1[i] != y1[i]) return x1[i] > y1[i]; return false;
        }

        // the zip unpacked (nothing outside the folder, Chrome's _metadata left out), the store key put into the manifest so the
        // extension keeps its store ID; returns the folder
        public static string Unpack(Crx.Result c, string name)
        {
            var dest = Path.Combine(ExtStore.Dir, ExtStore.Slug(name) + "-" + c.Id.Substring(0, 8) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dest); var root = Path.GetFullPath(dest) + "\\"; long total = 0;
            try
            {
                using (var z = new ZipArchive(new MemoryStream(c.Zip), ZipArchiveMode.Read))
                    foreach (var e in z.Entries)
                    {
                        if (e.FullName.EndsWith("/") || e.FullName.StartsWith("_metadata/", StringComparison.OrdinalIgnoreCase)) continue;
                        var to = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('/', '\\')));
                        if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("a file in it points outside its folder");
                        total += e.Length; if (total > 400L * 1024 * 1024) throw new IOException("it unpacks to more than 400 MB");
                        Directory.CreateDirectory(Path.GetDirectoryName(to)); e.ExtractToFile(to, true);
                    }
                var mf = Path.Combine(dest, "manifest.json"); if (!File.Exists(mf)) throw new IOException("it has no manifest.json");
                var m = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(mf));
                m["key"] = Convert.ToBase64String(c.PublicKey);
                File.WriteAllText(mf, js.Serialize(m), new UTF8Encoding(false));
                return dest;
            }
            catch { try { Directory.Delete(dest, true); } catch { } throw; }
        }

        // ---- what an extension asks for, in words (from its manifest), and what WebView2 won't give it
        public static Dictionary<string, object> Manifest(byte[] zip)
        {
            using (var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var e = z.GetEntry("manifest.json"); if (e == null) return null;
                using (var s = new StreamReader(e.Open(), Encoding.UTF8)) return js.Deserialize<Dictionary<string, object>>(s.ReadToEnd());
            }
        }
        static IEnumerable<string> Strs(Dictionary<string, object> m, string k) { object o; return m != null && m.TryGetValue(k, out o) && o is System.Collections.IEnumerable && !(o is string) ? ((System.Collections.IEnumerable)o).OfType<string>() : Enumerable.Empty<string>(); }
        static readonly Dictionary<string, string> words = new Dictionary<string, string> {
            { "tabs", "See your open tabs' addresses and titles" }, { "history", "Read and change your browsing history" }, { "bookmarks", "Read and change your bookmarks" },
            { "cookies", "Read and change cookies" }, { "webRequest", "See the requests pages make" }, { "declarativeNetRequest", "Block or change requests" },
            { "downloads", "Manage your downloads" }, { "clipboardRead", "Read what you copy" }, { "clipboardWrite", "Change what you copy" },
            { "notifications", "Show notifications" }, { "storage", "Keep its own data on this PC" }, { "scripting", "Run its scripts in pages it may access" },
            { "activeTab", "Work on the page you're on when you click it" }, { "contextMenus", "Add right-click menu items" }, { "alarms", "Run on a timer" },
            { "nativeMessaging", "Talk to a desktop app on this PC" }, { "identity", "Sign you in with a Google account" }, { "privacy", "Change privacy settings" },
            { "proxy", "Change the proxy" }, { "management", "Manage other extensions" }, { "webNavigation", "See where your tabs go" }, { "unlimitedStorage", "Keep unlimited data on this PC" },
            { "geolocation", "Know your location" }, { "offscreen", "Run hidden pages" }, { "sidePanel", "Open a side panel" }, { "idle", "Know when this PC is idle" }, { "power", "Keep this PC awake" } };
        // in WebView2 these are missing or limited (measured with real extensions where noted on hallow://extensions)
        public static readonly Dictionary<string, string> Missing = new Dictionary<string, string> {
            { "nativeMessaging", "native messaging - WebView2 can't connect an extension to a desktop app" },
            { "identity", "Google sign-in for extensions (chrome.identity) - not in WebView2" },
            { "sidePanel", "side panels - not in WebView2" }, { "management", "managing other extensions - not in WebView2" },
            { "enterprise.platformKeys", "enterprise keys - not in WebView2" }, { "webAuthenticationProxy", "passkey proxying - not in WebView2" },
            // 1.16.2 (real use: an extension asking for these was added, and couldn't work)
            { "debugger", "controlling pages through the debugger (chrome.debugger) - not in WebView2" }, { "tabGroups", "tab groups (chrome.tabGroups) - not in WebView2" },
            { "offscreen", "hidden helper pages (chrome.offscreen) - not in WebView2" }, { "tabCapture", "recording a tab (chrome.tabCapture) - not in WebView2" },
            { "desktopCapture", "recording the screen (chrome.desktopCapture) - not in WebView2" }, { "pageCapture", "saving pages as MHTML (chrome.pageCapture) - not in WebView2" },
            { "gcm", "push messages from Google (chrome.gcm) - not in WebView2" }, { "ttsEngine", "being a text-to-speech voice (chrome.ttsEngine) - not in WebView2" },
            { "documentScan", "scanners (chrome.documentScan) - not in WebView2" }, { "printing", "printers (chrome.printing) - not in WebView2" } };
        // 1.16.2: the plain warning shown above the list when anything is missing
        public const string GapWarning = "This extension needs Chrome features Hallow Browser doesn't have, so it probably won't work. You can still add it.";
        public static List<string> Asks(Dictionary<string, object> m)
        {
            var res = new List<string>();
            var hosts = Strs(m, "host_permissions").Concat(Strs(m, "permissions").Where(p => p.Contains("://") || p == "<all_urls>")).ToList();
            object cs; if (m != null && m.TryGetValue("content_scripts", out cs) && cs is System.Collections.IEnumerable) foreach (var x in ((System.Collections.IEnumerable)cs).OfType<Dictionary<string, object>>()) hosts.AddRange(Strs(x, "matches"));
            hosts = hosts.Distinct().ToList();
            if (hosts.Any(h => h == "<all_urls>" || h.StartsWith("*://*/") || h.StartsWith("http://*/") || h.StartsWith("https://*/"))) res.Add("Read and change everything on every website you open");
            else if (hosts.Count > 0) res.Add("Read and change your data on " + string.Join(", ", hosts.Select(h => Regex.Replace(h, @"^[a-z*]+://|/.*$", "")).Distinct().Take(6)) + (hosts.Count > 6 ? " and " + (hosts.Count - 6) + " more" : ""));
            foreach (var p in Strs(m, "permissions")) { string w; if (words.TryGetValue(p, out w)) res.Add(w); }
            var opt = Strs(m, "optional_permissions").Where(p => words.ContainsKey(p)).ToList();
            if (opt.Count > 0) res.Add("May ask later: " + string.Join(", ", opt.Select(p => words[p].ToLowerInvariant())));
            return res.Distinct().ToList();
        }
        public static List<string> Gaps(Dictionary<string, object> m)
        {
            var all = Strs(m, "permissions").Concat(Strs(m, "optional_permissions")).ToList();
            return Missing.Where(kv => all.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        }
        public static string Str(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
    }

    [System.Runtime.Serialization.DataContract]
    public class StoreInfo { [System.Runtime.Serialization.DataMember] public string Id, Checked, Latest; }

    // one store install waiting for your OK (shown on hallow://extensions)
    public class PendingExt { public string Id, Name, Version, Error; public Crx.Result Crx; public List<string> Asks = new List<string>(), Gaps = new List<string>(); public bool Update; public Compat Compat; }
}
