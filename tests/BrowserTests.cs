// Hallow Browser - unit tests: the address bar (address or search), origins and the Hallow Keys fill rules, gaming-light mode /
// tab sleeping / RAM limiter decisions, settings, Shield's fallback rules and update picking, the theme, the family pipe and the
// Keys protocol against a stand-in (fake logins only - never a real vault).
//   BrowserTests.exe                                     unit tests (exit 0 = all passed)
//   BrowserTests.exe --integration <exe> <dir> [mode]    the real browser off-screen (see Integration.cs)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace HallowBrowser.Tests
{
    public static class Run
    {
        static int pass, fail;
        public static void Ok(bool c, string what) { if (c) { pass++; Console.WriteLine("ok   " + what); } else { fail++; Console.WriteLine("FAIL " + what); } }
        public static void Eq(object a, object b, string what) { var ok = Equals(a, b); Ok(ok, what + (ok ? "" : "  (got " + (a ?? "null") + ", expected " + (b ?? "null") + ")")); }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--integration") return Integration.Main(args[1], args[2], args.Length > 3 ? args[3] : "main");
            // 1.3 developer checks: a fixture for a real SQLite to check (integrity_check), a dump of a table's row count (for files a real
            // SQLite wrote, with a -wal), and the one read-only listing of this PC's browsers - names and counts are NOT printed
            if (args.Length >= 2 && args[0] == "--write-fixture") { File.WriteAllBytes(args[1], TinySqlite.ChromeHistory(Enumerable.Range(0, 600).Select(i => Tuple.Create("https://site" + i + ".example/", i == 3 ? "long " + new string('x', 9000) : "Page " + i, i + 1, DateTime.UtcNow.AddMinutes(-i), false)).ToList())); return 0; }
            if (args.Length >= 3 && args[0] == "--sqlite-dump") { var r = SqliteReader.Open(args[1]); var rows = r.Read(args[2]).ToList(); Console.WriteLine("rows " + rows.Count + " wal-pages " + r.WalPages + (args.Length > 3 ? " first " + rows.First()[args[3]] + " last " + rows.Last()[args[3]] : "")); return 0; }
            if (args.Length >= 1 && args[0] == "--found-browsers")
            {
                var found = Importer.FindOnThisPC(); int counted = 0, errors = 0;
                foreach (var p in found) { Importer.Count(p); if (p.Bookmarks >= 0 && p.History >= 0) counted++; else errors++; }
                Console.WriteLine("found " + found.Count + " profiles; counted " + counted + "; with a note " + errors);
                return 0;
            }
            var dir = Path.Combine(Path.GetTempPath(), "HallowBrowserUnit-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", dir);
            try
            {
                OmniTests(); OriginTests(); KeysRuleTests(); PlannerTests(); SettingsTests(); ShieldTests(); ThemeTests(); FamilyTests(); KeysPipeTests(); NewTabTests(); V11.All(); V12.All(); V13.All(); V14.All(); V15.All(); V16.All(); V17.All(); V18.All();
            }
            catch (Exception e) { fail++; Console.WriteLine("FAIL crashed: " + e); }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(fail == 0 ? "ALL " + pass + " PASSED" : fail + " FAILED, " + pass + " passed");
            return fail == 0 ? 0 : 1;
        }

        static void OmniTests()
        {
            Func<string, string> I = s => Omni.Interpret(s, "duckduckgo");
            Eq(I("example.com"), "https://example.com/", "bare domain -> https");
            Eq(I("www.example.co.uk/path?q=1"), "https://www.example.co.uk/path?q=1", "domain with path");
            Eq(I("http://example.com"), "http://example.com/", "http kept");
            Eq(I("HTTPS://Example.com/A"), "https://example.com/A", "scheme case");
            Eq(I("localhost:8080"), "http://localhost:8080/", "localhost:port -> http");
            Eq(I("localhost"), "http://localhost/", "localhost -> http");
            Eq(I("127.0.0.1:5000/x"), "http://127.0.0.1:5000/x", "IPv4 with port");
            Eq(I("[::1]:8080"), "http://[::1]:8080/", "IPv6");
            Eq(I("999.1.1.1"), Omni.SearchUrl("duckduckgo", "999.1.1.1"), "bad IP -> search");
            Eq(I("cats"), "https://duckduckgo.com/?q=cats", "one word -> search");
            Eq(I("how to cook rice"), "https://duckduckgo.com/?q=how%20to%20cook%20rice", "words -> search");
            Eq(I("notes.txt"), "https://duckduckgo.com/?q=notes.txt", "file-like name -> search");
            Eq(I("javascript:alert(1)"), "https://duckduckgo.com/?q=javascript%3Aalert(1)", "javascript: never runs from the bar");
            Ok(I("data:text/html,hi").StartsWith("https://duckduckgo.com/?q="), "data: -> search");
            Eq(I("  "), null, "empty -> nothing");
            Eq(I("about:blank"), "about:blank", "about:blank");
            Eq(I("hallow://settings"), Omni.SettingsUrl, "settings page");
            Eq(I("intranet:8080"), "http://intranet:8080/", "single label with port");
            Eq(I("play.gg"), "https://play.gg/", "gaming TLD");
            Eq(I("münchen.de"), new Uri("https://münchen.de/").AbsoluteUri, "unicode domain");
            Eq(Omni.Interpret("rust lang", "google"), "https://www.google.com/search?q=rust%20lang", "Google");
            Eq(Omni.Interpret("rust", "bing"), "https://www.bing.com/search?q=rust", "Bing");
            Eq(Omni.Interpret("rust", "brave"), "https://search.brave.com/search?q=rust", "Brave Search");
            Eq(Omni.Interpret("a&b=c", "duckduckgo"), "https://duckduckgo.com/?q=a%26b%3Dc", "search text is escaped");
            Eq(Omni.Display(Omni.NewTabUrl), "", "new tab shows an empty bar");
            Eq(Omni.Display("https://x.com/"), "https://x.com/", "pages show their address");
            Ok(Omni.IsNewTab("https://newtab.hallow/") && !Omni.IsWeb("https://newtab.hallow/"), "new tab isn't a web page");
            var sug = Omni.Suggest(new[] { "https://www.youtube.com/", "https://github.com/x", "https://news.ycombinator.com/" }, "you", 5);
            Eq(sug.FirstOrDefault(), "https://www.youtube.com/", "suggestion: prefix after www");
            Eq(Omni.Suggest(new[] { "https://a.com/" }, "", 5).Count, 0, "no suggestions for nothing");
        }

        static void OriginTests()
        {
            Eq(Omni.Origin("https://Example.com:443/login?a"), "https://example.com", "origin drops default port");
            Eq(Omni.Origin("http://127.0.0.1:8080/x"), "http://127.0.0.1:8080", "origin keeps other ports");
            Eq(Omni.Origin("ftp://x.com/"), null, "only http/https have origins");
            Eq(Omni.Origin("https://xn--mnchen-3ya.de/"), Omni.Origin("https://münchen.de/"), "punycode and unicode are the same origin");
            Ok(Omni.SameOrigin("https://a.com/x", "https://a.com/y"), "same origin");
            Ok(!Omni.SameOrigin("https://a.com/", "https://b.a.com/"), "subdomain is another origin");
            Ok(!Omni.SameOrigin("https://a.com/", "http://a.com/"), "scheme matters");
            Ok(!Omni.SameOrigin("https://a.com/", "https://a.com:8443/"), "port matters");
            Ok(!Omni.SameOrigin("https://аpple.com/", "https://apple.com/"), "look-alike letters are another origin");
        }

        static void KeysRuleTests()
        {
            Eq(KeysLink.WhyNotFill(false, "https://a.com", "https://a.com/login", true), "a fill needs your click", "no click, no fill");
            Eq(KeysLink.WhyNotFill(true, "https://a.com", "https://a.com/login", true), null, "click on the same origin fills");
            Ok(KeysLink.WhyNotFill(true, "https://a.com", "https://evil.com/login", true) != null, "another site after the click: refused");
            Ok(KeysLink.WhyNotFill(true, "https://a.com", "https://login.a.com/", true) != null, "another subdomain: refused (exact origin only)");
            Ok(KeysLink.WhyNotFill(true, "https://a.com", "http://a.com/", true) != null, "https -> http: refused");
            Ok(KeysLink.WhyNotFill(true, null, "https://a.com/", true) != null, "no pinned origin: refused");
            Ok(KeysLink.WhyNotFill(true, "https://a.com", "https://a.com/", false) != null, "Keys autofill off: refused");
            Ok(KeysLink.WhyNotFill(true, "https://a.com", "file:///c:/x.html", true) != null, "non-web page: refused");
            var s = KeysLink.FillScript("https://a.com", "u\"ser", "p'</script>\\x");
            Ok(s.Contains("location.origin!==\"https://a.com\""), "fill script re-checks the origin in the page");
            Ok(s.Contains("window.top!==window"), "fill script fills only the top frame");
            Ok(s.Contains("\"u\\\"ser\"") && s.Contains("\\u003c/script\\u003e") || s.Contains("\\u003c/script"), "values are JSON-escaped");
            Ok(!s.Contains("</script>"), "no raw </script> in the script");
            // a fill that wasn't clicked never even asks Keys
            var r = KeysLink.Fill(new KeysItem { Id = "1", Title = "t", Username = "u" }, false, "https://a.com", () => "https://a.com/", true, x => { throw new Exception("must not run"); }).Result;
            Ok(r.StartsWith("Not filled"), "unclicked fill is refused before asking Keys");
        }

        static void PlannerTests()
        {
            var now = DateTime.Now;
            Func<int, bool, bool, int, TabState> T = (id, active, audible, ago) => new TabState { Id = id, Active = active, Audible = audible, LastActive = now.AddMinutes(-ago) };
            var tabs = new List<TabState> { T(1, true, false, 0), T(2, false, true, 30), T(3, false, false, 1), T(4, false, false, 40) };
            Eq(string.Join(",", Planner.ToSuspend(tabs, true, now, 15)), "3,4", "game: every quiet background tab sleeps, never the active or audible one");
            Eq(string.Join(",", Planner.ToSuspend(tabs, false, now, 15)), "4", "no game: only tabs idle longer than the sleep time");
            Eq(Planner.ToSuspend(tabs, false, now, 0).Count, 0, "sleep time Never");
            tabs.Add(new TabState { Id = 5, PoppedOut = true, LastActive = now.AddHours(-2) });
            tabs.Add(new TabState { Id = 6, Suspended = true, LastActive = now.AddHours(-2) });
            tabs.Add(new TabState { Id = 7, Discarded = true, LastActive = now.AddHours(-2) });
            Ok(!Planner.ToSuspend(tabs, true, now, 15).Contains(5), "popped-out video never sleeps");
            Ok(!Planner.ToSuspend(tabs, true, now, 15).Contains(6) && !Planner.ToSuspend(tabs, true, now, 15).Contains(7), "already asleep / unloaded tabs are left alone");
            var slept = new HashSet<int> { 3, 6 };
            var st = new List<TabState> { new TabState { Id = 3, Suspended = true, LastActive = now.AddMinutes(-2) }, new TabState { Id = 6, Suspended = true, LastActive = now.AddHours(-2) }, new TabState { Id = 8, Suspended = true, LastActive = now } };
            Eq(string.Join(",", Planner.ToWakeAfterGame(st, now, 15, slept)), "3", "after the game: wake what game mode put to sleep (not long-idle ones, not others)");
            List<int> sus, dis;
            var ram = new List<TabState> { T(1, true, false, 0), T(2, false, false, 50), T(3, false, false, 10), T(4, false, true, 60) };
            Planner.OverCap(ram, 3000, 4096, out sus, out dis); Ok(sus.Count == 0 && dis.Count == 0, "under the RAM cap: nothing");
            Planner.OverCap(ram, 3000, 0, out sus, out dis); Ok(sus.Count == 0 && dis.Count == 0, "RAM limiter off: nothing");
            Planner.OverCap(ram, 6000, 4096, out sus, out dis);
            Eq(string.Join(",", sus), "2,3", "over the cap: least recently used background tabs sleep first");
            Ok(dis.Count > 0 && dis[0] == 2 && !dis.Contains(1) && !dis.Contains(4), "over the cap: LRU tabs unload, never the active or audible one");
            Planner.OverCap(ram, 4300, 4096, out sus, out dis); Ok(dis.Count <= 1, "a little over: at most one unloads");
            Eq(Planner.Priority(true, false), System.Diagnostics.ProcessPriorityClass.BelowNormal, "game -> below normal");
            Eq(Planner.Priority(false, true), System.Diagnostics.ProcessPriorityClass.BelowNormal, "CPU limiter -> below normal");
            Eq(Planner.Priority(false, false), System.Diagnostics.ProcessPriorityClass.Normal, "otherwise normal");
            var fg = new FakeGames(); Eq(fg.Game(), null, "fake game source: none"); fg.Value = "Delta Force"; Eq(fg.Game(), "Delta Force", "fake game source: one");
            var gf = Path.Combine(BrowserSettings.TestDir, "game.txt"); var fgs = new FileGames(gf); Eq(fgs.Game(), null, "file game source: missing file = none");
            File.WriteAllText(gf, "Discovery\r\n"); Eq(fgs.Game(), "Discovery", "file game source: name");
            Ok(WindowsGames.Known.ContainsKey("DeltaForceClient-Win64-Shipping") && WindowsGames.Known.ContainsKey("discovery-d"), "known games (case-insensitive)");
        }

        static void SettingsTests()
        {
            var s = BrowserSettings.Load();
            Eq(s.Search, "duckduckgo", "default search: DuckDuckGo");
            Ok(s.RestoreSession && s.BlockThirdPartyCookies && s.DoNotTrack && s.GameLight && s.KeysOn && !s.ClearOnExit, "defaults: restore, 3P cookies blocked, GPC, game mode, Keys on, no clearing");
            Eq(s.PageColors, "follow", "pages follow the browser's look by default (1.1)");
            Eq(s.SleepMinutes, 15, "tabs sleep after 15 min");
            s.Search = "google"; s.SleepMinutes = 30; s.RamLimitMb = 4096; s.Remember("https://a.com/"); s.Remember("https://b.com/"); s.Remember("https://a.com/"); s.Save();
            var t = BrowserSettings.Load();
            Ok(t.Search == "google" && t.SleepMinutes == 30 && t.RamLimitMb == 4096, "settings round-trip");
            Eq(string.Join(" ", t.Typed), "https://a.com/ https://b.com/", "typed addresses: newest first, no duplicates");
            File.WriteAllText(Path.Combine(BrowserSettings.Dir, "settings.json"), "{\"Search\":\"altavista\",\"SleepMinutes\":7,\"RamLimitMb\":123,\"PageColors\":\"neon\"}");
            var b = BrowserSettings.Load();
            Ok(b.Search == "duckduckgo" && b.SleepMinutes == 15 && b.RamLimitMb == 0 && b.PageColors == "follow", "bad values fall back to defaults");
            Ok(b.GameLight && b.KeysOn, "missing values get their defaults");
            File.WriteAllText(Path.Combine(BrowserSettings.Dir, "settings.json"), "{ not json");
            Eq(BrowserSettings.Load().Search, "duckduckgo", "broken file = defaults");
            for (int i = 0; i < 250; i++) b.Remember("https://s" + i + ".com/");
            Eq(b.Typed.Count, 200, "at most 200 typed addresses");
            b.Remember("javascript:alert(1)"); Ok(!b.Typed.Contains("javascript:alert(1)"), "only web addresses are remembered");
            var sess = new Session(); sess.Tabs.Add(new SessionTab { Url = "https://a.com/", Title = "A" }); sess.Active = 0; BrowserSettings.SaveSession(sess);
            var ls = BrowserSettings.LoadSession(); Ok(ls != null && ls.Tabs.Count == 1 && ls.Tabs[0].Url == "https://a.com/", "session round-trip");
            Ok(BrowserSettings.ProfileDir.StartsWith(BrowserSettings.TestDir), "tests never touch the real profile");
        }

        static void ShieldTests()
        {
            var r = new ShieldHost.Rules();
            ShieldHost.AddRules(r, "[{\"id\":1,\"action\":{\"type\":\"block\"},\"condition\":{\"urlFilter\":\"||ads.example^\",\"resourceTypes\":[\"script\",\"image\"]}}," +
                "{\"id\":2,\"action\":{\"type\":\"block\"},\"condition\":{\"urlFilter\":\"||cdn.example/pagead/\"}}," +
                "{\"id\":3,\"action\":{\"type\":\"block\"},\"condition\":{\"urlFilter\":\"||site.example^\",\"initiatorDomains\":[\"x.com\"]}}," +
                "{\"id\":4,\"action\":{\"type\":\"allow\"},\"condition\":{\"urlFilter\":\"||ok.example^\"}}," +
                "{\"id\":5,\"action\":{\"type\":\"block\"},\"condition\":{\"urlFilter\":\"||page.example^\",\"resourceTypes\":[\"main_frame\"]}}," +
                "{\"id\":6,\"action\":{\"type\":\"block\"},\"condition\":{\"urlFilter\":\"/banner*.gif\"}}]");
            Ok(r.Blocks("https://ads.example/x.js") && r.Blocks("http://sub.ads.example/a"), "host rule blocks the host and its subdomains");
            Ok(!r.Blocks("https://notads.example/x") && !r.Blocks("https://ads.example.com/"), "host rule doesn't block look-alikes");
            Ok(r.Blocks("https://cdn.example/pagead/show_ads.js") && !r.Blocks("https://cdn.example/other.js"), "host+path rule");
            Ok(!r.Blocks("https://site.example/x"), "site-specific rules are left to the extension");
            Ok(!r.Blocks("https://ok.example/"), "allow rules never block");
            Ok(!r.Blocks("https://page.example/"), "rules for pages you open are skipped");
            Eq(r.Count, 2, "two plain rules kept");
            // picking the newest Shield: the bundled one (none in the test folder) or a downloaded update
            var up = Path.Combine(ShieldHost.UpdatesDir, "1.7"); Directory.CreateDirectory(up);
            File.WriteAllText(Path.Combine(up, "manifest.json"), "{\"name\":\"Hallow Shield\",\"version\":\"1.7\"}");
            var bad = Path.Combine(ShieldHost.UpdatesDir, "9.9"); Directory.CreateDirectory(bad);
            File.WriteAllText(Path.Combine(bad, "manifest.json"), "{\"name\":\"Something else\",\"version\":\"9.9\"}");
            Version v; var pick = ShieldHost.Pick(out v);
            Ok(pick == up && v == new Version(1, 7), "picks the newest real Shield build (not another extension)");
            ShieldHost.Tidy(up); Ok(Directory.Exists(up) && !Directory.Exists(bad), "old downloads are tidied, the one in use stays");
            var mf = Path.Combine(BrowserSettings.TestDir, "mf"); Directory.CreateDirectory(Path.Combine(mf, "rules"));
            File.WriteAllText(Path.Combine(mf, "manifest.json"), "{\"name\":\"Hallow Shield\",\"version\":\"1.6\",\"declarative_net_request\":{\"rule_resources\":[{\"id\":\"a\",\"enabled\":true,\"path\":\"rules/a.json\"},{\"id\":\"b\",\"enabled\":false,\"path\":\"rules/b.json\"}]}}");
            File.WriteAllText(Path.Combine(mf, "rules", "a.json"), "[{\"id\":1,\"action\":{\"type\":\"block\"},\"condition\":{\"urlFilter\":\"||on.example^\"}}]");
            File.WriteAllText(Path.Combine(mf, "rules", "b.json"), "[{\"id\":1,\"action\":{\"type\":\"block\"},\"condition\":{\"urlFilter\":\"||off.example^\"}}]");
            var lr = ShieldHost.LoadRules(mf); Ok(lr.Blocks("https://on.example/") && !lr.Blocks("https://off.example/"), "only rule sets the manifest turns on");
        }

        static void ThemeTests()
        {
            var x = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("HallowBrowser.Theme.xaml")).ReadToEnd();
            var swapped = Accent.ApplyTo(x, "#22AA66");
            Ok(!swapped.Contains("F07820") && swapped.Contains("#22AA66") && swapped.Contains("#3322AA66"), "the Hallow accent replaces the theme's accent (with alpha forms)");
            Eq(Accent.ApplyTo(x, Accent.Default), x, "own accent: unchanged");
            var app = new System.Windows.Application();
            app.Resources.MergedDictionaries.Add((System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Parse(swapped));
            foreach (var k in new[] { "Bg", "Strip", "Bar", "Panel", "PanelHi", "Text", "Muted", "Lilac", "AccentBrush", "Line", "FieldBg", "Green", "Amber", "Red" })
                Ok(app.Resources[k] is System.Windows.Media.SolidColorBrush, "theme brush " + k);
            Ok(app.Resources["PageBg"] is System.Windows.Media.Color, "page background colour is in the theme");
            // no colour literals in the window's code: every colour comes from the theme
            var srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "src");
            if (Directory.Exists(srcDir))
                foreach (var f in new[] { "BrowserWindow.cs", "Ui.cs" })
                    Ok(!System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(Path.Combine(srcDir, f)), "\"#[0-9A-Fa-f]{6}\""), f + ": no hard-coded colours");
            Directory.CreateDirectory(Accent.Folder);
            File.WriteAllText(Accent.FilePath, "{\"accent\":\"#12ab34\"}"); Eq(Accent.Read(), "#12AB34", "theme.json accent read");
            File.WriteAllText(Accent.FilePath, "{\"accent\":\"red\"}"); Eq(Accent.Read(), null, "bad accent ignored");
            File.Delete(Accent.FilePath);
        }

        static void FamilyTests()
        {
            Ok(Family.PipeName("Browser").Contains(".test-"), "test pipes never reach the real apps");
            var ping = Program.Command(null, "ping");
            Ok(ping.StartsWith("ok {") && ping.Contains("\"app\":\"Browser\"") && ping.Contains("\"version\":\"" + Program.Version + "\""), "ping");
            Ok(Program.Command(null, "open javascript:alert(1)").StartsWith("error"), "open: javascript: refused");
            Ok(Program.Command(null, "open file:///C:/Windows/win.ini").StartsWith("error"), "open: file: refused");
            Ok(Program.Command(null, "open ftp://x.com/").StartsWith("error"), "open: ftp refused");
            Eq(Program.Command(null, "frobnicate"), "error unknown command", "unknown command");
        }

        // the Keys protocol against a stand-in Keys (fake logins, a test pipe)
        static void KeysPipeTests()
        {
            var fake = new FakeKeys(); fake.Start();
            try
            {
                Eq(KeysLink.State(), "ready", "Keys stand-in: ready");
                var m = KeysLink.Match("http://127.0.0.1:5555/login");
                Ok(m.Count == 1 && m[0].Username == "tester", "match: the login for that site (no password in a match)");
                Eq(KeysLink.Match("http://localhost:5555/login").Count, 0, "match: nothing for another site");
                Ok(!fake.Log.Any(l => l.Contains("fake-PW")), "the stand-in never saw a password requested by a match");
                fake.Locked = true; Eq(KeysLink.State(), "locked", "locked Keys"); Eq(KeysLink.Match("http://127.0.0.1:5555/login").Count, 0, "locked: no logins");
                fake.Locked = false;
                File.WriteAllText(KeysLink.SecretFile, "00112233"); Eq(KeysLink.Match("http://127.0.0.1:5555/login").Count, 0, "wrong secret: refused by Keys");
                fake.WriteSecret();
                fake.Stop(); Thread.Sleep(200);
                Eq(KeysLink.State(), "off", "Keys not running");
            }
            finally { fake.Stop(); }
        }

        static void NewTabTests()
        {
            var h = Engine.NewTabHtml("brave", "#F07820");
            Ok(h.Contains("https://search.brave.com/search?q=") && h.Contains("Brave Search"), "new tab page searches the chosen engine");
            Ok(!h.Contains("http://") && !System.Text.RegularExpressions.Regex.IsMatch(h, "src=|href=\"http"), "new tab page loads nothing from the network");
        }
    }
}
