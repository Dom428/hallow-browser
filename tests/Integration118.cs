// Hallow Browser 1.18 - integration test "v118", off-screen like the others (muted: test browsers start with --mute-audio, and each
// tab is muted too), its own test folder, LOCAL pages only (a 127.0.0.1 server of its own; *.googlesyndication.com is mapped to it so
// Shield has something to block). The Brave-style top bar:
//  - the clean address (no "http://") while the bar isn't focused, the full one when it is, "Not secure" on http, the padlock's panel;
//  - Customize toolbar: hide (right-click menu), show, move, the panel's switch - kept across a restart; Reset to default;
//  - page actions that come and go: zoom (click = 100%), share (copy link, a QR code read back), install as app (a page with a
//    manifest), reader, pop out a playing video;
//  - Shield's badge: the number blocked on a page with a blocked request;
//  - the puzzle piece's list, a pin (a small unpacked extension made here), the pinned button opening its popup - kept across a restart;
//  - the downloads ring on a slow local download, the pop-up's latest five;
//  - the tab strip: even widths, the minimum, arrows when it overflows, close on hover, the tab search button.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v118
using System;
using System.Collections;
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
    public static class Integration118
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string s) { Console.WriteLine("     " + s); if (report != null) report.WriteLine("     " + s); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Cmd("test-mute " + i); Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Cmd("test-mute " + i); return i; }
        static void Nav(int tab, string url) { Cmd("test-activate " + tab); Cmd("test-nav " + tab + " " + url); Wait(Tabs, t => tab < t.Count && (bool)t[tab]["ready"] && (t[tab]["url"] ?? "").ToString().StartsWith(url.Split('#')[0]), 15000); Cmd("test-mute " + tab); }
        static Dictionary<string, object> J(string reply) { try { return js.Deserialize<Dictionary<string, object>>(Val(reply) ?? "{}") ?? new Dictionary<string, object>(); } catch { return new Dictionary<string, object>(); } }
        static Dictionary<string, object> St() { return J(Cmd("test-tb-state")); }
        static List<string> L(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is ArrayList ? ((ArrayList)o).Cast<object>().Select(x => x == null ? null : x.ToString()).ToList() : new List<string>(); }
        static List<string> LJ(string reply) { try { var a = js.Deserialize<ArrayList>(Val(reply) ?? "[]"); return a.Cast<object>().Select(x => x.ToString()).ToList(); } catch { return new List<string>(); } }
        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        static double D(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is bool && (bool)o; }

        // a tiny local server: pages, a manifest, small downloads and one slow one (so the ring can be seen moving)
        class Srv
        {
            public int Port; TcpListener l; volatile bool stop;
            public Func<string, Tuple<string, byte[], string>> Route;   // path -> (content type, body, extra headers)
            public const int SlowSize = 4 * 1024 * 1024;
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            public readonly List<string> Hits = new List<string>();
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var s = c.GetStream())
                    {
                        var head = new List<byte>(); int b; while ((b = s.ReadByte()) >= 0) { head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; }
                        var first = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
                        var path = first.Length > 1 ? first[1] : "/"; lock (Hits) Hits.Add(path);
                        if (path.StartsWith("/slow.bin"))
                        {
                            var hb = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"slow.bin\"\r\nContent-Length: " + SlowSize + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                            s.Write(hb, 0, hb.Length);
                            var chunk = new byte[64 * 1024]; for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)(i * 7);
                            for (int sent = 0; sent < SlowSize && !stop; sent += chunk.Length) { s.Write(chunk, 0, chunk.Length); s.Flush(); Thread.Sleep(110); }
                            return;
                        }
                        var r = Route(path) ?? Tuple.Create("text/plain", Encoding.UTF8.GetBytes("not found"), (string)null);
                        var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + (r.Item1 == "404" ? "404 Not Found" : "200 OK") + "\r\nContent-Type: " + (r.Item1 == "404" ? "text/plain" : r.Item1) + "\r\nContent-Length: " + r.Item2.Length + "\r\n" + (r.Item3 ?? "") + "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
                        s.Write(h, 0, h.Length); s.Write(r.Item2, 0, r.Item2.Length); s.Flush();
                    }
                }
                catch { }
            }
        }
        static Tuple<string, byte[], string> H(string html) { return Tuple.Create("text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), (string)null); }

        // a minimal unpacked extension (Manifest V3, a popup, its own icons) - made here, loaded from the test folder
        static string MakeExtension(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), "{\"manifest_version\":3,\"name\":\"Pin Test\",\"version\":\"1.0\",\"description\":\"A test extension for 1.18's pins\",\"action\":{\"default_popup\":\"popup.html\",\"default_icon\":{\"16\":\"i16.png\",\"32\":\"i32.png\"}},\"icons\":{\"48\":\"i48.png\"}}");
            File.WriteAllText(Path.Combine(dir, "popup.html"), "<!DOCTYPE html><html><body style='width:200px'>Pin Test popup</body></html>");
            foreach (var sz in new[] { 16, 32, 48 })
                using (var bmp = new System.Drawing.Bitmap(sz, sz))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp)) { g.Clear(System.Drawing.Color.FromArgb(255, 34, 197, 94)); g.FillEllipse(System.Drawing.Brushes.White, sz / 4, sz / 4, sz / 2, sz / 2); }
                    bmp.Save(Path.Combine(dir, "i" + sz + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            return dir;
        }

        static Process Start(string exe, string run, string hostRules)
        {
            var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
            psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
            psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
            psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = hostRules;
            psi.EnvironmentVariables.Remove("HALLOW_BROWSER_MUTE");   // test browsers start with --mute-audio
            var p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            return p;
        }
        static void Quit(Process br) { try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(10000)) br.Kill(); } } catch { } }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v118.txt"), false) { AutoFlush = true };
            var srv = new Srv(); string U = null;
            const string Wav = "(function(){var rate=8000,sec=30,n=rate*sec,buf=new ArrayBuffer(44+n),d=new DataView(buf);function s(o,t){for(var i=0;i<t.length;i++)d.setUint8(o+i,t.charCodeAt(i));}" +
                "s(0,'RIFF');d.setUint32(4,36+n,true);s(8,'WAVEfmt ');d.setUint32(16,16,true);d.setUint16(20,1,true);d.setUint16(22,1,true);d.setUint32(24,rate,true);d.setUint32(28,rate,true);d.setUint16(32,1,true);d.setUint16(34,8,true);s(36,'data');d.setUint32(40,n,true);" +
                "var u=new Uint8Array(buf,44);for(var i=0;i<n;i++)u[i]=128+Math.round(20*Math.sin(i/9));var v=document.getElementById('v');v.muted=true;v.src=URL.createObjectURL(new Blob([buf],{type:'audio/wav'}));v.play().catch(function(){});})();";
            srv.Route = path =>
            {
                var pure = path.Split('?')[0];
                if (pure.StartsWith("/page")) return H("<!DOCTYPE html><html><head><title>" + pure.Substring(1) + "</title></head><body><h1 id=top>" + pure + "</h1></body></html>");
                if (pure == "/app.html") return H("<!DOCTYPE html><html><head><title>Test app</title><link rel=\"manifest\" href=\"/manifest.json\"></head><body>An installable page</body></html>");
                if (pure == "/manifest.json") return Tuple.Create("application/manifest+json", Encoding.UTF8.GetBytes("{\"name\":\"Test app\",\"start_url\":\"/app.html\",\"display\":\"standalone\"}"), (string)null);
                if (pure == "/ad.html") return H("<!DOCTYPE html><html><head><title>ad page</title><script src=\"http://pagead2.googlesyndication.com:" + srv.Port + "/pagead/show_ads.js\"></script><script src=\"http://tpc.googlesyndication.com:" + srv.Port + "/pagead/two.js\"></script></head><body>ads</body></html>");
                if (pure.StartsWith("/pagead/")) return Tuple.Create("text/javascript", Encoding.UTF8.GetBytes("window.adran=1"), (string)null);
                if (pure == "/media.html") return H("<!DOCTYPE html><html><head><title>media</title></head><body><video id=v width=320 height=180 loop autoplay muted></video><script>" + Wav + "</script></body></html>");
                if (pure == "/article.html")
                {
                    var p = string.Join("", Enumerable.Range(1, 14).Select(i => "<p>Paragraph " + i + ". The season brings two new maps, a reworked lobby and matchmaking that finds a game in half the time; ranked play starts at level ten and teams of two can queue together in every mode, with the new shader cache cutting loading times on most PCs.</p>"));
                    return H("<!DOCTYPE html><html><head><title>A long article</title></head><body><nav>menu</nav><article><h1>Patch notes in depth</h1>" + p + "</article><footer>footer</footer></body></html>");
                }
                if (pure.StartsWith("/small-")) return Tuple.Create("application/octet-stream", Encoding.UTF8.GetBytes("small file " + pure), "Content-Disposition: attachment; filename=\"" + pure.Substring(1) + ".txt\"\r\n");
                return Tuple.Create("404", Encoding.UTF8.GetBytes("not found"), (string)null);
            };
            srv.Start(); U = "http://127.0.0.1:" + srv.Port;
            var hostRules = "MAP *.googlesyndication.com 127.0.0.1";
            var t0 = DateTime.Now; Process br = null; string extId = null; List<string> layout = null;
            try
            {
                br = Start(exe, run, hostRules);
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\"") && (Program.Version == "1.18" || Program.Version == "1.19"), "browser answers ping as 1.18 (or 1.19, which keeps its bar): " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                Cmd("test-mute 0");
                Cmd("test-settings DownloadScan=false");   // the downloads here are made-up bytes: no Defender scan

                // ---- the default toolbar
                var s0 = St();
                Ok(string.Join(",", L(s0, "toolbar")) == "shield,extensions,downloads,game,settings,profile" && S(s0, "saved") == null, "the default toolbar, close to Brave's: " + string.Join(",", L(s0, "toolbar")) + " (then the menu; nothing saved)");
                Ok(B(s0, "tabSearchButton") && S(s0, "profile") != null, "the tab-search button is on the strip and the profile button shows (" + S(s0, "profile") + ")");

                // ---- the address bar: clean when not focused, full when focused; "Not secure" on http
                var full = U + "/page-one?x=1#top";
                int pt = NewTab(full);
                var a0 = Wait(St, d => (S(d, "url") ?? "").EndsWith("#top"), 8000);
                var clean = U.Substring(7) + "/page-one?x=1#top";
                Ok(S(a0, "address") == clean, "not focused: the clean address (no http://): " + S(a0, "address"));
                Ok(B(a0, "notSecure"), "an http page says \"Not secure\" beside the padlock");
                var af = J(Cmd("test-tb-addr focus"));
                Ok(S(af, "text") == full, "focused: the full address to edit: " + S(af, "text") + " (keyboard focus " + S(af, "focused") + ")");
                var ab = J(Cmd("test-tb-addr blur"));
                Ok(S(ab, "text") == clean, "left again: clean once more: " + S(ab, "text"));
                var si = Val(Cmd("test-tb-siteinfo")) ?? "";
                Ok(si.Contains("Not secure") && si.ToUpperInvariant().Contains("PERMISSIONS FOR THIS SITE") && si.Contains("Site settings") && si.Contains("Cookies and site data") && si.Contains("Hallow Shield"), "the padlock's panel: connection, Shield, permissions, cookies and site data, Site settings (" + (si.Length > 160 ? si.Substring(0, 160) + "..." : si) + ")");
                Cmd("test-tb-close");

                // ---- page actions: zoom, share
                var p0 = St();
                Ok(!L(p0, "pageActions").Contains("zoom") && L(p0, "pageActions").Contains("share") && !L(p0, "pageActions").Contains("install"), "page actions at 100%: share yes, zoom and install no (" + string.Join(",", L(p0, "pageActions")) + ")");
                Cmd("test-zoom in");
                var p1 = Wait(St, d => L(d, "pageActions").Contains("zoom"), 4000);
                Ok(L(p1, "pageActions").Contains("zoom") && Math.Abs(D(p1, "zoom") - 1.1) < 0.01, "zoomed to 110%: the zoom action shows");
                var zc = Val(Cmd("test-tb-zoomclick"));
                var p2 = Wait(St, d => !L(d, "pageActions").Contains("zoom"), 4000);
                Ok(zc != null && Math.Abs(double.Parse(zc, System.Globalization.CultureInfo.InvariantCulture) - 1.0) < 0.01 && !L(p2, "pageActions").Contains("zoom"), "clicking it goes back to 100% and it goes away");
                var sh = J(Cmd("test-tb-share"));
                Ok(B(sh, "qr") && S(sh, "readBack") == full && Convert.ToInt32(sh["version"]) >= 1, "share: a QR code drawn here (version " + S(sh, "version") + ", read back to the same address)");
                Ok(S(sh, "copied") == full && (S(sh, "toast") ?? "").Contains("copied"), "share: Copy link copies the address (test runs record it instead of touching the clipboard)");
                Cmd("test-tb-close");

                // ---- install as app (a page with a manifest), reader, pop out a playing video
                int at = NewTab(U + "/app.html");
                var pi = Wait(St, d => L(d, "pageActions").Contains("install"), 8000);
                Ok(L(pi, "pageActions").Contains("install"), "a page with a web app manifest: the install action shows (" + string.Join(",", L(pi, "pageActions")) + ")");
                Nav(at, U + "/page-noapp");
                var pn = Wait(St, d => !L(d, "pageActions").Contains("install"), 6000);
                Ok(!L(pn, "pageActions").Contains("install"), "... and goes when the page has none");
                Nav(at, U + "/article.html");
                var pr = Wait(St, d => L(d, "pageActions").Contains("reader"), 10000);
                Ok(L(pr, "pageActions").Contains("reader"), "an article: the reader action shows (" + string.Join(",", L(pr, "pageActions")) + ")");
                Nav(at, U + "/media.html");
                var pp = Wait(St, d => L(d, "pageActions").Contains("pip"), 12000);
                Ok(L(pp, "pageActions").Contains("pip"), "a playing (muted) video: the pop-out action shows (" + string.Join(",", L(pp, "pageActions")) + ")");
                Nav(at, U + "/page-quiet");
                var pq = Wait(St, d => !L(d, "pageActions").Contains("pip"), 6000);
                Ok(!L(pq, "pageActions").Contains("pip") && !L(pq, "pageActions").Contains("reader"), "... and both go on a plain page");

                // ---- Shield's badge
                Nav(at, U + "/ad.html");
                var c1 = Wait(() => J(Cmd("test-tb-shieldcount " + at)), d => D(d, "n") >= 1, 20000);
                Note("Shield count: " + js.Serialize(c1));
                lock (srv.Hits) Ok(!srv.Hits.Any(h => h.StartsWith("/pagead/")), "Shield blocked the page's ad requests (none reached the server)");
                var b1 = Wait(St, d => D(d, "shieldBadge") >= 1, 6000);
                Ok(D(c1, "n") >= 1 && D(b1, "shieldBadge") >= 1, "Shield's button shows the number blocked on this page: " + D(b1, "shieldBadge") + " (" + S(c1, "state") + ")");
                Nav(at, U + "/page-clean");
                var b2 = Wait(St, d => D(d, "shieldBadge") == 0, 8000);
                Ok(D(b2, "shieldBadge") == 0, "a page with nothing blocked: no badge");

                // ---- Customize toolbar: hide (right-click menu), show, move, the panel's switch
                var rc = Val(Cmd("test-tb-rclick downloads")) ?? "";
                Ok(rc.Contains("Hide Downloads") && rc.Contains("Customize toolbar"), "right-click on a toolbar button: \"Hide Downloads\" and \"Customize toolbar...\" (" + rc + ")");
                var hid = Val(Cmd("test-tb-menu-run Hide")) ?? "";
                var t1 = St();
                Ok(!L(t1, "toolbar").Contains("downloads") && L(t1, "saved").Count > 0, "Hide: the downloads button goes (" + string.Join(",", L(t1, "toolbar")) + ")");
                Cmd("test-tb-show home 1");
                var mv = LJ(Cmd("test-tb-move game -1"));
                Ok(mv.FirstOrDefault() == "home" && mv.IndexOf("game") >= 0 && mv.IndexOf("game") < mv.IndexOf("extensions"), "Home shows (left of the address bar) and Game mode moved before Extensions: " + string.Join(",", mv));
                var cz = Val(Cmd("test-tb-customize")) ?? "";
                Ok(new[] { "Home", "Bookmarks", "Downloads", "Sidebar", "Extensions", "Hallow Shield", "Game mode", "Screenshot", "Split view", "Reader mode", "Profile", "Settings", "Reset to default" }.All(x => cz.Contains(x)), "Customize toolbar lists every button and Reset to default");
                var tg = LJ(Cmd("test-tb-customize-toggle bookmarks"));
                Ok(tg.Contains("bookmarks"), "the panel's switch shows Bookmarks: " + string.Join(",", tg));
                Cmd("test-tb-close");
                layout = L(St(), "toolbar");

                // ---- extensions: the list, a pin, the pinned button opens the popup
                var ext = MakeExtension(Path.Combine(dir, "ext118"));
                var xl = Cmd("test-v-xload " + ext, 60000);
                extId = LJ(xl).FirstOrDefault();
                Ok(extId != null, "a small unpacked extension loads (" + xl + ")");
                if (extId != null)
                {
                    var ef = Val(Cmd("test-tb-click extensions")) ?? "";
                    Ok(ef.Contains("Pin Test") && ef.Contains("Manage extensions"), "the puzzle piece lists it with Manage extensions (" + ef + ")");
                    var pins = LJ(Cmd("test-tb-pinflyout " + extId));
                    Ok(pins.Count == 1 && pins[0] == extId, "its pin puts it on the toolbar: " + string.Join(",", pins));
                    Cmd("test-tb-close");
                    var pc = Cmd("test-tb-pinclick 0", 20000);
                    Ok(pc != null && pc.StartsWith("ok chrome-extension://" + extId + "/popup.html"), "the pinned button opens its popup (" + pc + ")");
                }

                // ---- downloads: the ring on a slow download, then the pop-up's latest five
                for (int i = 1; i <= 6; i++) { Cmd("test-nav " + at + " " + U + "/small-" + i); Thread.Sleep(500); }
                Wait(St, d => L(d, "downloads").Count(x => x.StartsWith("done")) >= 6, 15000);
                Cmd("test-nav " + at + " " + U + "/slow.bin");
                var seen = new List<double>(); var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 30000) { var d = St(); var r = D(d, "ring"); if (r >= 0) seen.Add(Math.Round(r, 3)); if (seen.Count > 0 && r < 0) break; Thread.Sleep(250); }
                Note("ring: " + string.Join(" ", seen.Take(40)));
                Ok(seen.Any(x => x > 0 && x < 1) && seen.Distinct().Count() >= 3, "while the slow download runs the ring shows its progress (" + seen.Count + " looks, " + seen.Distinct().Count() + " values)");
                var orderOk = true; for (int i = 1; i < seen.Count; i++) if (seen[i] + 0.001 < seen[i - 1]) orderOk = false;
                Ok(orderOk && seen.Count > 0 && seen.Last() > 0.5, "... and only goes up (last seen " + (seen.Count > 0 ? seen.Last() : -1) + ")");
                var dd = Wait(St, d => D(d, "ring") < 0 && L(d, "downloads").Count(x => x.StartsWith("done")) >= 7, 30000);
                Ok(D(dd, "ring") < 0, "done: the ring goes (" + string.Join(" | ", L(dd, "downloads").Take(3)) + ")");
                var df = J(Cmd("test-tb-dlflyout"));
                Ok(D(df, "rows") == 5 && (S(df, "text") ?? "").Contains("All downloads") && (S(df, "text") ?? "").Contains("slow.bin"), "the pop-up lists the latest five (newest first) and All downloads (" + D(df, "rows") + " rows)");
                Cmd("test-tb-close");

                // ---- the tab strip
                while (Tabs().Count < 14) { Cmd("test-new " + U + "/page-strip" + Tabs().Count); }
                Thread.Sleep(1500); for (int i = 0; i < Tabs().Count; i++) Cmd("test-mute " + i);
                Cmd("test-activate 7");
                Cmd("test-tb-width 760"); Thread.Sleep(400);
                var n1 = Wait(() => J(Cmd("test-tb-strip")), d => B(d, "overflow"), 4000);
                var w1 = L(n1, "widths").Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                Ok(B(n1, "overflow") && B(n1, "arrows") && w1.Count == Tabs().Count && w1.All(x => Math.Abs(x - w1[0]) < 0.6 && x >= 63.5), "a narrow window with " + w1.Count + " tabs: the strip overflows, arrows show, every tab at the minimum (" + (w1.Count > 0 ? w1[0] : 0) + " px)");
                Ok(B(n1, "activeInView"), "the active tab is scrolled into view (offset " + D(n1, "offset") + " of " + D(n1, "extent") + ")");
                var cs = L(n1, "closeShown");
                Ok(cs.Count(x => x == "True") == 1 && cs[7] == "True", "narrow tabs: only the active tab shows its close button");
                var hv = J(Cmd("test-tb-hover 3 1"));
                Ok(L(hv, "closeShown")[3] == "True", "... and a tab you hover");
                Cmd("test-tb-hover 3 0");
                var off0 = D(n1, "offset"); var sc = J(Cmd("test-tb-scroll -1"));
                Ok(D(sc, "offset") < off0 || off0 == 0, "the left arrow scrolls the strip (" + off0 + " -> " + D(sc, "offset") + ")");
                Cmd("test-tb-width 1700"); Thread.Sleep(400);
                var n2 = Wait(() => J(Cmd("test-tb-strip")), d => !B(d, "overflow"), 4000);
                var w2 = L(n2, "widths").Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                Ok(!B(n2, "overflow") && !B(n2, "arrows") && w2.All(x => Math.Abs(x - w2[0]) < 0.6) && w2[0] > 64 && w2[0] <= 230, "a wide window: no arrows, the tabs share the room evenly (" + (w2.Count > 0 ? w2[0] : 0) + " px each)");
                Cmd("test-tb-width 1280");
                var ts = J(Cmd("test-tb-tabsearch"));
                var tsr = L(ts, "rows");
                Ok(S(ts, "text") == "% " && tsr.Count == Math.Min(8, Tabs().Count) && tsr.All(x => x.StartsWith("tab:")), "the tab-search button opens the address bar's tab search (\"% \", " + tsr.Count + " tabs listed)");
                Cmd("test-tb-addr blur");

                // ---- kept across a restart
                Quit(br); br = null; Thread.Sleep(1500);
                br = Start(exe, run, hostRules);
                Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Wait(Tabs, t => t.Count >= 1, 30000); Thread.Sleep(1500);
                for (int i = 0; i < Tabs().Count; i++) Cmd("test-mute " + i);
                var r1 = Wait(St, d => L(d, "pinButtons").Count > 0 || extId == null, 20000);
                Ok(string.Join(",", L(r1, "toolbar")) == string.Join(",", layout), "after a restart the toolbar is as you left it: " + string.Join(",", L(r1, "toolbar")));
                Ok(extId == null || (L(r1, "pinned").Contains(extId) && L(r1, "pinButtons").Contains(extId)), "... and the pinned extension is still on it");
                var rs = LJ(Cmd("test-tb-reset"));
                Ok(string.Join(",", rs) == "shield,extensions,downloads,game,settings,profile" && S(St(), "saved") == null, "Reset to default: " + string.Join(",", rs));
            }
            catch (Exception e) { Ok(false, "crashed: " + e); }
            finally
            {
                Quit(br);
                try { srv.Stop(); } catch { }
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
            }
            Console.WriteLine(pass + " passed, " + fail + " failed"); if (report != null) { report.WriteLine(pass + " passed, " + fail + " failed"); report.Dispose(); }
            return fail == 0 ? 0 : 1;
        }
    }
}
