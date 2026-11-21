// Hallow Browser 1.1 - unit tests: the sidebar's rules (unread badges, sleeping, sites, user agent), live-notification parsing,
// the hot tabs CPU maths, the bandwidth cap, light / dark mode (resolution, the light theme, contrast) and the 1.0 -> 1.1 settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser.Tests
{
    public static class V11
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All() { SidebarTests(); LiveTests(); HotTests(); LookTests(); SettingsTests(); }

        static void SidebarTests()
        {
            Eq(SidebarRules.Unread("(3) Discord | #general"), "3", "badge: (3) Discord");
            Eq(SidebarRules.Unread("(99+) Inbox"), "99+", "badge: (99+)");
            Eq(SidebarRules.Unread("(250) WhatsApp"), "99+", "badge: big counts show 99+");
            Eq(SidebarRules.Unread("[2] Chat"), "2", "badge: [2]");
            Eq(SidebarRules.Unread("* (1) Twitch"), "1", "badge: a marker before the count");
            Eq(SidebarRules.Unread("Discord"), null, "no count, no badge");
            Eq(SidebarRules.Unread("(0) Discord"), null, "(0) = no badge");
            Eq(SidebarRules.Unread("Top 10 (2024) games"), null, "a number later in the title isn't a count");
            Eq(SidebarRules.Unread(null), null, "no title");
            var now = DateTime.Now;
            Ok(!SidebarRules.ShouldSleep(true, false, false, true, now.AddHours(-1), now, 10), "a shown panel never sleeps");
            Ok(!SidebarRules.ShouldSleep(false, false, false, false, now.AddMinutes(-5), now, 10), "hidden 5 of 10 minutes: awake");
            Ok(SidebarRules.ShouldSleep(false, false, false, false, now.AddMinutes(-11), now, 10), "hidden 11 of 10 minutes: sleeps");
            Ok(SidebarRules.ShouldSleep(false, false, false, true, now, now, 10), "game mode: a hidden panel sleeps at once");
            Ok(!SidebarRules.ShouldSleep(false, false, true, true, now.AddHours(-1), now, 10), "a panel playing sound (Spotify) stays awake, even in game mode");
            Ok(!SidebarRules.ShouldSleep(false, false, false, false, now.AddHours(-5), now, 0), "Never = never by time");
            Ok(!SidebarRules.ShouldSleep(false, true, false, true, now, now, 10), "already asleep: nothing to do");
            var d = SidebarRules.Defaults();
            Eq(string.Join(",", d.Select(x => x.Name)), "Discord,Twitch,YouTube,Spotify,WhatsApp", "default panels");
            Ok(d.All(x => x.Url.StartsWith("https://")), "default panels are https");
            Eq(SidebarRules.Brand("https://discord.com/app"), "discord", "brand icon: Discord");
            Eq(SidebarRules.Brand("https://m.youtube.com/"), "youtube", "brand icon: YouTube (m.)");
            Eq(SidebarRules.Brand("https://notdiscord.com/"), null, "brand icon: look-alike host gets none");
            var a = SidebarRules.FromAddress("reddit.com", d);
            Ok(a != null && a.Url == "https://reddit.com/" && a.Name == "Reddit" && a.Id == "reddit", "add a site: reddit.com");
            Eq(SidebarRules.FromAddress("cats and dogs", d), null, "add a site: a search isn't a site");
            Eq(SidebarRules.FromAddress("javascript:alert(1)", d), null, "add a site: javascript: refused");
            Eq(SidebarRules.FromAddress("https://discord.com/channels", d).Id, "discord2", "add a site: ids stay unique");
            Eq(SidebarRules.ClampWidth(100), SidebarRules.MinWidth, "width: at least the minimum");
            Eq(SidebarRules.ClampWidth(5000), SidebarRules.MaxWidth, "width: at most the maximum");
            var ua = SidebarRules.MobileAgent("158.0.4370.33");
            Ok(ua.Contains("Android") && ua.Contains("Mobile") && ua.Contains("Chrome/158.0.0.0"), "phone user agent with the engine's version: " + ua);
        }

        static void LiveTests()
        {
            Eq(LiveCheck.Normalize("shroud"), "twitch:shroud", "channel: a plain name is Twitch");
            Eq(LiveCheck.Normalize("https://www.twitch.tv/Shroud"), "twitch:shroud", "channel: twitch.tv address");
            Eq(LiveCheck.Normalize("twitch.tv/shroud/videos"), null, "channel: a twitch sub-page isn't a channel");
            Eq(LiveCheck.Normalize("@MrBeast"), "youtube:@MrBeast", "channel: @handle is YouTube");
            Eq(LiveCheck.Normalize("https://www.youtube.com/@MrBeast/live"), "youtube:@MrBeast", "channel: youtube.com/@handle/live");
            Eq(LiveCheck.Normalize("youtube:@x.y-z"), "youtube:@x.y-z", "channel: stored form round-trips");
            Eq(LiveCheck.Normalize("not a channel!"), null, "channel: nonsense refused");
            Eq(LiveCheck.Normalize("https://evil.example/@x"), null, "channel: other sites refused");
            Ok(LiveCheck.PageUrl("twitch:shroud").EndsWith("twitch.tv/shroud") || LiveCheck.PageUrl("twitch:shroud").EndsWith("/shroud"), "Twitch: the channel's own page");
            Ok(LiveCheck.PageUrl("youtube:@x").EndsWith("@x/live"), "YouTube: the handle's /live page");
            Ok(LiveCheck.TwitchLive("<script type=\"application/ld+json\">[{\"@type\":\"VideoObject\",\"publication\":{\"@type\":\"BroadcastEvent\",\"isLiveBroadcast\":true}}]</script>"), "Twitch page: live");
            Ok(!LiveCheck.TwitchLive("<html><title>shroud - Twitch</title></html>"), "Twitch page: offline");
            Ok(!LiveCheck.TwitchLive("\"isLiveBroadcast\":false"), "Twitch page: isLiveBroadcast false");
            Ok(LiveCheck.YouTubeLive("var ytInitialPlayerResponse = {\"videoDetails\":{\"isLiveContent\":true},\"microformat\":{\"liveBroadcastDetails\":{\"isLiveNow\":true}}};"), "YouTube page: live now");
            Ok(!LiveCheck.YouTubeLive("{\"liveBroadcastDetails\":{\"isLiveNow\":false,\"startTimestamp\":\"x\"}}"), "YouTube page: an old stream isn't live");
            Ok(!LiveCheck.YouTubeLive("<link rel=\"canonical\" href=\"https://www.youtube.com/@x\">"), "YouTube page: the channel page (no stream)");
            Eq(LiveCheck.StreamTitle("<meta property=\"og:description\" content=\"Ranked &amp; chill\">"), "Ranked & chill", "stream title from the page");
        }

        static void HotTests()
        {
            var c = HotTabs.CpuPercent(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1400), TimeSpan.FromMilliseconds(1000), 4);
            Ok(c.HasValue && Math.Abs(c.Value - 10) < 0.01, "CPU: 400 ms over 1 s on 4 cores = 10%");
            Eq(HotTabs.CpuPercent(TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), 4), null, "CPU: too short to tell");
            var rows = HotTabs.Sort(new[] { new HotRow { Kind = "tab", Name = "a", Cpu = 1, MemMb = 900 }, new HotRow { Kind = "engine", Name = "e", Cpu = 50 }, new HotRow { Kind = "tab", Name = "b", Cpu = 20, MemMb = 10 }, new HotRow { Kind = "panel", Name = "c", Cpu = 1, MemMb = 1000 } });
            Eq(string.Join(",", rows.Select(r => r.Name)), "b,c,a,e", "hot tabs: hottest CPU first, then memory; the engine last");
            Eq(Throttle.Want(1024, true, false), -1.0, "bandwidth: game-only cap without a game = no cap");
            Eq(Throttle.Want(1024, true, true), 1048576.0, "bandwidth: 1024 KB/s while a game runs");
            Eq(Throttle.Want(256, false, false), 262144.0, "bandwidth: always on");
            Eq(Throttle.Want(0, false, true), -1.0, "bandwidth: off");
            Ok(Throttle.Params(1000).Contains("\"downloadThroughput\":1000") && Throttle.Params(1000).Contains("\"uploadThroughput\":-1") && Throttle.Params(1000).Contains("\"offline\":false"), "DevTools parameters: " + Throttle.Params(1000));
        }

        static void LookTests()
        {
            // resolution (spec): missing -> dark; system -> Windows (a fake); the browser's own choice wins
            var f = Path.Combine(BrowserSettings.TestDir, "look-theme.json");
            if (File.Exists(f)) File.Delete(f);
            Eq(Look.ReadFamilyMode(f), "dark", "no theme.json = dark");
            File.WriteAllText(f, "{ broken"); Eq(Look.ReadFamilyMode(f), "dark", "broken theme.json = dark");
            File.WriteAllText(f, "{\"accent\":\"#22AA66\",\"mode\":\" LIGHT \",\"future\":1}"); Eq(Look.ReadFamilyMode(f), "light", "mode \" LIGHT \" = light (unknown fields ignored)");
            File.WriteAllText(f, "{\"mode\":\"neon\"}"); Eq(Look.ReadFamilyMode(f), "dark", "unknown mode = dark");
            Ok(Look.Resolve("follow", "system", () => true) && !Look.Resolve("follow", "system", () => false), "system follows Windows' setting (fake)");
            Ok(Look.Resolve("light", "dark", () => false) && !Look.Resolve("dark", "light", () => true), "the browser's own Look wins");
            Ok(Look.Resolve("follow", "light", () => false) && !Look.Resolve("follow", "dark", () => true), "follow = Hallow's mode");
            Eq(Engine.PageScheme("follow", true), CoreWebView2PreferredColorScheme.Light, "pages follow a light browser");
            Eq(Engine.PageScheme("follow", false), CoreWebView2PreferredColorScheme.Dark, "pages follow a dark browser");
            Eq(Engine.PageScheme("dark", true), CoreWebView2PreferredColorScheme.Dark, "pages: dark stays dark");
            Eq(Engine.PageScheme("auto", true), CoreWebView2PreferredColorScheme.Auto, "pages: like Windows");
            // the text accent for the 8 Toolkit accents
            foreach (var a in new[] { "#F07820", "#8B5CF6", "#3B82F6", "#06B6D4", "#22C55E", "#EC4899", "#EF4444", "#EAB308" })
            {
                var ta = Look.TextAccent(a);
                Ok(Look.Contrast(ta, "#FFFFFF") >= 4.5 && Look.Contrast(ta, Look.LBg) >= 4.5, "text accent for " + a + " = " + ta + " (" + Look.Contrast(ta, "#FFFFFF").ToString("0.00") + ":1 on white)");
            }
            // the light theme: every named colour swapped, parses, no dark values left in the brushes
            var x = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("HallowBrowser.Theme.xaml")).ReadToEnd();
            var light = Accent.ApplyTo(Look.LightXaml(x, "#F07820"), "#F07820");
            var rd = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Parse(light);
            Func<string, string> col = k => { var o = rd[k]; var c = o is System.Windows.Media.SolidColorBrush ? ((System.Windows.Media.SolidColorBrush)o).Color : (System.Windows.Media.Color)o; return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); };
            Eq(col("Bg"), "#F6F4FA", "light: background");
            Eq(col("Panel"), "#FFFFFF", "light: panels");
            Eq(col("Text"), "#1E1A26", "light: text");
            Eq(col("PageBg"), "#F6F4FA", "light: page background behind pages");
            Ok(col("AccentBrush") == Look.TextAccent("#F07820"), "light: the accent is the darkened text accent (" + col("AccentBrush") + ")");
            var darkLeft = new[] { "#14101B", "#110E17", "#1C1726", "#2A2336", "#ECE8F4", "#1E1829", "#221C2E" }.Where(h => light.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Ok(darkLeft.Count == 0, "light: no dark colour left anywhere in the theme (" + string.Join(" ", darkLeft) + ")");
            Ok(Regex.IsMatch(light, "#33F07820"), "light: see-through accent fills keep the real accent");
            var lightBlue = Accent.ApplyTo(Look.LightXaml(x, "#3B82F6"), "#3B82F6");
            Ok(!lightBlue.Contains("F07820") && lightBlue.Contains(Look.TextAccent("#3B82F6")) && lightBlue.Contains("#333B82F6"), "light + another accent: text accent and fills both swapped");
            // contrast: every text / background pair the light theme uses
            var bgs = new[] { "Bg", "Panel", "PanelHi", "Bar", "Strip", "FieldBg" };
            foreach (var fg in new[] { "Text", "Muted", "Lilac", "AccentBrush" })
                foreach (var bg in bgs)
                {
                    var cr = Look.Contrast(col(fg), col(bg));
                    // Lilac is text (section labels) only on Bg / Panel / Bar; on PanelHi / Strip it is only ever an icon (3:1)
                    bool iconOnly = fg == "AccentBrush" || (fg == "Lilac" && (bg == "PanelHi" || bg == "Strip"));
                    Ok(cr >= 4.5 || (iconOnly && cr >= 3.0), "contrast " + fg + " on " + bg + ": " + cr.ToString("0.00") + ":1" + (cr < 4.5 ? " (icons only)" : ""));
                }
            foreach (var k in new[] { "Green", "Amber", "Red" }) Ok(Look.Contrast(col(k), col("Panel")) >= 3.0, "contrast icon " + k + " on white: " + Look.Contrast(col(k), col("Panel")).ToString("0.00") + ":1");
            Ok(Look.Contrast("#FFFFFF", col("BadgeBg")) >= 4.5, "badge text on the light badge: " + Look.Contrast("#FFFFFF", col("BadgeBg")).ToString("0.00") + ":1");
            var dark = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Parse(x);
            var db = ((System.Windows.Media.SolidColorBrush)dark["BadgeBg"]).Color; Ok(Look.Contrast("#FFFFFF", "#" + db.R.ToString("X2") + db.G.ToString("X2") + db.B.ToString("X2")) >= 4.5, "badge text on the dark badge");
            // the new tab page in light
            var nt = Engine.NewTabHtml("duckduckgo", "#F07820", true);
            Ok(nt.Contains("color-scheme content=light") && nt.Contains("#F6F4FA") && !nt.Contains("#14101B") && nt.Contains(Look.TextAccent("#F07820")), "new tab page: light version");
            Ok(Engine.NewTabHtml("duckduckgo", "#F07820").Contains("#14101B"), "new tab page: dark unchanged");
            File.Delete(f);
        }

        static void SettingsTests()
        {
            var path = Path.Combine(BrowserSettings.Dir, "settings.json");
            File.WriteAllText(path, "{\"Search\":\"google\",\"PageColors\":\"dark\"}");   // a 1.0 file
            var s = BrowserSettings.Load();
            Ok(s.PageColors == "follow" && s.SettingsVersion >= 2, "1.0's default dark pages become \"follow\"");
            Ok(s.SidebarOn && s.SidebarSites.Count == 5 && s.SidebarSleepMinutes == 10 && !s.LiveOn && s.BandwidthKBps == 0 && s.BandwidthGameOnly && s.Look == "follow", "1.1 defaults: sidebar on with 5 panels, 10 min, live off, no cap, Look follow");
            s.PageColors = "dark"; s.SidebarSites.RemoveAt(0); s.LiveChannels = new List<string> { "twitch:abc", "TWITCH:ABC", "bogus channel!" }; s.BandwidthKBps = 1024; s.Save();
            var t = BrowserSettings.Load();
            Ok(t.PageColors == "dark", "a dark choice made in 1.1 stays dark");
            Eq(t.SidebarSites.Count, 4, "a removed panel stays removed");
            Eq(string.Join(",", t.LiveChannels), "twitch:abc", "channels: cleaned, no duplicates");
            Eq(t.BandwidthKBps, 1024, "bandwidth cap round-trip");
            t.SidebarSites.Clear(); t.Save(); Eq(BrowserSettings.Load().SidebarSites.Count, 0, "an empty sidebar stays empty (no defaults forced back)");
            File.WriteAllText(path, "{\"BandwidthKBps\":77,\"SidebarSleepMinutes\":3,\"Look\":\"neon\",\"SidebarSites\":[{\"Name\":\"x\",\"Url\":\"javascript:alert(1)\"},{\"Url\":\"https://a.com/\",\"Width\":5}]}");
            var b = BrowserSettings.Load();
            Ok(b.BandwidthKBps == 0 && b.SidebarSleepMinutes == 10 && b.Look == "follow", "bad 1.1 values fall back");
            Ok(b.SidebarSites.Count == 1 && b.SidebarSites[0].Name == "a.com" && b.SidebarSites[0].Id == "acom" && b.SidebarSites[0].Width == SidebarRules.MinWidth, "panels: non-web ones dropped, missing name/id/width fixed");
            File.Delete(path);
        }
    }
}
