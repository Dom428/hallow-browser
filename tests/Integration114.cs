// Hallow Browser 1.14 - integration test "v114", off-screen like the others (never shown, never focused), its own test folder, made-up
// data and local test sites only (*.localhost on a loopback port; never the user's bookmarks or real Brave profile, nothing on the
// internet is contacted):
//  - the bookmarks bar before / after: letter tiles and a brand icon, then logos;
//  - the one-time "logos from the browser your bookmarks came from" against a FAKE Brave profile (HALLOW_BROWSER_IMPORT_ROOT) with a
//    made-up Favicons file: only Hallow's sites, marked done, not again;
//  - a tab's icon goes into the cache; a private window's icon does NOT (the engine did load it);
//  - the quiet fetch: a site's /favicon.ico (only that is requested), a site with only <link rel=icon> (its page's scripts are never
//    loaded), a site with none (remembered, not a candidate again), and nothing at all while a game runs;
//  - the bookmarks manager, the History page, Ctrl+K and the new tab tiles show the logos; the setting is in Settings.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v114
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
    public static class Integration114
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 60000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> JL(string reply) { var v = Val(reply); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static object G(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o : null; }
        static bool B(Dictionary<string, object> d, string k) { var o = G(d, k); return o is bool && (bool)o; }
        static string S(Dictionary<string, object> d, string k) { return G(d, k) as string; }
        static List<string> L(Dictionary<string, object> d, string k) { var a = G(d, k) as System.Collections.ArrayList; return a == null ? new List<string>() : a.Cast<object>().Select(x => x as string ?? js.Serialize(x)).ToList(); }
        static string KindOf(List<Dictionary<string, object>> items, string title) { var x = items.FirstOrDefault(i => S(i, "title") == title); return x == null ? null : S(x, "kind"); }

        // a local test server for *.localhost: binary responses, the Host header decides the site; every request logged
        class Server
        {
            public int Port; TcpListener l; volatile bool stop; public readonly List<string> Log = new List<string>();
            public Func<string, string, Tuple<int, string, byte[]>> Route;   // host, path -> status, content type, body
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            public List<string> For(string host) { lock (Log) return Log.Where(x => x.StartsWith(host + " ")).Select(x => x.Substring(host.Length + 1)).ToList(); }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var s = c.GetStream())
                    {
                        while (true)
                        {
                            var head = new List<byte>(); int b; while ((b = s.ReadByte()) >= 0) { head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; }
                            if (head.Count < 4) return;
                            var text = Encoding.ASCII.GetString(head.ToArray()); var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                            var path = lines[0].Split(' ')[1];
                            var hostLine = lines.FirstOrDefault(x => x.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) ?? "Host: ?";
                            var host = hostLine.Substring(5).Trim(); var colon = host.IndexOf(':'); if (colon > 0) host = host.Substring(0, colon);
                            lock (Log) Log.Add(host + " " + path);
                            var r = Route(host, path) ?? Tuple.Create(404, "text/html", Encoding.UTF8.GetBytes("<!DOCTYPE html><title>404</title>not found"));
                            var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + r.Item1 + (r.Item1 == 200 ? " OK" : " Not Found") + "\r\nContent-Type: " + r.Item2 + "\r\nContent-Length: " + r.Item3.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                            s.Write(hb, 0, hb.Length); s.Write(r.Item3, 0, r.Item3.Length); s.Flush();
                            return;
                        }
                    }
                }
                catch { }
            }
        }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v114.txt"), false) { AutoFlush = true };
            var red = V114.Png(32, 220, 40, 40); var blue = V114.Png(32, 40, 90, 230); var green = V114.Png(48, 30, 180, 60); var purple = V114.Png(16, 150, 60, 200);
            var srv = new Server();
            Func<string, string, byte[]> html = (title, headExtra) => Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>" + title + "</title>" + headExtra + "</head><body><h1>" + title + "</h1><p>Made-up test page.</p></body></html>");
            srv.Route = (host, path) =>
            {
                if (host == "pub.localhost") { if (path.StartsWith("/page")) return Tuple.Create(200, "text/html; charset=utf-8", html("Public page", "<link rel=icon href=/i.png>")); if (path == "/i.png") return Tuple.Create(200, "image/png", red); }
                if (host == "priv.localhost") { if (path.StartsWith("/page")) return Tuple.Create(200, "text/html; charset=utf-8", html("Private page", "<link rel=icon href=/i.png>")); if (path == "/i.png") return Tuple.Create(200, "image/png", purple); }
                if (host == "fetch.localhost") { if (path == "/favicon.ico") return Tuple.Create(200, "image/x-icon", blue); if (path == "/") return Tuple.Create(200, "text/html", html("Fetch home", "")); }
                if (host == "linkonly.localhost")
                {
                    if (path == "/") return Tuple.Create(200, "text/html; charset=utf-8", html("Link only", "<link rel=icon href=/brand.png><script src=/s.js></script><link rel=stylesheet href=/s.css>"));
                    if (path == "/brand.png") return Tuple.Create(200, "image/png", green);
                    if (path == "/s.js") return Tuple.Create(200, "text/javascript", Encoding.UTF8.GetBytes("document.title='scripts ran';"));
                    if (path == "/s.css") return Tuple.Create(200, "text/css", Encoding.UTF8.GetBytes("body{color:red}"));
                }
                if (host == "none.localhost" && path == "/") return Tuple.Create(200, "text/html", html("No icon", ""));
                return null;
            };
            srv.Start(); var P = srv.Port;
            Func<string, string> U = h => "http://" + h + ":" + P;

            // a FAKE Brave profile (made-up bookmarks and a made-up Favicons file) - never the real one
            var importRoot = Path.Combine(dir, "browsers");
            var bdir = Path.Combine(importRoot, "Local", "BraveSoftware", "Brave-Browser", "User Data", "Default"); Directory.CreateDirectory(bdir);
            File.WriteAllText(Path.Combine(bdir, "Bookmarks"), "{\"roots\":{\"bookmark_bar\":{\"type\":\"folder\",\"name\":\"Bookmarks bar\",\"children\":[{\"type\":\"url\",\"name\":\"Wanted\",\"url\":\"https://www.wanted.example/\"},{\"type\":\"url\",\"name\":\"Other\",\"url\":\"https://other.example/x\"}]}}}");
            File.WriteAllBytes(Path.Combine(bdir, "Favicons"), V114.SampleFavicons());
            Directory.CreateDirectory(Path.Combine(importRoot, "Roaming"));
            // the test profile: bookmarks as an earlier import left them, plus local test sites; "Fetch missing site logos" on
            var bm = new BookmarkStore(Path.Combine(run, "bookmarks.json")); bm.Load();
            bm.Add(BookmarkStore.BarId, "Wanted", "https://www.wanted.example/"); bm.Add(BookmarkStore.BarId, "Other", "https://other.example/x");
            bm.Add(BookmarkStore.BarId, "Pub", U("pub.localhost") + "/page"); bm.Add(BookmarkStore.BarId, "Fetch", U("fetch.localhost") + "/"); bm.Add(BookmarkStore.BarId, "Link", U("linkonly.localhost") + "/");
            bm.Add(BookmarkStore.BarId, "None", U("none.localhost") + "/"); bm.Add(BookmarkStore.BarId, "Twitch", "https://www.twitch.tv/"); bm.Save();
            File.WriteAllText(Path.Combine(run, "settings.json"), "{\"BookmarksBar\":true,\"BarRepair113\":true}");
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_IMPORT_ROOT"] = importRoot;
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Thread.Sleep(2500);

                // ================= the bar before: no globe - letter tiles and a brand icon
                var st0 = J(Cmd("test-fav-state"));
                Ok(st0 != null && !B(st0, "logos114") && B(st0, "fetchLogos") && Convert.ToInt32(G(st0, "count")) == 0, "start: the cache is empty, the one-time import hasn't run, the fetch setting is on");
                var bar0 = Wait(() => JL(Cmd("test-fav-bar")), b => b.Count >= 7, 15000);   // after the stores load
                Note("bar before: " + string.Join(" | ", bar0.Select(x => S(x, "title") + "=" + S(x, "kind"))));
                Ok(bar0.Count == 7 && KindOf(bar0, "Wanted") == "letter" && KindOf(bar0, "Pub") == "letter" && KindOf(bar0, "Twitch") == "brand", "bar: before any logo - letter tiles, and Twitch's brand icon (no plain globe)");

                // ================= the one-time logos from the browser the bookmarks came from (a FAKE Brave)
                var once = J(Cmd("test-fav-once 0"));
                Ok(once != null && B(once, "ran") && Convert.ToInt32(G(once, "saved")) == 2 && L(once, "from").SequenceEqual(new[] { "Brave" }), "once: 2 logos from the fake Brave's Favicons file (" + (once == null ? "?" : js.Serialize(once)) + ")");
                Ok((S(once, "toast") ?? "").Contains("Brave") && (S(once, "toast") ?? "").Contains("logos"), "once: a short message says where they came from: " + S(once, "toast"));
                var st1 = J(Cmd("test-fav-state"));
                Ok(B(st1, "logos114") && File.ReadAllText(Path.Combine(run, "settings.json")).Contains("\"Logos114\":true"), "once: marked done (runs once)");
                var once2 = J(Cmd("test-fav-once 0"));
                Ok(once2 != null && !B(once2, "ran"), "once: not again at the next try");
                var bar1 = Wait(() => JL(Cmd("test-fav-bar")), b => KindOf(b, "Wanted") == "favicon", 5000);
                Ok(KindOf(bar1, "Wanted") == "favicon" && KindOf(bar1, "Other") == "favicon" && KindOf(bar1, "Pub") == "letter", "bar: Wanted and Other show their logos now");

                // ================= a tab's icon goes into the cache
                Cmd("test-new " + U("pub.localhost") + "/page1");
                var pub = Wait(() => J(Cmd("test-fav-status " + U("pub.localhost") + "/")), x => B(x, "exact"), 15000);
                Ok(B(pub, "exact") && S(pub, "pick") == "favicon", "tab: the page's icon is kept for the site");
                var bar2 = Wait(() => JL(Cmd("test-fav-bar")), b => KindOf(b, "Pub") == "favicon", 5000);
                Ok(KindOf(bar2, "Pub") == "favicon", "bar: Pub's bookmark shows the tab's icon (redrawn when it arrived)");

                // ================= a private window: the engine loads the icon, the cache does NOT get it
                Cmd("test-v-wpriv " + U("priv.localhost") + "/page2");
                var wins = Wait(() => JL(Cmd("test-fav-wins")), w => w.Any(x => B(x, "private") && B(x, "favicon")), 15000);
                Ok(wins.Any(x => B(x, "private") && B(x, "favicon")) && srv.For("priv.localhost").Contains("/i.png"), "private window: its tab has the site's icon (the engine loaded /i.png)");
                Thread.Sleep(1500);
                var priv = J(Cmd("test-fav-status " + U("priv.localhost") + "/"));
                Ok(priv != null && !B(priv, "exact") && (S(priv, "pick") ?? "").StartsWith("letter:"), "private window: its site's icon was NOT written to the cache (" + S(priv, "pick") + ")");

                // ================= the quiet fetch
                var f1 = J(Cmd("test-fav-fetch " + U("fetch.localhost"), 90000));
                Ok(L(f1, "got").Contains("fetch.localhost") && B(J(Cmd("test-fav-status " + U("fetch.localhost") + "/")), "exact"), "fetch: the site's own /favicon.ico is fetched and kept (" + S(f1, "last") + ")");
                Ok(srv.For("fetch.localhost").Distinct().SequenceEqual(new[] { "/favicon.ico" }), "fetch: only /favicon.ico was requested from that site (" + string.Join(", ", srv.For("fetch.localhost")) + ")");
                var f2 = J(Cmd("test-fav-fetch " + U("linkonly.localhost"), 90000));
                var linkLog = srv.For("linkonly.localhost");
                Ok(L(f2, "got").Contains("linkonly.localhost"), "fetch: a site without /favicon.ico -> the icon its page names (<link rel=icon>)");
                Ok(linkLog.Contains("/favicon.ico") && linkLog.Contains("/") && linkLog.Contains("/brand.png") && !linkLog.Contains("/s.js") && !linkLog.Contains("/s.css"), "fetch: ... the page's scripts and styles were never loaded (" + string.Join(", ", linkLog) + ")");
                var f3 = J(Cmd("test-fav-fetch " + U("none.localhost"), 90000));
                Ok(f3 != null && L(f3, "got").Count == 0 && !B(J(Cmd("test-fav-status " + U("none.localhost") + "/")), "exact"), "fetch: a site with no icon -> nothing saved");
                var cand = L(J("ok {\"c\":" + (Val(Cmd("test-fav-candidates")) ?? "[]") + "}"), "c");
                Note("candidates now: " + string.Join(", ", cand));
                Ok(!cand.Any(c => c.Contains("none.localhost")) && !cand.Any(c => c.Contains("fetch.localhost")) && !cand.Any(c => c.Contains("wanted.example")) && cand.Contains("https://www.twitch.tv"), "fetch: once per site - tried or found sites aren't candidates again; Twitch (no logo yet) is, over https");
                var bar3 = JL(Cmd("test-fav-bar"));
                Ok(KindOf(bar3, "Fetch") == "favicon" && KindOf(bar3, "Link") == "favicon" && KindOf(bar3, "None") == "letter", "bar: fetched logos show; a site without one keeps its letter tile");
                // never during a game
                File.WriteAllText(gameFile, "Fake Game"); Cmd("test-a-chip");
                Thread.Sleep(800); Cmd("test-a-chip");
                var before = srv.Log.Count;
                var tick = J(Cmd("test-fav-tick", 30000));
                Ok(tick != null && !B(tick, "allowed") && srv.Log.Count == before, "fetch: nothing at all while a game runs");
                File.WriteAllText(gameFile, ""); Cmd("test-a-chip");

                // ================= the other places
                var mg = JL(Cmd("test-fav-manager"));
                Ok(KindOf(mg, "Wanted") == "favicon" && KindOf(mg, "Twitch") == "brand" && KindOf(mg, "None") == "letter", "bookmarks manager: logos, the brand icon and letter tiles (" + string.Join(" | ", mg.Select(x => S(x, "title") + "=" + S(x, "kind"))) + ")");
                var hp = JL(Cmd("test-fav-history"));
                Ok(hp.Any(x => (S(x, "url") ?? "").Contains("pub.localhost") && S(x, "kind") == "favicon"), "History page: the visited site shows its logo (" + string.Join(" | ", hp.Take(4).Select(x => S(x, "url") + "=" + S(x, "kind"))) + ")");
                var pal = JL(Cmd("test-fav-palette Pub"));
                Ok(pal.Any(x => S(x, "kind") == "Bookmark" && S(x, "pick") == "favicon"), "Ctrl+K: the bookmark shows its logo");
                var pal2 = JL(Cmd("test-fav-palette Fetch missing site logos"));
                Ok(pal2.Any(x => S(x, "kind") == "Setting"), "Settings: \"Fetch missing site logos\" is there (found by Ctrl+K)");
                var dataFile = Path.Combine(run, "Local", "newtab", "data.json");
                Cmd("test-new " + Omni.NewTabUrl);
                var data = Wait(() => File.Exists(dataFile) ? File.ReadAllText(dataFile) : "", x => x.Contains("\"i\":\"ico/"), 70000);
                Ok(data.Contains("\"i\":\"ico/"), "new tab page: the top site's tile has its logo");
                Ok(!Directory.Exists(Path.Combine(run, "Local", "Profile", "favicons")) || Directory.GetFiles(Path.Combine(run, "Local", "Profile", "favicons"), "*.png").Length == Convert.ToInt32(G(J(Cmd("test-fav-state")), "count")), "cache: in the test profile's Profile\\favicons folder (" + Convert.ToInt32(G(J(Cmd("test-fav-state")), "count")) + " logos)");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v114: ALL " + pass + " PASSED" : "INTEGRATION v114: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
