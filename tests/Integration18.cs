// Hallow Browser 1.8 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own
// test folder, engine and pipes, local pages and stand-ins only: a fake SponsorBlock server, a YouTube-like page with a video element
// and a Twitch-like page on 127.0.0.1 (the real SponsorBlock, YouTube and Twitch are never contacted - the browser is pointed at the
// stand-ins), a stand-in Hallow Clips pipe (test suffix) and a stand-in Hallow Hub folder. Play goes to a launch log and Steam's
// running flag to a test file: a game is never started.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v18
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
    public static class Integration18
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null || v == "null" ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> L(string reply) { var v = Val(reply); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static List<Dictionary<string, object>> Tabs() { return L(Cmd("test-tabs")); }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        static int I(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        static double D(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static List<string> A(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Cast<object>().Select(x => x == null ? null : x.ToString()).ToList() : new List<string>(); }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { return v == null ? null : js.Deserialize<object>(v) is string ? (string)js.Deserialize<object>(v) : v; } catch { return v; } }
        static double Num(int tab, string expr) { double d; return double.TryParse(Eval(tab, expr), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : -1; }
        static string Nav(int tab, string url) { return Cmd("test-nav " + tab + " " + url); }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Thread.Sleep(400); return i; }
        static List<int> Mine(string exe) { var r = new List<int>(); foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) r.Add(p.Id); } catch { } } return r; }
        static Dictionary<string, object> View() { return J(Cmd("test-g-view")); }
        static Dictionary<string, object> Sb(int tab) { return J(Cmd("test-g-sb " + tab)); }
        static Dictionary<string, object> Game() { return J(Cmd("test-g-game")); }

        // ---- the stand-ins: pages and the SponsorBlock API, every API request written down (path and headers)
        public class Req { public string Path, Cookie, Referer, Agent, Accept; }
        static readonly List<Req> reqs = new List<Req>();
        static List<Req> Api() { lock (reqs) return reqs.ToList(); }
        class Server
        {
            public int Port; TcpListener l; volatile bool stop;
            public Func<string, Req, Tuple<int, string, string>> Route;
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
                        var path = lines[0].Split(' ')[1];
                        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ln in lines.Skip(1)) { var i = ln.IndexOf(':'); if (i > 0) h[ln.Substring(0, i).Trim()] = ln.Substring(i + 1).Trim(); }
                        string v; var rq = new Req { Path = path, Cookie = h.TryGetValue("Cookie", out v) ? v : null, Referer = h.TryGetValue("Referer", out v) ? v : null, Agent = h.TryGetValue("User-Agent", out v) ? v : null, Accept = h.TryGetValue("Accept", out v) ? v : null };
                        var r = Route(path, rq) ?? Tuple.Create(404, "text/plain", "not found");
                        var bytes = Encoding.UTF8.GetBytes(r.Item3);
                        var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + r.Item1 + (r.Item1 == 200 ? " OK" : " Error") + "\r\nContent-Type: " + r.Item2 + "\r\nContent-Length: " + bytes.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        s.Write(hb, 0, hb.Length); s.Write(bytes, 0, bytes.Length); s.Flush();
                    }
                }
                catch { }
            }
        }
        // a YouTube-like watch page: a <video> playing 2 minutes of silence made in the page (a WAV blob - seekable, nothing heard)
        static string YouTubePage(string vid)
        {
            return "<!DOCTYPE html><html><head><title>Fake video " + vid + "</title></head><body style='margin:0;background:#000'><div id=player style='width:640px;height:360px'><video id=v width=640 height=360 muted></video></div><script>" +
                "(function(){var rate=8000,sec=120,n=rate*sec,buf=new ArrayBuffer(44+n),d=new DataView(buf);function s(o,t){for(var i=0;i<t.length;i++)d.setUint8(o+i,t.charCodeAt(i));}" +
                "s(0,'RIFF');d.setUint32(4,36+n,true);s(8,'WAVEfmt ');d.setUint32(16,16,true);d.setUint16(20,1,true);d.setUint16(22,1,true);d.setUint32(24,rate,true);d.setUint32(28,rate,true);d.setUint16(32,1,true);d.setUint16(34,8,true);s(36,'data');d.setUint32(40,n,true);" +
                "new Uint8Array(buf,44).fill(128);document.getElementById('v').src=URL.createObjectURL(new Blob([buf],{type:'audio/wav'}));})();</script></body></html>";
        }
        const string VidA = "hallowTst01", VidB = "hallowTst02", VidC = "hallowTst03";

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v18.txt"), false) { AutoFlush = true };
            string pA = SponsorBlock.Prefix(VidA), pB = SponsorBlock.Prefix(VidB), pC = SponsorBlock.Prefix(VidC);
            var srv = new Server();
            srv.Route = (path, rq) =>
            {
                if (path.StartsWith("/sb/"))
                {
                    lock (reqs) reqs.Add(rq);
                    var m = System.Text.RegularExpressions.Regex.Match(path, "^/sb/api/skipSegments/([0-9a-f]{4})\\?categories=([^&]*)&actionType=skip$");
                    if (!m.Success) return Tuple.Create(400, "text/plain", "bad request");
                    var cats = js.Deserialize<List<string>>(Uri.UnescapeDataString(m.Groups[2].Value));
                    if (m.Groups[1].Value == pC) return Tuple.Create(500, "text/plain", "server error");
                    if (m.Groups[1].Value != pA) return Tuple.Create(404, "text/plain", "Not Found");
                    Func<string, double, double, Dictionary<string, object>> seg = (c, a, b) => new Dictionary<string, object> { { "category", c }, { "actionType", "skip" }, { "segment", new[] { a, b } }, { "UUID", Guid.NewGuid().ToString("N") }, { "videoDuration", 120 }, { "locked", 0 }, { "votes", 3 } };
                    var mine = new[] { seg("sponsor", 2, 20), seg("selfpromo", 30, 40), seg("intro", 44, 50) }.Where(x => cats.Contains((string)x["category"])).ToArray();
                    var all = new object[] { new Dictionary<string, object> { { "videoID", "someOther_1" }, { "hash", pA + "00" }, { "segments", new[] { seg("sponsor", 1, 110) } } },
                        new Dictionary<string, object> { { "videoID", VidA }, { "hash", pA + "11" }, { "segments", mine } } };
                    return Tuple.Create(200, "application/json", js.Serialize(all));
                }
                if (path.StartsWith("/yt/watch?v=")) return Tuple.Create(200, "text/html; charset=utf-8", YouTubePage(path.Substring(12, 11)));
                if (path.StartsWith("/tw/popout/")) return Tuple.Create(200, "text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>Chat</title></head><body>chat for " + WebUtility.HtmlEncode(path.Split('/')[3]) + "</body></html>");
                if (path.StartsWith("/tw/"))
                    return Tuple.Create(200, "text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>" + WebUtility.HtmlEncode(path.Substring(4)) + " - Twitch</title></head><body><div id=player><video width=640 height=360></video>" +
                        "<button data-a-target=\"player-theatre-mode-button\" aria-label=\"Theatre Mode (alt+t)\" onclick=\"this.setAttribute('aria-label',/^Exit/.test(this.getAttribute('aria-label'))?'Theatre Mode (alt+t)':'Exit Theatre Mode (alt+t)');window.theatreClicks=(window.theatreClicks||0)+1\">T</button></div>" +
                        "<p>Channel points bonus: <button data-test-selector=\"community-points-summary\" aria-label=\"Claim Bonus\" onclick=\"window.claims=(window.claims||0)+1\">claim</button></p></body></html>");
                if (path.StartsWith("/plain.html")) return Tuple.Create(200, "text/html", "<!DOCTYPE html><title>Plain</title>plain");
                return null;
            };
            srv.Start(); var U = "http://127.0.0.1:" + srv.Port;
            var launchLog = Path.Combine(dir, "launch.log"); File.WriteAllText(launchLog, "");
            var steamFile = Path.Combine(dir, "steam.txt"); File.WriteAllText(steamFile, "");
            // ---- stand-ins: Hallow Hub's folder (no pipe: Play uses the launch link, which goes to the log) and Hallow Clips (status file now, pipe later)
            var hubDir = Path.Combine(run, "HallowHub"); Directory.CreateDirectory(hubDir);
            File.WriteAllText(Path.Combine(hubDir, "hub.json"), "{\"Games\":{\"steam:1422450\":{\"Name\":\"Deadlock\",\"Minutes\":50,\"LastPlayed\":\"2026-11-18T21:00:00\"}}}");
            Action<string> hubStatus = playing => File.WriteAllText(Path.Combine(hubDir, "status.json"), "{\"app\":\"Hub\",\"running\":true,\"playing\":[" + (playing == null ? "" : "{\"id\":\"steam:1422450\",\"name\":\"" + playing + "\"}") + "],\"recent\":[]}");
            hubStatus(null);
            var clipsDir = Path.Combine(run, "HallowClips"); Directory.CreateDirectory(clipsDir);
            File.WriteAllText(Path.Combine(clipsDir, "status.json"), "{\"app\":\"Clips\",\"recording\":false,\"clips\":[]}");
            var t0 = DateTime.Now;
            Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_LAUNCH_LOG"] = launchLog;
                psi.EnvironmentVariables["HALLOW_BROWSER_SPONSORBLOCK_URL"] = U + "/sb";
                psi.EnvironmentVariables["HALLOW_BROWSER_YOUTUBE_BASE"] = U + "/yt";
                psi.EnvironmentVariables["HALLOW_BROWSER_TWITCH_BASE"] = U + "/tw";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKE_STEAM"] = steamFile;
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1500);

                // ================= SponsorBlock: off by default, nothing contacted
                int yt = NewTab(U + "/yt/watch?v=" + VidA); Thread.Sleep(800);
                var sb = Sb(yt);
                Ok(!B(sb, "on") && S(sb, "vid") == null && Api().Count == 0, "sponsorblock: off by default - a YouTube video contacts nothing");
                Cmd("test-g-set sponsor 1");
                sb = Wait(() => Sb(yt), x => I(x, "count") >= 0, 8000);
                var rq = Api();
                Ok(I(sb, "count") == 2 && rq.Count == 1, "sponsorblock: turned on -> one lookup, two segments for this video (" + I(sb, "count") + ", " + rq.Count + " requests)");
                var path0 = rq.Count > 0 ? rq[0].Path : "";
                Ok(path0 == "/sb/api/skipSegments/" + pA + "?categories=" + Uri.EscapeDataString("[\"selfpromo\",\"sponsor\"]") + "&actionType=skip", "sponsorblock: the request is the 4-character hash prefix and the categories: " + path0);
                Ok(!path0.Contains(VidA) && rq.All(x => x.Cookie == null && x.Referer == null) && rq.All(x => x.Agent == "HallowBrowser/" + Program.Version), "sponsorblock: no video id, no cookies, no referrer - only \"HallowBrowser/" + Program.Version + "\"");
                Ok(Wait(() => Num(yt, "document.getElementById('v').readyState"), n => n >= 1, 8000) >= 1, "sponsorblock: the test video loaded");
                Eval(yt, "document.getElementById('v').currentTime=5");
                var ct = Wait(() => Num(yt, "document.getElementById('v').currentTime"), n => n >= 19.9, 6000);
                Ok(ct >= 19.9 && Eval(yt, "window.__hallowSB.log.join()") == "auto:sponsor", "sponsorblock: auto mode - playing into the sponsor jumps to its end (" + ct + " s)");
                var noteT = Eval(yt, "window.__hallowSB.root.getElementById('t').textContent"); var noteB = Eval(yt, "window.__hallowSB.root.getElementById('b').textContent");
                Ok(noteT == "Skipped sponsor SponsorBlock" && noteB == "Undo" && Eval(yt, "window.__hallowSB.host.style.display") == "block", "sponsorblock: a note says it skipped (credited to SponsorBlock), with Undo");
                Ok(Eval(yt, "document.querySelector('hallow-skip').shadowRoot===null") == "true", "sponsorblock: the note lives in a closed shadow root (the page's styles and scripts can't reach in)");
                Eval(yt, "window.__hallowSB.root.getElementById('b').click()");
                ct = Wait(() => Num(yt, "document.getElementById('v').currentTime"), n => n < 10, 4000);
                Ok(ct >= 1.9 && ct < 10, "sponsorblock: Undo goes back to the start of the segment (" + ct + " s)");
                Eval(yt, "document.getElementById('v').currentTime=6"); Thread.Sleep(1200);
                ct = Num(yt, "document.getElementById('v').currentTime"); var btn = Eval(yt, "window.__hallowSB.root.getElementById('b').textContent");
                Ok(ct < 10 && btn == "Skip sponsor \u25B8", "sponsorblock: a segment skipped once isn't skipped again by itself - a Skip button instead (" + btn + ")");
                Eval(yt, "window.__hallowSB.root.getElementById('b').click()");
                ct = Wait(() => Num(yt, "document.getElementById('v').currentTime"), n => n >= 19.9, 4000);
                Ok(ct >= 19.9 && Eval(yt, "String(window.__hallowSB.skips)") == "2", "sponsorblock: the Skip button skips");
                // button mode
                Cmd("test-g-set mode button"); Thread.Sleep(1500);
                Eval(yt, "document.getElementById('v').currentTime=31"); Thread.Sleep(1200);
                ct = Num(yt, "document.getElementById('v').currentTime"); btn = Eval(yt, "window.__hallowSB.root.getElementById('b').textContent");
                Ok(ct < 39 && btn == "Skip self-promotion \u25B8" && Eval(yt, "window.__hallowSB.host.style.display") == "block", "sponsorblock: \"Show a Skip button\" - nothing skipped by itself, a button (" + btn + ")");
                Eval(yt, "window.__hallowSB.root.getElementById('b').click()");
                ct = Wait(() => Num(yt, "document.getElementById('v').currentTime"), n => n >= 39.9, 4000);
                Ok(ct >= 39.9, "sponsorblock: ... which skips when clicked (" + ct + " s)");
                Eval(yt, "document.getElementById('v').currentTime=60"); Thread.Sleep(1000);
                Ok(Eval(yt, "window.__hallowSB.host.style.display") == "none", "sponsorblock: outside a segment the button goes away");
                // categories: intro added -> asked again with it; the intro segment shows up
                var n0 = Api().Count; Cmd("test-g-set cats sponsor,selfpromo,intro"); Thread.Sleep(1500);
                var last = Api().LastOrDefault();
                Ok(Api().Count == n0 + 1 && last != null && last.Path.Contains(Uri.EscapeDataString("[\"intro\",\"selfpromo\",\"sponsor\"]")) && I(Sb(yt), "count") == 3, "sponsorblock: a new kind picked -> asked again with it, the intro segment comes too");
                Eval(yt, "document.getElementById('v').currentTime=45"); Thread.Sleep(1200);
                Ok(Eval(yt, "window.__hallowSB.root.getElementById('b').textContent") == "Skip intro \u25B8", "sponsorblock: the intro gets its Skip button");
                // a video in the same page (YouTube moves between videos without loading a new page)
                n0 = Api().Count;
                Eval(yt, "history.pushState({},'','/yt/watch?v=" + VidB + "');1");
                sb = Wait(() => Sb(yt), x => S(x, "vid") == VidB && I(x, "count") >= 0, 6000);
                Ok(S(sb, "vid") == VidB && I(sb, "count") == 0 && Api().Count == n0 + 1 && Api().Last().Path.Contains("/" + pB + "?"), "sponsorblock: moving to another video in the page -> looked up by its own prefix (none: 404) (" + S(sb, "vid") + " " + I(sb, "count") + ")");
                Eval(yt, "document.getElementById('v').currentTime=5"); Thread.Sleep(1200);
                Ok(Num(yt, "document.getElementById('v').currentTime") < 6, "sponsorblock: ... and the old video's segments don't apply to it");
                // back to the first: answered from memory, not asked again
                n0 = Api().Count; Nav(yt, U + "/yt/watch?v=" + VidA); sb = Wait(() => Sb(yt), x => S(x, "vid") == VidA && I(x, "count") == 3, 6000);
                Ok(I(sb, "count") == 3 && Api().Count == n0, "sponsorblock: a video looked up a minute ago isn't asked for again (kept in memory)");
                // SponsorBlock down
                n0 = Api().Count; Nav(yt, U + "/yt/watch?v=" + VidC); sb = Wait(() => Sb(yt), x => S(x, "vid") == VidC && S(x, "note") != null, 8000);
                Ok((S(sb, "note") ?? "").Contains("couldn't be reached") && Api().Count == n0 + 1, "sponsorblock: SponsorBlock failing -> nothing skipped, the browser notes it (" + S(sb, "vid") + ": " + S(sb, "note") + ", " + (Api().Count - n0) + " requests)");
                // off again: the page forgets the segments, nothing more is asked
                Nav(yt, U + "/yt/watch?v=" + VidA); Wait(() => Sb(yt), x => S(x, "vid") == VidA && I(x, "count") == 3, 6000);
                n0 = Api().Count; Cmd("test-g-set sponsor 0"); Thread.Sleep(1200);
                Ok(Eval(yt, "String(window.__hallowSB.segs.length)") == "0" && S(Sb(yt), "vid") == null, "sponsorblock: switched off -> the page's segments are cleared");
                Nav(yt, U + "/yt/watch?v=" + VidB); Thread.Sleep(1500);
                Ok(Api().Count == n0, "sponsorblock: ... and nothing more is asked");

                // ================= Twitch: buttons, chat in the sidebar, full screen, theatre mode
                int tw = NewTab(U + "/tw/somestreamer");
                var v = View();
                Ok(B(v, "theatreBtn") && B(v, "clipBtn") && S(v, "open") == null, "twitch: on a channel the theatre and Clip it buttons show; the chat is off by default");
                int sites0 = I(v, "sites");
                Cmd("test-g-set twitchchat 1");
                v = Wait(View, x => S(x, "open") == Sidebar.ChatId, 5000);
                Ok(S(v, "open") == Sidebar.ChatId && S(v, "chatUrl") == U + "/tw/popout/somestreamer/chat" && B(v, "host") && S(v, "chatChannel") == "somestreamer", "twitch: chat on -> the channel's pop-out chat opens in the sidebar");
                v = Wait(View, x => S(x, "chatSrc") == U + "/tw/popout/somestreamer/chat", 10000);
                Ok(S(v, "chatSrc") == U + "/tw/popout/somestreamer/chat", "twitch: the chat panel loaded " + S(v, "chatSrc"));
                Ok(I(v, "sites") == sites0 && S(v, "openId") == null, "twitch: the chat isn't added to your sidebar sites, nor reopened at start");
                Cmd("test-g-fs 1"); Thread.Sleep(600); v = View();
                Ok(B(v, "fullscreen") && B(v, "host") && !B(v, "rail") && !B(v, "chrome"), "twitch: full screen -> the stream fills the window with the chat beside it (no rail, no browser bars)");
                Cmd("test-g-fs 0"); Thread.Sleep(600);
                Cmd("test-g-theatre 1"); Thread.Sleep(800); v = View();
                Ok(B(v, "theatre") && !B(v, "chrome") && D(v, "caption") == 0 && B(v, "host") && !B(v, "rail"), "theatre: the browser's bars, the rail and the drag strip go; the chat stays");
                Ok(Eval(tw, "document.querySelector('[data-a-target=player-theatre-mode-button]').getAttribute('aria-label')") == "Exit Theatre Mode (alt+t)", "theatre: Twitch's own theatre mode was turned on (its player button)");
                Cmd("test-g-fs 1"); Thread.Sleep(500); Cmd("test-g-fs 0"); Thread.Sleep(600); v = View();
                Ok(B(v, "theatre") && !B(v, "chrome"), "theatre: leaving full screen goes back to theatre mode");
                Cmd("test-g-theatre 0"); Thread.Sleep(800); v = View();
                Ok(!B(v, "theatre") && B(v, "chrome") && D(v, "caption") == 38 && Eval(tw, "document.querySelector('[data-a-target=player-theatre-mode-button]').getAttribute('aria-label')") == "Theatre Mode (alt+t)", "theatre: off -> the bars are back and Twitch's theatre mode is off again");
                Ok(Eval(tw, "String(window.claims||0)") == "0", "twitch: the channel-points bonus is never clicked (not built - Twitch's terms)");
                Cmd("test-activate " + yt); Thread.Sleep(500); v = View();
                Ok(S(v, "open") == null, "twitch: another tab -> the chat goes away");
                Cmd("test-activate " + tw); v = Wait(View, x => S(x, "open") == Sidebar.ChatId, 3000);
                Ok(S(v, "open") == Sidebar.ChatId, "twitch: back to the stream -> the chat is back");
                Cmd("test-g-side-hide"); Cmd("test-activate " + yt); Cmd("test-activate " + tw); Thread.Sleep(500); v = View();
                Ok(S(v, "open") == null, "twitch: closed with its X, the chat stays closed for that channel");
                Nav(tw, U + "/tw/otherstreamer"); v = Wait(View, x => S(x, "open") == Sidebar.ChatId, 4000);
                Ok(S(v, "open") == Sidebar.ChatId && S(v, "chatUrl") == U + "/tw/popout/otherstreamer/chat", "twitch: another channel -> its chat opens");
                Nav(tw, U + "/plain.html"); v = Wait(View, x => S(x, "open") == null, 4000);
                Ok(S(v, "open") == null && !B(v, "clipBtn") && !B(v, "theatreBtn"), "twitch: leaving the channel closes the chat; the stream buttons go");
                Nav(tw, U + "/tw/otherstreamer"); Thread.Sleep(800);

                // ================= Clip it (stand-in Clips)
                var r0 = Val(Cmd("test-g-clip " + tw)) ?? "";
                Ok(r0.Contains("isn't running") && r0.Contains("records your screen"), "clip it: Clips not running -> says so (" + r0 + ")");
                var clipsLines = new List<string>(); bool recording = false; string paused = null; string saveReply = null;
                Family.Serve("Clips", line =>
                {
                    lock (clipsLines) clipsLines.Add(line);
                    if (line == "status") return "ok " + js.Serialize(new Dictionary<string, object> { { "app", "Clips" }, { "recording", recording }, { "pausedFor", paused }, { "seconds", 30 } });
                    if (line == "save") return saveReply ?? "ok {\"file\":\"C:\\\\Clips\\\\Twitch 2026-11-21 20-00-00.mp4\"}";
                    return "error unknown command";
                });
                Thread.Sleep(300);
                r0 = Val(Cmd("test-g-clip " + tw)) ?? "";
                Ok(r0.Contains("isn't recording") && r0.Contains("Only while a game is running") && !clipsLines.Contains("save"), "clip it: Clips not recording -> explains why (only while a game runs by default) and asks nothing (" + r0.Substring(0, Math.Min(60, r0.Length)) + "...)");
                recording = true; paused = "Discord Clips";
                r0 = Val(Cmd("test-g-clip " + tw)) ?? "";
                Ok(r0.Contains("paused while Discord Clips") && !clipsLines.Contains("save"), "clip it: Clips paused for Discord Clips -> says so");
                paused = null;
                r0 = Val(Cmd("test-g-clip " + yt)) ?? "";
                Ok(r0.StartsWith("Bring the stream on screen") && !clipsLines.Contains("save"), "clip it: the stream not on screen (another tab) -> nothing asked");
                r0 = Val(Cmd("test-g-clip " + tw)) ?? "";
                Ok(r0 == "Clip saved (the last 30 s): C:\\Clips\\Twitch 2026-11-21 20-00-00.mp4" && clipsLines.Count(x => x == "save") == 1, "clip it: recording -> \"save\" sent once, \"Clip saved\" with the file from Clips' reply");
                Ok((S(View(), "toast") ?? "").StartsWith("Clip saved"), "clip it: the toast says Clip saved");
                saveReply = "error Nothing recorded yet";
                r0 = Val(Cmd("test-g-clip " + tw)) ?? "";
                Ok(r0 == "Hallow Clips couldn't save: Nothing recorded yet", "clip it: Clips' own error is passed on");

                // ================= game launch handoff (launch log, Steam flag from a file - no game is started)
                var games = Val(Cmd("test-g-fam")) ?? "";
                Ok(games.Contains("1422450"), "handoff: Your games has the stand-in Hub's game (" + games + ")");
                var g = Game(); Ok(S(g, "game") == null, "handoff: no game mode before");
                g = J(Cmd("test-g-play 1422450"));
                Ok(S(g, "result") == "link" && S(g, "game") == "Deadlock" && B(g, "active") && S(g, "priority") == "BelowNormal", "handoff: Play -> game mode at once (" + S(g, "game") + ", " + S(g, "priority") + ")");
                Ok(File.ReadAllText(launchLog).Contains("steam://rungameid/1422450"), "handoff: the launch link went to the test log (no game started)");
                Ok((S(g, "toast") ?? "").Contains("game mode is on"), "handoff: the toast says game mode is on");
                File.WriteAllText(steamFile, "1422450"); g = Game();
                Ok(S(g, "game") == "Deadlock" && B(g, "seen"), "handoff: Steam says it runs -> still game mode");
                File.WriteAllText(steamFile, ""); g = Game();
                Ok(S(g, "game") == null && !B(g, "active") && S(g, "priority") == "Normal", "handoff: the game closed -> game mode off");
                Cmd("test-g-set grace 3"); g = J(Cmd("test-g-play 1422450")); Thread.Sleep(3600); var g2 = Game();
                Ok(S(g, "game") == "Deadlock" && S(g2, "game") == null && !B(g2, "active"), "handoff: a game that never shows up -> off after the grace time");
                Cmd("test-g-set grace 120"); J(Cmd("test-g-play 1422450"));
                hubStatus("Deadlock"); g = Game();
                Ok(S(g, "game") == "Deadlock" && B(g, "seen"), "handoff: Hallow Hub says it's playing -> game mode (Hub's playing state)");
                hubStatus(null); g = Game();
                Ok(S(g, "game") == null && !B(g, "active"), "handoff: Hub says nothing is played any more -> game mode off");

                var status = Val(Cmd("status"));
                Ok(status != null && status.Contains("\"version\":\"" + Program.Version + "\"") && !status.Contains("127.0.0.1"), "status: " + Program.Version + ", no addresses");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var id in Mine(exe)) try { using (var p = Process.GetProcessById(id)) if (p.StartTime >= t0) p.Kill(); } catch { }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v18: ALL " + pass + " PASSED" : "INTEGRATION v18: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
