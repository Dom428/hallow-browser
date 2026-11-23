// Hallow Browser 1.14.2 - integration test "v1142", off-screen like the others, its own test folder, a LOCAL fake Twitch only.
// Real use: "heard the ad, then a white screen" - an ad that was neither swapped nor covered, then a white player under Twitch's
// controls. Fixture pages use the real player's markup (Integration1121.Player) with a real moving picture (a canvas stream):
//  - an unswapped ad shown only by the player's ad label: covered and muted at once (page and tab), lifted after it;
//  - then the picture turns white: found by the canvas sample, recovered (nudge...), and nothing more once the picture is back;
//  - an ad shown only by Twitch's own ad event (gql impression), by a second video playing in an ad box, by a playlist read with
//    XMLHttpRequest: covered; on a channel you allow ads on: nothing;
//  - a white sheet with one button over the video: hidden; a plain player: no false alarms;
//  - "Report a Twitch problem": the zip (report.json + player.png), anonymised (no channel name, chat, user name or cookie), the menu
//    entry, the shortcut's toast.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v1142
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration1142
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
        static Tuple<int, string, byte[]> T(string type, string text) { return Tuple.Create(200, type, Encoding.UTF8.GetBytes(text)); }
        static string Ev(int tab) { return Eval(tab, "JSON.stringify(window.__hallowTwitch&&window.__hallowTwitch.events||[])") ?? ""; }
        static string Dec(int tab) { return Eval(tab, "JSON.stringify(window.__hallowTwitch&&window.__hallowTwitch.decisions||[])") ?? ""; }
        static bool Covered(int tab) { return Eval(tab, "String(!!(window.__hallowTwitch.cover()&&window.__hallowTwitch.cover().shown))") == "true"; }
        static Dictionary<string, object> State(int tab) { try { return js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-tw-state 0 " + tab)) ?? "{}"); } catch { return new Dictionary<string, object>(); } }

        // a moving picture as the stream: a canvas (colour bars that move) captured into the player's <video>; window.paint('white') /
        // paint('bars') switch it; after a recovery try (pause / play or a reload of the source) the fixture "recovers" when asked to
        const string Picture = "var cv=document.createElement('canvas');cv.width=320;cv.height=180;var cx=cv.getContext('2d'),mode='bars',f=0;window.paint=function(m){mode=m;};" +
            "setInterval(function(){f++;if(mode==='white'){cx.fillStyle='#fff';cx.fillRect(0,0,320,180);return;}for(var i=0;i<8;i++){cx.fillStyle='hsl('+((i*45+f*7)%360)+',80%,45%)';cx.fillRect(i*40,0,40,180);}cx.fillStyle='#000';cx.fillRect((f*9)%320,60,30,60);},100);" +
            "var v=document.getElementById('v');v.removeAttribute('loop');v.srcObject=cv.captureStream(10);v.play().catch(function(){});" +
            "window.fixOnTry=false;v.addEventListener('play',function(){if(window.fixOnTry){window.fixedAt=Date.now();paint('bars');}});";

        static string Page(string ch, string U)
        {
            var html = Integration1121.Player("plain");
            var extra = new StringBuilder("<script>" + Picture);
            extra.Append("document.cookie='secret_cookie=XYZ123SECRET';");
            extra.Append("var chat=document.getElementById('chat');chat.innerHTML='<span class=chat-author__display-name data-a-user=chatuser_secret>chatuser_secret</span>: hello';");
            switch (ch)
            {
                case "adwhite": case "allowme":   // an ad shown only by the player's label (no playlist marker); then the picture goes white
                    extra.Append("setTimeout(function(){var l=document.createElement('div');l.setAttribute('data-a-target','video-ad-label');l.textContent='Ad';var c=document.createElement('span');c.setAttribute('data-a-target','video-ad-countdown');c.textContent='0:05';" +
                        "var o=document.querySelector('.video-player__overlay');o.appendChild(l);o.appendChild(c);window.adAt=Date.now();" +
                        "setTimeout(function(){l.remove();c.remove();window.adEnd=Date.now();paint('white');window.whiteAt=Date.now();setTimeout(function(){window.fixOnTry=true;},300);},5000);},2000);");
                    break;
                case "gqlad":   // only Twitch's ad events: an impression (6 s), then the pod is complete
                    extra.Append("function ev(n,p){return fetch(location.origin+'/gql',{method:'POST',body:JSON.stringify([{operationName:'ClientSideAdEventHandling_RecordAdEvent',variables:{input:{eventName:n,eventPayload:JSON.stringify(p||{})}}}])});}" +
                        "setTimeout(function(){ev('video_ad_impression',{ad_duration:6});window.adAt=Date.now();setTimeout(function(){ev('video_ad_pod_complete');window.adEnd=Date.now();},3000);},2000);");
                    break;
                case "adbox":   // a second video playing with sound in a picture-by-picture box
                    extra.Append("setTimeout(function(){var b=document.createElement('div');b.className='pbyp-player-instance';b.style.cssText='position:absolute;right:0;bottom:0;width:160px;height:90px';var x=document.createElement('video');x.id='adv';" +
                        "var c2=document.createElement('canvas');c2.width=16;c2.height=9;var g=c2.getContext('2d');setInterval(function(){g.fillStyle='hsl('+(Date.now()/10%360)+',70%,50%)';g.fillRect(0,0,16,9);},100);x.srcObject=c2.captureStream(10);x.muted=false;b.appendChild(x);document.body.appendChild(b);x.play().catch(function(){});" +
                        "window.adAt=Date.now();setTimeout(function(){x.pause();b.remove();window.adEnd=Date.now();},4000);},2000);");
                    break;
                case "xhrad":   // a playlist read with XMLHttpRequest carries an ad
                    extra.Append("setTimeout(function(){var x=new XMLHttpRequest();x.open('GET','/pl/ad.m3u8');x.send();window.adAt=Date.now();},2000);");
                    break;
                case "whitesheet":   // a white sheet with one button over the video
                    extra.Append("setTimeout(function(){var s=document.createElement('div');s.id='sheet';s.style.cssText='position:absolute;inset:0;z-index:5;background:#fff';s.innerHTML='<button>Prime Benefits</button>';document.querySelector('.video-player__container').appendChild(s);},1500);");
                    break;
            }
            extra.Append("</script>");
            return html.Replace("</body>", extra + "</body>").Replace("<title>plain - Twitch", "<title>" + ch + " - Twitch");
        }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v1142.txt"), false) { AutoFlush = true };
            var srv = new Integration110.Server(); string U = null;
            srv.Route = rq =>
            {
                var pure = rq.Path.Split('?')[0];
                if (pure == "/pl/ad.m3u8") return T("application/vnd.apple.mpegurl", "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-DATERANGE:ID=\"stitched-ad-1\",CLASS=\"twitch-stitched-ad\",START-DATE=\"2026-11-22T00:00:00Z\",DURATION=6.0\n#EXTINF:2.000,Amazon|1\n" + U + "/seg/a1.ts\n#EXTINF:2.000,Amazon|1\n" + U + "/seg/a2.ts\n");
                if (pure.StartsWith("/tw/")) return T("text/html; charset=utf-8", Page(pure.Substring(4), U));
                return null;
            };
            srv.Start(); U = "http://127.0.0.1:" + srv.Port;
            var t0 = DateTime.Now; Process br = null;
            var outDir = Path.Combine(dir, "reports");
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
                Ok((Val(Cmd("test-tw-state 0 0")) ?? "").Contains("\"version\":4"), "twitch: the built-in page script is version 4");

                // ---- an unswapped ad (only the label says so): covered + muted at once, lifted after; then the white picture is recovered
                int aw = NewTab(U + "/tw/adwhite");
                Ok(Wait(() => Eval(aw, "String(document.getElementById('v').videoWidth)"), x => x == "320", 6000) == "320", "fixture: the player shows a moving picture (a 320 px canvas stream)");
                var cov = Wait(() => Covered(aw), x => x, 8000);
                Ok(cov && Eval(aw, "String(document.getElementById('v').muted)") == "true", "unswapped ad: Hallow's cover is up and the stream is muted underneath (" + Eval(aw, "String(Date.now()-window.adAt)") + " ms after the label)");
                Ok(Wait(() => State(aw), s => s.ContainsKey("mutedByUs") && (bool)s["mutedByUs"], 4000).ContainsKey("coreMuted") && (bool)State(aw)["coreMuted"], "unswapped ad: the tab itself is muted too (so no ad sound gets out)");
                Ok((Eval(aw, "window.__hallowTwitch.cover()&&window.__hallowTwitch.cover().text") ?? "").Contains("Ad break"), "unswapped ad: the cover shows the countdown (" + Eval(aw, "window.__hallowTwitch.cover()&&window.__hallowTwitch.cover().text") + ")");
                Ok(Dec(aw).Contains("ad label") , "unswapped ad: the decision is kept (" + Dec(aw) + ")");
                var lifted = Wait(() => Covered(aw), x => !x, 9000);
                Ok(!lifted && Eval(aw, "String(document.getElementById('v').muted)") == "false", "after the ad: the cover lifts and the sound is back (" + Eval(aw, "String(Date.now()-window.adEnd)") + " ms after it)");
                var ev1 = Wait(() => Ev(aw), e => e.Contains("blank-white"), 14000);
                Ok(ev1.Contains("blank-white") && ev1.Contains("resync"), "white player: the white picture is found and recovered (" + Eval(aw, "String(Date.now()-window.whiteAt)") + " ms after it went white): " + ev1);
                Ok(Wait(() => Eval(aw, "String(!!window.fixedAt)"), x => x == "true", 8000) == "true", "white player: the fixture's picture came back after a recovery step");
                Thread.Sleep(9000);
                var bl = Eval(aw, "JSON.stringify({r:window.__hallowTwitch.blank.recovered,w:window.__hallowTwitch.blank.why,c:window.__hallowTwitch.blank.canvas})") ?? "";
                Ok(bl.Contains("\"w\":null") && bl.Contains("\"c\":\"ok\"") && !bl.Contains("\"r\":0") && Eval(aw, "String(window.__hallowTwitch.blank.reloads)") == "0", "white player: once the picture is back nothing more happens, no page reload (" + bl + ")");
                Ok(Dec(aw).Contains("player white - recovery step 1"), "white player: the recovery is in the decisions");

                // ---- the report from this tab
                var rp = Val(Cmd("test-twr-report " + aw + " " + outDir)); string path = null; try { path = js.Deserialize<string>(rp); } catch { }
                Ok(path != null && File.Exists(path) && Path.GetFileName(path).StartsWith("Hallow Twitch report ") && path.EndsWith(".zip"), "report: saved as one zip (" + path + ")");
                string json = ""; int pngLen = 0; List<string> names = new List<string>();
                if (path != null && File.Exists(path)) using (var z = ZipFile.OpenRead(path)) { names = z.Entries.Select(e => e.Name).ToList(); var e1 = z.GetEntry("report.json"); if (e1 != null) json = new StreamReader(e1.Open()).ReadToEnd(); var e2 = z.GetEntry("player.png"); if (e2 != null) pngLen = (int)e2.Length; }
                Ok(names.Contains("report.json") && names.Contains("player.png") && pngLen > 500, "report: report.json and a picture of the player (" + pngLen + " bytes)");
                Ok(json.Contains("\"script\":4") && json.Contains("\"browser\":\"" + Program.Version + "\"") && json.Contains("\"decisions\"") && json.Contains("blank-white") && json.Contains("\"videos\"") && json.Contains("\"over\"") && json.Contains("\"picture\""), "report: versions, the script's events and decisions, the video, what is over the player, a picture sample");
                Ok(!json.Contains("adwhite") && json.Contains("[channel]"), "report: the channel's name is replaced by [channel]");
                Ok(!json.Contains("XYZ123SECRET") && !json.Contains("secret_cookie") && !json.Contains("chatuser_secret") && !json.Contains("hello"), "report: no cookie, no chat, no user name");
                var bt = Val(Cmd("test-twr-menu")) ?? "";
                Cmd("test-activate " + aw);
                var mn = Val(Cmd("test-twr-menu")) ?? "";
                Ok(mn.Contains("Ctrl+Shift+F12") && mn.Contains("\"enabled\":true"), "report: the entry is in More tools (and Ctrl+K) on a Twitch tab, with Ctrl+Shift+F12 (" + mn + ")");
                var before = Val(Cmd("test-twr-last")) ?? "null";
                var toast = Val(Cmd("test-twr-button")) ?? "";
                var last = Wait(() => Val(Cmd("test-twr-last")) ?? "null", x => x != before, 15000);
                Ok(last != before && last.Contains("Downloads") && toast.Contains("Saving"), "report: the button saves into the (test) Downloads folder: " + last + " (toast: " + toast + ")");
                var saved = Wait(() => Val(Cmd("test-twr-toast")) ?? "", x => x.Contains("Twitch report saved"), 5000);
                Ok(saved.Contains("Twitch report saved") && saved.Contains(".zip") && saved.Contains("Show in folder"), "report: the toast shows where the file is, with Show in folder: " + saved);

                // ---- the ad only in Twitch's own ad events
                int ga = NewTab(U + "/tw/gqlad");
                Ok(Wait(() => Covered(ga), x => x, 8000), "gql ad event: an impression covers the player (" + Eval(ga, "String(Date.now()-window.adAt)") + " ms)");
                Ok(Wait(() => Covered(ga), x => !x, 9000) == false, "gql ad event: the cover lifts after the pod is complete (" + Eval(ga, "String(Date.now()-window.adEnd)") + " ms)");

                // ---- a second video in an ad box
                int ab = NewTab(U + "/tw/adbox");
                var abc = Wait(() => Covered(ab), x => x, 8000);
                Ok(abc && Eval(ab, "String(document.getElementById('adv')&&document.getElementById('adv').muted)") == "true", "ad box video: it is muted and the player covered");
                Ok(Wait(() => Covered(ab), x => !x, 9000) == false, "ad box video: the cover lifts when it stops");

                // ---- a playlist read with XMLHttpRequest
                int xa = NewTab(U + "/tw/xhrad");
                Ok(Wait(() => Covered(xa), x => x, 8000) && Dec(xa).Contains("via xhr"), "xhr playlist: an ad in it is covered (" + Dec(xa) + ")");

                // ---- a white sheet with one button over the video
                int ws = NewTab(U + "/tw/whitesheet");
                Ok(Wait(() => Eval(ws, "(function(){var e=document.getElementById('sheet');return e?getComputedStyle(e).display:'missing'})()"), x => x == "none", 8000) == "none", "white sheet: a white box with one button over the video is hidden");

                // ---- a channel you allow ads on: its ad plays (no cover, no mute)
                Cmd("test-tw-set allow allowme 1"); Thread.Sleep(1500);
                int al = NewTab(U + "/tw/allowme");
                Thread.Sleep(4500);
                Ok(!Covered(al) && Eval(al, "String(document.getElementById('v').muted)") == "false", "allowed channel: its ad isn't covered or muted");

                // ---- a Twitch link a page opens into a new tab gets the Twitch script from its first page (1.14.2: it didn't wait for it)
                int op = NewTab(U + "/tw/opener");
                int tabsBefore = Tabs().Count;
                Cmd("test-twr-click " + op + " (function(){var a=document.createElement('a');a.href=location.origin+'/tw/fromlink';a.target='_blank';document.body.appendChild(a);a.click();return 1})()");
                var tabsNow = Wait(Tabs, t => t.Count > tabsBefore && (bool)t[t.Count - 1]["ready"], 10000);
                if (tabsNow.Count > tabsBefore)
                {
                    int nt = tabsNow.FindIndex(x => ((string)x["url"] ?? "").EndsWith("/tw/fromlink")); if (nt < 0) nt = tabsNow.Count - 1;
                    var sv = Wait(() => Eval(nt, "String(window.__hallowTwitch&&window.__hallowTwitch.v)"), x => x == "4", 6000);
                    Ok(sv == "4" && Wait(() => Convert.ToInt32(State(nt).ContainsKey("scriptV") ? State(nt)["scriptV"] : 0), x => x == 4, 4000) == 4, "new tab from a link: the Twitch script runs on its first page (" + sv + ")");
                }
                else Ok(false, "new tab from a link: no tab opened (" + Tabs().Count + ")");

                // ---- a plain player: no false alarms for a while
                int pl = NewTab(U + "/tw/plainone");
                Thread.Sleep(14000);
                var pe = Ev(pl);
                Ok(!Covered(pl) && !pe.Contains("blank") && !pe.Contains("stall") && !pe.Contains("unblock") && !pe.Contains("cover"), "plain player: no cover, no recovery, nothing hidden (" + pe + ")");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v1142: ALL " + pass + " PASSED" : "INTEGRATION v1142: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
