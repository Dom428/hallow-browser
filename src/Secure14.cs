// Hallow Browser 1.4 - HTTPS-only mode and secure DNS.
// HTTPS-only (on by default): an http address (port 80) is tried as https first; when the https version doesn't answer, has a bad
// certificate or sends you back to http, a Hallow page asks before going on over http ("Continue" for this visit, or "Always for
// this site", which Site settings lists). Local names (localhost, single-label names, .local / .lan / .home.arpa) and IP addresses
// (routers, NAS boxes, LAN game servers) are left alone, like other browsers do.
// Secure DNS: DNS over HTTPS through Cloudflare, Quad9 or Google, written into the engine's "Local State" before it starts - so a
// change needs a browser restart (Settings says so).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class HttpsRules
    {
        static readonly string[] localEnds = { ".localhost", ".local", ".lan", ".home", ".internal", ".home.arpa", ".corp", ".intranet" };
        public static bool IsLocalHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            var h = host.Trim('[', ']').ToLowerInvariant();
            IPAddress ip; if (IPAddress.TryParse(h, out ip)) return true;
            if (h == "localhost" || !h.Contains(".")) return true;
            return localEnds.Any(e => h.EndsWith(e, StringComparison.Ordinal));
        }
        // should this address be tried as https first? (only port 80 - or a test port pair)
        public static bool ShouldUpgrade(string url, Func<string, bool> httpAllowed, string portMap = null)
        {
            Uri u; if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "http") return false;
            if (IsLocalHost(u.Host)) return false;
            if (!u.IsDefaultPort && MapPort(u.Port, portMap) < 0) return false;
            if (httpAllowed != null && httpAllowed(u.Host.ToLowerInvariant())) return false;
            return true;
        }
        public static int MapPort(int port, string map)
        {
            if (string.IsNullOrEmpty(map)) return -1;
            foreach (var p in map.Split(','))
            {
                var kv = p.Split('='); int a, b;
                if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out a) && int.TryParse(kv[1].Trim(), out b) && a == port) return b;
            }
            return -1;
        }
        public static string Upgrade(string url, string portMap = null)
        {
            var b = new UriBuilder(url) { Scheme = "https" };
            var m = MapPort(b.Port, portMap);
            b.Port = m > 0 ? m : -1;
            return b.Uri.AbsoluteUri;
        }
        // errors where the https version "doesn't work" (a refused connection comes as Unknown) (the page asks before http) - not "you went elsewhere" or "no such host"
        public static bool IsHttpsFailure(CoreWebView2WebErrorStatus s)
        {
            switch (s)
            {
                case CoreWebView2WebErrorStatus.OperationCanceled: case CoreWebView2WebErrorStatus.HostNameNotResolved: case CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired: case CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired: return false;
            }
            return true;
        }
        public static string Reason(CoreWebView2WebErrorStatus s)
        {
            switch (s)
            {
                case CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect: case CoreWebView2WebErrorStatus.CertificateExpired: case CoreWebView2WebErrorStatus.ClientCertificateContainsErrors:
                case CoreWebView2WebErrorStatus.CertificateRevoked: case CoreWebView2WebErrorStatus.CertificateIsInvalid: return "its secure version has a certificate problem";
                case CoreWebView2WebErrorStatus.Timeout: return "its secure version didn't answer in time";
                case CoreWebView2WebErrorStatus.RedirectFailed: return "its secure version sent you back to the insecure one";
            }
            return "it doesn't answer on a secure connection";
        }
        // the page that asks (drawn locally with NavigateToString; its two buttons post a message to the browser)
        public static string Interstitial(string host, string httpUrl, string reason, bool light)
        {
            var js = new JavaScriptSerializer();
            string bg = light ? "#F6F4FA" : "#14101B", card = light ? "#FFFFFF" : "#1C1626", fg = light ? "#1E1A26" : "#ECE8F4", muted = light ? "#5E5670" : "#B0A8C2", line = light ? "#DAD3E6" : "#3A3248", amber = light ? "#8A5200" : "#F5B544";
            Func<string, string> h = s => WebUtility.HtmlEncode(s ?? "");
            return "<!DOCTYPE html><html lang=en><head><meta charset=utf-8><meta name=color-scheme content=" + (light ? "light" : "dark") + "><title>Not secure: " + h(host) + "</title><style>" +
                "html,body{margin:0;height:100%;background:" + bg + ";color:" + fg + ";font:15px 'Segoe UI',system-ui,sans-serif}body{display:flex;align-items:center;justify-content:center}" +
                ".c{max-width:560px;margin:24px;padding:28px 32px;background:" + card + ";border:1px solid " + line + ";border-radius:14px}h1{font-size:21px;font-weight:600;margin:10px 0 10px}" +
                ".i{font:600 11px 'Cascadia Mono',Consolas,monospace;letter-spacing:.14em;color:" + amber + "}p{color:" + muted + ";line-height:1.5;margin:0 0 12px}code{color:" + fg + ";word-break:break-all}" +
                "button{font:600 13px 'Segoe UI',sans-serif;padding:9px 16px;border-radius:9px;border:1px solid " + line + ";background:transparent;color:" + fg + ";cursor:pointer;margin:8px 8px 0 0}" +
                "button.p{background:#7C3AED;border-color:#7C3AED;color:#fff}button:hover{filter:brightness(1.12)}</style></head><body><div class=c>" +
                "<div class=i>HTTPS-ONLY MODE</div><h1>This site doesn't support a secure connection</h1>" +
                "<p><b>" + h(host) + "</b>: " + h(reason) + ". If you continue, what you send and see there can be read or changed on the way (on public Wi-Fi, for example).</p>" +
                "<p><code>" + h(httpUrl) + "</code></p>" +
                "<button class=p id=back>Go back</button><button id=go>Continue to the site (not secure)</button><button id=always>Always allow this site</button>" +
                "<script>function m(x){try{chrome.webview.postMessage(x)}catch(e){}}document.getElementById('back').onclick=function(){m('hallow:https-back')};" +
                "document.getElementById('go').onclick=function(){m('hallow:https-continue')};document.getElementById('always').onclick=function(){m('hallow:https-always')};</script></div></body></html>";
        }
    }

    public static partial class Engine
    {
        // 1.4: a private window's new tab page - dark purple, a mask, what a private window keeps (nothing but downloads)
        public static string PrivateTabHtml(string engine)
        {
            var name = Omni.EngineName(engine); var url = Omni.SearchUrl(engine, "");
            const string mask = "M0 0L0 0M640 512L640 512M64 64C28.7 64 0 92.7 0 128L0 256c0 106 86 192 192 192l17.3 0c47.4 0 88.3-30.9 102.3-75.6L320 343.4l8.4 29c14 44.7 54.9 75.6 102.3 75.6l17.3 0c106 0 192-86 192-192l0-128c0-35.3-28.7-64-64-64L64 64zm96 96a64 64 0 1 1 0 128 64 64 0 1 1 0-128zm256 64a64 64 0 1 1 128 0 64 64 0 1 1-128 0z";
            return "<!DOCTYPE html><html lang=en><head><meta charset=utf-8><meta name=color-scheme content=dark><title>New private tab</title><style>" +
                "html,body{margin:0;height:100%;background:#1B1029;color:#F2EBFC;font:15px 'Segoe UI',system-ui,sans-serif}" +
                "body{display:flex;flex-direction:column;align-items:center;justify-content:center;background:radial-gradient(ellipse at 50% 30%,#3A1F5C 0,#1B1029 62%)}" +
                "svg{width:64px;height:52px;fill:#CDAEFF;margin-bottom:14px}h1{font-weight:300;letter-spacing:.28em;font-size:28px;margin:0 0 8px}h1 b{font-weight:700;color:#CDAEFF}" +
                "p{color:#B9A9D2;margin:0 0 8px;max-width:560px;text-align:center;line-height:1.5}form{width:min(620px,86vw);margin-top:20px}" +
                "input{width:100%;box-sizing:border-box;padding:14px 18px;border-radius:10px;border:1px solid #4C3270;background:#130A1F;color:#F2EBFC;font-size:16px;outline:none}input:focus{border-color:#CDAEFF}" +
                "</style></head><body><svg viewBox=\"0 0 640 512\"><path d=\"" + mask + "\"/></svg><h1><b>PRIVATE</b> WINDOW</h1>" +
                "<p>Nothing from this window is kept: no history, no session, no cookies or site data after you close it, no bookmark or sync changes. Downloads stay in your Downloads folder.</p>" +
                "<p>Sites here don't see your sign-ins. Your network, your work or school and the sites themselves can still see what you visit.</p>" +
                "<form id=f><input id=q autocomplete=off placeholder=\"Search " + name + " or type an address\" autofocus></form>" +
                "<script>var u=" + new JavaScriptSerializer().Serialize(url) + ";document.getElementById('f').onsubmit=function(e){e.preventDefault();var v=document.getElementById('q').value.trim();if(v)location.href=u+encodeURIComponent(v);};</script></body></html>";
        }
    }

    public partial class BrowserWindow
    {
        // the https try going on in a view: the http address it came from, the https one, its navigation id
        class Upgrade { public string Http, Https, Host; public ulong NavId; public bool Started; }
        static readonly Dictionary<CoreWebView2, Upgrade> upgrades = new Dictionary<CoreWebView2, Upgrade>();
        public static readonly HashSet<string> HttpThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // "Continue" (until the browser closes)
        public bool HttpAllowed(string host) { return HttpThisSession.Contains(host) || S.Sites.Any(x => x.Http && string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase)); }
        public int HttpsUpgrades, HttpsAsked;   // for status / tests

        Tab TabOfCore(CoreWebView2 c) { var w = Program.WindowOf(c); return w == null ? null : w.Tabs.FirstOrDefault(t => t.View != null && t.Core == c); }

        public static readonly List<string> HttpsTrace = new List<string>();   // what the last navigations did (shown to tests only)
        static void Trace(string s) { lock (HttpsTrace) { HttpsTrace.Add(s); if (HttpsTrace.Count > 40) HttpsTrace.RemoveAt(0); } }
        void HttpsStarting(CoreWebView2 c, CoreWebView2NavigationStartingEventArgs e)
        {
            if (BrowserSettings.TestDir != null) Trace("start " + e.NavigationId + " " + (e.Uri.Length > 80 ? e.Uri.Substring(0, 80) : e.Uri) + (e.IsRedirected ? " redirected" : ""));
            var t = TabOfCore(c);
            if (t != null && t.HttpsBlocked != null && !(e.Uri.StartsWith("data:") || e.Uri.StartsWith("about:"))) t.HttpsBlocked = null;   // our page itself keeps it
            Upgrade up; upgrades.TryGetValue(c, out up);
            if (up != null)
            {
                if (e.Uri == up.Https && !up.Started) { up.NavId = e.NavigationId; up.Started = true; return; }
                // the https version sent us straight back to http: that's a failure too
                if (e.IsRedirected && e.NavigationId == up.NavId && e.Uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase) && string.Equals(Omni.HostOf(e.Uri), up.Host, StringComparison.OrdinalIgnoreCase))
                { e.Cancel = true; upgrades.Remove(c); var u2 = up; Dispatcher.BeginInvoke(new Action(() => ShowHttpsPage(c, u2, CoreWebView2WebErrorStatus.RedirectFailed))); return; }
                if (e.NavigationId != up.NavId) upgrades.Remove(c);
            }
            if (!S.HttpsOnly) return;
            if (!HttpsRules.ShouldUpgrade(e.Uri, HttpAllowed, BrowserSettings.HttpsPortsTest)) return;
            var nu = new Upgrade { Http = e.Uri, Https = HttpsRules.Upgrade(e.Uri, BrowserSettings.HttpsPortsTest), Host = (Omni.HostOf(e.Uri) ?? "").ToLowerInvariant() };
            e.Cancel = true; upgrades[c] = nu; HttpsUpgrades++;
            Dispatcher.BeginInvoke(new Action(() => { try { c.Navigate(nu.Https); } catch { upgrades.Remove(c); } }));
        }
        void HttpsCompleted(CoreWebView2 c, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (BrowserSettings.TestDir != null) Trace("done " + e.NavigationId + " " + e.IsSuccess + " " + e.WebErrorStatus);
            Upgrade up; if (!upgrades.TryGetValue(c, out up) || !up.Started || e.NavigationId != up.NavId) return;
            upgrades.Remove(c);
            if (e.IsSuccess) return;
            if (e.WebErrorStatus == CoreWebView2WebErrorStatus.HostNameNotResolved) { HttpThisSession.Add(up.Host); try { c.Navigate(up.Http); } catch { } return; }   // no such host: http fails the same way, show its own error
            if (!HttpsRules.IsHttpsFailure(e.WebErrorStatus)) return;
            ShowHttpsPage(c, up, e.WebErrorStatus);
        }
        void ShowHttpsPage(CoreWebView2 c, Upgrade up, CoreWebView2WebErrorStatus why)
        {
            var t = TabOfCore(c); HttpsAsked++;
            if (t != null) { t.HttpsBlocked = up.Http; t.Url = up.Http; var w = Program.WindowOf(c); if (w != null && t == w.Active) w.UpdateBar(); }
            try { c.NavigateToString(HttpsRules.Interstitial(up.Host, up.Http, HttpsRules.Reason(why), Look.IsLight && !Private)); } catch { }
        }
        // the page's buttons (only while it's on screen - the tab knows which address it was about)
        void HttpsMessage(Tab t, string m)
        {
            if (t == null || t.HttpsBlocked == null || t.Core == null) return;
            var http = t.HttpsBlocked; var host = (Omni.HostOf(http) ?? "").ToLowerInvariant();
            if (m == "hallow:https-back") { t.HttpsBlocked = null; if (t.Core.CanGoBack) t.Core.GoBack(); else t.Core.Navigate(Private ? Omni.PrivateTabUrl : Omni.NewTabUrl); return; }
            if (m == "hallow:https-continue") HttpThisSession.Add(host);
            else if (m == "hallow:https-always") { if (Private) HttpThisSession.Add(host); else { SiteRules.Get(S, host, true).Http = true; S.Save(); } }
            else return;
            t.HttpsBlocked = null; try { t.Core.Navigate(http); } catch { }
        }
        public void ContinueHttp(Tab t, bool always) { HttpsMessage(t, always ? "hallow:https-always" : "hallow:https-continue"); }

        // ---- Settings: HTTPS-only and secure DNS
        public UIElement[] SecuritySettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Switch("HTTPS-only mode", "Addresses are tried with https first. When a site has no secure version, a page asks before going on over http. Local names and IP addresses (your router, a NAS, LAN games) are left alone.", S.HttpsOnly, on => { S.HttpsOnly = on; S.Save(); }));
            var allowed = S.Sites.Where(x => x.Http).Select(x => x.Host).ToList();
            if (allowed.Count > 0) items.Add(Ui.Muted("Allowed over http: " + string.Join(", ", allowed.Take(12)) + (allowed.Count > 12 ? "..." : "") + " (Site settings)", 11.5, 10));
            items.Add(Ui.Muted("Secure DNS (DNS over HTTPS): which provider looks up the names of the sites you open", 12, 6));
            var note = Ui.Muted("", 11.5, 4);
            Action showNote = () => note.Text = (S.SecureDns == (Engine.SecureDnsApplied ?? S.SecureDns) ? (S.SecureDns == "off" ? "Off: names are looked up the way Windows does." : "On: every name is looked up through " + Engine.DnsName(S.SecureDns) + " over HTTPS (encrypted) - and only there: if it can't be reached, pages don't load.") : "Restart Hallow Browser to use " + (S.SecureDns == "off" ? "Windows' DNS again." : Engine.DnsName(S.SecureDns) + "."));
            items.Add(Ui.Chips(BrowserSettings.DnsChoices.Select(d => new KeyValuePair<string, string>(d, Engine.DnsName(d))).ToList(), S.SecureDns, v => { S.SecureDns = v; S.Save(); showNote(); if (v != Engine.SecureDnsApplied) Toast("Secure DNS changes after a browser restart.", 4); }));
            showNote(); items.Add(note);
            items.Add(Ui.Muted("It's written into the page engine's own setting when the browser starts, so a change needs a restart. The provider sees the names of the sites you open (not the pages); your Wi-Fi or internet provider then doesn't.", 11, 0));
            return items.ToArray();
        }
    }
}
