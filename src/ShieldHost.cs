// Hallow Browser - Hallow Shield, built in. The browser ships Shield's Chrome build (package\shield) and always loads it into its
// profile as a WebView2 browser extension (CoreWebView2EnvironmentOptions.AreBrowserExtensionsEnabled + Profile.AddBrowserExtensionAsync).
// It can't be removed or turned off as an extension - there's no extension management at all - but everything in it can be set:
// levels, per-site Strict / Normal / Off and Pause, cookie banners, annoyances, the lists, My filters, the zapper (Shield's own popup
// and settings pages, opened from the Shield button and Settings).
// Updates: between browser releases, newer Shield versions come from Shield's own GitHub release (Dom428/hallow-shield, the
// hallow-shield-chrome-<v>.zip asset, checked against GitHub's sha256 digest) into %LOCALAPPDATA%\HallowBrowser\Shield\<version>;
// the newest of the bundled and downloaded copies is loaded at the next start.
// If the runtime can't load extensions, a network-level fallback blocks requests with Shield's own block rules (WebResourceRequested):
// only plain "||host^" / "||host/path" rules without site conditions - no cosmetic filtering, no per-site settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Web.Script.Serialization;

namespace HallowBrowser
{
    public static partial class ShieldHost
    {
        public const string Repo = "Dom428/hallow-shield";
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        public static string Bundled { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shield"); } }
        public static string UpdatesDir { get { return Path.Combine(BrowserSettings.LocalDir, "Shield"); } }

        // the version in a Shield folder's manifest.json (null if it isn't a Shield build)
        public static Version VersionOf(string folder)
        {
            try
            {
                var m = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(folder, "manifest.json")));
                object n, v; if (!m.TryGetValue("name", out n) || !(n as string ?? "").Contains("Shield")) return null;
                Version ver; return m.TryGetValue("version", out v) && Version.TryParse(v as string, out ver) ? ver : null;
            }
            catch { return null; }
        }
        // the newest Shield we have: the bundled one or a downloaded update
        public static string Pick(out Version version)
        {
            string best = null; version = null;
            var cands = new List<string> { Bundled };
            try { if (Directory.Exists(UpdatesDir)) cands.AddRange(Directory.GetDirectories(UpdatesDir)); } catch { }
            foreach (var c in cands)
            {
                var v = VersionOf(c);
                if (v != null && (version == null || v > version)) { version = v; best = c; }
            }
            return best;
        }

