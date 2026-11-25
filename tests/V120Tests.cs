// Hallow Browser 1.20 - unit tests: the extension verdicts (the three levels, their reasons, Claude in Chrome from a hand-written
// manifest), side panel paths, the chrome.sidePanel shim's text, "Open in Chrome" (which browser, the command line, the private flags,
// the standard install paths) and the new setting. No network, no browsers started, no registry written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace HallowBrowser.Tests
{
    public static class V120
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }
        static Dictionary<string, object> M(string json) { return ExtCompat.Parse(json); }

        public static void All() { Verdicts(); SidePanels(); OpenInRules(); Settings(); }

        static void Verdicts()
        {
            var plain = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"storage\",\"tabs\",\"scripting\",\"contextMenus\"],\"host_permissions\":[\"<all_urls>\"],\"background\":{\"service_worker\":\"bg.js\"}}"), "abcdefghijklmnopabcdefghijklmnop");
            Ok(plain.Level == ExtCompat.Full && plain.Title == "Works fully" && plain.Reasons.Count == 0 && !plain.AskClaude, "1.20 verdict: storage / tabs / scripting / a service worker - Works fully");
            var side = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"storage\",\"sidePanel\"],\"side_panel\":{\"default_path\":\"panel.html\"}}"), "x");
            Ok(side.Level == ExtCompat.Bridge && side.Title == "Works with Hallow's bridge" && side.SidePanelPath == "panel.html" && side.Reasons.Count == 1 && side.Reasons[0].Contains("Hallow's sidebar"), "1.20 verdict: a side panel only - Works with Hallow's bridge (" + string.Join(" | ", side.Reasons) + ")");
            var sideOnly = ExtCompat.Of(M("{\"manifest_version\":3,\"side_panel\":{\"default_path\":\"/p/side.html\"}}"), "x");
            Ok(sideOnly.Level == ExtCompat.Bridge && sideOnly.SidePanelPath == "p/side.html", "1.20 verdict: side_panel without the permission still counts; the path loses its leading slash");
            var runtime = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"sidePanel\"]}"), "x");
            Ok(runtime.Level == ExtCompat.Bridge && runtime.SidePanelPath == null && runtime.Notes.Any(n => n.Contains("while it runs")), "1.20 verdict: sidePanel with no default path - the bridge, and a note that it sets its page at run time");
            var dbg = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"storage\",\"debugger\",\"nativeMessaging\"]}"), "x");
            Ok(dbg.Level == ExtCompat.Chrome && dbg.Title == "Use Open in Chrome" && dbg.Keys.Contains("debugger") && dbg.Keys.Contains("nativeMessaging") && dbg.Reasons.Count == 2, "1.20 verdict: debugger + nativeMessaging - Use Open in Chrome (" + string.Join(" | ", dbg.Reasons) + ")");
            Ok(dbg.Reasons[0].StartsWith("chrome.debugger") && dbg.Reasons[1].StartsWith("native messaging"), "1.20 verdict: the reasons say which API, debugger first");
            var oauth = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"storage\"],\"oauth2\":{\"client_id\":\"x.apps.googleusercontent.com\",\"scopes\":[\"email\"]}}"), "x");
            Ok(oauth.Level == ExtCompat.Chrome && oauth.Keys.Contains("oauth2") && oauth.Reasons.Any(r => r.Contains("oauth2")), "1.20 verdict: an oauth2 key (Google sign-in) - Use Open in Chrome");
            var ident = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"identity\"],\"oauth2\":{\"client_id\":\"x\"}}"), "x");
            Ok(ident.Level == ExtCompat.Chrome && ident.Reasons.Count == 1, "1.20 verdict: identity + oauth2 is one reason, not two");
            foreach (var k in new[] { "tabGroups", "offscreen", "management", "tabCapture", "desktopCapture", "pageCapture", "gcm", "ttsEngine" })
                Ok(ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"" + k + "\"]}"), "x").Level == ExtCompat.Chrome, "1.20 verdict: " + k + " needs Chrome");
            var opt = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"storage\"],\"optional_permissions\":[\"nativeMessaging\",\"debugger\"]}"), "x");
            Ok(opt.Level == ExtCompat.Full && opt.Notes.Count == 2 && opt.Notes.All(n => n.StartsWith("It may ask later for")), "1.20 verdict: missing APIs only in optional_permissions - a note each, the level unchanged");
            var optSide = ExtCompat.Of(M("{\"manifest_version\":3,\"optional_permissions\":[\"sidePanel\"]}"), "x");
            Ok(optSide.Level == ExtCompat.Bridge, "1.20 verdict: an optional sidePanel - the bridge");
            var mv2 = ExtCompat.Of(M("{\"manifest_version\":2,\"permissions\":[\"storage\",\"http://*/*\"],\"background\":{\"scripts\":[\"bg.js\"],\"persistent\":true}}"), "x");
            Ok(mv2.Level == ExtCompat.Full && mv2.Notes.Any(n => n.StartsWith("Manifest V2")), "1.20 verdict: Manifest V2 gets a note; host permissions in permissions aren't APIs");
            var both = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"sidePanel\",\"debugger\"],\"side_panel\":{\"default_path\":\"s.html\"}}"), "x");
            Ok(both.Level == ExtCompat.Chrome && both.Reasons.Count == 2 && both.Reasons[1] == ExtCompat.SideReasonChrome && both.SidePanelPath == "s.html", "1.20 verdict: side panel + debugger - Chrome, the side panel listed as not enough on its own");
            Ok(ExtCompat.Of(null, "x").Level == ExtCompat.Full, "1.20 verdict: no manifest at all doesn't crash");

            // Claude in Chrome: a manifest written by hand from its published permissions (sidePanel, debugger, nativeMessaging, tabGroups ...)
            var claude = ExtCompat.Of(M(BrowserWindow.SnapClaudeManifest), ExtCompat.ClaudeId);
            Ok(claude.Level == ExtCompat.Chrome && claude.AskClaude && claude.Known != null && claude.Known.Contains("Ask Claude"), "1.20 verdict: Claude in Chrome - Use Open in Chrome, pointing to Hallow's Ask Claude");
            Ok(claude.Reasons.Count >= 3 && claude.Reasons[0].StartsWith("chrome.debugger") && claude.Reasons[1].StartsWith("side panel") && claude.Reasons[2].StartsWith("native messaging"), "1.20 verdict: Claude's reasons - debugger, side panel, native messaging first (" + string.Join(" | ", claude.Reasons.Take(3)) + ")");
            var claudeBare = ExtCompat.Of(M("{\"manifest_version\":3,\"permissions\":[\"storage\"]}"), ExtCompat.ClaudeId);
            Ok(claudeBare.Level == ExtCompat.Chrome && claudeBare.Keys.Contains("debugger") && claudeBare.Keys.Contains("sidePanel") && claudeBare.Keys.Contains("nativeMessaging"), "1.20 verdict: Claude's ID alone is enough (a changed manifest still gets the honest answer)");
            Ok(ExtCompat.ClaudeKnown.Contains("can't do its job here") && !ExtCompat.ClaudeKnown.Contains("works"), "1.20 verdict: the Claude note says plainly it can't work here");
        }

        static void SidePanels()
        {
            Eq(ExtCompat.SidePanelPath(M("{\"side_panel\":{\"default_path\":\"https://evil.example/x.html\"}}")), null, "1.20 side panel: a full address is not a panel path");
            Eq(ExtCompat.SidePanelPath(M("{\"side_panel\":{\"default_path\":\"../x.html\"}}")), null, "1.20 side panel: no climbing out of the extension");
            Eq(ExtCompat.SidePanelPath(M("{\"side_panel\":{}}")), null, "1.20 side panel: an empty one");
            Eq(BrowserWindow.SidePanelUrl("abcdefghijklmnopabcdefghijklmnop", "/panel/index.html"), "chrome-extension://abcdefghijklmnopabcdefghijklmnop/panel/index.html", "1.20 side panel: the page's address");
            var shim = BrowserWindow.SidePanelShim("panel.html");
            Ok(shim.Contains("location.protocol!=='chrome-extension:'") && shim.Contains("__hallow:true"), "1.20 shim: only in extension pages, marked as Hallow's");
            foreach (var f in new[] { "open:", "close:", "setOptions:", "getOptions:", "setPanelBehavior:", "getPanelBehavior:", "onOpened:", "onClosed:" }) Ok(shim.Contains(f), "1.20 shim: chrome.sidePanel." + f.TrimEnd(':'));
            Ok(shim.Contains("chrome.webview.postMessage('hallow:sidepanel\\t'") && shim.Contains("__hallowSidePanelBridge"), "1.20 shim: two ways to reach Hallow (postMessage, a DevTools binding)");
            Ok(shim.Contains("\"panel.html\"") && BrowserWindow.SidePanelShim(null).Contains("||null||undefined"), "1.20 shim: getOptions knows the manifest's default path");
            Ok(shim.Contains("Promise.resolve(v)") && shim.Contains("typeof cb==='function'"), "1.20 shim: promises and callbacks both resolve");
        }

        static void OpenInRules()
        {
            var all = new Dictionary<string, string> { { "chrome", "c.exe" }, { "brave", "b.exe" }, { "edge", "e.exe" } };
            Eq(OpenIn.Pick(null, all), "chrome", "1.20 open in: automatic - Chrome first");
            Eq(OpenIn.Pick(null, new Dictionary<string, string> { { "brave", "b" }, { "edge", "e" } }), "brave", "1.20 open in: no Chrome - Brave");
            Eq(OpenIn.Pick(null, new Dictionary<string, string> { { "edge", "e" } }), "edge", "1.20 open in: only Edge - Edge");
            Eq(OpenIn.Pick("edge", all), "edge", "1.20 open in: your pick when it's there");
            Eq(OpenIn.Pick("brave", new Dictionary<string, string> { { "chrome", "c" } }), "chrome", "1.20 open in: your pick gone - automatic");
            Eq(OpenIn.Pick(null, new Dictionary<string, string>()), null, "1.20 open in: nothing installed - nothing");
            Eq(OpenIn.Args("chrome", "https://example.com/a?b=1", false), "\"https://example.com/a?b=1\"", "1.20 open in: the address only, quoted - no profile flags");
            Eq(OpenIn.Args("chrome", "https://example.com/", true), "--incognito \"https://example.com/\"", "1.20 open in: Chrome's private window: --incognito");
            Eq(OpenIn.Args("brave", "https://example.com/", true), "--incognito \"https://example.com/\"", "1.20 open in: Brave's: --incognito");
            Eq(OpenIn.Args("edge", "https://example.com/", true), "--inprivate \"https://example.com/\"", "1.20 open in: Edge's: --inprivate");
            Eq(OpenIn.Args("chrome", "https://example.com/a\" --evil b", false), "\"https://example.com/a%22%20--evil%20b\"", "1.20 open in: a quote or space can't add a flag");
            Ok(OpenIn.CanOpen("https://x.example/") && OpenIn.CanOpen("http://127.0.0.1:5/") && !OpenIn.CanOpen("hallow://settings") && !OpenIn.CanOpen("file:///C:/x.html") && !OpenIn.CanOpen("chrome-extension://abc/p.html") && !OpenIn.CanOpen("javascript:alert(1)") && !OpenIn.CanOpen(null), "1.20 open in: only http and https pages");
            var c = OpenIn.Candidates("chrome", @"C:\PF", @"C:\PF86", @"C:\L");
            Ok(c.SequenceEqual(new[] { @"C:\PF\Google\Chrome\Application\chrome.exe", @"C:\PF86\Google\Chrome\Application\chrome.exe", @"C:\L\Google\Chrome\Application\chrome.exe" }), "1.20 open in: Chrome's standard paths");
            Ok(OpenIn.Candidates("brave", @"C:\PF", null, @"C:\L").SequenceEqual(new[] { @"C:\PF\BraveSoftware\Brave-Browser\Application\brave.exe", @"C:\L\BraveSoftware\Brave-Browser\Application\brave.exe" }), "1.20 open in: Brave's");
            Ok(OpenIn.Candidates("edge", @"C:\PF", @"C:\PF86", null)[0] == @"C:\PF86\Microsoft\Edge\Application\msedge.exe", "1.20 open in: Edge's (Program Files (x86) first)");
            var pf = OpenIn.ParseFound("chrome=C:\\a\\chrome.exe; edge = C:\\e.exe;opera=x;bad");
            Ok(pf.Count == 2 && pf["chrome"] == "C:\\a\\chrome.exe" && pf["edge"] == "C:\\e.exe", "1.20 open in: the test stand-in for what's installed");
            Eq(BrowserWindow.OpenInMenuLabel("brave", true), "Open link in Brave", "1.20 open in: the page's menu says which browser");
            Eq(OpenIn.PrivateName("edge"), "an InPrivate window", "1.20 open in: Edge's private window by its name");
        }

        static void Settings()
        {
            var s = new BrowserSettings();
            Ok(s.OpenIn == null, "1.20 settings: Open in - automatic by default");
            var ser = new DataContractJsonSerializer(typeof(BrowserSettings));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"Search\":\"google\"}"))) { var old = (BrowserSettings)ser.ReadObject(ms); old.Fix(); Ok(old.OpenIn == null, "1.20 settings: a 1.19 settings file - automatic"); }
            s.OpenIn = "brave";
            using (var ms = new MemoryStream()) { ser.WriteObject(ms, s); ms.Position = 0; var back = (BrowserSettings)ser.ReadObject(ms); back.Fix(); Eq(back.OpenIn, "brave", "1.20 settings: kept"); }
            s.OpenIn = "opera"; s.Fix(); Eq(s.OpenIn, null, "1.20 settings: an unknown browser goes back to automatic");
            Ok(!SyncData.SettingKeys.Contains("OpenIn"), "1.20 settings: which browser is installed is this PC's - not synced");
        }
    }
}
