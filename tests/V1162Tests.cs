// Hallow Browser 1.16.2 - unit tests: sign-in pages are left alone (which addresses count, the Shield patch, the sleeping rule), and
// the plain warning before adding an extension that needs Chrome-only features. No network.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class V1162
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }

        public static void All()
        {
            // which requests belong to a sign-in
            Ok(SignIn.IsIdentity("https://accounts.google.com/o/oauth2/v2/auth?client_id=x&redirect_uri=gis_transform") && SignIn.IsIdentity("https://accounts.google.com/v3/signin/identifier?x=1")
                && SignIn.IsIdentity("https://oauth2.googleapis.com/token") && SignIn.IsIdentity("https://login.microsoftonline.com/common/oauth2/v2.0/authorize") && SignIn.IsIdentity("https://login.live.com/oauth20_authorize.srf")
                && SignIn.IsIdentity("https://appleid.apple.com/auth/authorize") && SignIn.IsIdentity("https://github.com/login/oauth/authorize?client_id=1") && SignIn.IsIdentity("https://github.com/login"),
                "sign-in 1.16.2: Google, Microsoft, Apple and GitHub sign-in addresses count as sign-in");
            Ok(!SignIn.IsIdentity("https://www.google.com/search?q=x") && !SignIn.IsIdentity("https://github.com/Dom428/hallow-browser") && !SignIn.IsIdentity("https://accounts.google.com.evil.example/o/oauth2")
                && !SignIn.IsIdentity("https://evil.example/?u=https://accounts.google.com/") && !SignIn.IsIdentity("https://github.com/loginx") && !SignIn.IsIdentity("not a url") && !SignIn.IsIdentity(null),
                "sign-in 1.16.2: other Google / GitHub pages, look-alike hosts and addresses that only mention a provider don't");
            Ok(SignIn.IsSignInRequest("https://claude.ai/api/auth/callback", "https://accounts.google.com/", null) && SignIn.IsSignInRequest("https://site.example/cb", null, "https://appleid.apple.com")
                && !SignIn.IsSignInRequest("https://site.example/page", "https://www.google.com/", "https://www.google.com"),
                "sign-in 1.16.2: the page a sign-in sends you back to (Referer / Origin is the provider) is left alone too");

            // the Shield patch against the Shield that ships with this build
            var bgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "shield", "background.js");
            var bg = File.Exists(bgPath) ? File.ReadAllText(bgPath) : null;
            var pb = SignIn.PatchBackground(bg);
            Ok(pb != null && pb.Contains(SignIn.Marker) && pb.Contains("wantedRulesets(s, LIST_IDS, groupsOf(m)).concat(['hallow-signin'])") && pb.Contains("exclude.push(...HALLOW_SIGNIN_EXCLUDE)"),
                "sign-in 1.16.2: the Shield copy keeps the sign-in ruleset on and runs no content scripts on sign-in pages" + (bg == null ? " (no shield\\background.js at " + bgPath + ")" : ""));
            var man = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(ShieldHost.PatchManifest(File.Exists(Path.Combine(Path.GetDirectoryName(bgPath), "manifest.json")) ? File.ReadAllText(Path.Combine(Path.GetDirectoryName(bgPath), "manifest.json")) : "{}"));
            var rr = ((System.Collections.ArrayList)((Dictionary<string, object>)man["declarative_net_request"])["rule_resources"]).Cast<Dictionary<string, object>>().ToList();
            Ok(rr.Count > 1 && (string)rr[0]["id"] == "hallow-signin" && (bool)rr[0]["enabled"] && (string)rr[0]["path"] == "rules/hallow-signin.json" && rr.Count(x => (string)x["id"] == "ublock-privacy") == 1,
                "sign-in 1.16.2: the copy's manifest has the sign-in ruleset (on) next to Shield's own lists");
            if (pb != null)
            {
                var lines = pb.Split('\n'); int lastImport = Array.FindLastIndex(lines, l => l.StartsWith("import ")); int marker = Array.FindIndex(lines, l => l.StartsWith(SignIn.Marker));
                Ok(marker == lastImport + 1, "sign-in 1.16.2: the patch comes right after Shield's imports (a module)");
                Ok(SignIn.PatchBackground(pb) == pb, "sign-in 1.16.2: patching twice changes nothing");
            }
            Ok(SignIn.PatchBackground("// some other Shield\nasync function syncPauses() {}") == null, "sign-in 1.16.2: a Shield whose code changed is loaded unpatched (no half patch)");
            var rules = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(SignIn.RulesJson());
            Ok(rules.Count == 5 && rules.All(r => Convert.ToInt32(r["priority"]) > 1000), "sign-in 1.16.2: the rules are above every Shield rule (paused sites' priority is 1000)");
            Ok(rules.Any(r => ((Dictionary<string, object>)r["action"])["type"] as string == "allowAllRequests" && ((Dictionary<string, object>)r["condition"]).ContainsKey("requestDomains"))
                && rules.Any(r => ((Dictionary<string, object>)r["condition"]).ContainsKey("initiatorDomains")), "sign-in 1.16.2: everything on sign-in pages is allowed, and the pages they send you to keep their parameters");

            // the tab that opened a sign-in pop-up doesn't sleep (not even in game mode)
            var now = DateTime.Now;
            var tabs = new List<TabState> { new TabState { Id = 1, Active = false, Busy = true, LastActive = now.AddHours(-1) }, new TabState { Id = 2, Active = true }, new TabState { Id = 3, LastActive = now.AddHours(-1) } };
            Ok(string.Join(",", Planner.ToSuspend(tabs, true, now, 15)) == "3", "sign-in 1.16.2: game mode sleeps background tabs, never the one waiting for its sign-in pop-up");
            List<int> sus, dis; Planner.OverCap(tabs, 5000, 1000, out sus, out dis);
            Ok(!sus.Contains(1) && !dis.Contains(1), "sign-in 1.16.2: the RAM limit doesn't put it to sleep either");

            // extensions: the plain warning, the Chrome-only features it lists
            var m = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"permissions\":[\"sidePanel\",\"debugger\",\"nativeMessaging\",\"identity\",\"tabGroups\",\"offscreen\",\"storage\",\"tabs\"]}");
            var gaps = WebStore.Gaps(m);
            Ok(gaps.Count == 6 && new[] { "side panels", "chrome.debugger", "native messaging", "chrome.identity", "chrome.tabGroups", "chrome.offscreen" }.All(k => gaps.Any(g => g.Contains(k))), "extensions 1.16.2: sidePanel, debugger, nativeMessaging, identity, tabGroups and offscreen are listed as missing");
            Ok(WebStore.Gaps(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"permissions\":[\"storage\",\"tabs\",\"scripting\"]}")).Count == 0, "extensions 1.16.2: ordinary permissions aren't");
            Ok(WebStore.GapWarning.StartsWith("This extension needs Chrome features Hallow Browser doesn't have, so it probably won't work"), "extensions 1.16.2: the warning says it plainly");

            // Google's own light / dark choice: not overridden by "Make pages dark"
            Ok(new[] { "www.google.com", "google.com", "www.google.co.uk", "google.de", "mail.google.com", "accounts.google.com" }.All(SiteThemes.OwnTheme), "theme 1.16.2: Google's pages draw their own light / dark theme");
            Ok(!new[] { "google.evil.com", "notgoogle.com", "example.com", "www.youtube.com", "", null }.Any(SiteThemes.OwnTheme), "theme 1.16.2: other sites and look-alikes don't");
            // prefers-color-scheme follows the page colours setting and the browser's look
            Ok(Engine.PageScheme("follow", false) == Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark && Engine.PageScheme("follow", true) == Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light
                && Engine.PageScheme("dark", true) == Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark && Engine.PageScheme("light", false) == Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light
                && Engine.PageScheme("auto", true) == Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Auto, "theme 1.16.2: sites' prefers-color-scheme = the browser's look (follow), or dark / light / Windows' as chosen");
        }
    }
}
