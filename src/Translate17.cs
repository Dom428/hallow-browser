// Hallow Browser 1.7 - translate pages, off by default.
// Nothing is preset and nothing is sent until you pick a translation service in Settings AND click Translate (or said "Always
// translate <language>" for that language before). What goes out: the text of the page's text nodes (not form fields, code, scripts
// or anything marked translate="no"), in batches, to the one service you picked - and nothing else (no address, no cookies):
//   - a LibreTranslate-compatible server you enter (self-hosted, e.g. on your NAS): POST <server>/translate {q:[...], source:"auto", target, format:"text"[, api_key]}
//   - DeepL with your own API key: POST api(-free).deepl.com/v2/translate, "Authorization: DeepL-Auth-Key <key>"
//   - Google Cloud Translation (v2) with your own API key: POST translation.googleapis.com/language/translate/v2, "X-Goog-Api-Key: <key>"
// Keys are kept on this PC only, protected by Windows (DPAPI, this user) in translate-keys.bin - never in settings.json or sync.
// The browser makes the requests itself (the key never enters the page). Show original puts every text node back.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        // ---- 1.7 settings (per profile)
        [DataMember] public string TranslateProvider = "";      // "" = none (the default) | libre | deepl | google
        [DataMember] public string TranslateLibreUrl;           // the LibreTranslate-compatible server you entered
        [DataMember] public string TranslateTo;                 // target language ("en"); null = Windows' language
        [DataMember] public List<string> TranslateAlways;       // "always translate <language>" - page languages ("fr", "de")
        [DataMember] public bool SyncShieldFilters = true;      // Shield's My filters go through sync (when sync is on)
        [DataMember] public bool SyncHistory;                   // OFF by default; only ever with the sync password (encrypted)
        [DataMember] public int SyncHistoryMax = 1000;          // the newest N pages
        [DataMember] public bool JumpList = true;               // the taskbar / Start icon's jump list
        [DataMember] public bool TaskbarTabs;                   // OFF by default: each tab as its own taskbar preview

        public static readonly int[] SyncHistoryChoices = { 250, 1000, 2500, 5000 };
        // 1.7 tests: HALLOW_BROWSER_DEEPL_URL / _GOOGLE_TR_URL = local stand-ins for DeepL's and Google's endpoints (never the real ones)
        public static readonly string DeepLTest = TestDir != null ? Env("HALLOW_BROWSER_DEEPL_URL") : null;
        public static readonly string GoogleTrTest = TestDir != null ? Env("HALLOW_BROWSER_GOOGLE_TR_URL") : null;
        // 1.7 tests: HALLOW_BROWSER_JUMPLIST_APPID = the jump list goes to this test AppUserModelID (never the real browser's)
        public static readonly string JumpListTestId = TestDir != null ? Env("HALLOW_BROWSER_JUMPLIST_APPID") : null;

        void Defaults17() { TranslateProvider = ""; SyncShieldFilters = true; SyncHistoryMax = 1000; JumpList = true; }
        void Fix17()
        {
            if (TranslateProvider != "libre" && TranslateProvider != "deepl" && TranslateProvider != "google") TranslateProvider = "";
            if (TranslateProvider == "libre" && Translator.CleanServer(TranslateLibreUrl) == null) TranslateProvider = "";
            if (TranslateTo != null && Translator.LanguageName(TranslateTo) == null) TranslateTo = null;
            if (TranslateAlways == null) TranslateAlways = new List<string>();
            TranslateAlways = TranslateAlways.Select(Translator.Primary).Where(x => x != null).Distinct().Take(50).ToList();
            if (Array.IndexOf(SyncHistoryChoices, SyncHistoryMax) < 0) SyncHistoryMax = 1000;
        }
    }

    public class TrResult { public List<string> Texts = new List<string>(); public string Detected; }

    public static class Translator
    {
        // the languages offered as targets (all three services know these)
        public static readonly string[][] Languages = {
            new[] { "en", "English" }, new[] { "de", "German" }, new[] { "fr", "French" }, new[] { "es", "Spanish" }, new[] { "it", "Italian" }, new[] { "pt", "Portuguese" },
            new[] { "nl", "Dutch" }, new[] { "pl", "Polish" }, new[] { "ru", "Russian" }, new[] { "uk", "Ukrainian" }, new[] { "tr", "Turkish" }, new[] { "sv", "Swedish" },
            new[] { "da", "Danish" }, new[] { "fi", "Finnish" }, new[] { "nb", "Norwegian" }, new[] { "cs", "Czech" }, new[] { "sk", "Slovak" }, new[] { "hu", "Hungarian" },
            new[] { "ro", "Romanian" }, new[] { "bg", "Bulgarian" }, new[] { "el", "Greek" }, new[] { "ja", "Japanese" }, new[] { "ko", "Korean" }, new[] { "zh", "Chinese" },
            new[] { "ar", "Arabic" }, new[] { "id", "Indonesian" }, new[] { "hi", "Hindi" }, new[] { "vi", "Vietnamese" }, new[] { "th", "Thai" }, new[] { "he", "Hebrew" } };
        public const int BatchTexts = 50, BatchChars = 4500, MaxTexts = 3000;
        public static string LanguageName(string code) { var p = Primary(code); if (p == null) return null; var l = Languages.FirstOrDefault(x => x[0] == p); return l == null ? null : l[1]; }
        // "en-GB" / "EN_us" -> "en"; null for nothing usable
        public static string Primary(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var c = code.Trim().ToLowerInvariant().Replace('_', '-'); var i = c.IndexOf('-'); if (i > 0) c = c.Substring(0, i);
            if (c == "no" || c == "nn") c = "nb";
            if (c == "iw") c = "he";
            return c.Length >= 2 && c.Length <= 3 && c.All(ch => ch >= 'a' && ch <= 'z') ? c : null;
        }
        public static string DefaultTarget() { var p = Primary(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName); return LanguageName(p) != null ? p : "en"; }
        public static string Target(BrowserSettings s) { return s.TranslateTo != null && LanguageName(s.TranslateTo) != null ? Primary(s.TranslateTo) : DefaultTarget(); }
        // a LibreTranslate server address: http(s)://host[:port][/path], without /translate at the end
        public static string CleanServer(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var u = url.Trim(); if (!u.Contains("://")) u = (u.StartsWith("localhost") || u.StartsWith("127.") || u.StartsWith("192.168.") || u.StartsWith("10.") ? "http://" : "https://") + u;
            Uri x; if (!Uri.TryCreate(u, UriKind.Absolute, out x) || (x.Scheme != "http" && x.Scheme != "https") || string.IsNullOrEmpty(x.Host)) return null;
            if (!string.IsNullOrEmpty(x.UserInfo) || !string.IsNullOrEmpty(x.Query) || !string.IsNullOrEmpty(x.Fragment)) return null;
            var s = x.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (s.EndsWith("/translate", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 10);
            return s;
        }
        public static string ProviderName(BrowserSettings s)
        {
            switch (s.TranslateProvider)
            {
                case "libre": var u = CleanServer(s.TranslateLibreUrl); Uri x; return u != null && Uri.TryCreate(u, UriKind.Absolute, out x) ? x.Authority : "your server";
                case "deepl": return "DeepL";
                case "google": return "Google Cloud Translation";
            }
            return null;
        }
        public static bool Ready(BrowserSettings s) { return s.TranslateProvider != "" && (s.TranslateProvider == "libre" ? CleanServer(s.TranslateLibreUrl) != null : Keys.Get(s.TranslateProvider) != null); }

        // ---- batches: at most BatchTexts texts and about BatchChars characters each (a longer text goes alone)
        public static List<List<int>> Batches(IList<string> texts)
        {
            var res = new List<List<int>>(); var cur = new List<int>(); int chars = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                var n = (texts[i] ?? "").Length;
                if (cur.Count > 0 && (cur.Count >= BatchTexts || chars + n > BatchChars)) { res.Add(cur); cur = new List<int>(); chars = 0; }
                cur.Add(i); chars += n;
            }
            if (cur.Count > 0) res.Add(cur);
            return res;
        }

        // ---- the requests (made by the browser, never by the page)
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static string DeepLEndpoint(string key) { return DeepLTest ?? (key != null && key.EndsWith(":fx") ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate"); }
        static string DeepLTest { get { return BrowserSettings.DeepLTest; } }
        public static string GoogleEndpoint() { return BrowserSettings.GoogleTrTest ?? "https://translation.googleapis.com/language/translate/v2"; }
        // the request a provider gets: url, headers, JSON body (for tests and for Send)
        public static Tuple<string, Dictionary<string, string>, string> Request(string provider, string server, string key, IList<string> texts, string target)
        {
            var h = new Dictionary<string, string>();
            switch (provider)
            {
                case "libre":
                    var lb = new Dictionary<string, object> { { "q", texts.ToArray() }, { "source", "auto" }, { "target", target }, { "format", "text" } };
                    if (!string.IsNullOrEmpty(key)) lb["api_key"] = key;
                    return Tuple.Create(CleanServer(server) + "/translate", h, js.Serialize(lb));
                case "deepl":
                    h["Authorization"] = "DeepL-Auth-Key " + key;
                    return Tuple.Create(DeepLEndpoint(key), h, js.Serialize(new Dictionary<string, object> { { "text", texts.ToArray() }, { "target_lang", DeepLTarget(target) } }));
                case "google":
                    h["X-Goog-Api-Key"] = key;
                    return Tuple.Create(GoogleEndpoint(), h, js.Serialize(new Dictionary<string, object> { { "q", texts.ToArray() }, { "target", target }, { "format", "text" } }));
            }
            throw new InvalidOperationException("no translation service");
        }
        public static string DeepLTarget(string t) { t = Primary(t) ?? "en"; return t == "en" ? "EN-US" : t == "pt" ? "PT-PT" : t == "zh" ? "ZH-HANS" : t.ToUpperInvariant(); }
        // a provider's answer -> the texts (as many as asked) and the language it found
        public static TrResult Parse(string provider, string body, int count)
        {
            var r = new TrResult(); var d = js.Deserialize<Dictionary<string, object>>(body);
            if (d == null) throw new InvalidDataException("empty answer");
            object o;
            if (provider == "libre")
            {
                if (d.TryGetValue("error", out o) && o != null) throw new InvalidDataException(o.ToString());
                if (!d.TryGetValue("translatedText", out o)) throw new InvalidDataException("no translatedText in the answer");
                if (o is string) r.Texts.Add((string)o); else if (o is System.Collections.ArrayList) foreach (var x in (System.Collections.ArrayList)o) r.Texts.Add(x as string ?? "");
                object dl; if (d.TryGetValue("detectedLanguage", out dl))
                {
                    var one = dl as Dictionary<string, object>; if (one == null && dl is System.Collections.ArrayList && ((System.Collections.ArrayList)dl).Count > 0) one = ((System.Collections.ArrayList)dl)[0] as Dictionary<string, object>;
                    object lang; if (one != null && one.TryGetValue("language", out lang)) r.Detected = Primary(lang as string);
                }
            }
            else if (provider == "deepl")
            {
                if (!d.TryGetValue("translations", out o) || !(o is System.Collections.ArrayList)) throw new InvalidDataException(d.ContainsKey("message") ? d["message"].ToString() : "no translations in the answer");
                foreach (Dictionary<string, object> x in (System.Collections.ArrayList)o) { object t, l; x.TryGetValue("text", out t); r.Texts.Add(t as string ?? ""); if (r.Detected == null && x.TryGetValue("detected_source_language", out l)) r.Detected = Primary(l as string); }
            }
            else if (provider == "google")
            {
                object err; if (d.TryGetValue("error", out err)) { var e = err as Dictionary<string, object>; object m; throw new InvalidDataException(e != null && e.TryGetValue("message", out m) ? m.ToString() : "error"); }
                var data = d.TryGetValue("data", out o) ? o as Dictionary<string, object> : null; object tr;
                if (data == null || !data.TryGetValue("translations", out tr) || !(tr is System.Collections.ArrayList)) throw new InvalidDataException("no translations in the answer");
                foreach (Dictionary<string, object> x in (System.Collections.ArrayList)tr) { object t, l; x.TryGetValue("translatedText", out t); r.Texts.Add(WebUtility.HtmlDecode(t as string ?? "")); if (r.Detected == null && x.TryGetValue("detectedSourceLanguage", out l)) r.Detected = Primary(l as string); }
            }
            if (r.Texts.Count != count) throw new InvalidDataException("the service answered " + r.Texts.Count + " texts for " + count);
            return r;
        }
        public static int Sent;   // texts sent this run (tests and the status line)
        public static TrResult Send(string provider, string server, string key, IList<string> texts, string target)
        {
            var q = Request(provider, server, key, texts, target);
            var req = (HttpWebRequest)WebRequest.Create(q.Item1);
            req.Method = "POST"; req.ContentType = "application/json; charset=utf-8"; req.Accept = "application/json"; req.UserAgent = "HallowBrowser/" + Program.Version;
            req.Timeout = 30000; req.ReadWriteTimeout = 30000; req.AllowAutoRedirect = false; req.Proxy = WebRequest.DefaultWebProxy;
            foreach (var kv in q.Item2) req.Headers[kv.Key] = kv.Value;
            var body = Encoding.UTF8.GetBytes(q.Item3); req.ContentLength = body.Length;
            using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse()) using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                { var r = Parse(provider, rd.ReadToEnd(), texts.Count); System.Threading.Interlocked.Add(ref Sent, texts.Count); return r; }
            }
            catch (WebException e)
            {
                var hr = e.Response as HttpWebResponse; string msg = e.Message;
                if (hr != null) { msg = "HTTP " + (int)hr.StatusCode; try { using (var rd = new StreamReader(hr.GetResponseStream())) { var t = rd.ReadToEnd(); if (t.Length > 0) msg += " - " + (t.Length > 160 ? t.Substring(0, 160) : t); } } catch { } }
                if (hr != null && ((int)hr.StatusCode == 401 || (int)hr.StatusCode == 403)) msg = "the service refused the key (" + msg + ")";
                if (hr != null && (int)hr.StatusCode == 456) msg = "the DeepL quota is used up (" + msg + ")";
                throw new IOException(msg);
            }
        }

        // ---- the page side: text nodes in, translations out; Show original puts them back
        public static readonly string CollectScript = "(function(){var H=window.__hallowTr;if(!H)H=window.__hallowTr={nodes:[],orig:[],on:false};" +
            "if(H.on)return JSON.stringify({on:true,lang:(document.documentElement.lang||'')});" +
            "var bad='script,style,noscript,code,pre,kbd,samp,var,textarea,input,select,option,svg,math,iframe,canvas,template,[translate=no],.notranslate,[contenteditable=\"\"],[contenteditable=true]';" +
            "var root=document.body||document.documentElement;H.nodes=[];H.orig=[];var texts=[];" +
            "var w=document.createTreeWalker(root,NodeFilter.SHOW_TEXT,{acceptNode:function(n){var e=n.parentElement;if(!e||!/\\S/.test(n.data))return 2;if(!/[^\\s\\d.,:;!?()\\[\\]{}'\"\\-+*\\/\\\\|_=<>@#$%^&~`]/.test(n.data))return 2;if(e.closest(bad))return 2;return 1;}});" +
            "var n;while((n=w.nextNode())&&texts.length<" + MaxTexts + "){H.nodes.push(n);H.orig.push(n.data);texts.push(n.data.trim());}" +
            "return JSON.stringify({on:false,lang:(document.documentElement.lang||''),texts:texts,more:!!w.nextNode()});})()";
        public static string ApplyScript(int start, IList<string> texts, string target)
        {
            return "(function(s,a){var H=window.__hallowTr;if(!H)return 0;var k=0;for(var i=0;i<a.length;i++){var n=H.nodes[s+i],o=H.orig[s+i];if(!n||a[i]==null)continue;" +
                "var lead=o.match(/^\\s*/)[0],trail=o.match(/\\s*$/)[0];n.data=lead+a[i]+trail;k++;}H.on=true;document.documentElement.setAttribute('data-hallow-translated'," + js.Serialize(target) + ");return k;})(" + start + "," + js.Serialize(texts) + ")";
        }
        public const string RestoreScript = "(function(){var H=window.__hallowTr;if(!H)return 0;var k=0;for(var i=0;i<H.nodes.length;i++){if(H.nodes[i].data!==H.orig[i]){H.nodes[i].data=H.orig[i];k++;}}H.on=false;document.documentElement.removeAttribute('data-hallow-translated');return k;})()";
        public const string LangScript = "(document.documentElement.lang||'')";

        // ---- the API keys: DPAPI (this Windows user), on this PC only
        public static class Keys
        {
            static string FilePath { get { return System.IO.Path.Combine(BrowserSettings.LocalDir, "translate-keys.bin"); } }
            static readonly byte[] Entropy = Encoding.ASCII.GetBytes("HallowBrowserTranslate");
            static Dictionary<string, string> cache;
            static Dictionary<string, string> Load()
            {
                if (cache != null) return cache;
                var d = new Dictionary<string, string>();
                try { if (File.Exists(FilePath)) d = js.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser))) ?? d; } catch { }
                return cache = d;
            }
            public static string Get(string provider) { string v; return Load().TryGetValue(provider ?? "", out v) && !string.IsNullOrEmpty(v) ? v : null; }
            public static void Set(string provider, string key)
            {
                var d = new Dictionary<string, string>(Load());
                if (string.IsNullOrWhiteSpace(key)) d.Remove(provider); else d[provider] = key.Trim();
                try
                {
                    Directory.CreateDirectory(BrowserSettings.LocalDir);
                    if (d.Count == 0) File.Delete(FilePath);
                    else File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(js.Serialize(d)), Entropy, DataProtectionScope.CurrentUser));
                }
                catch { }
                cache = d;
            }
            public static string Masked(string provider) { var k = Get(provider); return k == null ? null : (k.Length <= 4 ? "****" : "****" + k.Substring(k.Length - 4)); }
        }
    }

    public partial class BrowserWindow
    {
        Button translateBtn;
        public string LastTranslateNote;
        void BuildTranslateButton(Panel inBar)
        {
            translateBtn = Ui.Tool("language", "Translate this page", () => TranslateFlyout(Active), 13); translateBtn.Height = 24; translateBtn.Width = 28; translateBtn.Visibility = Visibility.Collapsed;
            inBar.Children.Insert(0, translateBtn);
        }
        public void UpdateTranslateButtonPublic(Tab t) { UpdateTranslateButton(t); }
        public bool TranslateButtonVisible { get { return translateBtn != null && translateBtn.Visibility == Visibility.Visible; } }
        public string TranslateButtonTip { get { return translateBtn == null ? null : translateBtn.ToolTip as string; } }
        UIElement TrAnchor() { return translateBtn != null && translateBtn.IsVisible ? (UIElement)translateBtn : menuBtn; }
        void UpdateTranslateButton(Tab t)
        {
            if (translateBtn == null || t == null) return;
            translateBtn.Visibility = Translator.Ready(S) && Omni.IsWeb(t.Url) && !t.Internal && !AppMode ? Visibility.Visible : Visibility.Collapsed;
            ((System.Windows.Shapes.Path)translateBtn.Content).SetResourceReference(Shape.FillProperty, t.Translated ? "AccentBrush" : "Text");
            translateBtn.ToolTip = t.TranslateBusy ? "Translating..." : t.Translated ? "Translated to " + Translator.LanguageName(t.TranslatedTo) + " - click for the original" : "Translate this page";
        }
        void Hook17(CoreWebView2 c)
        {
            c.NavigationStarting += (a, b) => { Tab x; if (!owner.TryGetValue(c, out x)) return; x.Translated = false; x.TranslateBusy = false; x.PageLang = null; x.TranslateRun++; if (x == Active) UpdateTranslateButton(x); };
            c.NavigationCompleted += (a, b) => { Tab x; if (!owner.TryGetValue(c, out x) || !b.IsSuccess || !Omni.IsWeb(x.Url)) return; var _ = AfterLoad17(x, c); };
        }
        // "Always translate <language>": only for languages you said so for, with a service set up, never in a private window
        async Task AfterLoad17(Tab t, CoreWebView2 c)
        {
            if (!Translator.Ready(S)) return;
            string lang = null; try { lang = new JavaScriptSerializer().Deserialize<string>(await Panels.Eval(c, Translator.LangScript)); } catch { }
            if (t.Core != c) return;
            t.PageLang = Translator.Primary(lang);
            if (t == Active) UpdateTranslateButton(t);
            if (Private || t.PageLang == null || t.PageLang == Translator.Target(S) || !S.TranslateAlways.Contains(t.PageLang)) return;
            await TranslateTab(t, true);
        }
        public void TranslateFlyout(Tab t)
        {
            if (t == null) return;
            var sp = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
            sp.Children.Add(Ui.T("Translate this page", 14, true, null, 6));
            if (!Translator.Ready(S))
            {
                sp.Children.Add(Ui.Muted("No translation service is set up, so nothing is sent anywhere. Pick one in Settings > General > Translate pages: your own LibreTranslate server, or DeepL / Google with your own API key.", 12, 10));
                sp.Children.Add(Ui.Row(Ui.Btn("Open settings", () => { Panels.CloseFlyouts(); OpenSettingsAt("translate"); }, true, "gear")));
                Panels.ShowFlyout(TrAnchor(), sp, 360); return;
            }
            var target = Translator.Target(S); var from = t.PageLang != null ? Translator.LanguageName(t.PageLang) ?? t.PageLang : null;
            sp.Children.Add(Ui.Muted((from != null ? "From " + from : "From the page's language (the service finds it)") + " to " + Translator.LanguageName(target) + ".", 12, 4));
            var change = Ui.Btn("Change the language...", () => { Panels.CloseFlyouts(); OpenSettingsAt("Translate to"); }, false, "language"); change.Margin = new Thickness(0, 0, 0, 10); change.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(change);
            if (t.Translated) sp.Children.Add(Ui.Row(Ui.Btn("Show original", () => { Panels.CloseFlyouts(); var _ = ShowOriginal(t); }, true, "rotate-left"), Ui.Btn("Translate again", () => { Panels.CloseFlyouts(); var _ = TranslateTab(t, false); }, false, "language")));
            else sp.Children.Add(Ui.Row(Ui.Btn("Translate", () => { Panels.CloseFlyouts(); var _ = TranslateTab(t, false); }, true, "language")));
            if (t.PageLang != null && t.PageLang != target && !Private)
            {
                var name = Translator.LanguageName(t.PageLang) ?? t.PageLang;
                sp.Children.Add(new Border { Height = 8 });
                sp.Children.Add(Ui.Switch("Always translate " + name, "Pages in " + name + " are translated as they load (their text goes to " + Translator.ProviderName(S) + " each time).", S.TranslateAlways.Contains(t.PageLang), on => { S.TranslateAlways.Remove(t.PageLang); if (on) S.TranslateAlways.Add(t.PageLang); S.Save(); }));
            }
            var note = Ui.Muted("What's sent: the text of this page, to " + Translator.ProviderName(S) + " (the service you picked) - not the address, not your cookies, not form fields. Nothing goes out until you click.", 11, 0);
            note.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(note);
            if (LastTranslateNote != null && t.TranslateNote != null) { var e = Ui.T(t.TranslateNote, 11.5, false, "AmberText"); e.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(e); }
            Panels.ShowFlyout(TrAnchor(), sp, 360);
        }
        public async Task<string> TranslateTab(Tab t, bool auto)
        {
            if (t == null || t.Core == null || t.TranslateBusy) return "busy";
            if (!Translator.Ready(S)) return "no service";
            var c = t.Core; var run = ++t.TranslateRun; var provider = S.TranslateProvider; var server = S.TranslateLibreUrl; var key = Translator.Keys.Get(provider); var target = Translator.Target(S);
            t.TranslateBusy = true; t.TranslateNote = null; if (t == Active) UpdateTranslateButton(t);
            try
            {
                if (t.Translated) await Panels.Eval(c, Translator.RestoreScript);
                var js2 = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                var raw = js2.Deserialize<string>(await Panels.Eval(c, Translator.CollectScript));
                var page = js2.Deserialize<Dictionary<string, object>>(raw);
                var texts = page.ContainsKey("texts") && page["texts"] is System.Collections.ArrayList ? ((System.Collections.ArrayList)page["texts"]).Cast<object>().Select(x => x as string ?? "").ToList() : new List<string>();
                if (t.PageLang == null) t.PageLang = Translator.Primary(page["lang"] as string);
                if (texts.Count == 0) { t.TranslateNote = "No text to translate on this page."; return "empty"; }
                int done = 0;
                foreach (var b in Translator.Batches(texts))
                {
                    var part = b.Select(i => texts[i]).ToList();
                    var r = await Task.Run(() => Translator.Send(provider, server, key, part, target));
                    if (t.Core != c || t.TranslateRun != run) return "left";   // the page changed meanwhile
                    if (r.Detected != null && t.PageLang == null) t.PageLang = r.Detected;
                    await Panels.Eval(c, Translator.ApplyScript(b[0], r.Texts, target));
                    done += part.Count;
                    if (!t.Translated) { t.Translated = true; t.TranslatedTo = target; if (t == Active) UpdateTranslateButton(t); }
                }
                t.TranslatedTo = target; LastTranslateNote = "translated " + done + " texts" + (auto ? " (always)" : "");
                if (page.ContainsKey("more") && page["more"] is bool && (bool)page["more"]) Toast("Translated the first " + Translator.MaxTexts + " pieces of text on this long page.", 4);
                return "ok " + done;
            }
            catch (Exception e)
            {
                var m = e is IOException || e is InvalidDataException ? e.Message : e is WebException ? e.Message : e.GetType().Name + ": " + e.Message;
                t.TranslateNote = "Couldn't translate: " + m; LastTranslateNote = t.TranslateNote;
                if (!auto) Toast(t.TranslateNote, 6);
                return "error " + m;
            }
            finally { t.TranslateBusy = false; if (t == Active) UpdateTranslateButton(t); }
        }
        public async Task<int> ShowOriginal(Tab t)
        {
            if (t == null || t.Core == null) return 0;
            t.TranslateRun++;
            int n = 0; try { int.TryParse(await Panels.Eval(t.Core, Translator.RestoreScript), out n); } catch { }
            t.Translated = false; t.TranslateBusy = false; if (t == Active) UpdateTranslateButton(t);
            return n;
        }

        // Settings > General > Translate pages
        public UIElement[] TranslateSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Muted("Off until you pick a service. Then a translate button shows in the address bar; the page's text goes to the service you picked only when you click Translate (or for a language you said \"Always translate\" for). Nothing is preset and Hallow has no translation server of its own.", 12, 8));
            var status = Ui.T("", 11.5, false, "GreenText", 6);
            Action refresh = () =>
            {
                var ready = Translator.Ready(S);
                status.Text = ready ? "Translating with " + Translator.ProviderName(S) + " - what's sent: the page's text." : S.TranslateProvider == "" ? "No service set up: nothing is ever sent." : "Not ready yet: " + (S.TranslateProvider == "libre" ? "enter your server's address." : "enter your API key.");
                status.SetResourceReference(TextBlock.ForegroundProperty, ready ? "GreenText" : "Muted");
                foreach (var w in Program.Windows) if (w.Active != null) w.UpdateTranslateButton(w.Active);
            };
            var libreBox = new TextBox { Style = Ui.S("Field"), Width = 300, Height = 30, Padding = new Thickness(8, 3, 8, 3), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Text = S.TranslateLibreUrl ?? "", ToolTip = "Your LibreTranslate server, e.g. http://192.168.1.20:5000" };
            var keyBox = new PasswordBox { Width = 300, Height = 30, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            keyBox.SetResourceReference(Control.BackgroundProperty, "FieldBg"); keyBox.SetResourceReference(Control.ForegroundProperty, "Text"); keyBox.SetResourceReference(Control.BorderBrushProperty, "LineDim");
            var keyNote = Ui.Muted("", 11.5, 6);
            var libreRow = Ui.Row(libreBox, Ui.Btn("Use this server", () =>
            {
                var u = Translator.CleanServer(libreBox.Text);
                if (u == null) { Toast("That isn't a server address (http:// or https://, e.g. http://192.168.1.20:5000).", 5); return; }
                S.TranslateLibreUrl = u; libreBox.Text = u; S.Save(); refresh();
            }, false, "server"));
            var keyRow = Ui.Row(keyBox, Ui.Btn("Save key", () =>
            {
                if (keyBox.Password.Trim().Length < 8 && S.TranslateProvider != "libre") { Toast("That doesn't look like an API key.", 4); return; }
                Translator.Keys.Set(S.TranslateProvider, keyBox.Password); keyBox.Clear(); refresh(); fillKey();
            }, false, "key"), Ui.Btn("Forget key", () => { Translator.Keys.Set(S.TranslateProvider, null); refresh(); fillKey(); }, false, "trash-can"));
            Action showRows = null;
            showRows = () =>
            {
                libreRow.Visibility = S.TranslateProvider == "libre" ? Visibility.Visible : Visibility.Collapsed;
                keyRow.Visibility = keyNote.Visibility = S.TranslateProvider == "" ? Visibility.Collapsed : Visibility.Visible;
                fillKey();
            };
            items.Add(Ui.Muted("Service", 12, 6));
            items.Add(Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", "None"), new KeyValuePair<string, string>("libre", "LibreTranslate (your server)"), new KeyValuePair<string, string>("deepl", "DeepL (your key)"), new KeyValuePair<string, string>("google", "Google (your key)") },
                S.TranslateProvider, v => { S.TranslateProvider = v; S.Save(); showRows(); refresh(); }));
            items.Add(new Border { Height = 6 });
            libreRow.Margin = new Thickness(0, 0, 0, 6); keyRow.Margin = new Thickness(0, 0, 0, 4);
            items.Add(libreRow); items.Add(keyRow); items.Add(keyNote);
            items.Add(status);
            items.Add(Ui.Muted("Translate to", 12, 6));
            var target = Translator.Target(S);
            var pick = Ui.Chips(Translator.Languages.Select(l => new KeyValuePair<string, string>(l[0], l[1])).ToList(), target, v => { S.TranslateTo = v; S.Save(); });
            pick.Margin = new Thickness(0, 0, 0, 8);
            items.Add(pick);
            var always = Ui.Muted("", 11.5, 4);
            Action fillAlways = () => always.Text = S.TranslateAlways.Count == 0 ? "Always translate: no languages (pick them in the translate button's panel)." : "Always translate: " + string.Join(", ", S.TranslateAlways.Select(x => Translator.LanguageName(x) ?? x)) + ".";
            fillAlways(); items.Add(always);
            items.Add(Ui.Row(Ui.Btn("Clear \"always translate\"", () => { S.TranslateAlways.Clear(); S.Save(); fillAlways(); }, false, "rotate-left")));
            items.Add(Ui.Muted("What's sent: the visible text of the page (not form fields, code, or anything the page marks \"don't translate\") in batches, to the one service above. Never the address, cookies or your history. Keys stay on this PC (protected by Windows), never in sync. Private windows never translate by themselves. Text that appears after translating stays as it is until you translate again.", 11.5, 0));
            fillKeyAction = () =>
            {
                var m = S.TranslateProvider == "" ? null : Translator.Keys.Masked(S.TranslateProvider);
                keyNote.Text = S.TranslateProvider == "libre" ? (m != null ? "API key for your server: saved (" + m + ")." : "An API key only if your server asks for one.") : m != null ? "API key saved on this PC (" + m + ")." : "Paste your " + (S.TranslateProvider == "deepl" ? "DeepL" : "Google Cloud") + " API key (DeepL Free keys end in :fx).";
            };
            showRows(); refresh();
            return items.ToArray();
        }
        Action fillKeyAction;
        void fillKey() { if (fillKeyAction != null) fillKeyAction(); }
    }
}
