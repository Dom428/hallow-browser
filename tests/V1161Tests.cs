// Hallow Browser 1.16.1 - unit tests: the Twitch page script version 5 (keep the stream going during ads: more backups asked at
// once, a lower quality for the break, Twitch's own headers for the backup token, the badge, every attempt in the decisions). No network.
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HallowBrowser.Tests
{
    public static class V1161
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }

        public static void All()
        {
            var js = TwitchAdRules.BuiltIn;
            Ok(TwitchAdRules.VersionOf(js) >= 5 && TwitchAdRules.LooksRight(js), "twitch 1.16.1: the built-in script is version 5 or newer (1.10+ installs take it from the release)");
            Ok(js.Contains("CFG.types5 || ['embed', 'popout', 'autoplay', 'site', 'picture-by-picture', 'thunderdome', 'frontpage']"), "twitch 1.16.1: seven backups in order, from the script itself (an older browser's shorter list is ignored)");
            Ok(string.Join(",", TwitchAdRules.PlayerTypes) == "embed,popout,autoplay,site,picture-by-picture,thunderdome,frontpage", "twitch 1.16.1: the browser's own list matches");
            Ok(js.Contains("PLATFORM = { autoplay: 'android' }") && js.Contains("platform: PLATFORM[type] || 'web'"), "twitch 1.16.1: autoplay is asked for as Twitch's Android app asks for it");
            Ok(js.Contains("Promise.all(rest.map(function (t) { return attempt(ch, t, want, false); }))") && js.Contains("function lowOf(") && js.Contains("h <= 480"), "twitch 1.16.1: every backup asked at once; then their 480p / 360p streams (lower quality for the break)");
            Ok(js.Contains("setTimeout(function () { if (!r.ok && !r.why) r.why = 'timeout'; res(r); }, 6000)"), "twitch 1.16.1: a backup that doesn't answer is given up after 6 s (the player isn't kept waiting)");
            Ok(js.Contains("'x-device-id': 'X-Device-Id'") && js.Contains("'client-integrity': 'Client-Integrity'") && !Regex.IsMatch(js, "'authorization'\\s*:", RegexOptions.IgnoreCase), "twitch 1.16.1: the backup token uses Twitch's device id / integrity headers from the page - never Authorization");
            Ok(js.Contains("va.playerType === 'site') { va.playerType = 'popout'") && js.Contains("(CFG.allow || []).indexOf(CH_NOW()) < 0"), "twitch 1.16.1: the player's own token is asked as popout (not on channels you allow ads on)");
            Ok(js.Contains("searchParams.delete('parent_domains')"), "twitch 1.16.1: parent_domains is left out of usher addresses");
            Ok(js.Contains("'ad-free backup'") && js.Contains("pointer-events:none") && js.Contains("hallow-backup"), "twitch 1.16.1: the small \"ad-free backup (480p)\" badge, never in the way of a click");
            Ok(js.Contains("function summary(") && js.Contains("'no clean backup for '") && js.Contains("decide(String(m.m).slice(0, 700))"), "twitch 1.16.1: every backup asked for and why it failed is kept in the decisions");
            Ok(js.Contains("post({ ev: 'clean', ch: c, main: !!(oa && oa.lower) })") && js.Contains("oa.backBy = Date.now() + 5000"), "twitch 1.16.1: a lower-quality backup goes back to the player's own quality after the break");
            Ok(js.Contains("liftCover('swap')"), "twitch 1.16.1: a backup found while the cover is up lifts the cover");
            // kept from v4
            Ok(js.Contains("function blankCheck(") && js.Contains("function guardPlayer(") && js.Contains("function gqlAdEvents(") && js.Contains("function domAd(") && js.Contains("function showCover(") && js.Contains("window.__hallowTwitch.report = function"), "twitch 1.16.1: v4's cover, white-player recovery, ad signals and report are still there");
            Ok(js.Contains("credentials: 'omit'") && !Regex.IsMatch(js, @"https?://(?!gql\.twitch|usher\.ttvnw)[a-z0-9.-]+\.(com|net|org|io)/", RegexOptions.IgnoreCase), "twitch 1.16.1: no cookies on the backup requests, no outside servers");
        }
    }
}
