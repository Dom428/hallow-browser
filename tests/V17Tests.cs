// Hallow Browser 1.7 - unit tests: translation (languages, server addresses, batches, the exact requests each service gets and
// parsing their answers - no network here), the API keys kept with DPAPI, nothing preset; the settings page's sections and search
// matching; the jump list's items and --jump; Shield's My filters and history through sync between two test PCs (history only ever
// encrypted, its size cap, no tombstones), and the contrast of the new text in the dark, light and private looks.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class V17
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static string root;

        public static void All()
        {
            root = BrowserSettings.TestDir; BrowserSettings.ProfileId = ProfileStore.DefaultId;
            TranslateTests(); KeyTests(); SettingsTests(); JumpTests(); FiltersSyncTests(); HistorySyncTests(); ContrastTests();
        }

        static void TranslateTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Ok(s.TranslateProvider == "" && !Translator.Ready(s) && Translator.ProviderName(s) == null, "translate: nothing preset - no service, not ready, nothing to send to");
            Eq(Translator.Primary("en-GB"), "en", "translate: language en-GB -> en"); Eq(Translator.Primary("PT_br"), "pt", "translate: PT_br -> pt"); Eq(Translator.Primary("no"), "nb", "translate: Norwegian");
            Ok(Translator.Primary("") == null && Translator.Primary("x") == null && Translator.Primary("e1") == null, "translate: junk languages are ignored");
            Eq(Translator.CleanServer("192.168.1.20:5000"), "http://192.168.1.20:5000", "translate: a NAS address without http:// -> http://");
            Eq(Translator.CleanServer("https://lt.example.com/translate/"), "https://lt.example.com", "translate: /translate at the end is dropped");
            Eq(Translator.CleanServer("https://lt.example.com/api"), "https://lt.example.com/api", "translate: a server under a path is kept");
            Ok(Translator.CleanServer("ftp://x") == null && Translator.CleanServer("https://u:p@x.com") == null && Translator.CleanServer("https://x.com/?k=1") == null && Translator.CleanServer("") == null, "translate: no ftp, no user:password@, no query");
            s.TranslateProvider = "libre"; s.TranslateLibreUrl = null; s.Fix(); Eq(s.TranslateProvider, "", "translate: LibreTranslate without a server address isn't kept");
            s.TranslateProvider = "bogus"; s.Fix(); Eq(s.TranslateProvider, "", "translate: an unknown service isn't kept");
            s.TranslateAlways = new List<string> { "FR-fr", "fr", "de", "??" }; s.Fix(); Eq(string.Join(",", s.TranslateAlways), "fr,de", "translate: \"always translate\" keeps clean languages once");
            // batches: at most 50 texts / ~4,500 characters
            var many = Enumerable.Range(0, 120).Select(i => "text " + i).ToList(); var b = Translator.Batches(many);
            Ok(b.Count == 3 && b[0].Count == 50 && b[2].Count == 20 && b.SelectMany(x => x).SequenceEqual(Enumerable.Range(0, 120)), "translate: 120 texts -> 50 + 50 + 20, in order");
            var big = new List<string> { new string('a', 3000), new string('b', 3000), new string('c', 9000), "d" }; var bb = Translator.Batches(big);
            Ok(bb.Count == 4 && bb.All(x => x.Count == 1), "translate: long texts go in batches of their own (" + bb.Count + ")");
            // the requests
            var texts = new List<string> { "Bonjour", "le monde" };
            var lr = Translator.Request("libre", "http://nas:5000", null, texts, "en"); var lb = js.Deserialize<Dictionary<string, object>>(lr.Item3);
            Ok(lr.Item1 == "http://nas:5000/translate" && lr.Item2.Count == 0 && (string)lb["source"] == "auto" && (string)lb["target"] == "en" && (string)lb["format"] == "text" && ((System.Collections.ArrayList)lb["q"]).Count == 2 && !lb.ContainsKey("api_key"), "translate: LibreTranslate request = POST <server>/translate {q, source auto, target, format text}");
            var lr2 = Translator.Request("libre", "http://nas:5000", "k123", texts, "en"); Ok(lr2.Item3.Contains("\"api_key\":\"k123\""), "translate: ... with api_key only when you set one");
            var dr = Translator.Request("deepl", null, "abcd-1234:fx", texts, "en");
            Ok(dr.Item1 == "https://api-free.deepl.com/v2/translate" && dr.Item2["Authorization"] == "DeepL-Auth-Key abcd-1234:fx" && dr.Item3.Contains("\"target_lang\":\"EN-US\"") && dr.Item3.Contains("\"text\":[\"Bonjour\",\"le monde\"]"), "translate: DeepL Free key -> api-free.deepl.com, key in the Authorization header (never the address), target EN-US");
            Ok(Translator.Request("deepl", null, "abcd-1234", texts, "de").Item1 == "https://api.deepl.com/v2/translate" && Translator.DeepLTarget("pt") == "PT-PT" && Translator.DeepLTarget("zh") == "ZH-HANS", "translate: DeepL Pro key -> api.deepl.com; PT / ZH targets");
            var gr = Translator.Request("google", null, "AIzaTEST", texts, "en");
            Ok(gr.Item1 == "https://translation.googleapis.com/language/translate/v2" && gr.Item2["X-Goog-Api-Key"] == "AIzaTEST" && !gr.Item1.Contains("AIza") && gr.Item3.Contains("\"format\":\"text\""), "translate: Google v2 with the key in X-Goog-Api-Key (not in the address)");
            // answers
            var p1 = Translator.Parse("libre", "{\"translatedText\":[\"Hello\",\"the world\"],\"detectedLanguage\":[{\"confidence\":90,\"language\":\"fr\"}]}", 2);
            Ok(p1.Texts.SequenceEqual(new[] { "Hello", "the world" }) && p1.Detected == "fr", "translate: LibreTranslate answer (array + detected language)");
            var p2 = Translator.Parse("deepl", "{\"translations\":[{\"detected_source_language\":\"FR\",\"text\":\"Hello\"},{\"detected_source_language\":\"FR\",\"text\":\"the world\"}]}", 2);
            Ok(p2.Texts.SequenceEqual(new[] { "Hello", "the world" }) && p2.Detected == "fr", "translate: DeepL answer");
            var p3 = Translator.Parse("google", "{\"data\":{\"translations\":[{\"translatedText\":\"Tom &amp; Jerry\",\"detectedSourceLanguage\":\"fr\"},{\"translatedText\":\"x\"}]}}", 2);
            Ok(p3.Texts[0] == "Tom & Jerry" && p3.Detected == "fr", "translate: Google answer (entities decoded)");
            bool threw = false; try { Translator.Parse("libre", "{\"translatedText\":[\"only one\"]}", 2); } catch (InvalidDataException) { threw = true; }
            Ok(threw, "translate: an answer with the wrong number of texts is refused (nothing half-applied)");
            threw = false; try { Translator.Parse("libre", "{\"error\":\"Invalid API key\"}", 1); } catch (InvalidDataException e) { threw = e.Message.Contains("Invalid API key"); }
            Ok(threw, "translate: the service's own error is shown");
            Ok(Translator.ApplyScript(5, new[] { "a\"b" }, "en").Contains("(5,[\"a\\\"b\"])") && Translator.CollectScript.Contains("translate=no") && Translator.CollectScript.Contains("textarea"), "translate: page scripts escape the text; form fields and translate=no are skipped");
            Ok(Translator.LanguageName("de") == "German" && Translator.Languages.Length >= 30 && Translator.Target(new BrowserSettings { TranslateTo = "fr" }) == "fr", "translate: target languages");
        }
        static void KeyTests()
        {
            var f = Path.Combine(BrowserSettings.LocalDir, "translate-keys.bin");
            Translator.Keys.Set("deepl", "secret-deepl-key-1234:fx");
            Ok(File.Exists(f) && !Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains("secret-deepl") && !Encoding.Unicode.GetString(File.ReadAllBytes(f)).Contains("secret-deepl"), "translate keys: kept in translate-keys.bin, protected by Windows (not readable in the file)");
            Eq(Translator.Keys.Get("deepl"), "secret-deepl-key-1234:fx", "translate keys: read back for this user");
            Eq(Translator.Keys.Masked("deepl"), "****4:fx", "translate keys: shown masked");
            var s = new BrowserSettings(); s.Fix(); s.TranslateProvider = "deepl"; Ok(Translator.Ready(s) && Translator.ProviderName(s) == "DeepL", "translate: ready once a service AND its key are set");
            var json = new StringBuilder(); using (var ms = new MemoryStream()) { new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(BrowserSettings)).WriteObject(ms, s); json.Append(Encoding.UTF8.GetString(ms.ToArray())); }
            Ok(!json.ToString().Contains("secret-deepl") && !SyncData.SettingKeys.Any(k => k.StartsWith("Translate")), "translate keys: never in settings.json, translation settings never synced");
            Translator.Keys.Set("deepl", null); Ok(!File.Exists(f) && Translator.Keys.Get("deepl") == null, "translate keys: Forget key deletes the file");
            s.TranslateProvider = "libre"; s.TranslateLibreUrl = "http://127.0.0.1:5000"; Ok(Translator.Ready(s) && Translator.ProviderName(s) == "127.0.0.1:5000", "translate: LibreTranslate is ready with just a server");
        }
        static void SettingsTests()
        {
            Ok(SettingsLayout.Sections.SequenceEqual(new[] { "General", "Privacy & security", "Shield", "Gaming", "Appearance", "Sync", "Profiles", "Advanced" }), "settings: the eight sections in order");
            var known = new[] { "Search", "Address bar", "Downloads", "Translate pages", "Cast to a TV", "Privacy", "Security", "Hallow Keys", "Hallow Shield", "Speed and gaming", "Game corner", "Look", "Taskbar", "Sync", "Profiles", "About" };
            var expect = new[] { "General", "General", "General", "General", "General", "Privacy & security", "Privacy & security", "Privacy & security", "Shield", "Gaming", "Gaming", "Appearance", "Appearance", "Sync", "Profiles", "Advanced" };
            Ok(known.Select(SettingsLayout.SectionOf).SequenceEqual(expect), "settings: every card has its section");
            Eq(SettingsLayout.SectionOf("Something new"), "Advanced", "settings: an unknown card goes to Advanced");
            Ok(SettingsLayout.Matches("Block third-party cookies", "cookies") && SettingsLayout.Matches("Block third-party cookies", "BLOCK cook") && !SettingsLayout.Matches("Block third-party cookies", "cookies sync") && SettingsLayout.Matches("x", "  "), "settings: search = every word somewhere, any case");
        }
        static void JumpTests()
        {
            var ps = new ProfileStore(); ps.Items.Add(new ProfileInfo { Id = "default", Name = "Default" }); ps.Items.Add(new ProfileInfo { Id = "gaming", Name = "Gaming" });
            var it = JumpList.Items(@"C:\x\HallowBrowser.exe", ps);
            Ok(it.Count == 5 && it[0].Args == "--jump new-tab" && it[1].Args == "--jump private-window" && it[2].Args == "--jump reading-list" && it[4].Args == "--profile gaming" && it[4].Category == "Profiles" && it[0].Category == null, "jump list: New tab, New private window, Reading list, then each profile");
            Ok(Program.JumpInvoke("new-tab") == "new-tab" && Program.JumpInvoke("PRIVATE-WINDOW") == "private-window" && Program.JumpInvoke("reading-list") == "reading-list" && Program.JumpInvoke("https://evil.example") == null && Program.JumpInvoke(null) == null, "jump list: --jump only knows its three items");
            string prof, app; List<string> rest; Program.ParseArgs(new[] { "--profile", "gaming" }, out prof, out app, out rest); Ok(prof == "gaming" && rest.Count == 0, "jump list: a profile item is the usual --profile");
        }

        // ---- Shield's My filters through sync between two test PCs (the browser hands them over; Shield itself is in the integration test)
        class Pc { public BrowserSettings S; public string Filters; public HistoryStore H; public SyncState St = new SyncState(); public string In; public string Name; }
        static Pc NewPc(string name) { var s = new BrowserSettings(); s.Fix(); var dir = Path.Combine(root, "pc-" + name); Directory.CreateDirectory(dir); var h = new HistoryStore(Path.Combine(dir, "h.json")); h.Load(); return new Pc { S = s, H = h, Name = name }; }
        static SyncOutcome Sync(Pc p, string folder, string pw, bool hist, int max = 1000)
        {
            var input = new SyncInput { Settings = p.S, ShieldFilters = p.Filters, History = hist ? p.H : null, HistoryMax = hist ? max : 0 };
            var o = SyncCore.Run(SyncData.Gather(input), p.St, folder, new SyncOptions { Dev = p.S.SyncDevice, DevName = p.Name, Password = pw, CopyData = SyncData.CopyData, KeepDir = root, HistoryMax = hist && pw != null ? max : 0 });
            if (o.State2 != null) { if (o.ChangedKinds.Count > 0) SyncData.Apply(input, o.Result, o.ChangedKinds); p.St = o.State2; }
            p.In = input.ShieldFiltersIn; if (p.In != null) p.Filters = p.In;
            return o;
        }
        static void FiltersSyncTests()
        {
            SyncCrypto.Iterations = 2000;
            var folder = Path.Combine(root, "SyncFilters"); Directory.CreateDirectory(folder);
            var A = NewPc("A"); var B = NewPc("B");
            A.Filters = "||ads.example^\nexample.com##.banner\n"; B.Filters = "";   // B's Shield is there, with no filters yet
            Sync(A, folder, null, false); Sync(B, folder, null, false);
            Ok(B.In == A.Filters, "My filters: PC A's filters arrive on PC B (to be written into Shield there)");
            B.Filters += "news.example##.popup\n"; Sync(B, folder, null, false); Sync(A, folder, null, false);
            Ok(A.Filters.Contains("news.example##.popup"), "My filters: an edit on B comes back to A");
            var before = A.Filters; A.Filters = null;   // Shield couldn't be read on A this time
            Sync(A, folder, null, false); Sync(B, folder, null, false);
            Ok(B.Filters == before && B.In == null, "My filters: a PC that couldn't read Shield sends nothing - B's filters aren't emptied");
            var raw = File.ReadAllText(Path.Combine(folder, SyncCore.FileName)); Ok(raw.Contains("\"Kind\":\"shf\"") && !raw.Contains("\"Gone\":true"), "My filters: one record in the folder, no delete marker");
            A.Filters = ""; Sync(A, folder, null, false); Sync(B, folder, null, false);
            Ok(B.Filters == "", "My filters: emptying them on purpose does sync");
            var C = NewPc("C"); C.Filters = null; var oc = Sync(C, folder, null, false);   // filters sync off / Shield not there: they pass through, nothing to take
            Ok(C.In == null && C.Filters == null && File.ReadAllText(Path.Combine(folder, SyncCore.FileName)).Contains("\"Kind\":\"shf\""), "My filters: a PC that doesn't sync them leaves them in the folder and takes nothing");
            Ok(SyncData.Kinds.Contains("shf") && SyncData.Kinds.Contains("hist"), "sync kinds: shf and hist");
        }
        static void HistorySyncTests()
        {
            var plain = Path.Combine(root, "SyncHistPlain"); Directory.CreateDirectory(plain);
            var A = NewPc("HA"); var B = NewPc("HB");
            A.H.Add("https://raid.example/secret-plan", "Raid plan", DateTime.Now.AddMinutes(-5)); A.H.Add("https://news.example/a", "News A", DateTime.Now.AddMinutes(-4));
            Sync(A, plain, null, true);
            var raw = File.ReadAllText(Path.Combine(plain, SyncCore.FileName));
            Ok(!raw.Contains("raid.example") && !raw.Contains("\"hist\""), "history sync: never written to an unencrypted folder (no password = no history records at all)");
            var enc = Path.Combine(root, "SyncHistEnc"); Directory.CreateDirectory(enc); A.St = new SyncState(); B.St = new SyncState();
            Sync(A, enc, "correct horse", true);
            var bytes = File.ReadAllBytes(Path.Combine(enc, SyncCore.FileName));
            Ok(SyncCore.IsEncrypted(bytes) && !Encoding.UTF8.GetString(bytes).Contains("raid.example"), "history sync: with the password the folder's copy is encrypted (addresses not readable)");
            B.H.Add("https://news.example/a", "News A (B)", DateTime.Now.AddMinutes(-60));
            var ob = Sync(B, enc, "correct horse", true);
            var bh = B.H.All();
            Ok(bh.Any(x => x.Url == "https://raid.example/secret-plan" && x.Title == "Raid plan") && bh.First(x => x.Url == "https://news.example/a").Title == "News A", "history sync: A's pages arrive on B; the newer visit brings its title (" + bh.Count + " pages)");
            Sync(A, enc, "correct horse", true); var o2 = Sync(B, enc, "correct horse", true); var o3 = Sync(A, enc, "correct horse", true);
            Ok(!o2.Wrote && !o3.Wrote, "history sync: settles - nothing to write after both PCs merged (" + o2.Wrote + ", " + o3.Wrote + ")");
            // the cap: the newest N pages only
            for (int i = 0; i < 10; i++) A.H.Add("https://cap.example/" + i, "Cap " + i, DateTime.Now.AddSeconds(-20 + i));
            var cap = Path.Combine(root, "SyncHistCap"); Directory.CreateDirectory(cap); A.St = new SyncState();
            Sync(A, cap, "correct horse", true, 3);
            var doc = SyncCore.Read(File.ReadAllBytes(Path.Combine(cap, SyncCore.FileName)), "correct horse");
            var hs = doc.Records.Where(r => r.Kind == "hist").ToList();
            Ok(hs.Count == 3 && hs.All(r => r.Data.Contains("cap.example/9") || r.Data.Contains("cap.example/8") || r.Data.Contains("cap.example/7")), "history sync: the size cap keeps the newest 3 pages (" + hs.Count + ")");
            // no tombstones: a page leaving the cap, or history sync turned off, deletes nothing on the other PC
            var C = NewPc("HC"); Sync(C, cap, "correct horse", true, 3); var cn = C.H.Count;
            A.H.Add("https://cap.example/new", "Newest", DateTime.Now.AddSeconds(5)); Sync(A, cap, "correct horse", true, 3); Sync(C, cap, "correct horse", true, 3);
            doc = SyncCore.Read(File.ReadAllBytes(Path.Combine(cap, SyncCore.FileName)), "correct horse");
            Ok(C.H.All().Any(x => x.Url == "https://cap.example/7") && C.H.Count == cn + 1 && !doc.Records.Any(r => r.Kind == "hist" && r.Gone), "history sync: a page pushed out of the cap stays on the other PC (no delete markers)");
            Sync(A, cap, "correct horse", false); doc = SyncCore.Read(File.ReadAllBytes(Path.Combine(cap, SyncCore.FileName)), "correct horse");
            Ok(doc.Records.Count(r => r.Kind == "hist") == 3, "history sync: a PC with history sync off leaves the other PCs' (encrypted) pages alone");
            A.H.Clear(); Sync(A, cap, "correct horse", true, 3); Sync(C, cap, "correct horse", true, 3);
            Ok(C.H.Count >= 3, "history sync: clearing history on one PC doesn't clear the others");
            Ok(SyncHistory.IdOf("https://a.example/") == SyncHistory.IdOf("https://a.example/") && SyncHistory.IdOf("https://a.example/") != SyncHistory.IdOf("https://b.example/"), "history sync: one record per address, the same id on every PC");
        }
        static void ContrastTests()
        {
            var p = BrowserWindow.PrivatePalette;
            // status lines (green / amber text) and muted help text on cards; section titles (Text) on the page; flyout text
            var pairs = new[] {
                Tuple.Create("dark GreenText on Panel", Look.Contrast("#6ED28C", "#1C1726")), Tuple.Create("dark AmberText on Panel", Look.Contrast("#F0BE5A", "#1C1726")), Tuple.Create("dark Muted on Panel", Look.Contrast("#9790A8", "#1C1726")), Tuple.Create("dark Text on Bg", Look.Contrast("#ECE8F4", "#14101B")), Tuple.Create("dark Muted on FieldBg (search hint)", Look.Contrast("#9790A8", "#110E17")),
                Tuple.Create("light GreenText on Panel", Look.Contrast(Look.LGreenText, Look.LPanel)), Tuple.Create("light AmberText on Panel", Look.Contrast(Look.LAmberText, Look.LPanel)), Tuple.Create("light Muted on Panel", Look.Contrast(Look.LMuted, Look.LPanel)), Tuple.Create("light Text on Bg", Look.Contrast(Look.LText, Look.LBg)), Tuple.Create("light Muted on Panel2", Look.Contrast(Look.LMuted, Look.LPanel2)),
                Tuple.Create("private GreenText on Panel", Look.Contrast("#6ED28C", p["Panel"])), Tuple.Create("private AmberText on Panel", Look.Contrast("#F0BE5A", p["Panel"])), Tuple.Create("private Muted on Panel", Look.Contrast(p["Muted"], p["Panel"])), Tuple.Create("private Text on Panel", Look.Contrast(p["Text"], p["Panel"])) };
            var bad = pairs.Where(x => x.Item2 < 4.5).ToList();
            Ok(bad.Count == 0, "contrast 1.7: translate / sync / settings text in dark, light and private >= 4.5:1" + (bad.Count == 0 ? " (worst " + pairs.Min(x => x.Item2).ToString("0.00") + ")" : " - " + string.Join(", ", bad.Select(x => x.Item1 + " " + x.Item2.ToString("0.00")))));
            Ok(Icons.Has("language") && Icons.Has("tv") && Icons.Has("server"), "1.7 icons are there");
        }
    }
}
