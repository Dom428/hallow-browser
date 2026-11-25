// Hallow Browser 1.19 - modern extras:
//  - pinned tabs: right-click a tab > Pin tab. Pinned tabs sit at the left of the strip as small icons (in the vertical column: a row
//    of icon tiles at the top), have no close button and are in no group; they come back after a restart (session.json). Ctrl+W and
//    middle-click still close them, like in Chrome; "Close other tabs" leaves them alone (also like Chrome);
//  - cookie banners declined (on by default): a small script finds the consent banners of the common consent managers (OneTrust,
//    Cookiebot, Didomi, Quantcast, Usercentrics, TrustArc, Google Funding Choices, Sourcepoint...) and plain "Reject all" / "Only
//    necessary" buttons in a cookie dialog, and clicks the reject / necessary-only choice - only exact button texts (en, es, fr, de, pt,
//    it, nl), never an Accept button, never on sign-in pages, never on a site you turned it off for (the padlock); it gives up after
//    8 s. The padlock's panel shows what it did in the tab;
//  - Copy clean link: the address without tracking parameters (utm_*, fbclid, gclid, ...; functional ones stay) - in the share
//    pop-up, the tab's menu, the page's right-click menu and Ctrl+K; optionally for every copy of the address (off by default);
//  - auto picture-in-picture (1.5's "pop out when I switch tabs", off by default) now leaves muted autoplay videos (ads, background
//    loops) and tiny players alone, and never pops out while game mode is on;
//  - group tabs by site (1.9's auto-group) in the strip's menu and Ctrl+K, and "Ungroup all".
// The optional "Ask Claude" sidebar is in Ai119.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        [DataMember] public bool CookieDecline = true;   // 1.19: decline cookie banners (reject / necessary only) - on by default
        [DataMember] public bool CleanCopy;              // 1.19: copying the address copies it without tracking parameters - off by default
        [DataMember] public bool AiOn;                   // 1.19: the Ask Claude sidebar - off by default (nothing is sent anywhere while off)
        [DataMember] public string AiModel;              // 1.19: the model picked there (null = the default)
        void Defaults119() { CookieDecline = true; }
        void Fix119() { if (AiModel != null && !AiApi.Models.Any(m => m[0] == AiModel)) AiModel = null; }
    }

    public partial class SiteRule
    {
        [DataMember] public string Consent;   // 1.19: null = cookie banners declined (when the switch is on) | "off" = left alone here
    }

    public partial class Tab
    {
        public bool Pinned;                   // 1.19: pinned (left of the strip, small, no close button)
        public int ConsentDeclined;           // 1.19: cookie banners declined in this tab
        public string ConsentHere;            // ... on the page shown now (which consent manager), null = none
    }

    // ---- pinned tabs: pinned ones first, the rest in their order
    public static class PinRules
    {
        public const double Width = 42;
        public static List<T> Order<T>(IList<T> tabs, Func<T, bool> pinned) { return tabs.Where(pinned).Concat(tabs.Where(x => !pinned(x))).ToList(); }
    }

    // ---- Copy clean link: tracking parameters out, everything else exactly as it was
    public static class LinkClean
    {
        static readonly HashSet<string> Exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "mc_eid", "mc_cid", "igshid", "igsh", "yclid", "twclid", "ttclid",
            "_hsenc", "_hsmi", "__hssc", "__hstc", "__hsfp", "hsctatracking", "mkt_tok", "ref_src", "ref_url", "li_fat_id", "wt.mc_id", "wt.mc_ev",
            "_gl", "srsltid", "s_kwcid", "ef_id", "epik", "rb_clickid", "vgo_ee", "oly_anon_id", "oly_enc_id",
            "pk_campaign", "pk_kwd", "pk_source", "pk_medium", "pk_content", "mtm_campaign", "mtm_kwd", "mtm_source", "mtm_medium", "mtm_content", "mtm_cid" };
        static readonly string[] Prefixes = { "utm_", "oly_", "vero_" };
        // "si" is a share id on YouTube and Spotify only (elsewhere it can mean anything)
        static bool SiHost(string host)
        {
            host = (host ?? "").ToLowerInvariant();
            foreach (var h in new[] { "youtube.com", "youtu.be", "spotify.com", "youtube-nocookie.com" }) if (host == h || host.EndsWith("." + h)) return true;
            return false;
        }
        public static bool IsTracking(string name, string host)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.Trim();
            if (Exact.Contains(n)) return true;
            foreach (var p in Prefixes) if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(n, "si", StringComparison.OrdinalIgnoreCase) && SiHost(host)) return true;
            return false;
        }
        public static string Clean(string url) { int n; return Clean(url, out n); }
        // the address with its tracking parameters taken out (the others keep their order and spelling); http(s) only
        public static string Clean(string url, out int removed)
        {
            removed = 0;
            if (string.IsNullOrEmpty(url)) return url;
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) return url;
            int hash = url.IndexOf('#'); var frag = hash >= 0 ? url.Substring(hash) : ""; var main = hash >= 0 ? url.Substring(0, hash) : url;
            int q = main.IndexOf('?'); if (q < 0) return url;
            var head = main.Substring(0, q); var query = main.Substring(q + 1);
            var kept = new List<string>();
            foreach (var part in query.Split('&'))
            {
                if (part.Length == 0) { kept.Add(part); continue; }
                var eq = part.IndexOf('='); var name = eq < 0 ? part : part.Substring(0, eq);
                string dn; try { dn = Uri.UnescapeDataString(name.Replace('+', ' ')); } catch { dn = name; }
                if (IsTracking(dn, u.Host)) { removed++; continue; }
                kept.Add(part);
            }
            if (removed == 0) return url;
            kept.RemoveAll(x => x.Length == 0);
            return head + (kept.Count > 0 ? "?" + string.Join("&", kept) : "") + frag;
        }
    }

    // ---- cookie banners: the page script (one per document, also in frames) and its rules
    public static class CookieBanner
    {
        public const int GiveUpMs = 8000;
        // exact button texts (after Normalize) that mean "reject" / "necessary only" - nothing that accepts
        public static readonly string[] Reject = {
            // en
            "reject all", "reject", "reject all cookies", "reject cookies", "reject optional cookies", "reject non-essential cookies", "reject additional cookies",
            "decline", "decline all", "decline all cookies", "decline cookies", "decline optional cookies", "deny", "deny all", "refuse", "refuse all",
            "only necessary", "necessary only", "only necessary cookies", "necessary cookies only", "use necessary cookies only", "only essential", "essential only",
            "only essential cookies", "essential cookies only", "use essential cookies only", "strictly necessary only", "continue without accepting", "do not consent", "disagree", "i disagree",
            // es
            "rechazar", "rechazar todo", "rechazar todas", "rechazar todas las cookies", "rechazar cookies", "denegar", "denegar todo", "solo necesarias", "sólo necesarias",
            "solo las necesarias", "solo cookies necesarias", "usar solo cookies necesarias", "continuar sin aceptar", "no consentir",
            // fr
            "tout refuser", "refuser", "refuser tout", "refuser tous", "refuser les cookies", "refuser tous les cookies", "je refuse", "continuer sans accepter",
            "uniquement nécessaires", "cookies nécessaires uniquement", "uniquement les cookies nécessaires",
            // de
            "alle ablehnen", "ablehnen", "nur notwendige", "nur notwendige cookies", "nur erforderliche", "nur erforderliche cookies", "nur essenzielle cookies", "nur essentielle cookies",
            "nur notwendige cookies verwenden", "verweigern", "alle verweigern", "optionale cookies ablehnen", "nicht einverstanden", "weiter ohne einwilligung",
            // pt
            "rejeitar", "rejeitar tudo", "rejeitar todos", "rejeitar todos os cookies", "recusar", "recusar tudo", "recusar todos", "recusar cookies",
            "apenas necessários", "apenas os necessários", "somente necessários", "só necessários", "apenas cookies necessários", "continuar sem aceitar",
            // it
            "rifiuta", "rifiuta tutto", "rifiuta tutti", "rifiuta tutti i cookie", "rifiutare", "rifiuto", "solo necessari", "solo cookie necessari", "solo i necessari", "continua senza accettare", "nega",
            // nl
            "alles weigeren", "weigeren", "alle weigeren", "weiger", "weiger alles", "weiger alle cookies", "afwijzen", "alles afwijzen", "alle afwijzen",
            "alleen noodzakelijke", "alleen noodzakelijke cookies", "alleen functionele cookies", "alleen essentiële cookies", "niet akkoord" };
        // what a button may never say when it is clicked through a consent manager's own id (belt and braces: a site that put the
        // accept text on the reject id is left alone)
        public static readonly string[] AcceptWords = { "accept", "akzeptier", "acept", "aceit", "accett", "accepteer", "agree", "allow all", "alle zulassen", "zustimm", "consent to all", "allow cookies", "got it", "einverstanden" };
        public static string Normalize(string t)
        {
            if (t == null) return "";
            var s = t.ToLowerInvariant().Replace('’', '\'');
            s = string.Join(" ", s.Split(new[] { ' ', '\t', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return s.Trim().TrimEnd('.', '!', ':', '>', '›', '»', '→').Trim();
        }
        public static bool IsReject(string text) { return Array.IndexOf(Reject, Normalize(text)) >= 0; }
        public static bool LooksAccept(string text)
        {
            var n = Normalize(text); if (IsReject(n)) return false;
            foreach (var a in AcceptWords) { var i = n.IndexOf(a, StringComparison.Ordinal); if (i >= 0 && !(a == "agree" && i >= 3 && n.Substring(i - 3, 3) == "dis") && !(a == "einverstanden" && n.Contains("nicht einverstanden"))) return true; }
            return false;
        }
        // consent managers: name, the reject / necessary-only button's selector, and the cookie / storage key it keeps once you
        // chose. A visible button is clicked; a hidden one (Hallow Shield's cookie lists hide many banners without answering them)
        // only while that key isn't there yet - so the site is told "no" once, not on every page
        public static readonly string[][] Known = {
            new[] { "OneTrust", "#onetrust-reject-all-handler", "OptanonAlertBoxClosed" }, new[] { "OneTrust", ".ot-pc-refuse-all-handler", "OptanonAlertBoxClosed" },
            new[] { "Cookiebot", "#CybotCookiebotDialogBodyButtonDecline", "CookieConsent" }, new[] { "Cookiebot", "#CybotCookiebotDialogBodyLevelButtonLevelOptinDeclineAll", "CookieConsent" },
            new[] { "Didomi", "#didomi-notice-disagree-button", "didomi_token" }, new[] { "Didomi", ".didomi-continue-without-agreeing", "didomi_token" },
            new[] { "TrustArc", "#truste-consent-required", "notice_gdpr_prefs|notice_preferences" },
            new[] { "Google Funding Choices", ".fc-cta-do-not-consent", "FCCDCF|FCNEC" },
            new[] { "Usercentrics", "[data-testid=uc-deny-all-button]", "uc_settings|ucData|uc_user_interaction" },
            new[] { "Sourcepoint", "button.sp_choice_type_REJECT_ALL", "consentUUID|euconsent-v2" },
            new[] { "Complianz", ".cmplz-btn.cmplz-deny", "cmplz_banner-status" }, new[] { "CookieYes", ".cky-btn-reject", "cookieyes-consent" } };
        // consent managers whose buttons are found by their text inside their own box (Quantcast's TCF dialog has no stable id)
        public static readonly string[][] Boxes = { new[] { "Quantcast", ".qc-cmp2-container,#qc-cmp2-ui" }, new[] { "TCF", ".message-container,.sp_message_container" }, new[] { "Usercentrics", "#uc-center-container,#usercentrics-cmp-ui" } };

        // the script: exempt = the hosts where it never runs (the sign-in providers' and the sites you turned it off for)
        public static string Script(IEnumerable<string> exempt)
        {
            var js = new JavaScriptSerializer();
            var ex = js.Serialize(exempt.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().TrimEnd('.').ToLowerInvariant()).Distinct().ToList());
            return ScriptBody.Replace("__EXEMPT__", ex).Replace("__REJECT__", js.Serialize(Reject)).Replace("__ACCEPT__", js.Serialize(AcceptWords))
                .Replace("__KNOWN__", js.Serialize(Known)).Replace("__BOXES__", js.Serialize(Boxes)).Replace("__LIMIT__", GiveUpMs.ToString());
        }
        const string ScriptBody = @"(function(){try{
if(window.__hallowConsent)return;window.__hallowConsent=1;
if(!/^https?:$/.test(location.protocol))return;
var EX=__EXEMPT__,REJ=__REJECT__,ACC=__ACCEPT__,KNOWN=__KNOWN__,BOXES=__BOXES__,LIMIT=__LIMIT__;
function hostOk(h,p){h=(h||'').toLowerCase().replace(/\.$/,'');if(EX.indexOf(h)>=0)return false;
if((h==='github.com'||h==='www.github.com')&&/^\/(login|session)(\/|$)|^\/sessions\//.test(p||''))return false;return true;}
if(!hostOk(location.hostname,location.pathname))return;
try{var ao=location.ancestorOrigins;if(ao)for(var i=0;i<ao.length;i++){var a=document.createElement('a');a.href=ao[i];if(!hostOk(a.hostname,''))return;}}catch(e){}
function norm(t){t=(t||'').toLowerCase().replace(/’/g,""'"").replace(/[\s ]+/g,' ').trim();return t.replace(/[.!:>›»→]+$/,'').trim();}
function isRej(t){return REJ.indexOf(norm(t))>=0;}
function looksAcc(t){var n=norm(t);if(REJ.indexOf(n)>=0)return false;for(var i=0;i<ACC.length;i++){var k=n.indexOf(ACC[i]);if(k<0)continue;if(ACC[i]==='agree'&&k>=3&&n.substr(k-3,3)==='dis')continue;if(ACC[i]==='einverstanden'&&n.indexOf('nicht einverstanden')>=0)continue;return true;}return false;}
function chosen(m){return m.split('|').some(function(k){if(('; '+document.cookie).indexOf('; '+k+'=')>=0)return true;try{return localStorage.getItem(k)!=null;}catch(e){return false;}});}
function txt(e){var t=(e.innerText||e.value||'').trim();if(!t)t=e.getAttribute('aria-label')||e.getAttribute('title')||'';return t;}
function vis(e){if(!e||e.disabled)return false;var r=e.getBoundingClientRect();if(r.width<2||r.height<2)return false;var s=getComputedStyle(e);return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.05;}
var WORDS=/cookie|galleta|témoin|traceur|consent|datenschutz|privacy|privacidad|privacidade|confidentialit|toestemming|tracking|gdpr|rgpd|dsgvo|tcf/i;
var BAD=/accept|allow-?all|(^|[^s])agree/i;
function roots(){var r=[document];['#usercentrics-root','#usercentrics-cmp-ui','#cmpwrapper','#cmp-app-container'].forEach(function(s){try{var h=document.querySelector(s);if(h&&h.shadowRoot)r.push(h.shadowRoot);}catch(e){}});return r;}
var BOX='[role=dialog],[role=alertdialog],[aria-modal=true],dialog,[id*=cookie i],[class*=cookie i],[id*=consent i],[class*=consent i],[id*=gdpr i],[class*=gdpr i],[id*=cmp i],[class*=cmp i],[id*=privacy i],[class*=privacy i],[id*=banner i],[class*=banner i]';
var BTN='button,[role=button],a,input[type=button],input[type=submit]';
function inBox(box,name){if(box.querySelector('input[type=password]'))return null;var bs=box.querySelectorAll(BTN);for(var j=0;j<bs.length;j++){var b=bs[j];if(!vis(b))continue;var t=txt(b);if(!isRej(t))continue;var attr=(b.id||'')+' '+(typeof b.className==='string'?b.className:'');if(BAD.test(attr))continue;return [name,b];}return null;}
function find(){var rs=roots();
for(var r=0;r<rs.length;r++){var R=rs[r];
for(var i=0;i<KNOWN.length;i++){var K=KNOWN[i],e=null;try{e=R.querySelector(K[1]);}catch(x){}if(!e||e.disabled||!e.isConnected||looksAcc(txt(e)||e.textContent))continue;if(vis(e)||(K[2]&&!chosen(K[2])))return [K[0],e];}
for(var i=0;i<BOXES.length;i++){var bx=null;try{bx=R.querySelectorAll(BOXES[i][1]);}catch(x){}if(bx)for(var k=0;k<bx.length;k++){if(!vis(bx[k]))continue;var f=inBox(bx[k],BOXES[i][0]);if(f)return f;}}
var cs=[];try{cs=R.querySelectorAll(BOX);}catch(x){}
for(var i=0;i<cs.length&&i<60;i++){var c=cs[i];if(!vis(c))continue;var tx=(c.innerText||'').slice(0,4000);if(!WORDS.test(tx)&&!WORDS.test((c.id||'')+' '+(typeof c.className==='string'?c.className:'')))continue;var f=inBox(c,'banner');if(f)return f;}}
return null;}
var t0=Date.now(),done=false,timer=null,mo=null,busy=false;
function stop(){done=true;if(timer)clearInterval(timer);if(mo)try{mo.disconnect();}catch(e){}}
function report(n){var m='hallow:consent\t'+n+'\t'+location.hostname;try{if(window.top===window){window.chrome.webview.postMessage(m);}else{window.top.postMessage({__hallowConsent:String(n)},'*');}}catch(e){}}
function tick(){if(done||busy)return;if(Date.now()-t0>LIMIT){stop();return;}busy=true;try{var r=find();if(r){stop();try{r[1].click();}catch(e){}report(r[0]);}}catch(e){}busy=false;}
if(window.top===window)addEventListener('message',function(e){var d=e.data;if(!d||typeof d!=='object'||typeof d.__hallowConsent!=='string'||e.source===window)return;try{window.chrome.webview.postMessage('hallow:consent\t'+d.__hallowConsent.slice(0,40)+'\tframe');}catch(x){}});
timer=setInterval(tick,400);
function watch(){try{mo=new MutationObserver(function(){if(!done)setTimeout(tick,60);});mo.observe(document.documentElement,{childList:true,subtree:true});}catch(e){}tick();}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',watch);else watch();
}catch(e){}})();";
    }

    // ---- three small glyphs drawn here, in Font Awesome's 512 box (nothing downloaded): a speech bubble, a chain link, a paper plane
    public static partial class Icons
    {
        static string Rr(double x, double y, double w, double h, double r, bool cw)
        {
            Func<double, string> f = d => d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (cw) return "M" + f(x + r) + " " + f(y) + "H" + f(x + w - r) + "A" + f(r) + " " + f(r) + " 0 0 1 " + f(x + w) + " " + f(y + r) + "V" + f(y + h - r) + "A" + f(r) + " " + f(r) + " 0 0 1 " + f(x + w - r) + " " + f(y + h) + "H" + f(x + r) + "A" + f(r) + " " + f(r) + " 0 0 1 " + f(x) + " " + f(y + h - r) + "V" + f(y + r) + "A" + f(r) + " " + f(r) + " 0 0 1 " + f(x + r) + " " + f(y) + "Z";
            return "M" + f(x + r) + " " + f(y) + "A" + f(r) + " " + f(r) + " 0 0 0 " + f(x) + " " + f(y + r) + "V" + f(y + h - r) + "A" + f(r) + " " + f(r) + " 0 0 0 " + f(x + r) + " " + f(y + h) + "H" + f(x + w - r) + "A" + f(r) + " " + f(r) + " 0 0 0 " + f(x + w) + " " + f(y + h - r) + "V" + f(y + r) + "A" + f(r) + " " + f(r) + " 0 0 0 " + f(x + w - r) + " " + f(y) + "Z";
        }
        public static void Add119()
        {
            if (!Data.ContainsKey("comments"))
                Data["comments"] = "M0 0L0 0M512 512L512 512" +
                    "M80 48h352a64 64 0 0 1 64 64v208a64 64 0 0 1 -64 64h-200l-120 88v-88h-32a64 64 0 0 1 -64 -64v-208a64 64 0 0 1 64 -64z" +
                    "M128 216a40 40 0 1 0 80 0a40 40 0 1 0 -80 0z M216 216a40 40 0 1 0 80 0a40 40 0 1 0 -80 0z M304 216a40 40 0 1 0 80 0a40 40 0 1 0 -80 0z";
            if (!Data.ContainsKey("link"))
                Data["link"] = "F1 M0 0L0 0M512 512L512 512" + Rr(8, 152, 300, 208, 104, true) + Rr(72, 216, 172, 80, 40, false) + Rr(204, 152, 300, 208, 104, true) + Rr(268, 216, 172, 80, 40, false);
            if (!Data.ContainsKey("paper-plane"))
                Data["paper-plane"] = "M0 0L0 0M512 512L512 512 M16 232L496 24L408 488L264 360L392 136L216 328Z M216 352L264 384L216 456Z";
        }
    }

    public partial class BrowserWindow
    {
        // ================================================================ pinned tabs
        WrapPanel vPinned;
        public void SetPinnedTab(Tab t, bool on)
        {
            if (t == null || t.Pinned == on || !Tabs.Contains(t) || AppMode) return;
            t.Pinned = on;
            if (on) t.Group = null;
            Tabs.Remove(t); var last = Tabs.FindLastIndex(x => x.Pinned); Tabs.Insert(last + 1, t);   // the end of the pinned ones / the first place after them
            t.Header.ClearValue(FrameworkElement.WidthProperty);
            RenderStrip(); Changed();
            if (on) Toast("Pinned - it stays at the left and comes back after a restart. Right-click it to unpin.", 3);
        }
        // the column: pinned tabs are icon tiles in a row of their own at the top
        void PinnedColumn119(List<UIElement> want, List<UIElement> heads, bool vert)
        {
            if (vPinned == null) vPinned = new WrapPanel { Margin = new Thickness(4, 2, 4, 4) };
            if (vert) { Sync(vPinned, heads); if (heads.Count > 0) want.Insert(0, vPinned); }
            else if (vPinned.Children.Count > 0) vPinned.Children.Clear();
        }
        void StylePinned119(Tab t)
        {
            if (!t.Pinned) { t.CloseBtn.ToolTip = "Close tab (Ctrl+W)"; return; }
            t.TitleBlock.Visibility = Visibility.Collapsed; t.RightBox.Visibility = Visibility.Collapsed; t.CloseBtn.Visibility = Visibility.Collapsed;
            t.IconHost.Margin = new Thickness((PinRules.Width - 16) / 2, 0, (PinRules.Width - 16) / 2, 0);
            t.Header.Width = PinRules.Width;
            if (S.VerticalTabs) { t.Header.HorizontalAlignment = HorizontalAlignment.Left; t.Header.CornerRadius = new CornerRadius(6); t.Header.Margin = new Thickness(2, 1, 2, 1); t.Header.Height = double.NaN; }
            if (!S.TabPreviews) t.Header.ToolTip = t.Title + " (pinned)";
        }
        public List<UIElement> TabMenuRows119(Tab t, Action<Action> run)
        {
            var r = new List<UIElement>();
            if (!AppMode) r.Add(Ui.MenuRow("thumbtack", t.Pinned ? "Unpin tab" : "Pin tab", null, () => run(() => SetPinnedTab(t, !t.Pinned))));
            if (Omni.IsWeb(t.Url)) r.Add(Ui.MenuRow("link", "Copy clean link", null, () => run(() => CopyCleanLink(t))));
            var oi = OpenInRow120(t, run); if (oi != null) r.Add(oi);   // 1.20: Open in Chrome / Brave / Edge
            return r;
        }
        public List<UIElement> StripMenuRows119(Action<Action> run)
        {
            var r = new List<UIElement>();
            r.Add(Ui.MenuRow("layer-group", "Group tabs by site", null, () => run(() => AutoGroupBySite())));
            if (Groups.Count > 0) r.Add(Ui.MenuRow("layer-group", "Ungroup all", null, () => run(() => UngroupAll())));
            return r;
        }
        public int UngroupAll()
        {
            int n = Groups.Count;
            foreach (var t in Tabs) t.Group = null;
            Groups.Clear(); RenderStrip(); Changed();
            Toast(n == 0 ? "No groups to undo." : n == 1 ? "Ungrouped (1 group)." : "Ungrouped all " + n + " groups.", 3);
            return n;
        }

        // ================================================================ Copy clean link
        public string CopyCleanLink(Tab t)
        {
            if (t == null || !Omni.IsWeb(t.Url)) return null;
            int n; var c = LinkClean.Clean(t.Url, out n); CopyText(c);
            Toast(n == 0 ? "Link copied (it had no tracking parameters)." : "Clean link copied - " + n + (n == 1 ? " tracking parameter" : " tracking parameters") + " removed.", 3);
            return c;
        }
        Button CleanLinkButton119(Tab t, Action close)
        {
            var b = Ui.Btn("Copy clean link", () => { CopyCleanLink(t); if (close != null) close(); }, false, "link");
            b.ToolTip = "The address without tracking parameters (utm_..., fbclid, gclid...)";
            return b;
        }
        FrameworkElement CleanLinkNote119(Tab t)
        {
            int n; LinkClean.Clean(t.Url, out n); if (n == 0) return null;
            var m = Ui.Muted(n + (n == 1 ? " tracking parameter" : " tracking parameters") + " in this address - \"Copy clean link\" leaves " + (n == 1 ? "it" : "them") + " out.", 11, 0);
            m.Margin = new Thickness(0, 6, 0, 0); m.TextWrapping = TextWrapping.Wrap; return m;
        }
        // the address bar: Ctrl+C / Copy of the address copies the clean one when "Clean links when I copy the address" is on
        void Build119()
        {
            DataObject.AddCopyingHandler(Address, (s, e) =>
            {
                try
                {
                    var sel = Address.SelectedText; var c = AddressCopyText(sel);
                    if (c == sel) return;
                    e.CancelCommand(); CopyText(c);
                }
                catch { }
            });
        }
        public string AddressCopyText(string selected)
        {
            var t = Active;
            if (!S.CleanCopy || t == null || !Omni.IsWeb(t.Url) || string.IsNullOrEmpty(selected)) return selected;
            var s = selected.Trim();
            if (s == t.Url || s == Omni.Display(t.Url) || s == AddressText.Clean(t.Url)) return LinkClean.Clean(t.Url);
            return selected;
        }
        public FrameworkElement CleanCopySwitch119()
        {
            return Ui.Switch("Clean links when I copy the address", "Off by default. When on, copying the page's address (Ctrl+C in the address bar, Copy link) leaves out tracking parameters such as utm_source, fbclid and gclid. \"Copy clean link\" (share, the tab's menu, right-click on a page) does it any time.", S.CleanCopy, on => { S.CleanCopy = on; S.Save(); });
        }

        // ================================================================ cookie banners
        readonly Dictionary<CoreWebView2, string> consentScripts = new Dictionary<CoreWebView2, string>();
        public static int ConsentTotal;   // tests: declined in this run
        public static string LastConsent;
        List<string> ConsentExempt()
        {
            var l = SignIn.Hosts.ToList();
            l.AddRange(S.Sites.Where(r => r.Consent == "off").Select(r => r.Host));
            return l;
        }
        async void AddConsent(CoreWebView2 c)
        {
            if (c == null || !S.CookieDecline || consentScripts.ContainsKey(c)) return;
            consentScripts[c] = "";
            try { consentScripts[c] = await c.AddScriptToExecuteOnDocumentCreatedAsync(CookieBanner.Script(ConsentExempt())); } catch { }
        }
        void ConsentChanged()
        {
            foreach (var c in Tabs.Select(t => t.Core).Where(x => x != null).ToList())
            {
                string id; if (consentScripts.TryGetValue(c, out id)) { try { if (id.Length > 0) c.RemoveScriptToExecuteOnDocumentCreated(id); } catch { } consentScripts.Remove(c); }
                if (S.CookieDecline) AddConsent(c);
            }
        }
        public static void ConsentChangedAll() { foreach (var w in Program.Windows.ToList()) if (!w.Gone) w.ConsentChanged(); }
        public bool ConsentOn(string url)
        {
            if (!S.CookieDecline || !Omni.IsWeb(url) || SignIn.IsIdentity(url)) return false;
            var r = SiteRules.Find(S, Omni.HostOf(url)); return r == null || r.Consent != "off";
        }
        public void SetConsentForSite(string host, bool on)
        {
            if (string.IsNullOrEmpty(host)) return;
            var r = SiteRules.Get(S, host, true); r.Consent = on ? null : "off";
            SiteRules.Tidy(S); S.Save(); ConsentChangedAll();
        }
        void Hook119(CoreWebView2 c)
        {
            AddConsent(c);
            c.WebMessageReceived += (a, b) =>
            {
                string m = null; try { m = b.TryGetWebMessageAsString(); } catch { }
                if (m == null || !m.StartsWith("hallow:consent\t")) return;
                Tab x; if (!owner.TryGetValue(c, out x)) return;
                string src = null; try { src = b.Source; } catch { }
                if (SignIn.IsIdentity(src) || !ConsentOn(x.Url)) return;   // never counted for a page it must not run on
                var p = m.Split('\t'); var name = p.Length > 1 && p[1].Length > 0 && p[1].Length <= 40 ? p[1] : "banner";
                x.ConsentDeclined++; x.ConsentHere = name == "banner" ? "a cookie banner" : name; ConsentTotal++; LastConsent = name;
            };
            c.NavigationStarting += (a, b) => { Tab x; if (owner.TryGetValue(c, out x)) x.ConsentHere = null; };
            // a view's first page can start before the script above is registered: run it once more in the loaded page (it runs only
            // once a document; through DevTools, so the page gets no user gesture from it)
            c.NavigationCompleted += (a, b) =>
            {
                Tab x; if (!b.IsSuccess || !owner.TryGetValue(c, out x) || !ConsentOn(x.Url)) return;
                try { var _ = c.CallDevToolsProtocolMethodAsync("Runtime.evaluate", new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "expression", CookieBanner.Script(ConsentExempt()) }, { "userGesture", false } })); } catch { }
            };
            c.ContextMenuRequested += (a, e) => CleanLinkMenu119(c, e);
        }
        // the site-info panel's line: what was declined in this tab, and the switch for this site
        FrameworkElement ConsentInfo119(Tab t, string host)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            string line;
            if (!S.CookieDecline) line = "Cookie banners: left as they are (Settings > Privacy)";
            else if (SignIn.IsIdentity(t.Url)) line = "Cookie banners: left alone on sign-in pages";
            else if (!ConsentOn(t.Url)) line = "Cookie banners: left alone on this site";
            else if (t.ConsentHere != null) line = "Declined " + t.ConsentHere + " here (reject / necessary only)" + (t.ConsentDeclined > 1 ? " - " + t.ConsentDeclined + " in this tab" : "");
            else line = t.ConsentDeclined > 0 ? "Cookie banners declined in this tab: " + t.ConsentDeclined : "No cookie banner to decline on this page";
            var row = Ui.Row(Ui.Icon("cookie-bite", 11, "AccentBrush"), Ui.T("  " + line, 12, false, "Text"));
            sp.Children.Add(row);
            if (S.CookieDecline && !SignIn.IsIdentity(t.Url))
            {
                var on = ConsentOn(t.Url); var h = host;
                var sw = new CheckBox { Content = "Decline cookie banners on this site", IsChecked = on, Style = Ui.S("Switch"), Margin = new Thickness(18, 4, 0, 0), FontSize = 12 };
                sw.Click += (a, b) => SetConsentForSite(h, sw.IsChecked == true);
                sw.Tag = "consent-site";
                sp.Children.Add(sw);
            }
            return sp;
        }
        public FrameworkElement CookieSwitch119()
        {
            return Ui.Switch("Decline cookie banners", "On by default. When a site shows a cookie banner (OneTrust, Cookiebot, Didomi, Quantcast, Usercentrics, TrustArc, Google's and plain \"Reject all\" ones), Hallow clicks Reject / Only necessary for you - never Accept. Not on sign-in pages; turn it off for one site with the padlock. It gives up after a few seconds if it finds nothing.", S.CookieDecline, on => { S.CookieDecline = on; S.Save(); ConsentChangedAll(); });
        }
        // the page's right-click menu: "Copy clean link" (the page) / "Copy clean link address" (a link)
        void CleanLinkMenu119(CoreWebView2 c, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                if (Engine.Env == null) return;
                var target = e.ContextMenuTarget; string url = null; bool link = false;
                if (target.HasLinkUri && Omni.IsWeb(target.LinkUri)) { url = target.LinkUri; link = true; }
                else if (target.Kind == CoreWebView2ContextMenuTargetKind.Page && !target.IsEditable && Omni.IsWeb(target.PageUri)) url = target.PageUri;
                if (url == null) return;
                var item = Engine.Env.CreateContextMenuItem(link ? "Copy clean link address" : "Copy clean link", null, CoreWebView2ContextMenuItemKind.Command);
                var u = url;
                item.CustomItemSelected += (s, x) => Dispatcher.BeginInvoke(new Action(() => { int n; CopyText(LinkClean.Clean(u, out n)); Toast(n == 0 ? "Link copied." : "Clean link copied - " + n + " tracking " + (n == 1 ? "parameter" : "parameters") + " removed.", 3); }));
                var items = e.MenuItems; int at = -1;
                for (int i = 0; i < items.Count; i++) { var nm = items[i].Name; if (nm == "copyLinkLocation" || (!link && nm == "copy")) at = i + 1; }
                if (at < 0 || at > items.Count) items.Add(item); else items.Insert(at, item);
                LastMenuAdded = item.Label;
            }
            catch { }
        }
        public static string LastMenuAdded;

        // ================================================================ auto picture-in-picture: which videos count
        public string LastAutoPip;
        // a playing video at least 200 x 112 that isn't a muted autoplay on a page you never touched (ads, background loops)
        public const string AutoPipCheckJs = "(function(){var vs=[].slice.call(document.querySelectorAll('video'));var best=null,ba=0;vs.forEach(function(v){if(v.paused||v.ended||v.readyState<2)return;" +
            "var r=v.getBoundingClientRect();if(r.width<200||r.height<112)return;var a=r.width*r.height;if(a>ba){ba=a;best=v;}});if(!best)return vs.length?'not playing':'no video';" +
            "var act=navigator.userActivation?navigator.userActivation.hasBeenActive:true;if((best.muted||best.volume===0)&&!act)return 'muted autoplay';return 'ok';})()";
        async Task<string> AutoPipWhy119(Tab t)
        {
            var c = t.Core; if (c == null) return "no view";
            try { var r = await Panels.Eval(c, AutoPipCheckJs); return new JavaScriptSerializer().Deserialize<string>(r) ?? "none"; } catch { return "failed"; }
        }

        // ================================================================ --snap (made-up)
        public void SnapExtras(string what)
        {
            switch (what)
            {
                case "pinned":
                case "pinned-vertical":
                {
                    foreach (var x in new[] { Tuple.Create("https://discord.com/channels/@me", "Discord"), Tuple.Create("https://open.spotify.com/", "Spotify - Web Player"), Tuple.Create("https://mail.example.com/", "Inbox (3)") })
                    { var nt = new Tab { Url = x.Item1, Title = x.Item2 }; AddHeader(nt, 0); Tabs.Insert(0, nt); }
                    RenderStrip();
                    foreach (var x in Tabs.Take(3).ToList()) SetPinnedTab(x, true);
                    if (what == "pinned-vertical") { S.VerticalTabs = true; ApplyTabLayout(); }
                    Activate(Tabs[3]);
                    break;
                }
                case "cookie":
                {
                    var t = Active; t.ConsentDeclined = 2; t.ConsentHere = "OneTrust";
                    var sp = SiteInfoPanel(t, null); var f = Ui.Flyout(sp, 430); f.HorizontalAlignment = HorizontalAlignment.Left; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(112, 4, 0, 0); f.Tag = "mock"; Grid.SetColumnSpan(f, 3); content.Children.Add(f);
                    break;
                }
                case "share-clean":
                {
                    var t = Active; t.Url = "https://news.example.com/patch-notes?id=42&utm_source=newsletter&utm_medium=email&fbclid=IwAR0made-up"; t.Title = "Hallow News - patch notes"; UpdateBar();
                    UpdatePageActions(t); shareBtn.Visibility = Visibility.Visible;
                    SnapAddRight(Ui.Flyout(SharePanel(t, null), 320), PageActionRightOf(shareBtn));
                    break;
                }
            }
        }
    }
}
