// Hallow Browser 1.16.2 - integration test "v1162", off-screen like the others (muted: test browsers start with --mute-audio), its own
// test folder and profile, LOCAL stand-ins only: "accounts.google.com" and "site.test" are mapped to a local HTTPS stand-in (a
// throw-away self-signed certificate, test-only flag) - nothing goes to Google. Real use: "Sign in with Google" on a site ended on
// Google's "400 ... malformed" page; an extension closing its login tab closed the whole browser.
//  - control: Shield is on and strips gclid from an ordinary site's address, and Hallow sends Sec-GPC there;
//  - a site opens Google's sign-in pop-up (window.open, a moment after any click): it opens (not blocked), keeps window.opener, and
//    the request Google gets has every parameter (gclid, fbclid, _gl, s_cid ...) and Hallow didn't touch its headers;
//  - with a game running, the tab that opened the pop-up stays awake and the pop-up <-> opener messages go both ways;
//  - the sign-in's answer posted back to the site (form_post, from Google's page) keeps its address parameters and its form body;
//  - an extension closing the last tab (chrome.tabs.remove) or the window (chrome.windows.remove): the browser stays open with a
//    new tab page;
//  - after Exit, none of the test browser's WebView2 processes are left.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v1162
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration1162
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static int Num(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -1; }
        static List<bool> Dark() { object o; var st = State(); return st.TryGetValue("dark", out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Cast<object>().Select(x => (bool)x).ToList() : new List<bool>(); }
        static Dictionary<string, object> State() { try { return js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-netlog state")) ?? "{}"); } catch { return new Dictionary<string, object>(); } }

        // ---- the local HTTPS stand-in: every request written down (method, host, path, headers, body)
        class Hit { public string Method, Host, Path, Body; public Dictionary<string, string> H; }
        static readonly List<Hit> hits = new List<Hit>();
        static List<Hit> Hits(Func<Hit, bool> f) { lock (hits) return hits.Where(f).ToList(); }
        class Server
        {
            public int Port; TcpListener l; readonly X509Certificate2 cert; volatile bool stop;
            public Func<Hit, string> Route;
            public Server(X509Certificate2 c) { cert = c; }
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var ss = new SslStream(c.GetStream(), false))
                    {
                        ss.AuthenticateAsServer(cert, false, System.Security.Authentication.SslProtocols.Tls12, false);
                        while (true)
                        {
                            var head = new List<byte>();
                            while (true) { int b = ss.ReadByte(); if (b < 0) return; head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; if (n > 65536) return; }
                            var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                            var first = lines[0].Split(' ');
                            var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var ln in lines.Skip(1)) { var i = ln.IndexOf(':'); if (i > 0) h[ln.Substring(0, i).Trim()] = ln.Substring(i + 1).Trim(); }
                            string v; int len = h.TryGetValue("Content-Length", out v) ? int.Parse(v) : 0;
                            var body = new byte[len]; int got = 0; while (got < len) { int r = ss.Read(body, got, len - got); if (r <= 0) return; got += r; }
                            var hit = new Hit { Method = first[0], Path = first[1], H = h, Host = (h.TryGetValue("Host", out v) ? v : "").Split(':')[0], Body = Encoding.UTF8.GetString(body) };
                            if (!hit.Path.StartsWith("/favicon")) lock (hits) hits.Add(hit);
                            var page = Route(hit);
                            var bytes = Encoding.UTF8.GetBytes(page ?? "not found");
                            var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + (page == null ? "404 Not Found" : "200 OK") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nCache-Control: no-store\r\n\r\n");
                            ss.Write(hb, 0, hb.Length); ss.Write(bytes, 0, bytes.Length); ss.Flush();
                        }
                    }
                }
                catch { }
            }
        }

        const string AuthQuery = "client_id=123-abc.apps.googleusercontent.com&scope=openid%20profile%20email&redirect_uri=gis_transform&prompt=consent&access_type=offline&response_type=code&include_granted_scopes=true&state=st%2F1%3D&gclid=g1&fbclid=f1&_gl=gl1&s_cid=sc1&utm_source=u1";
        static string Page(string title, string body) { return "<!DOCTYPE html><html><head><meta charset=utf-8><title>" + title + "</title></head><body>" + body + "</body></html>"; }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v1162.txt"), false) { AutoFlush = true };
            var cert = TlsServer.TestCert();
            var srv = new Server(cert); string G = null, S = null;
            srv.Route = q =>
            {
                var pure = q.Path.Split('?')[0];
                if (q.Host == "site.test" && pure == "/plain") return Page("plain", "plain page");
                if (q.Host == "site.test" && pure == "/start")
                    return Page("Sign in to Site", "<script>window.got=[];addEventListener('message',function(e){if(e.data&&e.data.ping){window.got.push(e.data.ping);e.source.postMessage({pong:e.data.ping},'*');}});" +
                        "window.go=function(){setTimeout(function(){window.pop=window.open('" + G + "/o/oauth2/v2/auth?" + AuthQuery + "','g_auth_code_window_1','width=500,height=600');},300);return 'asked';};</script>");
                if (q.Host == "accounts.google.com" && pure == "/o/oauth2/v2/auth")
                    return Page("Sign in - Google Accounts (stand-in)", "Email or phone<form id=f method=post action='" + S + "/cb?gclid=keep3&state=st1&fbclid=keep5'><input name=code value='4/0Ab-c0de'><input name=state value=st1><input name=fbclid value=keep4></form>" +
                        "<script>window.hadOpener=!!window.opener;window.pongs=[];var n=0;addEventListener('message',function(e){if(e.data&&e.data.pong)window.pongs.push(e.data.pong);});" +
                        "setInterval(function(){if(window.opener){n++;window.opener.postMessage({ping:n},'*');}},400);window.sendBack=function(){document.getElementById('f').submit();return 'sent';};</script>");
                if (q.Host == "site.test" && pure == "/cb") return Page("signed in", "signed in");
                if (q.Host == "site.test" && pure == "/closeme") return Page("close me", "an extension closes this");
                if (q.Host == "www.google.com" && pure == "/setprefs") return Page("prefs", "<script>document.cookie='THEME=2; path=/; max-age=3600; secure';</script>saved");
                if (q.Host == "www.google.com" && pure == "/search") return Page("Google (stand-in)", "results");
                return null;
            };
            srv.Start(); G = "https://accounts.google.com:" + srv.Port; S = "https://site.test:" + srv.Port;
            // a test extension that closes the tab (or its window) it's asked to - like an extension closing its own login tab
            var ext = Path.Combine(dir, "closer-extension"); Directory.CreateDirectory(ext);
            File.WriteAllText(Path.Combine(ext, "manifest.json"), "{\"manifest_version\":3,\"name\":\"Hallow Closer Test\",\"version\":\"1.0\",\"description\":\"test\",\"permissions\":[\"tabs\"],\"background\":{\"service_worker\":\"bg.js\"},\"content_scripts\":[{\"matches\":[\"https://site.test/*\"],\"js\":[\"c.js\"],\"run_at\":\"document_idle\"}]}");
            File.WriteAllText(Path.Combine(ext, "bg.js"), "chrome.runtime.onMessage.addListener(function(m,s,reply){var t=s.tab;if(!t){reply({err:'no tab'});return;}" +
                "if(m.close==='tab'){chrome.tabs.remove(t.id).then(function(){reply({ok:'tab'})},function(e){reply({err:String(e)})});return true;}" +
                "if(m.close==='window'){chrome.windows.remove(t.windowId).then(function(){reply({ok:'window'})},function(e){reply({err:String(e)})});return true;}});");
            File.WriteAllText(Path.Combine(ext, "c.js"), "var mm=/[?&]mode=(\\w+)/.exec(location.search);if(mm&&location.pathname==='/closeme')setTimeout(function(){chrome.runtime.sendMessage({close:mm[1]},function(r){document.title='reply '+JSON.stringify(r||chrome.runtime.lastError&&chrome.runtime.lastError.message);});},500);");
            var game = Path.Combine(run, "game.txt"); File.WriteAllText(game, "");
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = game;
                psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = "MAP accounts.google.com 127.0.0.1, MAP www.google.com 127.0.0.1, MAP site.test 127.0.0.1";
                psi.EnvironmentVariables["HALLOW_BROWSER_IGNORE_CERTS"] = "1";
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                Cmd("test-mute 0"); Cmd("test-netlog on");
                var shield = Wait(() => Val(Cmd("test-state")) ?? "", s => s.Contains("\"shield\":\"extension\""), 30000);
                Ok(shield.Contains("\"shield\":\"extension\""), "Hallow Shield runs as the extension");

                // ---- control: an ordinary site - Shield strips gclid, Hallow sends Sec-GPC
                List<Hit> plain = null;
                for (int i = 0; i < 12; i++) { Cmd("test-nav 0 " + S + "/plain?gclid=g9&x=1&n=" + i); plain = Hits(h => h.Host == "site.test" && h.Path.StartsWith("/plain") && h.Path.Contains("n=" + i)); if (plain.Count > 0 && !plain[0].Path.Contains("gclid")) break; Thread.Sleep(1500); }
                Ok(plain != null && plain.Count > 0 && !plain[0].Path.Contains("gclid") && plain[0].Path.Contains("x=1"), "control: on an ordinary site Shield still strips gclid (" + (plain != null && plain.Count > 0 ? plain[0].Path : "-") + ")");
                string hv;
                Ok(plain != null && plain.Count > 0 && plain[0].H.TryGetValue("Sec-GPC", out hv) && hv == "1", "control: and Hallow still sends Global Privacy Control there");

                var sets = Val(Cmd("test-shield-eval chrome.declarativeNetRequest.getEnabledRulesets().then(function(r){return r.join(',')})")) ?? "";
                Ok(sets.Contains("hallow-signin") && sets.Contains("ublock-privacy"), "Shield has the sign-in ruleset on, next to its lists: " + sets);
                Cmd("test-nav 0 " + G + "/o/oauth2/v2/auth?" + AuthQuery + "&direct=1");
                var direct = Hits(h => h.Host == "accounts.google.com" && h.Path.Contains("direct=1"));
                Ok(direct.Count > 0 && direct.All(h => h.Path == "/o/oauth2/v2/auth?" + AuthQuery + "&direct=1"), "a sign-in page opened in a tab keeps every parameter (" + (direct.Count > 0 ? direct[0].Path.Length + " chars" : "none") + ")");
                lock (hits) hits.RemoveAll(h => h.Path.Contains("direct=1"));

                // ---- the sign-in pop-up, with a game running (background tabs sleep at once)
                File.WriteAllText(game, "Fake Game"); Cmd("test-tick");
                Ok(Wait(() => Val(Cmd("test-state")) ?? "", s => s.Contains("\"game\":\"Fake Game\""), 15000).Contains("Fake Game"), "game mode is on (a fake game)");
                if (Environment.GetEnvironmentVariable("HALLOW_TEST_ALLOW_POPUPS") == "1") Note("(comparison run: pop-ups allowed for site.test) " + Cmd("test-v-rule site.test popups allow"));
                Cmd("test-nav 0 " + S + "/start");
                Ok(Eval(0, "window.go()") == "asked", "the site asks for Google's sign-in pop-up 0.3 s later (no click: like sites that ask their server first)");
                var tabs = Wait(Tabs, t => t.Count >= 2 && (bool)t[1]["ready"], 15000);
                Ok(tabs.Count == 2 && ((tabs[1]["url"] as string) ?? "").StartsWith(G + "/o/oauth2/v2/auth"), "the pop-up opens next to the site (not blocked): " + (tabs.Count > 1 ? tabs[1]["url"] : "-"));
                var auth = Wait(() => Hits(h => h.Host == "accounts.google.com" && h.Path.StartsWith("/o/oauth2/v2/auth")), l => l.Count > 0, 10000);
                Ok(auth.Count > 0 && auth.All(h => h.Path == "/o/oauth2/v2/auth?" + AuthQuery), "the sign-in request reaches Google unchanged - every parameter, gclid / fbclid / _gl / s_cid / utm_source included (" + (auth.Count > 0 ? auth[0].Path.Length + " chars" : "none") + ")");
                Ok(auth.Count > 0 && auth.All(h => !h.H.ContainsKey("Sec-GPC")) && auth.All(h => h.H.TryGetValue("Referer", out hv) && hv.StartsWith(S)), "its headers are the engine's own: Hallow added nothing (no Sec-GPC), the Referer is the site");
                Note("the sign-in pop-up's first request reached the stand-in " + auth.Count + " time(s)");
                Ok(Eval(1, "String(window.hadOpener)") == "true", "the pop-up keeps window.opener");
                Thread.Sleep(1500); Cmd("test-tick"); Thread.Sleep(1500); Cmd("test-tick");
                var st = State();
                var asleep = Tabs().Select(x => (bool)x["asleep"]).ToList();
                Ok(asleep.Count == 2 && !asleep[0], "game mode: the tab that opened the sign-in pop-up stays awake (" + js.Serialize(st) + ")");
                var p0 = Eval(1, "String(window.pongs.length)"); Thread.Sleep(2000); var p1 = Eval(1, "String(window.pongs.length)");
                int a, b; int.TryParse(p0, out a); int.TryParse(p1, out b);
                Ok(b > a && b >= 3, "the pop-up and the site talk both ways (" + a + " -> " + b + " answers from the site)");
                Ok(Eval(1, "window.sendBack()") == "sent", "the stand-in sign-in page posts its answer back to the site (form_post)");
                var cb = Wait(() => Hits(h => h.Host == "site.test" && h.Path.StartsWith("/cb")), l => l.Count > 0, 10000);
                Ok(cb.Count > 0 && cb[0].Method == "POST" && cb[0].Path == "/cb?gclid=keep3&state=st1&fbclid=keep5", "the site's return address keeps its parameters (" + (cb.Count > 0 ? cb[0].Method + " " + cb[0].Path : "none") + ")");
                Ok(cb.Count > 0 && cb[0].Body == "code=4%2F0Ab-c0de&state=st1&fbclid=keep4" && cb[0].H.TryGetValue("Origin", out hv) && hv == G && !cb[0].H.ContainsKey("Sec-GPC"), "and its form body and Origin arrive unchanged, nothing added by Hallow (" + (cb.Count > 0 ? cb[0].Body : "-") + ")");
                Ok(Num(State(), "signInLeftAlone") >= 2, "Hallow counted the sign-in requests it left alone");
                Cmd("test-close 1"); Thread.Sleep(800);
                File.WriteAllText(game, ""); Cmd("test-tick");

                // ---- an extension closing the last tab, then the window
                var xl = Cmd("test-v-xload " + ext, 60000);
                Ok(xl != null && xl.StartsWith("ok"), "the closer test extension loads: " + xl);
                while (Tabs().Count > 1) { Cmd("test-close " + (Tabs().Count - 1)); Thread.Sleep(400); }
                var kept0 = Num(State(), "pageClosesKept");
                Cmd("test-nav 0 " + S + "/closeme?mode=tab");
                var after = Wait(() => { var t = Tabs(); return t.Count == 1 && ((t[0]["url"] as string) ?? "").Contains("closeme") ? null : t; }, t => t != null && t.Count >= 1, 15000);
                Thread.Sleep(1500);
                bool alive = !br.HasExited && Val(Cmd("ping", 3000)) != null; tabs = Tabs();
                Ok(alive && tabs.Count == 1 && !((tabs[0]["url"] as string) ?? "").Contains("closeme") && Num(State(), "pageClosesKept") == kept0 + 1,
                    "an extension closing the last tab (chrome.tabs.remove): the browser stays open with a new tab page (" + (alive ? js.Serialize(tabs.Select(t => t["url"])) : "the browser EXITED") + ")");
                if (alive)
                {
                    int t1 = Convert.ToInt32(Val(Cmd("test-new " + S + "/plain?two=1")) ?? "-1"); Thread.Sleep(1500);
                    Cmd("test-nav 0 " + S + "/closeme?mode=window"); Thread.Sleep(6000);
                    alive = !br.HasExited && Val(Cmd("ping", 3000)) != null; tabs = alive ? Tabs() : new List<Dictionary<string, object>>();
                    var st2 = alive ? State() : new Dictionary<string, object>();
                    Ok(alive && tabs.Count == 1 && ((tabs[0]["url"] as string) ?? "").Contains("two=1") && Num(st2, "windows") == 1 && Num(st2, "foreignRefused") >= 1,
                        "an extension closing the window (chrome.windows.remove - WebView2 sends the window WM_CLOSE and shuts the asking page): the window stays, the page that asked is closed, your other tab stays (" + (alive ? js.Serialize(tabs.Select(t => t["url"])) + " " + js.Serialize(st2) : "the browser EXITED") + ")");
                    var e0 = Eval(0, "String(6*7)"); var n0 = Cmd("test-nav 0 " + S + "/plain?after=0", 15000);
                    Ok(e0 == "42" && n0 != null && n0.StartsWith("ok") && Hits(h => h.Path.Contains("after=0")).Count > 0, "and the remaining tab still works (script: " + e0 + ", navigation: " + n0 + ")");
                }

                // ---- Google's own light / dark choice ("www.google.com" is the local stand-in) with "Make pages dark" on
                if (!br.HasExited)
                {
                    while (Tabs().Count > 1) { Cmd("test-close " + (Tabs().Count - 1)); Thread.Sleep(400); }
                    Cmd("test-netlog colors dark true");
                    Cmd("test-nav 0 " + S + "/plain?theme=1"); Thread.Sleep(800);
                    Ok(Dark().SequenceEqual(new[] { true }), "theme: \"Make pages dark\" still darkens an ordinary site");
                    var W = "https://www.google.com:" + srv.Port;
                    const string prefs = "/setprefs?sig=0_abc%3D&prev=https%3A%2F%2Fwww.google.com%2F&cs=2&gclid=g7";
                    Cmd("test-nav 0 " + W + prefs); Thread.Sleep(800);
                    var sp = Hits(h => h.Host == "www.google.com" && h.Path.StartsWith("/setprefs"));
                    Note("Google's preference address arrived as " + (sp.Count > 0 ? sp[0].Path : "none") + "");
                    Ok(sp.Count > 0 && sp[0].Path == prefs, "theme: Google's preference address (Dark theme On / Off / Device default) arrives exactly as sent - Shield leaves it alone");
                    Cmd("test-nav 0 " + W + "/search?q=x"); Thread.Sleep(600); Eval(0, "location.reload()"); Thread.Sleep(1500);
                    var ck = Eval(0, "document.cookie") ?? "";
                    Ok(ck.Contains("THEME=2"), "theme: Google's own preference cookie is kept across reloads (" + ck + ")");
                    Ok(Dark().SequenceEqual(new[] { false }), "theme: Hallow doesn't darken Google's pages - Google draws its own light or dark theme");
                    Ok(Eval(0, "String(matchMedia('(prefers-color-scheme: dark)').matches)") == "true", "theme: pages \"dark\" -> sites see prefers-color-scheme: dark");
                    Cmd("test-netlog colors light true"); Eval(0, "location.reload()"); Thread.Sleep(1500);
                    Ok(Eval(0, "String(matchMedia('(prefers-color-scheme: light)').matches)") == "true", "theme: pages \"light\" -> prefers-color-scheme: light");
                    Cmd("test-netlog colors follow false"); Eval(0, "location.reload()"); Thread.Sleep(1500);
                    object lo; var light = State().TryGetValue("light", out lo) && lo is bool && (bool)lo;
                    Ok(Eval(0, "String(matchMedia('(prefers-color-scheme: " + (light ? "light" : "dark") + ")').matches)") == "true", "theme: pages \"follow the browser\" -> prefers-color-scheme matches the browser's look (" + (light ? "light" : "dark") + ")");
                }

                // ---- Exit: none of the test browser's WebView2 processes are left
                if (!br.HasExited) { Cmd("quit", 5000); br.WaitForExit(15000); }
                Ok(br.HasExited, "the browser exits on quit");
                var left = Wait(() => WebViewsOf(run), n => n == 0, 15000);
                Ok(left == 0, "after Exit no WebView2 process of this browser is left (" + left + ")");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop(); try { cert.Dispose(); } catch { }
                var msg = fail == 0 ? "INTEGRATION v1162: ALL " + pass + " PASSED" : "INTEGRATION v1162: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
        // msedgewebview2.exe processes whose command line has this test's folder (read through WMI)
        static int WebViewsOf(string folder)
        {
            try
            {
                var script = "@(Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | Where-Object { $_.CommandLine -like '*" + folder.Replace("'", "''") + "*' }).Count";
                var ps = new ProcessStartInfo("powershell", "-NoProfile -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(ps)) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(20000); int n; return int.TryParse(o.Trim(), out n) ? n : -1; }
            }
            catch { return -1; }
        }
    }
}
