// Hallow Browser 1.16.3 - unit tests: the white Twitch player (a channel's overlay extension over a healthy video). The setting, the
// script's version and its extension rules, the cover check's measure / verdict on made-up pictures, Twitch left out of "Make pages
// dark". No network.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace HallowBrowser.Tests
{
    public static class V1163
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }

        static Bitmap Pic(int w, int h, Func<int, int, Color> f) { var b = new Bitmap(w, h); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) b.SetPixel(x, y, f(x, y)); return b; }

        public static void All()
        {
            var s = new BrowserSettings();
            Ok(!s.TwitchShowExt, "twitch 1.16.3: \"Show channel extensions over the video\" is off by default");
            Ok(TwitchAdRules.Config(s).Contains("\"showExt\":false"), "twitch 1.16.3: the page script is told the switch (off)");
            s.TwitchShowExt = true; Ok(TwitchAdRules.Config(s).Contains("\"showExt\":true"), "twitch 1.16.3: ... and on");
            var js = TwitchAdRules.BuiltIn;
            Ok(TwitchAdRules.VersionOf(js) == 6 && TwitchAdRules.LooksRight(js), "twitch 1.16.3: the built-in script is version 6 (older browsers pick it up from the release)");
            Ok(js.Contains("var SHOW_EXT = CFG.showExt === true;"), "twitch 1.16.3: a browser that passes no switch (older ones) gets overlays hidden");
            var sel = System.Text.RegularExpressions.Regex.Match(js, "var EXT_SEL = '([^']*)'").Groups[1].Value;
            Ok(sel.Length > 0 && sel.Split(',').All(x => x.Trim().EndsWith(".extension-view__iframe")) && !sel.Contains("panel") && !sel.Contains("chat"), "twitch 1.16.3: only overlay frames inside the player are hidden - never panel extensions or chat (" + sel + ")");
            Ok(js.Contains("hideBox(el, v);") && js.Contains("function hideBox(el, v)"), "twitch 1.16.3: the watchdog and the cover check share the unblock path");

            // the cover check's measure and verdict
            var white = Pic(200, 120, (x, y) => (x > 150 && y > 90) ? Color.FromArgb(40, 120, 200) : Color.White);   // white with a small button
            var m = TwitchCover.Measure(white, new double[] { 0, 0, 200, 120 }, 200);
            Ok(Convert.ToDouble(m["white"]) >= 0.85 && (bool)TwitchCover.Verdict(m, 0.0, 200)["covered"] && (string)TwitchCover.Verdict(m, 0.0, 200)["why"] == "white", "cover check: a white sheet with a small button over a playing video = covered " + string.Join(",", m.Select(kv => kv.Key + "=" + kv.Value)));
            Ok(!(bool)TwitchCover.Verdict(m, 0.97, 10)["covered"], "cover check: not when the video's own picture is white too (a white scene)");
            var grey = Pic(200, 120, (x, y) => Color.FromArgb(32, 33, 36));
            var mg = TwitchCover.Measure(grey, new double[] { 0, 0, 200, 120 }, 200);
            Ok((bool)TwitchCover.Verdict(mg, 0.0, 150)["covered"] && (string)TwitchCover.Verdict(mg, 0.0, 150)["why"] == "blank" && !(bool)TwitchCover.Verdict(mg, 0.0, 20)["covered"], "cover check: one flat colour over a lively video = blank; over a flat video = fine");
            var video = Pic(200, 120, (x, y) => Color.FromArgb((x * 7) % 256, (y * 5) % 256, ((x + y) * 3) % 256));
            var mv = TwitchCover.Measure(video, new double[] { 0, 0, 200, 120 }, 200);
            Ok(!(bool)TwitchCover.Verdict(mv, 0.0, 200)["covered"], "cover check: the video showing = not covered " + string.Join(",", mv.Select(kv => kv.Key + "=" + kv.Value)));
            var page = Pic(400, 240, (x, y) => x >= 100 && x < 300 && y >= 60 && y < 180 ? Color.White : Color.FromArgb((x * 7) % 256, (y * 5) % 256, 90));
            var mp = TwitchCover.Measure(page, new double[] { 50, 30, 100, 60 }, 200);   // the picture is twice the page's CSS size
            Ok(Convert.ToDouble(mp["white"]) >= 0.95, "cover check: the player's CSS rect is scaled to the picture (device pixels)");
            Ok(TwitchCover.Measure(white, new double[] { 500, 500, 100, 100 }, 200) == null && !(bool)TwitchCover.Verdict(null, 0, 200)["covered"], "cover check: a player off the picture measures nothing and hides nothing");

            // "Make pages dark" leaves Twitch alone (it has its own theme; dark mode painted overlay extensions white)
            Ok(SiteThemes.OwnTheme("www.twitch.tv") && SiteThemes.OwnTheme("twitch.tv") && SiteThemes.OwnTheme("m.twitch.tv") && !SiteThemes.OwnTheme("twitch.tv.evil.example") && !SiteThemes.OwnTheme("nottwitch.tv"),
                "theme 1.16.3: twitch.tv draws its own light / dark theme (look-alikes don't)");
        }
    }
}
