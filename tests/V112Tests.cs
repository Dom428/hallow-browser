// Hallow Browser 1.12 - unit tests: the registration (written to an in-memory fake and to a sandbox key under
// HKCU\Software\HallowBrowserTests that is deleted after - never HKCU's real classes, clients or RegisteredApplications, which are
// compared before / after), the start-up decision, the default-browser status mapping (read-only; made-up ProgIDs), command-line
// arguments (URLs, local paths with spaces, # and unicode, split paths, junk), the pipe's "open" rules and the new tab hint.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace HallowBrowser.Tests
{
    public static class V112
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All()
        {
            var before = RealSnapshot();
            EntryTests(); FakeTests(); SandboxTests(); DecisionTests(); StatusTests(); ArgTests(); HintTests();
            Eq(RealSnapshot(), before, "registration: the real HKCU keys (ProgIDs, client, RegisteredApplications, UserChoice) are exactly as before the tests");
        }
        // what must never change because of the tests (read-only)
        static string RealSnapshot()
        {
            var parts = new List<string>();
            foreach (var p in new[] { @"Software\Classes\HallowBrowserURL\shell\open\command", @"Software\Classes\HallowBrowserHTML\shell\open\command", DefaultBrowser.ClientKey + @"\shell\open\command" })
                using (var k = Registry.CurrentUser.OpenSubKey(p)) parts.Add(p + "=" + (k == null ? "-" : k.GetValue("") as string));
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications")) parts.Add("ra=" + (k == null ? "-" : k.GetValue(DefaultBrowser.AppName) as string));
            foreach (var pr in new[] { "http", "https" })
                foreach (var n in new[] { @"\UserChoice", @"\UserChoiceLatest", @"\UserChoiceLatest\ProgId" })
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + pr + n))
                        parts.Add(pr + n + "=" + (k == null ? "-" : string.Join(",", k.GetValueNames().OrderBy(x => x).Select(x => x + ":" + k.GetValue(x)))));
            return string.Join("|", parts);
        }

        const string Exe = @"C:\Users\Test User\AppData\Local\Programs\Hallow Browser\HallowBrowser.exe";

        static void EntryTests()
        {
            var e = DefaultBrowser.Entries(Exe);
            Func<string, string, object> V = (p, n) => { var x = e.FirstOrDefault(r => r.Path.Equals(p, StringComparison.OrdinalIgnoreCase) && r.Name == n); return x == null ? null : x.Value; };
            Eq(V(@"Software\Classes\HallowBrowserURL\shell\open\command", ""), "\"" + Exe + "\" --single-argument %1", "registration: the URL ProgID opens with --single-argument %1 (like Chrome / Brave)");
            Eq(V(@"Software\Classes\HallowBrowserHTML\shell\open\command", ""), "\"" + Exe + "\" --single-argument %1", "registration: the file ProgID too");
            Ok(V(@"Software\Classes\HallowBrowserURL", "URL Protocol") as string == "" && V(@"Software\Classes\HallowBrowserHTML", "URL Protocol") == null, "registration: only the URL ProgID is a URL protocol");
            foreach (var pid in new[] { "HallowBrowserURL", "HallowBrowserHTML" })
                Ok(V(@"Software\Classes\" + pid + @"\Application", "ApplicationName") as string == "Hallow Browser" && V(@"Software\Classes\" + pid + @"\Application", "ApplicationIcon") as string == "\"" + Exe + "\",0" && V(@"Software\Classes\" + pid + @"\Application", "ApplicationDescription") != null && V(@"Software\Classes\" + pid + @"\Application", "AppUserModelId") == null, "registration: " + pid + " has an Application subkey (name, icon, description; no AppUserModelId - the app sets none)");
            Ok(DefaultBrowser.FileTypes.All(x => V(DefaultBrowser.ClientKey + @"\Capabilities\FileAssociations", x) as string == "HallowBrowserHTML") && DefaultBrowser.FileTypes.Length == 8, "registration: Capabilities\\FileAssociations for .htm .html .shtml .xht .xhtml .svg .pdf .webp -> HallowBrowserHTML");
            Ok(DefaultBrowser.FileTypes.All(x => V(@"Software\Classes\" + x + @"\OpenWithProgids", "HallowBrowserHTML") as string == ""), "registration: OpenWithProgids for every file type");
            Ok(V(DefaultBrowser.ClientKey + @"\Capabilities\URLAssociations", "http") as string == "HallowBrowserURL" && V(DefaultBrowser.ClientKey + @"\Capabilities\URLAssociations", "https") as string == "HallowBrowserURL", "registration: URLAssociations http / https");
            Eq(V(DefaultBrowser.ClientKey + @"\Capabilities\StartMenu", "StartMenuInternet"), "HallowBrowser", "registration: Capabilities\\StartMenu\\StartMenuInternet");
            Eq(V(@"Software\RegisteredApplications", "Hallow Browser"), DefaultBrowser.ClientKey + @"\Capabilities", "registration: RegisteredApplications points at the Capabilities");
            Ok(((string)V(DefaultBrowser.ClientKey + @"\InstallInfo", "ReinstallCommand")).EndsWith("--register") && ((string)V(DefaultBrowser.ClientKey + @"\InstallInfo", "HideIconsCommand")).EndsWith("--hide-icons") && ((string)V(DefaultBrowser.ClientKey + @"\InstallInfo", "ShowIconsCommand")).EndsWith("--show-icons"), "registration: InstallInfo commands (re-register, hide / show the shortcuts) - never --make-default-browser");
            Ok(!e.Any(r => r.Path.IndexOf("UserChoice", StringComparison.OrdinalIgnoreCase) >= 0 || r.Path.IndexOf(@"Shell\Associations", StringComparison.OrdinalIgnoreCase) >= 0 || r.Path.IndexOf("FileExts", StringComparison.OrdinalIgnoreCase) >= 0), "registration: nothing about the user's choice (no UserChoice, no Associations, no FileExts)");
            Ok(e.All(r => r.Path.StartsWith(@"Software\Classes\", StringComparison.Ordinal) || r.Path.StartsWith(DefaultBrowser.ClientKey, StringComparison.Ordinal) || r.Path == @"Software\RegisteredApplications"), "registration: only our ProgIDs, file types' OpenWithProgids, our client key and RegisteredApplications");
            Ok(!e.Any(r => r.Path.StartsWith(@"Software\Classes\", StringComparison.Ordinal) && !r.Path.StartsWith(@"Software\Classes\HallowBrowser", StringComparison.Ordinal) && !r.Path.EndsWith(@"\OpenWithProgids", StringComparison.Ordinal)), "registration: no file type's default ((default) of .html etc.) is touched");
            Eq(DefaultBrowser.SettingsUri, "ms-settings:defaultapps?registeredAppUser=Hallow%20Browser", "settings link: Default apps for the per-user registered name (documented format)");
        }

        static void FakeTests()
        {
            var r = new FakeRegistry();
            r.Set(@"Software\Classes\.html\OpenWithProgids", "BraveHTML", ""); r.Set(@"Software\Classes\BraveHTML\shell\open\command", "", "brave.exe"); r.Set(@"Software\RegisteredApplications", "Brave", "x");
            Ok(DefaultBrowser.RegisteredExe(r) == null && DefaultBrowser.Missing(r, Exe).Count == DefaultBrowser.Entries(Exe).Count, "fake: nothing registered at first");
            DefaultBrowser.Register(r, Exe);
            Ok(DefaultBrowser.Missing(r, Exe).Count == 0, "fake: registered completely");
            Eq(DefaultBrowser.RegisteredExe(r), Exe, "fake: the registered exe read back from the command");
            Eq(r.Get(DefaultBrowser.ClientKey + @"\InstallInfo", "IconsVisible"), 1, "fake: IconsVisible = 1 on the first registration");
            int w0 = r.Writes; DefaultBrowser.Register(r, Exe); Eq(r.Writes, w0, "fake: registering again writes nothing (already right)");
            r.Set(DefaultBrowser.ClientKey + @"\InstallInfo", "IconsVisible", 0); DefaultBrowser.Register(r, Exe);
            Eq(r.Get(DefaultBrowser.ClientKey + @"\InstallInfo", "IconsVisible"), 0, "fake: a re-registration keeps hidden icons hidden");
            var moved = @"D:\Games\Hallow Browser\HallowBrowser.exe";
            Ok(DefaultBrowser.Missing(r, moved).Count > 10, "fake: a moved exe = the registration is out of date");
            DefaultBrowser.Register(r, moved); Ok(DefaultBrowser.Missing(r, moved).Count == 0 && DefaultBrowser.RegisteredExe(r) == moved, "fake: re-registered to the moved exe");
            DefaultBrowser.Unregister(r);
            Ok(!r.Exists(@"Software\Classes\HallowBrowserURL") && !r.Exists(@"Software\Classes\HallowBrowserHTML") && !r.Exists(DefaultBrowser.ClientKey) && r.Get(@"Software\RegisteredApplications", "Hallow Browser") == null, "fake: unregister removes the ProgIDs, the client key and RegisteredApplications");
            Ok(DefaultBrowser.FileTypes.All(x => r.Get(@"Software\Classes\" + x + @"\OpenWithProgids", "HallowBrowserHTML") == null), "fake: unregister removes every OpenWithProgids entry of ours");
            Ok(r.Get(@"Software\Classes\.html\OpenWithProgids", "BraveHTML") as string == "" && r.Get(@"Software\Classes\BraveHTML\shell\open\command", "") as string == "brave.exe" && r.Get(@"Software\RegisteredApplications", "Brave") as string == "x", "fake: other browsers' entries are left alone");
            Ok(!r.Exists(@"Software\Classes\.webp") && r.Exists(@"Software\Classes\.html"), "fake: a file-type key we created and left empty goes; one with other entries stays");
        }

        static void SandboxTests()
        {
            var sub = @"Software\HallowBrowserTests\unit-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            bool refused = false; try { RealRegistry.Sandbox(@"Software\Classes"); } catch (ArgumentException) { refused = true; }
            Ok(refused, "sandbox: a test root outside HKCU\\Software\\HallowBrowserTests is refused");
            try
            {
                var r = RealRegistry.Sandbox(sub);
                DefaultBrowser.Register(r, Exe);
                using (var k = Registry.CurrentUser.OpenSubKey(sub + @"\Software\Classes\HallowBrowserURL\shell\open\command")) Eq(k == null ? null : k.GetValue(""), "\"" + Exe + "\" --single-argument %1", "sandbox: written to the real registry API under the test root");
                using (var k = Registry.CurrentUser.OpenSubKey(sub + @"\" + DefaultBrowser.ClientKey + @"\InstallInfo")) Ok(k != null && k.GetValueKind("IconsVisible") == RegistryValueKind.DWord, "sandbox: IconsVisible is a DWORD");
                Ok(DefaultBrowser.Missing(r, Exe).Count == 0, "sandbox: read back complete");
                DefaultBrowser.Unregister(r);
                Ok(!r.Exists(@"Software\Classes\HallowBrowserURL") && !r.Exists(DefaultBrowser.ClientKey) && r.Get(@"Software\RegisteredApplications", "Hallow Browser") == null && !r.Exists(@"Software\Classes\.html"), "sandbox: unregister leaves nothing of ours");
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(sub, false); } catch { }
                try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\HallowBrowserTests")) if (k != null && k.SubKeyCount == 0 && k.ValueCount == 0) { k.Close(); Registry.CurrentUser.DeleteSubKey(@"Software\HallowBrowserTests", false); } } catch { }
            }
            using (var k = Registry.CurrentUser.OpenSubKey(sub)) Ok(k == null, "sandbox: the test root is deleted after");
        }

        static void DecisionTests()
        {
            var inst = @"C:\U\AppData\Local\Programs\Hallow Browser\HallowBrowser.exe"; var dev = @"C:\U\HallowBrowser\HallowBrowser.exe"; var moved = @"D:\Apps\Hallow Browser\HallowBrowser.exe";
            Func<string, bool> yes = p => true, no = p => false;
            Eq(DefaultBrowser.StartDecision(inst, true, inst, inst, yes), "ok", "start: registered to this exe, complete -> nothing written");
            Eq(DefaultBrowser.StartDecision(inst, false, inst, inst, yes), "register", "start: same exe but something missing (e.g. 1.11's registration) -> re-register");
            Eq(DefaultBrowser.StartDecision(null, false, inst, inst, yes), "register", "start: nothing registered, the installed copy -> register");
            Ok(DefaultBrowser.StartDecision(null, false, dev, inst, yes).StartsWith("skip"), "start: nothing registered, a build run from elsewhere while an install exists -> left alone");
            Eq(DefaultBrowser.StartDecision(null, false, moved, inst, no), "register", "start: nothing registered and no install (a moved install) -> register");
            Eq(DefaultBrowser.StartDecision(inst, true, moved, inst, no), "register", "start: registered exe is gone (moved) -> register this one");
            Ok(DefaultBrowser.StartDecision(inst, true, dev, inst, yes).StartsWith("skip"), "start: registered to the installed copy that still exists -> a dev build never takes over");
            Eq(DefaultBrowser.StartDecision(dev, true, inst, inst, yes), "register", "start: the installed copy takes the registration back");
            Eq(DefaultBrowser.StartDecision(inst.ToUpperInvariant(), true, inst, inst, yes), "ok", "start: paths compared without case");
        }

        static void StatusTests()
        {
            var cases = new Dictionary<string, string> { { "BraveHTML", "Brave" }, { "ChromeHTML", "Google Chrome" }, { "MSEdgeHTM", "Microsoft Edge" }, { "HallowBrowserURL", "Hallow Browser" }, { "HallowBrowserHTML", "Hallow Browser" },
                { "FirefoxURL-308046B0AF4A39CB", "Firefox" }, { "VivaldiHTM.KCBJ5N4TYIZDVAIEE4JS2TIVG4", "Vivaldi" }, { "OperaGXStable", "Opera GX" }, { "OperaStable", "Opera" }, { "BraveSSHTM", "Brave Nightly" }, { "ChromeBHTML", "Google Chrome Beta" }, { "MSEdgeDHTML", "Microsoft Edge Dev" } };
            foreach (var c in cases) Eq(DefaultBrowser.FriendlyName(c.Key), c.Value, "status: " + c.Key + " -> " + c.Value);
            Ok(DefaultBrowser.FriendlyName("SomethingElse.Url") == null && DefaultBrowser.FriendlyName(null) == null && DefaultBrowser.FriendlyName("") == null, "status: unknown / none -> no name");
            Eq(DefaultBrowser.StatusFrom("BraveHTML", "BraveHTML").Text, "Your default browser is Brave.", "status text: Brave");
            Eq(DefaultBrowser.StatusFrom("ChromeHTML", "ChromeHTML").Text, "Your default browser is Google Chrome.", "status text: Chrome");
            Eq(DefaultBrowser.StatusFrom("MSEdgeHTM", "MSEdgeHTM").Text, "Your default browser is Microsoft Edge.", "status text: Edge");
            var h = DefaultBrowser.StatusFrom("HallowBrowserURL", "HallowBrowserURL"); Ok(h.IsHallow && h.Text == "Hallow Browser is your default browser.", "status text: Hallow");
            Eq(DefaultBrowser.StatusFrom("Weird.Assoc.1", "Weird.Assoc.1").Text, "Your default browser is another app (Weird.Assoc.1).", "status text: an unknown ProgID is named as it is");
            Eq(DefaultBrowser.StatusFrom("AppX90nv6nhay5n6a98fnetv7tpk64pp35es", null).Text, "Your default browser is a Microsoft Store app.", "status text: a Store app");
            Eq(DefaultBrowser.StatusFrom(null, null).Text, "No default browser is set for web links.", "status text: none set");
            var mix = DefaultBrowser.StatusFrom("HallowBrowserURL", "BraveHTML"); Ok(mix.IsHallow && mix.HttpDiffers && mix.Text.Contains("HTTP links open in Brave"), "status: HTTPS is Hallow but HTTP still Brave -> said");
            Ok(!DefaultBrowser.StatusFrom("BraveHTML", "ChromeHTML").HttpDiffers, "status: two other browsers aren't called out");
            var r = new FakeRegistry(); var b = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https";
            r.Set(b + @"\UserChoice", "ProgId", "ChromeHTML"); Eq(DefaultBrowser.ReadProgId(r, "https"), "ChromeHTML", "status: read from UserChoice");
            r.Set(b + @"\UserChoiceLatest\ProgId", "ProgId", "BraveHTML"); Eq(DefaultBrowser.ReadProgId(r, "https"), "BraveHTML", "status: UserChoiceLatest (newer Windows 11) wins over UserChoice");
            Eq(DefaultBrowser.ReadProgId(r, "http"), null, "status: nothing recorded -> null");
            DefaultBrowser.ProgIdOverride = "ChromeHTML"; Eq(DefaultBrowser.Status().Name, "Google Chrome", "status: the made-up answer used by --snap / tests"); DefaultBrowser.ProgIdOverride = null;
            Eq(SettingsLayout.SectionOf("Default browser"), "General", "settings: the Default browser card is in General");
            Ok(DefaultBrowser.HowTo.Contains("Set default") && DefaultBrowser.HowTo.Contains("HTTPS") && DefaultBrowser.HowTo.Contains("HTTP") && DefaultBrowser.HowTo.Contains(".html"), "settings: one line says what to click (Set default, or HTTPS / HTTP / .html)");
        }

        static void ArgTests()
        {
            Func<string, bool> none = p => false;
            Func<string, string> A = a => DefaultBrowser.ArgToUrl(a, @"C:\Work", none);
            Eq(A("https://example.com/a?b=1&c=2#top"), "https://example.com/a?b=1&c=2#top", "args: an https address as it is");
            Eq(A("http://127.0.0.1:8080/x"), "http://127.0.0.1:8080/x", "args: http with a port");
            Eq(A("\"https://example.com/\""), "https://example.com/", "args: surrounding quotes dropped");
            Eq(A("HTTPS://Example.COM/Path"), "https://example.com/Path", "args: scheme / host case normalised");
            var p1 = "C:\\My Files\\page #1 \u00fc\u00f1\u00ef \u20ac.html";
            var u1 = A(p1);
            Eq(u1, "file:///C:/My%20Files/page%20%231%20%C3%BC%C3%B1%C3%AF%20%E2%82%AC.html", "args: a local path with spaces, # and unicode -> file:/// (UTF-8, percent-escaped)");
            Eq(new Uri(u1).LocalPath, p1, "args: the file address leads back to exactly that path");
            var p2 = @"C:\Temp\100% sure;v=2.svg";
            var u2 = A(p2); Ok(u2 == "file:///C:/Temp/100%25%20sure%3Bv=2.svg" && new Uri(u2).LocalPath == p2, "args: % and ; escaped too (" + u2 + ")");
            Eq(A(@"C:/Users/me/a b.pdf"), "file:///C:/Users/me/a%20b.pdf", "args: forward slashes");
            Eq(A(@"c:\x\..\y\page.html"), "file:///C:/y/page.html", "args: .. resolved, drive upper case");
            var unc = A(@"\\NAS\Share\Docs\read me.html"); Ok(unc == "file://nas/Share/Docs/read%20me.html" && new Uri(unc).IsUnc, "args: a network path -> file://server/share/... (" + unc + ")");
            Eq(A("file:///C:/a%20b/c.html"), "file:///C:/a%20b/c.html", "args: a file: address as it is");
            Eq(A("example.com"), "https://example.com/", "args: a bare domain -> https");
            Eq(A("localhost:3000"), "http://localhost:3000/", "args: localhost:port -> http");
            foreach (var junk in new[] { "javascript:alert(1)", "data:text/html,<b>x</b>", "mailto:a@b.c", "hallow://settings", "vbscript:x", "cats", "how to cook rice", "notes.txt", "", "   ", "C:", "--weird", "ftp://example.com/" })
                Eq(A(junk), null, "args: refused (never searched): [" + junk + "]");
            Eq(DefaultBrowser.ArgToUrl("page.html", @"C:\Work", p => p == @"C:\Work\page.html"), "file:///C:/Work/page.html", "args: a relative file that's there (a console in its folder)");
            var parts = new List<string> { @"C:\My", @"Files\page", "#1.html", "https://example.com/", "junk words" };
            var joined = DefaultBrowser.UrlsFromArgs(parts, @"C:\Work", p => p == @"C:\My Files\page #1.html");
            Eq(string.Join(" | ", joined), "file:///C:/My%20Files/page%20%231.html | https://example.com/", "args: a path split at its spaces is joined back; the URL kept; junk dropped");
            Eq(DefaultBrowser.UrlsFromArgs(new List<string> { @"C:\a", "b.html" }, null, none).Count, 1, "args: a split path that doesn't exist isn't guessed (the first part as a path, the rest refused)");
            Eq(DefaultBrowser.SingleArgument("\"C:\\P\\HallowBrowser.exe\" --single-argument https://x.test/a?b=\"1\" --cmd quit"), "https://x.test/a?b=\"1\" --cmd quit", "single-argument: everything after it is one argument (no switches sneak in)");
            Eq(DefaultBrowser.SingleArgument("HallowBrowser.exe --single-argument \"C:\\My Files\\a #1.html\""), "C:\\My Files\\a #1.html", "single-argument: a quoted path keeps its spaces");
            Ok(DefaultBrowser.SingleArgument("HallowBrowser.exe https://a.test/") == null && DefaultBrowser.SingleArgument("HallowBrowser.exe --single-argument") == null && DefaultBrowser.SingleArgument("x.exe --single-argumentx y") == null, "single-argument: absent / empty -> nothing");
            string n;
            Ok(DefaultBrowser.Openable("file:///C:/a%20b.html", out n) && n == "file:///C:/a%20b.html", "pipe open: file addresses accepted (a second start hands them over)");
            Ok(DefaultBrowser.Openable("https://example.com", out n) && n == "https://example.com/", "pipe open: https");
            Ok(!DefaultBrowser.Openable("javascript:alert(1)", out n) && !DefaultBrowser.Openable("hallow://settings", out n) && !DefaultBrowser.Openable("cats", out n) && !DefaultBrowser.Openable("", out n), "pipe open: other schemes / words refused");
        }

        static void HintTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(!s.DefaultHintDismissed, "hint: shown by default (until dismissed)");
            var d = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(BrowserSettings));
            using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"))) { var old = (BrowserSettings)d.ReadObject(ms); old.Fix(); Ok(!old.DefaultHintDismissed, "hint: 1.11 settings -> not dismissed"); }
            var brave = DefaultBrowser.StatusFrom("BraveHTML", "BraveHTML");
            var h = NewTabDefault.Hint(s, false, brave); Ok(h != null && (string)h["name"] == "Brave", "hint: Brave is the default -> the hint names it");
            Ok(NewTabDefault.Hint(s, true, brave) == null, "hint: never while a game runs");
            Ok(NewTabDefault.Hint(s, false, DefaultBrowser.StatusFrom("HallowBrowserURL", "HallowBrowserURL")) == null, "hint: none once Hallow is the default");
            var un = NewTabDefault.Hint(s, false, DefaultBrowser.StatusFrom("Odd.Prog", "Odd.Prog")); Ok(un != null && (string)un["name"] == "another app", "hint: an unknown default -> 'another app'");
            s.DefaultHintDismissed = true; Ok(NewTabDefault.Hint(s, false, brave) == null, "hint: dismissed = never again");
            s.DefaultHintDismissed = false;
            DefaultBrowser.ProgIdOverride = "BraveHTML";
            var j = NewTabData.Build(s, new List<HistoryEntry>(), null, DateTime.Now);
            Ok(j.Contains("\"defaultHint\":{\"name\":\"Brave\"}"), "hint: in data.json");
            Ok(NewTabData.Build(s, new List<HistoryEntry>(), null, DateTime.Now, true).Contains("\"defaultHint\":null"), "hint: data.json without it during a game");
            DefaultBrowser.ProgIdOverride = null;
            var html = Engine.NewTabHtml("duckduckgo", "#F07820", false);
            Ok(html.Contains("id=dh") && html.Contains("hallow:nt-default'") && html.Contains("hallow:nt-default-off") && html.Contains("D.defaultHint"), "hint: the new tab page draws it and has its two buttons");
            Ok(Engine.NewTabHtml("duckduckgo", "#F07820", true).Contains("#dh .go{background:"), "hint: styled in the light look too");
        }
    }
}
