// Hallow Browser 1.5 - unit tests: the calculator and unit conversions, search keywords, tab search, the engine suggestions' format
// and address (a stand-in in tests), login messages and saving through a stand-in Hallow Keys (fake logins only), mouse gesture
// actions, Defender's answers and arguments, the download limit maths and the downloads list, media state, dark-page site rules,
// crash detection, private flyouts and the new badges' contrast in light, dark and private looks, and 1.5's settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowBrowser.Tests
{
    public static class V15
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All()
        {
            CalcTests(); ConvertTests(); KeywordTests(); TabSearchTests(); SuggestTests(); LoginTests(); GestureTests(); DefenderTests(); DownloadTests(); MediaTests(); DarkTests(); CrashTests(); LookTests(); SettingsTests();
        }

        static void CalcTests()
        {
            Eq(Calc.Answer("200*1.2"), "240", "calc: 200*1.2 = 240");
            Eq(Calc.Answer("2+3*4"), "14", "calc: precedence");
            Eq(Calc.Answer("(2+3)*4"), "20", "calc: brackets");
            Eq(Calc.Answer("2^10"), "1,024", "calc: powers (with a thousands separator)");
            Eq(Calc.Answer("-5+2"), "-3", "calc: a minus sign");
            Eq(Calc.Answer("10/4"), "2.5", "calc: division");
            Eq(Calc.Answer("80*15%"), "12", "calc: a percentage (80*15%)");
            Eq(Calc.Answer("10 % 3"), "1", "calc: remainder with spaces");
            Eq(Calc.Answer("sqrt(16)+1"), "5", "calc: sqrt");
            Eq(Calc.Answer("= 7 x 6"), "42", "calc: = and x for times");
            Eq(Calc.Answer("1/0"), null, "calc: dividing by zero gives no answer");
            Eq(Calc.Answer("42"), null, "calc: a lone number is not a sum");
            Eq(Calc.Answer("cats"), null, "calc: words are not sums");
            Eq(Calc.Answer("example.com"), null, "calc: an address is not a sum");
            Eq(Calc.Answer("2024 cats - dogs"), null, "calc: words with a minus are not sums");
            Eq(Calc.Answer("1+"), null, "calc: an unfinished sum gives nothing");
            Eq(Calc.Answer("0.1+0.2"), "0.3", "calc: 0.1+0.2 shows 0.3");
        }
        static void ConvertTests()
        {
            double v;
            Eq(Calc.Convert("5 km in miles", out v), "3.10686 miles", "units: 5 km in miles");
            Eq(Calc.Convert("10 miles to km", out v), "16.09344 km", "units: 10 miles to km");
            Eq(Calc.Convert("72 f to c", out v), "22.22222 \u00b0C", "units: 72 F to C");
            Eq(Calc.Convert("100 c in f", out v), "212 \u00b0F", "units: 100 C in F");
            Eq(Calc.Convert("0 c in k", out v), "273.15 K", "units: 0 C in K");
            Eq(Calc.Convert("1 kg in lbs", out v), "2.20462 lb", "units: 1 kg in lbs");
            Eq(Calc.Convert("3 gb in mib", out v), "2,861.02 MiB", "units: 3 GB in MiB");
            Eq(Calc.Convert("60 mph to km/h", out v), "96.56064 km/h", "units: 60 mph to km/h");
            Eq(Calc.Convert("90 min in hours", out v), "1.5 h", "units: 90 min in hours");
            Eq(Calc.Convert("6 ft in cm", out v), "182.88 cm", "units: 6 ft in cm");
            Eq(Calc.Convert("5 km in kg", out v), null, "units: length to mass is refused");
            Eq(Calc.Convert("5 parsecs in km", out v), null, "units: unknown units give nothing");
            Eq(Calc.Answer("5 km in miles"), "3.10686 miles", "address bar answer: a conversion");
        }
        static void KeywordTests()
        {
            var d = Keywords.Defaults(); SearchKeyword used;
            Eq(Keywords.Expand(d, "yt cats", out used), "https://www.youtube.com/results?search_query=cats", "keywords: yt cats -> YouTube");
            Eq(used.Name, "YouTube", "keywords: the keyword used is named");
            Eq(Keywords.Expand(d, "w hallow knight", out used), "https://en.wikipedia.org/wiki/Special:Search?search=hallow%20knight", "keywords: w term -> Wikipedia (words escaped)");
            Eq(Keywords.Expand(d, "gh hallow-browser", out used), "https://github.com/search?q=hallow-browser", "keywords: gh repo -> GitHub");
            Eq(Keywords.Expand(d, "YT Cats", out used), "https://www.youtube.com/results?search_query=Cats", "keywords: the keyword is case-insensitive");
            Eq(Keywords.Expand(d, "yt", out used), null, "keywords: a keyword alone is not a search");
            Eq(Keywords.Expand(d, "youtube cats", out used), null, "keywords: other words are not keywords");
            Eq(Keywords.Expand(d, "w.org", out used), null, "keywords: an address starting with the letters isn't one");
            Ok(Keywords.ValidUrl("https://www.reddit.com/search/?q=%s") && !Keywords.ValidUrl("https://www.reddit.com/") && !Keywords.ValidUrl("javascript:alert(%s)") && !Keywords.ValidUrl("file:///c:/%s"), "keywords: only http(s) addresses with %s");
            Eq(Keywords.Clean(" R "), "r", "keywords: cleaned to lower case"); Eq(Keywords.Clean("two words"), null, "keywords: one word only");
            var bad = new List<SearchKeyword> { new SearchKeyword { Key = "x", Url = "javascript:%s" } };
            Eq(Keywords.Expand(bad, "x y", out used), null, "keywords: a bad address is never used");
        }
        static void TabSearchTests()
        {
            Eq(TabSearch.Query("% news"), "news", "tab search: % name"); Eq(TabSearch.Query("%news"), "news", "tab search: %name"); Eq(TabSearch.Query("news"), null, "tab search: not without %");
            Ok(TabSearch.Matches("delta guide", "Delta Force - season guide", "https://x.example/") && !TabSearch.Matches("delta wiki", "Delta Force - season guide", "https://x.example/") && TabSearch.Matches("example", "A", "https://x.example/"), "tab search: every word in the title or address");
        }
        static void SuggestTests()
        {
            Eq(string.Join("|", Suggestions.Parse("[\"cat\",[\"cats\",\"cat videos\",\"cats\",\"\"]]", 5)), "cats|cat videos", "suggestions: OpenSearch JSON (duplicates and blanks dropped)");
            Eq(Suggestions.Parse("{\"bad\":1}", 5).Count, 0, "suggestions: anything else gives nothing");
            Eq(Suggestions.Parse("not json", 5).Count, 0, "suggestions: broken text gives nothing");
            // in tests the address is the stand-in's (HALLOW_BROWSER_SUGGEST_URL isn't set in unit tests: the real addresses' shapes)
            Ok(BrowserSettings.SuggestTest != null || (Suggestions.Url("duckduckgo", "a b").StartsWith("https://duckduckgo.com/ac/?type=list&q=a%20b") && Suggestions.Url("google", "x").StartsWith("https://suggestqueries.google.com/") && Suggestions.Url("bing", "x").StartsWith("https://api.bing.com/osjson.aspx") && Suggestions.Url("brave", "x").StartsWith("https://search.brave.com/api/suggest")), "suggestions: each engine's own suggestion address");
            var s = new BrowserSettings(); s.Fix(); Ok(!s.SearchSuggest, "suggestions: OFF by default");
        }
        static void LoginTests()
        {
            var l = KeysLink.ParseLogin("{\"u\":\"me@example.com\",\"p\":\"fake-1\"}"); Ok(l != null && l.Item1 == "me@example.com" && l.Item2 == "fake-1", "save logins: a login message is read");
            Ok(KeysLink.ParseLogin("{\"u\":\"x\",\"p\":\"\"}") == null && KeysLink.ParseLogin("{\"u\":\"x\"}") == null && KeysLink.ParseLogin("{\"p\":\"" + new string('a', 1001) + "\"}") == null && KeysLink.ParseLogin("nope") == null, "save logins: no password / a too long one / junk are refused");
            Ok(KeysLink.ParseLogin("{\"p\":\"x\"}").Item1 == "", "save logins: a password without a username is fine");
            var cs = KeysLink.CaptureScript;
            Ok(cs.Contains("window.top!==window") && cs.Contains("isTrusted") && cs.Contains("input[type=password]") && cs.Contains("hallow:login") && !cs.Contains("localStorage") && !cs.Contains("fetch("), "save logins: the capture script is top-frame only, real events only, posts only to the browser");
            // the protocol against a stand-in Keys (made-up login, the test pipe - never the real Keys)
            var keys = new FakeKeys(); keys.SavedUrl = "https://login.example"; keys.Start();
            try
            {
                Eq(KeysLink.SaveCheck("https://login.example/signin", FakeKeys.User, FakeKeys.Pass), "same", "save logins: Keys says the stand-in's own login is already saved");
                Eq(KeysLink.SaveCheck("https://login.example/signin", FakeKeys.User, "fake-new"), "update", "save logins: a changed password = update");
                Eq(KeysLink.SaveCheck("https://other.example/", "someone", "fake-x"), "new", "save logins: another site = new");
                Eq(KeysLink.Save("https://other.example/", "someone", "fake-x"), "new", "save logins: saved after the click");
                Ok(keys.Saved.Count == 1 && keys.Saved[0].Item1 == "https://other.example" && keys.Saved[0].Item2 == "someone", "save logins: Keys got the site (origin) and username");
                Eq(KeysLink.SaveCheck("https://other.example/", "someone", "fake-x"), "same", "save logins: then it's the same");
                Eq(KeysLink.SaveCheck("javascript:alert(1)", "a", "b"), "no", "save logins: never for a page that isn't http(s)");
                keys.Locked = true; Eq(KeysLink.SaveCheck("https://other.example/", "someone", "fake-y"), "locked", "save logins: Keys locked = locked");
                Ok(!keys.Log.Any(x => x.Contains("fake-")), "save logins: the stand-in's log never holds a password");
            }
            finally { keys.Stop(); }
            Thread(300);
            Eq(KeysLink.SaveCheck("https://other.example/", "someone", "fake-y"), "off", "save logins: Keys not running = off (nothing offered)");
        }
        static void Thread(int ms) { System.Threading.Thread.Sleep(ms); }
        static void GestureTests()
        {
            Eq(Gestures.Action("L"), "back", "gestures: left = back"); Eq(Gestures.Action("R"), "forward", "gestures: right = forward"); Eq(Gestures.Action("DR"), "close", "gestures: down then right = close");
            Ok(Gestures.Action("U") == null && Gestures.Action("RL") == null && Gestures.Action("") == null, "gestures: anything else does nothing");
            Ok(Gestures.Script.Contains("isTrusted") && Gestures.Script.Contains("contextmenu") && Gestures.Script.Contains("window.top!==window"), "gestures: real right-drags only, and the menu stays shut after a gesture");
            var s = new BrowserSettings(); s.Fix(); Ok(!s.Gestures && !s.AutoPip && s.TabPreviews, "gestures and auto pop-out off by default; previews on");
        }
        static void DefenderTests()
        {
            string n;
            Eq(Defender.Parse(0, new[] { "Scan starting...", "Scan finished.", "Scanning C:\\x found no threats." }, out n), "clean", "Defender: exit 0 = clean");
            Eq(Defender.Parse(2, new[] { "Scan starting...", "Threat                  : Virus:DOS/EICAR_Test_File", "found 1 threats." }, out n), "threat", "Defender: exit 2 = threat");
            Eq(n, "Virus:DOS/EICAR_Test_File", "Defender: the threat's name");
            Eq(Defender.Parse(5, new string[0], out n), "error", "Defender: any other code = not checked");
            Eq(Defender.Args(@"C:\Users\x\Downloads\a b.zip"), "-Scan -ScanType 3 -File \"C:\\Users\\x\\Downloads\\a b.zip\"", "Defender: a custom scan of the one file");
            try { Defender.Args("C:\\a\"b"); Ok(false, "Defender: a quote in a path is refused"); } catch (ArgumentException) { Ok(true, "Defender: a quote in a path is refused"); }
            Ok(BrowserSettings.TestDir == null || Defender.Find() == BrowserSettings.MpCmdRunTest, "Defender: tests use a stand-in only (never the real scanner)");
        }
        static void DownloadTests()
        {
            Ok(DownloadLimit.ShouldPause(300000, 0, 1.0, 262144) && !DownloadLimit.ShouldPause(200000, 0, 1.0, 262144), "download limit: ahead of the limit = pause, behind = go");
            Eq(DownloadLimit.Each(1024, 2), 524288.0, "download limit: two downloads share the limit");
            Eq(DownloadLimit.Each(0, 2), 0.0, "download limit: off");
            Eq(DownloadLimit.Name(-1), "Pause them", "download limit: -1 = pause while gaming"); Eq(DownloadLimit.Name(4096), "4 MB/s", "download limit: names");
            DownloadHistory.Clear();
            DownloadHistory.Add(new DownloadRecord { Name = "a.zip", File = @"C:\x\a.zip", State = "done", Scan = "clean", Size = 10 });
            var f = DownloadHistory.FilePath; Ok(File.Exists(f) && File.ReadAllText(f).Contains("a.zip") && !File.ReadAllText(f).Contains("http"), "downloads list: saved (file names and folders, never addresses)");
            var field = typeof(DownloadHistory).GetField("items", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); field.SetValue(null, null);
            Ok(DownloadHistory.Items.Count == 1 && DownloadHistory.Items[0].Scan == "clean", "downloads list: read back");
            DownloadHistory.Clear(); Ok(!File.Exists(f) && DownloadHistory.Items.Count == 0, "downloads list: cleared (with Clear browsing data)");
            Ok(Omni.IsInternal(Omni.DownloadsUrl) && Omni.Interpret("hallow://downloads", "duckduckgo") == Omni.DownloadsUrl, "downloads page: hallow://downloads");
        }
        static void MediaTests()
        {
            var st = Media.Parse("\"{\\\"n\\\":2,\\\"playing\\\":true,\\\"video\\\":true,\\\"next\\\":true,\\\"prev\\\":false,\\\"title\\\":\\\"Song - Band\\\"}\"");
            Ok(st != null && st.N == 2 && st.Playing && st.Video && st.Next && !st.Prev && st.Title == "Song - Band", "media: the page's state is read");
            Ok(Media.Parse("null") == null && Media.Parse("junk") == null, "media: no answer = nothing");
            Ok(Media.Script.Contains("setActionHandler") && Media.Script.Contains("nexttrack") && Media.Script.Contains("window.top!==window"), "media: the page's own next / previous are used");
        }
        static void DarkTests()
        {
            Ok(SiteRules.Empty(new SiteRule { Host = "a" }) && !SiteRules.Empty(new SiteRule { Host = "a", Dark = "on" }), "dark pages: a site's own choice is a site rule");
            var ser = new DataContractJsonSerializer(typeof(SiteRule)); var ms = new MemoryStream(); ser.WriteObject(ms, new SiteRule { Host = "x.example", Dark = "off" }); ms.Position = 0;
            Eq(((SiteRule)ser.ReadObject(ms)).Dark, "off", "dark pages: saved with the site's rules");
        }
        static void CrashTests()
        {
            var marker = Program.RunningMarker; try { File.Delete(marker); } catch { }
            var s = new BrowserSettings(); s.Fix(); s.RestoreSession = false;
            BrowserSettings.SaveSession(new Session { Tabs = new List<SessionTab> { new SessionTab { Url = "https://a.example/" }, new SessionTab { Url = "https://b.example/" } } });
            Program.MarkRunning(s); Ok(!Program.CrashedLastTime && File.Exists(marker), "crash recovery: a first start isn't a crash; the marker is written");
            Program.MarkRunning(s); Ok(Program.CrashedLastTime && Program.CrashedSession != null && Program.CrashedSession.Tabs.Count == 2, "crash recovery: the marker still there = it didn't close properly; the tabs from then are kept for \"Restore your tabs\"");
            Program.MarkClosed(); Program.MarkRunning(s); Ok(!Program.CrashedLastTime, "crash recovery: a normal exit removes the marker");
            s.RestoreSession = true; Program.CrashedSession = null; Program.MarkRunning(s); Ok(Program.CrashedLastTime && Program.CrashedSession == null, "crash recovery: with session restore on the tabs come back by themselves");
            Program.MarkClosed(); Program.CrashedLastTime = false; BrowserSettings.DeleteSession();
        }
        static void LookTests()
        {
            // the private palette on a flyout's own resources
            var rd = new ResourceDictionary(); PrivateFlyouts.Apply(rd);
            Ok(rd.Contains("Panel") && ((SolidColorBrush)rd["Panel"]).Color == (Color)ColorConverter.ConvertFromString(BrowserWindow.PrivatePalette["Panel"]) && rd.Contains("HallowPrivate"), "private flyouts: the private palette goes on a flyout's resources");
            var b = new Border(); PrivateFlyouts.Apply(b.Resources); b.SetResourceReference(Border.BackgroundProperty, "Panel");
            Ok(b.Background is SolidColorBrush && ((SolidColorBrush)b.Background).Color.ToString().EndsWith(BrowserWindow.PrivatePalette["Panel"].TrimStart('#')), "private flyouts: a flyout's background resolves to the private panel colour");
            // the new badges and notes: Defender clean / threat / not checked, the info bar, as small text
            var p = BrowserWindow.PrivatePalette;
            string dPanel = "#1C1726", dBg = "#14101B";
            foreach (var fg in new[] { new[] { "GreenText", "#6ED28C", Look.LGreenText }, new[] { "Red", "#E05A64", Look.LRed }, new[] { "AmberText", "#F0BE5A", Look.LAmberText } })
            {
                var cd = Math.Min(Look.Contrast(fg[1], dPanel), Look.Contrast(fg[1], dBg)); var cl = Math.Min(Look.Contrast(fg[2], Look.LPanel), Look.Contrast(fg[2], Look.LBg)); var cp = Math.Min(Look.Contrast(fg[1], p["Panel"]), Look.Contrast(fg[1], p["Bg"]));
                Ok(cd >= 4.5 && cl >= 4.5 && cp >= 4.5, "contrast: " + fg[0] + " badge text - dark " + cd.ToString("0.00") + ", light " + cl.ToString("0.00") + ", private " + cp.ToString("0.00") + " (all >= 4.5:1)");
            }
            Ok(Icons.Has("calculator") && Icons.Has("forward-step") && Icons.Has("circle-check") && Icons.Has("shield-virus") && Icons.Has("heart-crack"), "1.5 icons are there");
        }
        static void SettingsTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(s.Keywords.Count == 3 && s.SaveLogins && s.DownloadScan && s.DownloadGameKBps == 0 && !s.ForceDark && s.NeverSave.Count == 0, "1.5 defaults: yt / w / gh, offer to save logins, Defender checks on, no download limit, dark pages off");
            var ser = new DataContractJsonSerializer(typeof(BrowserSettings));
            var old = (BrowserSettings)ser.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes("{\"Search\":\"bing\"}"))); old.Fix();
            Ok(old.SaveLogins && old.DownloadScan && old.TabPreviews && old.Keywords.Count == 3 && !old.SearchSuggest, "1.5 defaults also for a 1.4 settings.json");
            var odd = (BrowserSettings)ser.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes("{\"Keywords\":[null,{\"Key\":\"bad key\",\"Url\":\"https://a.example/?q=%s\"},{\"Key\":\"ok\",\"Url\":\"javascript:%s\"},{\"Key\":\"R\",\"Url\":\"https://r.example/?q=%s\"},{\"Key\":\"r\",\"Url\":\"https://dup.example/?q=%s\"}],\"DownloadGameKBps\":77,\"NeverSave\":[\"A.Example.\",\"a.example\",\"\"]}"))); odd.Fix();
            Ok(odd.Keywords.Count == 1 && odd.Keywords[0].Key == "r" && odd.Keywords[0].Url.StartsWith("https://r.example") && odd.DownloadGameKBps == 0 && odd.NeverSave.Count == 1 && odd.NeverSave[0] == "a.example", "1.5 settings: bad keywords dropped (one per word), an unknown limit = none, never-save sites tidied");
        }
    }
}
