// Hallow Browser 1.1 - Twitch / YouTube live notifications for channels you list (off by default). No API keys and no third-party
// service: every 5 minutes it loads each channel's own public page (twitch.tv/<name>, youtube.com/@<handle>/live) like a browser
// would and looks for the "live" markers in it. Honestly: that is page-scraping - if Twitch or YouTube change their pages it can
// stop seeing streams until an update fixes it. Notifications go through Hallow Hub's icon when Hub runs, otherwise a Windows
// balloon from a short-lived tray icon. Only the channel pages are fetched; nothing about you is sent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HallowBrowser
{
    public static class LiveCheck
    {
        public static string TwitchBase { get { return BrowserSettings.LiveTwitchBase ?? "https://www.twitch.tv/"; } }
        public static string YouTubeBase { get { return BrowserSettings.LiveYouTubeBase ?? "https://www.youtube.com/"; } }
        public static TimeSpan Interval { get { int s; return BrowserSettings.LiveSeconds != null && int.TryParse(BrowserSettings.LiveSeconds, out s) && s > 0 ? TimeSpan.FromSeconds(s) : TimeSpan.FromMinutes(5); } }

        // what you type -> "twitch:<name>" or "youtube:@<handle>" (null when it isn't a channel)
        //   "shroud", "twitch.tv/shroud", "https://www.twitch.tv/shroud" -> twitch:shroud
        //   "@MrBeast", "youtube.com/@MrBeast", "yt:@MrBeast"              -> youtube:@MrBeast (handles keep their case, for display)
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim();
            var m = Regex.Match(s, @"^(?:twitch:)([A-Za-z0-9_]{2,25})$", RegexOptions.IgnoreCase);
            if (m.Success) return "twitch:" + m.Groups[1].Value.ToLowerInvariant();
            m = Regex.Match(s, @"^(?:youtube:|yt:)?@([A-Za-z0-9_.\-]{3,30})$");
            if (m.Success) return "youtube:@" + m.Groups[1].Value;
            m = Regex.Match(s, @"^(?:https?://)?(?:www\.|m\.)?twitch\.tv/([A-Za-z0-9_]{2,25})/?(?:[?#].*)?$", RegexOptions.IgnoreCase);
            if (m.Success) return "twitch:" + m.Groups[1].Value.ToLowerInvariant();
            m = Regex.Match(s, @"^(?:https?://)?(?:www\.|m\.)?youtube\.com/@([A-Za-z0-9_.\-]{3,30})(?:/\w*)?/?(?:[?#].*)?$", RegexOptions.IgnoreCase);
            if (m.Success) return "youtube:@" + m.Groups[1].Value;
            m = Regex.Match(s, @"^([A-Za-z0-9_]{2,25})$");
            if (m.Success) return "twitch:" + m.Groups[1].Value.ToLowerInvariant();
            return null;
        }
        public static bool IsTwitch(string ch) { return ch.StartsWith("twitch:"); }
        public static string Name(string ch) { return ch.Substring(ch.IndexOf(':') + 1); }
        public static string Display(string ch) { return IsTwitch(ch) ? Name(ch) + " (Twitch)" : Name(ch) + " (YouTube)"; }
        public static string PageUrl(string ch) { return IsTwitch(ch) ? TwitchBase + Uri.EscapeDataString(Name(ch)) : YouTubeBase + Name(ch) + "/live"; }
        public static string WatchUrl(string ch) { return IsTwitch(ch) ? "https://www.twitch.tv/" + Name(ch) : "https://www.youtube.com/" + Name(ch) + "/live"; }

        // Twitch's channel page carries schema.org JSON with "isLiveBroadcast":true while the channel is live (and a stream title).
        public static bool TwitchLive(string html) { return html != null && Regex.IsMatch(html, "\"isLiveBroadcast\"\\s*:\\s*true"); }
        // YouTube's /@handle/live goes to the live video while there is one: its player data says "isLiveNow":true
        // (a scheduled stream or the last stream doesn't); an older form is "isLive":true on a watch page.
        public static bool YouTubeLive(string html)
        {
            if (html == null) return false;
            if (Regex.IsMatch(html, "\"isLiveNow\"\\s*:\\s*true")) return true;
            return Regex.IsMatch(html, "<link rel=\"canonical\" href=\"https://www\\.youtube\\.com/watch\\?v=") && Regex.IsMatch(html, "\"isLive\"\\s*:\\s*true") && !html.Contains("\"isUpcoming\":true");
        }
        public static bool IsLive(string ch, string html) { return IsTwitch(ch) ? TwitchLive(html) : YouTubeLive(html); }
        // the stream title when the page has one (Twitch's JSON "description" / og:title), else null
        public static string StreamTitle(string html)
        {
            if (html == null) return null;
            var m = Regex.Match(html, "<meta property=\"og:description\" content=\"([^\"]{1,200})\"");
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        // one channel page, like a browser loads it (desktop Chrome's user agent; YouTube gets "reject all" for its consent screen)
        public static string Fetch(string url)
        {
            try
            {
                var rq = (HttpWebRequest)WebRequest.Create(url);
                rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" + ((Engine.RuntimeVersion ?? "130").Split('.')[0]) + ".0.0.0 Safari/537.36";
                rq.Accept = "text/html"; rq.Headers["Accept-Language"] = "en-US,en;q=0.8"; rq.Timeout = 15000; rq.ReadWriteTimeout = 15000;
                rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                if (url.Contains("youtube.com")) rq.Headers["Cookie"] = "SOCS=CAI";
                using (var r = (HttpWebResponse)rq.GetResponse())
                using (var s = r.GetResponseStream())
                {
                    var ms = new MemoryStream(); var buf = new byte[65536]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0 && ms.Length < 4 * 1024 * 1024) ms.Write(buf, 0, n);
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            catch { return null; }
        }
    }

    // the watcher: a background thread, one channel at a time, a notification when a channel goes live (once per stream)
    public class LiveWatcher
    {
        readonly BrowserWindow w;
        readonly Dictionary<string, bool> live = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> LastSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // channel -> live / offline / unknown
        public string LastNotify; public DateTime LastRun;
        Timer timer; int running;
        public LiveWatcher(BrowserWindow w) { this.w = w; }
        public void Start()
        {
            // the first look a minute after start (never in the way of the first page), then every 5 minutes
            var first = BrowserSettings.LiveSeconds != null ? LiveCheck.Interval : TimeSpan.FromSeconds(60);
            timer = new Timer(_ => RunOnce(), null, first, LiveCheck.Interval);
        }
        public void Stop() { if (timer != null) timer.Dispose(); }
        // checks every channel now; returns what it saw (also for the test hook)
        public void RunOnce()
        {
            if (Interlocked.Exchange(ref running, 1) == 1) return;
            try
            {
                List<string> chans; bool on;
                lock (w.S) { on = w.S.LiveOn; chans = w.S.LiveChannels.ToList(); }
                if (!on) return;
                LastRun = DateTime.Now;
                foreach (var ch in chans)
                {
                    var html = LiveCheck.Fetch(LiveCheck.PageUrl(ch));
                    if (html == null) { LastSeen[ch] = "unknown"; continue; }   // couldn't load: say nothing, keep the last state
                    var now = LiveCheck.IsLive(ch, html); bool was;
                    live.TryGetValue(ch, out was);
                    LastSeen[ch] = now ? "live" : "offline";
                    live[ch] = now;
                    if (now && !was) Notify(ch, LiveCheck.StreamTitle(html));
                    Thread.Sleep(BrowserSettings.LiveSeconds != null ? 50 : 1500);   // light on the sites
                }
                foreach (var k in live.Keys.Where(k => !chans.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) live.Remove(k);
            }
            catch { }
            finally { Interlocked.Exchange(ref running, 0); }
        }
        void Notify(string ch, string streamTitle)
        {
            var title = LiveCheck.Name(ch) + " is live";
            var text = (streamTitle != null ? streamTitle + " - " : "") + (LiveCheck.IsTwitch(ch) ? "on Twitch" : "on YouTube") + ". Click to watch in Hallow Browser.";
            var hub = Family.Send("Hub", "notify Browser\t" + title + "\t" + text.Replace("\t", " "), 3000);
            if (hub != null && (hub == "ok" || hub.StartsWith("ok "))) { LastNotify = "hub: " + title; return; }
            LastNotify = "tray: " + title;
            if (BrowserSettings.Offscreen) return;   // tests: never a balloon on the screen
            var url = LiveCheck.WatchUrl(ch);
            w.Dispatcher.BeginInvoke(new Action(() => Tray.Balloon(title, text, () => { w.NewTab(url, true); w.Bring(); })));
        }
    }

    // a short-lived tray icon just for a balloon (Hallow Browser has no tray icon of its own)
    public static class Tray
    {
        public static void Balloon(string title, string text, Action click)
        {
            try
            {
                var ni = new System.Windows.Forms.NotifyIcon { Text = "Hallow Browser", Visible = true };
                try { ni.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
                ni.BalloonTipClicked += (a, b) => { try { click(); } catch { } ni.Dispose(); };
                ni.BalloonTipClosed += (a, b) => ni.Dispose();
                ni.ShowBalloonTip(10000, title, text.Length > 240 ? text.Substring(0, 240) : text, System.Windows.Forms.ToolTipIcon.Info);
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                t.Tick += (a, b) => { t.Stop(); try { ni.Dispose(); } catch { } };
                t.Start();
            }
            catch { }
        }
    }
}
