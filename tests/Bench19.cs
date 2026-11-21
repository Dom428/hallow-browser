// Hallow Browser 1.9 - the speed benchmark (kept for later comparisons, e.g. with Brave): start-up (a new profile, then warm),
// new tab, memory with 14 pages on 14 different sites (local stand-ins) and again after 10 minutes in the background, scrolling
// frame times on a long local page, and the browser's CPU / GPU use while a fake game runs next to a background video tab -
// game mode against normal. Off-screen like every other run (never shown, never focused, below-normal priority), its own test
// folder and pipes, only 127.0.0.1. It uses only test hooks that 1.8 already had, so the same run measures 1.8 and 1.9.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> bench[:quick]
// Speedometer 3 isn't run: it isn't on this PC and nothing is downloaded (for 2.0, if you agree to run it against browserbench.org).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Bench19
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        static StreamWriter report; static readonly Dictionary<string, object> result = new Dictionary<string, object>();
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(200); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static Dictionary<string, object> Perf() { var v = Val(Cmd("test-perf")); try { return v == null ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static double Mark(Dictionary<string, object> pf, string k) { if (pf == null) return -1; var m = pf["marks"] as Dictionary<string, object>; object o; return m != null && m.TryGetValue(k, out o) ? Convert.ToDouble(o) : -1; }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : v; } catch { return v; } }
        static readonly string[] Games = { "Discovery-d", "DeltaForceClient-Win64-Shipping" };
        static bool force; static bool GameRunning() { if (force) return false; foreach (var g in Games) { var p = Process.GetProcessesByName(g); if (p.Length > 0) return true; } return false; }
        static double Med(IEnumerable<double> x) { var l = x.OrderBy(v => v).ToList(); return l.Count == 0 ? -1 : l[l.Count / 2]; }
        static double Pct(IEnumerable<double> x, double p) { var l = x.OrderBy(v => v).ToList(); return l.Count == 0 ? -1 : l[Math.Min(l.Count - 1, (int)Math.Floor(l.Count * p))]; }
        static string F(double d) { return d.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }

        // ---- the browser's process tree (the exe and every WebView2 process under it), from a Toolhelp snapshot
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PE32 { public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint f, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr h, ref PE32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr h, ref PE32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        public static List<int> Tree(int root)
        {
            var parent = new Dictionary<int, int>(); var h = CreateToolhelp32Snapshot(2, 0);
            try { var e = new PE32 { dwSize = (uint)Marshal.SizeOf(typeof(PE32)) }; if (Process32FirstW(h, ref e)) do { parent[(int)e.th32ProcessID] = (int)e.th32ParentProcessID; } while (Process32NextW(h, ref e)); } finally { CloseHandle(h); }
            var r = new List<int> { root }; bool grew = true;
            while (grew) { grew = false; foreach (var kv in parent) if (!r.Contains(kv.Key) && r.Contains(kv.Value) && kv.Key != kv.Value) { r.Add(kv.Key); grew = true; } }
            return r;
        }
        static long PrivMb(int pid) { try { using (var p = Process.GetProcessById(pid)) return p.PrivateMemorySize64 / (1024 * 1024); } catch { return 0; } }
        static double CpuMs(IEnumerable<int> pids) { double s = 0; foreach (var id in pids) try { using (var p = Process.GetProcessById(id)) s += p.TotalProcessorTime.TotalMilliseconds; } catch { } return s; }
        // GPU: Windows' own "GPU Engine" counters (per process, no admin), summed over the browser's processes, sampled every 500 ms
        static double GpuPercent(List<int> pids, int seconds)
        {
            try
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                var names = cat.GetInstanceNames().Where(n => pids.Any(id => n.StartsWith("pid_" + id + "_"))).ToList();
                var cs = names.Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n, true)).ToList();
                foreach (var c in cs) try { c.NextValue(); } catch { }
                var samples = new List<double>();
                for (int i = 0; i < seconds * 2; i++) { Thread.Sleep(500); double s = 0; foreach (var c in cs) try { s += c.NextValue(); } catch { } samples.Add(s); }
                foreach (var c in cs) c.Dispose();
                return samples.Count == 0 ? -1 : samples.Average();
            }
            catch { return -1; }
        }

        // ---- local pages: 14 "sites" (each its own host, so the engine keeps them apart as it would real sites), a long page to scroll,
        // a background video (a canvas animation played through a <video>, and a tone that is muted before it starts - nothing is heard)
        static string SitePage(string host)
        {
            var sb = new StringBuilder("<!DOCTYPE html><html><head><meta charset=utf-8><title>" + host + "</title><style>body{font:15px Segoe UI;margin:24px;max-width:900px}article{margin:0 0 18px}img,canvas{display:block}</style></head><body><h1>" + host + "</h1>");
            for (int i = 0; i < 40; i++) sb.Append("<article><h2>Story " + i + "</h2><p>" + string.Join(" ", Enumerable.Repeat("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore.", 6)) + "</p></article>");
            sb.Append("<canvas id=c width=600 height=300></canvas><script>var x=document.getElementById('c').getContext('2d');for(var i=0;i<400;i++){x.fillStyle='hsl('+(i*7%360)+',60%,50%)';x.fillRect(Math.random()*600,Math.random()*300,30,30);}" +
                "var data=[];for(var i=0;i<20000;i++)data.push({id:i,t:'item '+i});window.tick=0;setInterval(function(){window.tick++;},1000);</script></body></html>");
            return sb.ToString();
        }
        static string LongPage()
        {
            var sb = new StringBuilder("<!DOCTYPE html><html><head><meta charset=utf-8><title>Long page</title><style>body{font:15px Segoe UI;margin:0 auto;max-width:860px}section{padding:12px;margin:10px 0;border-radius:10px;background:linear-gradient(90deg,#eef,#fee);box-shadow:0 2px 6px #0003}</style></head><body>");
            for (int i = 0; i < 600; i++) sb.Append("<section><h3>Row " + i + "</h3><p>" + string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 5)) + "</p></section>");
            sb.Append("<script>window.scrollTest=function(ms){return new Promise(function(res){var d=[],last=performance.now(),t0=last;function f(now){d.push(now-last);last=now;window.scrollBy(0,12);if(now-t0<ms)requestAnimationFrame(f);else res(JSON.stringify(d.slice(1)));}requestAnimationFrame(function(n){last=n;t0=n;requestAnimationFrame(f);});});};</script></body></html>");
            return sb.ToString();
        }
        const string VideoPage = "<!DOCTYPE html><html><head><title>Video</title></head><body style='margin:0;background:#000'><canvas id=c width=640 height=360 style='display:none'></canvas><video id=v width=640 height=360 autoplay muted></video><script>" +
            "var c=document.getElementById('c'),x=c.getContext('2d'),f=0;function draw(){f++;x.fillStyle='hsl('+(f%360)+',70%,40%)';x.fillRect(0,0,640,360);x.fillStyle='#fff';x.font='48px Segoe UI';x.fillText('frame '+f,40,200);}" +
            "setInterval(draw,33);document.getElementById('v').srcObject=c.captureStream(30);window.frames2=0;setInterval(function(){window.frames2++;},50);" +
            "window.startTone=function(){var a=new AudioContext(),o=a.createOscillator(),g=a.createGain();g.gain.value=0.05;o.connect(g);g.connect(a.destination);o.start();return a.state;};</script></body></html>";

        class Server
        {
            public int Port; TcpListener l; volatile bool stop; public Func<string, string, string> Route;
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
                        var path = lines[0].Split(' ')[1]; var host = "";
                        foreach (var ln in lines) if (ln.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) host = ln.Substring(5).Trim();
                        var body = Route(path, host); var bytes = Encoding.UTF8.GetBytes(body ?? "not found");
                        var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + (body == null ? "404 Not Found" : "200 OK") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nCache-Control: max-age=600\r\nConnection: close\r\n\r\n");
                        s.Write(hb, 0, hb.Length); s.Write(bytes, 0, bytes.Length); s.Flush();
                    }
                }
                catch { }
            }
        }

        public static int Main(string exe, string dir, string opts)
        {
            exe = Path.GetFullPath(exe); bool quick = opts.Contains("quick"), startOnly = opts.Contains("startonly"), nomcj = opts.Contains("nomcj"); force = opts.Contains("force"); bool scrollOnly = opts.Contains("scrollonly");
            int bgMinutes = quick ? 1 : 10;
            Directory.CreateDirectory(dir);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "bench.txt"), false) { AutoFlush = true };
            var ver = FileVersionInfo.GetVersionInfo(exe).FileVersion; result["exe"] = exe; result["version"] = ver; result["when"] = DateTime.Now.ToString("s");
            Note("benchmark of " + exe + " (" + ver + ")");
            var srv = new Server(); string longPage = LongPage();
            srv.Route = (path, host) =>
            {
                var h = host.Split(':')[0];
                if (path.StartsWith("/long.html")) return longPage;
                if (path.StartsWith("/video.html")) return VideoPage;
                if (path.StartsWith("/start.html")) return "<!DOCTYPE html><html><head><title>Start</title></head><body><h1>Start page</h1><p>A small local page.</p></body></html>";
                if (path.StartsWith("/site.html")) return SitePage(h);
                return null;
            };
            srv.Start(); var P = srv.Port;
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            var run = Path.Combine(dir, "profile"); try { if (Directory.Exists(run)) Directory.Delete(run, true); } catch { } Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            Func<string, Process> launch = arg =>
            {
                var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = "MAP *.bench.test 127.0.0.1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                if (nomcj) psi.EnvironmentVariables["HALLOW_BROWSER_NOMCJ"] = "1";
                var so = System.Text.RegularExpressions.Regex.Match(opts, "off=([a-z,]+)"); if (so.Success) psi.EnvironmentVariables["HALLOW_BROWSER_SPEEDOFF"] = so.Groups[1].Value;
                var p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return p;
            };
            Process br = null; var start = "http://127.0.0.1:" + P + "/start.html";
            try
            {
                // ================= start-up: the first start makes a new profile, then 4 warm starts (each opening the start page)
                var starts = new List<Dictionary<string, double>>();
                for (int s = 0; s < (scrollOnly ? 1 : 5); s++)
                {
                    if (GameRunning()) { Note("a game started - stopped"); return 3; }
                    var sw = Stopwatch.StartNew();
                    br = launch(s == 0 ? start : "");
                    Wait(() => Cmd("ping", 1500), r => r != null && r.StartsWith("ok"), 30000);
                    Wait(Tabs, t => t.Count > 0 && t.Any(x => (bool)x["ready"]), 30000);
                    Thread.Sleep(2500);
                    var pf = Perf(); var keep = new Dictionary<string, double>();
                    foreach (var k in new[] { "main", "settings", "theme", "runtime-found", "prefs-written", "engine-started", "build-start", "sidebar-start", "sidebar-done", "build-done", "window-built", "window", "chrome-first-paint", "environment", "first-navigation-completed", "first-page-loaded", "shield-ready", "prewarm-ready" }) { var v = Mark(pf, k); if (v >= 0) keep[k] = Math.Round(v, 1); }
                    starts.Add(keep);
                    Note("start " + (s == 0 ? "new profile" : "warm " + s) + ": " + string.Join(", ", keep.Select(kv => kv.Key + " " + F(kv.Value))));
                    if ((s < 4 && !scrollOnly) || startOnly) { Cmd("quit"); br.WaitForExit(20000); if (!br.HasExited) br.Kill(); Thread.Sleep(2500); }
                }
                result["starts"] = starts;
                Func<string, List<double>> warm = k => starts.Skip(1).Where(x => x.ContainsKey(k)).Select(x => x[k]).ToList();
                Note("SUMMARY start: first paint new profile " + F(starts[0].ContainsKey("chrome-first-paint") ? starts[0]["chrome-first-paint"] : -1) + " ms, warm median " + F(Med(warm("chrome-first-paint"))) +
                    " ms; first page loaded warm median " + F(Med(warm("first-navigation-completed"))) + " ms; engine ready warm median " + F(Med(warm("environment"))) + " ms; main() reached warm median " + F(Med(warm("main"))) + " ms");

                if (startOnly) { File.WriteAllText(Path.Combine(dir, "bench.json"), js.Serialize(result)); return 0; }
                if (!scrollOnly) {
                // ================= new tab (pre-warmed): 7 tries
                var nts = new List<double>();
                for (int i = 0; i < 7; i++) { Thread.Sleep(1800); Cmd("test-new"); Thread.Sleep(300); nts.Add(Math.Round(Mark(Perf(), "newtab-last"), 1)); }
                result["newTab"] = nts;
                Note("SUMMARY new tab (pre-warmed, until drawn): " + string.Join(", ", nts.Select(F)) + " ms, median " + F(Med(nts)));
                while (Tabs().Count > 1) Cmd("test-close " + (Tabs().Count - 1));
                }

                // ================= scrolling: rAF deltas while the long page scrolls 12 px a frame for 4 s
                Cmd("test-nav 0 http://127.0.0.1:" + P + "/long.html"); Thread.Sleep(1500);
                var frames = new List<double>();
                for (int r = 0; r < (scrollOnly ? 4 : 2); r++) { var raw = Eval(0, "scrollTest(4000)"); try { frames.AddRange(js.Deserialize<List<object>>(raw).Select(o => Convert.ToDouble(o))); } catch { } Eval(0, "window.scrollTo(0,0)"); Thread.Sleep(500); }
                if (frames.Count > 0)
                {
                    result["scroll"] = new Dictionary<string, object> { { "frames", frames.Count }, { "median", Med(frames) }, { "p95", Pct(frames, 0.95) }, { "max", frames.Max() }, { "over25ms", frames.Count(f => f > 25) } };
                    Note("SUMMARY scrolling: " + frames.Count + " frames, median " + F(Med(frames)) + " ms, p95 " + F(Pct(frames, 0.95)) + " ms, max " + F(frames.Max()) + " ms, " + frames.Count(f => f > 25) + " frames over 25 ms");
                }
                else Note("scrolling: no frames measured");

                if (scrollOnly) { Cmd("quit"); br.WaitForExit(20000); File.WriteAllText(Path.Combine(dir, "bench.json"), js.Serialize(result)); return 0; }
                // ================= memory: 14 pages on 14 sites, then after N minutes in the background
                if (GameRunning()) { Note("a game started - stopped"); return 3; }
                for (int i = 1; i <= 14; i++) { Cmd("test-new http://site" + i + ".bench.test:" + P + "/site.html"); Thread.Sleep(700); }
                Wait(Tabs, t => t.Count(x => (bool)x["ready"]) >= 15, 60000);
                Thread.Sleep(6000);
                Func<string, Dictionary<string, object>> mem = label =>
                {
                    var pf = Perf(); var tree = Tree(br.Id); long host = PrivMb(br.Id); long all = tree.Sum(id => PrivMb(id));
                    var tabs = Tabs();
                    var d = new Dictionary<string, object> { { "webviewMb", pf != null ? pf["memMb"] : null }, { "hostMb", host }, { "totalMb", all }, { "processes", tree.Count - 1 }, { "tabs", tabs.Count }, { "asleep", tabs.Count(x => x["asleep"] is bool && (bool)x["asleep"]) } };
                    Note("SUMMARY memory " + label + ": " + d["webviewMb"] + " MB in the engine's processes (" + d["processes"] + " processes), the browser exe " + host + " MB, total " + all + " MB private; " + d["tabs"] + " tabs, " + d["asleep"] + " asleep");
                    return d;
                };
                result["mem14"] = mem("14 pages loaded");
                Cmd("test-activate 0");   // the long page in front: the 14 sites are background tabs now
                var until = DateTime.Now.AddMinutes(bgMinutes).AddSeconds(30);
                while (DateTime.Now < until) { if (GameRunning()) { Note("a game started - stopped"); return 3; } Thread.Sleep(5000); }
                result["mem14bg"] = mem("after " + bgMinutes + " min in the background");
                // back to 4 tabs for the game test
                while (Tabs().Count > 4) Cmd("test-close " + (Tabs().Count - 1));

                // ================= game impact: a background video tab (muted, then a tone - never heard), 3 background pages, CPU and GPU
                int vt = Val(Cmd("test-new http://127.0.0.1:" + P + "/video.html")) == null ? -1 : Tabs().Count - 1;
                Thread.Sleep(2500); Cmd("test-mute " + vt); Thread.Sleep(300); Eval(vt, "startTone()");
                Cmd("test-activate 0"); Thread.Sleep(4000);
                Func<string, Dictionary<string, object>> load = label =>
                {
                    var tree = Tree(br.Id); var c0 = CpuMs(tree); var sw = Stopwatch.StartNew();
                    var gpu = GpuPercent(tree, 20);
                    var cpu = (CpuMs(Tree(br.Id)) - c0) / sw.Elapsed.TotalMilliseconds * 100.0;   // % of one core
                    var tabs = Tabs();
                    var d = new Dictionary<string, object> { { "cpuPercentOfOneCore", Math.Round(cpu, 1) }, { "gpuPercent", Math.Round(gpu, 2) }, { "processes", tree.Count - 1 }, { "asleep", tabs.Count(x => x["asleep"] is bool && (bool)x["asleep"]) } };
                    Note("SUMMARY game impact " + label + ": CPU " + F(cpu) + " % of one core, GPU " + gpu.ToString("0.00") + " %, " + d["asleep"] + " tabs asleep");
                    return d;
                };
                result["normal"] = load("normal (no game)");
                File.WriteAllText(gameFile, "Fake Game"); Cmd("test-tick"); Thread.Sleep(6000);
                result["game"] = load("game mode (fake game)");
                File.WriteAllText(gameFile, ""); Cmd("test-tick"); Thread.Sleep(2000);

                Cmd("quit"); br.WaitForExit(20000); if (!br.HasExited) br.Kill();
            }
            catch (Exception e) { Note("benchmark error: " + e); }
            finally { try { if (br != null && !br.HasExited) br.Kill(); } catch { } srv.Stop(); }
            File.WriteAllText(Path.Combine(dir, "bench.json"), js.Serialize(result));
            return 0;
        }
    }
}
