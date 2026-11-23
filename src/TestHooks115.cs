// Hallow Browser 1.15 - developer test commands for passkeys through Hallow Keys (only with HALLOW_BROWSER_TESTDIR, like the others):
// the prompt's state and its rows (clicked by calling the same code the buttons call, with the same half-second guard), the waiting
// autofill request, the setting, a script inside a tab's frame, and an extension popup (the WebView2 extension probe). Test profiles
// only; the Keys on the other end is a stand-in running Hallow Keys' own code with a made-up vault (never the real Keys).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V115(BrowserWindow w, string cmd, string arg)
        {
            string rest;
            switch (cmd)
            {
                case "state":
                    return Program.OnUi(w, () =>
                    {
                        var p = w.PkPrompt;
                        object prompt = p == null ? null : new Dictionary<string, object> { { "title", p.Title }, { "sub", p.Sub }, { "rows", p.Rows.Where(r => r.Hk != null).Select(r => r.Hk).ToList() },
                            { "titles", p.Rows.Where(r => r.Title != null).Select(r => r.Title).ToList() }, { "op", p.R.Op }, { "url", p.R.Url }, { "topUrl", p.R.TopUrl }, { "rpId", p.R.RpId }, { "busy", p.Busy },
                            { "tab", w.Tabs.IndexOf(p.R.Tab) }, { "frame", p.R.Frame != null }, { "onScreen", p.Pop != null && p.Pop.IsOpen } };
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "prompt", prompt }, { "last", w.LastPasskey }, { "messages", BrowserWindow.PasskeyMessages }, { "refused", BrowserWindow.PasskeyRefused },
                            { "fallbacks", BrowserWindow.PasskeyFallbacks }, { "creds", BrowserWindow.PasskeyCreds }, { "frames", BrowserWindow.FramesHooked }, { "on", w.S.KeysPasskeys },
                            { "conds", w.Tabs.Select((t, i) => t.PasskeyCond == null ? null : new Dictionary<string, object> { { "i", i }, { "items", t.PasskeyCond.Items.Count }, { "rpId", t.PasskeyCond.RpId }, { "locked", t.PasskeyCond.Locked } }).Where(x => x != null).ToList() } });
                    });
                case "click":   // <row>: save / pick-0 / fallback / cancel / unlock
                    return Program.OnUi(w, () => "ok " + w.PasskeyClick(w.PkPrompt, arg));
                case "keyshow":   // <tab>: does the key in the address bar show for a waiting passkey autofill request?
                    return Program.OnUi(w, () => { var t = TabAt(w, arg); if (t == null) return "error no tab"; w.Activate(t); return "ok " + js.Serialize(new Dictionary<string, object> { { "shows", w.PasskeyKeyShows(t) }, { "rows", w.PasskeyFlyoutRows(t, () => null).Count } }); });
                case "condpick":   // <tab> <n>
                {
                    var t = TabAt(w, Split(arg, out rest)); int n; if (t == null || !int.TryParse(rest, out n)) return "error";
                    return Async(w, async () => { var r = t.PasskeyCond; if (r == null || n >= r.Items.Count) return "error nothing waiting"; return "ok " + await w.PasskeyCondPick(t, KeysLink.Str(r.Items[n], "id", 64)); });
                }
                case "set": return Program.OnUi(w, () => { w.S.KeysPasskeys = arg == "on"; w.S.Save(); return "ok " + w.S.KeysPasskeys; });
                case "settings": return Program.OnUi(w, () => "ok " + js.Serialize(w.PasskeySettings().Select(e => e is System.Windows.Controls.CheckBox ? "switch: " + (((System.Windows.Controls.StackPanel)((System.Windows.Controls.CheckBox)e).Content).Children[0] as System.Windows.Controls.TextBlock).Text + " = " + ((System.Windows.Controls.CheckBox)e).IsChecked : e is System.Windows.Controls.TextBlock ? ((System.Windows.Controls.TextBlock)e).Text : e.GetType().Name).ToList()));
                case "frames": return Program.OnUi(w, () => { var t = TabAt(w, arg); List<Microsoft.Web.WebView2.Core.CoreWebView2Frame> fl; return "ok " + (t == null || t.Core == null || !w.PasskeyFrames.TryGetValue(t.Core, out fl) ? 0 : fl.Count); });
                case "feval":   // <tab> <expression>: in the tab's first frame (ExecuteScriptAsync - the result as JSON)
                {
                    var t = TabAt(w, Split(arg, out rest)); if (t == null) return "error no tab";
                    return Async(w, async () =>
                    {
                        List<Microsoft.Web.WebView2.Core.CoreWebView2Frame> fl;
                        if (t.Core == null || !w.PasskeyFrames.TryGetValue(t.Core, out fl) || fl.Count == 0) return "error no frame";
                        return "ok " + await fl[fl.Count - 1].ExecuteScriptAsync(rest);
                    });
                }
                case "popeval":   // <expression>: in the extension popup the last "test-v-xpopup" opened
                    return Async(w, async () => { var c = BrowserWindow.ExtPopupCore; if (c == null) return "error no popup"; return "ok " + await Panels.Eval(c, arg); });
            }
            return "error unknown 1.15 test command";
        }
    }
}
