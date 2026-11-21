// Hallow Browser 1.8 - unit tests: SponsorBlock (which addresses are YouTube videos, the 4-character SHA-256 prefix, the exact request
// - nothing else about the video in it - and picking our video's segments out of the answer; no network here), the settings' defaults
// (everything off), Twitch channel addresses and the chat address, the game launch handoff's rules, and the contrast of the Skip button.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowBrowser.Tests
{
    public static class V18
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All() { SponsorTests(); SettingsTests(); TwitchTests(); HandoffTests(); ContrastTests(); }

        static void SponsorTests()
        {
            Eq(SponsorBlock.VideoId("https://www.youtube.com/watch?v=dQw4w9WgXcQ"), "dQw4w9WgXcQ", "sponsorblock: www.youtube.com/watch?v=");
            Eq(SponsorBlock.VideoId("https://m.youtube.com/watch?list=PL1&v=dQw4w9WgXcQ&t=30s"), "dQw4w9WgXcQ", "sponsorblock: m.youtube.com, v= not first");
            Eq(SponsorBlock.VideoId("https://youtu.be/dQw4w9WgXcQ"), "dQw4w9WgXcQ", "sponsorblock: youtu.be");
            Eq(SponsorBlock.VideoId("https://www.youtube.com/shorts/dQw4w9WgXcQ"), "dQw4w9WgXcQ", "sponsorblock: shorts");
            Eq(SponsorBlock.VideoId("https://music.youtube.com/watch?v=dQw4w9WgXcQ"), "dQw4w9WgXcQ", "sponsorblock: YouTube Music");
            Ok(SponsorBlock.VideoId("https://www.youtube.com/") == null && SponsorBlock.VideoId("https://www.youtube.com/watch?v=short") == null && SponsorBlock.VideoId("https://www.youtube.com/@channel") == null,
                "sponsorblock: the home page, a bad id and a channel page aren't videos");
            Ok(SponsorBlock.VideoId("https://evil-youtube.com/watch?v=dQw4w9WgXcQ") == null && SponsorBlock.VideoId("https://youtube.com.evil.net/watch?v=dQw4w9WgXcQ") == null && SponsorBlock.VideoId("file:///c:/watch?v=dQw4w9WgXcQ") == null,
                "sponsorblock: look-alike hosts and files aren't YouTube");
            Eq(SponsorBlock.Prefix("dQw4w9WgXcQ"), "5f6b", "sponsorblock: the prefix is the first 4 hex characters of SHA-256(video id)");
            Eq(SponsorBlock.Prefix("aaaaaaaaaaa"), "28cb", "sponsorblock: ... another video, another prefix");
            var url = SponsorBlock.Url(SponsorBlock.Prefix("dQw4w9WgXcQ"), new[] { "sponsor", "intro" });
            Eq(url, "https://sponsor.ajay.app/api/skipSegments/5f6b?categories=%5B%22intro%22%2C%22sponsor%22%5D&actionType=skip", "sponsorblock: the request: prefix, categories, skip - the public API");
            Ok(!url.Contains("dQw4w9WgXcQ") && !url.Contains("youtube"), "sponsorblock: the video's id and address are never in the request");
            Ok(!SponsorBlock.Url("5f6b", new[] { "sponsor", "bogus" }).Contains("bogus"), "sponsorblock: unknown categories aren't sent");
            var answer = "[{\"videoID\":\"zzzzzzzzzzz\",\"hash\":\"5f6b00\",\"segments\":[{\"category\":\"sponsor\",\"actionType\":\"skip\",\"segment\":[1,9]}]}," +
                "{\"videoID\":\"dQw4w9WgXcQ\",\"hash\":\"5f6bd3\",\"segments\":[{\"category\":\"intro\",\"actionType\":\"skip\",\"segment\":[0.5,7.25],\"UUID\":\"a\"},{\"category\":\"sponsor\",\"actionType\":\"skip\",\"segment\":[60.1,90]}," +
                "{\"category\":\"sponsor\",\"actionType\":\"mute\",\"segment\":[100,110]},{\"category\":\"outro\",\"actionType\":\"skip\",\"segment\":[200,210]},{\"category\":\"sponsor\",\"actionType\":\"skip\",\"segment\":[30,30.2]}]}]";
            var segs = SponsorBlock.Parse(answer, "dQw4w9WgXcQ", new[] { "sponsor", "intro" });
            Eq(string.Join(" ", segs.Select(s => s.Cat + ":" + s.Start + "-" + s.End)), "intro:0.5-7.25 sponsor:60.1-90", "sponsorblock: only our video's skip segments of the kinds asked for, in order (not mute, not a 0.2 s blip, not other videos)");
            Eq(SponsorBlock.Parse("", "dQw4w9WgXcQ", new[] { "sponsor" }).Count, 0, "sponsorblock: no answer (404) = no segments");
            Eq(SponsorBlock.Parse("not json", "dQw4w9WgXcQ", new[] { "sponsor" }).Count, 0, "sponsorblock: nonsense = no segments");
            var sc = SponsorBlock.Script("dQw4w9WgXcQ", segs, "button", "#A060FF");
            Ok(sc.Contains("\"vid\":\"dQw4w9WgXcQ\"") && sc.Contains("\"mode\":\"button\"") && sc.Contains("\"s\":60.1") && sc.Contains("attachShadow({mode:'closed'})") && sc.Contains("Skipped "), "sponsorblock: the page script gets the segments and draws in its own closed shadow DOM");
            Ok(SponsorBlock.Credit.Contains("SponsorBlock") && SponsorBlock.Credit.Contains("CC BY-NC-SA 4.0"), "sponsorblock: the credit names SponsorBlock and its licence");
            Eq(SponsorBlock.Cats.Length, 5, "sponsorblock: five kinds to choose from (sponsor, intro, outro, self-promo, interaction reminder)");
        }
        static void SettingsTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(!s.SponsorSkip && !s.TwitchChat && s.SponsorMode == "auto" && string.Join(",", s.SponsorCats) == "sponsor,selfpromo", "settings: SponsorBlock and the Twitch chat are off by default");
            s.SponsorCats = new List<string> { "sponsor", "sponsor", "x", "outro" }; s.SponsorMode = "weird"; s.Fix();
            Ok(string.Join(",", s.SponsorCats) == "sponsor,outro" && s.SponsorMode == "auto", "settings: junk categories and modes aren't kept");
            s.SponsorCats = null; s.Fix(); Ok(s.SponsorCats != null && s.SponsorCats.Count == 2, "settings: missing categories -> the defaults");
            Eq(SettingsLayout.SectionOf("YouTube and Twitch"), "Gaming", "settings: the card is in Gaming");
        }
        static void TwitchTests()
        {
            Eq(Twitch.Channel("https://www.twitch.tv/SomeStreamer"), "somestreamer", "twitch: a channel page");
            Eq(Twitch.Channel("https://m.twitch.tv/some_streamer?referrer=raid"), "some_streamer", "twitch: m.twitch.tv with a query");
            Ok(Twitch.Channel("https://www.twitch.tv/directory") == null && Twitch.Channel("https://www.twitch.tv/") == null && Twitch.Channel("https://www.twitch.tv/somestreamer/videos") == null && Twitch.Channel("https://www.twitch.tv/popout/somestreamer/chat") == null && Twitch.Channel("https://www.twitch.tv/settings") == null,
                "twitch: the directory, home, a channel's videos, the chat pop-out and settings aren't streams");
            Ok(Twitch.Channel("https://twitch.tv.evil.com/somestreamer") == null && Twitch.Channel("http://www.twitch.tv/somestreamer") == null && Twitch.Channel("https://www.twitch.tv/a-b") == null, "twitch: look-alike hosts, plain http and bad names aren't");
            Eq(Twitch.ChatUrl("somestreamer"), "https://www.twitch.tv/popout/somestreamer/chat", "twitch: the chat pop-out address");
            Ok(Twitch.TheatreScript(true).Contains("player-theatre-mode-button") && Twitch.TheatreScript(true).Contains("!==true") && Twitch.TheatreScript(false).Contains("!==false"), "twitch: theatre mode uses Twitch's own player button, only to change it");
            Ok(BrowserWindow.IsStream("https://www.twitch.tv/somestreamer") && BrowserWindow.IsStream("https://www.youtube.com/watch?v=dQw4w9WgXcQ") && !BrowserWindow.IsStream("https://example.com/"), "stream pages: a Twitch channel or a YouTube video");
        }
        static void HandoffTests()
        {
            var t0 = new DateTime(2026, 11, 21, 20, 0, 0); var h = new Handoff();
            Eq(h.Step(null, null, t0), null, "handoff: nothing started = the usual detection (none)");
            Eq(h.Step("Delta Force", null, t0), "Delta Force", "handoff: nothing started = the usual detection (a game)");
            h.Start("1422450", "Deadlock", t0);
            Eq(h.Step(null, false, t0.AddSeconds(1)), "Deadlock", "handoff: game mode at once, before anything sees the game");
            Eq(h.Step(null, null, t0.AddSeconds(60)), "Deadlock", "handoff: ... still waiting for it within 2 minutes");
            Eq(h.Step(null, true, t0.AddSeconds(70)), "Deadlock", "handoff: Steam says it runs");
            Eq(h.Step(null, true, t0.AddMinutes(90)), "Deadlock", "handoff: ... game mode stays while it runs (past the 2 minutes)");
            Ok(h.Step(null, false, t0.AddMinutes(91)) == null && !h.Active, "handoff: the game closed -> game mode off, handoff over");
            Eq(h.Step(null, true, t0.AddMinutes(92)), null, "handoff: over means over (back to the usual detection)");
            h.Start("1422450", "Deadlock", t0);
            Eq(h.Step("Deadlock", null, t0.AddSeconds(30)), "Deadlock", "handoff: Hub's playing state (the usual detection) counts as running");
            Ok(h.Step(null, null, t0.AddSeconds(40)) == null && !h.Active, "handoff: ... and Hub saying nothing is played any more ends it");
            h.Start("1422450", "Deadlock", t0);
            Ok(h.Step(null, null, t0.AddMinutes(3)) == null && !h.Active, "handoff: a game that never shows up -> off after 2 minutes");
            Ok(SteamState.Running("abc") == null, "handoff: Steam's flag only for app ids");
        }
        static void ContrastTests()
        {
            Ok(Look.Contrast(SponsorBlock.BtnText, SponsorBlock.BtnBg) >= 7 && Look.Contrast(SponsorBlock.NoteText, SponsorBlock.BtnBg) >= 7, "contrast: the Skip button and its note on the video (" + Math.Round(Look.Contrast(SponsorBlock.NoteText, SponsorBlock.BtnBg), 1) + ":1) - the same in dark, light and private windows");
            var p = BrowserWindow.PrivatePalette;
            var pairs = new[] { Tuple.Create("dark Muted on Panel (the card's notes)", Look.Contrast("#9790A8", "#1C1726")), Tuple.Create("light Muted on Panel", Look.Contrast(Look.LMuted, Look.LPanel)), Tuple.Create("private Muted on Panel", Look.Contrast(p["Muted"], p["Panel"])), Tuple.Create("private Text on Panel", Look.Contrast(p["Text"], p["Panel"])) };
            foreach (var x in pairs) Ok(x.Item2 >= 4.5, "contrast: " + x.Item1 + " " + Math.Round(x.Item2, 2) + ":1");
        }
    }
}
