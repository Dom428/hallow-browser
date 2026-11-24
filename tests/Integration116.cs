// Hallow Browser 1.16 - integration test "v116", off-screen (never shown, never focused, every page muted), its own test folder: a
// LOCAL stand-in for Google's update endpoint (HALLOW_BROWSER_CWS_URL) serving CRX3 files made here with throw-away keys, and a
// stand-in store page (chromewebstore.google.com mapped to 127.0.0.1 for this test browser only). Nothing is downloaded from Google.
//  - paste an ID: fetched once, checked, the card shows what it asks for; nothing loads before Add; Add loads it with its store ID;
//    its content script runs, its popup opens, which chrome.* APIs it has;
//  - a changed file and another extension's file are refused (nothing unpacked); Cancel leaves nothing behind;
//  - the store page's "Add to Hallow Browser" button; the same message from another site is ignored;
//  - "Check for updates": a newer version, through the same checks and card, keeps the ID and replaces the old copy.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v116
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
    public static class Integration116
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 60000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static object G(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o : null; }
        static string S(Dictionary<string, object> d, string k) { return G(d, k) as string; }
        static Dictionary<string, object> D(Dictionary<string, object> d, string k) { return G(d, k) as Dictionary<string, object>; }
        static List<Dictionary<string, object>> L(Dictionary<string, object> d, string k) { var a = G(d, k) as System.Collections.ArrayList; return a == null ? new List<Dictionary<string, object>>() : a.OfType<Dictionary<string, object>>().ToList(); }
        static string Eval(int tab, string expr) { return Val(Cmd("test-eval " + tab + " " + expr)); }

        // a small binary HTTP server (Host header + path + query); every request logged
        class Server
        {
            public int Port; TcpListener l; volatile bool stop; public readonly List<string> Log = new List<string>();
            public Func<string, string, Tuple<int, string, byte[]>> Route;
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            public int Count(string s) { lock (Log) return Log.Count(x => x.Contains(s)); }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var s = c.GetStream())
                    {
                        var head = new List<byte>(); int b; while ((b = s.ReadByte()) >= 0) { head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; }
                        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                        var path = lines[0].Split(' ')[1]; var host = lines.Where(x => x.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).Select(x => x.Substring(5).Trim().Split(':')[0]).FirstOrDefault() ?? "";
                        lock (Log) Log.Add(host + " " + path);
                        var r = Route(host, path) ?? Tuple.Create(404, "text/plain", Encoding.UTF8.GetBytes("not found"));
                        var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + r.Item1 + " X\r\nContent-Type: " + r.Item2 + "\r\nContent-Length: " + r.Item3.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        s.Write(h, 0, h.Length); s.Write(r.Item3, 0, r.Item3.Length); s.Flush();
                    }
                }
                catch { }
            }
        }
        static Tuple<int, string, byte[]> H(string html) { return Tuple.Create(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)); }
        static string Q(string path, string key) { var i = path.IndexOf('?'); if (i < 0) return null; foreach (var kv in path.Substring(i + 1).Split('&')) { var p = kv.Split(new[] { '=' }, 2); if (p[0] == key && p.Length == 2) return Uri.UnescapeDataString(p[1]); } return null; }

        public static int Main(string exe, string dir)
        {
            Directory.CreateDirectory(dir);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v116.txt"), false) { AutoFlush = true };

            // ---- made-up extensions, signed here with throw-away keys
            var k1 = CrxMaker.NewRsa(); var store = CrxMaker.NewRsa(); var k2 = CrxMaker.NewRsa(); var k3 = CrxMaker.NewRsa();
            var crx = new Dictionary<string, byte[]>(); var latest = new Dictionary<string, string>();
            crx[k1.Id] = CrxMaker.Build(CrxMaker.Zip(CrxMaker.TestExtension("1.0")), k1, null, store); latest[k1.Id] = "1.0";
            crx[k2.Id] = CrxMaker.Build(CrxMaker.Zip(CrxMaker.TestExtension("1.0")), k2, null, null, true);    // changed after signing
            crx[k3.Id] = crx[k1.Id];                                                                             // another extension's file
            var srv = new Server();
            srv.Route = (host, path) =>
            {
                if (path.StartsWith("/cws?"))
                {
                    var x = Q(path, "x") ?? ""; var id = x.Split('&').Select(p => p.Split('=')).Where(p => p[0] == "id").Select(p => p[1]).FirstOrDefault();
                    if (Q(path, "response") == "updatecheck") return Tuple.Create(200, "text/xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><gupdate xmlns=\"http://www.google.com/update2/response\" protocol=\"2.0\"><app appid=\"" + id + "\" status=\"ok\"><updatecheck codebase=\"http://127.0.0.1/x.crx\" version=\"" + (latest.ContainsKey(id ?? "") ? latest[id] : "0") + "\" status=\"" + (latest.ContainsKey(id ?? "") ? "ok" : "noupdate") + "\"/></app></gupdate>"));
                    byte[] b; return id != null && crx.TryGetValue(id, out b) ? Tuple.Create(200, "application/x-chrome-extension", b) : Tuple.Create(204, "text/plain", new byte[0]);
                }
                if (host == "chromewebstore.google.com" && path.StartsWith("/detail/")) return H("<!DOCTYPE html><html><head><title>Hallow Store Test - Chrome Web Store</title></head><body><h1>Hallow Store Test</h1><button>Add to Chrome</button></body></html>");
                if (host == "evil.localhost") return H("<!DOCTYPE html><title>evil</title><script>try{chrome.webview.postMessage('hallow:cws\\t" + k1.Id + "');}catch(e){}</script>");
                if (path.StartsWith("/page.html")) return H("<!DOCTYPE html><title>Test page</title><p>ok</p>");
                return null;
            };
            srv.Start(); var P = srv.Port;
            File.WriteAllText(Path.Combine(run, "settings.json"), "{\"HttpsOnly\":false,\"DefaultHintDismissed\":true}");
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "http://127.0.0.1:" + P + "/page.html") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_CWS_URL"] = "http://127.0.0.1:" + P + "/cws";
                psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = "MAP chromewebstore.google.com 127.0.0.1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = Path.Combine(dir, "game.txt"); File.WriteAllText(Path.Combine(dir, "game.txt"), "");
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(() => J(Cmd("test-v-shstate")), s => s != null && S(s, "state") == "extension", 25000);
                Ok(Cmd("test-mute 0") == "ok", "the test page is muted");
                Ok(srv.Count("/cws") == 0, "nothing is asked of the store before a click");

                // ---- paste an ID: fetched, checked, the card; nothing loaded before Add
                var a = J(Cmd("test-cws-add " + k1.Id)); Note("add: " + js.Serialize(a));
                var pend = D(a, "pending");
                Ok(pend != null && S(pend, "error") == null && S(pend, "name") == "Hallow Store Test" && S(pend, "version") == "1.0" && srv.Count("/cws?response=redirect") == 1, "paste an ID: fetched once from the (stand-in) update endpoint, signature and ID checked, the card shows it");
                var asks = (G(pend, "asks") as System.Collections.ArrayList ?? new System.Collections.ArrayList()).Cast<string>().ToList();
                Ok(asks.Any(x => x.Contains("Keep its own data")) && asks.Any(x => x.StartsWith("Read and change your data on 127.0.0.1")), "the card says what it asks for (" + string.Join("; ", asks) + ")");
                var x0 = J(Cmd("test-v-xlist")); var eng0 = (G(x0, "engine") as System.Collections.ArrayList ?? new System.Collections.ArrayList()).Cast<string>().ToList();
                Ok(!eng0.Any(e => e.Contains(k1.Id)) && L(x0, "mine").Count == 0, "nothing is loaded before you click Add");
                var cf = Cmd("test-cws-confirm"); Note("confirm: " + cf);
                var x1 = J(Cmd("test-v-xlist")); var mine = L(x1, "mine").FirstOrDefault(); var eng1 = (G(x1, "engine") as System.Collections.ArrayList ?? new System.Collections.ArrayList()).Cast<string>().ToList();
                Ok(cf == "ok" && mine != null && S(mine, "id") == k1.Id && eng1.Any(e => e.StartsWith("Hallow Store Test|" + k1.Id + "|True")), "Add: loaded with its store ID (" + (mine == null ? "-" : S(mine, "id")) + ")");
                Cmd("test-nav 0 http://127.0.0.1:" + P + "/page.html?2");
                Ok(Wait(() => Eval(0, "document.documentElement.getAttribute('data-hallow-store')"), v => v == "\"1.0\"", 6000) == "\"1.0\"", "its content script runs on pages");
                var pop = J(Cmd("test-v-xpopup " + k1.Id)); Note("popup: " + js.Serialize(pop));
                Ok(pop != null && (S(pop, "text") ?? "").Contains("Store test popup 1.0"), "its popup opens");
                Thread.Sleep(800); var apisRaw = Val(Cmd("test-cws-popupeval JSON.stringify(window.apis)")); Dictionary<string, object> apis = null; try { var s1 = js.Deserialize<object>(apisRaw); apis = js.Deserialize<Dictionary<string, object>>(s1 as string ?? apisRaw); } catch { } Note("APIs in its popup: " + js.Serialize(apis));
                Ok(apis != null && G(apis, "runtime") is bool && (bool)G(apis, "runtime") && (bool)G(apis, "storage"), "its popup runs with chrome.runtime and chrome.storage (" + js.Serialize(apis) + ")");

                // ---- refused files leave nothing behind; Cancel too
                var dirs0 = Directory.GetDirectories(Path.Combine(run, "Local", "Extensions")).Length;
                var b2 = J(Cmd("test-cws-add " + k2.Id)); Note("changed file: " + js.Serialize(D(b2, "pending")));
                Ok((S(D(b2, "pending"), "error") ?? "").Contains("signature"), "a changed file is refused (signature)");
                var b3 = J(Cmd("test-cws-add " + k3.Id));
                Ok((S(D(b3, "pending"), "error") ?? "").Contains("not " + k3.Id), "another extension's file for this ID is refused");
                var b4 = J(Cmd("test-cws-add " + "a".PadRight(32, 'b'))); Ok((S(D(b4, "pending"), "error") ?? "").Contains("no such extension"), "an ID the store doesn't have: said so");
                Ok(Directory.GetDirectories(Path.Combine(run, "Local", "Extensions")).Length == dirs0, "refused files: nothing unpacked");

                // ---- the store page's button; the same message from another site does nothing
                Cmd("test-nav 0 http://chromewebstore.google.com:" + P + "/detail/hallow-store-test/" + k1.Id);
                var btn = Wait(() => Eval(0, "(function(){var b=document.getElementById('hallow-cws-add');return b?b.textContent+'|'+b.dataset.id:'';})()"), v => v != null && v.Contains("|"), 8000);
                Ok(btn == "\"Add to Hallow Browser|" + k1.Id + "\"", "the store page gets an \"Add to Hallow Browser\" button (" + btn + ")");
                var f0 = srv.Count("/cws?response=redirect");
                Eval(0, "document.getElementById('hallow-cws-add').click()");
                var st = Wait(() => J(Cmd("test-cws-state")), s => s != null && D(s, "pending") != null && (S(D(s, "pending"), "name") != null || S(D(s, "pending"), "error") != null), 15000);
                Ok(S(D(st, "pending"), "name") == "Hallow Store Test" && srv.Count("/cws?response=redirect") == f0 + 1, "the button: fetched on that click, the card shows it (with Add / Cancel)");
                var dirs1 = Directory.GetDirectories(Path.Combine(run, "Local", "Extensions")).Length;
                Cmd("test-cws-cancel");
                Ok(Directory.GetDirectories(Path.Combine(run, "Local", "Extensions")).Length == dirs1 - 1 && D(J(Cmd("test-cws-state")), "pending") == null, "Cancel: nothing added, the unpacked copy removed");
                var f1 = srv.Count("/cws?response=redirect");
                Cmd("test-nav 0 http://evil.localhost:" + P + "/x"); Thread.Sleep(2500);
                Ok(srv.Count("/cws?response=redirect") == f1 && D(J(Cmd("test-cws-state")), "pending") == null, "the same message from another site is ignored (no fetch)");
                var hb = Eval(0, "!!document.getElementById('hallow-cws-add')"); Ok(hb == "false", "other sites get no button");

                // ---- updates: a newer version through the same checks and card
                crx[k1.Id] = CrxMaker.Build(CrxMaker.Zip(CrxMaker.TestExtension("1.1")), k1, null, store); latest[k1.Id] = "1.1";
                var oldFolder = S(L(J(Cmd("test-v-xlist")), "mine").First(), "folder");
                var up = J(Cmd("test-cws-updates")); Note("updates: " + js.Serialize(up));
                Ok(srv.Count("response=updatecheck") >= 1 && S(D(up, "pending"), "version") == "1.1" && G(D(up, "pending"), "update") is bool && (bool)G(D(up, "pending"), "update"), "Check for updates: the store's newer version is shown for your OK");
                Ok(Cmd("test-cws-confirm") == "ok", "Update: confirmed");
                var x2 = J(Cmd("test-v-xlist")); var m2 = L(x2, "mine");
                Ok(m2.Count == 1 && S(m2[0], "id") == k1.Id && !Directory.Exists(oldFolder) && Directory.Exists(S(m2[0], "folder")), "the update keeps the ID and replaces the old copy");
                Cmd("test-nav 0 http://127.0.0.1:" + P + "/page.html?3");
                Ok(Wait(() => Eval(0, "document.documentElement.getAttribute('data-hallow-store')"), v => v == "\"1.1\"", 6000) == "\"1.1\"", "the new version runs");
                var none = J(Cmd("test-cws-updates")); Ok(S(none, "result") == "Your Chrome Web Store extensions are up to date." && D(none, "pending") == null, "nothing newer: up to date");
                Ok(srv.Count("evil.localhost") >= 1 && srv.Log.All(l => !l.Contains("google.com/") || l.StartsWith("chromewebstore.google.com")), "only the local stand-ins were asked");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v116: ALL " + pass + " PASSED" : "INTEGRATION v116: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
