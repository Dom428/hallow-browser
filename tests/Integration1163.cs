// Hallow Browser 1.16.3 - integration test "v1163", off-screen like the others (muted: test browsers start with --mute-audio, and each
// tab is muted), its own test folder, a LOCAL fake Twitch page only (127.0.0.1 / localhost; the real Twitch is never contacted).
// Real use: a white player over a healthy video - a channel's video-overlay extension frame covered it. The fake channel page has a
// playing video (a canvas stream with a moving picture), an ext-twitch.tv-style overlay frame over it, Twitch-like controls, a panel
// extension below the player and a chat box:
//  - before the fix (Twitch script off, extensions shown): the overlay frame that failed to load (an error page) and the dark-mode overlay frame turn the player
//    white / blank on screen;
//  - "Show channel extensions over the video" off (the default): overlay frames hidden, the video shows; the panel extension, the
//    controls and chat are untouched;
//  - on: overlays show; a blocked one (load error seen by the browser) is hidden; one drawn as a white sheet over the playing video is
//    found by the cover check (a picture of the player area) and hidden; a healthy transparent one stays;
//  - an ad-host frame over the video (blocked by Hallow) is hidden through the watchdog's unblock path, also with the switch off;
//  - Report a Twitch problem has the switch, the frame load errors and the extension frames.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v1163
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration1163
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-v-weval 0 " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Cmd("test-mute " + i); Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Cmd("test-mute " + i); return i; }
        static Tuple<int, string, byte[]> T(string type, string text, int code = 200) { return Tuple.Create(code, type, Encoding.UTF8.GetBytes(text)); }
        static Dictionary<string, object> St(int tab) { var v = Val(Cmd("test-tw63-state " + tab)); try { return js.Deserialize<Dictionary<string, object>>(v); } catch { return new Dictionary<string, object>(); } }
        static string Dec(int tab) { return Eval(tab, "JSON.stringify(window.__hallowTwitch&&window.__hallowTwitch.decisions||[])") ?? ""; }
        static bool Playing(int tab) { return Wait(() => Eval(tab, "(function(){var v=document.getElementById('v');return String(!!v&&v.readyState>=3&&!v.paused&&v.currentTime>0.5)})()"), x => x == "true", 15000) == "true"; }
        static string Shown(int tab, string sel) { return Eval(tab, "(function(){var e=document.querySelector(\"" + sel + "\");if(!e)return 'missing';var c=getComputedStyle(e);return c.display!=='none'&&c.visibility!=='hidden'?'shown':'hidden'})()"); }
        // what the player area looks like on screen (a picture of the tab, measured like the browser's own cover check)
        static Dictionary<string, object> Screen(int tab, string dir, string name)
        {
            Cmd("test-activate " + tab); Thread.Sleep(600);
            var png = Path.Combine(dir, name + ".png"); Cmd("test-capture " + tab + " " + png);
            var vw = Eval(tab, "String(innerWidth)"); var r = Eval(tab, "(function(){var r=document.getElementById('v').getBoundingClientRect();return [r.left,r.top,r.width,r.height].join(',')})()");
            try { using (var b = new Bitmap(png)) return TwitchCover.Measure(b, r.Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(), double.Parse(vw, System.Globalization.CultureInfo.InvariantCulture)); }
            catch (Exception e) { return new Dictionary<string, object> { { "error", e.Message }, { "white", 0.0 }, { "flat", 0.0 } }; }
        }
        static double W(Dictionary<string, object> m) { return Convert.ToDouble(m["white"]); }
        static double F(Dictionary<string, object> m) { return Convert.ToDouble(m["flat"]); }
        static bool Blank(Dictionary<string, object> m) { return W(m) >= 0.85 || F(m) >= 0.93; }
        static string S(Dictionary<string, object> m) { return js.Serialize(m); }

        // a Twitch-like channel page: the overlay frame (src) over a playing video, controls, a panel extension below, chat on the side
        static string Page(string U, string L, string ch)
        {
            string ov;
            switch (ch)
            {
                case "blocked": ov = U + "/h/abc123.ext-twitch.tv/overlay.html"; break;          // the extension's frame fails to load (its error page)
                case "darkext": ov = L + "/ext/white.html"; break;                               // drawn as a plain white sheet (as Chromium's dark mode drew the real one)
                case "goodext": ov = L + "/ext/clear.html"; break;                               // a healthy transparent overlay with one small button
                default: ov = null; break;
            }
            var adframe = ch == "adframe" ? "<iframe id=adf src='" + U + "/h/s.amazon-adsystem.com/ad.html' style='position:absolute;left:0;top:0;width:640px;height:360px;border:0'></iframe>" : "";
            return "<!DOCTYPE html><html><head><title>" + ch + " - Twitch</title></head><body style='margin:0;background:#18181b;color:#eee;font:14px sans-serif'>" +
                "<div style='display:flex'><div>" +
                "<div data-a-target='video-player' class='video-player' style='width:640px;height:360px;position:relative'>" +
                "<div data-a-target='video-ref'><video id=v width=640 height=360 muted playsinline style='position:absolute;left:0;top:0;width:640px;height:360px;background:#000'></video></div>" +
                "<div class='video-player__overlay' style='position:absolute;left:0;top:0;right:0;bottom:0'>" +
                (ov != null ? "<div class='extensions-video-overlay-size-container' style='position:absolute;left:0;top:0;right:0;bottom:0'><iframe class='extension-view__iframe' src='" + ov + "' style='position:absolute;left:0;top:0;width:100%;height:100%;border:0;background:transparent'></iframe></div>" : "") +
                adframe +
                "<div data-a-target='player-controls' class='player-controls' style='position:absolute;left:0;right:0;bottom:0;height:36px;background:rgba(0,0,0,.4)'><button data-a-target='player-play-pause-button'>Pause</button></div>" +
                "</div></div>" +
                "<div class='extension-panel' style='width:318px;height:150px;margin-top:8px'><iframe class='extension-panel-extension-frame' src='" + L + "/ext/panel.html' style='width:318px;height:150px;border:0'></iframe></div>" +
                "</div><div class='chat-shell' style='width:300px;height:500px;background:#0e0e10;padding:8px'>Chat: hello</div></div>" +
                "<script>(function(){var c=document.createElement('canvas');c.width=320;c.height=180;var x=c.getContext('2d'),f=0;" +
                "function draw(){f++;for(var i=0;i<8;i++){x.fillStyle='hsl('+((i*45+f*3)%360)+',70%,'+(30+i*5)+'%)';x.fillRect(i*40,0,40,180);}x.fillStyle='#111';x.fillRect((f*4)%320,60,40,60);}" +
                "draw();setInterval(draw,50);var v=document.getElementById('v');v.srcObject=c.captureStream(20);v.play().catch(function(){});})();</script></body></html>";
        }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            var shots = Path.Combine(dir, "shots"); Directory.CreateDirectory(shots);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v1163.txt"), false) { AutoFlush = true };
            var srv = new Integration110.Server(); string U = null, L = null;
            srv.Route = rq =>
            {
                var pure = rq.Path.Split('?')[0];
                if (pure.StartsWith("/tw/")) return T("text/html; charset=utf-8", Page(U, L, pure.Substring(4)));
                if (pure == "/h/abc123.ext-twitch.tv/overlay.html") throw new InvalidOperationException("dropped");   // no answer: the frame shows the browser's error page
                if (pure == "/ext/white.html") return T("text/html; charset=utf-8", "<!DOCTYPE html><html><body style='margin:0;background:#fff'><button style='position:absolute;right:8px;bottom:44px'>Prime Benefits</button></body></html>");
                if (pure == "/ext/clear.html") return T("text/html; charset=utf-8", "<!DOCTYPE html><html><body style='margin:0;background:transparent'><button style='position:absolute;right:8px;top:8px'>Poll</button></body></html>");
                if (pure == "/ext/panel.html") return T("text/html; charset=utf-8", "<!DOCTYPE html><html><body style='margin:0;background:#fff;color:#000'>Panel extension</body></html>");
                return T("text/plain", "not found", 404);
            };
            srv.Start(); U = "http://127.0.0.1:" + srv.Port; L = "http://localhost:" + srv.Port;
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_TWITCH_BASE"] = U + "/tw";
                psi.EnvironmentVariables["HALLOW_BROWSER_TWITCH_API"] = U;
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                Cmd("test-mute 0");
                Ok((Val(Cmd("test-tw-state 0 0")) ?? "").Contains("\"version\":6"), "twitch: the built-in page script is version 6");
                Ok((Val(Cmd("test-tw63-state 0")) ?? "").Contains("\"showExt\":false"), "\"Show channel extensions over the video\" is off by default");

                // ================= before the fix: no Twitch script, extensions shown -> the player is white / blank on screen
                Cmd("test-tw-set on 0"); Cmd("test-tw63-showext 1"); Thread.Sleep(500);
                int b1 = NewTab(U + "/tw/blocked"); bool pl1 = Playing(b1); Thread.Sleep(1500);
                var sb1 = Screen(b1, shots, "before-blocked");
                Ok(pl1 && Eval(b1, "String(!window.__hallowTwitch)") == "true" && Blank(sb1), "before: an overlay frame that failed to load (its error page) covers a playing video - blank on screen " + S(sb1));
                Cmd("test-close " + b1); Thread.Sleep(400);
                int b2 = NewTab(U + "/tw/darkext"); bool pl2 = Playing(b2); Thread.Sleep(1500);
                var sb2 = Screen(b2, shots, "before-darkext");
                Ok(pl2 && W(sb2) >= 0.85, "before: a white-drawn overlay frame covers a playing video - white on screen " + S(sb2));
                Cmd("test-close " + b2); Thread.Sleep(400);

                // ================= the default: switch off -> overlays hidden (ads blocked or not), panel / controls / chat untouched
                Cmd("test-tw63-showext 0"); Thread.Sleep(500);   // Twitch ads still off: the script comes for the extensions alone
                int d1 = NewTab(U + "/tw/blocked"); Playing(d1); Thread.Sleep(2500);
                var sd1 = Screen(d1, shots, "off-noads-blocked");
                Ok(Eval(d1, "String(!!window.__hallowTwitch&&window.__hallowTwitch.ext().show===false)") == "true" && Shown(d1, ".extension-view__iframe") == "hidden" && !Blank(sd1),
                    "off (Twitch ads not blocked): the script still comes, the overlay frame is hidden, the video shows " + S(sd1));
                Cmd("test-close " + d1); Thread.Sleep(400);
                Cmd("test-tw-set on 1"); Thread.Sleep(500);
                int d2 = NewTab(U + "/tw/darkext"); Playing(d2); Thread.Sleep(2500);
                var sd2 = Screen(d2, shots, "off-darkext");
                Ok(Shown(d2, ".extension-view__iframe") == "hidden" && !Blank(sd2), "off: the white overlay frame is hidden at once, the video shows " + S(sd2));
                Ok(Shown(d2, ".extension-panel-extension-frame") == "shown" && Shown(d2, "[data-a-target=player-controls]") == "shown" && Shown(d2, ".chat-shell") == "shown" && Shown(d2, "#v") == "shown",
                    "off: the panel extension below the player, the controls, chat and the video are untouched");
                Ok(Wait(() => St(d2), s => s.ContainsKey("extHidden") && Convert.ToInt32(s["extHidden"]) >= 1, 6000).ContainsKey("extHidden") && Dec(d2).Contains("channel extensions over the video are hidden"), "off: the browser and the decisions know overlays were hidden");
                Cmd("test-close " + d2); Thread.Sleep(400);

                // ================= on: a blocked overlay (load error) is hidden; a white one is found by the cover check; a healthy one stays
                Cmd("test-tw63-showext 1"); Thread.Sleep(500);
                int o1 = NewTab(U + "/tw/blocked"); Playing(o1);
                var hid1 = Wait(() => Shown(o1, ".extension-view__iframe"), x => x == "hidden", 15000);
                var st1 = St(o1); var fe = js.Serialize(st1.ContainsKey("frameErrors") ? st1["frameErrors"] : null);
                Ok(fe.Contains("abc123.ext-twitch.tv"), "on: the browser saw the overlay frame fail to load: " + fe);
                Thread.Sleep(800); var so1 = Screen(o1, shots, "on-blocked");
                Ok(hid1 == "hidden" && !Blank(so1) && Eval(o1, "document.querySelector('.extension-view__iframe').getAttribute('data-hallow-ext-hidden')").Length > 0, "on: the failed overlay frame is hidden, the video shows " + S(so1) + " " + Eval(o1, "document.querySelector('.extension-view__iframe').getAttribute('data-hallow-ext-hidden')"));
                Ok(Shown(o1, ".extension-panel-extension-frame") == "shown", "on: the panel extension stays");
                var rp = Val(Cmd("test-twr-report " + o1 + " " + Path.Combine(dir, "reports"))); string zp = null; try { zp = js.Deserialize<string>(rp); } catch { }
                string json = ""; if (zp != null && File.Exists(zp)) using (var z = ZipFile.OpenRead(zp)) { var e1 = z.GetEntry("report.json"); if (e1 != null) json = new StreamReader(e1.Open()).ReadToEnd(); }
                Ok(json.Contains("\"showChannelExtensions\":true") && json.Contains("\"frameLoadErrors\":[") && json.Contains("abc123.ext-twitch.tv") && json.Contains("\"extensionFrames\":[") && json.Contains("hallowExtHidden") && json.Contains("\"makePagesDark\":"),
                    "report: the switch, the frame load errors, the extension frames (hidden ones marked) and \"Make pages dark\" are in report.json");
                Cmd("test-close " + o1); Thread.Sleep(400);

                int o2 = NewTab(U + "/tw/darkext"); Playing(o2);
                var hid2 = Wait(() => Shown(o2, ".extension-view__iframe"), x => x == "hidden", 30000);
                var st2 = St(o2); Thread.Sleep(800); var so2 = Screen(o2, shots, "on-darkext");
                Ok(hid2 == "hidden" && !Blank(so2) && Convert.ToInt32(st2["checks"]) >= 2, "on: the white overlay over a playing video is found by the cover check and hidden (" + st2["checks"] + " checks, last " + js.Serialize(st2["last"]) + "), the video shows " + S(so2));
                Ok(Dec(o2).Contains("on screen the player was white while its video played"), "on: the decision says why it was hidden");
                Cmd("test-close " + o2); Thread.Sleep(400);

                int o3 = NewTab(U + "/tw/goodext"); Playing(o3); Thread.Sleep(14000);
                var st3 = St(o3);
                Ok(Shown(o3, ".extension-view__iframe") == "shown" && Convert.ToInt32(st3["checks"]) >= 1 && Convert.ToInt32(st3["covered"]) == 0, "on: a healthy transparent overlay stays (" + st3["checks"] + " checks, none blank)");
                Cmd("test-close " + o3); Thread.Sleep(400);

                // ================= an ad frame (blocked by Hallow) over a playing video: hidden through the watchdog's unblock path, switch off
                Cmd("test-tw63-showext 0"); Thread.Sleep(500);
                int a1 = NewTab(U + "/tw/adframe"); Playing(a1);
                var hidA = Wait(() => Eval(a1, "String(document.getElementById('adf').hasAttribute('data-hallow-unblocked')&&getComputedStyle(document.getElementById('adf')).display==='none')"), x => x == "true", 30000);
                var sa = St(a1); Thread.Sleep(800); var sa1 = Screen(a1, shots, "adframe");
                Ok(hidA == "true" && !Blank(sa1), "an ad-host frame blocked by Hallow over a playing video: hidden by the cover check (" + js.Serialize(sa["last"]) + "), the video shows " + S(sa1));
                Ok(Shown(a1, "[data-a-target=player-controls]") == "shown" && Shown(a1, ".chat-shell") == "shown", "the controls and chat are never hidden");
                Cmd("test-close " + a1); Thread.Sleep(400);
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v1163: ALL " + pass + " PASSED" : "INTEGRATION v1163: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