        // ---- the network-level fallback
        public class Rules
        {
            public readonly HashSet<string> Hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // ||host^
            public readonly Dictionary<string, List<string>> Paths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);   // ||host/path...
            public int Count { get { return Hosts.Count + Paths.Values.Sum(l => l.Count); } }
            public bool Blocks(string url)
            {
                Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) return false;
                var host = u.Host.ToLowerInvariant(); var path = u.PathAndQuery;
                for (var h = host; h.Length > 0; )
                {
                    if (Hosts.Contains(h)) return true;
                    List<string> ps; if (Paths.TryGetValue(h, out ps)) foreach (var p in ps) if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
                    int dot = h.IndexOf('.'); if (dot < 0) break; h = h.Substring(dot + 1);
                }
                return false;
            }
            public void Add(string urlFilter)
            {
                if (urlFilter == null || !urlFilter.StartsWith("||")) return;
                var f = urlFilter.Substring(2);
                if (f.IndexOfAny(new[] { '*', '|' }) >= 0) return;
                int slash = f.IndexOf('/');
                if (slash < 0)
                {
                    if (!f.EndsWith("^")) return;
                    var h = f.TrimEnd('^'); if (h.Length > 0 && h.IndexOf('^') < 0 && h.Contains('.')) Hosts.Add(h);
                    return;
                }
                var host = f.Substring(0, slash); var p = f.Substring(slash);
                if (host.Length == 0 || host.IndexOf('^') >= 0 || !host.Contains('.')) return;
                int caret = p.IndexOf('^'); if (caret >= 0) p = p.Substring(0, caret);
                if (p.Length < 2) return;
                List<string> l; if (!Paths.TryGetValue(host, out l)) Paths[host] = l = new List<string>();
                l.Add(p);
            }
        }
        // the plain block rules of the rule sets the manifest turns on (unit-tested with made-up rules)
        public static Rules LoadRules(string folder)
        {
            var r = new Rules();
            try
            {
                var m = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(folder, "manifest.json")));
                var dnr = m["declarative_net_request"] as Dictionary<string, object>;
                foreach (var o in (System.Collections.IEnumerable)dnr["rule_resources"])
                {
                    var rr = o as Dictionary<string, object>; object en, path;
                    if (rr == null || !rr.TryGetValue("enabled", out en) || !(en is bool) || !(bool)en || !rr.TryGetValue("path", out path)) continue;
                    var file = Path.Combine(folder, ((string)path).Replace('/', '\\'));
                    if (File.Exists(file)) AddRules(r, File.ReadAllText(file));
                }
            }
            catch { }
            return r;
        }
        public static void AddRules(Rules r, string json)
        {
            var list = js.Deserialize<object[]>(json);
            foreach (var o in list)
            {
                var rule = o as Dictionary<string, object>; if (rule == null) continue;
                var action = rule.ContainsKey("action") ? rule["action"] as Dictionary<string, object> : null;
                var cond = rule.ContainsKey("condition") ? rule["condition"] as Dictionary<string, object> : null;
                if (action == null || cond == null || (action.ContainsKey("type") ? action["type"] as string : null) != "block") continue;
                // site-specific or otherwise conditional rules are left to the real extension
                if (cond.Keys.Any(k => k != "urlFilter" && k != "resourceTypes")) continue;
                if (cond.ContainsKey("resourceTypes"))
                {
                    var types = ((System.Collections.IEnumerable)cond["resourceTypes"]).Cast<object>().Select(x => x as string).ToList();
                    if (types.Contains("main_frame")) continue;   // never block a page you opened yourself
                }
                r.Add(cond.ContainsKey("urlFilter") ? cond["urlFilter"] as string : null);
            }
        }

        // ---- Shield updates from its GitHub release (sha256-checked). Returns a message, or null when nothing happened.
        public static string CheckForUpdate()
        {
            Version have; Pick(out have);
            var rel = Updater.Latest(Repo);
            if (rel == null || rel.Assets == null) return null;
            if (have != null && rel.Version <= have) return null;
            var a = rel.Assets.FirstOrDefault(x => x.Name != null && x.Name.StartsWith("hallow-shield-chrome-", StringComparison.OrdinalIgnoreCase) && x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (a == null) return null;
            var tmp = Path.Combine(UpdatesDir, "download"); var target = Path.Combine(UpdatesDir, rel.Version.ToString());
            try
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                var zip = Path.Combine(tmp, "shield.zip");
                var err = Updater.DownloadChecked(a, zip);
                if (err != null) return "Shield update not installed: " + err + ".";
                var x = Path.Combine(tmp, "x");
                using (var z = ZipFile.OpenRead(zip))
                    foreach (var e in z.Entries)
                    {
                        if (e.FullName.EndsWith("/") || e.FullName.StartsWith("bridge/", StringComparison.OrdinalIgnoreCase)) continue;   // the family bridge is for desktop browsers
                        var dest = Path.GetFullPath(Path.Combine(x, e.FullName));
                        if (!dest.StartsWith(Path.GetFullPath(x) + "\\", StringComparison.OrdinalIgnoreCase)) continue;   // never outside the folder
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)); e.ExtractToFile(dest, true);
                    }
                var v = VersionOf(x);
                if (v == null || v != rel.Version) return "Shield update not installed: the download isn't Shield " + rel.Version.ToString(2) + ".";
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(x, target);
                return "Hallow Shield " + rel.Version.ToString(2) + " is ready - it's used from the next start.";
            }
            catch (Exception e) { return "Shield update failed: " + e.Message; }
            finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
        }
        // older downloaded copies (not the one in use) are removed
        public static void Tidy(string inUse)
        {
            try
            {
                if (!Directory.Exists(UpdatesDir)) return;
                foreach (var d in Directory.GetDirectories(UpdatesDir))
                    if (!string.Equals(Path.GetFullPath(d), Path.GetFullPath(inUse ?? ""), StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(d, true); } catch { }
            }
            catch { }
        }
    }
}
