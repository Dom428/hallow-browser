// Hallow Browser 1.5 - "Save to Hallow Keys?" after you sign in. A small script of the browser's own (top frame only) notices when a
// form with a filled password box is sent - a real submit, a real click on its button or Enter in a box - and hands the username
// and password to the browser, which asks Hallow Keys over Keys' own native-host protocol: first "save-check" (is it new, an
// update or already saved?), then - only after you click Save - "save" (the same two messages Keys' browser extension sends;
// Keys 1.1+ documents them as "offers to save new logins"). The same rules as Keys' extension, done by the browser:
//  - the address sent to Keys is the tab's real top-level address from the browser, never what the page says; the message must
//    come from the top frame of that same origin;
//  - nothing is saved without your click (Save / Never for this site / Not now); Keys itself decides new / update / same;
//  - the password is held only in memory while the question is open (at most 3 minutes, or until you answer or close the tab),
//    never written, logged, synced or put in a crash report; private windows never offer it.
// A page's own scripts could fake a "sent" form - all it can do is offer to save a login for its own site, which you can refuse.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static partial class KeysLink
    {
        // "new" | "update" | "same" | "locked" | "off" (Keys not running / no answer) | "no"
        public static string SaveCheck(string pageUrl, string user, string pass)
        {
            if (!Omni.IsWeb(pageUrl)) return "no";
            var r = Ask(new Dictionary<string, object> { { "type", "save-check" }, { "url", pageUrl }, { "username", user ?? "" }, { "password", pass ?? "" } });
            if (r == null) return "off";
            var st = B(r, "ok") ? S(r, "state") : B(r, "locked") ? "locked" : "no"; r.Clear(); return st;
        }
        public static string Save(string pageUrl, string user, string pass)
        {
            if (!Omni.IsWeb(pageUrl)) return "no";
            var r = Ask(new Dictionary<string, object> { { "type", "save" }, { "url", pageUrl }, { "username", user ?? "" }, { "password", pass ?? "" } });
            if (r == null) return "off";
            var st = B(r, "ok") ? S(r, "state") : B(r, "locked") ? "locked" : "no"; r.Clear(); return st;
        }
        // the in-page part: notices a sent login form (real events only), posts the username and password to the browser once per send
        public const string CaptureScript = "(function(){try{if(window.top!==window||window.__hallowLogins)return;window.__hallowLogins=1;" +
            "var wv=window.chrome&&window.chrome.webview;if(!wv)return;var post=wv.postMessage.bind(wv);var J=JSON.stringify;var last=0;" +
            "var grab=function(scope){scope=scope||document;var pws=[].slice.call(scope.querySelectorAll('input[type=password]')).filter(function(e){return e.value&&e.value.length<=1000;});if(!pws.length)return null;" +
            "var p=pws[pws.length>=2?pws.length-1:0];var all=[].slice.call(scope.querySelectorAll('input'));var u='';" +
            "for(var i=all.indexOf(pws[0])-1;i>=0;i--){var t=(all[i].type||'text').toLowerCase();if((t==='text'||t==='email'||t==='tel')&&all[i].value){u=all[i].value;break;}}" +
            "return {u:u.slice(0,500),p:p.value};};" +
            "var send=function(c){var n=Date.now();if(!c||n-last<1500)return;last=n;post('hallow:login\\t'+J(c));};" +
            "document.addEventListener('submit',function(e){if(e.isTrusted)send(grab(e.target));},true);" +
            "document.addEventListener('click',function(e){if(!e.isTrusted)return;var b=e.target&&e.target.closest&&e.target.closest('button,input[type=submit],input[type=image],[role=button]');if(!b)return;var f=b.form||b.closest('form');send(grab(f||document));},true);" +
            "document.addEventListener('keydown',function(e){if(!e.isTrusted||e.key!=='Enter')return;var t=e.target;if(!t||t.tagName!=='INPUT')return;send(grab(t.form||document));},true);" +
            "}catch(e){}})();";
        // what a "hallow:login" message may contain (unit-tested): null = refused
        public static Tuple<string, string> ParseLogin(string json)
        {
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json ?? ""); if (d == null) return null;
                object u, p; d.TryGetValue("u", out u); d.TryGetValue("p", out p);
                var us = u as string ?? ""; var ps = p as string;
                if (string.IsNullOrEmpty(ps) || ps.Length > 1000 || us.Length > 500) return null;
                return Tuple.Create(us, ps);
            }
            catch { return null; }
        }
    }

    public class LoginOffer { public Tab Tab; public string Url, Origin, Host, User, State; public string Pass; public DateTime At = DateTime.Now; public string Note; }

    public partial class BrowserWindow
    {
        public LoginOffer PendingLogin; Popup loginPop; DispatcherTimer loginTimer;
        public static int LoginOffers, LoginRefused;   // tests
        public string LastLoginNote;

        // the "hallow:login" message from the top frame of a tab
        async void OnLoginMessage(Tab t, CoreWebView2 c, string source, string json)
        {
            if (t == null || c == null) return;
            if (Private || !S.SaveLogins || !S.KeysOn) { LoginRefused++; return; }
            // the address of the top-level document that sent it, as the engine reports it (a page can't change its origin); the
            // engine delivers CoreWebView2.WebMessageReceived only for the top frame - frames' messages never get here
            var url = source; var origin = Omni.Origin(url);
            if (!Omni.IsWeb(url) || origin == null) { LoginRefused++; LastLoginNote = "not a web page"; return; }
            var host = SiteRules.Key(Omni.HostOf(url));
            if (S.NeverSave.Contains(host)) { LoginRefused++; LastLoginNote = "never for this site"; return; }
            var login = KeysLink.ParseLogin(json); json = null;
            if (login == null) { LoginRefused++; LastLoginNote = "no password"; return; }
            if (!KeysLink.Running()) { LastLoginNote = "Keys isn't running"; return; }
            var state = await Task.Run(() => KeysLink.SaveCheck(url, login.Item1, login.Item2));
            if (state != "new" && state != "update" && state != "locked") { LastLoginNote = "Keys says: " + state; login = null; return; }
            ClearLogin();
            PendingLogin = new LoginOffer { Tab = t, Url = url, Origin = origin, Host = host, User = login.Item1, Pass = login.Item2, State = state };
            Crash.Secrets = new[] { login.Item2 };   // never in a crash report
            login = null; LoginOffers++; LastLoginNote = "offered (" + state + ")";
            ShowLoginOffer();
            if (loginTimer == null) { loginTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; loginTimer.Tick += (a, b) => { if (PendingLogin == null || DateTime.Now - PendingLogin.At > TimeSpan.FromMinutes(3) || !Tabs.Contains(PendingLogin.Tab)) ClearLogin(); }; }
            loginTimer.Start();
        }
        void ShowLoginOffer()
        {
            var o = PendingLogin; if (o == null) return;
            var sp = new StackPanel { Margin = new Thickness(6) };
            var ic = Ui.Icon("key", 18, "AccentBrush"); ic.Margin = new Thickness(0, 2, 12, 0); ic.VerticalAlignment = VerticalAlignment.Top;
            var tx = new StackPanel();
            tx.Children.Add(Ui.T(o.State == "update" ? "Update the password in Hallow Keys?" : "Save to Hallow Keys?", 13.5, true, "Text", 2));
            tx.Children.Add(Ui.Muted((o.User.Length > 0 ? o.User + " on " : "") + o.Host, 12, 0));
            if (o.State == "locked") tx.Children.Add(Ui.T("Hallow Keys is locked - unlock it, then click Save.", 11.5, false, "AmberText", 0));
            sp.Children.Add(Ui.Row(ic, tx));
            var row = Ui.Row(Ui.Btn(o.State == "update" ? "Update" : "Save", () => { var _ = AnswerLogin("save"); }, true, "key"), Ui.Btn("Never for this site", () => { var _ = AnswerLogin("never"); }), Ui.Btn("Not now", () => { var _ = AnswerLogin("later"); }));
            row.Margin = new Thickness(30, 12, 0, 2); foreach (FrameworkElement b in row.Children) b.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = "Hallow Keys keeps it, encrypted. The browser doesn't store it.", FontSize = 10.5, Margin = new Thickness(30, 4, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
            var fl = Ui.Flyout(sp, 420); fl.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            if (loginPop != null) loginPop.IsOpen = false;
            loginPop = new Popup { Child = fl, PlacementTarget = addressBox, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = true, VerticalOffset = 6, HorizontalOffset = Math.Max(0, addressBox.ActualWidth - 430) };
            if (!BrowserSettings.Offscreen && !snap && IsVisible) loginPop.IsOpen = true;   // tests answer through the pipe (never on screen)
        }
        // Save / Never for this site / Not now
        public async Task<string> AnswerLogin(string choice)
        {
            var o = PendingLogin; if (o == null) return "nothing asked";
            if (loginPop != null) loginPop.IsOpen = false;
            string msg;
            if (choice == "save")
            {
                var url = o.Url; var u = o.User; var p = o.Pass;
                var r = await Task.Run(() => KeysLink.Save(url, u, p)); p = null;
                if (r == "locked") { o.State = "locked"; ShowLoginOffer(); Toast("Hallow Keys is locked - unlock it, then click Save again.", 5); return "locked"; }
                msg = r == "new" ? "Saved to Hallow Keys: " + (o.User.Length > 0 ? o.User + " on " : "") + o.Host + "." : r == "update" ? "Updated the password in Hallow Keys for " + o.Host + "." : r == "same" ? "Hallow Keys already has this login." : r == "off" ? "Hallow Keys didn't answer - nothing was saved." : "Hallow Keys didn't save it.";
                Toast(msg, 5); ClearLogin(); return r;
            }
            if (choice == "never") { if (!S.NeverSave.Contains(o.Host)) { S.NeverSave.Add(o.Host); S.Save(); } Toast("Hallow Browser won't offer to save logins for " + o.Host + " (Settings > Hallow Keys).", 5); }
            ClearLogin();
            return choice;
        }
        void ClearLogin()
        {
            if (PendingLogin != null) { PendingLogin.Pass = null; PendingLogin = null; }
            Crash.Secrets = new string[0];
            if (loginPop != null) { loginPop.IsOpen = false; loginPop = null; }
            if (loginTimer != null) loginTimer.Stop();
        }
    }
}
