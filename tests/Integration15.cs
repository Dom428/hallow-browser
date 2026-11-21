// Hallow Browser 1.5 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own test
// folder, profile and pipes, local pages and local stand-ins only: a stand-in Hallow Keys (made-up logins), a stand-in for the
// search engine's suggestions (never a real engine, no real query ever sent), a stand-in MpCmdRun (never a real Defender scan), a
// fake game file. Mouse gestures and form keys go into the off-screen page through DevTools input - never the real mouse/keyboard.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v15
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration15
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null || v == "null" ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> L(string reply) { var v = Val(reply); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static List<Dictionary<string, object>> Tabs() { return L(Cmd("test-tabs")); }
        static List<Dictionary<string, object>> Wins() { return L(Cmd("test-v-wlist")); }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static int I(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        static long Lg(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt64(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static List<object> A(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Cast<object>().ToList() : new List<object>(); }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { return v == null ? null : js.Deserialize<object>(v) as string ?? v; } catch { return v; } }
        static string Nav(int tab, string url) { return Cmd("test-nav " + tab + " " + url); }
        static string Url(int tab) { var t = Tabs(); return tab < t.Count ? (string)t[tab]["url"] : null; }
        static int Active() { var t = Tabs(); for (int i = 0; i < t.Count; i++) if ((bool)t[i]["active"]) return i; return -1; }
        // new tabs open next to the active one: the last tab is activated first, so earlier tabs keep their numbers
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Thread.Sleep(300); return i; }
        // brightness at the middle of the tab's picture (CapturePreviewAsync)
        static int Bright(string dir, int tab)
        {
            var f = Path.Combine(dir, "cap-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".png");
            Thread.Sleep(700);
            if (Val(Cmd("test-capture " + tab + " " + f)) == null) return -1;
            try { using (var b = new System.Drawing.Bitmap(f)) { var px = b.GetPixel(b.Width / 2, b.Height / 2); return (px.R + px.G + px.B) / 3; } } catch { return -1; } finally { try { File.Delete(f); } catch { } }
        }
        static void Key(int tab, string key, int vk)
        {
            Cmd("test-x-cdp " + tab + " Input.dispatchKeyEvent " + js.Serialize(new Dictionary<string, object> { { "type", "keyDown" }, { "key", key }, { "code", key }, { "text", "\r" }, { "unmodifiedText", "\r" }, { "windowsVirtualKeyCode", vk } }));
            Cmd("test-x-cdp " + tab + " Input.dispatchKeyEvent " + js.Serialize(new Dictionary<string, object> { { "type", "keyUp" }, { "key", key }, { "code", key }, { "windowsVirtualKeyCode", vk } }));
        }
        static byte[] Wav(int seconds)
        {
            int rate = 8000, n = rate * seconds; var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2); for (int i = 0; i < n; i++) w.Write((short)0); w.Flush(); return ms.ToArray();   // silence: nothing is heard
        }

        public static int Main(string exe, string dir)
        {
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v15.txt"), false) { AutoFlush = true };
            var keys = new FakeKeys(); int P = 0;
            var suggestHits = new List<string>(); var wav = Wav(40);
            var srv = new SlowServer();
            srv.Route = (path) =>
            {
                Func<string, Tuple<string, byte[]>> H = s => Tuple.Create("text/html; charset=utf-8", Encoding.UTF8.GetBytes(s));
                if (path.StartsWith("/login.html")) return H("<!DOCTYPE html><html><head><title>Sign in</title></head><body><form id=f onsubmit=\"event.preventDefault();document.title='signed in';\"><input id=u type=email><input id=p type=password><button id=go>Sign in</button></form></body></html>");
                if (path.StartsWith("/frame.html")) return H("<!DOCTYPE html><html><head><title>Frame host</title></head><body><iframe id=fr src=\"http://localhost:" + P + "/framed.html\"></iframe></body></html>");
                if (path.StartsWith("/framed.html")) return H("<!DOCTYPE html><html><body><script>try{chrome.webview.postMessage('hallow:login\\t{\"u\":\"framed\",\"p\":\"fake-frame-1\"}');}catch(e){}</script>framed</body></html>");
                if (path.StartsWith("/white.html")) return H("<!DOCTYPE html><html><head><title>White page</title></head><body style='background:#fff;color:#000;margin:0'><div style='height:3000px'>A plain white page</div></body></html>");
                if (path.StartsWith("/one.html")) return H("<!DOCTYPE html><html><head><title>Page one</title></head><body style='height:2000px'>one</body></html>");
                if (path.StartsWith("/two.html")) return H("<!DOCTYPE html><html><head><title>Page two</title></head><body style='height:2000px'>two</body></html>");
                if (path.StartsWith("/media.html")) return H("<!DOCTYPE html><html><head><title>Media test</title></head><body><video id=v src='/silent.wav' loop autoplay width=320 height=180></video><script>window.nextHits=0;navigator.mediaSession.metadata=new MediaMetadata({title:'Test track',artist:'Hallow tests'});navigator.mediaSession.setActionHandler('nexttrack',function(){window.nextHits++;});</script></body></html>");
                if (path.StartsWith("/silent.wav")) return Tuple.Create("audio/wav", wav);
                if (path.StartsWith("/echo")) return H("<!DOCTYPE html><title>echo</title>echo");
                if (path.StartsWith("/suggest")) { lock (suggestHits) suggestHits.Add(path); var q = Uri.UnescapeDataString(path.Substring(path.IndexOf("q=") + 2)); return Tuple.Create("application/json", Encoding.UTF8.GetBytes(js.Serialize(new object[] { q, new[] { q + " one", q + " two" } }))); }
                return null;
            };
            srv.Start(); P = srv.Port; keys.SavedUrl = "http://127.0.0.1:" + P; keys.Start();
            var U1 = "http://127.0.0.1:" + P; var U2 = "http://localhost:" + P;
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            var mp = Path.Combine(dir, "MpCmdRun-standin.cmd");
            File.WriteAllText(mp, "@echo off\r\necho %* | find /i \"threat-test\" >nul && (echo Threat                  : Test:Fake/HallowThreat& echo found 1 threats.& exit /b 2)\r\necho Scan finished. Scanning found no threats.\r\nexit /b 0\r\n");
            Process br = null;
            Func<Process> launch = () =>
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                psi.EnvironmentVariables["HALLOW_BROWSER_SUGGEST_URL"] = U1 + "/suggest";
                psi.EnvironmentVariables["HALLOW_BROWSER_MPCMDRUN"] = mp;
                var pr = Process.Start(psi); try { pr.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return pr;
            };
            try
            {
                br = launch();
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count > 0 && (bool)t[0]["ready"], 30000);
                var info0 = J(Cmd("test-x-info")); Ok(S(info0, "info") == null && B(info0, "marker"), "a first start: no \"didn't close properly\" note; the running marker is there");

                // ================= address bar
                Func<string, List<Dictionary<string, object>>> Rows = t => L(Cmd("test-x-omni " + t));
                var r1 = Rows("200*1.2"); Ok(r1.Count > 0 && S(r1[0], "kind") == "calc" && S(r1[0], "text") == "= 240", "address bar: 200*1.2 shows = 240 first");
                var r2 = Rows("5 km in miles"); Ok(r2.Count > 0 && S(r2[0], "text") == "= 3.10686 miles", "address bar: 5 km in miles = 3.10686 miles");
                var r3 = Rows("yt cats"); Ok(r3.Any(x => S(x, "kind") == "keyword" && S(x, "text") == "Search YouTube for \"cats\""), "address bar: yt cats offers a YouTube search (not opened in tests)");
                Cmd("test-x-set keyword loc|" + U1 + "/echo?q=%s");
                Cmd("test-type loc hallow test"); var kwu = Wait(() => Url(Active()), u => u != null && u.Contains("/echo?q="), 8000);
                Ok(kwu != null && kwu.StartsWith(U1 + "/echo?q=hallow%20test"), "address bar: a keyword of your own opens its search (" + kwu + ")");
                int tOne = NewTab(U1 + "/one.html"); int tTwo = NewTab(U1 + "/two.html");
                var r4 = Rows("% page one"); Ok(r4.Count >= 1 && S(r4[0], "kind") == "tab" && S(r4[0], "text").StartsWith("Page one"), "address bar: \"% page one\" finds the open tab");
                Cmd("test-type % page one"); Ok(Wait(Active, a => a == tOne, 4000) == tOne, "address bar: Enter on a tab search switches to that tab");
                Ok(L(Cmd("test-x-omni % nothing-like-this")).All(x => S(x, "text").StartsWith("No open tab")), "address bar: no match says so");
                var hitsBefore = suggestHits.Count;
                var sg0 = J(Cmd("test-x-suggest hallo")); var rows0 = Rows("hallo");
                Ok(!B(sg0, "allowed") && suggestHits.Count == hitsBefore && !rows0.Any(x => S(x, "kind") == "engine"), "search suggestions: OFF by default - nothing sent to the (stand-in) engine");
                Cmd("test-x-set suggest true");
                var sg1 = J(Cmd("test-x-suggest hallo")); var rows1 = Rows("hallo");
                Ok(B(sg1, "allowed") && suggestHits.Count > hitsBefore && suggestHits.Last().Contains("q=hallo") && rows1.Any(x => S(x, "kind") == "engine" && S(x, "text") == "hallo one"), "search suggestions: when switched on, the stand-in engine's suggestions show");
                Cmd("test-x-set suggest false");

                // ================= save logins to Hallow Keys (stand-in, made-up logins)
                int tLogin = NewTab(U1 + "/login.html");
                Func<string, string, bool> fillAndEnter = (user, pass) =>
                {
                    Eval(tLogin, "document.getElementById('u').value=" + js.Serialize(user) + ";var p=document.getElementById('p');p.value=" + js.Serialize(pass) + ";p.focus();'ok'");
                    Key(tLogin, "Enter", 13); Thread.Sleep(1800); return true;
                };
                fillAndEnter("newuser@example.com", "fake-pass-9");
                var lg = Wait(() => J(Cmd("test-x-login")), d => B(d, "pending"), 8000);
                Ok(B(lg, "pending") && S(lg, "state") == "new" && S(lg, "user") == "newuser@example.com" && S(lg, "host") == "127.0.0.1", "save logins: after a real Enter in the form, \"Save to Hallow Keys?\" (new) - " + S(lg, "note"));
                Ok(Val(Cmd("test-x-loginanswer save")) == "new" && keys.Saved.Any(x => x.Item1 == U1 && x.Item2 == "newuser@example.com" && x.Item3 == "fake-pass-9"), "save logins: Save hands it to Keys with the page's origin (Keys' own save request)");
                int offers = I(J(Cmd("test-x-login")), "offers");
                fillAndEnter("newuser@example.com", "fake-pass-9"); Thread.Sleep(600);
                Ok(I(J(Cmd("test-x-login")), "offers") == offers && !B(J(Cmd("test-x-login")), "pending"), "save logins: the same login again isn't offered (Keys says it's saved)");
                fillAndEnter("newuser@example.com", "fake-pass-10");
                lg = Wait(() => J(Cmd("test-x-login")), d => B(d, "pending"), 8000);
                Ok(S(lg, "state") == "update", "save logins: a new password offers an update");
                Ok(Val(Cmd("test-x-loginanswer later")) == "later" && !keys.Saved.Any(x => x.Item3 == "fake-pass-10"), "save logins: Not now saves nothing");
                fillAndEnter("other@example.com", "fake-pass-11");
                lg = Wait(() => J(Cmd("test-x-login")), d => B(d, "pending"), 8000);
                Ok(Val(Cmd("test-x-loginanswer never")) == "never" && A(J(Cmd("test-x-login")), "never").Contains("127.0.0.1"), "save logins: Never for this site is remembered");
                offers = I(J(Cmd("test-x-login")), "offers");
                fillAndEnter("third@example.com", "fake-pass-12"); Thread.Sleep(500);
                Ok(I(J(Cmd("test-x-login")), "offers") == offers && !keys.Saved.Any(x => x.Item3 == "fake-pass-12"), "save logins: never offered again on that site");
                Cmd("test-x-set never");
                Eval(tLogin, "document.getElementById('u').value='scripted@example.com';document.getElementById('p').value='fake-pass-13';document.getElementById('f').dispatchEvent(new Event('submit',{cancelable:true}));'ok'"); Thread.Sleep(900);
                Ok(I(J(Cmd("test-x-login")), "offers") == offers, "save logins: a submit made by a script (not you) offers nothing");
                int tFrame = NewTab(U1 + "/frame.html"); Thread.Sleep(1200);
                Ok(I(J(Cmd("test-x-login")), "offers") == offers && !keys.Saved.Any(x => x.Item2 == "framed"), "save logins: a message from a cross-origin frame is never taken");
                var pw = Val(Cmd("test-v-wpriv " + U1 + "/login.html")); int pwi; int.TryParse(pw, out pwi);
                Thread.Sleep(3500);
                Cmd("test-v-weval " + pwi + " 0 (function(){document.getElementById('u').value='priv@example.com';var p=document.getElementById('p');p.value='fake-pass-14';p.focus();return 1;})()");
                Ok(I(J(Cmd("test-x-login")), "offers") == offers, "save logins: never offered in a private window (the capture script isn't even there)");
                Ok(Cmd("test-v-weval " + pwi + " 0 typeof window.__hallowLogins") == "ok \"undefined\"", "save logins: private windows have no capture script");
                Cmd("test-v-wclose " + pwi); Thread.Sleep(800);
                Cmd("test-v-savenow"); Thread.Sleep(500);
                var leaked = Directory.GetFiles(run, "*", SearchOption.TopDirectoryOnly).Concat(Directory.Exists(Path.Combine(run, "crashes")) ? Directory.GetFiles(Path.Combine(run, "crashes")) : new string[0])
                    .Where(f => { try { var t = File.ReadAllText(f); return t.Contains("fake-pass-"); } catch { return false; } }).ToList();
                Ok(leaked.Count == 0, "save logins: no password in the browser's own files (settings, session, history, crashes)" + (leaked.Count > 0 ? " - " + string.Join(", ", leaked.Select(Path.GetFileName)) : ""));

                // ================= dark pages (Chromium's auto dark mode through DevTools)
                int tWhite = NewTab(U1 + "/white.html");
                int b0 = Bright(dir, tWhite); Ok(b0 > 200, "dark pages: the white test page is white (" + b0 + ")");
                Cmd("test-x-set dark true"); int b1 = Bright(dir, tWhite);
                Ok(b1 >= 0 && b1 < 80, "dark pages: \"Make pages dark\" darkens it at once (" + b1 + ")");
                Nav(tWhite, U1 + "/white.html?again"); int b1b = Bright(dir, tWhite); Ok(b1b >= 0 && b1b < 80, "dark pages: still dark after a navigation (" + b1b + ")");
                Cmd("test-x-darktoggle " + tWhite); int b2 = Bright(dir, tWhite);
                Ok(b2 > 200 && S(J(Cmd("test-x-dark " + tWhite)), "global") == null && !B(J(Cmd("test-x-dark " + tWhite)), "wants"), "dark pages: this site set to \"never dark\" while the rest stay dark (" + b2 + ")");
                Nav(tWhite, U2 + "/white.html"); int b3 = Bright(dir, tWhite); Ok(b3 >= 0 && b3 < 80, "dark pages: another site is still made dark (" + b3 + ")");
                Cmd("test-x-set dark false"); int b4 = Bright(dir, tWhite); Ok(b4 > 200, "dark pages: off again (" + b4 + ")");
                Cmd("test-x-darktoggle " + tWhite); int b5 = Bright(dir, tWhite); Ok(b5 >= 0 && b5 < 80, "dark pages: one site made dark on its own (" + b5 + ")");
                Cmd("test-x-darktoggle " + tWhite);

                // ================= downloads: Defender (stand-in), pause / resume / cancel, the limit while gaming, the list
                Cmd("test-x-set scan true");
                Func<Dictionary<string, object>> Dl = () => J(Cmd("test-x-dl"));
                Func<Dictionary<string, object>, string, Dictionary<string, object>> item = (d, name) => A(d, "items").Cast<Dictionary<string, object>>().FirstOrDefault(x => S(x, "name") != null && S(x, "name").StartsWith(name));
                Eval(tOne, "var a=document.createElement('a');a.href='/small.bin';a.download='hallow-small.bin';document.body.appendChild(a);a.click();'ok'");
                var dl1 = Wait(Dl, d => { var x = item(d, "hallow-small"); return x != null && (S(x, "scan") == "clean" || S(x, "scan") == "error"); }, 20000);
                var x1 = item(dl1, "hallow-small");
                Ok(x1 != null && S(x1, "state") == "done" && S(x1, "scan") == "clean", "downloads: a finished download is checked by Defender (stand-in) - clean");
                Nav(tOne, U1 + "/one.html?dl2"); Eval(tOne, "var a=document.createElement('a');a.href='/small.bin?2';a.download='threat-test.bin';document.body.appendChild(a);a.click();'ok'");
                var dl2 = Wait(Dl, d => { var x = item(d, "threat-test"); return x != null && S(x, "scan") != null && S(x, "scan") != "scanning"; }, 20000);
                var x2 = item(dl2, "threat-test");
                Ok(x2 != null && S(x2, "scan") == "threat" && (S(x2, "note") ?? "").Contains("HallowThreat"), "downloads: a threat is flagged with Defender's (stand-in) name for it");
                Ok(A(dl2, "history").Cast<string>().Any(h => h.StartsWith("hallow-small.bin|done|clean")) && A(dl2, "history").Cast<string>().Any(h => h.Contains("threat-test.bin|done|threat")), "downloads: both are in the downloads list with the badge");
                Nav(tOne, U1 + "/one.html?dl3"); Eval(tOne, "var a=document.createElement('a');a.href='/slow.bin';a.download='hallow-slow.bin';document.body.appendChild(a);a.click();'ok'");
                var dl3 = Wait(Dl, d => { var x = item(d, "hallow-slow"); return x != null && Lg(x, "got") > 200000; }, 15000);
                int si = A(dl3, "items").Cast<Dictionary<string, object>>().ToList().FindIndex(x => S(x, "name").StartsWith("hallow-slow"));
                Cmd("test-x-dlact " + si + " pause"); Thread.Sleep(700);
                var g1 = Lg(item(Dl(), "hallow-slow"), "got"); Thread.Sleep(1500); var g2 = Lg(item(Dl(), "hallow-slow"), "got");
                Ok(S(item(Dl(), "hallow-slow"), "state") == "paused" && g2 == g1, "downloads: Pause stops it (" + g1 + " -> " + g2 + ")");
                Cmd("test-x-dlact " + si + " resume"); Thread.Sleep(1500); var g3 = Lg(item(Dl(), "hallow-slow"), "got");
                Ok(S(item(Dl(), "hallow-slow"), "state") == "running" && g3 > g2, "downloads: Resume goes on (" + g3 + ")");
                // the limit while a game runs (256 KB/s; the stand-in server sends about 2 MB/s)
                File.WriteAllText(gameFile, "Fake Game"); Cmd("test-tick"); Thread.Sleep(1500); var z1 = Lg(item(Dl(), "hallow-slow"), "got"); Thread.Sleep(2000); var zr = (Lg(item(Dl(), "hallow-slow"), "got") - z1) / 2048; Ok(zr > 700, "downloads: game mode doesn't put the downloading tab to sleep (a sleeping tab stalls its download) - " + zr + " KB/s with no limit"); File.WriteAllText(gameFile, ""); Cmd("test-tick"); Thread.Sleep(1000);
                Cmd("test-x-set dllimit 256"); File.WriteAllText(gameFile, "Fake Game"); Cmd("test-tick");
                Thread.Sleep(500); var l1 = Lg(item(Dl(), "hallow-slow"), "got"); Thread.Sleep(10000); var l2 = Lg(item(Dl(), "hallow-slow"), "got");
                var limited = (l2 - l1) / 10.0 / 1024; Note("download while gaming: " + limited.ToString("0") + " KB/s (limit 256) " + Cmd("test-x-dl"));
                Ok(limited > 50 && limited < 900 && S(Dl(), "limit") == "limited", "downloads: slowed a lot while a game runs (limit 256 KB/s: " + limited.ToString("0") + " KB/s over 10 s, the stand-in sends ~2 MB/s; bursty - data already on its way when a pause lands still arrives)");
                File.WriteAllText(gameFile, ""); Cmd("test-tick"); Thread.Sleep(1200);
                var f1 = Lg(item(Dl(), "hallow-slow"), "got"); Thread.Sleep(2000); var f2 = Lg(item(Dl(), "hallow-slow"), "got");
                var free = (f2 - f1) / 2.0 / 1024; Note("download after the game: " + free.ToString("0") + " KB/s");
                Ok(free > 700 || S(item(Dl(), "hallow-slow"), "state") == "done", "downloads: full speed again after the game (" + free.ToString("0") + " KB/s)");
                Cmd("test-x-set dllimit -1"); File.WriteAllText(gameFile, "Fake Game"); Cmd("test-tick"); Thread.Sleep(1500);
                var p1 = Lg(item(Dl(), "hallow-slow"), "got"); Thread.Sleep(1500); var p2 = Lg(item(Dl(), "hallow-slow"), "got");
                Ok(S(item(Dl(), "hallow-slow"), "state") == "done" || (p2 == p1 && S(Dl(), "limit") == "paused"), "downloads: \"Pause them\" while gaming pauses the download");
                File.WriteAllText(gameFile, ""); Cmd("test-tick"); Cmd("test-x-set dllimit 0"); Thread.Sleep(800);
                Cmd("test-x-dlact " + si + " cancel"); Thread.Sleep(800);
                var st3 = S(item(Dl(), "hallow-slow"), "state"); Ok(st3 == "cancelled" || st3 == "done", "downloads: Cancel (" + st3 + ")");
                var rowsDl = Val(Cmd("test-x-dlpage")); int nrows; int.TryParse(rowsDl, out nrows);
                Ok(nrows >= 3, "downloads: hallow://downloads lists them (" + nrows + " rows)");
                Cmd("test-close " + Active());

                // ================= tabs: previews, hover, drag picture, gestures, reopen closed window
                Cmd("test-activate " + tTwo); Thread.Sleep(1500);
                var pv = J(Cmd("test-x-preview " + tTwo)); Ok(B(pv, "has") && I(pv, "w") == 240, "tab previews: a 240 px picture is taken while the tab is on screen");
                Cmd("test-activate " + tOne); Thread.Sleep(400);
                var hv = J(Cmd("test-x-hover " + tTwo)); Ok(B(hv, "image") && (S(hv, "text") ?? "").Contains("Page two"), "tab previews: hovering a background tab shows its picture and title");
                var gh = J(Cmd("test-x-ghost " + tTwo)); Ok(B(gh, "image") && (S(gh, "text") ?? "").Contains("Page two"), "tear-off: the drag picture is the tab's preview");
                Cmd("test-x-set gestures true"); Thread.Sleep(300);
                Nav(tOne, U1 + "/one.html?a"); Nav(tOne, U1 + "/two.html?b"); Cmd("test-activate " + tOne); Thread.Sleep(500);
                var gs = J(Cmd("test-x-gesture " + tOne + " L")); Thread.Sleep(800);
                Ok((Url(tOne) ?? "").Contains("one.html?a") && (S(gs, "last") ?? "").StartsWith("L=back"), "gestures: right-drag left = back (" + S(gs, "last") + ")");
                gs = J(Cmd("test-x-gesture " + tOne + " R")); Thread.Sleep(800);
                Ok((Url(tOne) ?? "").Contains("two.html?b"), "gestures: right-drag right = forward");
                Ok(I(gs, "menus") == 0, "gestures: the page's menu didn't open after a gesture");
                int before = Tabs().Count; int tg = NewTab(U1 + "/one.html?close"); Cmd("test-activate " + tg); Thread.Sleep(400);
                Cmd("test-x-gesture " + tg + " DR"); Thread.Sleep(800); gs = J(Cmd("test-x-gstate"));
                Ok(Tabs().Count == before && (S(gs, "last") ?? "").StartsWith("DR=close"), "gestures: down then right closes the tab (" + S(gs, "last") + ", tabs " + Tabs().Count + " vs " + before + ")");
                Cmd("test-x-set gestures false"); Thread.Sleep(300);
                int doneBefore = I(gs, "done"); gs = J(Cmd("test-x-gesture " + tOne + " L")); Thread.Sleep(600);
                Ok(I(gs, "done") == doneBefore && (Url(tOne) ?? "").Contains("two.html?b"), "gestures: off = nothing happens (" + I(gs, "done") + " vs " + doneBefore + ", " + Url(tOne) + ")");
                var wn = Val(Cmd("test-v-wnew " + U1 + "/one.html?win")); int wni; int.TryParse(wn, out wni); Thread.Sleep(4000);
                int wcount = Wins().Count; Cmd("test-v-wclose " + wni); Thread.Sleep(1500);
                Ok(Val(Cmd("test-x-closedwins")) == "1" && Wins().Count == wcount - 1, "windows: a closed window is remembered");
                Cmd("test-x-reopenwin"); Thread.Sleep(4000);
                var ws = Wins(); Ok(ws.Count == wcount && ws.Any(x => ((System.Collections.ArrayList)x["tabs"]).Cast<Dictionary<string, object>>().Any(t => ((string)t["url"]).Contains("one.html?win"))), "windows: Reopen closed window brings it back with its tab");
                for (int k = 0; k < 5 && Wins().Count > 1; k++) { Cmd("test-v-wclose " + (Wins().Count - 1)); Thread.Sleep(800); }
                Thread.Sleep(1000);

                // ================= media controls
                int tMedia = NewTab(U1 + "/media.html"); Thread.Sleep(2500);
                var md = Wait(() => J(Cmd("test-x-media")), d => A(d, "tabs").Count > 0 && A(d, "tabs").Cast<Dictionary<string, object>>().Any(x => B(x, "playing")), 10000);
                var mt = A(md, "tabs").Cast<Dictionary<string, object>>().FirstOrDefault(x => I(x, "i") == tMedia);
                Ok(mt != null && B(mt, "playing") && B(mt, "video") && B(mt, "next") && S(mt, "title") == "Test track - Hallow tests", "media: the playing tab is listed (title from the page, video, next)");
                Ok(B(md, "button"), "media: the toolbar's media button shows");
                Ok(Val(Cmd("test-x-mediaact 0 " + tMedia + " toggle")) == "paused" && Eval(tMedia, "document.getElementById('v').paused") == "true", "media: pause from the browser");
                Ok(Val(Cmd("test-x-mediaact 0 " + tMedia + " toggle")) == "playing" && Wait(() => Eval(tMedia, "document.getElementById('v').paused"), v => v == "false", 3000) == "false", "media: play again");
                Ok(Val(Cmd("test-x-mediaact 0 " + tMedia + " next")) == "ok" && Eval(tMedia, "String(window.nextHits)") == "1", "media: next calls the page's own next-track handler");
                Cmd("test-x-set autopip true"); Cmd("test-activate " + tMedia); Thread.Sleep(600); Cmd("test-activate " + tOne);
                var ap = Wait(() => J(Cmd("test-x-autopip")), d => I(d, "count") >= 1, 6000);
                Ok(I(ap, "count") >= 1 && A(ap, "popped").Count > tMedia && (bool)A(ap, "popped")[tMedia], "media: switching away pops the playing video out (optional)");
                Cmd("test-activate " + tMedia); Thread.Sleep(800);
                Ok(!(bool)A(J(Cmd("test-x-autopip")), "popped")[tMedia], "media: coming back puts it back in the tab");
                Cmd("test-x-set autopip false");

                // ================= crashes
                Cmd("test-activate " + tTwo); Thread.Sleep(400);
                Cmd("test-x-crash " + tTwo);
                var cs = Wait(() => J(Cmd("test-x-crashstate")), d => A(d, "tabs").Count > tTwo && (bool)A(d, "tabs")[tTwo], 10000);
                Ok(A(cs, "tabs").Count > tTwo && (bool)A(cs, "tabs")[tTwo] && (bool)A(cs, "viewsHidden")[tTwo], "crash: a crashed tab shows \"This tab crashed - Reload\" (the dead page is hidden)");
                Cmd("test-x-crashreload " + tTwo);
                var title = Wait(() => Eval(tTwo, "document.title"), v => v == "Page two", 10000);
                Ok(title == "Page two" && !(bool)A(J(Cmd("test-x-crashstate")), "tabs")[tTwo], "crash: Reload brings the page back");

                // ================= private flyouts
                var pw2 = Val(Cmd("test-v-wpriv")); int pwi2; int.TryParse(pw2, out pwi2); Thread.Sleep(3000);
                var fp = J(Cmd("test-x-flyout " + pwi2)); var fn = J(Cmd("test-x-flyout 0"));
                Ok(B(fp, "private") && B(fp, "fixed") && (S(fp, "panel") ?? "").EndsWith("251538"), "private windows: flyouts use the private palette (" + S(fp, "panel") + ")");
                Ok(!B(fn, "fixed") && !(S(fn, "panel") ?? "").EndsWith("251538"), "normal windows: flyouts keep the normal palette (" + S(fn, "panel") + ")");
                Cmd("test-v-wclose " + pwi2); Thread.Sleep(1000);

                // ================= the browser itself didn't close properly
                Cmd("test-x-set restore false"); Cmd("test-v-savenow"); Thread.Sleep(1500);
                int tabsThen = Tabs().Count;
                br.Kill(); br.WaitForExit(10000); Thread.Sleep(3000);
                br = launch(); Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000); Wait(Tabs, t => t.Count > 0, 20000); Thread.Sleep(2000);
                var inf = J(Cmd("test-x-info")); int fresh = Tabs().Count;
                Ok((S(inf, "info") ?? "").Contains("didn't close properly") && fresh == 1, "crash recovery: after a crash (session restore off) it says so and offers your tabs");
                Cmd("test-x-infoclick"); var back = Wait(Tabs, t => t.Count >= tabsThen - 1, 6000);
                Ok(back.Count >= tabsThen - 1 && back.Any(t => ((string)t["url"]).StartsWith(U1)), "crash recovery: \"Restore your tabs\" brings them back (" + back.Count + " of " + tabsThen + ")");
                Cmd("test-x-set restore true"); Cmd("quit"); br.WaitForExit(15000);
                Ok(br.HasExited && !File.Exists(Path.Combine(run, "running.flag")), "a normal quit removes the running marker");
                br = launch(); Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000); Wait(Tabs, t => t.Count > 0, 20000); Thread.Sleep(1500);
                Ok(S(J(Cmd("test-x-info")), "info") == null, "after a normal quit: no crash note");
                var status = Val(Cmd("status")); Ok(status != null && status.Contains("\"version\":\"1.5\"") && status.Contains("\"forceDark\":false") && !status.Contains("127.0.0.1"), "status: 1.5 fields, no addresses");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                keys.Stop(); srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v15: ALL " + pass + " PASSED" : "INTEGRATION v15: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }

    // a small HTTP server with bytes bodies and a slow download (about 2 MB/s) for pause / resume and the gaming limit
    public class SlowServer
    {
        public int Port; System.Net.Sockets.TcpListener l; volatile bool stop;
        public Func<string, Tuple<string, byte[]>> Route;
        public void Start()
        {
            l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start(); Port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start();
        }
        public void Stop() { stop = true; try { l.Stop(); } catch { } }
        void Serve(System.Net.Sockets.TcpClient c)
        {
            try
            {
                using (c) using (var s = c.GetStream())
                {
                    var r = new StreamReader(s, Encoding.ASCII); var first = r.ReadLine(); if (first == null) return; string line; while (!string.IsNullOrEmpty(line = r.ReadLine())) { }
                    var path = first.Split(' ')[1];
                    if (path.StartsWith("/slow.bin") || path.StartsWith("/small.bin"))
                    {
                        bool slow = path.StartsWith("/slow"); long total = slow ? 64L * 1024 * 1024 : 40000;
                        var head = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + total + "\r\nConnection: close\r\n\r\n";
                        var hb = Encoding.ASCII.GetBytes(head); s.Write(hb, 0, hb.Length);
                        var chunk = new byte[slow ? 64 * 1024 : 40000]; long sent = 0;
                        while (sent < total && !stop) { var n = (int)Math.Min(chunk.Length, total - sent); s.Write(chunk, 0, n); sent += n; if (slow) Thread.Sleep(30); }
                        return;
                    }
                    var res = Route(path);
                    var body = res == null ? Encoding.ASCII.GetBytes("not found") : res.Item2;
                    var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + (res == null ? "404 Not Found" : "200 OK") + "\r\nContent-Type: " + (res == null ? "text/plain" : res.Item1) + "\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                    s.Write(h, 0, h.Length); s.Write(body, 0, body.Length); s.Flush();
                }
            }
            catch { }
        }
    }
}
