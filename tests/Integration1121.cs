// Hallow Browser 1.12.1 - integration test "v1121", off-screen like the others, its own test folder, a LOCAL fake Twitch only.
// The Twitch player showed as a plain white box in real use. Fixture pages use the real player's markup as seen on twitch.tv in Nov 2026
// (anonymous: no channel, no ids - class names and data attributes only): video-player > video-player__container > the stream display
// ad wrapper (squeezeback) and video-ref > video + overlay + controls. On them:
//  - an empty ad frame (an ad-named box with an iframe that has no address) and an empty white box laid over the video are hidden;
//  - Twitch's own overlays stay: an extension iframe, a mature-content gate with text and a button, the controls; chat stays;
//  - a hide rule that matches a box around the video (aria-label="Advertisement") doesn't hide the stream, and a display-ad box beside it
//    is still hidden; an ad-named box around the video that gets hidden is shown again (the guard);
//  - a video that stops moving while it should play is nudged (at most 3 times); one you paused is left alone; a plain page gets no
//    false alarms;
//  - the memory target: with a game running only the tabs nobody sees are "low"; the one you look at is normal, at once after a switch.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v1121
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
    public static class Integration1121
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-v-weval 0 " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); return i; }
        static Dictionary<string, object> Watch(int tab) { var s = Eval(tab, "JSON.stringify(window.__hallowTwitch&&window.__hallowTwitch.watch||null)"); try { return s == null || s == "null" ? null : js.Deserialize<Dictionary<string, object>>(s); } catch { return null; } }
        static int N(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -1; }
        static string Disp(int tab, string sel) { return Eval(tab, "(function(){var e=document.querySelector('" + sel + "');return e?getComputedStyle(e).display:'missing'})()"); }
        static Tuple<int, string, byte[]> T(string type, string text) { return Tuple.Create(200, type, Encoding.UTF8.GetBytes(text)); }

        // the player, as twitch.tv draws it (anonymous)
        public static string Player(string ch)
        {
            bool wrapped = ch == "wrapped";
            var sb = new StringBuilder("<!DOCTYPE html><html><head><title>" + ch + " - Twitch</title></head><body style='margin:0;background:#0e0e10;color:#efeff1;font:14px sans-serif'>");
            sb.Append("<div class='persistent-player' style='position:relative;width:640px;height:360px'>");
            sb.Append("<div data-a-target='video-player' data-a-player-state='playing' class='video-player' style='position:relative;width:640px;height:360px;background:#000'>");
            sb.Append("<div class='video-player__container' data-test-selector='video-player__video-container' style='position:absolute;inset:0;background:#000'>");
            sb.Append("<div class='stream-display-ad__wrapper stream-display-ad__wrapper_squeezeback stream-display-ad__wrapper-hidden' data-test-selector='sda-wrapper' style='position:absolute;inset:0'>" +
                "<div class='stream-display-ad__container_squeezeback' data-test-selector='sda-container' style='position:absolute;inset:0;background:#000'><div class='squeezeback-accessibility-frame'></div>" +
                "<div class='stream-display-ad__transform-container_squeezeback' data-test-selector='sda-transform'><div class='stream-display-ad__frame_squeezeback' data-test-selector='sda-frame'></div></div></div></div>");
            sb.Append("<div class='video-ref' data-a-target='video-ref'" + (wrapped ? " aria-label='Advertisement'" : "") + " style='position:absolute;inset:0'>");
            sb.Append("<video id=v aria-label='Twitch video player' style='position:absolute;inset:0;width:100%;height:100%' loop></video>");
            sb.Append("<div class='video-player__overlay' style='position:absolute;inset:0'><div data-a-target='ax-overlay' style='position:absolute;inset:0'></div>" +
                "<div data-a-target='player-overlay-click-handler' class='click-handler' style='position:absolute;inset:0'></div>" +
                "<div class='player-controls' data-a-target='player-controls' style='position:absolute;left:0;right:0;bottom:0;height:40px'><button data-a-target='player-play-pause-button'>Pause</button></div></div>");
            sb.Append("</div></div></div></div>");
            sb.Append("<div id=sdabox class='stream-display-ad__wrapper' style='width:300px;height:50px;background:#333'>display ad</div>");
            sb.Append("<div id=chat class='chat-shell' style='width:300px;height:80px'><span>chat</span></div>");
            // a silent minute of sound as the stream (moves currentTime like a live video does)
            sb.Append("<script>(function(){var rate=8000,sec=60,n=rate*sec,buf=new ArrayBuffer(44+n),d=new DataView(buf);function s(o,t){for(var i=0;i<t.length;i++)d.setUint8(o+i,t.charCodeAt(i));}" +
                "s(0,'RIFF');d.setUint32(4,36+n,true);s(8,'WAVEfmt ');d.setUint32(16,16,true);d.setUint16(20,1,true);d.setUint16(22,1,true);d.setUint32(24,rate,true);d.setUint32(28,rate,true);d.setUint16(32,1,true);d.setUint16(34,8,true);s(36,'data');d.setUint32(40,n,true);" +
                "new Uint8Array(buf,44).fill(128);var v=document.getElementById('v');v.src=URL.createObjectURL(new Blob([buf],{type:'audio/wav'}));v.play().catch(function(){});})();");
            sb.Append("var C=document.querySelector('.video-player__container');function add(h){var t=document.createElement('div');t.innerHTML=h;var e=t.firstChild;C.appendChild(e);return e;}");
            switch (ch)
            {
                case "whiteframe":   // a picture-by-picture style ad frame whose ad never loaded: a white box with an empty iframe, over the video
                    sb.Append("setTimeout(function(){add(\"<div id=layer1 class='pbyp-layer' data-test-selector='pbyp-layer' style='position:absolute;inset:0;z-index:3;background:#fff'><iframe id=layer1f style='width:100%;height:100%;border:0;background:#fff'></iframe></div>\");window.adAt=Date.now();},1500);");
                    break;
                case "emptywhite":   // an empty white box with no ad name at all
                    sb.Append("setTimeout(function(){add(\"<div id=white style='position:absolute;inset:0;z-index:3;background:rgb(255,255,255)'></div>\");},1500);");
                    break;
                case "keep":         // Twitch's own overlays: an extension iframe (transparent) and a mature-content gate with text and a button
                    sb.Append("setTimeout(function(){add(\"<iframe id=ext class='extension-view__iframe' src='/ext/supervisor.html' style='position:absolute;inset:0;width:100%;height:100%;border:0;z-index:2'></iframe>\");" +
                        "add(\"<div id=gate data-a-target='content-classification-gate-overlay' style='position:absolute;inset:0;z-index:4;background:#18181b'><p>The broadcaster indicated that the channel is intended for mature audiences.</p><button>Start watching</button></div>\");},1500);");
                    break;
                case "guarded":      // a display-ad layout box around the video that gets hidden (as an ad filter would)
                    sb.Append("setTimeout(function(){var r=document.querySelector('.video-ref');r.classList.add('StreamDisplayAdLayout');r.style.display='none';},1500);");
                    break;
                case "stall":        // the video stops moving while it should play
                    sb.Append("setTimeout(function(){document.getElementById('v').playbackRate=0;window.stallAt=Date.now();},2000);");
                    break;
                case "paused":       // you paused it
                    sb.Append("setTimeout(function(){document.getElementById('v').pause();},2000);");
                    break;
            }
            sb.Append("</script></body></html>");
            return sb.ToString();
        }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v1121.txt"), false) { AutoFlush = true };
            var game = Path.Combine(run, "game.txt"); File.WriteAllText(game, "");
            var srv = new Integration110.Server(); string U = null;
            srv.Route = rq =>
            {
                var pure = rq.Path.Split('?')[0];
                if (pure == "/ext/supervisor.html") return T("text/html; charset=utf-8", "<!DOCTYPE html><title>ext</title><body style='background:transparent'></body>");
                if (pure.StartsWith("/tw/")) return T("text/html; charset=utf-8", Player(pure.Substring(4)));
                return null;
            };
            srv.Start(); U = "http://127.0.0.1:" + srv.Port;
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_TWITCH_BASE"] = U + "/tw";
                psi.EnvironmentVariables["HALLOW_BROWSER_TWITCH_API"] = U;
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = game;
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                var st = Val(Cmd("test-tw-state 0 0")) ?? "";
                Ok(st.Contains("\"version\":3") || st.Contains("\"version\":4"), "twitch: the built-in page script is version 3 or newer (" + st.Length + ")");

                // ---- an empty ad frame over the video: hidden within a few seconds; the video is on top again
                int wf = NewTab(U + "/tw/whiteframe");
                var w1 = Wait(() => Watch(wf), x => N(x, "unblocks") >= 1, 9000);
                if (N(w1, "unblocks") < 1) Console.WriteLine("debug: " + Eval(wf, "(function(){var v=document.getElementById('v'),r=v.getBoundingClientRect(),s=document.elementsFromPoint(r.left+r.width/2,r.top+r.height/2);return JSON.stringify({r:[r.left,r.top,r.width,r.height],s:s.slice(0,6).map(function(e){return e.tagName+'#'+e.id+'.'+e.className}),w:window.__hallowTwitch.watch,ev:window.__hallowTwitch.events,f:(function(){var a=document.getElementById('layer1');if(!a)return 'none';var cs=getComputedStyle(a);return [cs.display,cs.visibility,a.getAttribute('style'),a.outerHTML.slice(0,300),document.getElementById('hallow-twitch-css').textContent.slice(0,200)].join(' | ')})()})})()"));
                Ok(N(w1, "unblocks") >= 1 && Disp(wf, "#layer1") == "none", "white frame: the empty ad frame over the player is hidden (" + N(w1, "unblocks") + ", " + Eval(wf, "String(Date.now()-window.adAt)") + " ms after it came)");
                Ok(Eval(wf, "(function(){var v=document.getElementById('v'),r=v.getBoundingClientRect(),s=document.elementsFromPoint(r.left+r.width/2,r.top+r.height/2);for(var i=0;i<s.length;i++){if(s[i]===v)return 'video';if(s[i].id==='layer1'||s[i].id==='layer1f')return 'frame';}return 'none'})()") == "video", "white frame: what is at the player's centre now leads to the video, not the frame");
                Ok(Disp(wf, "#chat") != "none" && Disp(wf, ".player-controls") != "none" && Disp(wf, "#sdabox") == "none", "white frame: chat and the controls stay, the display-ad box beside the player stays hidden");
                Ok((Eval(wf, "JSON.stringify(window.__hallowTwitch.events)") ?? "").Contains("unblock"), "white frame: logged in the page's events");

                int ew = NewTab(U + "/tw/emptywhite");
                var w2 = Wait(() => Watch(ew), x => N(x, "unblocks") >= 1, 9000);
                Ok(N(w2, "unblocks") >= 1 && Disp(ew, "#white") == "none", "white frame: an empty opaque white box over the video (no ad name) is hidden too");

                // ---- Twitch's own overlays stay
                int kp = NewTab(U + "/tw/keep");
                Thread.Sleep(6000);
                var w3 = Watch(kp);
                Ok(N(w3, "unblocks") == 0 && Disp(kp, "#gate") != "none" && Disp(kp, "#ext") != "none", "overlays: the mature-content gate (text, a button) and an extension iframe are left alone (" + N(w3, "unblocks") + " hidden)");

                // ---- a hide rule matching a box around the video
                int wr = NewTab(U + "/tw/wrapped");
                Thread.Sleep(3000);
                Ok(Eval(wr, "(function(){for(var p=document.getElementById('v');p;p=p.parentElement){if(getComputedStyle(p).display==='none')return 'hidden';}return 'shown'})()") == "shown", "hide rules: [aria-label=Advertisement] on a box around the video doesn't hide the stream");
                Ok(Disp(wr, "#sdabox") == "none" && Disp(wr, "[data-test-selector=sda-wrapper]") == "none", "hide rules: display-ad boxes without the video are still hidden");
                int gd = NewTab(U + "/tw/guarded");
                var w4 = Wait(() => Watch(gd), x => N(x, "guards") >= 1, 8000);
                Ok(N(w4, "guards") >= 1 && Disp(gd, ".video-ref") != "none", "guard: an ad-named box around the video that gets hidden is shown again (" + N(w4, "guards") + ")");

                // ---- the stuck-video watchdog
                int sl = NewTab(U + "/tw/stall");
                var w5 = Wait(() => Watch(sl), x => N(x, "stalls") >= 1, 12000);
                Ok(N(w5, "stalls") >= 1 && (Eval(sl, "JSON.stringify(window.__hallowTwitch.events)") ?? "").Contains("resync"), "watchdog: a video stuck for ~3.5 s while it should play is nudged (" + Eval(sl, "String(Date.now()-window.stallAt)") + " ms after it stopped)");
                Thread.Sleep(14000);
                var w6 = Watch(sl);
                Ok(N(w6, "stalls") >= 2 && N(w6, "stalls") <= 3, "watchdog: then the player is reloaded, at most 3 tries while it stays stuck (" + N(w6, "stalls") + ")");
                int ps = NewTab(U + "/tw/paused");
                Thread.Sleep(8000);
                Ok(N(Watch(ps), "stalls") == 0, "watchdog: a video you paused is left alone");
                int pl = NewTab(U + "/tw/plainplayer");
                Thread.Sleep(7000);
                var w7 = Watch(pl);
                Ok(N(w7, "stalls") == 0 && N(w7, "unblocks") == 0 && N(w7, "guards") == 0, "watchdog: a normal player gets no false alarms");

                // ---- memory target: only the tabs nobody sees are low while a game runs
                File.WriteAllText(game, "Test Game"); Cmd("test-tick");
                var m1 = Wait(() => Val(Cmd("test-mm-mem")) ?? "", x => x.Contains("\"game\":\"Test Game\""), 8000);
                var d1 = js.Deserialize<Dictionary<string, object>>(m1);
                var tabs = ((System.Collections.ArrayList)d1["tabs"]).Cast<Dictionary<string, object>>().ToList();
                Ok(tabs.Count >= 3 && tabs.All(t => (bool)t["low"] == !(bool)t["active"]), "memory: with a game running the shown tab keeps the normal memory target, the others are low (" + m1 + ")");
                Cmd("test-activate 1");
                var m2 = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-mm-mem")) ?? "{}");
                var tabs2 = ((System.Collections.ArrayList)m2["tabs"]).Cast<Dictionary<string, object>>().ToList();
                Ok((bool)tabs2[1]["active"] && !(bool)tabs2[1]["low"] && (bool)tabs2[tabs2.Count - 1]["low"], "memory: a tab that is switched to is back to normal at once, the one left goes low");
                File.WriteAllText(game, ""); Cmd("test-tick");
                var m3 = Wait(() => Val(Cmd("test-mm-mem")) ?? "", x => x.Contains("\"game\":null"), 8000);
                Ok(!m3.Contains("\"low\":true"), "memory: after the game every tab is back to normal");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v1121: ALL " + pass + " PASSED" : "INTEGRATION v1121: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
