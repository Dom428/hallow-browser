// Hallow Browser 1.12.1 - unit tests: the Twitch page script version 3 (hide rules that can't hide the player, the guard, the
// watchdog), which names count as an ad box (from the real player's markup, Nov 2026) and which never do, and the memory target rule
// (only pages nobody sees go low). No network.
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HallowBrowser.Tests
{
    public static class V1121
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }

        public static void All() { ScriptTests(); NameTests(); MemoryTests(); FixtureTests(); }

        static void ScriptTests()
        {
            var js = TwitchAdRules.BuiltIn;
            Ok(TwitchAdRules.VersionOf(js) == 3 && TwitchAdRules.LooksRight(js), "twitch 1.12.1: the built-in script is version 3 (newer than 1.11 / 1.12's 2, so 1.10-1.12 take it from the release)");
            Ok(js.Contains(":not(:has(video))") && js.Contains("CSS.supports('selector(:not(:has(video)))')"), "twitch 1.12.1: each hide rule skips a box that holds the video (when the engine knows :has)");
            Ok(js.Contains("function guardPlayer(") && js.Contains("'display', 'block', 'important'") && js.Contains("data-hallow-keep"), "twitch 1.12.1: the guard shows a hidden box around the video again");
            Ok(js.Contains("function unblock(") && js.Contains("elementsFromPoint") && js.Contains("function stuck(") && js.Contains("setInterval(watchdog, 1000)"), "twitch 1.12.1: the watchdog looks at what covers the video and whether it moves, once a second");
            Ok(Regex.IsMatch(js, @"now - WD\.stuckSince >= 3500") && js.Contains("WD.tries < 3"), "twitch 1.12.1: a stuck video is nudged after 3.5 s, at most 3 tries");
            Ok(js.Contains("CFG.on && !allowedHere(null)) unblock(v)"), "twitch 1.12.1: on a channel you allow ads on, no ad frame is touched");
        }

        static Regex AdName()
        {
            var m = Regex.Match(TwitchAdRules.BuiltIn, @"var AD_NAME = /(.+)/i;");
            return m.Success ? new Regex(m.Groups[1].Value, RegexOptions.IgnoreCase) : null;
        }
        static void NameTests()
        {
            var re = AdName();
            Ok(re != null, "twitch 1.12.1: the ad-name rule is in the script");
            if (re == null) return;
            foreach (var n in new[] { "stream-display-ad__wrapper stream-display-ad__wrapper_squeezeback", "stream-display-ad__container_squeezeback", " sda-frame", "pbyp-ad__frame", "pbyp-layer", "StreamDisplayAdLayout",
                "video-ad-label", "ad-banner-default-text", "outstream-ax-overlay", "commercial-break-in-progress", "Advertisement", "picture-by-picture-player" })
                Ok(re.IsMatch(n), "twitch 1.12.1: an ad box by its name: " + n);
            foreach (var n in new[] { "Layout-sc-1xcs6mc-0 ehakif video-player__container video-player__video-container", "video-ref", "click-handler player-overlay-click-handler", "ax-overlay", "chat-shell",
                "extension-view__iframe", "side-nav-header side-nav-header-expanded", "chat-room-header-label", "player-controls", "content-classification-gate-overlay", "muted-segments-alert__content",
                "community-points-summary", "channel-leaderboard", "top-bar", "tw-root--theme-dark", "ScAspectSpacer-sc-18km980-0 shadow gradient header load badge" })
                Ok(!re.IsMatch(n), "twitch 1.12.1: never an ad box: " + n);
        }

        static void MemoryTests()
        {
            Ok(!MemoryRules.Low(true, true, false) && !MemoryRules.Low(true, false, true) && !MemoryRules.Low(true, true, true), "memory 1.12.1: the page you look at never gets the low memory target (game, CPU limiter or battery)");
            Ok(MemoryRules.Low(false, true, false) && MemoryRules.Low(false, false, true), "memory 1.12.1: a page nobody sees goes low while a game runs or on battery saver");
            Ok(!MemoryRules.Low(false, false, false), "memory 1.12.1: otherwise every page keeps the normal target");
        }

        static void FixtureTests()
        {
            var html = Integration1121.Player("whiteframe");
            Ok(html.Contains("data-test-selector='sda-wrapper'") && html.Contains("data-a-target='video-ref'") && html.Contains("pbyp-layer"), "twitch 1.12.1: the white-frame fixture has the real player's structure and the empty ad frame");
            Ok(!Regex.IsMatch(html, "ludwig|kaicenat|xqc|channel-id|user-id", RegexOptions.IgnoreCase), "twitch 1.12.1: the fixture is anonymous (no channel or user from the capture)");
            var wrapped = Integration1121.Player("wrapped");
            Ok(TwitchAdRules.Hide.Contains("[aria-label=\"Advertisement\"]") && wrapped.Contains("aria-label='Advertisement'"), "twitch 1.12.1: the 'hide rule matches the player' fixture uses a selector from the hide list");
        }
    }
}
