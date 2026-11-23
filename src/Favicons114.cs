// Hallow Browser 1.14 - site logos everywhere. "Will the logos appear for each bookmark?" - they do now.
//  - A favicon cache per site (SiteIcons): whenever a tab's (or a sidebar panel's) icon changes it is kept as a small PNG (at most
//    32 px, 64 KB) in the profile: %LOCALAPPDATA%\HallowBrowser\Profile\favicons\<hash of the host>.png (another Hallow profile: its own
//    folder). Keyed by host ("www." dropped); at most 2,000 sites, the least recently used go first. Private windows never write to it.
//  - Used everywhere a site is listed: the bookmarks bar, its folder menus, the bookmarks manager, the History page, the new tab
//    page's tiles, Ctrl+K and the address bar's suggestions. Fallback: the cached icon (the host, then its parent domain) -> a Font
//    Awesome brand icon for well-known sites -> a coloured letter tile (the site's first letter, the colour from a hash of the host),
//    never the plain globe.
//  - Logos for bookmarks never opened in Hallow: from the other browser's own icon store (Logos114.cs) and, when "Fetch missing site
//    logos" is on, one quiet fetch per site of its own /favicon.ico or the icon its page names, through Hallow's own engine.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        [DataMember] public bool FetchLogos = true;    // 1.14: one quiet fetch per site of its own icon, for bookmarks never opened here
        [DataMember] public bool Logos114;             // 1.14: the one-time "logos from the browser your bookmarks came from" ran
        [DataMember] public int Logos114Tries;         // ... tries that found the other browser's icon file in use
        [DataMember] public bool Logos1141;            // 1.14.1: the copy from your other browsers ran again (1.14's could skip itself)
        void Defaults114() { FetchLogos = true; }
    }

    public static class SiteIcons
    {
        public const int Px = 32, MaxBytes = 64 * 1024;
        public static int MaxFiles = 2000;
        static readonly object gate = new object();
        static string dir; static int count = -1;
        static readonly Dictionary<string, ImageSource> mem = new Dictionary<string, ImageSource>(StringComparer.Ordinal);   // key -> image (null = none)
        static readonly HashSet<string> touched = new HashSet<string>(StringComparer.Ordinal);
        public static bool ReadOnly;                       // --snap: nothing written
        static Dictionary<string, byte[]> fake;
        public static Dictionary<string, byte[]> Fake { get { return fake; } set { lock (gate) { fake = value; mem.Clear(); } } }   // --snap: made-up icons by key (the cache folder isn't read at all)
        public static event Action<string> Changed;        // a site's icon was added or changed (any thread)
        public static int Writes;

        public static string Dir { get { lock (gate) return dir ?? (dir = Path.Combine(BrowserSettings.HistoryDir, "favicons")); } }
        public static void UseDir(string d) { lock (gate) { dir = d; mem.Clear(); touched.Clear(); count = -1; } }   // tests
        public static void Forget() { lock (gate) { mem.Clear(); count = -1; } }

        // "https://www.Example.com/x" -> "example.com"; a bare host works too; null when it isn't a web address
        public static string Key(string urlOrHost)
        {
            if (string.IsNullOrWhiteSpace(urlOrHost)) return null;
            string host; int port = -1;
            if (urlOrHost.Contains("://"))
            {
                if (!Omni.IsWeb(urlOrHost)) return null; host = Omni.HostOf(urlOrHost);
                Uri u; if (Uri.TryCreate(urlOrHost, UriKind.Absolute, out u) && !u.IsDefaultPort) port = u.Port;
            }
            else host = urlOrHost;
            if (string.IsNullOrEmpty(host)) return null;
            host = host.Trim().TrimEnd('.').ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
            if (host.Length == 0 || host.Contains("/")) return null;
            // 1.14.1: several apps on one home server (192.168.1.5:8096, :9443...) each keep their own logo
            if (port > 0 && (IsIp(host) || !host.Contains("."))) host = host + ":" + port.ToString(CultureInfo.InvariantCulture);
            return host;
        }
        static bool IsIp(string key)
        {
            var h = key.Trim(); var c = h.LastIndexOf(':');
            if (c > 0 && h.IndexOf(':') == c) h = h.Substring(0, c);   // "192.168.1.5:8096" -> the address (an IPv6 address has several colons)
            var k = Uri.CheckHostName(h.Trim('[', ']')); return k == UriHostNameType.IPv4 || k == UriHostNameType.IPv6;
        }
        // the host, then its parent domains down to two labels ("mail.google.com" -> "google.com"); an IP address only itself
        public static IEnumerable<string> Chain(string key)
        {
            if (key == null) yield break;
            yield return key;
            if (IsIp(key)) yield break;
            var parts = key.Split('.');
            for (int i = 1; i <= parts.Length - 2; i++) yield return string.Join(".", parts, i, parts.Length - i);
        }
        public static string FileName(string key)
        {
            using (var h = SHA1.Create())
            {
                var b = h.ComputeHash(Encoding.UTF8.GetBytes(key)); var sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(b[i].ToString("x2"));
                return sb.ToString() + ".png";
            }
        }
        public static string PathFor(string key) { return Path.Combine(Dir, FileName(key)); }

        // any image the engine or a site gives (PNG, ICO, GIF, JPEG, BMP) -> a PNG of at most 32 px; null when it isn't a usable icon
        public static byte[] Normalize(byte[] data)
        {
            if (data == null || data.Length < 8 || data.Length > 4 * 1024 * 1024) return null;
            try
            {
                BitmapFrame best = null;
                using (var ms = new MemoryStream(data))
                {
                    var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    foreach (var f in dec.Frames) if (best == null || Score(f.PixelWidth) < Score(best.PixelWidth)) best = f;
                }
                if (best == null || best.PixelWidth < 8 || best.PixelHeight < 8 || best.PixelWidth > 4096 || best.PixelHeight > 4096) return null;
                BitmapSource src = best;
                if (src.PixelWidth > Px || src.PixelHeight > Px) { double s = (double)Px / Math.Max(src.PixelWidth, src.PixelHeight); src = new TransformedBitmap(src, new ScaleTransform(s, s)); }
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(src));
                using (var o = new MemoryStream()) { enc.Save(o); var r = o.ToArray(); return r.Length <= MaxBytes ? r : null; }
            }
            catch { return null; }
        }
        static int Score(int w) { return w == Px ? 0 : w > Px ? w - Px : (Px - w) * 3; }

        // ---- writing: a tab's icon (never from a private window), an imported or fetched one
        public static bool Save(string url, byte[] image, bool privateWindow)
        {
            if (privateWindow || ReadOnly) return false;
            var key = Key(url); if (key == null) return false;
            var png = Normalize(image); if (png == null) return false;
            return Put(key, png, true);
        }
        // icons from another browser or a fetch: only for sites without one (a live tab's icon is never replaced)
        public static int AddMissing(IDictionary<string, byte[]> byKey)
        {
            int n = 0; if (byKey == null || ReadOnly) return 0;
            foreach (var kv in byKey) { var key = Key(kv.Key); if (key == null || HasExact(key)) continue; var png = Normalize(kv.Value); if (png != null && Put(key, png, false)) n++; }
            return n;
        }
        static bool Put(string key, byte[] png, bool overwrite)
        {
            lock (gate)
            {
                try
                {
                    var d = Dir; Directory.CreateDirectory(d);
                    var f = PathFor(key); bool isNew = !File.Exists(f);
                    if (!isNew)
                    {
                        if (!overwrite) return false;
                        var old = File.ReadAllBytes(f);
                        if (old.Length == png.Length && old.SequenceEqual(png)) { Touch(f, key, true); return false; }
                    }
                    var tmp = f + ".tmp"; File.WriteAllBytes(tmp, png);
                    if (File.Exists(f)) File.Delete(f);
                    File.Move(tmp, f);
                    Writes++; mem.Remove(key);
                    foreach (var k in mem.Where(x => x.Value == null).Select(x => x.Key).ToList()) mem.Remove(k);   // "none" answers may be stale now (a parent domain)
                    if (count < 0) count = Files().Length; else if (isNew) count++;
                    if (count > MaxFiles) Evict();
                }
                catch { return false; }
            }
            var c = Changed; if (c != null) try { c(key); } catch { }
            return true;
        }
        static string[] Files() { try { return Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.png") : new string[0]; } catch { return new string[0]; } }
        public static int Count() { lock (gate) { if (Fake != null) return Fake.Count; if (count < 0) count = Files().Length; return count; } }
        // least recently used first, down to 90% of the limit (the file's write time is its last use: reading touches it, once a session)
        static void Evict()
        {
            var files = Files().Select(x => new FileInfo(x)).OrderBy(x => x.LastWriteTimeUtc).ToList();
            int n = files.Count, target = MaxFiles * 9 / 10;
            foreach (var fi in files) { if (n <= target) break; try { fi.Delete(); n--; } catch { } }
            count = n; mem.Clear();
        }
        static void Touch(string f, string key, bool force)
        {
            if (!force && !touched.Add(key)) return;
            try { var t = File.GetLastWriteTimeUtc(f); if (force || DateTime.UtcNow - t > TimeSpan.FromMinutes(10)) File.SetLastWriteTimeUtc(f, DateTime.UtcNow); } catch { }
        }

        // ---- reading
        public static bool HasExact(string urlOrKey)
        {
            var key = Key(urlOrKey); if (key == null) return false;
            lock (gate) { if (Fake != null) return Fake.ContainsKey(key); try { return File.Exists(PathFor(key)); } catch { return false; } }
        }
        static byte[] ReadKey(string key)
        {
            if (Fake != null) { byte[] b; return Fake.TryGetValue(key, out b) ? b : null; }
            try { var f = PathFor(key); if (!File.Exists(f)) return null; var b = File.ReadAllBytes(f); Touch(f, key, false); return b; } catch { return null; }
        }
        public static byte[] Png(string url) { var key = Key(url); if (key == null) return null; lock (gate) return ReadKey(key); }
        // the site's icon (or its parent domain's), frozen, for any thread; null when there is none
        public static ImageSource Image(string url)
        {
            var key = Key(url); if (key == null) return null;
            foreach (var k in Chain(key)) { var img = ImageFor(k); if (img != null) return img; }
            return null;
        }
        static ImageSource ImageFor(string key)
        {
            lock (gate)
            {
                ImageSource img; if (mem.TryGetValue(key, out img)) return img;
                var b = ReadKey(key); img = b == null ? null : Decode(b);
                mem[key] = img; return img;
            }
        }
        static ImageSource Decode(byte[] png)
        {
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; bi.StreamSource = new MemoryStream(png); bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // ---- the fallback order: cached icon -> brand icon -> letter tile
        public static string Brand(string url)
        {
            var b = SidebarRules.Brand(url); if (b != null) return Icons.Has(b) ? b : null;
            var h = Key(url) ?? "";
            if (h == "github.com" || h.EndsWith(".github.com", StringComparison.Ordinal)) return "github";
            return null;
        }
        public static string Letter(string url) { return NewTabData.Letter(url, null); }
        public static string Color(string url) { return NewTabData.Color(url); }
        // what a site shows: "favicon", "brand:<icon>" or "letter:<L>:<colour>"
        public static string Pick(string url)
        {
            if (Image(url) != null) return "favicon";
            var b = Brand(url); if (b != null) return "brand:" + b;
            return "letter:" + Letter(url) + ":" + Color(url);
        }
        static readonly Dictionary<string, Brush> brushes = new Dictionary<string, Brush>();
        static Brush BrushOf(string hex)
        {
            lock (brushes)
            {
                Brush b; if (brushes.TryGetValue(hex, out b)) return b;
                try { b = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex)); } catch { b = Brushes.SlateGray; }
                b.Freeze(); brushes[hex] = b; return b;
            }
        }
        // the site's picture, size x size (Tag = "favicon" | "brand" | "letter", for tests)
        public static FrameworkElement Element(string url, double size, string brandFg = "Lilac")
        {
            var img = Image(url);
            if (img != null)
            {
                var im = new Image { Source = img, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Tag = "favicon", SnapsToDevicePixels = true };
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                return im;
            }
            var b = Brand(url);
            if (b != null) { var p = Ui.Icon(b, size, brandFg); p.Tag = "brand"; return p; }
            return LetterTile(url, size);
        }
        public static FrameworkElement LetterTile(string url, double size)
        {
            var t = new TextBlock { Text = Letter(url), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = Math.Max(7, size * 0.62), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Math.Max(2, size * 0.26)), Background = BrushOf(Color(url)), Child = t, VerticalAlignment = VerticalAlignment.Center, Tag = "letter", SnapsToDevicePixels = true };
        }
        // a menu row with the site's picture instead of an icon
        public static Button MenuRow(string url, string text, string keys, Action click)
        {
            var b = Ui.MenuRow(null, text, keys, click);
            var g = b.Content as Grid; if (g != null) g.Children.Insert(0, Element(url, 14));
            return b;
        }
        // the new tab page: the icon copied beside the page (it reads nothing outside its own folder); "ico/<file>?v=<time>" or null
        public static string ForNewTab(string url)
        {
            var key = Key(url); if (key == null) return null;
            lock (gate)
            {
                foreach (var k in Chain(key))
                {
                    try
                    {
                        byte[] fake = null; if (Fake != null && !Fake.TryGetValue(k, out fake)) continue;
                        var f = PathFor(k); if (fake == null && !File.Exists(f)) continue;
                        var to = Path.Combine(Engine.NewTabDir, "ico", FileName(k)); Directory.CreateDirectory(Path.GetDirectoryName(to));
                        if (fake != null) { if (!File.Exists(to)) File.WriteAllBytes(to, fake); }
                        else
                        {
                            var fi = new FileInfo(f); var ti = new FileInfo(to);
                            if (!ti.Exists || ti.Length != fi.Length || ti.LastWriteTimeUtc < fi.LastWriteTimeUtc) File.Copy(f, to, true);
                        }
                        return "ico/" + FileName(k) + "?v=" + new FileInfo(to).LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                    }
                    catch { }
                }
            }
            return null;
        }

        // --snap: made-up icons (a coloured rounded square with a white symbol) for made-up sites - nothing read from or written to disk
        public static Dictionary<string, byte[]> SnapIcons()
        {
            var res = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            Action<string, string, string> add = (host, color, glyph) =>
            {
                try
                {
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRoundedRectangle(BrushOf(color), null, new Rect(0, 0, Px, Px), 7, 7);
                        var g = Icons.Get(glyph);
                        if (g != null)
                        {
                            var bnd = g.Bounds; double s = 18.0 / Math.Max(bnd.Width, bnd.Height);
                            var tg = new TransformGroup(); tg.Children.Add(new TranslateTransform(-bnd.X - bnd.Width / 2, -bnd.Y - bnd.Height / 2)); tg.Children.Add(new ScaleTransform(s, s)); tg.Children.Add(new TranslateTransform(Px / 2.0, Px / 2.0));
                            dc.PushTransform(tg); dc.DrawGeometry(Brushes.White, null, g); dc.Pop();
                        }
                    }
                    var rtb = new RenderTargetBitmap(Px, Px, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var ms = new MemoryStream()) { enc.Save(ms); res[host] = ms.ToArray(); }
                }
                catch { }
            };
            add("news.example.com", "#2563EB", "newspaper"); add("guides.example.com", "#059669", "book-open"); add("patch.example.com", "#D97706", "file-import");
            add("season.example.com", "#DB2777", "star"); add("status.example.com", "#0891B2", "server"); add("loadouts.example", "#7C3AED", "gamepad");
            add("drivers.example", "#DC2626", "microchip"); add("speedrun.example", "#65A30D", "bolt"); add("hallow.example", "#7C3AED", "shield-halved");
            return res;
        }
    }
}
