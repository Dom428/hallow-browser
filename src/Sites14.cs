// Hallow Browser 1.4 - site permissions and site settings.
// The engine's permission requests (camera, microphone, location, notifications, clipboard, MIDI, sensors, window placement, local
// fonts, files, several downloads) get a Hallow prompt under the address bar: Allow (saved), Allow this time, Block (saved). Saved
// answers live in the profile (WebView2 keeps them and doesn't ask again). Pop-ups, sound and JavaScript per site are the browser's
// own rules (settings.json); cookies can be deleted per site. hallow://settings/sites lists every site with its own settings, the
// padlock in the address bar shows the current site's.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    [DataContract]
    public class SiteRule
    {
        [DataMember] public string Host;
        [DataMember] public string Popups;   // null = blocked unless you clicked | allow | block
        [DataMember] public string Sound;    // null = allowed | mute
        [DataMember] public string Script;   // null = allowed | block
        [DataMember] public bool Http;       // HTTPS-only: allowed over http
        [DataMember] public string Dark;     // 1.5: null = as the global "Make pages dark" | on | off
    }

    public static class SiteRules
    {
        public static string Key(string host) { return (host ?? "").Trim().TrimEnd('.').ToLowerInvariant(); }
        public static SiteRule Find(BrowserSettings s, string host) { var k = Key(host); return k.Length == 0 || s.Sites == null ? null : s.Sites.FirstOrDefault(x => x.Host == k); }
        public static SiteRule Get(BrowserSettings s, string host, bool create)
        {
            var r = Find(s, host); if (r != null || !create) return r;
            r = new SiteRule { Host = Key(host) }; s.Sites.Add(r); return r;
        }
        public static bool Empty(SiteRule r) { return r == null || (r.Popups == null && r.Sound == null && r.Script == null && !r.Http && r.Dark == null); }
        public static void Tidy(BrowserSettings s) { s.Sites.RemoveAll(Empty); }
        public static void Reset(BrowserSettings s, string host) { var k = Key(host); s.Sites.RemoveAll(x => x.Host == k); }

        // the kinds the browser asks you about (the rest keep the engine's own default)
        public static bool Prompted(CoreWebView2PermissionKind k)
        {
            switch (k)
            {
                case CoreWebView2PermissionKind.Camera: case CoreWebView2PermissionKind.Microphone: case CoreWebView2PermissionKind.Geolocation: case CoreWebView2PermissionKind.Notifications:
                case CoreWebView2PermissionKind.ClipboardRead: case CoreWebView2PermissionKind.MidiSystemExclusiveMessages: case CoreWebView2PermissionKind.OtherSensors:
                case CoreWebView2PermissionKind.WindowManagement: case CoreWebView2PermissionKind.LocalFonts: case CoreWebView2PermissionKind.FileReadWrite: case CoreWebView2PermissionKind.MultipleAutomaticDownloads:
                    return true;
            }
            return false;
        }
        public static string Name(CoreWebView2PermissionKind k)
        {
            switch (k)
            {
                case CoreWebView2PermissionKind.Camera: return "Camera";
                case CoreWebView2PermissionKind.Microphone: return "Microphone";
                case CoreWebView2PermissionKind.Geolocation: return "Location";
                case CoreWebView2PermissionKind.Notifications: return "Notifications";
                case CoreWebView2PermissionKind.ClipboardRead: return "Clipboard";
                case CoreWebView2PermissionKind.MidiSystemExclusiveMessages: return "MIDI devices";
                case CoreWebView2PermissionKind.OtherSensors: return "Motion sensors";
                case CoreWebView2PermissionKind.WindowManagement: return "Window placement";
                case CoreWebView2PermissionKind.LocalFonts: return "Fonts on this PC";
                case CoreWebView2PermissionKind.FileReadWrite: return "Files you open";
                case CoreWebView2PermissionKind.MultipleAutomaticDownloads: return "Several downloads";
                case CoreWebView2PermissionKind.Autoplay: return "Autoplay";
                case CoreWebView2PermissionKind.PersistentStorage: return "Persistent storage";
            }
            return "Other";
        }
        public static string Wants(CoreWebView2PermissionKind k)
        {
            switch (k)
            {
                case CoreWebView2PermissionKind.Camera: return "use your camera";
                case CoreWebView2PermissionKind.Microphone: return "use your microphone";
                case CoreWebView2PermissionKind.Geolocation: return "know your location";
                case CoreWebView2PermissionKind.Notifications: return "show notifications";
                case CoreWebView2PermissionKind.ClipboardRead: return "see what you copied (clipboard)";
                case CoreWebView2PermissionKind.MidiSystemExclusiveMessages: return "control your MIDI devices";
                case CoreWebView2PermissionKind.OtherSensors: return "use motion sensors";
                case CoreWebView2PermissionKind.WindowManagement: return "place windows on your screens";
                case CoreWebView2PermissionKind.LocalFonts: return "use the fonts on this PC";
                case CoreWebView2PermissionKind.FileReadWrite: return "edit files you open";
                case CoreWebView2PermissionKind.MultipleAutomaticDownloads: return "download several files";
            }
            return "use something on your PC";
        }
        public static string Icon(CoreWebView2PermissionKind k)
        {
            switch (k)
            {
                case CoreWebView2PermissionKind.Camera: return "video";
                case CoreWebView2PermissionKind.Microphone: return "microphone";
                case CoreWebView2PermissionKind.Geolocation: return "location-dot";
                case CoreWebView2PermissionKind.Notifications: return "bell";
                case CoreWebView2PermissionKind.ClipboardRead: return "clipboard";
                case CoreWebView2PermissionKind.MidiSystemExclusiveMessages: return "music";
                case CoreWebView2PermissionKind.OtherSensors: return "gauge-high";
                case CoreWebView2PermissionKind.WindowManagement: return "display";
                case CoreWebView2PermissionKind.LocalFonts: return "font";
                case CoreWebView2PermissionKind.FileReadWrite: return "folder-open";
                case CoreWebView2PermissionKind.MultipleAutomaticDownloads: return "download";
            }
            return "circle-info";
        }
        public static string OriginKey(string o) { return (o ?? "").TrimEnd('/').ToLowerInvariant(); }
        public static string HostOfOrigin(string o) { return Omni.HostOf(o) ?? o; }
    }

    public class PermAsk { public string Origin, Host; public CoreWebView2PermissionKind Kind; public Action<string> Done; }

    public partial class BrowserWindow
    {
        public int PopupsBlocked;
        readonly List<PermAsk> asks = new List<PermAsk>();
        Popup askPop;
        public PermAsk PendingAsk { get { return asks.FirstOrDefault(); } }

        void Hook14(CoreWebView2 c)
        {
            c.PermissionRequested += (a, e) => OnPermission(c, e);
            c.NavigationStarting += (a, e) => { try { HttpsStarting(c, e); } catch { } if (!e.Cancel) try { SiteOnStart(c, e); } catch { } };
            c.NavigationCompleted += (a, e) => { try { HttpsCompleted(c, e); } catch { } };
            if (BrowserSettings.IgnoreCerts) c.ServerCertificateErrorDetected += (a, e) => { e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow; };   // tests only (with the flag above)
        }
        // per site: JavaScript off, muted
        void SiteOnStart(CoreWebView2 c, CoreWebView2NavigationStartingEventArgs e)
        {
            var host = Omni.IsWeb(e.Uri) ? Omni.HostOf(e.Uri) : null; var r = host == null ? null : SiteRules.Find(S, host);
            bool noJs = r != null && r.Script == "block";
            if (c.Settings.IsScriptEnabled == noJs) c.Settings.IsScriptEnabled = !noJs;
            var t = TabOfCore(c); bool mute = r != null && r.Sound == "mute";
            if (mute && !c.IsMuted) { c.IsMuted = true; if (t != null) t.MutedBySite = true; }
            else if (!mute && t != null && t.MutedBySite) { t.MutedBySite = false; c.IsMuted = false; }
        }
        // pop-ups: allowed when you clicked (like other browsers), or as the site's setting says
        bool PopupAllowed(Tab x, CoreWebView2NewWindowRequestedEventArgs b)
        {
            var host = Omni.HostOf(x.Url); var r = host == null || !Omni.IsWeb(x.Url) ? null : SiteRules.Find(S, host);
            bool ok = r != null && r.Popups == "allow" ? true : r != null && r.Popups == "block" ? false : b.IsUserInitiated || SignIn.IsIdentity(b.Uri);   // 1.16.2: a sign-in pop-up opened a moment after the click
            if (!ok) { PopupsBlocked++; LastPopup = b.Uri; if (x == Active) Toast("Blocked a pop-up from " + host + ". The padlock in the address bar can allow them for this site.", 5); }
            return ok;
        }
        public string LastPopup;

        // ---- the prompt
        void OnPermission(CoreWebView2 c, CoreWebView2PermissionRequestedEventArgs e)
        {
            if (!SiteRules.Prompted(e.PermissionKind)) return;
            var w = Program.WindowOf(c) ?? this;
            var origin = Omni.Origin(e.Uri) ?? e.Uri; var d = e.GetDeferral();
            w.Ask(new PermAsk { Origin = origin, Host = Omni.HostOf(e.Uri) ?? origin, Kind = e.PermissionKind, Done = choice =>
            {
                try
                {
                    if (choice == "allow") { e.State = CoreWebView2PermissionState.Allow; e.SavesInProfile = true; }
                    else if (choice == "once") { e.State = CoreWebView2PermissionState.Allow; e.SavesInProfile = false; }
                    else if (choice == "block") { e.State = CoreWebView2PermissionState.Deny; e.SavesInProfile = true; }
                    else { e.State = CoreWebView2PermissionState.Deny; e.SavesInProfile = false; }
                }
                catch { }
                try { d.Complete(); } catch { }
            } });
        }
        void Ask(PermAsk a) { asks.Add(a); if (asks.Count == 1) ShowAsk(); }
        public void Answer(string choice)
        {
            var a = PendingAsk; if (a == null) return;
            asks.RemoveAt(0); if (askPop != null) askPop.IsOpen = false;
            a.Done(choice);
            if (choice == "allow" || choice == "block") Toast(a.Host + ": " + SiteRules.Name(a.Kind).ToLowerInvariant() + (choice == "allow" ? " allowed" : " blocked") + ". Change it with the padlock or in Site settings.", 4);
            if (asks.Count > 0) ShowAsk();
        }
        void ShowAsk()
        {
            var a = PendingAsk; if (a == null) return;
            var sp = new StackPanel { Margin = new Thickness(6) };
            var ic = Ui.Icon(SiteRules.Icon(a.Kind), 18, "AccentBrush"); ic.Margin = new Thickness(0, 2, 12, 0); ic.VerticalAlignment = VerticalAlignment.Top;
            var tx = new StackPanel();
            var head = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 300, FontSize = 13.5 }; head.Inlines.Add(new System.Windows.Documents.Run(a.Host) { FontWeight = FontWeights.SemiBold }); head.Inlines.Add(new System.Windows.Documents.Run(" wants to " + SiteRules.Wants(a.Kind)));
            tx.Children.Add(head);
            if (Private) tx.Children.Add(Ui.Muted("Private window: an answer here is forgotten when you close it.", 11, 0));
            sp.Children.Add(Ui.Row(ic, tx));
            var row = Ui.Row(Ui.Btn("Allow", () => Answer("allow"), true), Ui.Btn("Allow this time", () => Answer("once")), Ui.Btn("Block", () => Answer("block")));
            row.Margin = new Thickness(30, 12, 0, 2); foreach (FrameworkElement b in row.Children) b.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(row);
            var close = Ui.Tool("xmark", "Not now (blocked this time)", () => Answer("dismiss"), 10); close.Width = 24; close.Height = 22; close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top;
            var g = new Grid(); g.Children.Add(sp); g.Children.Add(close);
            var fl = Ui.Flyout(g, 400); fl.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            if (askPop != null) askPop.IsOpen = false;
            askPop = new Popup { Child = fl, PlacementTarget = addressBox, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = true, VerticalOffset = 6, HorizontalOffset = 4 };
            if (!BrowserSettings.Offscreen && !snap && IsVisible) askPop.IsOpen = true;   // tests answer through the pipe (never on screen)
            Deactivated += (s, e2) => { };
        }

        // ---- the padlock: this site's connection and settings
        Button lockBtn;
        UIElement LockButton(System.Windows.Shapes.Path icon)
        {
            icon.Margin = new Thickness(0);
            notSecureText = new TextBlock { Text = "Not secure", FontSize = 11.5, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };   // 1.18: http pages say so beside the padlock
            notSecureText.SetResourceReference(TextBlock.ForegroundProperty, "AmberText");
            lockBtn = new Button { Style = Ui.S("Tool"), Width = double.NaN, MinWidth = 28, Height = 24, Margin = new Thickness(5, 0, 3, 0), Content = new Border { Child = Ui.Row(icon, notSecureText), Margin = new Thickness(8, 0, 8, 0) }, ToolTip = "Site info and permissions", VerticalAlignment = VerticalAlignment.Center };
            lockBtn.Click += (a, b) => { var _ = SiteFlyout(lockBtn); };
            return lockBtn;
        }
        public CoreWebView2Profile NormalProfile
        {
            get
            {
                foreach (var w in Program.Windows.Where(x => x.Private == Private)) { var p = w.Profile; if (p != null) return p; }
                try { if (prewarm != null && prewarm.CoreWebView2 != null) return prewarm.CoreWebView2.Profile; } catch { }
                return null;
            }
        }
        public async Task<List<CoreWebView2PermissionSetting>> PermissionsFor(string origin)
        {
            var p = NormalProfile; if (p == null) return new List<CoreWebView2PermissionSetting>();
            var all = await p.GetNonDefaultPermissionSettingsAsync();
            var k = SiteRules.OriginKey(origin);
            return all.Where(x => origin == null || SiteRules.OriginKey(x.PermissionOrigin) == k).ToList();
        }
        public async Task SetPermission(string origin, CoreWebView2PermissionKind kind, CoreWebView2PermissionState state)
        {
            var p = NormalProfile; if (p == null) return;
            await p.SetPermissionStateAsync(kind, SiteRules.OriginKey(origin), state);
        }
        public async Task<int> CookieCount(string host)
        {
            var p = NormalProfile; if (p == null || string.IsNullOrEmpty(host)) return 0;
            try { var a = await p.CookieManager.GetCookiesAsync("https://" + host); var b = await p.CookieManager.GetCookiesAsync("http://" + host); return a.Select(x => x.Name + "|" + x.Domain + "|" + x.Path).Union(b.Select(x => x.Name + "|" + x.Domain + "|" + x.Path)).Count(); }
            catch { return 0; }
        }
        public async Task<int> DeleteCookies(string host)
        {
            var p = NormalProfile; if (p == null || string.IsNullOrEmpty(host)) return 0;
            int n = 0;
            try { foreach (var u in new[] { "https://" + host, "http://" + host }) foreach (var ck in await p.CookieManager.GetCookiesAsync(u)) { p.CookieManager.DeleteCookie(ck); n++; } } catch { }
            return n;
        }
        public async Task ResetSite(string host)
        {
            foreach (var ps in await PermissionsFor(null)) if (string.Equals(SiteRules.HostOfOrigin(ps.PermissionOrigin), host, StringComparison.OrdinalIgnoreCase)) try { await SetPermission(ps.PermissionOrigin, ps.PermissionKind, CoreWebView2PermissionState.Default); } catch { }
            SiteRules.Reset(S, host); S.Save(); HttpThisSession.Remove(host);
        }
        static readonly List<KeyValuePair<string, string>> permChoices = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("allow", "Allow"), new KeyValuePair<string, string>("block", "Block"), new KeyValuePair<string, string>("ask", "Ask") };
        // rows for one site (the padlock flyout and the site settings page)
        async Task<List<UIElement>> SiteRows(string host, string origin, Action changed, bool compact)
        {
            var rows = new List<UIElement>();
            Func<string, string, UIElement, UIElement> row = (icon, label, ctl) =>
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 120 : 170) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(Ui.Icon(icon, 12, "Lilac")); var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(l, 1); g.Children.Add(l);
                Grid.SetColumn(ctl, 2); g.Children.Add(ctl); return g;
            };
            List<CoreWebView2PermissionSetting> perms;
            try { perms = origin != null ? await PermissionsFor(origin) : (await PermissionsFor(null)).Where(x => string.Equals(SiteRules.HostOfOrigin(x.PermissionOrigin), host, StringComparison.OrdinalIgnoreCase)).ToList(); } catch { perms = new List<CoreWebView2PermissionSetting>(); }
            foreach (var ps in perms.OrderBy(x => SiteRules.Name(x.PermissionKind)))
            {
                var o = ps.PermissionOrigin; var k = ps.PermissionKind;
                var cur = ps.PermissionState == CoreWebView2PermissionState.Allow ? "allow" : ps.PermissionState == CoreWebView2PermissionState.Deny ? "block" : "ask";
                rows.Add(row(SiteRules.Icon(k), SiteRules.Name(k), Ui.Chips(permChoices, cur, v => { var _ = SetPermission(o, k, v == "allow" ? CoreWebView2PermissionState.Allow : v == "block" ? CoreWebView2PermissionState.Deny : CoreWebView2PermissionState.Default).ContinueWith(x => Dispatcher.BeginInvoke(changed)); })));
            }
            if (perms.Count == 0 && compact) { var m = Ui.Muted("No permissions given or blocked yet - the site asks when it needs one.", 11.5, 4); rows.Add(m); }
            var r = SiteRules.Find(S, host) ?? new SiteRule { Host = SiteRules.Key(host) };
            Action<Action<SiteRule>> edit = f => { var rr = SiteRules.Get(S, host, true); f(rr); SiteRules.Tidy(S); S.Save(); changed(); };
            rows.Add(row("up-right-from-square", "Pop-ups", Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", "When I click"), new KeyValuePair<string, string>("allow", "Allow"), new KeyValuePair<string, string>("block", "Block") }, r.Popups ?? "", v => edit(x => x.Popups = v == "" ? null : v))));
            rows.Add(row("volume-high", "Sound", Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", "Allow"), new KeyValuePair<string, string>("mute", "Mute") }, r.Sound ?? "", v => edit(x => x.Sound = v == "" ? null : v))));
            rows.Add(row("code", "JavaScript", Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", "Allow"), new KeyValuePair<string, string>("block", "Block") }, r.Script ?? "", v => edit(x => x.Script = v == "" ? null : v))));
            rows.Add(row("moon", "Dark pages", Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", S.ForceDark ? "Default (dark)" : "Default (as drawn)"), new KeyValuePair<string, string>("on", "Make dark"), new KeyValuePair<string, string>("off", "Never dark") }, r.Dark ?? "", v => { edit(x => x.Dark = v == "" ? null : v); DarkChanged(); })));   // 1.5
            if (r.Http || HttpThisSession.Contains(SiteRules.Key(host)))
                rows.Add(row("lock-open", "Insecure http", Ui.Btn(r.Http ? "Allowed - stop allowing" : "Allowed until restart - stop", () => { HttpThisSession.Remove(SiteRules.Key(host)); edit(x => x.Http = false); })));
            var cookieText = Ui.Muted("...", 12); cookieText.VerticalAlignment = VerticalAlignment.Center; cookieText.Margin = new Thickness(0, 0, 10, 0);
            var del = Ui.Btn("Delete", () => { var _ = DeleteCookies(host).ContinueWith(x => Dispatcher.BeginInvoke(new Action(() => { Toast("Deleted " + x.Result + " cookies of " + host + ".", 3); changed(); }))); }, false, "trash-can");
            rows.Add(row("cookie-bite", "Cookies", Ui.Row(cookieText, del)));
            var __ = CookieCount(host).ContinueWith(x => Dispatcher.BeginInvoke(new Action(() => cookieText.Text = x.Result == 1 ? "1 cookie" : x.Result + " cookies")));
            if (snap) cookieText.Text = "3 cookies";   // 1.18: --snap draws before the count comes back (made-up)
            return rows;
        }
        async Task SiteFlyout(UIElement at)
        {
            var t = Active; if (t == null) return;
            Popup pop = null;
            var sp = SiteInfoPanel(t, () => { if (pop != null) pop.IsOpen = false; });
            pop = Panels.ShowFlyout(at, sp, Omni.IsWeb(t.Url) ? 430 : 300); pop.HorizontalOffset = 0;
            LastSiteInfo = sp;
            await Task.Yield();
        }
        public static FrameworkElement LastSiteInfo;   // tests: what the padlock's flyout says
        // 1.18: the padlock's flyout as a panel (--snap draws it too): the connection, Shield on this page, the site's permissions and
        // rules, its cookies and site data, Site settings
        public StackPanel SiteInfoPanel(Tab t, Action close)
        {
            var sp = new StackPanel { Margin = new Thickness(6, 4, 6, 6) };
            if (!Omni.IsWeb(t.Url))
            {
                sp.Children.Add(Ui.T(t.Internal ? "A page of Hallow Browser" : Private && Omni.IsNewTab(t.Url) ? "A private window's new tab" : Omni.IsNewTab(t.Url) ? "Your new tab page" : "No site", 13.5, true, "Text", 4));
                sp.Children.Add(Ui.Muted("It's drawn by the browser itself, on this PC.", 11.5, 2));
                return sp;
            }
            var host = Omni.HostOf(t.Url); var https = t.Url.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && t.HttpsBlocked == null;
            sp.Children.Add(Ui.T(host, 14.5, true, "Text", 2));
            var conn = Ui.Row(Ui.Icon(https ? "lock" : "lock-open", 11, https ? "Green" : "Amber"), Ui.T("  " + (https ? "Connection is secure (https)" : "Not secure: this page isn't sent over https"), 12, false, https ? "GreenText" : "AmberText"));
            conn.Margin = new Thickness(0, 0, 0, 4); sp.Children.Add(conn);
            var n = BadgeNumber(t);
            var sh = Ui.Row(Ui.Icon("shield-halved", 11, "AccentBrush"), Ui.T("  " + (ShieldState == "extension" || ShieldState == "fallback" ? (n > 0 ? "Hallow Shield blocked " + n + (n == 1 ? " request" : " requests") + " on this page" : "Hallow Shield is on - nothing blocked on this page") : "Hallow Shield is starting"), 12, false, "Text"));
            sh.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(sh);
            var permHead = Ui.Label("Permissions for this site"); permHead.Margin = new Thickness(0, 0, 0, 4); sp.Children.Add(permHead);
            var body = new StackPanel(); sp.Children.Add(body);
            Action fill = null;
            fill = async () => { var rows = await SiteRows(host, Omni.Origin(t.Url), () => fill(), true); body.Children.Clear(); foreach (var r in rows) body.Children.Add(r); };
            fill();
            var links = Ui.Row(Ui.Btn("Site settings", () => { if (close != null) close(); NewTab(Omni.SitesUrl, true); }, false, "sliders"), Ui.Btn("Cookies and site data", () => { if (close != null) close(); Panels.ClearData(this); }, false, "cookie-bite"), Ui.Btn("Reset", () => { var _ = ResetSite(host).ContinueWith(x => Dispatcher.BeginInvoke(fill)); }, false, "rotate-left", "Forget this site's permissions and rules"));
            links.Margin = new Thickness(0, 10, 0, 8); foreach (FrameworkElement b in links.Children) b.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(links);
            sp.Children.Add(Ui.Muted("Pop-ups, sound and JavaScript take effect on the next page load; dark pages at once.", 11, 0));
            return sp;
        }

        // ---- hallow://settings/sites
        StackPanel sitesList;
        FrameworkElement SitesPage()
        {
            var col = new StackPanel { MaxWidth = 820, Margin = new Thickness(28, 26, 28, 40), HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(Ui.Row(Ui.Icon("sliders", 18, "AccentBrush"), Ui.T("  SITE SETTINGS", 20, true, "Text")));
            col.Children.Add(Ui.Muted("What each site may do. Sites ask when they want your camera, microphone, location and so on (Allow, Allow this time or Block) - saved answers show up here, with the pop-ups, sound and JavaScript you set for a site and its cookies.", 12, 14));
            sitesList = new StackPanel(); col.Children.Add(sitesList);
            col.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Label("What WebView2 allows"), Ui.Muted("Permissions are kept in the profile by Microsoft's engine; pop-ups, sound and JavaScript per site are the browser's own rules (JavaScript off means the site's pages run no scripts). Cookies can be deleted per site, but blocking cookies for a single site isn't something WebView2 offers - third-party cookies are blocked for every site (Settings > Privacy).", 11.5, 0) } }));
            var _ = RefreshSites();
            var sv = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            sv.SetResourceReference(Control.BackgroundProperty, "Bg");
            return sv;
        }
        public async Task<List<string>> SiteHosts()
        {
            var hosts = new List<string>();
            try { foreach (var p in await PermissionsFor(null)) { var h = SiteRules.HostOfOrigin(p.PermissionOrigin); if (h != null) hosts.Add(SiteRules.Key(h)); } } catch { }
            hosts.AddRange(S.Sites.Select(x => x.Host)); hosts.AddRange(HttpThisSession.Select(SiteRules.Key));
            return hosts.Where(h => h.Length > 0).Distinct().OrderBy(h => h.StartsWith("www.") ? h.Substring(4) : h, StringComparer.Ordinal).ToList();
        }
        public async Task RefreshSites()
        {
            if (sitesList == null) return;
            var hosts = await SiteHosts();
            sitesList.Children.Clear();
            if (hosts.Count == 0) { sitesList.Children.Add(Ui.Card(Ui.Muted("No site has settings of its own yet.", 12, 0))); return; }
            foreach (var h in hosts)
            {
                var host = h; var card = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var reset = Ui.Btn("Reset", () => { var _ = ResetSite(host).ContinueWith(x => Dispatcher.BeginInvoke(new Action(() => { var __ = RefreshSites(); }))); }, false, "rotate-left"); DockPanel.SetDock(reset, Dock.Right);
                head.Children.Add(reset); head.Children.Add(Ui.T(host, 14, true, "Text"));
                card.Children.Add(head);
                foreach (var r in await SiteRows(host, null, () => { var _ = RefreshSites(); }, false)) card.Children.Add(r);
                var c = Ui.Card(card); c.Margin = new Thickness(0, 0, 0, 10); sitesList.Children.Add(c);
            }
        }
    }
}
