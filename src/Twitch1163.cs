// Hallow Browser 1.16.3 - the white Twitch player, real cause (a user's "Report a Twitch problem" file: the video played fine under a
// white player; what covered it was a channel's video-overlay extension, an iframe from supervisor.ext-twitch.tv). Reproduced on the
// real twitch.tv: with "Make pages dark" on, Chromium's automatic dark mode paints that transparent extension frame opaque white over
// the stream; a frame that fails to load (blocked, or an HTTP error) shows a light error page over it the same way.
//  - "Show channel extensions over the video" (Settings > Gaming > Twitch ads, the Twitch button's panel): OFF by default. Off = the
//    Twitch script (v6) hides video-overlay extensions inside the player (never panel extensions below the stream, chat or controls).
//    The script is added to Twitch pages for this even when Twitch ads aren't blocked.
//  - On: a broken one is hidden - the browser watches every frame of a Twitch tab and tells the page when an ext-twitch.tv frame failed
//    to load; and the page's cover check asks the browser for a picture of the player area (CapturePreview, only of the tab you are
//    looking at, at most every few seconds while something lies over a healthy video) to see what is really on screen.
//  - Twitch draws its own light / dark theme, so "Make pages dark" leaves twitch.tv alone (like Google, 1.16.2).
//  - Report a Twitch problem: the switch, the extension frames hidden, frame load errors, the cover checks and "Make pages dark".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        [DataMember] public bool TwitchShowExt;   // 1.16.3: channel extensions over the video - OFF by default (hidden)
    }

    public partial class Tab
    {
        public int TwExtHidden, TwExtBroken, TwChecks, TwBlankFound;
        public readonly List<string> TwFrameErrors = new List<string>();
        public Dictionary<string, object> TwLastCheck;
        public DateTime TwCheckAt;
    }

    public static class TwitchCover
    {
        // what is on screen in the player area of a picture of the page: the share of white pixels, the share within +-12 of the most
        // common brightness (one flat colour), the mean. rect is in CSS pixels; viewW the page's innerWidth (the picture can be scaled).
        public static Dictionary<string, object> Measure(Bitmap bmp, double[] rect, double viewW)
        {
            var k = viewW > 0 ? bmp.Width / viewW : 1.0;
            // a 5 % margin inside the player: its edges and Twitch's controls bar at the bottom count for little
            int x0 = (int)((rect[0] + rect[2] * 0.05) * k), y0 = (int)((rect[1] + rect[3] * 0.05) * k), x1 = (int)((rect[0] + rect[2] * 0.95) * k), y1 = (int)((rect[1] + rect[3] * 0.85) * k);
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(bmp.Width, x1); y1 = Math.Min(bmp.Height, y1);
            if (x1 - x0 < 8 || y1 - y0 < 8) return null;
            int step = Math.Max(2, Math.Min(x1 - x0, y1 - y0) / 40), n = 0, white = 0; long sum = 0; var hist = new int[256];
            for (int y = y0; y < y1; y += step)
                for (int x = x0; x < x1; x += step)
                {
                    var p = bmp.GetPixel(x, y); int l = (p.R + p.G + p.B) / 3;
                    n++; sum += l; hist[l]++; if (p.R >= 235 && p.G >= 235 && p.B >= 235) white++;
                }
            int mode = 0; for (int i = 1; i < 256; i++) if (hist[i] > hist[mode]) mode = i;
            int near = 0; for (int i = Math.Max(0, mode - 12); i <= Math.Min(255, mode + 12); i++) near += hist[i];
            return new Dictionary<string, object> { { "white", Math.Round(white / (double)n, 2) }, { "flat", Math.Round(near / (double)n, 2) }, { "mean", (int)(sum / n) }, { "n", n } };
        }
        // the verdict: the screen shows white (or one flat colour) where the video itself has a picture
        public static Dictionary<string, object> Verdict(Dictionary<string, object> m, double videoWhite, double videoSpread)
        {
            var r = new Dictionary<string, object>(m ?? new Dictionary<string, object>()); r["covered"] = false;
            if (m == null) { r["why"] = "no picture"; return r; }
            double w = Convert.ToDouble(m["white"]), f = Convert.ToDouble(m["flat"]);
            if (w >= 0.85 && videoWhite < 0.5) { r["covered"] = true; r["why"] = "white"; }
            else if (f >= 0.93 && videoSpread >= 60) { r["covered"] = true; r["why"] = "blank"; }
            return r;
        }
    }

    public partial class BrowserWindow
    {
        public static int TwCoverChecks;   // tests
        public static List<string> TwFrameErrorsOf(Tab t) { lock (t.TwFrameErrors) return t.TwFrameErrors.ToList(); }
        // every frame of a tab (nested ones too): a Twitch page's frame that failed to load is written down; an extension's tells the page
        void TwHookFrames(CoreWebView2 c)
        {
            try { c.FrameCreated += (a, e) => TwHookFrame(c, e.Frame, 0); } catch { }
        }
        void TwHookFrame(CoreWebView2 c, CoreWebView2Frame f, int depth)
        {
            string uri = null;
            try
            {
                f.NavigationStarting += (a, e) => { try { uri = e.Uri; } catch { } };
                f.NavigationCompleted += (a, e) =>
                {
                    try
                    {
                        if (e.IsSuccess) return;
                        string status = e.WebErrorStatus.ToString(); try { if (e.HttpStatusCode > 0) status += " (HTTP " + e.HttpStatusCode + ")"; } catch { }
                        TwFrameFailed(c, uri, status);
                    }
                    catch { }
                };
                if (depth < 3) f.FrameCreated += (a, e) => TwHookFrame(c, e.Frame, depth + 1);
            }
            catch { }
        }
        void TwFrameFailed(CoreWebView2 c, string uri, string status)
        {
            Tab t; if (!owner.TryGetValue(c, out t)) return;
            string page = null; try { page = c.Source; } catch { }
            if (!TwitchAdRules.IsTwitch(page)) return;
            Uri u; var host = Uri.TryCreate(TwitchAdRules.Normalize(uri), UriKind.Absolute, out u) ? u.Host.ToLowerInvariant() : "(unknown)";
            lock (t.TwFrameErrors) { t.TwFrameErrors.Add(DateTime.Now.ToString("HH:mm:ss") + " " + host + " " + status); if (t.TwFrameErrors.Count > 20) t.TwFrameErrors.RemoveAt(0); }
            if (host == "ext-twitch.tv" || host.EndsWith(".ext-twitch.tv"))
            {
                var js = new JavaScriptSerializer();
                try { var _ = c.ExecuteScriptAsync("window.__hallowTwitch&&window.__hallowTwitch.extFailed&&window.__hallowTwitch.extFailed(" + js.Serialize(host) + "," + js.Serialize(status) + ")"); } catch { }
            }
        }

        // the page's messages new in 1.16.3; true = handled
        bool OnTwitchMessage1163(Tab t, CoreWebView2 c, string ev, Dictionary<string, object> m)
        {
            object o;
            if (ev == "ext")
            {
                try { if (m.TryGetValue("hidden", out o)) t.TwExtHidden = Math.Max(t.TwExtHidden, Convert.ToInt32(o)); if (m.TryGetValue("broken", out o)) t.TwExtBroken = Convert.ToInt32(o); } catch { }
                return true;
            }
            if (ev != "covercheck") return false;
            var now = DateTime.Now;
            if ((now - t.TwCheckAt).TotalSeconds < 2.5 || t != Active || !Showing(t) || WindowState == System.Windows.WindowState.Minimized) return true;   // only what you are looking at
            t.TwCheckAt = now; t.TwChecks++; TwCoverChecks++;
            double[] rect = null; double vw = 0, vWhite = 1, vSpread = 0;
            try
            {
                rect = ((System.Collections.ArrayList)m["rect"]).Cast<object>().Select(Convert.ToDouble).ToArray();
                vw = Convert.ToDouble(m["vw"]);
                var vid = (Dictionary<string, object>)m["video"]; vWhite = Convert.ToDouble(vid["white"]); vSpread = Convert.ToDouble(vid["spread"]);
            }
            catch { return true; }
            if (rect == null || rect.Length != 4) return true;
            var _ = TwCoverCheck(t, c, rect, vw, vWhite, vSpread);
            return true;
        }
        async Task TwCoverCheck(Tab t, CoreWebView2 c, double[] rect, double vw, double vWhite, double vSpread)
        {
            Dictionary<string, object> verdict;
            try
            {
                using (var ms = new MemoryStream())
                {
                    await c.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                    ms.Position = 0;
                    var measured = await Task.Run(() => { using (var bmp = new Bitmap(ms)) return TwitchCover.Measure(bmp, rect, vw); });
                    verdict = TwitchCover.Verdict(measured, vWhite, vSpread);
                }
            }
            catch (Exception e) { verdict = new Dictionary<string, object> { { "covered", false }, { "why", "capture failed: " + e.GetType().Name } }; }
            t.TwLastCheck = verdict; if ((bool)verdict["covered"]) t.TwBlankFound++;
            if (t.Core != c) return;
            try { await c.ExecuteScriptAsync("window.__hallowTwitch&&window.__hallowTwitch.checked&&window.__hallowTwitch.checked(" + new JavaScriptSerializer().Serialize(verdict) + ")"); } catch { }
        }

        // the switch (Settings > Gaming > Twitch ads, and the Twitch button's panel)
        public System.Windows.UIElement TwitchShowExtSwitch(bool closeFlyouts)
        {
            return Ui.Switch("Show channel extensions over the video", "Some channels add extensions that draw over the stream (polls, overlays, offers). Off: they're hidden, so a broken one can't cover the stream with a white or blank sheet. On: they show, and one that fails to load or covers a playing stream with a blank sheet is still hidden. Extensions in the panels below the stream always show. Twitch pages load again.",
                S.TwitchShowExt, on => { if (closeFlyouts) Panels.CloseFlyouts(); S.TwitchShowExt = on; S.Save(); TwitchSettingsChanged(); });
        }
    }

    public static partial class TestHooks
    {
        static string V1163(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                case "showext": return Program.OnUi(w, () => { w.S.TwitchShowExt = arg.Trim() == "1"; w.S.Save(); w.TwitchSettingsChanged(); return "ok " + w.S.TwitchShowExt; });
                case "state":   // <tab>
                    return Program.OnUi(w, () =>
                    {
                        var t = TabAt(w, p.Length > 0 ? p[0] : ""); if (t == null) return "error no tab";
                        List<string> fe; lock (t.TwFrameErrors) fe = t.TwFrameErrors.ToList();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "showExt", w.S.TwitchShowExt }, { "extHidden", t.TwExtHidden }, { "extBroken", t.TwExtBroken }, { "checks", t.TwChecks }, { "covered", t.TwBlankFound }, { "last", t.TwLastCheck }, { "frameErrors", fe }, { "allChecks", BrowserWindow.TwCoverChecks }, { "dark", t.DarkApplied == true } });
                    });
            }
            return "error unknown 1.16.3 test command";
        }
    }
}
