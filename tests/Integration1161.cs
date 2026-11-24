// Hallow Browser 1.16.1 - integration test "v1161", off-screen like the others (muted: test browsers start with --mute-audio), its own
// test folder, a LOCAL fake Twitch only (usher, GraphQL and playlists on 127.0.0.1; the real Twitch is never contacted). Real use: the
// dark cover showed because every backup v4 tried had failed. Twitch script v5 keeps the stream going:
//  - "deep": embed has the ad, popout's token fails (integrity), autoplay has the ad, site's usher answers 403, picture-by-picture has
//    the ad, thunderdome is clean -> thunderdome at the player's quality; every failure and its reason is in the decisions;
//  - "backup3": only autoplay (360p) is clean -> it plays the break, the badge says "ad-free backup (360p)", and after the break the
//    player goes back to its own 1080p stream;
//  - "lowq": every backup has the ad at the player's quality, popout's 480p is clean -> the quality fallback, badge "(480p)";
//  - "allbad": every backup has the ad at every quality -> only then the dark cover (lifted after the break), no badge;
//  - backup tokens carry Twitch's own device id / integrity headers from the page (never Authorization or cookies), the player's own
//    token is asked as popout, Report a Twitch problem's report.json has the attempts.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v1161
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration1161
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-v-weval 0 " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); return i; }
        static string Dec(int tab) { return Eval(tab, "JSON.stringify(window.__hallowTwitch&&window.__hallowTwitch.decisions||[])") ?? ""; }
        static string Badge(int tab) { return Eval(tab, "String(window.__hallowTwitch.badge())"); }
        static bool Covered(int tab) { return Eval(tab, "String(!!(window.__hallowTwitch.cover()&&window.__hallowTwitch.cover().shown))") == "true"; }
        static Tuple<int, string, byte[]> T(string type, string text, int code = 200) { return Tuple.Create(code, type, Encoding.UTF8.GetBytes(text)); }

        // ---- the fake Twitch: every request written down with its headers
        class Hit { public string Path, Body; public Dictionary<string, string> H; public DateTime At; }
        static readonly List<Hit> hits = new List<Hit>();
        static List<Hit> Hits(string prefix) { lock (hits) return hits.Where(r => r.Path.StartsWith(prefix)).ToList(); }
        static readonly Dictionary<string, DateTime> firstSeen = new Dictionary<string, DateTime>();
        static double Since(string ch) { DateTime t0; lock (firstSeen) { if (!firstSeen.TryGetValue(ch, out t0)) firstSeen[ch] = t0 = DateTime.Now; } return (DateTime.Now - t0).TotalSeconds; }
        const double AdFrom = 3;
        static double AdLen(string ch) { return ch == "deep" ? 40 : ch == "allbad" ? 7 : 9; }
        static bool AdOn(string ch) { var s = Since(ch); return s >= AdFrom && s < AdFrom + AdLen(ch); }
        // which stream carries the ad: per channel, player type and quality ("own" = the player's own stream)
        static bool AdIn(string ch, string type, string q)
        {
            if (!AdOn(ch)) return false;
            switch (ch)
            {
                case "deep": return type != "thunderdome";
                case "backup3": return type != "autoplay";
                case "lowq": return !(type == "popout" && q == "480p30");
                default: return true;   // allbad
            }
        }
        static readonly string[][] Qs = { new[] { "1080p60", "1920x1080", "6000000" }, new[] { "720p60", "1280x720", "3000000" }, new[] { "480p30", "852x480", "1400000" }, new[] { "360p30", "640x360", "700000" } };
        static string Master(string U, string ch, string type)
        {
            var sb = new StringBuilder("#EXTM3U\n#EXT-X-TWITCH-INFO:NODE=\"fake\"\n");
            foreach (var q in Qs)
            {
                if ((type == "autoplay" || type == "picture-by-picture") && q[0] != "360p30") continue;   // like the real ones: 360p at most
                sb.Append("#EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID=\"" + q[0] + "\",NAME=\"" + q[0] + "\",AUTOSELECT=YES,DEFAULT=YES\n#EXT-X-STREAM-INF:BANDWIDTH=" + q[2] + ",RESOLUTION=" + q[1] + ",CODECS=\"avc1.64002A,mp4a.40.2\",VIDEO=\"" + q[0] + "\"\n");
                sb.Append(U + "/v1/playlist/" + ch + "~" + type + "~" + q[0] + "\n");
            }
            return sb.ToString();
        }
        static string Media(string U, string ch, string type, string q)
        {
            int now = (int)Since(ch); bool ad = AdIn(ch, type, q);
            var sb = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:" + now + "\n");
            if (ad) sb.Append("#EXT-X-DATERANGE:ID=\"stitched-ad-" + ch + "-1\",CLASS=\"twitch-stitched-ad\",START-DATE=\"2026-11-23T20:00:00.000Z\",DURATION=" + AdLen(ch).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                ",X-TV-TWITCH-AD-ROLL-TYPE=\"MIDROLL\",X-TV-TWITCH-AD-AD-SESSION-ID=\"sess-" + ch + "\"\n");
            for (int i = 0; i < 4; i++) sb.Append("#EXTINF:1.000," + (ad ? "Amazon|1234" : "live") + "\n" + U + "/seg/" + ch + "/" + type + "/" + (ad ? "ad-" : "live-") + q + "-" + (now + i) + ".ts\n");
            return sb.ToString();
        }
        // a Twitch-like channel page: Twitch's own GraphQL calls (with its device id / integrity headers and a sign-in header), its own
        // PlaybackAccessToken call (as "site"), and a player whose Web Worker (from a revoked blob address) plays usher's stream
        static string Page(string U, string ch)
        {
            return "<!DOCTYPE html><html><head><title>" + ch + " - Twitch</title></head><body style='margin:0;background:#000'>" +
                "<div class='video-player' style='width:640px;height:360px;position:relative'><video id=v width=640 height=360 loop></video></div>" +
                "<script>(function(){var rate=8000,sec=60,n=rate*sec,buf=new ArrayBuffer(44+n),d=new DataView(buf);function s(o,t){for(var i=0;i<t.length;i++)d.setUint8(o+i,t.charCodeAt(i));}" +
                "s(0,'RIFF');d.setUint32(4,36+n,true);s(8,'WAVEfmt ');d.setUint32(16,16,true);d.setUint16(20,1,true);d.setUint16(22,1,true);d.setUint32(24,rate,true);d.setUint32(28,rate,true);d.setUint16(32,1,true);d.setUint16(34,8,true);s(36,'data');d.setUint32(40,n,true);" +
                "new Uint8Array(buf,44).fill(128);var v=document.getElementById('v');v.muted=true;v.src=URL.createObjectURL(new Blob([buf],{type:'audio/wav'}));v.play().catch(function(){});})();" +
                "fetch('" + U + "/gql',{method:'POST',headers:{'Client-ID':'kimne78kx3ncx6brgo4mv6wki5h1ko','X-Device-Id':'testdevice123','Client-Integrity':'integ-abc','Client-Version':'cv-1','Authorization':'OAuth secret-signin'},body:JSON.stringify([{operationName:'ChannelPointsContext',variables:{channelLogin:'" + ch + "'}}])});" +
                "fetch('" + U + "/gql',{method:'POST',headers:{'Client-ID':'kimne78kx3ncx6brgo4mv6wki5h1ko'},body:JSON.stringify({operationName:'PlaybackAccessToken',variables:{isLive:true,login:'" + ch + "',isVod:false,vodID:'',playerType:'site',platform:'web'},extensions:{persistedQuery:{version:1,sha256Hash:'x'}}})}).then(function(r){return r.json()}).then(function(j){window.ownToken=j});" +
                "window.wlog=[];setTimeout(function(){var bu=URL.createObjectURL(new Blob([\"importScripts('" + U + "/tw-worker.js?ch=" + ch + "')\"],{type:'text/javascript'}));var wk=new Worker(bu);URL.revokeObjectURL(bu);" +
                "wk.onmessage=function(e){window.wlog.push(e.data);if(window.wlog.length>300)window.wlog.shift();};},700);" +
                "</script></body></html>";
        }
        static string WorkerJs(string U, string ch)
        {
            return "play('" + ch + "');async function play(ch){var seen={};try{var m=await (await fetch('" + U + "/usher/api/v2/channel/hls/'+ch+'.m3u8?allow_source=true&fast_bread=true&platform=web&player_backend=mediaplayer&parent_domains=example.com&sig=s0&token=type%3Down')).text();" +
                "var media=m.split('\\n').filter(function(l){return l.indexOf('http')===0})[0];postMessage({ev:'master',ch:ch,media:media});" +
                "for(;;){try{var t=await (await fetch(media)).text();var segs=t.split('\\n').filter(function(l){return l.indexOf('http')===0});" +
                "segs.forEach(function(s){if(!seen[s]){seen[s]=1;fetch(s).then(function(r){return r.arrayBuffer()}).catch(function(){});}});" +
                "postMessage({ev:'pl',ad:/stitched/.test(t),last:segs[segs.length-1]});}catch(e){postMessage({ev:'err',e:String(e)})}await new Promise(function(r){setTimeout(r,1000)});}}catch(e){postMessage({ev:'fatal',e:String(e)})}}";
        }
        static string Qp(string path, string key) { var m = Regex.Match(path, "[?&]" + key + "=([^&]*)"); return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null; }
        static List<Hit> Segs(string ch, string kind) { return Hits("/seg/" + ch + "/").Where(h => h.Path.Contains(kind)).ToList(); }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v1161.txt"), false) { AutoFlush = true };
            var srv = new Integration110.Server(); string U = null;
            srv.Route = rq =>
            {
                var path = rq.Path; var pure = path.Split('?')[0];
                lock (hits) hits.Add(new Hit { Path = path, Body = rq.Body, H = rq.Headers, At = DateTime.Now });
                if (pure.StartsWith("/tw/")) return T("text/html; charset=utf-8", Page(U, pure.Substring(4)));
                if (pure == "/tw-worker.js") return T("text/javascript", WorkerJs(U, Qp(path, "ch")));
                if (pure.StartsWith("/usher/api/v2/channel/hls/"))
                {
                    var ch = pure.Substring(pure.IndexOf("/hls/") + 5).Replace(".m3u8", ""); var tok = Qp(path, "token") ?? "";
                    var type = tok.StartsWith("type=") ? tok.Substring(5).Split('|')[0] : "own";
                    if (ch == "deep" && type == "site" && Qp(path, "sig") == "fakesig") return T("text/plain", "forbidden", 403);
                    return T("application/vnd.apple.mpegurl", Master(U, ch, type));
                }
                if (pure.StartsWith("/v1/playlist/")) { var p = pure.Substring(13).Split('~'); return T("application/vnd.apple.mpegurl", Media(U, p[0], p[1], p[2])); }
                if (pure.StartsWith("/seg/")) return T("video/mp2t", "segment");
                if (pure == "/gql")
                {
                    object o = null; try { o = js.DeserializeObject(rq.Body ?? ""); } catch { }
                    var d = o as Dictionary<string, object>;
                    if (d != null && ((d["operationName"] as string) ?? "").StartsWith("PlaybackAccessToken"))
                    {
                        var vars = (Dictionary<string, object>)d["variables"];
                        if ((vars["login"] as string) == "deep" && (vars["playerType"] as string) == "popout" && d.ContainsKey("query"))
                            return T("application/json", "{\"errors\":[{\"message\":\"failed integrity check\"}],\"data\":{\"streamPlaybackAccessToken\":null}}");
                        return T("application/json", js.Serialize(new Dictionary<string, object> { { "data", new Dictionary<string, object> { { "streamPlaybackAccessToken", new Dictionary<string, object> { { "value", "type=" + vars["playerType"] + "|" + vars["platform"] }, { "signature", "fakesig" }, { "__typename", "PlaybackAccessToken" } } } } } }));
                    }
                    var arr = o as object[];
                    return T("application/json", js.Serialize((arr ?? new object[] { o }).Select(x => new Dictionary<string, object> { { "data", new Dictionary<string, object> { { "ok", true } } } }).ToList()));
                }
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
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                Cmd("test-mute 0");
                Ok(Regex.IsMatch(Val(Cmd("test-tw-state 0 0")) ?? "", "\"version\":([5-9]|\\d\\d)"), "twitch: the built-in page script is version 5 or newer");

                // ================= deep: the 6th backup is the clean one; every failure is in the decisions
                int dp = NewTab(U + "/tw/deep"); Cmd("test-mute " + dp);
                Wait(() => Segs("deep", "/thunderdome/live-").Count, n => n >= 6, 20000);
                var dAd = Segs("deep", "/ad-").Count; var dTd = Segs("deep", "/thunderdome/live-1080p60").Count;
                Ok(dAd == 0 && dTd >= 6, "deep: thunderdome (the only clean backup) plays the break at the player's 1080p - no ad segment (" + dAd + " ad, " + dTd + " from the backup)");
                var dd = Dec(dp);
                Ok(dd.Contains("swap deep -> thunderdome 1080p") && !dd.Contains("lower quality"), "deep: the decision names the backup and its quality");
                Ok(dd.Contains("embed:ad/class") && dd.Contains("popout:token-failed integrity check") && dd.Contains("autoplay/android:ad/") && dd.Contains("site:usher-403") && dd.Contains("picture-by-picture:ad/") && dd.Contains("thunderdome:clean 1080p"),
                    "deep: every backup asked for and why it failed is in the decisions (" + Regex.Match(dd, "swap deep[^\"]*").Value + ")");
                Ok(Wait(() => Badge(dp), x => x == "ad-free backup (1080p)", 4000) == "ad-free backup (1080p)", "deep: the badge on the player says \"ad-free backup (1080p)\"");
                Ok(Eval(dp, "(function(){var b=document.querySelector('hallow-backup');return b?getComputedStyle(b).pointerEvents:'none-found'})()") == "none" && Eval(dp, "document.querySelector('hallow-backup').shadowRoot===null") == "true", "badge: never in the way of a click, in a closed shadow root");
                Ok(!Covered(dp) && Eval(dp, "String(document.getElementById('v').muted)") == "true" && !(Val(Cmd("test-tw-state 0 " + dp)) ?? "").Contains("\"covered\":true"), "deep: no cover - the stream keeps going");
                var tokReq = Hits("/gql").Where(h => (h.Body ?? "").Contains("PlaybackAccessToken_Template") && (h.Body ?? "").Contains("\"login\":\"deep\"")).ToList();
                string hv;
                Ok(tokReq.Count >= 6 && tokReq.All(h => h.H.TryGetValue("X-Device-Id", out hv) && hv == "testdevice123" && h.H.ContainsKey("Client-Integrity") && h.H["Client-Integrity"] == "integ-abc" && h.H.ContainsKey("Client-Version")),
                    "backup tokens: asked with Twitch's own device id, integrity and version headers from the page (" + tokReq.Count + " requests)");
                Ok(tokReq.All(h => !h.H.ContainsKey("Authorization") && !h.H.ContainsKey("Cookie")), "backup tokens: never the sign-in (Authorization) or cookies");
                Ok(tokReq.Any(h => h.Body.Contains("\"playerType\":\"autoplay\"") && h.Body.Contains("\"platform\":\"android\"")) && tokReq.Any(h => h.Body.Contains("\"playerType\":\"embed\"") && h.Body.Contains("\"platform\":\"web\"")), "backup tokens: autoplay asked as Twitch's Android app, the rest as web");
                var own = Hits("/gql").Where(h => (h.Body ?? "").Contains("\"operationName\":\"PlaybackAccessToken\"") && (h.Body ?? "").Contains("\"login\":\"deep\"")).ToList();
                Ok(own.Count == 1 && own[0].Body.Contains("\"playerType\":\"popout\"") && Dec(dp).Contains("asked as popout"), "the player's own token: asked as popout instead of site");
                var um = Hits("/usher/api/v2/channel/hls/deep").Where(h => Qp(h.Path, "sig") == "s0").ToList();
                Ok(um.Count >= 1 && um.All(h => Qp(h.Path, "parent_domains") == null), "the player's own master: parent_domains left out");
                // Report a Twitch problem: the attempts are in report.json
                var rp = Val(Cmd("test-twr-report " + dp + " " + Path.Combine(dir, "reports"))); string zp = null; try { zp = js.Deserialize<string>(rp); } catch { }
                string json = ""; if (zp != null && File.Exists(zp)) using (var z = ZipFile.OpenRead(zp)) { var e1 = z.GetEntry("report.json"); if (e1 != null) json = new StreamReader(e1.Open()).ReadToEnd(); }
                Ok(json.Contains("popout:token-failed integrity check") && json.Contains("site:usher-403") && json.Contains("thunderdome:clean") && !json.Contains("deep -> ") && json.Contains("[channel]"), "report: report.json has every backup asked for and why it failed (channel anonymised)");
                Ok(!json.Contains("testdevice123") && !json.Contains("integ-abc") && !json.Contains("secret-signin"), "report: no device id, integrity token or sign-in in it");
                Cmd("test-close " + dp); Thread.Sleep(500);

                // ================= backup3: only autoplay (360p) is clean -> it plays the break, then back to the player's 1080p
                int b3 = NewTab(U + "/tw/backup3"); Cmd("test-mute " + b3);
                Wait(() => Segs("backup3", "/autoplay/live-360p30").Count, n => n >= 4, 20000);
                var b3d = Dec(b3);
                Ok(Segs("backup3", "/ad-").Count == 0 && Segs("backup3", "/autoplay/live-360p30").Count >= 4 && b3d.Contains("swap backup3 -> autoplay 360p (lower quality for the break, the player had 1080p)"), "backup3: autoplay's 360p plays the break (lower quality is fine), no ad segment");
                Ok(Wait(() => Badge(b3), x => x == "ad-free backup (360p)", 4000) == "ad-free backup (360p)" && !Covered(b3), "backup3: badge \"ad-free backup (360p)\", no cover");
                var back = Wait(() => Segs("backup3", "/own/live-1080p60").Where(h => h.At > DateTime.Now.AddSeconds(-30) && Segs("backup3", "/autoplay/").Count > 0 && h.At > Segs("backup3", "/autoplay/").Max(x => x.At)).Count(), n => n >= 4, 25000);
                Ok(back >= 4, "backup3: after the break the player is back on its own 1080p stream (" + back + " segments after the last backup one)");
                Ok(Wait(() => Badge(b3), x => x == "null", 4000) == "null", "backup3: the badge is gone after the break");
                Ok(Dec(b3).Contains("back to the player's own quality") || Dec(b3).Contains("back to the original stream of backup3"), "backup3: the return is in the decisions (" + Regex.Match(Dec(b3), "back to [^\"]*").Value + ")");
                Cmd("test-close " + b3); Thread.Sleep(500);

                // ================= lowq: every backup has the ad at 1080p; popout's 480p is clean -> the quality fallback
                int lq = NewTab(U + "/tw/lowq"); Cmd("test-mute " + lq);
                Wait(() => Segs("lowq", "/popout/live-480p30").Count, n => n >= 4, 20000);
                var lqd = Dec(lq);
                Ok(Segs("lowq", "/ad-").Count == 0 && Segs("lowq", "/popout/live-480p30").Count >= 4, "lowq: popout's 480p plays the break, no ad segment (" + Segs("lowq", "/popout/live-480p30").Count + ")");
                Ok(lqd.Contains("swap lowq -> popout 480p (lower quality") && lqd.Contains("popout:ad/class") && lqd.Contains("popout@low:clean 480p") && lqd.Contains("embed@low:ad/class 480p"), "lowq: the decisions show the full-quality round failed and the 480p round found popout (" + Regex.Match(lqd, "swap lowq[^\"]*").Value.Length + " chars)");
                Ok(Wait(() => Badge(lq), x => x == "ad-free backup (480p)", 4000) == "ad-free backup (480p)" && !Covered(lq), "lowq: badge \"ad-free backup (480p)\", no cover");
                Ok(Wait(() => Badge(lq), x => x == "null", 20000) == "null" && Wait(() => Segs("lowq", "/own/live-1080p60").Count(h => h.At > Segs("lowq", "/popout/").Max(x => x.At)), n => n >= 3, 15000) >= 3, "lowq: after the break, back to 1080p and the badge is gone");
                Cmd("test-close " + lq); Thread.Sleep(500);

                // ================= allbad: every backup, every quality has the ad -> only now the cover
                int ab = NewTab(U + "/tw/allbad"); Cmd("test-mute " + ab);
                var cov = Wait(() => Covered(ab), x => x, 15000); var shownAt = DateTime.Now;
                var abd = Dec(ab);
                Ok(cov && Eval(ab, "String(document.getElementById('v').muted)") == "true", "allbad: every backup has the ad -> the dark cover, muted");
                Ok(abd.Contains("no clean backup for allbad") && new[] { "embed:", "popout:", "autoplay/android:", "site:", "picture-by-picture:", "thunderdome:", "frontpage:", "@low:" }.All(k => abd.Contains(k)), "allbad: the decision lists all 7 backups and the low-quality round");
                Ok(Badge(ab) == "null", "allbad: no badge with the cover");
                var gone = Wait(() => Covered(ab), x => !x, 15000); var took = (DateTime.Now - shownAt).TotalSeconds;
                Ok(!gone && took < 12 && Eval(ab, "String(document.getElementById('v').muted)") == "true", "allbad: the cover lifts after the break (" + took.ToString("0.0") + " s on screen; the page's video stays muted as the fixture had it)");
                Ok(Wait(() => Segs("allbad", "/own/live-").Count(h => h.At > shownAt), n => n >= 3, 10000) >= 3, "allbad: the stream goes on after the break");
                Cmd("test-close " + ab); Thread.Sleep(500);
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v1161: ALL " + pass + " PASSED" : "INTEGRATION v1161: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
