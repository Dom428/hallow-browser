// Hallow Browser 1.6 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own
// test folder, engine and pipes, local pages and stand-ins only: a fake 1.5 data folder (migrated on the first start), a second
// profile in its own process, stand-in Hallow Hub / Clips / Overlay (status files and pipes in the test folder - a game is never
// started: launch links go to a log file), Start menu shortcuts in the test folder, a fake battery file, a fake game file. Right
// clicks go into the off-screen page through DevTools input - never the real mouse.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v16
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowBrowser.Tests
{
    public static class Integration16
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string pipe = "Browser";
        static string Cmd(string line, int ms = 45000) { return Family.Send(pipe, line, ms); }
        static string CmdTo(string app, string line, int ms = 45000) { return Family.Send(app, line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null || v == "null" ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> L(string reply) { var v = Val(reply); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static List<Dictionary<string, object>> Tabs() { return L(Cmd("test-tabs")); }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static int I(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        static double D(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static List<object> A(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Cast<object>().ToList() : new List<object>(); }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { return v == null ? null : js.Deserialize<object>(v) as string ?? v; } catch { return v; } }
        static string Nav(int tab, string url) { return Cmd("test-nav " + tab + " " + url); }
        static int Active() { var t = Tabs(); for (int i = 0; i < t.Count; i++) if ((bool)t[i]["active"]) return i; return -1; }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Thread.Sleep(400); return i; }
        static void Mouse(string prefix, double x, double y, string button)
        {
            foreach (var type in new[] { "mousePressed", "mouseReleased" })
                Cmd(prefix + " Input.dispatchMouseEvent " + js.Serialize(new Dictionary<string, object> { { "type", type }, { "x", x }, { "y", y }, { "button", button }, { "buttons", type == "mousePressed" ? (button == "right" ? 2 : 1) : 0 }, { "clickCount", 1 } }));
        }
        // the centre of a word in the contenteditable test area (from a DOM range)
        static double[] WordAt(int tab, string word)
        {
            var r = Eval(tab, "(function(){var n=document.getElementById('e').firstChild;var i=n.data.indexOf(" + js.Serialize(word) + ");var g=document.createRange();g.setStart(n,i);g.setEnd(n,i+" + word.Length + ");var b=g.getBoundingClientRect();return JSON.stringify([b.left+b.width/2,b.top+b.height/2]);})()");
            try { var a = js.Deserialize<double[]>(r); return a; } catch { return new double[] { 50, 30 }; }
        }
        static byte[] Png(int size, Color c)
        {
            var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null); var px = new byte[size * size * 4];
            for (int i = 0; i < size * size; i++) { px[i * 4] = c.B; px[i * 4 + 1] = c.G; px[i * 4 + 2] = c.R; px[i * 4 + 3] = 255; }
            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, size, size), px, size * 4, 0);
            var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(bmp)); var ms = new MemoryStream(); e.Save(ms); return ms.ToArray();
        }
        static List<int> Mine(string exe) { var r = new List<int>(); foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) r.Add(p.Id); } catch { } } return r; }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v16.txt"), false) { AutoFlush = true };
            int P = 0;
            var srv = new SlowServer();
            var icon192 = Png(192, Color.FromRgb(0x8B, 0x5C, 0xF6)); var fav32 = Png(32, Color.FromRgb(0x22, 0xC5, 0x5E));
            srv.Route = (path) =>
            {
                Func<string, Tuple<string, byte[]>> H = s => Tuple.Create("text/html; charset=utf-8", Encoding.UTF8.GetBytes(s));
                if (path.StartsWith("/pwa/manifest.json")) return Tuple.Create("application/manifest+json", Encoding.UTF8.GetBytes("{\"name\":\"Hallow Raids\",\"short_name\":\"Raids\",\"start_url\":\"/pwa/index.html?app=1\",\"scope\":\"/pwa/\",\"icons\":[{\"src\":\"/pwa/icon-192.png\",\"sizes\":\"192x192\",\"type\":\"image/png\"},{\"src\":\"/pwa/icon.svg\",\"sizes\":\"any\",\"type\":\"image/svg+xml\"}]}"));
                if (path.StartsWith("/pwa/icon-192.png")) return Tuple.Create("image/png", icon192);
                if (path.StartsWith("/pwa/two.html")) return H("<!DOCTYPE html><html><head><title>Raids two</title></head><body>two</body></html>");
                if (path.StartsWith("/pwa/index.html")) return H("<!DOCTYPE html><html><head><title>Raid page</title><link rel=manifest href=/pwa/manifest.json></head><body style='margin:0'><a id=out href='http://localhost:" + P + "/other.html' style='position:absolute;left:10px;top:10px;width:300px;height:40px;display:block;background:#ccc'>out</a><a id=in href='/pwa/two.html' style='position:absolute;left:10px;top:80px;width:300px;height:40px;display:block;background:#ccc'>in</a></body></html>");
                if (path.StartsWith("/plain.html")) return H("<!DOCTYPE html><html><head><title>Plain site</title><link rel=icon href=/fav.png></head><body>plain</body></html>");
                if (path.StartsWith("/fav.png")) return Tuple.Create("image/png", fav32);
                if (path.StartsWith("/other.html")) return H("<!DOCTYPE html><html><head><title>Other site</title></head><body>other</body></html>");
                if (path.StartsWith("/link.html")) return H("<!DOCTYPE html><html><head><title>Links</title></head><body style='margin:0'><a id=l href='http://127.0.0.1:" + P + "/target.html' style='position:absolute;left:10px;top:10px;width:300px;height:40px;display:block'>the link</a></body></html>");
                if (path.StartsWith("/target.html")) return H("<!DOCTYPE html><html><head><title>Target</title></head><body>target</body></html>");
                if (path.StartsWith("/spell.html")) return H("<!DOCTYPE html><html><head><title>Spelling</title></head><body style='margin:0'><div id=e contenteditable=true spellcheck=true style='position:absolute;left:10px;top:10px;width:900px;font:30px Arial'>helo wrods here sentance</div><script>var e=document.getElementById('e');e.focus();var r=document.createRange();r.selectNodeContents(e);r.collapse(false);var s=getSelection();s.removeAllRanges();s.addRange(r);</script></body></html>");
                if (path.StartsWith("/one.html")) return H("<!DOCTYPE html><html><head><title>Page one</title></head><body>one</body></html>");
                if (path.StartsWith("/two.html")) return H("<!DOCTYPE html><html><head><title>Page two</title></head><body>two</body></html>");
                return null;
            };
            srv.Start(); P = srv.Port;
            var U1 = "http://127.0.0.1:" + P; var U2 = "http://localhost:" + P;
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            var battery = Path.Combine(dir, "battery.txt"); File.WriteAllText(battery, "ac");
            var launchLog = Path.Combine(dir, "launch.log"); File.WriteAllText(launchLog, "");
            // ---- stand-ins: Hallow Hub, Clips and Overlay (status files in the test folder, pipes with the test suffix)
            var hubDir = Path.Combine(run, "HallowHub"); Directory.CreateDirectory(Path.Combine(hubDir, "meta", "2507950"));
            File.WriteAllText(Path.Combine(hubDir, "hub.json"), "{\"Games\":{\"steam:2507950\":{\"Name\":\"Delta Force\",\"Minutes\":900,\"LastPlayed\":\"2026-11-20T21:00:00\"},\"steam:1422450\":{\"Name\":\"Deadlock\",\"Minutes\":50,\"LastPlayed\":\"2026-11-18T21:00:00\"}}}");
            File.WriteAllText(Path.Combine(hubDir, "status.json"), "{\"app\":\"Hub\",\"playing\":[],\"recent\":[]}");
            File.WriteAllBytes(Path.Combine(hubDir, "meta", "2507950", "shot0.jpg"), Png(64, Colors.DarkRed));
            var clipsDir = Path.Combine(run, "HallowClips"); Directory.CreateDirectory(clipsDir);
            var clipFile = Path.Combine(dir, "clips", "Deadlock 2026-11-20.mp4"); Directory.CreateDirectory(Path.GetDirectoryName(clipFile)); File.WriteAllBytes(clipFile, new byte[1024]);
            var thumb = Path.Combine(dir, "clips", "thumb.jpg"); File.WriteAllBytes(thumb, Png(64, Colors.Teal));
            File.WriteAllText(Path.Combine(clipsDir, "status.json"), js.Serialize(new Dictionary<string, object> { { "app", "Clips" }, { "clips", new object[] { new Dictionary<string, object> { { "file", clipFile }, { "game", "Deadlock" }, { "time", DateTime.Now.AddHours(-2).ToString("s") }, { "durationSec", 30.5 }, { "thumb", thumb } } } } }));
            var ovDir = Path.Combine(run, "HallowOverlay"); Directory.CreateDirectory(ovDir);
            bool ovVisible = true; Action ovWrite = () => File.WriteAllText(Path.Combine(ovDir, "status.json"), "{\"app\":\"Overlay\",\"visible\":" + (ovVisible ? "true" : "false") + "}");
            ovWrite();
            bool hubPlays = false; var hubLines = new List<string>(); var ovLines = new List<string>();
            Family.Serve("Hub", line => { lock (hubLines) hubLines.Add(line); if (line == "ping") return "ok {\"app\":\"Hub\",\"version\":\"2.16\"}"; if (line.StartsWith("play ")) return hubPlays ? "ok" : "error unknown command"; return "error unknown command"; });
            Family.Serve("Overlay", line => { lock (ovLines) ovLines.Add(line); if (line == "ping") return "ok {\"app\":\"Overlay\",\"version\":\"1.21\"}"; if (line == "toggle") { ovVisible = !ovVisible; ovWrite(); return "ok"; } return "error unknown command"; });
            // ---- a fake 1.5 data folder: settings and two open tabs from "before profiles"
            File.WriteAllText(Path.Combine(run, "settings.json"), "{\"Search\":\"google\",\"SleepMinutes\":15,\"SettingsVersion\":2,\"RestoreSession\":true}");
            File.WriteAllText(Path.Combine(run, "session.json"), js.Serialize(new Dictionary<string, object> { { "Tabs", new object[] { new Dictionary<string, object> { { "Url", U1 + "/one.html" }, { "Title", "Page one" } }, new Dictionary<string, object> { { "Url", U1 + "/two.html" }, { "Title", "Page two" } } } }, { "Active", 0 } }));
            var t0 = DateTime.Now;
            Process br = null;
            Func<string, Process> launch = args =>
            {
                var psi = new ProcessStartInfo(exe, args ?? "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEBATTERY"] = battery;
                psi.EnvironmentVariables["HALLOW_BROWSER_LAUNCH_LOG"] = launchLog;
                var pr = Process.Start(psi); try { pr.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return pr;
            };
            Action lowerAll = () => { foreach (var id in Mine(exe)) try { using (var p = Process.GetProcessById(id)) p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { } };
            try
            {
                br = launch(null);
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 2 && (bool)t[0]["ready"], 30000); Thread.Sleep(1500);

                // ================= the migration on the first start
                var info = J(Cmd("test-y-info"));
                Ok(S(info, "profile") == "default" && S(info, "pipe") == "Browser" && File.Exists(Path.Combine(run, "profiles.json")), "migration: the first 1.6 start makes 1.5's data the Default profile (profiles.json)");
                Ok(S(info, "backup") != null && File.Exists(Path.Combine(run, "backup-1.5", "settings.json")) && File.ReadAllText(Path.Combine(run, "backup-1.5", "settings.json")).Contains("google"), "migration: 1.5's settings files were backed up first (backup-1.5)");
                var tabs = Tabs();
                Ok(tabs.Count >= 2 && (string)tabs[0]["url"] == U1 + "/one.html" && tabs.Any(t => (string)t["url"] == U1 + "/two.html"), "migration: 1.5's open tabs came back");
                Ok(Val(Cmd("test-m-settings")).Contains("\"search\":\"google\""), "migration: 1.5's settings kept (search engine)");
                Ok((S(info, "webview") ?? "") == "" || S(info, "webview") == "Default", "migration: the Default profile is the engine's default WebView2 profile (1.5's sign-ins): \"" + S(info, "webview") + "\" at " + (S(info, "profilePath") ?? "").Replace(run, "<test>"));
                Ok(S(info, "shield") == "extension", "Shield runs in the Default profile");
                Ok(S(info, "chip") == null, "one profile: no profile chip in the title bar");

                // ================= the Hallow family on the new tab page (stand-ins)
                var famJson = Val(Cmd("test-y-famrefresh"));
                Ok(famJson != null && famJson.Contains("\"games\":[") && famJson.Contains("Delta Force") && famJson.Contains("\"clips\":[") && famJson.Contains("\"visible\":true"), "family: family.json has your games, recent clips and the Overlay switch (stand-ins)");
                int nt = NewTab(""); Thread.Sleep(800);
                Eval(nt, "window.hallowFamily()"); Thread.Sleep(600);
                var cards = Eval(nt, "document.querySelectorAll('#hf .hc').length"); var ovText = Eval(nt, "(document.querySelector('#hf .ov')||{}).textContent");
                Ok(cards == "3" && ovText == "Overlay: on", "family: the new tab page shows 2 games + 1 clip and \"Overlay: on\" (" + cards + ", " + ovText + ")");
                var pics = Eval(nt, "Array.from(document.querySelectorAll('#hf .pic')).filter(function(p){return p.style.backgroundImage.indexOf('fam/')>0}).length");
                Ok(pics == "2", "family: Hub's game picture and the clip's thumbnail show (from the page's own folder): " + pics);
                Nav(nt, "https://newtab.hallow/index.html?lazy"); Thread.Sleep(1500);
                var lazy = Eval(nt, "(function(){var f=performance.getEntriesByType('resource').filter(function(e){return e.name.indexOf('family.json')>=0})[0];var p=performance.getEntriesByType('paint').filter(function(e){return e.name==='first-contentful-paint'})[0];var n=performance.getEntriesByType('navigation')[0];return JSON.stringify({fam:f?f.startTime:-1,fcp:p?p.startTime:-1,dcl:n?n.domContentLoadedEventEnd:-1,load:n?n.loadEventEnd:-1});})()");
                var lz = js.Deserialize<Dictionary<string, object>>(lazy ?? "{}");
                Ok(D(lz, "fam") > 0 && D(lz, "fam") >= D(lz, "load") && D(lz, "fam") >= D(lz, "fcp"), "family: the rows load after the page is drawn (family.json requested at " + D(lz, "fam").ToString("0") + " ms; first paint " + D(lz, "fcp").ToString("0") + ", page loaded " + D(lz, "load").ToString("0") + ")");
                // Play: Hub 2.16 has no launch command -> the game's own link, written to the log (never started)
                Eval(nt, "document.querySelector('#hf .pl').click()"); Thread.Sleep(1500);
                var log1 = File.ReadAllText(launchLog);
                Ok(log1.Trim() == "steam://rungameid/2507950" && hubLines.Any(l => l == "play steam:2507950"), "family: Play asks Hub first (stand-in answers \"unknown command\" like Hub 2.16), then the game's own link steam://rungameid/2507950 - logged, not started");
                hubPlays = true; Eval(nt, "document.querySelector('#hf .pl').click()"); Thread.Sleep(1500);
                var fam = J(Cmd("test-y-family"));
                Ok(File.ReadAllText(launchLog).Trim() == "steam://rungameid/2507950" && I(fam, "hubPlays") == 1, "family: when Hub takes \"play\", Hub starts it (no link opened)");
                Eval(nt, "document.querySelectorAll('#hf .hc')[2].click()"); Thread.Sleep(800);
                Ok(File.ReadAllText(launchLog).Contains(clipFile) && I(J(Cmd("test-y-family")), "clipOpens") == 1, "family: a click on a clip opens that clip file (logged in tests)");
                Eval(nt, "document.querySelector('#hf .ov').click()"); Thread.Sleep(2200);
                var ovText2 = Eval(nt, "(document.querySelector('#hf .ov')||{}).textContent");
                Ok(ovLines.Contains("toggle") && !ovVisible && ovText2 == "Overlay: off", "family: the Overlay switch sends Overlay \"toggle\" over its pipe and shows the new state (" + ovText2 + ")");
                Cmd("test-eval " + nt + " chrome.webview.postMessage('hallow:nt-play\\t999999')"); Thread.Sleep(800);
                Ok(File.ReadAllText(launchLog).Split('\n').Count(l => l.Trim().Length > 0) == 2, "family: a Play for a game that isn't in the list does nothing");
                File.Delete(Path.Combine(clipsDir, "status.json")); Cmd("test-y-famrefresh"); Eval(nt, "window.hallowFamily()"); Thread.Sleep(600);
                Ok(Eval(nt, "document.querySelectorAll('#hf .hc').length") == "2" && Eval(nt, "document.body.innerText.indexOf('RECENT CLIPS')") == "-1", "family: Clips not installed = no clips row");
                foreach (var a in new[] { "Hub", "Overlay" }) File.Delete(Path.Combine(run, "Hallow" + a, "status.json"));
                Cmd("test-y-famrefresh"); Eval(nt, "window.hallowFamily()"); Thread.Sleep(600);
                Ok(Eval(nt, "document.getElementById('hf').children.length") == "0", "family: none of them installed = nothing on the page");

                // ================= spell check and Add to dictionary
                int sp = NewTab(U1 + "/spell.html"); Thread.Sleep(600);
                Cmd("test-x-cdp " + sp + " Input.insertText {\"text\":\" \"}"); Thread.Sleep(2500);
                var wpos = WordAt(sp, "wrods"); Mouse("test-x-cdp " + sp, wpos[0], wpos[1], "right"); Thread.Sleep(900);
                var menu = J(Cmd("test-y-menu"));
                var items = A(menu, "items").Cast<string>().ToList();
                Ok(items.Count(x => x == "spellcheck") >= 1, "spell check: a misspelled word gets the engine's suggestions (" + items.Count(x => x == "spellcheck") + " in its menu) - Windows' spell checker");
                Ok(S(menu, "word") == "wrods" && items.Any(x => x.StartsWith("Add “wrods” to dictionary")), "spell check: the menu offers \"Add “wrods” to dictionary\" right after the suggestions");
                Cmd("test-y-addword wrods");
                var spj = J(Cmd("test-y-spell"));
                Ok(A(spj, "words").Contains("wrods") && (S(spj, "file") ?? "").StartsWith("wrods\nchecksum_v1 = "), "spell check: the word is in the profile's Custom Dictionary.txt (Chromium's own format)");
                Note("spell check: Windows' spell checker languages here: " + string.Join(", ", A(spj, "langs").Cast<string>()));

                // ================= a second profile in its own process
                var gid = Val(Cmd("test-y-addprofile Gaming"));
                Ok(gid == "gaming" && S(J(Cmd("test-y-info")), "chip") == "Default" && (S(J(Cmd("test-y-info")), "title") ?? "").Contains("(Default)"), "profiles: with a second profile the window shows its profile (chip and title)");
                int lk = NewTab(U1 + "/link.html"); Thread.Sleep(500);
                Mouse("test-x-cdp " + lk, 60, 25, "right"); Thread.Sleep(900);
                var items2 = A(J(Cmd("test-y-menu")), "items").Cast<string>().ToList();
                Ok(items2.Any(x => x == "Open link in profile:[Gaming]"), "profiles: right-click a link > Open link in profile > Gaming (" + string.Join(", ", items2.Where(x => x.Contains("profile"))) + ")");
                Eval(lk, "document.cookie='who=default; path=/';document.cookie");
                Cmd("test-y-openinprofile gaming " + U1 + "/target.html");
                var gping = Wait(() => CmdTo("Browser-gaming", "ping", 2000), r => r != null && r.StartsWith("ok"), 30000); lowerAll();
                Ok(gping != null && gping.Contains("1.6"), "profiles: Gaming starts as its own process (its pipe answers): " + gping);
                var gt = Wait(() => L(CmdTo("Browser-gaming", "test-tabs")), t => t.Any(x => ((string)x["url"]) == U1 + "/target.html" && (bool)x["ready"]), 20000);
                Ok(gt.Any(x => ((string)x["url"]) == U1 + "/target.html"), "profiles: the link opened in the Gaming profile");
                var ginfo = J(CmdTo("Browser-gaming", "test-y-info"));
                Ok(S(ginfo, "profile") == "gaming" && S(ginfo, "webview") == "Hallow_gaming" && (S(ginfo, "profilePath") ?? "").EndsWith("WV2Profile_hallow_gaming"), "profiles: Gaming runs on its own WebView2 profile (Hallow_gaming)");
                Ok(I(ginfo, "browserPid") == I(J(Cmd("test-y-info")), "browserPid") && I(ginfo, "browserPid") > 0, "profiles: ... on the same engine (one browser process for both)");
                Ok(Wait(() => S(J(CmdTo("Browser-gaming", "test-y-info")), "shield"), s => s == "extension", 15000) == "extension", "profiles: Shield is loaded in the Gaming profile too");
                int gti = gt.FindIndex(x => ((string)x["url"]) == U1 + "/target.html");
                var gcookie = Val(CmdTo("Browser-gaming", "test-eval " + gti + " document.cookie"));
                var dcookie = Eval(lk, "document.cookie");
                Ok(dcookie == "who=default" && gcookie == "\"\"", "profiles: separate sign-ins - a cookie set in Default isn't in Gaming (" + dcookie + " / " + gcookie + ")");
                Ok(S(ginfo, "chip") == "Gaming" && (S(ginfo, "history") ?? "").Contains(Path.Combine("Profiles", "gaming")) && S(ginfo, "dir").EndsWith(Path.Combine("Profiles", "gaming")), "profiles: Gaming's chip, its own history and files (Profiles\\gaming)");
                CmdTo("Browser-gaming", "invoke private-window"); Thread.Sleep(3500);
                var gw = L(CmdTo("Browser-gaming", "test-y-wins")); var gpriv = gw.FirstOrDefault(x => B(x, "private"));
                Ok(gpriv != null && S(gpriv, "profile") == "HallowPrivate_gaming" && B(gpriv, "inprivate"), "profiles: Gaming's private window is its own InPrivate profile (" + S(gpriv, "profile") + ")");
                // sync per profile: Default in the folder, Gaming in its sub-folder
                var syncDir = Path.Combine(dir, "sync"); Directory.CreateDirectory(syncDir);
                Cmd("test-m-sync-set " + syncDir); CmdTo("Browser-gaming", "test-m-sync-set " + syncDir);
                Cmd("test-m-sync-now"); CmdTo("Browser-gaming", "test-m-sync-now");
                Ok(File.Exists(Path.Combine(syncDir, SyncCore.FileName)) && File.Exists(Path.Combine(syncDir, "profile-gaming", SyncCore.FileName)), "profiles: sync keeps working per profile (Default in the folder, Gaming in profile-gaming)");
                CmdTo("Browser-gaming", "test-m-sync-set off"); Cmd("test-m-sync-set off");
                var gmenu = Val(Cmd("menu"));
                Ok(gmenu != null && gmenu.Contains("\"id\":\"profile-gaming\"") && gmenu.Contains("Open profile: Gaming"), "profiles: the family \"menu\" (Hub's tray) offers Open profile: Gaming");
                CmdTo("Browser-gaming", "quit"); Wait(() => Family.Running("Browser-gaming"), r => !r, 15000); Thread.Sleep(1500);
                Ok(!Family.Running("Browser-gaming"), "profiles: Gaming quit on its own; Default goes on");

                // ================= install a site as an app
                int pwa = NewTab(U1 + "/pwa/index.html"); Thread.Sleep(800);
                var app = J(Cmd("test-y-install " + pwa));
                Ok(app != null && S(app, "Name") == "Hallow Raids" && S(app, "Url") == U1 + "/pwa/index.html?app=1" && S(app, "Scope") == U1 + "/pwa/" && S(app, "IconFrom") == "manifest", "apps: installed from the page's manifest (name, start, scope, icon): " + S(app, "Name") + " / " + S(app, "IconFrom"));
                var apps = L(Cmd("test-y-apps")); var a0 = apps.FirstOrDefault();
                Ok(a0 != null && S(a0, "lnkArgs") == "--app \"" + U1 + "/pwa/index.html?app=1\" --profile default" && string.Equals(S(a0, "lnkTarget"), exe, StringComparison.OrdinalIgnoreCase) && I(a0, "ico") > 1000, "apps: a Start menu shortcut \"HallowBrowser.exe --app <url> --profile default\" with the site's icon (in the test folder)");
                var wins = Wait(() => L(Cmd("test-y-wins")), w => w.Any(x => S(x, "app") == "Hallow Raids" && A(x, "tabs").Count == 1), 15000);
                var aw = wins.FirstOrDefault(x => S(x, "app") == "Hallow Raids");
                Ok(aw != null && S(aw, "title") == "Hallow Raids" && (string)A(aw, "tabs")[0] == U1 + "/pwa/index.html?app=1" && !B(aw, "first"), "apps: it opens in its own window (its name, no tabs)");
                Ok(!Tabs().Any(t => (string)t["url"] == U1 + "/pwa/index.html"), "apps: the tab it came from moved into the app window");
                Thread.Sleep(1500);
                Mouse("test-y-appcdp", 60, 25, "left"); Thread.Sleep(1800);
                Ok(Tabs().Any(t => (string)t["url"] == U2 + "/other.html") && L(Cmd("test-y-wins")).Any(x => S(x, "app") == "Hallow Raids" && ((string)A(x, "tabs")[0]).Contains("/pwa/")), "apps: a link to another site opens in the browser; the app window stays on its site");
                Mouse("test-y-appcdp", 60, 95, "left"); Thread.Sleep(1500);
                Ok(L(Cmd("test-y-wins")).Any(x => S(x, "app") == "Hallow Raids" && ((string)A(x, "tabs")[0]) == U1 + "/pwa/two.html"), "apps: a link inside its scope stays in the app window");
                int pl = NewTab(U1 + "/plain.html"); Thread.Sleep(2000);
                var app2 = J(Cmd("test-y-install " + pl));
                Ok(app2 != null && S(app2, "Name") == "Plain site" && S(app2, "IconFrom") == "favicon" && S(app2, "Scope") == U1 + "/", "apps: a site without a manifest - its title and the favicon the engine already has");
                Ok(Val(Cmd("test-y-appspage")) == "2", "apps: hallow://apps lists both");
                var plainEntry = L(Cmd("test-y-apps")).FirstOrDefault(x => S(x["app"] as Dictionary<string, object>, "Name") == "Plain site"); var lnk2 = plainEntry == null ? null : S(plainEntry["app"] as Dictionary<string, object>, "Shortcut");
                Cmd("test-y-uninstall " + S(app2, "Id")); Thread.Sleep(800);
                Ok(L(Cmd("test-y-apps")).Count == 1 && lnk2 != null && !File.Exists(lnk2) && !L(Cmd("test-y-wins")).Any(x => S(x, "app") == "Plain site"), "apps: Uninstall removes it, its window and its Start menu shortcut");
                // the shortcut itself: while the profile runs it hands over; with no app window open, it opens one
                Cmd("test-y-appclose"); Thread.Sleep(1200);
                var sh = launch(S(a0, "lnkArgs")); sh.WaitForExit(20000);
                var wins2 = Wait(() => L(Cmd("test-y-wins")), w => w.Any(x => S(x, "app") == "Hallow Raids"), 15000);
                Ok(sh.ExitCode == 0 && wins2.Count(x => S(x, "app") == "Hallow Raids") == 1, "apps: the Start menu shortcut's command opens the app window (handed to the running profile)");
                var sh2 = launch(S(a0, "lnkArgs")); sh2.WaitForExit(20000); Thread.Sleep(1500);
                Ok(L(Cmd("test-y-wins")).Count(x => S(x, "app") == "Hallow Raids") == 1, "apps: again = the same window comes forward (not a second one)");
                Cmd("test-y-appclose"); Thread.Sleep(800);

                // ================= reading list and notes
                int r1 = NewTab(U1 + "/one.html?read");
                Ok(Val(Cmd("test-y-readadd " + r1)) == "1" && L(Cmd("test-y-readlist")).Any(x => S(x, "Url") == U1 + "/one.html?read" && !B(x, "Read")), "reading list: Save for later (Ctrl+Shift+D) adds the page, unread");
                var pr = J(Cmd("test-y-panel reading"));
                Ok(S(pr, "open") == "reading" && A(pr, "text").Cast<string>().Any(x => x == "Unread (1)") && A(pr, "text").Cast<string>().Any(x => x == "Page one"), "reading list: the side panel lists it under Unread");
                Cmd("test-y-readmark 1 " + U1 + "/one.html?read");
                var pr2 = J(Cmd("test-y-panel reading"));
                Ok(A(pr2, "text").Cast<string>().Any(x => x == "Unread (0)") && L(Cmd("test-y-readlist")).All(x => B(x, "Read")), "reading list: marked as read");
                Ok(Cmd("test-y-note Meet at the north gate") == "ok", "notes: typed into the notes panel for this page");
                Thread.Sleep(500);
                var notes = L(Cmd("test-y-notes"));
                Ok(notes.Count == 1 && S(notes[0], "Key") == U1 + "/one.html" && S(notes[0], "Text") == "Meet at the north gate", "notes: kept per page (" + (notes.Count > 0 ? S(notes[0], "Key") : "none") + ")");
                var pn = J(Cmd("test-y-panel notes"));
                Ok(A(pn, "text").Cast<string>().Any(x => x == "[box]Meet at the north gate"), "notes: the panel shows this page's note");
                Cmd("test-activate " + Tabs().FindIndex(t => (string)t["url"] == U1 + "/two.html")); Thread.Sleep(1500);
                var pn2 = J(Cmd("test-y-panel notes"));
                Ok(A(pn2, "text").Cast<string>().Any(x => x == "[box]") && A(pn2, "text").Cast<string>().Any(x => x.StartsWith("ALL NOTES (1)")), "notes: another page has its own (empty) note; the list shows the one written");
                Cmd("test-y-panel hide");
                Ok(File.Exists(Path.Combine(run, "notes.json")) && File.Exists(Path.Combine(run, "reading-list.json")), "reading list and notes: kept in the profile's folder");
                var syncDir2 = Path.Combine(dir, "sync2"); Directory.CreateDirectory(syncDir2);
                Cmd("test-m-sync-set " + syncDir2 + " correct horse battery"); var so = Val(Cmd("test-m-sync-now", 90000));
                var syncBytes = File.Exists(Path.Combine(syncDir2, SyncCore.FileName)) ? File.ReadAllBytes(Path.Combine(syncDir2, SyncCore.FileName)) : new byte[0];
                Ok(SyncCore.IsEncrypted(syncBytes) && !Encoding.UTF8.GetString(syncBytes).Contains("north gate"), "notes: with sync on and a sync password, notes go to the folder encrypted (" + (so ?? "").Split(',').FirstOrDefault() + ")");
                Cmd("test-m-sync-set off");

                // ================= memory saver, battery saver, "memory saved"
                Cmd("test-y-set memory max"); var m1 = J(Cmd("test-y-mem"));
                Ok(I(m1, "minutes") == 2 && !B(m1, "battery"), "memory saver: Max = tabs sleep after 2 minutes");
                Cmd("test-y-set memory off"); Ok(I(J(Cmd("test-y-mem")), "minutes") == 30, "memory saver: Off = 30 minutes");
                Cmd("test-y-set memory balanced"); Ok(I(J(Cmd("test-y-mem")), "minutes") == 10, "memory saver: Balanced = 10 minutes");
                int bg = NewTab(U1 + "/two.html?bg"); Cmd("test-activate 0"); Thread.Sleep(700);
                var fast = Val(Cmd("test-y-throttle " + bg));
                File.WriteAllText(battery, "battery"); Cmd("test-y-set battery 1"); Thread.Sleep(1200);
                var m2 = J(Cmd("test-y-mem"));
                Ok(B(m2, "battery") && B(m2, "batteryMode") && I(m2, "minutes") == 5 && I(m2, "slowed") >= 1, "battery saver: on battery tabs sleep twice as soon (5 min) and background tabs are slowed (" + I(m2, "slowed") + ")");
                var slow = Val(Cmd("test-y-throttle " + bg)); long lf = 0, ls = 0; long.TryParse(fast, out lf); long.TryParse(slow, out ls);
                Ok(lf > 0 && ls > 0 && ls < lf * 0.6, "battery saver: a background tab's scripts run slower on battery (" + ls + " vs " + lf + " loops in 200 ms)");
                File.WriteAllText(battery, "ac"); Cmd("test-y-set battery 1"); Thread.Sleep(1200);
                var m3 = J(Cmd("test-y-mem")); var back = Val(Cmd("test-y-throttle " + bg)); long lb = 0; long.TryParse(back, out lb);
                Ok(!B(m3, "batteryMode") && I(m3, "slowed") == 0 && lb > ls, "battery saver: back on mains, full speed again (" + lb + ")");
                var saved = Val(Cmd("test-y-sleep " + bg)); long sv = 0; long.TryParse(saved, out sv);
                var tip = S(((System.Collections.ArrayList)J(Cmd("test-y-mem"))["tabs"])[bg] as Dictionary<string, object>, "tip");
                Ok(sv >= 1 && tip != null && tip.Contains("saved"), "memory saved: a sleeping tab shows a leaf with about how much it gave back: \"" + tip + "\"");
                Cmd("test-activate " + bg); Thread.Sleep(500);

                // ================= sounds (played at volume 0 in tests)
                Cmd("test-y-set sounds 1"); Cmd("test-y-set volume 0");
                var v0 = J(Cmd("test-y-vibe")); NewTab(U1 + "/one.html?sound"); Thread.Sleep(1200); var v1 = J(Cmd("test-y-vibe"));
                Ok(I(v1, "played") > I(v0, "played") && S(v1, "last") == "open" && I(v1, "opened") >= 1, "sounds: a new tab plays the (synthesised) open sound - the player opened it (" + I(v1, "opened") + ")");
                Cmd("test-y-set ambience 1"); Thread.Sleep(800); Ok(B(J(Cmd("test-y-vibe")), "ambience"), "ambience: the loop plays when switched on");
                File.WriteAllText(gameFile, "Fake Game"); var vg = Wait(() => J(Cmd("test-y-vibe")), d => S(d, "last") == "game", 12000);
                Ok(S(vg, "last") == "game" && !B(vg, "ambience"), "sounds: game mode on plays its cue once; the ambience pauses in the game");
                var pg = I(vg, "played"); NewTab(U1 + "/two.html?ingame"); Thread.Sleep(800); var vg2 = J(Cmd("test-y-vibe"));
                Ok(I(vg2, "played") == pg && I(vg2, "skipped") > I(vg, "skipped"), "sounds: muted during the game (a new tab makes no sound)");
                File.WriteAllText(gameFile, ""); Ok(B(Wait(() => J(Cmd("test-y-vibe")), d => B(d, "ambience"), 12000), "ambience"), "ambience: back after the game");
                Cmd("test-y-set ambience 0"); Cmd("test-y-set sounds 0");
                Ok(!B(J(Cmd("test-y-vibe")), "ambience"), "sounds and ambience off again");

                // ================= startup boost (a test profile)
                Cmd("test-y-set boost 1"); Cmd("test-v-savenow"); Thread.Sleep(500);
                int before = Tabs().Count;
                Cmd("test-y-exit"); Thread.Sleep(5000);
                var bst = J(Cmd("test-y-boost"));
                Ok(!br.HasExited && B(bst, "boosted") && I(bst, "windows") == 0, "startup boost: after Exit the process stays, hidden, with no window");
                Note("startup boost: memory while closed: " + I(bst, "ws") + " MB in use (working set), " + I(bst, "mb") + " MB committed (private bytes, incl. any engine process still there); engine kept: " + B(bst, "env"));
                var st1 = Val(Cmd("status")); Ok(st1 != null && st1.Contains("\"boosted\":true"), "startup boost: status says boosted");
                Thread.Sleep(3000); var bst2 = J(Cmd("test-y-boost")); Note("startup boost: after trimming: " + I(bst2, "ws") + " MB in use, " + I(bst2, "mb") + " MB committed");
                var sw = Stopwatch.StartNew(); var again = launch(null); again.WaitForExit(20000);
                var wtabs = Wait(Tabs, t => t.Count >= before - 1 && t.Count > 0 && (bool)t[0]["ready"], 30000); var wake = D(J(Cmd("test-y-boost")), "wake");
                Ok(again.ExitCode == 0 && wtabs.Count > 0 && !B(J(Cmd("test-y-boost")), "boosted"), "startup boost: starting the browser again opens a window in the boosted process (" + wtabs.Count + " tabs back)");
                Ok(wake > 0 && wake < 600, "startup boost: the window is drawn " + wake.ToString("0") + " ms after the new start's request");
                Cmd("test-y-set boost 0");

                // ================= after a restart: the dictionary word is accepted
                Cmd("quit"); br.WaitForExit(20000); Thread.Sleep(2500);
                Ok(br.HasExited, "quit really exits (boost off)");
                br = launch(null); Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000); Wait(Tabs, t => t.Count > 0 && (bool)t[0]["ready"], 30000); Thread.Sleep(1500);
                int sp2 = NewTab(U1 + "/spell.html?again"); Thread.Sleep(600);
                Cmd("test-x-cdp " + sp2 + " Input.insertText {\"text\":\" \"}"); Thread.Sleep(2500);
                var p1 = WordAt(sp2, "wrods"); Mouse("test-x-cdp " + sp2, p1[0], p1[1], "right"); Thread.Sleep(900);
                var m4 = A(J(Cmd("test-y-menu")), "items").Cast<string>().ToList();
                var p2 = WordAt(sp2, "sentance"); Mouse("test-x-cdp " + sp2, p2[0], p2[1], "right"); Thread.Sleep(900);
                var m5 = A(J(Cmd("test-y-menu")), "items").Cast<string>().ToList();
                Note("spell check after the restart: menu on wrods [" + string.Join(",", m4) + "], on sentance [" + string.Join(",", m5) + "], dictionary file: " + (S(J(Cmd("test-y-spell")), "file") ?? "").Replace("\n", "|"));
                Ok(!m4.Contains("spellcheck") && m5.Contains("spellcheck"), "spell check: after the restart \"wrods\" is accepted (Add to dictionary worked) while \"sentance\" is still flagged");
                Ok(File.ReadAllText(Path.Combine(run, "profiles.json")).Contains("gaming") && S(J(Cmd("test-y-info")), "chip") == "Default", "profiles: still two after the restart");
                var status = Val(Cmd("status")); Ok(status != null && status.Contains("\"version\":\"1.6\"") && status.Contains("\"profiles\":2") && status.Contains("\"memorySaver\":\"balanced\"") && !status.Contains("127.0.0.1"), "status: 1.6 fields, no addresses");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (Family.Running("Browser-gaming")) CmdTo("Browser-gaming", "quit", 3000); } catch { }
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var id in Mine(exe)) try { using (var p = Process.GetProcessById(id)) if (p.StartTime >= t0) p.Kill(); } catch { }   // only this test's own processes of the repo's exe
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v16: ALL " + pass + " PASSED" : "INTEGRATION v16: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
