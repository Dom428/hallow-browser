// Hallow Browser 1.7 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own
// test folder, engine and pipes, local pages and stand-ins only: a fake LibreTranslate server and fake DeepL / Google endpoints on
// 127.0.0.1 (the real services are never called - the browser is pointed at the stand-ins), the jump list written to a TEST
// AppUserModelID and deleted after, taskbar messages sent to the browser's own stand-in windows, "Open in Edge" written to a log
// (Edge is never started), and a sync folder in the test folder shared by two test profiles.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v17
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration17
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string pipe = "Browser";
        static string Cmd(string line, int ms = 45000) { return Family.Send(pipe, line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null || v == "null" ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> L(string reply) { var v = Val(reply); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static List<Dictionary<string, object>> Tabs() { return L(Cmd("test-tabs")); }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static int I(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        static double D(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static List<string> A(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Cast<object>().Select(x => x == null ? null : x.ToString()).ToList() : new List<string>(); }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { return v == null ? null : js.Deserialize<object>(v) as string ?? v; } catch { return v; } }
        static string Nav(int tab, string url) { return Cmd("test-nav " + tab + " " + url); }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Thread.Sleep(400); return i; }
        static List<int> Mine(string exe) { var r = new List<int>(); foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) r.Add(p.Id); } catch { } } return r; }

        // ---- the stand-ins: test pages (GET) and the three translation APIs (POST), every API request written down
        public class Req { public string Path, Body, Auth, GoogKey, Cookie, Referer; }
        static readonly List<Req> reqs = new List<Req>();
        static List<Req> Api(string prefix) { lock (reqs) return reqs.Where(r => r.Path.StartsWith(prefix)).ToList(); }
        static List<Req> AllApi() { lock (reqs) return reqs.ToList(); }
        class Server
        {
            public int Port; TcpListener l; volatile bool stop;
            public Func<string, string> Page;
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            static string Tr(string t, string target) { return "[" + target + "] " + t; }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var s = c.GetStream())
                    {
                        var head = new List<byte>(); int b; while ((b = s.ReadByte()) >= 0) { head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; }
                        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                        var parts = lines[0].Split(' '); var method = parts[0]; var path = parts[1];
                        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ln in lines.Skip(1)) { var i = ln.IndexOf(':'); if (i > 0) h[ln.Substring(0, i).Trim()] = ln.Substring(i + 1).Trim(); }
                        string body = ""; string cl; if (h.TryGetValue("Content-Length", out cl)) { var len = int.Parse(cl); var buf = new byte[len]; int got = 0; while (got < len) { var k = s.Read(buf, got, len - got); if (k <= 0) break; got += k; } body = Encoding.UTF8.GetString(buf, 0, got); }
                        int code = 200; string type = "application/json"; string resp;
                        if (method == "POST")
                        {
                            string v; var rq = new Req { Path = path, Body = body, Auth = h.TryGetValue("Authorization", out v) ? v : null, GoogKey = h.TryGetValue("X-Goog-Api-Key", out v) ? v : null, Cookie = h.TryGetValue("Cookie", out v) ? v : null, Referer = h.TryGetValue("Referer", out v) ? v : null };
                            lock (reqs) reqs.Add(rq);
                            var d = js.Deserialize<Dictionary<string, object>>(body);
                            if (path.StartsWith("/libre/translate"))
                            {
                                var q = ((System.Collections.ArrayList)d["q"]).Cast<string>().ToList(); var target = (string)d["target"];
                                resp = js.Serialize(new Dictionary<string, object> { { "translatedText", q.Select(x => Tr(x, target)).ToArray() }, { "detectedLanguage", new[] { new Dictionary<string, object> { { "confidence", 90 }, { "language", "fr" } } } } });
                            }
                            else if (path.StartsWith("/deepl/"))
                            {
                                if (rq.Auth != "DeepL-Auth-Key test-deepl-key:fx") { code = 403; resp = "{\"message\":\"Wrong key\"}"; }
                                else { var q = ((System.Collections.ArrayList)d["text"]).Cast<string>().ToList(); var target = (string)d["target_lang"]; resp = js.Serialize(new Dictionary<string, object> { { "translations", q.Select(x => new Dictionary<string, object> { { "detected_source_language", "FR" }, { "text", Tr(x, target) } }).ToArray() } }); }
                            }
                            else if (path.StartsWith("/google/"))
                            {
                                if (rq.GoogKey != "test-google-key-1234") { code = 403; resp = "{\"error\":{\"code\":403,\"message\":\"API key not valid\"}}"; }
                                else { var q = ((System.Collections.ArrayList)d["q"]).Cast<string>().ToList(); var target = (string)d["target"]; resp = js.Serialize(new Dictionary<string, object> { { "data", new Dictionary<string, object> { { "translations", q.Select(x => new Dictionary<string, object> { { "translatedText", WebUtility.HtmlEncode(Tr(x, "g-" + target)) }, { "detectedSourceLanguage", "fr" } }).ToArray() } } } }); }
                            }
                            else { code = 404; resp = "{}"; }
                        }
                        else { resp = Page(path); type = "text/html; charset=utf-8"; if (resp == null) { code = 404; resp = "not found"; } }
                        var bytes = Encoding.UTF8.GetBytes(resp);
                        var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Error") + "\r\nContent-Type: " + type + "\r\nContent-Length: " + bytes.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        s.Write(hb, 0, hb.Length); s.Write(bytes, 0, bytes.Length); s.Flush();
                    }
                }
                catch { }
            }
        }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            var run2 = Path.Combine(dir, "pc2", "run"); Directory.CreateDirectory(run2);   // the same folder name: this process's test pipe reaches either (one runs at a time)
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v17.txt"), false) { AutoFlush = true };
            var paras = string.Join("", Enumerable.Range(0, 120).Select(i => "<p class=m>Ligne numero " + i + " du texte</p>"));
            var srv = new Server();
            srv.Page = path =>
            {
                if (path.StartsWith("/fr.html")) return "<!DOCTYPE html><html lang=fr><head><title>Page fr</title></head><body><p id=a>Bonjour le monde</p><p id=b>  Il fait <b>beau</b> aujourd'hui  </p><code id=c>secret code</code><textarea id=t>texte du formulaire</textarea><input id=i value='valeur'><span id=n translate=no>keepme</span><span class=notranslate>keeptoo</span><script>var x='script texte';</script><p id=d>12:30</p></body></html>";
                if (path.StartsWith("/long.html")) return "<!DOCTYPE html><html lang=fr-FR><head><title>Long fr</title></head><body>" + paras + "</body></html>";
                if (path.StartsWith("/fr2.html")) return "<!DOCTYPE html><html lang=fr><head><title>Encore</title></head><body><p id=a>Merci beaucoup</p></body></html>";
                if (path.StartsWith("/en.html")) return "<!DOCTYPE html><html lang=en><head><title>English</title></head><body><p id=a>Hello there</p></body></html>";
                if (path.StartsWith("/plain.html")) return "<!DOCTYPE html><html><head><title>Plain</title></head><body>plain</body></html>";
                return null;
            };
            srv.Start(); var P = srv.Port; var U = "http://127.0.0.1:" + P;
            var launchLog = Path.Combine(dir, "launch.log"); File.WriteAllText(launchLog, "");
            var appId = "HallowBrowser.Test." + Guid.NewGuid().ToString("N").Substring(0, 10);
            var t0 = DateTime.Now;
            Process br = null;
            Func<string, string, Process> launch = (testDir, args) =>
            {
                var psi = new ProcessStartInfo(exe, args ?? "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = testDir;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_LAUNCH_LOG"] = launchLog;
                psi.EnvironmentVariables["HALLOW_BROWSER_JUMPLIST_APPID"] = appId;
                psi.EnvironmentVariables["HALLOW_BROWSER_DEEPL_URL"] = U + "/deepl/v2/translate";
                psi.EnvironmentVariables["HALLOW_BROWSER_GOOGLE_TR_URL"] = U + "/google/language/translate/v2";
                var pr = Process.Start(psi); try { pr.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return pr;
            };
            var syncFolder = Path.Combine(dir, "SyncFolder"); Directory.CreateDirectory(syncFolder);
            try
            {
                br = launch(run, null);
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1500);

                // ================= translate: nothing preset, nothing sent before a service is set AND you click
                int fr = NewTab(U + "/fr.html");
                var st = J(Cmd("test-z-tr-state " + fr));
                Ok(!B(st, "ready") && !B(st, "button") && S(st, "provider") == null, "translate: off by default - no service, no button");
                Ok(Val(Cmd("test-z-tr " + fr)) == "no service" && AllApi().Count == 0, "translate: asking without a service sends nothing");
                var fly0 = js.Deserialize<string>(Val(Cmd("test-z-tr-flyout " + fr)) ?? "\"\"") ?? ""; Cmd("test-z-tr-close");
                Ok(fly0.Contains("No translation service is set up") && fly0.Contains("nothing is sent"), "translate: the panel says no service is set up and nothing is sent");
                Cmd("test-z-tr-set libre " + U + "/libre"); Thread.Sleep(1500);
                Nav(fr, U + "/fr.html?2"); Thread.Sleep(1200);
                st = J(Cmd("test-z-tr-state " + fr));
                Ok(B(st, "ready") && B(st, "button") && S(st, "provider") == "127.0.0.1:" + P && S(st, "lang") == "fr", "translate: with your LibreTranslate server the button shows; the page's language is French");
                Ok(AllApi().Count == 0, "translate: setting a service up sends nothing by itself");
                var fly = js.Deserialize<string>(Val(Cmd("test-z-tr-flyout " + fr)) ?? "\"\"") ?? ""; Cmd("test-z-tr-close"); Note("translate panel: " + fly);
                Ok(fly.Contains("What's sent: the text of this page, to 127.0.0.1:" + P) && fly.Contains("Always translate French") && fly.Contains("Nothing goes out until you click"), "translate: the panel says what is sent and to whom, and offers \"Always translate French\"");
                var r1 = Val(Cmd("test-z-tr " + fr));
                var lib = Api("/libre/translate");
                Ok(r1 != null && r1.StartsWith("ok ") && lib.Count == 1, "translate: one click -> one request to your server (" + r1 + ", " + lib.Count + " requests)");
                var body = lib.Count > 0 ? lib[0].Body : "";
                Ok(body.Contains("Bonjour le monde") && body.Contains("\"source\":\"auto\"") && body.Contains("\"target\":\"" + Translator.DefaultTarget() + "\""), "translate: the request carries the page's text, source auto, your target language");
                Ok(!body.Contains("secret code") && !body.Contains("texte du formulaire") && !body.Contains("valeur") && !body.Contains("keepme") && !body.Contains("keeptoo") && !body.Contains("script texte") && !body.Contains("12:30"), "translate: code, form fields, translate=no / notranslate, scripts and bare numbers are not sent");
                Ok(!body.Contains("127.0.0.1:" + P + "/fr") && lib.All(x => x.Cookie == null && x.Referer == null), "translate: no page address, no cookies, no referrer go with it");
                var tgt = Translator.DefaultTarget();
                Ok(Eval(fr, "document.getElementById('a').textContent") == "[" + tgt + "] Bonjour le monde" && Eval(fr, "document.getElementById('b').innerHTML").StartsWith("  [" + tgt + "] Il fait") && Eval(fr, "document.getElementById('c').textContent") == "secret code" && Eval(fr, "document.getElementById('t').value") == "texte du formulaire",
                    "translate: the page shows the translation (spaces around kept); code and the form field untouched");
                st = J(Cmd("test-z-tr-state " + fr)); Ok(B(st, "translated") && (S(st, "tip") ?? "").Contains("click for the original"), "translate: the button shows the page is translated");
                Ok(Val(Cmd("test-z-tr-orig " + fr)) != "0" && Eval(fr, "document.getElementById('a').textContent") == "Bonjour le monde" && Eval(fr, "document.getElementById('b').innerHTML") == "  Il fait <b>beau</b> aujourd'hui  " && Eval(fr, "document.documentElement.hasAttribute('data-hallow-translated')") == "false", "translate: Show original puts every text back exactly");
                // batches
                int lg = NewTab(U + "/long.html"); var before = Api("/libre/translate").Count;
                var r2 = Val(Cmd("test-z-tr " + lg)); var batches = Api("/libre/translate").Skip(before).ToList();
                var sizes = batches.Select(x => ((System.Collections.ArrayList)js.Deserialize<Dictionary<string, object>>(x.Body)["q"]).Count).ToList();
                Ok(r2 == "ok 120" && batches.Count == 3 && sizes.All(n => n <= 50) && sizes.Sum() == 120, "translate: 120 paragraphs go in batches (" + string.Join(" + ", sizes) + ")");
                Ok(Eval(lg, "Array.from(document.querySelectorAll('p.m')).every(function(p,i){return p.textContent==='[" + tgt + "] Ligne numero '+i+' du texte'})") == "true", "translate: every batch lands on the right paragraph");
                Cmd("test-z-tr-orig " + lg);
                Ok(Eval(lg, "Array.from(document.querySelectorAll('p.m')).every(function(p,i){return p.textContent==='Ligne numero '+i+' du texte'})") == "true", "translate: ... and Show original restores all 120");
                // DeepL (your key, in the Authorization header)
                Cmd("test-z-tr-set deepl"); var mk = Val(Cmd("test-z-tr-key deepl test-deepl-key:fx"));
                Ok(mk == "****y:fx" && File.Exists(Path.Combine(run, "Local", "translate-keys.bin")) && !File.ReadAllText(Path.Combine(run, "settings.json")).Contains("test-deepl-key"), "translate: the DeepL key is kept protected (translate-keys.bin), never in settings.json");
                var r3 = Val(Cmd("test-z-tr " + fr)); var dl = Api("/deepl/");
                Ok(r3 != null && r3.StartsWith("ok") && dl.Count == 1 && dl[0].Auth == "DeepL-Auth-Key test-deepl-key:fx" && dl[0].Body.Contains("\"target_lang\":\"" + Translator.DeepLTarget(tgt) + "\"") && Eval(fr, "document.getElementById('a').textContent") == "[" + Translator.DeepLTarget(tgt) + "] Bonjour le monde", "translate: DeepL with your key (stand-in) translated the page");
                Cmd("test-z-tr-orig " + fr);
                // Google (your key, in X-Goog-Api-Key) - a wrong key first
                Cmd("test-z-tr-set google"); Cmd("test-z-tr-key google wrong-key-0000");
                var r4 = Val(Cmd("test-z-tr " + fr)) ?? "";
                Ok(r4.StartsWith("error") && r4.Contains("refused the key") && Eval(fr, "document.getElementById('a').textContent") == "Bonjour le monde", "translate: a refused key says so and changes nothing (" + r4.Substring(0, Math.Min(80, r4.Length)) + ")");
                Cmd("test-z-tr-key google test-google-key-1234");
                var r5 = Val(Cmd("test-z-tr " + fr)); var gg = Api("/google/");
                Ok(r5 != null && r5.StartsWith("ok") && gg.Last().GoogKey == "test-google-key-1234" && !gg.Last().Path.Contains("key=") && Eval(fr, "document.getElementById('a').textContent") == "[g-" + tgt + "] Bonjour le monde", "translate: Google with your key (stand-in) translated the page; the key isn't in the address");
                Cmd("test-z-tr-orig " + fr);
                // always translate French
                Cmd("test-z-tr-set libre " + U + "/libre"); Cmd("test-z-tr-always fr 1");
                var n0 = Api("/libre/").Count;
                Nav(fr, U + "/fr2.html"); var auto = Wait(() => Eval(fr, "document.getElementById('a').textContent"), v => v != null && v.StartsWith("["), 8000);
                Ok(auto == "[" + tgt + "] Merci beaucoup" && Api("/libre/").Count == n0 + 1, "translate: \"Always translate French\" translates French pages as they load");
                var n1 = Api("/libre/").Count; Nav(fr, U + "/en.html"); Thread.Sleep(2000);
                Ok(Api("/libre/").Count == n1 && Eval(fr, "document.getElementById('a').textContent") == "Hello there", "translate: ... and leaves an English page alone (nothing sent)");
                Cmd("test-z-tr-always fr 0"); Nav(fr, U + "/fr2.html?b"); Thread.Sleep(2000);
                Ok(Api("/libre/").Count == n1 && Eval(fr, "document.getElementById('a').textContent") == "Merci beaucoup", "translate: with \"always\" off again, nothing is sent by itself");
                Cmd("test-z-tr-set none"); Thread.Sleep(300); st = J(Cmd("test-z-tr-state " + fr)); Ok(!B(st, "button") && !B(st, "ready"), "translate: service set back to None: the button goes");

                // ================= cast: what WebView2 has, and "Open in Edge to cast"
                var pres = Gesture(fr, "(async function(){var r={pres:typeof navigator.presentation,req:typeof window.PresentationRequest,secure:window.isSecureContext,cast:typeof (window.chrome||{}).cast};try{var q=new PresentationRequest(['https://example.com/']);r.avail=await Promise.race([q.getAvailability().then(function(x){return x.value},function(e){return 'err:'+e.name}),new Promise(function(z){setTimeout(function(){z('timeout')},4000)})]);}catch(e){r.availEx=e.name}try{r.start=await Promise.race([new PresentationRequest(['https://example.com/']).start().then(function(){return 'started'},function(e){return e.name+': '+e.message}),new Promise(function(z){setTimeout(function(){z('timeout')},3000)})]);}catch(e){r.startEx=e.name}return JSON.stringify(r);})()");
                Note("cast: WebView2 " + pres);
                var pd = js.Deserialize<Dictionary<string, object>>(pres ?? "{}");
                Ok(S(pd, "pres") == "object" && !(pd.ContainsKey("avail") && pd["avail"] is bool && (bool)pd["avail"]) && (S(pd, "start") ?? "").StartsWith("NotFoundError"), "cast: navigator.presentation exists but finds no screen (no Media Router in WebView2) - measured, so no cast button of our own");
                var cl = Val(Cmd("test-z-cast " + fr));
                Ok(cl == U + "/fr2.html?b" && File.ReadAllText(launchLog).Contains("edge " + U + "/fr2.html?b"), "cast: \"Open in Edge to cast\" hands the tab's address to Edge (written to the test log, Edge not started)");

                // ================= settings: sections, search, jump, Ctrl+K
                Cmd("test-z-settings-open "); Thread.Sleep(1200);
                var all = A(J("ok {\"x\":" + Val(Cmd("test-z-settings-all")) + "}"), "x");
                var secs = all.Select(x => x.Split('|')[0]).Distinct().ToList();
                Ok(secs.SequenceEqual(SettingsLayout.Sections), "settings: grouped in General, Privacy & security, Shield, Gaming, Appearance, Sync, Profiles, Advanced (" + all.Count + " cards)");
                Ok(all.Contains("General|Translate pages") && all.Contains("General|Cast to a TV") && all.Contains("Appearance|Taskbar") && all.Contains("Shield|Hallow Shield") && all.Contains("Advanced|About"), "settings: the new cards are in their sections");
                Cmd("test-z-settings-type cookies"); var ss = J(Cmd("test-z-settings-state"));
                Ok(A(ss, "cards").Contains("Privacy & security|Privacy") && A(ss, "bright").Contains("Block third-party cookies") && !A(ss, "cards").Contains("Appearance|Look") && !B(ss, "empty"), "settings: search \"cookies\" shows the Privacy card with that switch bright, hides the rest (" + S(ss, "count") + ")");
                Cmd("test-z-settings-type translate"); ss = J(Cmd("test-z-settings-state"));
                Ok(A(ss, "cards").First() == "General|Translate pages" && A(ss, "sections").Contains("General"), "settings: search \"translate\" finds Translate pages");
                Cmd("test-z-settings-type zzqx nothing"); ss = J(Cmd("test-z-settings-state")); Ok(B(ss, "empty") && I(ss, "shown") == 0, "settings: no match says so");
                Cmd("test-z-settings-type "); var so = D(J(Cmd("test-z-settings-state")), "offset");
                var secOff = Val(Cmd("test-z-settings-section Sync")); double sOff; double.TryParse(secOff, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sOff);
                Ok(sOff > 200, "settings: the section links scroll to the section (Sync at " + sOff + ")");
                Cmd("test-z-settings-type jump list"); var jj = J(Cmd("test-z-settings-jump"));
                Ok((S(jj, "target") ?? "").Contains("Jump list on the taskbar") && B(jj, "inView"), "settings: Enter jumps to the first matching setting (in view at " + D(jj, "y") + ")");
                var pal = L(Cmd("test-z-palette cookies"));
                var hit = pal.FirstOrDefault(x => S(x, "kind") == "Setting" && S(x, "title") == "Block third-party cookies");
                Ok(hit != null && S(hit, "sub") == "Settings > Privacy & security > Privacy", "Ctrl+K: finds the setting \"Block third-party cookies\" (Settings > Privacy & security > Privacy)");
                Cmd("test-activate 0"); var pr = Val(Cmd("test-z-palette-run Setting translate to"));
                ss = J(Cmd("test-z-settings-state"));
                Ok(pr != null && B(ss, "active") && (S(ss, "query") ?? "").Length > 0 && A(ss, "cards").Contains("General|Translate pages"), "Ctrl+K: picking a setting opens Settings at it (\"" + pr + "\" -> " + S(ss, "query") + ")");
                var q0 = S(ss, "query"); Cmd("test-z-palette-run Command gaming-light mode turn"); Thread.Sleep(600);
                Ok(S(J(Cmd("test-z-settings-state")), "query") == q0, "settings: a setting changed from Ctrl+K rebuilds the page and keeps your search");
                Cmd("test-z-palette-run Command gaming-light mode turn");

                // ================= the jump list (a test AppUserModelID only)
                var jl = J(Cmd("test-z-jump"));
                var cdDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent", "CustomDestinations");
                Func<List<string>> ours = () => { try { var needle = Encoding.Unicode.GetBytes("--jump private-window"); var exeNeedle = Encoding.Unicode.GetBytes(exe); return Directory.GetFiles(cdDir, "*.customDestinations-ms").Where(f => File.GetLastWriteTime(f) >= t0.AddSeconds(-2)).Where(f => { var b = File.ReadAllBytes(f); return IndexOf(b, needle) >= 0 && IndexOf(b, exeNeedle) >= 0; }).ToList(); } catch { return new List<string>(); } };
                var files = ours();
                Ok(B(jl, "ok") && S(jl, "appId") == appId && I(jl, "count") == 4 && A(jl, "items").Contains("New private window|--jump private-window|Tasks") && A(jl, "items").Contains("Default|--profile default|Profiles"), "jump list: committed to the test AppUserModelID (" + I(jl, "count") + " items" + (B(jl, "profilesAsTasks") ? ", profiles under the tasks" : "") + ")");
                Ok(files.Count == 1, "jump list: Windows saved it (" + files.Count + " list file with this exe's --jump items)");
                // --jump from a second start goes to the running browser through its pipe
                var tc = Tabs().Count;
                var j2 = launch(run, "--jump new-tab"); j2.WaitForExit(15000);
                Ok(Wait(() => Tabs().Count, n => n == tc + 1, 6000) == tc + 1, "jump list: \"New tab\" (--jump new-tab) opens a tab in the running browser");
                var j3 = launch(run, "--jump reading-list"); j3.WaitForExit(15000);
                Ok(S(Wait(() => J(Cmd("test-z-side")), d => S(d, "native") == "reading", 6000), "native") == "reading", "jump list: \"Reading list\" opens the reading list");
                var j4 = launch(run, "--jump private-window"); j4.WaitForExit(15000);
                Ok(I(Wait(() => J(Cmd("test-z-side")), d => I(d, "private") == 1, 8000), "private") == 1, "jump list: \"New private window\" opens one");
                var tc2 = Tabs().Count; var j5 = launch(run, "--jump https://evil.example/"); j5.WaitForExit(15000); Thread.Sleep(800);
                Ok(Tabs().Count == tc2 && !Tabs().Any(t => ((string)t["url"]).Contains("evil")), "jump list: an unknown --jump value opens nothing");
                Ok(Cmd("test-z-jump-delete") == "ok", "jump list: the test list is deleted");
                Thread.Sleep(800); Ok(ours().Count == 0, "jump list: ... and its file is gone");

                // ================= tab previews in the taskbar (stand-in windows; Windows' messages sent by the test)
                var th = J(Cmd("test-z-thumbs 1")); int ntabs = I(th, "tabs");
                Note("taskbar tabs: RegisterTab answered 0x" + I(th, "registerHr").ToString("X8") + " (the hover previews themselves need the real taskbar - not looked at)");
                Ok(I(th, "registered") == ntabs && A(th, "titles").Count == ntabs, "taskbar tabs: a stand-in window per tab (" + ntabs + "), named after its tab");
                var tm = J(Cmd("test-z-thumb-msg 1 thumb")); Note("taskbar tabs: DwmSetIconicThumbnail answered 0x" + I(tm, "hr").ToString("X8"));
                Ok(I(tm, "hr") == 0 && I(tm, "requests") == 1, "taskbar tabs: asked for a preview, the tab's picture goes to Windows (DwmSetIconicThumbnail S_OK)");
                tm = J(Cmd("test-z-thumb-msg 1 activate")); Ok(I(tm, "active") == 1, "taskbar tabs: clicking a preview switches to that tab");
                var last = ntabs - 1; tm = J(Cmd("test-z-thumb-msg " + last + " close")); Ok(I(tm, "tabs") == ntabs - 1, "taskbar tabs: the preview's X closes that tab");
                Thread.Sleep(700); th = J(Cmd("test-z-thumbs")); Ok(I(th, "registered") == ntabs - 1, "taskbar tabs: the closed tab's stand-in is gone");
                th = J(Cmd("test-z-thumbs 0")); Ok(I(th, "registered") == 0, "taskbar tabs: off again = no stand-ins");

                // ================= Shield's My filters through Shield's own page
                var shieldUp = Wait(() => J(Cmd("test-state")), d => S(d, "shield") == "extension", 30000);
                Ok(S(shieldUp, "shield") == "extension", "Shield runs (the built-in copy)");
                var filters = "||ads.hallow-test.example^\nhallow-test.example##.promo\n";
                Ok(Cmd("test-z-shf-set " + filters.Replace("\n", "\\n")).StartsWith("ok"), "My filters: written through Shield's page");
                Ok(js.Deserialize<string>(Val(Cmd("test-z-shf-get")) ?? "\"\"") == filters, "My filters: read back through Shield's page");
                var compiled = Wait(() => Val(Cmd("test-z-shf-eval chrome.storage.local.get({userSynced:null}).then(function(o){return o.userSynced})")), v => v != null && v.Contains("ads.hallow-test"), 10000);
                Ok(compiled != null && compiled.Contains("ads.hallow-test"), "My filters: Shield's background picked them up and compiled them (userSynced)");

                // ================= sync: My filters and history (encrypted) from this test profile to a second one
                Cmd("test-z-set synchist 1"); Cmd("test-z-set synchistmax 250");
                Cmd("test-z-hist-add https://history-only.example/raid Raid night plan");
                Cmd("test-m-sync-set " + syncFolder + " correct horse battery");
                var s1 = J(Cmd("test-m-sync-now"));
                var raw = File.ReadAllBytes(Path.Combine(syncFolder, SyncCore.FileName));
                Ok(S(s1, "state") == "synced" && SyncCore.IsEncrypted(raw) && IndexOf(raw, Encoding.UTF8.GetBytes("history-only.example")) < 0 && IndexOf(raw, Encoding.UTF8.GetBytes("ads.hallow-test")) < 0, "sync: the folder's copy is encrypted (no address, no filter readable)");
                var doc = SyncCore.Read(raw, "correct horse battery");
                Ok(doc.Records.Any(r => r.Kind == "shf" && r.Data.Contains("ads.hallow-test")) && doc.Records.Any(r => r.Kind == "hist" && r.Data.Contains("history-only.example")) && doc.Records.Count(r => r.Kind == "hist") <= 250, "sync: My filters and history (at most 250 pages) are in it");
                Cmd("quit"); br.WaitForExit(20000); Thread.Sleep(2000);
                // the second test profile (its own engine and Shield) joins the folder
                Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run2);
                br = launch(run2, null);
                Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000); Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000);
                Wait(() => J(Cmd("test-state")), d => S(d, "shield") == "extension", 30000); Thread.Sleep(1500);
                Cmd("test-z-set synchist 1");
                Cmd("test-m-sync-set " + syncFolder + " correct horse battery");
                var s2 = J(Cmd("test-m-sync-now")); Thread.Sleep(2500);
                var hist2 = Val(Cmd("test-z-hist")) ?? "";
                Ok(S(s2, "state") == "synced" && hist2.Contains("history-only.example/raid") && hist2.Contains("Raid night plan"), "sync: the history page arrives in the second profile (encrypted folder)");
                var f2 = Wait(() => Val(Cmd("test-z-shf-get")), v => v != null && v.Contains("ads.hallow-test"), 10000);
                Ok(f2 != null && js.Deserialize<string>(f2) == filters, "sync: My filters arrive and are written into the second profile's Shield");
                // history sync off on a PC with no password: nothing about history is written
                Cmd("test-m-sync-set off");
                var status = Val(Cmd("status")); Ok(status != null && status.Contains("\"version\":\"" + Program.Version + "\"") && !status.Contains("127.0.0.1") && !status.Contains("history-only"), "status: " + Program.Version + ", no addresses");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var id in Mine(exe)) try { using (var p = Process.GetProcessById(id)) if (p.StartTime >= t0) p.Kill(); } catch { }   // only this test's own processes of the repo's exe
                try { JumpListCleanup(appId); } catch { }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v17: ALL " + pass + " PASSED" : "INTEGRATION v17: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
        // the test's jump list never stays behind (deleted through the same API, for the test id only)
        // Runtime.evaluate with a user gesture (as if the page's button was clicked), awaiting the promise
        static string Gesture(int tab, string expr)
        {
            var r = Val(Cmd("test-x-cdp " + tab + " Runtime.evaluate " + js.Serialize(new Dictionary<string, object> { { "expression", expr }, { "awaitPromise", true }, { "returnByValue", true }, { "userGesture", true } })));
            try { var d = js.Deserialize<Dictionary<string, object>>(r); var res = d["result"] as Dictionary<string, object>; return res["value"] as string; } catch { return r; }
        }
        static void JumpListCleanup(string appId) { JumpList.Delete(appId); }
        static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++) { int j = 0; while (j < needle.Length && hay[i + j] == needle[j]) j++; if (j == needle.Length) return i; }
            return -1;
        }
    }
}
