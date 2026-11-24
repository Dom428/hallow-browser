// Hallow Browser 1.16.2 - "Sign in with Google / Microsoft / Apple / GitHub" is left alone.
// Real use: signing in to a site with Google (Google's sign-in pop-up) ended on Google's "400. That's an error ... malformed" page.
// A sign-in pop-up only works when the requests Google gets are exactly what the site and Google's own pages sent, and the tab that
// opened the pop-up can still talk to it. What Hallow did on the way, and what 1.16.2 does instead (sign-in pages = the identity
// providers' pages below, and the pages they send you back to):
//  - Shield's privacy list strips "tracking" parameters (gclid, fbclid, _gl, s_cid, ...) from every page address, also on sign-in
//    pages and on the address a sign-in sends its answer to, and blocks some of Google's sign-in helpers: now Shield never touches
//    sign-in pages (no blocking, no parameter stripping, no hiding, no scriptlets) - "allow everything" rules above every list
//    rule, also for the pages a sign-in page sends you to, added to the copy of Shield the browser loads;
//  - Do Not Track: Hallow rewrote the headers of every page request itself (WebResourceRequested) - never for sign-in requests now
//    (the engine's own Do Not Track header still goes out; Hallow just doesn't touch the request);
//  - while a game runs, every background tab is put to sleep at once - also the tab that opened the sign-in pop-up, which then
//    can't receive the pop-up's answer: the tab that opened a sign-in pop-up stays awake (and is woken) while the pop-up is open;
//  - a sign-in pop-up opened a moment after the click (sites that ask their server first) counted as an unwanted pop-up: pop-ups
//    to sign-in pages are allowed unless you blocked pop-ups for that site. They keep window.opener (a tab next to the site).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;

namespace HallowBrowser
{
    public static class SignIn
    {
        // the identity providers' sign-in hosts (exact host names)
        public static readonly string[] Hosts = {
            "accounts.google.com", "oauth2.googleapis.com", "accounts.youtube.com",
            "login.microsoftonline.com", "login.live.com", "login.microsoft.com", "account.live.com",
            "appleid.apple.com", "idmsa.apple.com" };

