// Hallow Browser 1.19 - integration test "v119", off-screen like the others (muted: test browsers start with --mute-audio, and each
// tab is muted too), its own test folder, LOCAL pages only (a 127.0.0.1 server of its own; accounts.google.com is mapped to it so a
// sign-in host can be checked without going anywhere). Ask Claude talks to a local stand-in of the Messages API
// (HALLOW_BROWSER_ANTHROPIC_URL, honoured only in test runs) with a made-up key - never the real API, never a real key.
//  - pinned tabs: pin / unpin, compact width, no close button, the strip's other tabs, the vertical column, kept across a restart;
//  - cookie banners: a OneTrust-like banner and a generic one get "Reject all" clicked (Accept never), a sign-in host and a site
//    turned off are left alone, the padlock's panel says what happened;
//  - Copy clean link: the share pop-up, the tab's menu, "Clean links when I copy the address";
//  - auto picture-in-picture: a playing (muted) video pops out when you switch away and comes back, a muted autoplay doesn't, nor
//    anything with the setting off;
//  - group tabs by site / ungroup all (pinned tabs stay out);
//  - Ask Claude: the key stored encrypted (nowhere in plain text), a streamed answer, the page's text only after "Use this page",
//    the model picker, an API error said plainly.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v119
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
    public static class Integration119
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string s) { Console.WriteLine("     " + s); if (report != null) report.WriteLine("     " + s); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-nn-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static List<Dictionary<string, object>> RawTabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static int NewTab(string url) { Cmd("test-activate " + (RawTabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Cmd("test-mute " + i); Wait(RawTabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Cmd("test-mute " + i); return i; }
        static int IndexOf(string urlPart) { var t = RawTabs(); for (int i = 0; i < t.Count; i++) if ((t[i]["url"] ?? "").ToString().Contains(urlPart)) return i; return -1; }
        static void Nav(int tab, string url) { Cmd("test-activate " + tab); Cmd("test-nav " + tab + " " + url); Wait(RawTabs, t => tab < t.Count && (bool)t[tab]["ready"] && (t[tab]["url"] ?? "").ToString().StartsWith(url.Split('#')[0]), 15000); Cmd("test-mute " + tab); }
        static Dictionary<string, object> J(string reply) { try { return js.Deserialize<Dictionary<string, object>>(Val(reply) ?? "{}") ?? new Dictionary<string, object>(); } catch { return new Dictionary<string, object>(); } }
        static List<string> L(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is ArrayList ? ((ArrayList)o).Cast<object>().Select(x => x == null ? null : x.ToString()).ToList() : new List<string>(); }
        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        static double D(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is bool && (bool)o; }

        // ---- the local server: pages, the "clicks" the banners report, and a stand-in for the Messages API
        class Req { public string Method, Path; public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); public string Body; }
        class Srv
        {
            public int Port; TcpListener l; volatile bool stop;
            public Func<string, string, Tuple<string, byte[]>> Route;   // (host, path) -> (content type, body)
            public readonly List<string> Hits = new List<string>();
            public readonly List<Req> Api = new List<Req>();
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var s = c.GetStream())
                    {
                        var head = new List<byte>(); int b; while ((b = s.ReadByte()) >= 0) { head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; }
                        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                        var first = lines[0].Split(' '); var rq = new Req { Method = first[0], Path = first.Length > 1 ? first[1] : "/" };
                        foreach (var hl in lines.Skip(1)) { var i = hl.IndexOf(':'); if (i > 0) rq.Headers[hl.Substring(0, i).Trim()] = hl.Substring(i + 1).Trim(); }
                        string cl; int len = rq.Headers.TryGetValue("Content-Length", out cl) ? int.Parse(cl) : 0;
                        var body = new byte[len]; int got = 0; while (got < len) { var k = s.Read(body, got, len - got); if (k <= 0) break; got += k; }
                        rq.Body = Encoding.UTF8.GetString(body, 0, got);
                        lock (Hits) Hits.Add(rq.Path);
                        if (rq.Method == "POST" && rq.Path.StartsWith("/v1/messages")) { lock (Api) Api.Add(rq); Messages(s, rq); return; }
                        string host; rq.Headers.TryGetValue("Host", out host);
                        var r = Route(host ?? "", rq.Path) ?? Tuple.Create("404", Encoding.UTF8.GetBytes("not found"));
                        var h = Encoding.ASCII.GetBytes("HTTP/1.1 " + (r.Item1 == "404" ? "404 Not Found" : "200 OK") + "\r\nContent-Type: " + (r.Item1 == "404" ? "text/plain" : r.Item1) + "\r\nContent-Length: " + r.Item2.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        s.Write(h, 0, h.Length); s.Write(r.Item2, 0, r.Item2.Length); s.Flush();
                    }
                }
                catch { }
            }
            // the Messages API stand-in: checks the key and the version header, answers with a streamed message (a thinking block
            // with a signature, then the text in four pieces) - or an error when the question asks for one
            void Messages(NetworkStream s, Req rq)
            {
                string key, ver; rq.Headers.TryGetValue("x-api-key", out key); rq.Headers.TryGetValue("anthropic-version", out ver);
                Action<int, string, string> err = (code, type, msg) => { var e = Encoding.UTF8.GetBytes("{\"type\":\"error\",\"error\":{\"type\":\"" + type + "\",\"message\":\"" + msg + "\"}}"); var hh = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + " X\r\nContent-Type: application/json\r\nContent-Length: " + e.Length + "\r\nConnection: close\r\n\r\n"); s.Write(hh, 0, hh.Length); s.Write(e, 0, e.Length); s.Flush(); };
                if (key != FakeKey) { err(401, "authentication_error", "invalid x-api-key"); return; }
                if (ver != "2026-10-21") { err(400, "invalid_request_error", "anthropic-version"); return; }
                if (rq.Body.Contains("please-fail")) { err(529, "overloaded_error", "Overloaded"); return; }
                var h = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
                s.Write(h, 0, h.Length); s.Flush();
                Action<string, string> ev = (name, data) => { var bb = Encoding.UTF8.GetBytes("event: " + name + "\ndata: " + data + "\n\n"); s.Write(bb, 0, bb.Length); s.Flush(); Thread.Sleep(120); };
                ev("message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_standin\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[],\"model\":\"stand-in\",\"stop_reason\":null,\"usage\":{\"input_tokens\":10,\"output_tokens\":1}}}");
                ev("content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"\"}}");
                ev("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"signature_delta\",\"signature\":\"stand-in-signature-1\"}}");
                ev("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}");
                ev("content_block_start", "{\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}");
                ev("ping", "{\"type\":\"ping\"}");
                foreach (var piece in new[] { "Hello", " from", " the", " stand-in." }) ev("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"" + piece + "\"}}");
                ev("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":1}");
                ev("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":12}}");
                ev("message_stop", "{\"type\":\"message_stop\"}");
            }
        }
        const string FakeKey = "sk-ant-test-0000000000-made-up-not-a-real-key";
        static Tuple<string, byte[]> H(string html) { return Tuple.Create("text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)); }
        const string Wav = "(function(){var rate=8000,sec=30,n=rate*sec,buf=new ArrayBuffer(44+n),d=new DataView(buf);function s(o,t){for(var i=0;i<t.length;i++)d.setUint8(o+i,t.charCodeAt(i));}" +
            "s(0,'RIFF');d.setUint32(4,36+n,true);s(8,'WAVEfmt ');d.setUint32(16,16,true);d.setUint16(20,1,true);d.setUint16(22,1,true);d.setUint32(24,rate,true);d.setUint32(28,rate,true);d.setUint16(32,1,true);d.setUint16(34,8,true);s(36,'data');d.setUint32(40,n,true);" +
            "var u=new Uint8Array(buf,44);for(var i=0;i<n;i++)u[i]=128+Math.round(20*Math.sin(i/9));var v=document.getElementById('v');v.muted=true;v.src=URL.createObjectURL(new Blob([buf],{type:'audio/wav'}));AUTO})();";
        // a video element playing a generated sound: as a canvas stream it shows a picture, so it counts as a video of its size
        const string Canvas = "(function(){var c=document.createElement('canvas');c.width=320;c.height=180;var g=c.getContext('2d');var k=0;setInterval(function(){g.fillStyle='hsl('+(k++%360)+',70%,40%)';g.fillRect(0,0,320,180);},100);" +
            "var v=document.getElementById('v');v.muted=true;v.loop=true;v.srcObject=c.captureStream(10);AUTO})();";

        static Process Start(string exe, string run, string hostRules, string api)
        {
            var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
            psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
            psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
            psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = hostRules;
            psi.EnvironmentVariables["HALLOW_BROWSER_ANTHROPIC_URL"] = api;
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
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v119.txt"), false) { AutoFlush = true };
            var srv = new Srv(); string U = null, LH = null;
            srv.Route = (host, path) =>
            {
                var pure = path.Split('?')[0];
                if (pure.StartsWith("/page")) return H("<!DOCTYPE html><html><head><title>" + pure.Substring(1) + "</title></head><body><h1>" + pure + "</h1></body></html>");
                if (pure.StartsWith("/hit-")) return Tuple.Create("text/plain", Encoding.UTF8.GetBytes("ok"));
                if (pure == "/onetrust.html")   // the markup of OneTrust's banner (ids as OneTrust's), shown a moment after load
                    return H("<!DOCTYPE html><html><head><title>OneTrust-like</title></head><body><h1>News</h1><script>setTimeout(function(){var d=document.createElement('div');d.id='onetrust-banner-sdk';d.innerHTML='<p>We use cookies to improve your experience.</p>" +
                        "<button id=\"onetrust-accept-btn-handler\" onclick=\"fetch(\\'/hit-accept-ot\\')\">Accept All Cookies</button><button id=\"onetrust-pc-btn-handler\">Cookie Settings</button><button id=\"onetrust-reject-all-handler\" onclick=\"document.cookie=\\'OptanonAlertBoxClosed=1\\';fetch(\\'/hit-reject-ot\\');this.parentNode.remove()\">Reject All</button>';document.body.appendChild(d);},400);</script></body></html>");
                if (pure == "/generic.html" || pure == "/signin.html")   // a plain cookie dialog (no consent manager), late (1.5 s)
                    return H("<!DOCTYPE html><html><head><title>" + (pure == "/signin.html" ? "Sign in" : "Generic banner") + "</title></head><body><h1>Shop</h1><script>setTimeout(function(){var d=document.createElement('div');d.setAttribute('role','dialog');d.className='consent-popup';d.innerHTML='<h2>Your privacy</h2><p>This site uses cookies for analytics and ads.</p>" +
                        "<button onclick=\"fetch(\\'/hit-accept-gen\\')\">Accept all</button> <a href=\"#\" onclick=\"fetch(\\'/hit-reject-gen\\');return false\">Reject all</a> <button>More options</button>';document.body.appendChild(d);},1500);</script></body></html>");
                if (pure == "/track") return H("<!DOCTYPE html><html><head><title>Tracked link</title></head><body>tracked</body></html>");
                if (pure == "/article.html") return H("<!DOCTYPE html><html><head><title>Patch notes</title></head><body><article><h1>Patch notes</h1><p>MARKER-PAGE-TEXT-7781: ranked play starts at level ten.</p></article></body></html>");
                if (pure == "/media.html") return H("<!DOCTYPE html><html><head><title>media</title></head><body><video id=v width=480 height=270></video><script>" + Canvas.Replace("AUTO", "") + "</script></body></html>");
                if (pure == "/autoplay.html") return H("<!DOCTYPE html><html><head><title>autoplay ad</title></head><body><video id=v width=480 height=270 autoplay muted></video><script>" + Canvas.Replace("AUTO", "v.play().catch(function(){});") + "</script></body></html>");
                return Tuple.Create("404", Encoding.UTF8.GetBytes("not found"));
            };
            srv.Start(); U = "http://127.0.0.1:" + srv.Port; LH = "http://localhost:" + srv.Port;
            var hostRules = "MAP accounts.google.com 127.0.0.1";
            var api = U + "/v1/messages";
            var t0 = DateTime.Now; Process br = null; var pinnedUrls = new List<string>();
            Func<string, int> hits = p => { lock (srv.Hits) return srv.Hits.Count(h => h.StartsWith(p)); };
            try
            {
                br = Start(exe, run, hostRules, api);
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\"") && (Program.Version == "1.19" || Program.Version == "1.20" || Program.Version == "1.21"), "browser answers ping as 1.19 (or 1.20, which keeps its extras): " + ping);
                Wait(RawTabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                Cmd("test-mute 0");
                Cmd("test-settings HttpsOnly=false");   // the mapped sign-in host is plain http here

                // ---- pinned tabs
                int ta = NewTab(U + "/page-a"), tb = NewTab(U + "/page-b"), tc = NewTab(U + "/page-c");
                Val(Cmd("test-nn-pin " + IndexOf("/page-b") + " 1"));
                var p1 = Tabs();
                Ok(p1.Count >= 4 && B(p1[0], "pinned") && (S(p1[0], "url") ?? "").EndsWith("/page-b"), "Pin tab: it moves to the left (" + string.Join(", ", p1.Select(x => S(x, "url").Split('/').Last() + (B(x, "pinned") ? "*" : ""))) + ")");
                Val(Cmd("test-nn-pin " + IndexOf("/page-c") + " 1"));
                var p2 = Wait(Tabs, d => d.Count > 1 && B(d[1], "pinned") && D(d[0], "width") > 0, 4000);
                Ok(B(p2[0], "pinned") && B(p2[1], "pinned") && (S(p2[1], "url") ?? "").EndsWith("/page-c"), "a second pinned tab goes after the first");
                Ok(p2.Where(x => B(x, "pinned")).All(x => Math.Abs(D(x, "width") - 42) < 0.6 && !B(x, "closeShown") && !B(x, "titleShown")), "pinned tabs are compact: 42 px, icon only, no close button (" + string.Join(", ", p2.Where(x => B(x, "pinned")).Select(x => D(x, "width"))) + ")");
                var unp = p2.Where(x => !B(x, "pinned")).Select(x => D(x, "width")).ToList();
                Ok(unp.Count >= 2 && unp.All(x => Math.Abs(x - unp[0]) < 0.6 && x > 64), "the other tabs still share the strip evenly (" + string.Join(", ", unp) + ")");
                Cmd("test-tb-width 700"); Thread.Sleep(500);
                while (RawTabs().Count < 14) Cmd("test-new " + U + "/page-fill" + RawTabs().Count);
                Thread.Sleep(1200); for (int i = 0; i < RawTabs().Count; i++) Cmd("test-mute " + i);
                var st = J(Cmd("test-tb-strip"));
                var po = Tabs();
                Ok(B(st, "overflow") && B(st, "arrows") && po.Take(2).All(x => Math.Abs(D(x, "width") - 42) < 0.6) && po.Skip(2).All(x => Math.Abs(D(x, "width") - 64) < 0.6), "1.18's overflow still works with pins: arrows, the others at the minimum, pinned ones stay 42 px");
                Cmd("test-tb-width 1280"); Thread.Sleep(400);
                for (int i = RawTabs().Count - 1; i >= 0; i--) { var u = S(RawTabs()[i], "url") ?? ""; if (u.Contains("/page-fill")) Cmd("test-close " + i); }
                Cmd("test-p-vertical on"); Thread.Sleep(600);
                var pv = Tabs();
                Ok(pv.Take(2).All(x => B(x, "pinned") && S(x, "headerParent") == "WrapPanel" && Math.Abs(D(x, "width") - 42) < 0.6) && pv.Skip(2).All(x => S(x, "headerParent") == "StackPanel"), "vertical tabs: pinned ones are icon tiles in a row at the top (" + string.Join(", ", pv.Select(x => S(x, "headerParent"))) + ")");
                Cmd("test-p-vertical off"); Thread.Sleep(600);
                var ph = Tabs();
                Ok(ph.All(x => S(x, "headerParent") == "StackPanel") && B(ph[0], "pinned") && Math.Abs(D(ph[0], "width") - 42) < 0.6, "back in the title bar, the pinned ones too");
                var mn = J(Cmd("test-nn-tabmenu-run 0 Unpin tab"));
                Ok((S(mn, "menu") ?? "").Contains("Unpin tab") && (S(mn, "menu") ?? "").Contains("Copy clean link"), "the tab's menu: Unpin tab and Copy clean link");
                var pu = Tabs();
                Ok(pu.Count(x => B(x, "pinned")) == 1 && (S(pu[0], "url") ?? "").EndsWith("/page-c") && !B(pu[1], "pinned"), "Unpin: it leaves the pinned area (" + string.Join(", ", pu.Select(x => S(x, "url").Split('/').Last() + (B(x, "pinned") ? "*" : ""))) + ")");
                Cmd("test-nn-pin " + IndexOf("/page-b") + " 1");
                pinnedUrls = Tabs().Where(x => B(x, "pinned")).Select(x => S(x, "url")).ToList();
                Ok(pinnedUrls.Count == 2, "pinned again: " + string.Join(", ", pinnedUrls));
                Cmd("test-close 0"); var ac = Tabs();
                Ok(ac.Count(x => B(x, "pinned")) == 1, "Ctrl+W (close) closes a pinned tab, like in Chrome");
                Cmd("test-reopen"); Thread.Sleep(1500); var ro = Tabs(); var reopened = ro.FindIndex(x => S(x, "url") == pinnedUrls[0]);
                if (reopened >= 0) Cmd("test-nn-pin " + reopened + " 1");
                pinnedUrls = Tabs().Where(x => B(x, "pinned")).Select(x => S(x, "url")).ToList();

                // ---- group tabs by site (pinned tabs stay out), ungroup all
                NewTab(LH + "/page-lh1"); NewTab(LH + "/page-lh2"); NewTab(U + "/page-x");
                var sm = Val(Cmd("test-nn-stripmenu")) ?? "";
                Ok(sm.Contains("Group tabs by site"), "the strip's menu offers Group tabs by site (" + sm + ")");
                var gr = J(Cmd("test-nn-group"));
                var gt = Tabs();
                Ok(D(gr, "made") == 2 && gt.Where(x => B(x, "pinned")).All(x => S(x, "group") == null) && gt.Count(x => S(x, "group") == "Localhost") == 2, "Group tabs by site: two groups (" + string.Join(", ", L(gr, "groups")) + "), pinned tabs left out");
                var pal = (Val(Cmd("test-p-palette group tabs by site")) ?? "") + (Val(Cmd("test-p-palette ungroup all")) ?? "");
                Ok(pal.Contains("Group tabs by site") && pal.Contains("Ungroup all tabs"), "Ctrl+K: Group tabs by site, Ungroup all tabs");
                var ug = J(Cmd("test-nn-ungroup"));
                Ok(D(ug, "undone") == 2 && D(ug, "groups") == 0 && Tabs().All(x => S(x, "group") == null), "Ungroup all: no groups left");

                // ---- cookie banners
                int ck = NewTab(U + "/page-blank");
                Nav(ck, U + "/onetrust.html");
                Wait(() => hits("/hit-reject-ot"), n => n > 0, 8000); Thread.Sleep(800);
                Ok(hits("/hit-reject-ot") == 1 && hits("/hit-accept-ot") == 0, "a OneTrust-like banner: Reject All clicked, Accept never (reject " + hits("/hit-reject-ot") + ", accept " + hits("/hit-accept-ot") + ")");
                var cs = J(Cmd("test-nn-consent")); var ctab = ((ArrayList)cs["tabs"])[ck] as Dictionary<string, object>;
                Ok(ctab != null && S(ctab, "here") == "OneTrust" && D(ctab, "declined") == 1, "the tab knows: OneTrust declined (" + js.Serialize(ctab) + ")");
                var si = Val(Cmd("test-tb-siteinfo")) ?? ""; Cmd("test-tb-close");
                Ok(si.Contains("Declined OneTrust") && si.Contains("Decline cookie banners on this site"), "the padlock's panel says so, with this site's switch (" + (si.Length > 200 ? si.Substring(0, 200) + "..." : si) + ")");
                Nav(ck, U + "/generic.html");
                Wait(() => hits("/hit-reject-gen"), n => n > 0, 9000); Thread.Sleep(800);
                Ok(hits("/hit-reject-gen") == 1 && hits("/hit-accept-gen") == 0, "a plain cookie dialog shown late: \"Reject all\" clicked, \"Accept all\" never");
                var cs2 = J(Cmd("test-nn-consent")); var ctab2 = ((ArrayList)cs2["tabs"])[ck] as Dictionary<string, object>;
                Ok(ctab2 != null && D(ctab2, "declined") == 2, "two declined in this tab (" + js.Serialize(ctab2) + ")");
                var signin = "http://accounts.google.com:" + srv.Port + "/signin.html";
                Nav(ck, signin);
                Thread.Sleep(9500);
                Ok(hits("/hit-reject-gen") == 1 && hits("/hit-accept-gen") == 0, "a sign-in host (accounts.google.com, mapped to the local server): the same dialog is left alone");
                var si2 = Val(Cmd("test-tb-siteinfo")) ?? ""; Cmd("test-tb-close");
                Ok(si2.Contains("left alone on sign-in pages"), "... and the padlock says why");
                Cmd("test-nn-consent-site 127.0.0.1 0");
                Nav(ck, U + "/generic.html?again=1");
                Thread.Sleep(9500);
                Ok(hits("/hit-reject-gen") == 1, "a site you turned it off for (the padlock's switch): left alone");
                Cmd("test-nn-consent-site 127.0.0.1 1");
                Nav(ck, U + "/generic.html?third=1");
                Wait(() => hits("/hit-reject-gen"), n => n > 1, 9000);
                Ok(hits("/hit-reject-gen") == 2 && hits("/hit-accept-gen") == 0, "turned back on: declined again");
                Cmd("test-settings CookieDecline=false"); Cmd("test-tick");

                // ---- Copy clean link
                var tr = U + "/track?id=7&utm_source=news&utm_medium=email&fbclid=IwAR0abc&ref=home#top";
                var want = U + "/track?id=7&ref=home#top";
                Ok(Val(Cmd("test-nn-clean " + tr)) == want, "the cleaner: utm_*, fbclid out; id, ref and the fragment stay");
                int tt = NewTab(tr);
                var sc = J(Cmd("test-nn-share-clean"));
                Ok(S(sc, "copied") == want && (S(sc, "text") ?? "").Contains("Copy clean link") && (S(sc, "text") ?? "").Contains("tracking parameter"), "the share pop-up: Copy clean link copies " + S(sc, "copied") + " (" + S(sc, "toast") + ")");
                Cmd("test-tb-close");
                var tm = J(Cmd("test-nn-tabmenu-run " + tt + " Copy clean link"));
                Ok(S(tm, "copied") == want, "the tab's menu: Copy clean link");
                Ok(Val(Cmd("test-nn-addrcopy " + S(RawTabs()[tt], "url"))) == S(RawTabs()[tt], "url"), "copying the address: as it is while \"Clean links when I copy the address\" is off (the default)");
                Cmd("test-settings CleanCopy=true");
                var trNow = S(RawTabs()[tt], "url") ?? tr; Note("the tab's address (Shield may strip some parameters itself): " + trNow);
                Ok(Val(Cmd("test-nn-addrcopy " + trNow)) == want && Val(Cmd("test-nn-addrcopy id=7")) == "id=7", "... and clean with it on (part of the address is copied as it is)");
                Cmd("test-settings CleanCopy=false");

                // ---- auto picture-in-picture
                Cmd("test-settings AutoPip=true");
                int mv = NewTab(U + "/media.html");
                Cmd("test-nn-gesture " + mv + " document.getElementById('v').play().then(function(){return 1})");
                var playing = Wait(() => Val(Cmd("test-eval " + mv + " (function(){var v=document.getElementById('v');return !v.paused&&v.readyState>=2})()")), v => v == "true", 10000);
                Note("media tab playing: " + playing);
                var c0 = D(J(Cmd("test-nn-autopip")), "count");
                Cmd("test-activate " + IndexOf("/page-a"));
                var ap = Wait(() => J(Cmd("test-nn-autopip")), d => D(d, "count") > c0 && L(d, "popped")[mv] == "True", 8000);
                Ok(D(ap, "count") == c0 + 1 && L(ap, "popped")[mv] == "True", "switching away from a playing (muted) video pops it out (" + S(ap, "last") + ")");
                Cmd("test-activate " + mv);
                var back = Wait(() => J(Cmd("test-nn-autopip")), d => L(d, "popped")[mv] == "False", 6000);
                Ok(L(back, "popped")[mv] == "False", "coming back to its tab brings it back");
                int av = NewTab(U + "/autoplay.html"); Thread.Sleep(2500);
                var c1 = D(J(Cmd("test-nn-autopip")), "count");
                Cmd("test-activate " + IndexOf("/page-a")); Thread.Sleep(2500);
                var ap2 = J(Cmd("test-nn-autopip"));
                Ok(D(ap2, "count") == c1 && L(ap2, "popped")[av] == "False" && S(ap2, "last") == "muted autoplay", "a muted autoplay (an ad, a background loop) is left alone (" + S(ap2, "last") + ")");
                Cmd("test-settings AutoPip=false");
                Cmd("test-activate " + mv); Thread.Sleep(600);
                Cmd("test-activate " + IndexOf("/page-a")); Thread.Sleep(2500);
                var ap3 = J(Cmd("test-nn-autopip"));
                Ok(D(ap3, "count") == c1 && L(ap3, "popped").All(x => x == "False"), "with the setting off nothing pops out");
                Cmd("test-close " + av); Cmd("test-close " + IndexOf("/media.html"));

                // ---- Ask Claude (the local stand-in, a made-up key)
                Cmd("test-settings AiOn=true");
                var a0 = J(Cmd("test-nn-ai-open"));
                var s0 = J(Cmd("test-nn-ai-state"));
                Ok(!B(s0, "hasKey") && (S(s0, "view") ?? "").Contains("Save key") && srv.Api.Count == 0, "Ask Claude without a key: the key form, nothing sent");
                Ok(Val(Cmd("test-nn-ai-key not-a-key")) == "False", "a key that isn't one is refused");
                Ok(Val(Cmd("test-nn-ai-key " + FakeKey)) == "True", "the (made-up) key is saved");
                var kf = S(J(Cmd("test-nn-ai-state")), "keyFile") ?? "";
                var kb = File.Exists(kf) ? File.ReadAllBytes(kf) : new byte[0];
                Ok(kf.StartsWith(run, StringComparison.OrdinalIgnoreCase) && kb.Length > 0 && Encoding.ASCII.GetString(kb).IndexOf("sk-ant", StringComparison.Ordinal) < 0 && Encoding.Unicode.GetString(kb).IndexOf("sk-ant", StringComparison.Ordinal) < 0, "the key file is in the test profile folder and encrypted (no plain text in " + Path.GetFileName(kf) + ", " + kb.Length + " bytes)");
                Cmd("test-nn-ai-open");
                int art = NewTab(U + "/article.html");
                Ok(Val(Cmd("test-nn-ai-send What is two plus two?")) == "True", "a question is sent");
                var s1 = Wait(() => J(Cmd("test-nn-ai-state")), d => !B(d, "busy"), 20000);
                Req r1; lock (srv.Api) r1 = srv.Api.LastOrDefault();
                Ok(r1 != null && r1.Headers.ContainsKey("x-api-key") && r1.Headers["x-api-key"] == FakeKey && r1.Headers["anthropic-version"] == "2026-10-21" && r1.Body.Contains("\"model\":\"claude-opus-5-5\"") && r1.Body.Contains("\"stream\":true"), "the request: the key in x-api-key, anthropic-version 2026-10-21, Claude Opus 5.5 by default, streaming");
                Ok(r1 != null && r1.Headers.ContainsKey("anthropic-beta") && r1.Headers["anthropic-beta"] == "server-side-fallback-2026-07-01" && r1.Body.Contains("\"fallbacks\":\"default\""), "Opus asks for the server-side refusal fallback");
                Ok(r1 != null && !r1.Body.Contains("MARKER-PAGE-TEXT") && !r1.Body.Contains("<page"), "the page you're on is NOT sent without \"Use this page\"");
                var turns1 = s1.ContainsKey("turns") ? (ArrayList)s1["turns"] : new ArrayList(); var last1 = turns1.Count > 0 ? turns1[turns1.Count - 1] as Dictionary<string, object> : null;
                Ok(last1 != null && S(last1, "text") == "Hello from the stand-in." && D(s1, "updates") >= 4 && (S(s1, "view") ?? "").Contains("Hello from the stand-in."), "the answer streamed in piece by piece (" + D(s1, "updates") + " updates) and shows in the panel");
                var up = Val(Cmd("test-nn-ai-usepage"));
                var s2 = J(Cmd("test-nn-ai-state"));
                Ok(up == "True" && B(s2, "attached") && srv.Api.Count == 1, "Use this page: the page's text waits for the next question (nothing sent on the click)");
                Cmd("test-nn-ai-send Summarize it");
                Wait(() => J(Cmd("test-nn-ai-state")), d => !B(d, "busy"), 20000);
                Req r2; lock (srv.Api) r2 = srv.Api.LastOrDefault();
                Ok(r2 != null && r2 != r1 && r2.Body.Contains("MARKER-PAGE-TEXT-7781") && r2.Body.Contains("Patch notes"), "the next question carries the page's text and title");
                Ok(r2 != null && r2.Body.Contains("stand-in-signature-1") && r2.Body.Contains("What is two plus two?"), "the conversation goes along, the earlier answer's thinking block unchanged (signature kept)");
                var s3 = J(Cmd("test-nn-ai-state"));
                Ok(!B(s3, "attached"), "the page was sent once - the chip is gone");
                Ok(Val(Cmd("test-nn-ai-model claude-haiku-5-5")) == "claude-haiku-5-5", "the model picker: Claude Haiku 5.5");
                Cmd("test-nn-ai-send And in one word?");
                Wait(() => J(Cmd("test-nn-ai-state")), d => !B(d, "busy"), 20000);
                Req r3; lock (srv.Api) r3 = srv.Api.LastOrDefault();
                Ok(r3 != null && r3.Body.Contains("\"model\":\"claude-haiku-5-5\"") && !r3.Headers.ContainsKey("anthropic-beta") && !r3.Body.Contains("fallbacks"), "Haiku 5.5 is asked (no fallback header)");
                Cmd("test-nn-ai-send please-fail");
                var s4 = Wait(() => J(Cmd("test-nn-ai-state")), d => !B(d, "busy"), 20000);
                var t4 = (ArrayList)s4["turns"]; var last4 = t4[t4.Count - 1] as Dictionary<string, object>;
                Ok(B(last4, "failed") && (S(last4, "note") ?? "").Contains("busy"), "an API error (529 overloaded) is said plainly: " + S(last4, "note"));
                Cmd("test-nn-ai-send ok again");
                Wait(() => J(Cmd("test-nn-ai-state")), d => !B(d, "busy"), 20000);
                Req r5; lock (srv.Api) r5 = srv.Api.LastOrDefault();
                Ok(r5 != null && !r5.Body.Contains("please-fail"), "the failed question isn't sent again");
                Cmd("test-nn-ai-model claude-opus-5-5");
                // never in plain text anywhere in the test profile (settings, session, status, logs)
                var leaks = new List<string>();
                foreach (var f in Directory.GetFiles(run, "*", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "Local" + Path.DirectorySeparatorChar) && new FileInfo(f).Length < 8 * 1024 * 1024))
                    try { if (File.ReadAllText(f).Contains(FakeKey)) leaks.Add(Path.GetFileName(f)); } catch { }
                Ok(leaks.Count == 0, "the key is in no file in plain text (settings.json, session.json, status.json, logs)" + (leaks.Count > 0 ? ": " + string.Join(", ", leaks) : ""));
                var stj = Path.Combine(run, "settings.json"); Ok(File.Exists(stj) && !File.ReadAllText(stj).Contains("sk-ant"), "settings.json has no key");

                // ---- kept across a restart: the pinned tabs
                Cmd("test-settings AiOn=false");
                Quit(br); br = null; Thread.Sleep(1500);
                br = Start(exe, run, hostRules, api);
                Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Wait(RawTabs, t => t.Count >= 2, 30000); Thread.Sleep(1500);
                for (int i = 0; i < RawTabs().Count; i++) Cmd("test-mute " + i);
                var rt = Wait(Tabs, d => d.Count(x => B(x, "pinned")) == pinnedUrls.Count && d.Count > 0 && D(d[0], "width") > 0, 10000);
                var rpins = rt.Where(x => B(x, "pinned")).Select(x => S(x, "url")).ToList();
                Ok(rpins.SequenceEqual(pinnedUrls) && rt.Take(rpins.Count).All(x => B(x, "pinned")) && rt.Where(x => B(x, "pinned")).All(x => Math.Abs(D(x, "width") - 42) < 0.6), "after a restart the pinned tabs are back, first and compact: " + string.Join(", ", rpins));
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
