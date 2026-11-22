// Hallow Browser 1.14 - logos for bookmarks never opened in Hallow:
//  1. From the other browser's own icon store: Chrome / Edge / Brave / Opera / Vivaldi keep site icons in their profile's "Favicons"
//     SQLite file (icon_mapping: page -> icon, favicon_bitmaps: the PNGs). Like the 1.3 importer, a COPY of the file is read (in a
//     temp folder, deleted afterwards), never the live one, and only icons for sites that are in Hallow's bookmarks or history are
//     taken. Never passwords, never cookies. Done with every import from now on, and once at start for an earlier import: when the
//     cache is empty and Hallow's bookmarks came from a browser on this PC (its bookmarks file has the same pages).
//  2. "Fetch missing site logos" (Settings > Privacy, on by default): one quiet fetch per site of the site's own /favicon.ico or, if
//     that isn't an image, the icon its home page names (<link rel=icon>, the page's scripts off) - in a hidden view of Hallow's own
//     engine and profile, so Shield and secure DNS apply. Never a third-party icon service. Three sites at a time, only when you
//     haven't touched the PC for a little while and no tab is loading, never during a game; a site that has none is tried again
//     after 30 days at the earliest.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public static partial class Importer
    {
        // Chromium's Favicons file -> the best bitmap (closest to 32 px) per site, only for the wanted sites (keys from SiteIcons.Key)
        public static Dictionary<string, byte[]> ChromiumFavicons(SqliteReader r, ICollection<string> wanted)
        {
            var res = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (wanted == null || wanted.Count == 0) return res;
            var want = new HashSet<string>(wanted, StringComparer.Ordinal);
            var pages = new Dictionary<string, List<KeyValuePair<int, long>>>(StringComparer.Ordinal);   // key -> (page url length, icon id)
            foreach (var row in r.Read("icon_mapping"))
            {
                var u = row.ContainsKey("page_url") ? row["page_url"] as string : null; object iid; row.TryGetValue("icon_id", out iid);
                if (!(iid is long)) continue;
                var k = SiteIcons.Key(u); if (k == null || !want.Contains(k)) continue;
                List<KeyValuePair<int, long>> l; if (!pages.TryGetValue(k, out l)) pages[k] = l = new List<KeyValuePair<int, long>>();
                if (l.Count < 50) l.Add(new KeyValuePair<int, long>(u.Length, (long)iid));
            }
            if (pages.Count == 0) return res;
            var needed = new HashSet<long>(pages.Values.SelectMany(l => l.Select(x => x.Value)));
            var best = new Dictionary<long, KeyValuePair<int, byte[]>>();   // icon id -> (score, png)
            foreach (var row in r.Read("favicon_bitmaps"))
            {
                object iid, data, w; row.TryGetValue("icon_id", out iid); row.TryGetValue("image_data", out data); row.TryGetValue("width", out w);
                if (!(iid is long) || !needed.Contains((long)iid)) continue;
                var png = data as byte[]; if (png == null || png.Length < 8) continue;
                int width = w is long ? (int)(long)w : 16; if (width <= 0) continue;
                int score = width == 32 ? 0 : width > 32 ? width - 32 : (32 - width) * 3;
                KeyValuePair<int, byte[]> cur;
                if (!best.TryGetValue((long)iid, out cur) || score < cur.Key) best[(long)iid] = new KeyValuePair<int, byte[]>(score, png);
            }
            foreach (var kv in pages)
            {
                // the site's home page icon first (the shortest address), then the best of the rest
                foreach (var p in kv.Value.OrderBy(x => x.Key))
                {
                    KeyValuePair<int, byte[]> b;
                    if (best.TryGetValue(p.Value, out b)) { res[kv.Key] = b.Value; break; }
                }
            }
            return res;
        }
        // a profile's icons (from a copy of its Favicons file); null when it has none. IOException = the file is in use.
        public static Dictionary<string, byte[]> Favicons(FoundProfile p, ICollection<string> wanted, string tmp)
        {
            if (p == null || p.Kind != "chromium" || wanted == null || wanted.Count == 0) return null;
            var f = Path.Combine(p.Dir, "Favicons"); if (!File.Exists(f)) return null;
            var copy = CopyIn(f, tmp);
            try { return ChromiumFavicons(SqliteReader.Open(copy), wanted); }
            finally { try { File.Delete(copy); File.Delete(copy + "-wal"); } catch { } }
        }
        // the sites (keys) in Hallow's bookmarks and history that have no icon yet
        public static List<string> MissingKeys(IEnumerable<string> urls)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); var res = new List<string>();
            foreach (var u in urls) { var k = SiteIcons.Key(u); if (k != null && seen.Add(k) && !SiteIcons.HasExact(k)) res.Add(k); }
            return res;
        }
        static List<string> Flatten(IEnumerable<ImportNode> nodes)
        {
            var res = new List<string>();
            Action<IEnumerable<ImportNode>, int> walk = null;
            walk = (l, d) => { if (l == null || d > 64) return; foreach (var n in l) { if (n == null) continue; if (n.Folder) walk(n.Children, d + 1); else if (n.Url != null) res.Add(n.Url); } };
            walk(nodes, 0); return res;
        }

        public class LogoResult { public int Saved; public List<string> From = new List<string>(); public bool InUse; public string Skipped; public int Profiles; }
        // logos from the browsers on this PC for Hallow's bookmarks and history. matchBookmarks: only browsers whose bookmarks file has
        // pages Hallow has (= where the bookmarks came from), most in common first; onlyIfEmpty: only when the cache is empty.
        public static LogoResult LogosFromBrowsers(List<FoundProfile> profiles, ICollection<string> bookmarkUrls, ICollection<string> historyUrls, bool matchBookmarks, bool onlyIfEmpty)
        {
            var res = new LogoResult();
            if (onlyIfEmpty && SiteIcons.Count() > 0) { res.Skipped = "the logo cache isn't empty"; return res; }
            if (bookmarkUrls == null || bookmarkUrls.Count == 0) { res.Skipped = "no bookmarks"; return res; }
            var chromium = (profiles ?? new List<FoundProfile>()).Where(p => p.Kind == "chromium").ToList();
            if (chromium.Count == 0) { res.Skipped = "no Chromium browser on this PC"; return res; }
            var tmp = TempDir();
            try
            {
                var mine = new HashSet<string>(bookmarkUrls, StringComparer.Ordinal);
                var order = new List<KeyValuePair<FoundProfile, int>>();
                foreach (var p in chromium)
                {
                    int common = 0;
                    try { var tree = Bookmarks(p, tmp); if (tree != null) common = Flatten(tree).Count(u => mine.Contains(u)); } catch { }
                    if (common > 0 || !matchBookmarks) order.Add(new KeyValuePair<FoundProfile, int>(p, common));
                }
                if (order.Count == 0) { res.Skipped = "your bookmarks didn't come from a browser on this PC"; return res; }
                var all = bookmarkUrls.Concat(historyUrls ?? new string[0]).ToList();
                foreach (var p in order.OrderByDescending(x => x.Value).Select(x => x.Key))
                {
                    var missing = MissingKeys(all); if (missing.Count == 0) break;
                    res.Profiles++;
                    try
                    {
                        var icons = Favicons(p, missing, tmp);
                        var n = SiteIcons.AddMissing(icons);
                        if (n > 0) { res.Saved += n; if (!res.From.Contains(p.Browser)) res.From.Add(p.Browser); }
                    }
                    catch (IOException) { res.InUse = true; }
                    catch { }
                }
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
            return res;
        }
    }

    public partial class BrowserWindow
    {
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        public static double IdleSeconds()
        {
            try { var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) }; if (!GetLastInputInfo(ref i)) return 0; return unchecked((uint)Environment.TickCount - i.dwTime) / 1000.0; } catch { return 0; }
        }
        static string Env114(string n) { if (BrowserSettings.TestDir == null) return null; var v = Environment.GetEnvironmentVariable(n); return string.IsNullOrWhiteSpace(v) ? null : v; }
        // tests: the one-time import and the fetcher run by themselves only with HALLOW_BROWSER_LOGOS_AUTO=1 (else only by test commands);
        // HALLOW_BROWSER_LOGOS_FAST=1 = no waiting for idle
        static bool LogosAuto { get { return BrowserSettings.TestDir == null || Env114("HALLOW_BROWSER_LOGOS_AUTO") == "1"; } }
        static bool LogosFast { get { return Env114("HALLOW_BROWSER_LOGOS_FAST") == "1"; } }

        static bool logosStarted, logoImportBusy, logoOnceTried; bool logoFetchBusy; DispatcherTimer logoTimer, iconRedraw;
        public static string LastLogoImport, LastLogoFetch; public static int LogoFetchTries;
        public bool LogoFetchBusy { get { return logoFetchBusy; } }

        // after the stores load (StoresLoaded): every window redraws when icons arrive; the first normal window runs the rest
        void StartLogos()
        {
            if (snap) return;
            Action<string> changed = k => { try { Dispatcher.BeginInvoke(new Action(IconsArrived)); } catch { } };
            SiteIcons.Changed += changed; Closed += (a, b) => SiteIcons.Changed -= changed;
            if (Private || logosStarted || !LogosAuto) return;
            logosStarted = true;
            int first = LogosFast ? 2 : 8, every = LogosFast ? 3 : 20;   // 1.14.1: sooner and more often (it waited for an idle PC)
            var started = DateTime.Now;
            logoTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(first) };
            logoTimer.Tick += (a, b) =>
            {
                logoTimer.Interval = TimeSpan.FromSeconds(every);
                if (!Program.Windows.Contains(this)) { logoTimer.Stop(); return; }
                if (!S.Logos1141 && !logoOnceTried) { if (Game == null) { logoOnceTried = true; LogosOnce(false); } return; }
                LogoFetchTick(false);
            };
            logoTimer.Start();
            Closed += (a, b) => { logoTimer.Stop(); logosStarted = false; };
        }
        void IconsArrived()
        {
            if (iconRedraw == null)
            {
                iconRedraw = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1200) };
                iconRedraw.Tick += (a, b) => { iconRedraw.Stop(); RenderBookmarksBar(); if (bmPage != null && bmList != null) RefreshBookmarksList(); };
            }
            iconRedraw.Stop(); iconRedraw.Start();
        }
        List<string> BookmarkUrls() { return Bookmarks != null && Bookmarks.Loaded ? Bookmarks.All().Select(b => b.Url).Where(Omni.IsWeb).ToList() : new List<string>(); }
        List<string> HistoryUrls(int max) { return History != null && History.Loaded ? History.All().OrderByDescending(h => h.Visits).Take(max).Select(h => h.Url).ToList() : new List<string>(); }

        // ---- 1. the one-time logos from the browser the bookmarks came from (force = the Settings button: any Chromium browser, now)
        public Task<Importer.LogoResult> LogosOnce(bool force)
        {
            if (snap || Private || logoImportBusy || (!force && (S.Logos1141 || Game != null))) return Task.FromResult<Importer.LogoResult>(null);
            logoImportBusy = true;
            var bm = BookmarkUrls(); var hist = HistoryUrls(5000); var profiles = FindBrowsers(); var s = S;
            return Task.Run(() => Importer.LogosFromBrowsers(profiles, bm, hist, false, false)).ContinueWith(t =>
            {
                logoImportBusy = false;
                var r = t.Exception == null ? t.Result : new Importer.LogoResult { Skipped = "error: " + t.Exception.GetBaseException().Message };
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LastLogoImport = r.Saved > 0 ? "saved " + r.Saved + " from " + string.Join(", ", r.From) : r.InUse ? "in use" : r.Skipped ?? "nothing new";
                    if (!force)
                    {
                        if (r.InUse && r.Saved == 0 && s.Logos114Tries < 7) s.Logos114Tries++;   // the other browser is open - next start
                        else { s.Logos114 = true; s.Logos1141 = true; }
                        s.Save();
                    }
                    if (r.Saved > 0) Toast("Your bookmarks have their site logos now: " + r.Saved + (r.Saved == 1 ? " logo" : " logos") + " copied from " + string.Join(" and ", r.From) + "'s own icon store (a copy of it; nothing else was read).", 8);
                    else if (force) Toast(r.InUse ? "Your other browser is using its icon file - close it and try again." : "No new logos: " + (r.Skipped ?? "your other browsers have none for these sites") + ".", 5);
                }));
                return r;
            });
        }

        // ---- 2. the quiet fetch, once per site
        public static string LogoOrigin(string url)
        {
            var o = Omni.Origin(url); if (o == null) return null;
            var host = Omni.HostOf(url);
            if (o.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !HttpsRules.IsLocalHost(host)) o = "https://" + o.Substring(7);   // HTTPS-only: never plain http to the internet
            return o;
        }
        public List<string> LogoCandidates(int max)
        {
            var res = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal); var now = DateTime.Now;
            foreach (var u in BookmarkUrls().Concat(HistoryUrls(60)))
            {
                var k = SiteIcons.Key(u); if (k == null || !seen.Add(k) || SiteIcons.HasExact(k) || LogoTried.Recent(k, now)) continue;
                var o = LogoOrigin(u); if (o == null) continue;
                res.Add(o); if (res.Count >= max) break;
            }
            return res;
        }
        public bool LogoFetchAllowed(bool ignoreIdle)
        {
            if (snap || Private || !S.FetchLogos || logoFetchBusy || Game != null) return false;
            if (ignoreIdle || LogosFast) return true;
            return !Tabs.Any(t => t.Loading);
        }
        public Task<List<string>> LogoFetchTick(bool ignoreIdle)
        {
            if (!LogoFetchAllowed(ignoreIdle)) return Task.FromResult<List<string>>(null);
            var c = LogoCandidates(4); if (c.Count == 0) return Task.FromResult(new List<string>());
            return FetchLogos(c);
        }
        // a hidden view (never shown) on this profile: muted, no pop-ups, no downloads, no permissions, scripts off, and only the page
        // itself and images load
        public async Task<List<string> > FetchLogos(List<string> origins)
        {
            var got = new List<string>(); if (logoFetchBusy || origins == null || origins.Count == 0) return got;
            logoFetchBusy = true; WebView2 v = null;
            try
            {
                v = new WebView2 { Visibility = Visibility.Hidden, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                content.Children.Add(v);
                await Engine.Ensure(v);
                var c = v.CoreWebView2; QuietView(c);
                foreach (var o in origins)
                {
                    if (Game != null || !S.FetchLogos) break;
                    var key = SiteIcons.Key(o); bool ok = false;
                    try { ok = await FetchLogo(c, o); } catch { }
                    LogoFetchTries++; LogoTried.Mark(key, ok, DateTime.Now);
                    if (ok) got.Add(key);
                }
            }
            catch (Exception e) { LastLogoFetch = "error: " + e.Message; }
            finally { logoFetchBusy = false; if (v != null) { content.Children.Remove(v); try { v.Dispose(); } catch { } } }
            if (LastLogoFetch == null || !LastLogoFetch.StartsWith("error")) LastLogoFetch = DateTime.Now.ToString("HH:mm:ss") + " " + got.Count + " of " + origins.Count;
            return got;
        }
        static void QuietView(CoreWebView2 c)
        {
            c.IsMuted = true;
            c.Settings.IsScriptEnabled = false; c.Settings.AreDefaultScriptDialogsEnabled = false; c.Settings.IsStatusBarEnabled = false;
            c.Settings.IsPasswordAutosaveEnabled = false; c.Settings.IsGeneralAutofillEnabled = false;
            c.NewWindowRequested += (a, b) => b.Handled = true;
            c.DownloadStarting += (a, b) => b.Cancel = true;
            c.PermissionRequested += (a, b) => b.State = CoreWebView2PermissionState.Deny;
            c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            c.WebResourceRequested += (a, b) =>
            {
                var k = b.ResourceContext;
                if (k == CoreWebView2WebResourceContext.Document || k == CoreWebView2WebResourceContext.Image || k == CoreWebView2WebResourceContext.Other) return;
                try { b.Response = c.Environment.CreateWebResourceResponse(null, 403, "Blocked", ""); } catch { }
            };
        }
        static async Task<bool> FetchLogo(CoreWebView2 c, string origin)
        {
            var bytes = await IcoFile(c, origin);
            if (bytes != null && SiteIcons.Save(origin, bytes, false)) return true;
            if (bytes != null && SiteIcons.HasExact(origin)) return true;   // the same icon as before
            var png = await PageIcon(c, origin);
            return png != null && (SiteIcons.Save(origin, png, false) || SiteIcons.HasExact(origin));
        }
        // the site's /favicon.ico, read from the response itself (an image that decodes, else null)
        static async Task<byte[]> IcoFile(CoreWebView2 c, string origin)
        {
            var url = origin.TrimEnd('/') + "/favicon.ico";
            var tcs = new TaskCompletionSource<byte[]>();
            EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs> h = async (a, b) =>
            {
                try
                {
                    var st = b.Response.StatusCode;
                    if (st >= 300 && st < 400) return;   // a redirect: the next response
                    if (st != 200) { tcs.TrySetResult(null); return; }
                    using (var s = await b.Response.GetContentAsync())
                    {
                        if (s == null) { tcs.TrySetResult(null); return; }
                        var ms = new MemoryStream(); s.CopyTo(ms);
                        var data = ms.ToArray(); tcs.TrySetResult(SiteIcons.Normalize(data) != null ? data : null);
                    }
                }
                catch { tcs.TrySetResult(null); }
            };
            EventHandler<CoreWebView2NavigationCompletedEventArgs> done = (a, b) => { if (!b.IsSuccess) tcs.TrySetResult(null); else { var _ = Task.Delay(1500).ContinueWith(x => tcs.TrySetResult(null)); } };
            c.WebResourceResponseReceived += h; c.NavigationCompleted += done;
            try { c.Navigate(url); var w = await Task.WhenAny(tcs.Task, Task.Delay(9000)); return w == tcs.Task ? tcs.Task.Result : null; }
            finally { c.WebResourceResponseReceived -= h; c.NavigationCompleted -= done; }
        }
        // the icon the home page names (or the engine's own /favicon.ico guess), as the engine gives it (PNG)
        static async Task<byte[]> PageIcon(CoreWebView2 c, string origin)
        {
            await Panels.NavigateAndWait(c, "about:blank");
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<object> fh = (a, b) => { if (!string.IsNullOrEmpty(c.FaviconUri)) tcs.TrySetResult(true); };
            c.FaviconChanged += fh;
            try
            {
                await Panels.NavigateAndWait(c, origin.TrimEnd('/') + "/");
                if (string.IsNullOrEmpty(c.FaviconUri)) await Task.WhenAny(tcs.Task, Task.Delay(4000));
                if (string.IsNullOrEmpty(c.FaviconUri)) return null;
                // the page that answered must be the site (or its own domain), not a sign-in page somewhere else
                var want = SiteIcons.Key(origin); var at = SiteIcons.Key(c.Source);
                if (want == null || at == null || !(SiteIcons.Chain(want).Contains(at) || SiteIcons.Chain(at).Contains(want) || SiteIcons.Chain(want).Last() == SiteIcons.Chain(at).Last())) return null;
                using (var s = await c.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                {
                    if (s == null || s.Length == 0) return null;
                    var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
                }
            }
            finally { c.FaviconChanged -= fh; }
        }

        // Settings > Privacy: the switch and the "get logos now" button
        public UIElement[] LogoSettings()
        {
            return new UIElement[] {
                Ui.Switch("Fetch missing site logos", "For bookmarks you haven't opened here yet: each site's own icon, fetched once by Hallow itself (Shield and secure DNS apply) when the PC is idle, never during games - no third-party icon service.", S.FetchLogos, on => { S.FetchLogos = on; S.Save(); }),
                Ui.Row(Ui.Btn("Get logos from my other browsers", () => { var _ = LogosOnce(true); }, false, "file-import", "Copies the icons Chrome, Edge or Brave already has for your bookmarks and history (from a copy of its icon file - never passwords or cookies)")) };
        }
    }

    // sites a fetch found nothing for: tried again after 30 days at the earliest (favicons\tried.txt: key, date, ok)
    public static class LogoTried
    {
        static readonly object gate = new object(); static Dictionary<string, DateTime> map;
        static string FilePath { get { return Path.Combine(SiteIcons.Dir, "tried.txt"); } }
        public static void Reset() { lock (gate) map = null; }
        static void Load()
        {
            if (map != null) return; map = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var l in File.ReadAllLines(FilePath)) { var p = l.Split('\t'); DateTime d; if (p.Length >= 2 && DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) map[p[0]] = d; }
            }
            catch { }
        }
        public static bool Recent(string key, DateTime now) { lock (gate) { Load(); DateTime d; return key != null && map.TryGetValue(key, out d) && now - d < TimeSpan.FromDays(30); } }
        public static void Mark(string key, bool ok, DateTime now)
        {
            if (key == null || SiteIcons.ReadOnly) return;
            lock (gate)
            {
                Load(); if (ok) map.Remove(key); else map[key] = now;
                try { Directory.CreateDirectory(SiteIcons.Dir); File.WriteAllLines(FilePath, map.OrderBy(x => x.Key, StringComparer.Ordinal).Take(5000).Select(x => x.Key + "\t" + x.Value.ToString("s", CultureInfo.InvariantCulture))); } catch { }
            }
        }
    }
}