        static Uri Parse(string url) { Uri u; return !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == "https" || u.Scheme == "http") ? u : null; }
        public static bool IsIdentityHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            return Hosts.Contains(host.ToLowerInvariant().TrimEnd('.'));
        }
        // a page or request address on an identity provider's sign-in (GitHub: only its sign-in paths)
        public static bool IsIdentity(string url)
        {
            var u = Parse(url); if (u == null) return false;
            if (IsIdentityHost(u.Host)) return true;
            var h = u.Host.ToLowerInvariant();
            if (h != "github.com" && h != "www.github.com") return false;
            var p = u.AbsolutePath;
            return p == "/login" || p.StartsWith("/login/", StringComparison.Ordinal) || p == "/session" || p.StartsWith("/sessions/", StringComparison.Ordinal);
        }
        // a page request that belongs to a sign-in: to an identity provider, or sent from one (the site's return address: the
        // Referer or Origin is the provider's)
        public static bool IsSignInRequest(string url, string referer, string origin)
        {
            if (IsIdentity(url) || IsIdentity(referer)) return true;
            var o = Parse(origin); return o != null && IsIdentityHost(o.Host);
        }

        // ---- Shield (the copy the browser loads): sign-in pages get "allow everything" rules above every list rule (a static
        // ruleset of the copy's own, "hallow-signin": on from the moment the copy is loaded - measured: WebView2 doesn't always start
        // an extension's background when it's added, so rules put in by the background could be missing for a whole session) and
        // no content scripts. Shield's background is patched to keep that ruleset on and to leave sign-in pages out of its content
        // scripts; if Shield's code changed and an anchor isn't found, the background is left unpatched (the ruleset still loads).
        public const string RulesetId = "hallow-signin", RulesetPath = "rules/hallow-signin.json";
        public static string RulesJson()
        {
            var domains = "[" + string.Join(",", Hosts.Select(h => "\"" + h + "\"")) + "]";
            const string frames = "[\"main_frame\",\"sub_frame\"]";
            const string all = "[\"main_frame\",\"sub_frame\",\"stylesheet\",\"script\",\"image\",\"font\",\"object\",\"xmlhttprequest\",\"ping\",\"media\",\"websocket\",\"other\"]";
            return "[" +
                "{\"id\":1,\"priority\":2000000,\"action\":{\"type\":\"allowAllRequests\"},\"condition\":{\"requestDomains\":" + domains + ",\"resourceTypes\":" + frames + "}}," +
                "{\"id\":2,\"priority\":2000000,\"action\":{\"type\":\"allow\"},\"condition\":{\"requestDomains\":" + domains + ",\"resourceTypes\":" + all + "}}," +
                "{\"id\":3,\"priority\":2000000,\"action\":{\"type\":\"allowAllRequests\"},\"condition\":{\"regexFilter\":\"^https://(www\\\\.)?github\\\\.com/(login|session)\",\"resourceTypes\":" + frames + "}}," +
                // 1.16.2: Google's own preference address (Dark theme On / Off / Device default) is left as Google sent it
                "{\"id\":5,\"priority\":2000000,\"action\":{\"type\":\"allow\"},\"condition\":{\"regexFilter\":\"^https://(www\\\\.)?google\\\\.[a-z.]+(:[0-9]+)?/setprefs\",\"resourceTypes\":[\"main_frame\",\"sub_frame\",\"xmlhttprequest\"]}}," +
                // the site's return address: a page a sign-in page sends you to (form_post, redirect) keeps every parameter
                "{\"id\":4,\"priority\":2000000,\"action\":{\"type\":\"allow\"},\"condition\":{\"initiatorDomains\":" + domains + ",\"resourceTypes\":" + frames + "}}" +
                "]";
        }
        // the manifest's rule_resources with the sign-in ruleset added (on)
        public static void AddRuleset(Dictionary<string, object> manifest)
        {
            object d; var dnr = manifest.TryGetValue("declarative_net_request", out d) ? d as Dictionary<string, object> : null;
            if (dnr == null) manifest["declarative_net_request"] = dnr = new Dictionary<string, object>();
            object r; var list = dnr.TryGetValue("rule_resources", out r) && r is System.Collections.IEnumerable ? ((System.Collections.IEnumerable)r).Cast<object>().ToList() : new List<object>();
            list.RemoveAll(x => x is Dictionary<string, object> && (((Dictionary<string, object>)x).ContainsKey("id") ? ((Dictionary<string, object>)x)["id"] as string : null) == RulesetId);
            list.Insert(0, new Dictionary<string, object> { { "id", RulesetId }, { "enabled", true }, { "path", RulesetPath } });
            dnr["rule_resources"] = list;
        }
        public static string ExcludePatterns()
        {
            return "[" + string.Join(",", Hosts.Select(h => "\"*://" + h + "/*\"").Concat(new[] { "\"*://github.com/login*\"", "\"*://github.com/session*\"" })) + "]";
        }
        const string WantAnchor = "const want = wantedRulesets(s, LIST_IDS, groupsOf(m));";
        const string ScriptsAnchor = "const exclude = [...s.paused.flatMap(d => [`*://${d}/*`, `*://*.${d}/*`]), ...sitePats(s.blockOnly)];";
        public const string Marker = "/* hallow-browser 1.16.2: sign-in pages */";
        public static string PatchBackground(string js)
        {
            if (js == null) return null;
            js = js.Replace("\r\n", "\n");
            if (js.Contains(Marker)) return js;
            if (!js.Contains(WantAnchor) || !js.Contains(ScriptsAnchor)) return null;
            // Shield switches off every ruleset it doesn't know: the sign-in ruleset is one it wants
            js = js.Replace(WantAnchor, "const want = wantedRulesets(s, LIST_IDS, groupsOf(m)).concat(['" + RulesetId + "']);");
            js = js.Replace(ScriptsAnchor, ScriptsAnchor + " exclude.push(...HALLOW_SIGNIN_EXCLUDE);");
            // after the imports (a module: imports come first)
            var lines = js.Split('\n').ToList();
            int last = -1; for (int i = 0; i < lines.Count; i++) if (lines[i].StartsWith("import ")) last = i;
            lines.Insert(last + 1, Marker + "\nconst HALLOW_SIGNIN_EXCLUDE = " + ExcludePatterns() + ";");
            return string.Join("\n", lines);
        }
    }

    // 1.16.2 (real use: Google's "Dark theme: On / Off / Device default" prompt seemed to do nothing): with "Make pages dark" on,
    // Chromium's automatic dark mode darkens Google's light theme as well, so picking Light (or Dark) on Google changed nothing you
    // could see. Sites with a light / dark choice of their own draw themselves (their own choice, kept in their own cookie, or
    // "Device default" = the page colours from Settings via prefers-color-scheme). A site set to "Make dark" in Site settings is
    // still made dark.
    public static class SiteThemes
    {
        public static bool OwnTheme(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            host = host.ToLowerInvariant().TrimEnd('.');
            // Google: google.com, google.<country> (google.co.uk, google.de ...) and their subdomains; and the sign-in pages
            var labels = host.Split('.');
            int g = Array.LastIndexOf(labels, "google");
            if (g >= 0 && labels.Length - g >= 2 && labels.Length - g <= 3 && labels.Skip(g + 1).All(l => l.Length >= 2 && l.Length <= 3)) return true;
            // 1.16.3: Twitch has its own light / dark theme - and Chromium's automatic dark mode painted channel extensions over the
            // video opaque white (the white Twitch player)
            if (host == "twitch.tv" || host.EndsWith(".twitch.tv")) return true;
            return SignIn.IsIdentityHost(host);
        }
    }

    public partial class Tab
    {
        public Tab SignInPopup;   // 1.16.2: the sign-in pop-up this tab opened (while it's open, this tab never sleeps)
        public bool SignInBusy { get { return SignInPopup != null && SignInPopup.View != null; } }
    }

    public partial class BrowserWindow
    {
        public int SignInRequestsLeftAlone, PageClosesKept, ForeignClosesRefused;   // tests
        public static readonly List<string> CloseTrace = new List<string>();   // tests: how a window was asked to close
        static void CT(string s) { if (BrowserSettings.TestDir == null) return; lock (CloseTrace) { CloseTrace.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); if (CloseTrace.Count > 40) CloseTrace.RemoveAt(0); } }

        // Real use: an extension (Claude in Chrome) closed its sign-in tab and the whole browser closed - WebView2 answers an
        // extension's chrome.tabs.remove / chrome.windows.remove by closing the host window (WM_CLOSE), not the tab. Only these close
        // a window now: your own closes (the X, Ctrl+Shift+W, Exit, Alt+F4, the taskbar's Close window = SC_CLOSE), the browser's own
        // code, Windows signing out or shutting down. Anything else is refused and the browser stays open.
        bool closeAsked;
        void HookForeignClose()
        {
            try { var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); if (src != null) src.AddHook(CloseHook); } catch { }
        }
        IntPtr CloseHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0112 && ((long)wParam & 0xFFF0) == 0xF060) { closeAsked = true; CT("SC_CLOSE"); }   // WM_SYSCOMMAND SC_CLOSE: Alt+F4, the system menu, the taskbar
            else if (msg == 0x0011 || msg == 0x0016) closeAsked = true;   // WM_QUERYENDSESSION / WM_ENDSESSION: Windows signs out or shuts down
            else if (msg == 0x0010) CT("WM_CLOSE asked=" + closeAsked);
            return IntPtr.Zero;
        }
        // true = this close is refused (OnClosing stops there)
        bool ForeignClose(System.ComponentModel.CancelEventArgs e)
        {
            bool ok = closeAsked || Program.Quitting || snap || Gone || (Application.Current != null && Application.Current.Dispatcher.HasShutdownStarted);
            CT("Closing ok=" + ok);
            if (ok) return false;
            e.Cancel = true; ForeignClosesRefused++;
            // measured: after an extension's chrome.windows.remove WebView2 also shuts the page that asked (its view stays, empty) -
            // such tabs are closed like a page closing itself (the window's last one becomes a new tab page)
            foreach (var ms in new[] { 300, 1500, 4000 })
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                timer.Tick += (a, b) => { timer.Stop(); foreach (var t in Tabs.Where(x => x.View != null && x.Core == null && x.Ready).ToList()) { CT("dead view closed"); PageWantsClose(t); } };
                timer.Start();
            }
            Toast("Something tried to close Hallow Browser (an extension?). It stays open - close it with X when you want.", 6);
            return true;
        }
        // a pop-up to a sign-in page: the tab that opened it stays awake while it's open (woken now if it was asleep)
        void SignInPopupOpened(Tab opener, Tab popup, string uri)
        {
            if (opener == null || popup == null || !SignIn.IsIdentity(uri)) return;
            opener.SignInPopup = popup;
            if (opener.Suspended && opener.Core != null) { try { opener.Core.Resume(); } catch { } opener.Suspended = false; SetTabIcon(opener); }
        }
        // a page or an extension closing a tab (window.close, chrome.tabs.remove / chrome.windows.remove): the window's last tab
        // becomes a new tab page - only you close the browser (the window's X, Ctrl+Shift+W, Exit)
        void PageWantsClose(Tab x)
        {
            if (x == null || !Tabs.Contains(x)) return;
            if (Tabs.Count == 1) { PageClosesKept++; NewTab(Private ? Omni.PrivateTabUrl : Omni.NewTabUrl, true); }
            CloseTab(x);
        }
    }
}
