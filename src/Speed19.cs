// Hallow Browser 1.9 - the speed pass: what was measured to help is kept (tests\Bench19.cs, see the README's speed table): the
// engine's preference files are written on another core at start (App.cs / Engine.cs), the theme reuses the settings already read,
// your own extensions load after the first page, and pictures don't keep their source data. Tried and dropped because they measured
// no better: a low memory target for pages off screen a minute (suspended tabs kept the same memory), and putting the pre-warmed tab
// to sleep / slowing background players in game mode (the browser already sat at ~0.2% of one core there).
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class Speed19
    {
        // tests: HALLOW_BROWSER_SPEEDOFF=prefs,theme turns a 1.9 start-up change off, so the benchmark can measure each one
        static readonly string off = BrowserSettings.TestDir != null ? (Environment.GetEnvironmentVariable("HALLOW_BROWSER_SPEEDOFF") ?? "") : "";
        public static bool Off(string what) { return off.Split(',').Contains(what); }
        // a decoded picture that no longer holds on to the stream it came from (a BitmapImage keeps its StreamSource - the whole JPEG/PNG)
        public static BitmapSource Detach(BitmapSource img)
        {
            if (img == null) return null;
            try { var wb = new WriteableBitmap(img); wb.Freeze(); return wb; } catch { return img; }
        }
    }

    public partial class BrowserWindow
    {
        // ---- your own extensions: after the first page (Hallow Shield itself is never deferred)
        async Task LoadUserExtensionsLater(CoreWebView2Profile p)
        {
            if (S.Extensions.Count == 0) return;
            for (int i = 0; i < 40 && !Engine.Perf.ContainsKey("first-navigation-completed"); i++) await Task.Delay(100);
            await Task.Delay(300);
            await LoadUserExtensions(p);
        }
    }
}
