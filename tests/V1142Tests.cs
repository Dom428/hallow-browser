// Hallow Browser 1.14.2 - unit tests: the Twitch page script version 4 (an ad that gets through is covered, a white player is
// recovered, every decision is kept) and "Report a Twitch problem" (the file name, the picture of the player, the zip, the
// anonymising). No network.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HallowBrowser.Tests
{
    public static class V1142
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }

        public static void All() { ScriptTests(); ReportTests(); }

        static void ScriptTests()
        {
            var js = TwitchAdRules.BuiltIn;
            Ok(TwitchAdRules.VersionOf(js) == 4 && TwitchAdRules.LooksRight(js), "twitch 1.14.2: the built-in script is version 4 (newer than 1.12.1 - 1.14.1's 3, so 1.10+ installs take it from the release)");
            Ok(js.Contains("function adWhy(") && js.Contains("'amazon'") && js.Contains("'cue'") && js.Contains("'title'") && js.Contains("sawLive"), "twitch 1.14.2: more kinds of ad marker (class, X-TV-TWITCH-AD, Amazon titles, cue tags, a non-live title where live ones were)");
            Ok(js.Contains("XO.prototype.send") && js.Contains("handle(u, channelOf(u), x.responseText, null)"), "twitch 1.14.2: playlists fetched with XMLHttpRequest are looked at (covered, not swapped)");
            Ok(js.Contains("import(' + JSON.stringify(abs)") && !js.Contains("if (!(o && o.type === 'module'))"), "twitch 1.14.2: module workers get the prelude too");
            Ok(js.Contains("function gqlAdEvents(") && js.Contains("gqlAdEvents(list)") && Regex.IsMatch(js, @"impression\|quartile"), "twitch 1.14.2: Twitch's own ad events (impression / quartile) cover an ad");
            Ok(js.Contains("function adVideo(") && js.Contains("pbyp-player-instance"), "twitch 1.14.2: a second video playing with sound in an ad box is muted and covered");
            Ok(js.Contains("function labelAd(") && js.Contains("SIG.domMuteUntil = now + 600000"), "twitch 1.14.2: a short ad label found by its text; a label that stays 3 minutes can't keep a cover up for good");
            Ok(js.Contains("function blankCheck(") && js.Contains("s.white >= 0.95 && s.spread <= 24") && js.Contains("getImageData(0, 0, 32, 18)"), "twitch 1.14.2: a white player is found by sampling the video's picture (32 x 18)");
            Ok(js.Contains("var maxTries = why === 'frozen' ? 2") && js.Contains("now - SIG.lastAd < 5 * 60000 ? 4 : 3"), "twitch 1.14.2: a frozen picture gets only the gentle steps; the page is reloaded only within 5 minutes after an ad");
            Ok(js.Contains("now - last < 10 * 60000") && js.Contains("sessionStorage"), "twitch 1.14.2: at most one page reload in 10 minutes");
            Ok(js.Contains("cmd: 'backup'") && js.Contains("preferAlt[m.ch]"), "twitch 1.14.2: the backup stream (Twitch's own stream as another player type) on request");
            Ok(js.Contains("function whiteSheet(") && js.Contains("<= 40"), "twitch 1.14.2: an opaque white sheet with at most a button / a short line over the video is hidden");
            Ok(js.Contains("window.__hallowTwitch.report = function") && js.Contains("'[channel]'"), "twitch 1.14.2: the script's report replaces channel names");
            // earlier behaviour kept
            Ok(js.Contains(":not(:has(video))") && js.Contains("function guardPlayer(") && js.Contains("function unblock(") && js.Contains("WD.tries < 3"), "twitch 1.14.2: the 1.12.1 guard, unblock and stall watchdog are still there");
            Ok(js.Contains("credentials: 'omit'") && !Regex.IsMatch(js, @"https?://(?!gql\.twitch|usher\.ttvnw)[a-z0-9.-]+\.(com|net|org|io)/", RegexOptions.IgnoreCase), "twitch 1.14.2: still no cookies on the alternative requests, no outside servers");
        }

        static void ReportTests()
        {
            Ok(TwitchReport.FileName(new DateTime(2026, 11, 22, 19, 5, 3)) == "Hallow Twitch report 2026-11-22 19-05-03.zip", "report: the file name");
            var s = TwitchReport.Scrub("{\"u\":\"/CaseOh_\",\"d\":\"swap caseoh_ -> embed\"}", "caseoh_");
            Ok(!s.ToLowerInvariant().Contains("caseoh_") && s.Contains("[channel]"), "report: the channel's name is replaced everywhere, any case (" + s + ")");
            var p = TwitchReport.Probe;
            Ok(!p.Contains("cookie") && !p.Contains("localStorage") && !p.Contains("getAttribute('aria-label')") && !p.Contains(".innerText||'').slice") && p.Contains("textLen"), "report: the page probe reads no cookies, storage, labels or text (only text lengths)");
            Ok(p.Contains("data-a-target") && p.Contains("elementsFromPoint") && p.Contains("getImageData") && p.Contains("__hallowTwitch"), "report: the probe has the elements over the player, a picture sample and the script's state");
            // the picture: the player area, at most 480 px wide
            byte[] png;
            using (var b = new Bitmap(1600, 900)) { using (var g = Graphics.FromImage(b)) { g.Clear(Color.Black); g.FillRectangle(Brushes.White, 200, 100, 960, 540); } using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); png = ms.ToArray(); } }
            var crop = TwitchReport.Crop(png, new[] { 100, 50, 480, 270 }, 800);   // the page is 800 CSS px wide, the picture 1600
            using (var ms = new MemoryStream(crop)) using (var c = new Bitmap(ms))
                Ok(c.Width == 480 && c.Height == 270 && c.GetPixel(240, 135).R > 240 && c.GetPixel(240, 135).G > 240, "report: the picture is the player's area only, 480 px wide (" + c.Width + "x" + c.Height + ")");
            Ok(TwitchReport.Crop(null, null, 0) == null, "report: no picture, no crash");
            var dir = Path.Combine(Path.GetTempPath(), "hb-twr-" + Guid.NewGuid().ToString("n").Substring(0, 8));
            try
            {
                var when = new DateTime(2026, 1, 2, 3, 4, 5);
                var a = TwitchReport.WriteZip(dir, when, "{\"x\":1}", crop); var b2 = TwitchReport.WriteZip(dir, when, "{\"x\":2}", null);
                using (var z = ZipFile.OpenRead(a))
                    Ok(z.Entries.Select(e => e.Name).OrderBy(x => x).SequenceEqual(new[] { "player.png", "report.json" }) && new StreamReader(z.GetEntry("report.json").Open()).ReadToEnd() == "{\"x\":1}", "report: one zip with report.json and player.png");
                Ok(a != b2 && b2.EndsWith("(2).zip") && File.Exists(b2), "report: a second one at the same second gets its own name");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
