// Hallow Browser 1.10 - unit tests: Twitch ad handling's settings (on by default, the allow list cleaned), which requests are blocked
// (ad hosts - never spade, countess, GraphQL, chat or emotes, never look-alikes), what counts as twitch.tv, the page script's version
// line and its update rules, and that the built-in script only ever names Twitch's own servers (no third-party proxy). No network.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HallowBrowser.Tests
{
    public static class V110
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All() { SettingsTests(); BlockTests(); ScriptTests(); }

        static void SettingsTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(s.TwitchAds && s.TwitchFixes && s.TwitchAllow != null && s.TwitchAllow.Count == 0, "twitch ads: on by default (the user asked for it), fixes on, nobody on the allow list");
            var d = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(BrowserSettings));
            using (var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"))) { var old = (BrowserSettings)d.ReadObject(ms); old.Fix(); Ok(old.TwitchAds && old.TwitchFixes && old.TwitchAllow != null, "twitch ads: settings from 1.9 (no such fields) -> on, like a new install"); }
            s.TwitchAllow = new List<string> { "SomeStreamer", "@other_one", "https://www.twitch.tv/third?x=1", "no", "bad name!", "somestreamer" }; s.Fix();
            Eq(string.Join(",", s.TwitchAllow), "somestreamer,other_one,third", "twitch ads: the allow list keeps channel names only (lower case, from @names and addresses, no junk or doubles)");
            Eq(SettingsLayout.SectionOf("Twitch ads"), "Gaming", "twitch ads: the card is in Gaming");
            Ok(SyncData.SettingKeys.Contains("TwitchAds") && SyncData.SettingKeys.Contains("TwitchAllow"), "twitch ads: the switch and the allow list sync");
        }
        static void BlockTests()
        {
            foreach (var u in new[] { "https://aax.amazon-adsystem.com/e/dtb/bid", "https://edge.ads.twitch.tv/ads?x", "https://imasdk.googleapis.com/js/sdkloader/ima3.js", "https://pubads.g.doubleclick.net/gampad/ads", "https://sb.scorecardresearch.com/b", "https://client-event-reporter.twitch.tv/x" })
                Ok(TwitchAdRules.Blocks(u), "twitch ads: blocks " + u);
            foreach (var u in new[] { "https://spade.twitch.tv/track", "https://countess.twitch.tv/x", "https://gql.twitch.tv/gql", "https://irc-ws.chat.twitch.tv/", "https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/1.0",
                "https://usher.ttvnw.net/api/channel/hls/x.m3u8", "https://video-edge-1.abc.hls.ttvnw.net/v1/segment/x.ts", "https://pubsub-edge.twitch.tv/v1", "https://www.twitch.tv/somestreamer", "https://assets.twitch.tv/assets/player.js",
                "https://amazon-adsystem.com.evil.net/x", "https://notamazon-adsystem.com/x", "file:///c:/amazon-adsystem.com/x" })
                Ok(!TwitchAdRules.Blocks(u), "twitch ads: never blocks " + u);
            Ok(TwitchAdRules.Filters().All(f => f.StartsWith("*://")) && TwitchAdRules.Filters().Count() == TwitchAdRules.BlockHosts.Length * 2, "twitch ads: request filters only for the ad hosts (nothing else goes through the handler)");
            Ok(TwitchAdRules.IsTwitch("https://www.twitch.tv/x") && TwitchAdRules.IsTwitch("https://player.twitch.tv/?channel=x") && TwitchAdRules.IsTwitch("https://twitch.tv/") &&
               !TwitchAdRules.IsTwitch("https://twitch.tv.evil.net/") && !TwitchAdRules.IsTwitch("http://www.twitch.tv/x") && !TwitchAdRules.IsTwitch("https://nottwitch.tv/") && !TwitchAdRules.IsTwitch(null), "twitch ads: twitch.tv pages (https) and nothing that only looks like one");
            Eq(TwitchAdRules.CleanChannel("https://www.twitch.tv/Streamer_1/videos"), "streamer_1", "twitch ads: a channel from its address");
            Ok(TwitchAdRules.CleanChannel("ab") == null && TwitchAdRules.CleanChannel("") == null && TwitchAdRules.CleanChannel(null) == null, "twitch ads: too short / empty isn't a channel");
        }
        static void ScriptTests()
        {
            var js = TwitchAdRules.BuiltIn;
            Ok(TwitchAdRules.VersionOf(js) >= 1 && TwitchAdRules.LooksRight(js), "twitch ads: the built-in script has its version line (" + TwitchAdRules.VersionOf(js) + ")");
            Eq(TwitchAdRules.VersionOf("// hallow-twitch 12\nx"), 12, "twitch ads: version line read");
            Eq(TwitchAdRules.VersionOf("\uFEFF// hallow-twitch 3\r\nx"), 3, "twitch ads: ... also with a BOM and CRLF");
            Ok(TwitchAdRules.VersionOf("// something else 12\n") == 0 && TwitchAdRules.VersionOf(null) == 0 && TwitchAdRules.VersionOf("x\n// hallow-twitch 5\n") == 0, "twitch ads: no version line = not our script");
            Ok(!TwitchAdRules.LooksRight("// hallow-twitch 5\nalert(1)"), "twitch ads: an update has to be the real script (its core), not just any file with the line");
            var urls = Regex.Matches(js, "https?://[A-Za-z0-9.-]+").Cast<Match>().Select(m => new Uri(m.Value).Host).Distinct().ToList();
            Ok(urls.All(h => h.EndsWith("twitch.tv") || h.EndsWith("ttvnw.net")), "twitch ads: the script names no server but Twitch's own (" + string.Join(", ", urls) + ")");
            Ok(new Uri(TwitchAdRules.Gql).Host == "gql.twitch.tv" && new Uri(TwitchAdRules.Usher).Host == "usher.ttvnw.net", "twitch ads: tokens and playlists only from gql.twitch.tv and usher.ttvnw.net");
            foreach (var p in new[] { "ttv-lol", "ttv.lol", "luminous", "perfprod", "proxy." }) Ok(js.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0, "twitch ads: no third-party proxy in the script (" + p + ")");
            Ok(js.Contains("credentials: 'omit'") && !js.Contains("credentials: 'include'"), "twitch ads: Twitch requests of its own go without cookies");
            var s = new BrowserSettings(); s.TwitchAllow.Add("friend");
            var c = TwitchAdRules.Compose(s);
            Ok(c.StartsWith("(function(CFG){\n") && c.Contains("\"allow\":[\"friend\"]") && c.Contains("\"clientId\":\"" + TwitchAdRules.ClientId + "\"") && c.Contains("\"types\":[\"embed\",\"popout\",\"autoplay\",\"picture-by-picture\"]"), "twitch ads: the page gets the settings: allow list, client id, the player types to try");
            Ok(c.Contains("\"gql\":\"https://gql.twitch.tv/gql\"") && c.Contains("\"usher\":\"https://usher.ttvnw.net\"") && c.Contains("\"test\":null"), "twitch ads: outside tests the real Twitch addresses, no test base");
            Ok(TwitchAdRules.Hide.All(h => !Regex.IsMatch(h, "chat|emote|community-points|channel-points", RegexOptions.IgnoreCase)), "twitch ads: nothing about chat, emotes or channel points is hidden");
        }
    }
}
