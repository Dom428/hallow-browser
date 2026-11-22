// Hallow Browser 1.10 - Twitch ad handling (on by default; the user asked for it).
//  - Stitched ads (server-side ad insertion): the page part is src\hallow-twitch.js, run at document start on twitch.tv pages in the
//    page itself (AddScriptToExecuteOnDocumentCreatedAsync - not an extension, so private windows get it too). It wraps Worker
//    creation so the player worker's playlist fetches can be looked at; a playlist with ad markers is swapped for the same channel's
//    stream as another Twitch player type (embed, popout, ...) fetched from Twitch itself, or - when every alternative has the ad -
//    the ad plays muted under a Hallow "Ad break - back in 0:28" cover. Only Twitch's own servers (gql.twitch.tv, usher.ttvnw.net)
//    are contacted, never a third-party proxy, and with no cookies or sign-in.
//  - Display ads and ad beacons: requests from Twitch tabs to ad hosts are blocked here, in the browser (WebResourceRequested - works
//    in private windows, where extensions don't run); ad labels and display-ad boxes are hidden by the page part; GraphQL calls that
//    only report ad events are answered in the page instead of sent. Left alone on purpose: spade.twitch.tv (it carries the
//    minute-watched events that earn channel points and drops) and countess.twitch.tv (creators' view counts), chat, emotes and
//    channel points.
//  - "Allow ads on these channels": nothing above happens on those channels - their ads play and are counted, so the streamer is paid.
//  - Twitch changes this often. Newer page parts can come between browser releases from this repo's GitHub release (asset
//    hallow-twitch.js, checked against GitHub's sha256; used only when its version line is newer than the built-in one).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        // ---- 1.10 settings (per profile)
        [DataMember] public bool TwitchAds = true;           // ON by default
        [DataMember] public List<string> TwitchAllow = new List<string>();   // channels whose ads play (support them)
        [DataMember] public bool TwitchFixes = true;         // newer page parts from the GitHub release (sha256-checked); ON
        [DataMember] public string LastTwitchCheck;

        // 1.10 tests: local stand-ins for Twitch's GraphQL and usher (never the real ones)
        public static readonly string TwitchApiTest = Env("HALLOW_BROWSER_TESTDIR") != null ? Env("HALLOW_BROWSER_TWITCH_API") : null;
        // 1.11 tests: the page part writes what it sees (worker playlists, alternatives tried) to window.__hallowTwitch.diag - never in normal use
        public static readonly bool TwitchDiag = Env("HALLOW_BROWSER_TESTDIR") != null && Env("HALLOW_BROWSER_TWITCH_DIAG") == "1";

        void Defaults110() { TwitchAds = true; TwitchAllow = new List<string>(); TwitchFixes = true; }
        void Fix110()
        {
            TwitchAllow = (TwitchAllow ?? new List<string>()).Select(TwitchAdRules.CleanChannel).Where(c => c != null).Distinct().ToList();
        }
    }

    public static class TwitchAdRules
    {
        public const string ClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";   // the public web client id twitch.tv itself sends
        public const string Gql = "https://gql.twitch.tv/gql", Usher = "https://usher.ttvnw.net";
        public const string AssetName = "hallow-twitch.js";
        public static readonly string[] PlayerTypes = { "embed", "popout", "autoplay", "picture-by-picture" };
        // ad hosts blocked for Twitch tabs (subdomains included). Not spade / countess: channel points, drops and view counts need them.
        public static readonly string[] BlockHosts = {
            "amazon-adsystem.com", "edge.ads.twitch.tv", "ads.ttvnw.net", "imasdk.googleapis.com", "pubads.g.doubleclick.net", "securepubads.g.doubleclick.net",
            "ad.doubleclick.net", "googletagservices.com", "scorecardresearch.com", "imrworldwide.com", "adsrvr.org", "adnxs.com", "client-event-reporter.twitch.tv" };
        // display-ad boxes and ad labels on twitch.tv (CSS selectors; never chat, emotes or channel points)
        public static readonly string[] Hide = {
            "[data-a-target=\"video-ad-label\"]", "[data-a-target=\"video-ad-countdown\"]", "[data-test-selector=\"ad-banner-default-text\"]",
            ".stream-display-ad__wrapper", ".stream-display-ad__container", "[data-test-selector=\"sda-wrapper\"]", "[data-a-target=\"sda-frame\"]",
            ".video-ad-display__container", "[data-test-selector=\"display-ad\"]", "div[class*=\"StreamDisplayAd\"]", "[aria-label=\"Advertisement\"]",
            "[data-a-target=\"outstream-ax-overlay\"]", "[data-test-selector=\"commercial-break-in-progress\"]" };
        // GraphQL operations that only report ad events (a call made only of these is answered in the page)
        public static readonly string[] AdOps = { "ClientSideAdEventHandling_RecordAdEvent", "VideoAdRequestDecline", "VideoAdUI_RecordAdEvent" };

        static readonly Regex nameRe = new Regex("^[a-z0-9_]{3,25}$", RegexOptions.Compiled);
        public static string CleanChannel(string s)
        {
            if (s == null) return null;
            s = s.Trim().TrimStart('@').ToLowerInvariant();
            var m = Regex.Match(s, "twitch\\.tv/([a-z0-9_]+)"); if (m.Success) s = m.Groups[1].Value;
            return nameRe.IsMatch(s) ? s : null;
        }

        // ---- host-side blocking. In tests a stand-in address <twitch test base>/h/<host>/<path> counts as https://<host>/<path>.
        public static string Normalize(string url)
        {
            var t = BrowserSettings.TwitchTest;
            if (t != null && url != null)
            {
                var o = Origin(t) + "/h/";
                if (url.StartsWith(o, StringComparison.OrdinalIgnoreCase)) return "https://" + url.Substring(o.Length);
            }
            return url;
        }
        public static string Origin(string url) { Uri u; return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.GetLeftPart(UriPartial.Authority) : url; }
        public static bool Blocks(string url)
        {
            Uri u; if (!Uri.TryCreate(Normalize(url), UriKind.Absolute, out u) || (u.Scheme != "https" && u.Scheme != "http")) return false;
            var h = u.Host.ToLowerInvariant();
            foreach (var b in BlockHosts) if (h == b || h.EndsWith("." + b)) return true;
            return false;
        }
        public static IEnumerable<string> Filters()
        {
            foreach (var b in BlockHosts) { yield return "*://" + b + "/*"; yield return "*://*." + b + "/*"; }
            if (BrowserSettings.TwitchTest != null) yield return Origin(BrowserSettings.TwitchTest) + "/h/*";
        }
        // a page on twitch.tv (or the test pages)
        public static bool IsTwitch(string url)
        {
            if (url == null) return false;
            if (BrowserSettings.TwitchTest != null && url.StartsWith(BrowserSettings.TwitchTest + "/", StringComparison.OrdinalIgnoreCase)) return true;
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https") return false;
            var h = u.Host.ToLowerInvariant();
            return h == "twitch.tv" || h.EndsWith(".twitch.tv");
        }

        // ---- the page part: built in, or a newer copy from the GitHub release
        public static string UpdatesDir { get { return Path.Combine(BrowserSettings.RootLocalDir, "Twitch"); } }
        public static string Downloaded { get { return Path.Combine(UpdatesDir, AssetName); } }
        public static int VersionOf(string script)
        {
            if (script == null) return 0;
            var m = Regex.Match(script, "^\\uFEFF?// hallow-twitch (\\d{1,6})\\r?\\n");
            int v; return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }
        public static bool LooksRight(string script) { return VersionOf(script) > 0 && script.Contains("function core(") && script.Length < 512 * 1024; }
        static string builtIn, current; static int currentV;
        public static string BuiltIn
        {
            get
            {
                if (builtIn == null) using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("HallowBrowser.hallow-twitch.js")) using (var r = new StreamReader(s, Encoding.UTF8)) builtIn = r.ReadToEnd();
                return builtIn;
            }
        }
        public static string Source { get; private set; }   // "built-in" or "update"
        public static string Current
        {
            get
            {
                if (current != null) return current;
                var best = BuiltIn; Source = "built-in";
                try { if (File.Exists(Downloaded)) { var d = File.ReadAllText(Downloaded, Encoding.UTF8); if (LooksRight(d) && VersionOf(d) > VersionOf(best)) { best = d; Source = "update"; } } } catch { }
                currentV = VersionOf(best); current = best;
                return current;
            }
        }
        public static int CurrentVersion { get { var _ = Current; return currentV; } }
        public static void Reload() { current = null; }

        public static string Config(BrowserSettings s)
        {
            var api = BrowserSettings.TwitchApiTest;
            var d = new Dictionary<string, object> {
                { "version", CurrentVersion }, { "on", s.TwitchAds }, { "allow", s.TwitchAllow ?? new List<string>() },
                { "gql", api != null ? api + "/gql" : Gql }, { "usher", api != null ? api + "/usher" : Usher }, { "clientId", ClientId }, { "types", PlayerTypes },
                { "hide", string.Join(",", Hide) }, { "adOps", AdOps }, { "accent", Program.AccentHex ?? "#9B6BFF" }, { "test", BrowserSettings.TwitchTest } };
            if (BrowserSettings.TwitchDiag) d["diag"] = true;
            return new JavaScriptSerializer().Serialize(d);
        }
        public static string Compose(BrowserSettings s) { return "(function(CFG){\n" + Current + "\n})(" + Config(s) + ");"; }

        // ---- fixes between browser releases: the newest release's hallow-twitch.js asset, sha256-checked against GitHub's digest
        public static string CheckForUpdate()
        {
            var rel = Updater.Latest();
            var a = rel == null || rel.Assets == null ? null : rel.Assets.FirstOrDefault(x => string.Equals(x.Name, AssetName, StringComparison.OrdinalIgnoreCase));
            if (a == null) return null;
            try
            {
                Directory.CreateDirectory(UpdatesDir);
                var tmp = Path.Combine(UpdatesDir, "download.tmp");
                var err = Updater.DownloadChecked(a, tmp);
                if (err != null) return "Twitch ad handling not updated: " + err + ".";
                var text = File.ReadAllText(tmp, Encoding.UTF8);
                if (!LooksRight(text)) { File.Delete(tmp); return "Twitch ad handling not updated: the download isn't Hallow Browser's Twitch script."; }
                if (VersionOf(text) <= CurrentVersion) { File.Delete(tmp); return null; }
                if (File.Exists(Downloaded)) File.Delete(Downloaded);
                File.Move(tmp, Downloaded);
                Reload();
                return "Twitch ad handling updated (version " + VersionOf(text) + ") - Twitch pages use it from their next load.";
            }
            catch (Exception e) { return "Twitch ad handling update failed: " + e.Message; }
        }
    }

    public partial class Tab { public string TwNote; public int TwSwaps, TwCovers, TwScriptV; public bool TwMutedByUs, TwCovered; public DateTime TwWhen; }

    public partial class BrowserWindow
    {
        public static int TwBlocked;   // requests to ad hosts blocked (tests, Settings)
        readonly Dictionary<CoreWebView2, string> twScripts = new Dictionary<CoreWebView2, string>();
        readonly Dictionary<CoreWebView2, Task> twPending = new Dictionary<CoreWebView2, Task>();
        // 1.11: a tab that opens straight onto twitch.tv waits for the page part to be registered first (else its first page could
        // load without it - the registration is asynchronous)
        public async Task TwReady(CoreWebView2 c)
        {
            Task p; if (c == null || !twPending.TryGetValue(c, out p)) return;
            try { await Task.WhenAny(p, Task.Delay(3000)); } catch { }
        }

        void Hook110(CoreWebView2 c)
        {
            foreach (var f in TwitchAdRules.Filters()) try { c.AddWebResourceRequestedFilter(f, CoreWebView2WebResourceContext.All); } catch { }
            c.WebResourceRequested += OnTwitchRequest;
            c.WebMessageReceived += (a, b) =>
            {
                string m = null; try { m = b.TryGetWebMessageAsString(); } catch { }
                if (m == null || !m.StartsWith("hallow:twitch\t") || !TwitchAdRules.IsTwitch(b.Source)) return;
                Tab x; if (!owner.TryGetValue(c, out x)) return;
                OnTwitchMessage(x, c, m.Substring(14));
            };
            c.NavigationStarting += (a, b) =>
            {
                Tab x; if (!owner.TryGetValue(c, out x)) return;
                if (x.TwMutedByUs) { try { c.IsMuted = false; } catch { } x.TwMutedByUs = false; }
                x.TwCovered = false;
            };
            twPending[c] = TwRegister(c);
        }
        // the sidebar's panels (Twitch chat): only the blocking
        void HookPanel110(CoreWebView2 c)
        {
            foreach (var f in TwitchAdRules.Filters()) try { c.AddWebResourceRequestedFilter(f, CoreWebView2WebResourceContext.All); } catch { }
            c.WebResourceRequested += OnTwitchRequest;
        }
        async Task TwRegister(CoreWebView2 c)
        {
            string old;
            if (twScripts.TryGetValue(c, out old)) { twScripts.Remove(c); try { c.RemoveScriptToExecuteOnDocumentCreated(old); } catch { } }
            if (!S.TwitchAds) return;
            try { twScripts[c] = await c.AddScriptToExecuteOnDocumentCreatedAsync(TwitchAdRules.Compose(S)); } catch { }
        }
        void OnTwitchRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                if (!S.TwitchAds || e.ResourceContext == CoreWebView2WebResourceContext.Document || !TwitchAdRules.Blocks(e.Request.Uri)) return;
                var c = sender as CoreWebView2; string page = null; try { page = c == null ? null : c.Source; } catch { }
                if (!TwitchAdRules.IsTwitch(page)) return;                       // only Twitch's tabs (Shield looks after the rest)
                var ch = Twitch.Channel(page);
                if (ch != null && S.TwitchAllow.Contains(ch)) return;          // a channel you support: its ads and their beacons go through
                e.Response = Engine.Env.CreateWebResourceResponse(null, 403, "Blocked by Hallow Browser (Twitch ads)", "");
                TwBlocked++;
            }
            catch { }
        }
        void OnTwitchMessage(Tab t, CoreWebView2 c, string json)
        {
            Dictionary<string, object> m; try { m = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); } catch { return; }
            if (m == null) return;
            object o; string ev = m.TryGetValue("ev", out o) ? o as string : null;
            switch (ev)
            {
                case "ready": try { t.TwScriptV = Convert.ToInt32(m["v"]); } catch { } break;
                case "swap": t.TwSwaps++; t.TwNote = "Ad break skipped (swapped stream)"; t.TwWhen = DateTime.Now; break;
                case "cover":
                    t.TwCovers++; t.TwCovered = true; t.TwNote = "Ad covered"; t.TwWhen = DateTime.Now;
                    try { if (!c.IsMuted) { c.IsMuted = true; t.TwMutedByUs = true; } } catch { }
                    break;
                case "lift":
                    t.TwCovered = false;
                    if (t.TwMutedByUs) { t.TwMutedByUs = false; try { c.IsMuted = false; } catch { } }
                    break;
                case "allowed": t.TwNote = "Ads play on this channel - you support it"; t.TwWhen = DateTime.Now; break;
                default: return;
            }
            if (t == Active) UpdateTwitchButton(t);
        }

        // ---- settings changed: every tab gets the new page part; Twitch pages load again so it applies
        public void TwitchSettingsChanged(Tab only = null)
        {
            foreach (var w in Program.Windows.ToList())
                foreach (var t in w.Tabs) if (t.Core != null)
                    {
                        var c = t.Core; var url = t.Url;
                        w.TwRegister(c).ContinueWith(_ => w.Dispatcher.BeginInvoke(new Action(() => { if ((only == null || only == t) && TwitchAdRules.IsTwitch(url) && t.Core == c) try { c.Reload(); } catch { } })));
                    }
            foreach (var w in Program.Windows) if (w.Active != null) w.UpdateTwitchButton(w.Active);
        }
        public bool SupportChannel(string ch, bool on, Tab reload)
        {
            ch = TwitchAdRules.CleanChannel(ch); if (ch == null) return false;
            S.TwitchAllow.Remove(ch); if (on) S.TwitchAllow.Add(ch); S.Save();
            TwitchSettingsChanged(reload);
            return true;
        }

        // ---- the Twitch button in the address bar (on a channel): what happened to the last ad break, and "Support <channel>"
        Button twitchBtn;
        void BuildTwitchButton(Panel inBar)
        {
            twitchBtn = Ui.Tool("twitch", "Twitch ads", () => TwitchFlyout(Active), 13); twitchBtn.Height = 24; twitchBtn.Width = 28; twitchBtn.Visibility = Visibility.Collapsed;
            inBar.Children.Insert(0, twitchBtn);
        }
        public bool TwitchButtonVisible { get { return twitchBtn != null && twitchBtn.Visibility == Visibility.Visible; } }
        public string TwitchButtonTip { get { return twitchBtn == null ? null : twitchBtn.ToolTip as string; } }
        public string TwitchStatus(Tab t)
        {
            var ch = t == null ? null : Twitch.Channel(t.Url);
            if (!S.TwitchAds) return "Twitch ads aren't blocked (Settings > Gaming > Twitch ads)";
            if (ch != null && S.TwitchAllow.Contains(ch)) return "Ads play on this channel - you support " + ch;
            if (t != null && t.TwNote != null) return t.TwNote + " - " + Ago(t.TwWhen);
            return "Blocking Twitch ads - no ad break in this tab yet";
        }
        static string Ago(DateTime d) { var s = (DateTime.Now - d).TotalSeconds; return s < 60 ? "just now" : s < 3600 ? (int)(s / 60) + " min ago" : d.ToString("HH:mm"); }
        public void UpdateTwitchButton(Tab t)
        {
            if (twitchBtn == null || t == null) return;
            var ch = !AppMode && !t.Internal ? Twitch.Channel(t.Url) : null;
            twitchBtn.Visibility = ch != null ? Visibility.Visible : Visibility.Collapsed;
            if (ch == null) return;
            var lit = S.TwitchAds && !S.TwitchAllow.Contains(ch) && t.TwNote != null && DateTime.Now - t.TwWhen < TimeSpan.FromMinutes(2);
            ((System.Windows.Shapes.Path)twitchBtn.Content).SetResourceReference(System.Windows.Shapes.Shape.FillProperty, lit ? "AccentBrush" : "Text");
            twitchBtn.ToolTip = TwitchStatus(t);
        }
        public void TwitchFlyout(Tab t)
        {
            if (t == null) return;
            var ch = Twitch.Channel(t.Url);
            var sp = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
            sp.Children.Add(Ui.T("Twitch ads", 14, true, null, 6));
            sp.Children.Add(Ui.Muted(TwitchStatus(t), 12, 4));
            if (t.TwSwaps + t.TwCovers > 0) sp.Children.Add(Ui.Muted("In this tab: " + t.TwSwaps + " skipped (swapped stream), " + t.TwCovers + " covered.", 11.5, 8));
            if (ch != null)
                sp.Children.Add(Ui.Switch("Support " + ch + ": allow ads", "Ads on this channel play and are counted, so " + ch + " is paid for them. The page loads again.", S.TwitchAllow.Contains(ch), on => { Panels.CloseFlyouts(); SupportChannel(ch, on, t); }));
            sp.Children.Add(Ui.Switch("Block Twitch ads", "Swaps an ad break for Twitch's own ad-free stream of the channel when there is one, otherwise covers it and mutes it. Only Twitch's servers are contacted.", S.TwitchAds, on => { Panels.CloseFlyouts(); S.TwitchAds = on; S.Save(); TwitchSettingsChanged(); }));
            var more = Ui.Btn("Twitch ad settings", () => { Panels.CloseFlyouts(); OpenSettingsAt("Twitch ads"); }, false, "gear"); more.Margin = new Thickness(0, 8, 0, 0); more.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(more);
            Panels.ShowFlyout(twitchBtn != null && twitchBtn.IsVisible ? (UIElement)twitchBtn : menuBtn, sp, 360);
        }

        // ================================================================ Settings > Gaming > Twitch ads
        public UIElement[] TwitchAdSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Switch("Block Twitch ads", "Ads Twitch stitches into the stream are swapped for Twitch's own ad-free stream of the same channel (as another player type) while the break lasts; when every alternative has the ad, it plays muted under an \"Ad break - back in 0:28\" cover. Display ads and ad beacons are blocked - also in private windows. Only Twitch's own servers are contacted, never a third-party proxy.", S.TwitchAds, on => { S.TwitchAds = on; S.Save(); TwitchSettingsChanged(); }));
            items.Add(Ui.Muted("Allow ads on these channels - their ads play and are counted, so the streamer is paid (also from the Twitch button on a channel: \"Support <channel>\").", 12, 6));
            var list = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            Action fill = null;
            fill = () =>
            {
                list.Children.Clear();
                if (S.TwitchAllow.Count == 0) list.Children.Add(Ui.Muted("No channels yet.", 11.5, 4));
                foreach (var ch in S.TwitchAllow.OrderBy(x => x).ToList())
                {
                    var name = ch;
                    var rm = Ui.Tool("xmark", "Stop allowing ads on " + name, () => { SupportChannel(name, false, null); fill(); }, 11); rm.Width = 24; rm.Height = 22;
                    var row = Ui.Row(Ui.Icon("twitch", 12, "Lilac"), new TextBlock { Text = name, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, rm);
                    row.Margin = new Thickness(0, 0, 0, 2); list.Children.Add(row);
                }
            };
            fill();
            items.Add(list);
            var box = new TextBox { Style = Ui.S("Field"), Width = 220, Height = 28, ToolTip = "A channel name or its twitch.tv address", Margin = new Thickness(0, 0, 8, 0) };
            Action add = () => { if (SupportChannel(box.Text, true, null)) { box.Text = ""; fill(); } else Toast("That isn't a Twitch channel name.", 3); };
            box.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) add(); };
            items.Add(Ui.Row(box, Ui.Btn("Allow ads", add, false, "plus")));
            items.Add(new Border { Height = 10 });
            items.Add(Ui.Switch("Get fixes for Twitch ad handling between browser updates", "Twitch changes how ads work often. A newer version of the browser's Twitch script can come from Hallow Browser's GitHub release (checked against GitHub's sha256) - used only on twitch.tv.", S.TwitchFixes, on => { S.TwitchFixes = on; S.Save(); }));
            items.Add(Ui.Row(Ui.Btn("Check now", () => { Toast("Checking...", 2); Task.Run(() => TwitchAdRules.CheckForUpdate()).ContinueWith(r => Dispatcher.BeginInvoke(new Action(() => { Toast(r.Result ?? "Twitch ad handling is up to date (version " + TwitchAdRules.CurrentVersion + ").", 5); if (r.Result != null && r.Result.StartsWith("Twitch ad handling updated")) TwitchSettingsChanged(); }))); }, false, "arrows-rotate")));
            items.Add(Ui.Muted("Script version " + TwitchAdRules.CurrentVersion + " (" + TwitchAdRules.Source + ").", 11, 8));
            items.Add(Ui.Muted("Honestly: Twitch changes its ads often, so this can stop working until a fix ships. A swapped stream can be a lower quality for the length of the break, and switching can stutter for a second. Ads pay streamers - allow them on channels you want to support. Twitch's terms discourage blocking its ads. Left alone on purpose: chat, emotes, channel points, and Twitch's own analytics (spade), which carries the watch-time events that earn channel points and drops.", 11.5, 0));
            return items.ToArray();
        }
    }
}
