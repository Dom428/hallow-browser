// Hallow Browser 1.10 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own
// test folder, engine and pipes, and a LOCAL fake Twitch only (1.11: shaped like the real one as seen in Nov 2026 - a classic Worker
// from a blob: address that importScripts() the player worker, usher's master at /api/v2/channel/hls/<channel>.m3u8, media playlists
// at /v1/playlist/<opaque> with no ".m3u8", and a front page whose worker keeps playing after in-page navigation to a channel): player pages whose worker fetches HLS playlists (a master and media
// playlists with stitched-ad #EXT-X-DATERANGE segments), a fake usher, a fake GraphQL endpoint that hands out tokens for other player
// types, chat / emote / beacon stand-ins and a GitHub release stand-in - all on 127.0.0.1. The browser is pointed at them with its
// test switches; the real Twitch and GitHub are never contacted.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v110
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration110
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
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string Eval(int win, int tab, string expr) { var v = Val(Cmd("test-v-weval " + win + " " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : v; } catch { return v; } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Thread.Sleep(400); return i; }
        static List<int> Mine(string exe) { var r = new List<int>(); foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) r.Add(p.Id); } catch { } } return r; }
        static Dictionary<string, object> St(int win, int tab) { return J(Cmd("test-tw-state " + win + " " + tab)); }

        // ---- the fake Twitch: everything asked for is written down (path, method, body, some headers)
        public class Req { public string Path, Method, Body, Cookie, ClientId; public DateTime At; }
        static readonly List<Req> reqs = new List<Req>();
        static List<Req> Hits(string prefix) { lock (reqs) return reqs.Where(r => r.Path.StartsWith(prefix)).ToList(); }
        static void Clear() { lock (reqs) reqs.Clear(); }
        internal class Server
        {
            public int Port; TcpListener l; volatile bool stop;
            public Func<Req, Tuple<int, string, byte[]>> Route;
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
                        var first = lines[0].Split(' ');
                        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ln in lines.Skip(1)) { var i = ln.IndexOf(':'); if (i > 0) h[ln.Substring(0, i).Trim()] = ln.Substring(i + 1).Trim(); }
                        string v; string body = null;
                        if (h.TryGetValue("Content-Length", out v)) { int len = int.Parse(v); var buf = new byte[len]; int got = 0; while (got < len) { int k = s.Read(buf, got, len - got); if (k <= 0) break; got += k; } body = Encoding.UTF8.GetString(buf, 0, got); }
                        var rq = new Req { Path = first[1], Method = first[0], Body = body, Cookie = h.TryGetValue("Cookie", out v) ? v : null, ClientId = h.TryGetValue("Client-ID", out v) ? v : null, At = DateTime.Now };
                        Tuple<int, string, byte[]> r;
                        if (rq.Method == "OPTIONS") r = Tuple.Create(204, "text/plain", new byte[0]);
                        else { lock (reqs) reqs.Add(rq); r = Route(rq) ?? Tuple.Create(404, "text/plain", Encoding.UTF8.GetBytes("not found")); }
                        var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + r.Item1 + (r.Item1 < 300 ? " OK" : " Error") + "\r\nContent-Type: " + r.Item2 + "\r\nContent-Length: " + r.Item3.Length +
                            "\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: *\r\nConnection: close\r\n\r\n");
                        s.Write(hb, 0, hb.Length); s.Write(r.Item3, 0, r.Item3.Length); s.Flush();
                    }
                }
                catch { }
            }
        }
        static Tuple<int, string, byte[]> T(string type, string text, int code = 200) { return Tuple.Create(code, type, Encoding.UTF8.GetBytes(text)); }

        // a Twitch-like channel page: a player whose Web Worker (made from a blob address that is revoked at once, like Twitch does)
        // fetches the master playlist from "usher", then the media playlist every second and every segment in it; a display-ad box, a
        // chat box with an emote, a channel-points GraphQL call, an ad-event GraphQL call and two beacons (an ad host and spade)
        static string Player(string U, string ch)
        {
            return "<!DOCTYPE html><html><head><title>" + ch + " - Twitch</title></head><body style='margin:0;background:#000'>" +
                "<div class='video-player' style='width:640px;height:360px;position:relative'><video id=v width=640 height=360 loop></video></div>" +
                "<div class='stream-display-ad__wrapper' id=sda style='width:300px;height:50px;background:#333'>display ad</div>" +
                "<div id=chat class='chat-shell' style='width:300px;height:80px'><span id=chatstate>loading</span><img id=emote src='/static/emote.svg' width=28 height=28></div>" +
                "<script>(function(){var rate=8000,sec=60,n=rate*sec,buf=new ArrayBuffer(44+n),d=new DataView(buf);function s(o,t){for(var i=0;i<t.length;i++)d.setUint8(o+i,t.charCodeAt(i));}" +
                "s(0,'RIFF');d.setUint32(4,36+n,true);s(8,'WAVEfmt ');d.setUint32(16,16,true);d.setUint16(20,1,true);d.setUint16(22,1,true);d.setUint32(24,rate,true);d.setUint32(28,rate,true);d.setUint16(32,1,true);d.setUint16(34,8,true);s(36,'data');d.setUint32(40,n,true);" +
                "new Uint8Array(buf,44).fill(128);document.getElementById('v').src=URL.createObjectURL(new Blob([buf],{type:'audio/wav'}));})();" +
                "window.wlog=[];var bu=URL.createObjectURL(new Blob([\"importScripts('" + U + "/tw-worker.js?ch=" + (ch == "" ? "plainstream" : ch) + "')\"],{type:'text/javascript'}));var wk=new Worker(bu);URL.revokeObjectURL(bu);" +
                "wk.onmessage=function(e){window.wlog.push(e.data);if(window.wlog.length>200)window.wlog.shift();};" +
                // 1.11: Twitch's in-page navigation - the address changes and the SAME worker is told to play another channel
                "window.go=function(c){history.pushState({},'','" + U + "/tw/'+c);wk.postMessage({ch:c});};" +
                // 1.11: the player's own ad label, with playlists that carry no marker (the second signal)
                (ch == "domad" ? "setTimeout(function(){var l=document.createElement('div');l.setAttribute('data-a-target','video-ad-label');l.textContent='Ad';document.querySelector('.video-player').appendChild(l);setTimeout(function(){l.remove();window.labelGone=Date.now();},4000);},1500);" : "") +
                "fetch('/tw/chat-api?ch=" + ch + "').then(function(r){return r.text()}).then(function(t){document.getElementById('chatstate').textContent=t});" +
                "fetch('" + U + "/gql',{method:'POST',body:JSON.stringify([{operationName:'ChannelPointsContext',variables:{channelLogin:'" + ch + "'}}])}).then(function(r){return r.json()}).then(function(j){window.points=j});" +
                "fetch('" + U + "/gql',{method:'POST',body:JSON.stringify([{operationName:'ClientSideAdEventHandling_RecordAdEvent',variables:{input:{eventName:'video_ad_impression'}}}])}).then(function(r){return r.json()}).then(function(j){window.adEvent=j});" +
                "fetch('" + U + "/h/edge.ads.twitch.tv/ads?ch=" + ch + "').then(function(r){window.adHost=r.status}).catch(function(){window.adHost='blocked'});" +
                "fetch('" + U + "/h/spade.twitch.tv/track?ch=" + ch + "').then(function(r){window.spade=r.status}).catch(function(){window.spade='blocked'});" +
                "</script></body></html>";
        }
        static string WorkerJs(string U, string ch)
        {
            return "var gen=0;onmessage=function(e){gen++;play(e.data.ch,gen);};play('" + ch + "',0);" +
                "async function play(ch,g){var seen={};try{var m=await (await fetch('" + U + "/usher/api/v2/channel/hls/'+ch+'.m3u8?allow_source=true&fast_bread=true&platform=web&player_backend=mediaplayer&sig=s0&token=type%3Dsite')).text();" +
                "var media=m.split('\\n').filter(function(l){return l.indexOf('http')===0})[0];postMessage({ev:'master',ch:ch,media:media});" +
                "while(g===gen){try{var t=await (await fetch(media)).text();var segs=t.split('\\n').filter(function(l){return l.indexOf('http')===0});" +
                "segs.forEach(function(s){if(!seen[s]){seen[s]=1;fetch(s).then(function(r){return r.arrayBuffer()}).catch(function(){});}});" +
                "postMessage({ev:'pl',ch:ch,ad:/stitched/.test(t),last:segs[segs.length-1]});}catch(e){postMessage({ev:'err',e:String(e)})}await new Promise(function(r){setTimeout(r,1000)});}}catch(e){postMessage({ev:'fatal',e:String(e)})}}";
        }
        // which streams carry an ad: per channel and player type
        static readonly Dictionary<string, DateTime> firstSeen = new Dictionary<string, DateTime>();
        const double AllAdsSecs = 6;
        static bool AdNow(string ch, string type)
        {
            DateTime t0; lock (firstSeen) { if (!firstSeen.TryGetValue(ch, out t0)) firstSeen[ch] = t0 = DateTime.Now; }
            var el = (DateTime.Now - t0).TotalSeconds;
            switch (ch)
            {
                case "adstreamer": case "privtest": case "spastreamer": return type != "popout";        // the "popout" stream is clean
                case "allads": return el < AllAdsSecs;                               // every stream has the ad for 6 s
                case "friendly": return type == "site";
                default: return false;                                               // plainstream
            }
        }
        static string Media(string U, string ch, string type, string q)
        {
            DateTime t0; lock (firstSeen) { if (!firstSeen.TryGetValue(ch, out t0)) firstSeen[ch] = t0 = DateTime.Now; }
            int now = (int)(DateTime.Now - t0).TotalSeconds;
            bool ad = AdNow(ch, type);
            var sb = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:" + now + "\n");
            sb.Append("#EXT-X-DATERANGE:ID=\"playlist-session-1\",CLASS=\"twitch-session\",START-DATE=\"2026-11-21T22:00:00.000Z\",END-ON-NEXT=YES\n");
            if (ad)
            {
                sb.Append("#EXT-X-DATERANGE:ID=\"stitched-ad-" + ch + "-1\",CLASS=\"twitch-stitched-ad\",START-DATE=\"2026-11-21T22:00:00.000Z\",DURATION=" + (ch == "allads" ? AllAdsSecs.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : "30.000") +
                    ",X-TV-TWITCH-AD-POD-LENGTH=\"1\",X-TV-TWITCH-AD-ROLL-TYPE=\"MIDROLL\",X-TV-TWITCH-AD-AD-SESSION-ID=\"sess-" + ch + "\",X-TV-TWITCH-AD-POD-FILLED-DURATION=\"" + (ch == "allads" ? "6" : "30") + "\"\n");
                sb.Append("#EXT-X-DATERANGE:ID=\"quartile-1\",CLASS=\"twitch-ad-quartile\",START-DATE=\"2026-11-21T22:00:00.000Z\",DURATION=2.000,X-TV-TWITCH-AD-QUARTILE=\"0\"\n");
            }
            for (int i = 0; i < 4; i++)
            {
                int n = now + i;
                sb.Append("#EXTINF:1.000," + (ad ? "Amazon|1234" : "live") + "\n" + U + "/seg/" + ch + "/" + type + "/" + (ad ? "ad-" : "live-") + q + "-" + n + ".ts\n");
            }
            return sb.ToString();
        }
        static string Master(string U, string ch, string type)
        {
            var sb = new StringBuilder("#EXTM3U\n#EXT-X-TWITCH-INFO:NODE=\"fake\"\n");
            foreach (var q in new[] { new[] { "1080p60", "1920x1080", "6000000" }, new[] { "720p60", "1280x720", "3000000" }, new[] { "360p30", "640x360", "700000" } })
            {
                if ((type == "autoplay" || type == "picture-by-picture") && q[0] != "360p30") continue;   // like the real ones: 360p at most
                sb.Append("#EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID=\"" + q[0] + "\",NAME=\"" + q[0] + "\",AUTOSELECT=YES,DEFAULT=YES\n#EXT-X-STREAM-INF:BANDWIDTH=" + q[2] + ",RESOLUTION=" + q[1] + ",CODECS=\"avc1.64002A,mp4a.40.2\",VIDEO=\"" + q[0] + "\"\n");
                sb.Append(U + "/v1/playlist/" + ch + "~" + type + "~" + q[0] + "\n");
            }
            return sb.ToString();
        }
        static string Qs(string path, string key) { var m = Regex.Match(path, "[?&]" + key + "=([^&]*)"); return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : null; }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v110.txt"), false) { AutoFlush = true };
            bool ghOn = false; string ghScript = null, ghDigest = null;
            var srv = new Server(); string U = null;
            srv.Route = rq =>
            {
                var path = rq.Path; var pure = path.Split('?')[0];
                if (pure.StartsWith("/tw/chat-api")) return T("text/plain", "chat ok");
                if (pure.StartsWith("/tw/popout/")) return T("text/html; charset=utf-8", "<!DOCTYPE html><title>Chat</title>chat");
                if (pure.StartsWith("/tw/")) return T("text/html; charset=utf-8", Player(U, pure.Substring(4)));
                if (pure == "/tw-worker.js") return T("text/javascript", WorkerJs(U, Qs(path, "ch")));
                if (pure == "/static/emote.svg") return T("image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' width='28' height='28'><circle cx='14' cy='14' r='12' fill='#9146ff'/></svg>");
                if (pure.StartsWith("/usher/api/channel/hls/") || pure.StartsWith("/usher/api/v2/channel/hls/"))
                {
                    var ch = pure.Substring(pure.IndexOf("/hls/") + 5).Replace(".m3u8", ""); var tok = Qs(path, "token") ?? "";
                    var type = tok.StartsWith("type=") ? tok.Substring(5) : "site";
                    return T("application/vnd.apple.mpegurl", Master(U, ch, type));
                }
                if (pure.StartsWith("/v1/playlist/")) { var p = pure.Substring(13).Split('~'); return T("application/vnd.apple.mpegurl", Media(U, p[0], p[1], p[2])); }
                if (pure.StartsWith("/seg/")) return T("video/mp2t", "segment");
                if (pure == "/gql")
                {
                    object o = null; try { o = js.DeserializeObject(rq.Body ?? ""); } catch { }
                    var d = o as Dictionary<string, object>;
                    if (d != null && (d["operationName"] as string) == "PlaybackAccessToken_Template")
                    {
                        var vars = (Dictionary<string, object>)d["variables"];
                        return T("application/json", js.Serialize(new Dictionary<string, object> { { "data", new Dictionary<string, object> { { "streamPlaybackAccessToken", new Dictionary<string, object> { { "value", "type=" + vars["playerType"] }, { "signature", "fakesig" }, { "__typename", "PlaybackAccessToken" } } } } } }));
                    }
                    var arr = o as object[];
                    return T("application/json", js.Serialize((arr ?? new object[] { o }).Select(x => new Dictionary<string, object> { { "data", new Dictionary<string, object> { { "ok", true } } } }).ToList()));
                }
                if (pure.StartsWith("/h/")) return T("text/plain", "beacon");
                if (pure == "/gh/repos/Dom428/hallow-browser/releases/latest")
                {
                    if (!ghOn) return T("application/json", "{\"message\":\"Not Found\"}", 404);
                    return T("application/json", js.Serialize(new Dictionary<string, object> { { "tag_name", "v1.10" }, { "body", "test" }, { "assets", new object[] { new Dictionary<string, object> { { "name", "hallow-twitch.js" }, { "browser_download_url", U + "/gh/dl/hallow-twitch.js" }, { "digest", ghDigest } } } } }));
                }
                if (pure == "/gh/dl/hallow-twitch.js") return T("text/javascript; charset=utf-8", ghScript ?? "");
                if (pure == "/plain.html") return T("text/html", "<!DOCTYPE html><title>Plain</title>plain");
                return null;
            };
            srv.Start(); U = "http://127.0.0.1:" + srv.Port;
            var t0 = DateTime.Now;
            Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_TWITCH_BASE"] = U + "/tw";
                psi.EnvironmentVariables["HALLOW_BROWSER_TWITCH_API"] = U;
                psi.EnvironmentVariables["HALLOW_BROWSER_API"] = U + "/gh";
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1500);

                // ================= on by default; not on other sites
                var st0 = St(0, 0);
                Ok(B(st0, "on") && I(st0, "version") >= 1, "twitch ads: blocking is on by default (script version " + I(st0, "version") + ", " + S(st0, "source") + ")");
                int plain = NewTab(U + "/plain.html");
                Ok(Eval(0, plain, "typeof window.__hallowTwitch") == "undefined" && Eval(0, plain, "String(window.Worker).indexOf('native code')>0") == "true", "twitch ads: other sites are untouched (no script state, the real Worker)");

                // ================= a clean alternative: ad segments are never requested
                Clear();
                int tw = NewTab(U + "/tw/adstreamer");
                Wait(() => Hits("/seg/adstreamer/popout/live-").Count, n => n >= 6, 15000);
                var adSegs = Hits("/seg/adstreamer/").Where(r => r.Path.Contains("/ad-")).ToList();
                var liveSegs = Hits("/seg/adstreamer/popout/live-");
                Ok(adSegs.Count == 0 && liveSegs.Count >= 6, "stitched ads: with a clean alternative, no ad segment is ever requested (" + adSegs.Count + " ad, " + liveSegs.Count + " from the swapped stream)");
                Ok(Eval(0, tw, "String(window.__hallowTwitch.workers)") == "1", "stitched ads: the player's worker (made from a revoked blob address) got the prelude");
                var tokens = Hits("/gql").Where(r => (r.Body ?? "").Contains("PlaybackAccessToken_Template")).ToList();
                var types = tokens.Select(r => Regex.Match(r.Body, "\"playerType\":\"([a-z-]+)\"").Groups[1].Value).Distinct().ToList();
                Ok(types.Contains("embed") && types.Contains("popout") && !types.Contains("site"), "stitched ads: tokens asked for other player types (embed had the ad too, popout was clean): " + string.Join(",", types));
                Ok(tokens.Count > 0 && tokens.All(r => r.ClientId == TwitchAdRules.ClientId && r.Cookie == null), "stitched ads: GraphQL asked with Twitch's public web client id and no cookies");
                var altMasters = Hits("/usher/").Where(r => (Qs(r.Path, "token") ?? "").StartsWith("type=popout")).ToList();
                Ok(altMasters.Count >= 1 && altMasters.All(r => Qs(r.Path, "sig") == "fakesig" && Qs(r.Path, "allow_source") == "true"), "stitched ads: the alternative master is the player's own usher address with the new token");
                Ok(Hits("/v1/playlist/adstreamer~popout~1080p60").Count > 0 && Hits("/v1/playlist/adstreamer~popout~720p60").Count == 0 && Hits("/v1/playlist/adstreamer~popout~360p30").Count == 0, "stitched ads: the same quality as the player's (1080p60) is used from the alternative");
                var wl = Eval(0, tw, "JSON.stringify(window.wlog.slice(-3))") ?? "";
                Ok(wl.Contains("\"ad\":false") && wl.Contains("/popout/live-"), "stitched ads: the player sees playlists without ad markers: " + wl.Substring(0, Math.Min(160, wl.Length)));
                var s1 = Wait(() => St(0, tw), x => S(x, "note") != null, 5000);
                Ok(S(s1, "note") == "Ad break skipped (swapped stream)" && I(s1, "swaps") >= 1 && I(s1, "scriptV") >= 1, "stitched ads: the browser knows: " + S(s1, "note") + " (" + I(s1, "swaps") + " swaps)");
                Ok(B(s1, "button") && (S(s1, "tip") ?? "").StartsWith("Ad break skipped (swapped stream)"), "twitch button: shows on the channel with the status: " + S(s1, "tip"));
                Ok(!B(s1, "covered") && !B(s1, "coreMuted"), "stitched ads: nothing covered or muted when the swap worked");
                // ---- display ads, chat, emotes, channel points, beacons
                Ok(Eval(0, tw, "getComputedStyle(document.getElementById('sda')).display") == "none", "display ads: the display-ad box is hidden");
                Ok(Eval(0, tw, "getComputedStyle(document.getElementById('chat')).display") != "none" && Wait(() => Eval(0, tw, "document.getElementById('chatstate').textContent"), x => x == "chat ok", 4000) == "chat ok", "chat: still loads and shows");
                Ok(Eval(0, tw, "String(document.getElementById('emote').naturalWidth)") == "28", "chat: emotes still load");
                Ok(Wait(() => Hits("/gql").Count(r => (r.Body ?? "").Contains("ChannelPointsContext\",\"variables\":{\"channelLogin\":\"adstreamer")), n => n == 1, 4000) == 1 && (Eval(0, tw, "JSON.stringify(window.points)") ?? "").Contains("\"ok\":true"), "channel points: their GraphQL call goes through");
                Ok(Hits("/gql").Count(r => (r.Body ?? "").Contains("RecordAdEvent")) == 0 && (Eval(0, tw, "JSON.stringify(window.adEvent)") ?? "").Contains("\"data\":null"), "ad events: a GraphQL call made only of ad events is answered in the page, never sent");
                Ok(Hits("/h/edge.ads.twitch.tv").Count == 0 && Eval(0, tw, "String(window.adHost)") != "200", "beacons: the ad host is blocked by the browser (" + Eval(0, tw, "String(window.adHost)") + ")");
                Ok(Hits("/h/spade.twitch.tv").Count == 1 && Eval(0, tw, "String(window.spade)") == "200", "beacons: spade (watch time for channel points and drops) is left alone");
                Ok(I(St(0, tw), "blocked") >= 1, "beacons: counted as blocked (" + I(St(0, tw), "blocked") + ")");

                // ================= 1.11: the real-Twitch case 1.10 missed - the worker is made on the front page (playing a featured stream),
                // then the user clicks a stream: in-page navigation, the SAME worker plays the new channel
                Clear();
                int fp = NewTab(U + "/tw/");
                Wait(() => Hits("/seg/plainstream/site/live-").Count, n => n >= 3, 12000);
                Ok(Hits("/seg/plainstream/site/live-").Count >= 3 && Hits("/gql").Count(r => (r.Body ?? "").Contains("PlaybackAccessToken")) == 0, "in-page navigation: the front page's player plays its featured stream, nothing asked for");
                Clear();
                Eval(0, fp, "go('spastreamer')");
                Wait(() => Hits("/seg/spastreamer/popout/live-").Count, n => n >= 6, 15000);
                var spaAd = Hits("/seg/spastreamer/").Count(r => r.Path.Contains("/ad-"));
                Ok(spaAd == 0 && Hits("/seg/spastreamer/popout/live-").Count >= 6, "in-page navigation: the stream opened from the front page is swapped too - no ad segment (" + spaAd + " ad, " + Hits("/seg/spastreamer/popout/live-").Count + " swapped)");
                var spaTok = Hits("/gql").Where(r => (r.Body ?? "").Contains("PlaybackAccessToken")).ToList();
                Ok(spaTok.Count > 0 && spaTok.All(r => r.Body.Contains("\"login\":\"spastreamer\"")), "in-page navigation: the alternative is the channel the worker plays now (never the front page's stream)");
                Ok(Hits("/usher/api/v2/channel/hls/spastreamer").Count >= 1 && Hits("/v1/playlist/spastreamer~popout~1080p60").Count > 0, "in-page navigation: usher's v2 master is understood and the same quality is kept");
                Cmd("test-close " + fp); Thread.Sleep(500);

                // ================= 1.11: the second signal - the player's own ad label (with playlists that carry no marker)
                Clear();
                int da = NewTab(U + "/tw/domad");
                var dcov = Wait(() => Eval(0, da, "JSON.stringify(window.__hallowTwitch.cover())"), x => x != null && x.Contains("\"shown\":true"), 6000) ?? "";
                Ok(dcov.Contains("\"shown\":true") && Eval(0, da, "String(document.getElementById('v').muted)") == "true", "ad label: the player's own ad label -> covered and muted: " + dcov);
                Ok(B(Wait(() => St(0, da), x => B(x, "coreMuted"), 3000), "coreMuted"), "ad label: the browser mutes the tab too");
                var dgone = Wait(() => Eval(0, da, "JSON.stringify(window.__hallowTwitch.cover())"), x => x == "null", 9000);
                Ok(dgone == "null" && Eval(0, da, "String(window.labelGone>0)") == "true" && Eval(0, da, "String(document.getElementById('v').muted)") == "false", "ad label: gone -> the cover lifts and the video is unmuted");
                Cmd("test-close " + da); Thread.Sleep(500);

                // ================= every alternative has the ad: covered and muted, lifted after the DATERANGE's duration
                Clear();
                Cmd("test-v-wnav 0 " + tw + " " + U + "/tw/allads");
                var cov = Wait(() => Eval(0, tw, "JSON.stringify(window.__hallowTwitch.cover())"), x => x != null && x.Contains("\"shown\":true"), 8000) ?? "";
                var shownAt = DateTime.Now;
                Ok(cov.Contains("\"shown\":true") && cov.Contains("Ad break — back in 0:0"), "fallback: every alternative has the ad -> the Hallow cover shows: " + cov);
                Ok(Eval(0, tw, "String(document.getElementById('v').muted)") == "true", "fallback: the video underneath is muted");
                var s2 = St(0, tw);
                Ok(S(s2, "note") == "Ad covered" && B(s2, "covered") && B(s2, "coreMuted") && B(s2, "mutedByUs"), "fallback: the browser mutes the tab too and the button says \"" + S(s2, "note") + "\"");
                Ok(Eval(0, tw, "document.querySelector('hallow-adbreak').shadowRoot===null") == "true", "fallback: the cover lives in a closed shadow root");
                Ok(Hits("/seg/allads/site/ad-").Count > 0, "fallback: the ad itself plays (muted, covered) - " + Hits("/seg/allads/site/ad-").Count + " ad segments");
                var gone = Wait(() => Eval(0, tw, "JSON.stringify(window.__hallowTwitch.cover())"), x => x == "null", 12000);
                var took = (DateTime.Now - shownAt).TotalSeconds;
                Ok(gone == "null" && took > 3 && took < 9, "fallback: the cover lifts after the break's duration (6 s in the tag; lifted after " + took.ToString("0.0") + " s on screen)");
                Ok(Eval(0, tw, "String(document.getElementById('v').muted)") == "false", "fallback: the video is unmuted again");
                var s3 = Wait(() => St(0, tw), x => !B(x, "coreMuted"), 3000);
                Ok(!B(s3, "coreMuted") && !B(s3, "covered") && !B(s3, "mutedByUs"), "fallback: the tab is unmuted again");
                Ok(Wait(() => Eval(0, tw, "JSON.stringify(window.wlog.slice(-1))"), x => x != null && x.Contains("\"ad\":false"), 6000).Contains("live-"), "fallback: the stream goes on after the break");

                // ================= a channel you support: ads play untouched
                Clear();
                Ok(Val(Cmd("test-tw-set allow friendly 1")) != null, "support list: friendly added");
                int fr = NewTab(U + "/tw/friendly");
                Wait(() => Hits("/seg/friendly/site/ad-").Count, n => n >= 6, 12000);
                Ok(Hits("/seg/friendly/site/ad-").Count >= 6 && Hits("/gql").Count(r => (r.Body ?? "").Contains("PlaybackAccessToken")) == 0, "support list: its ads are played as they come - no other stream asked for (" + Hits("/seg/friendly/site/ad-").Count + " ad segments)");
                var sf = Wait(() => St(0, fr), x => S(x, "note") != null, 4000);
                Ok(I(sf, "swaps") == 0 && I(sf, "covers") == 0 && Eval(0, fr, "JSON.stringify(window.__hallowTwitch.cover())") == "null" && !B(sf, "coreMuted"), "support list: nothing swapped, covered or muted");
                Ok((S(sf, "tip") ?? "").StartsWith("Ads play on this channel"), "support list: the button says so: " + S(sf, "tip"));
                Ok(Eval(0, fr, "getComputedStyle(document.getElementById('sda')).display") != "none", "support list: its display ads show");
                Ok(Wait(() => Hits("/h/edge.ads.twitch.tv").Count, n => n == 1, 3000) == 1 && Wait(() => Hits("/gql").Count(r => (r.Body ?? "").Contains("RecordAdEvent")), n => n == 1, 3000) == 1, "support list: its ad beacons and ad events go through (the streamer gets credited)");
                // the Twitch button's "Support <channel>: allow ads" on the ad-free channel
                Clear();
                Cmd("test-v-wnav 0 " + tw + " " + U + "/tw/adstreamer"); Thread.Sleep(500);
                Ok(Val(Cmd("test-tw-support 0 " + tw + " 1")) == "adstreamer", "twitch button: \"Support adstreamer: allow ads\"");
                Wait(() => Hits("/seg/adstreamer/site/ad-").Count, n => n >= 4, 12000);
                var sa = St(0, tw);
                Ok(Hits("/seg/adstreamer/site/ad-").Count >= 4 && ((System.Collections.ArrayList)sa["allow"]).Contains("adstreamer"), "twitch button: the page loads again and its ads play (" + Hits("/seg/adstreamer/site/ad-").Count + " ad segments)");
                Clear(); Cmd("test-tw-support 0 " + tw + " 0");
                Wait(() => Hits("/seg/adstreamer/popout/").Count, n => n >= 4, 15000);
                var firstPop = Hits("/seg/adstreamer/popout/").Select(r => r.At).DefaultIfEmpty(DateTime.MaxValue).Min();
                var adAfter = Hits("/seg/adstreamer/").Count(r => r.Path.Contains("/ad-") && r.At >= firstPop);
                Ok(firstPop != DateTime.MaxValue && adAfter == 0, "twitch button: support off again -> the page loads again and swaps, no ad segment (" + adAfter + " ad after the swap, " + Hits("/seg/adstreamer/popout/").Count + " swapped)");

                // ================= a private window blocks too
                Clear();
                var pw = Val(Cmd("test-v-wpriv " + U + "/tw/privtest")); int pwi; int.TryParse(pw, out pwi);
                Wait(() => Hits("/seg/privtest/popout/live-").Count, n => n >= 4, 15000);
                var sp = St(pwi, 0);
                Ok(B(sp, "private") && Hits("/seg/privtest/").Count(r => r.Path.Contains("/ad-")) == 0 && Hits("/seg/privtest/popout/live-").Count >= 4, "private window: stitched ads swapped there too (no ad segment requested)");
                Ok(Wait(() => St(pwi, 0), x => I(x, "swaps") > 0, 4000) != null && I(St(pwi, 0), "swaps") > 0, "private window: the button's status works there");
                Ok(Hits("/h/edge.ads.twitch.tv").Count == 0 && Eval(pwi, 0, "String(window.adHost)") != "200" && Eval(pwi, 0, "getComputedStyle(document.getElementById('sda')).display") == "none", "private window: ad host blocked and display ad hidden (no extension needed)");
                Ok(Eval(pwi, 0, "document.getElementById('chatstate').textContent") == "chat ok", "private window: chat still loads");
                Cmd("test-v-wclose " + pwi); Thread.Sleep(800);

                // ================= switched off: nothing touched
                Clear();
                Cmd("test-activate " + tw); Cmd("test-tw-set on 0");
                Wait(() => Hits("/seg/adstreamer/site/ad-").Count, n => n >= 4, 12000);
                Ok(Hits("/seg/adstreamer/site/ad-").Count >= 4 && Eval(0, tw, "typeof window.__hallowTwitch") == "undefined" && Hits("/h/edge.ads.twitch.tv/ads?ch=adstreamer").Count == 1, "off: the page loads again with nothing touched (ads play, beacons go): " + Hits("/seg/adstreamer/site/ad-").Count + " ad segments, script " + Eval(0, tw, "typeof window.__hallowTwitch") + ", " + Hits("/h/edge.ads.twitch.tv/ads?ch=adstreamer").Count + " beacons");
                var soff = St(0, tw);
                Ok((S(soff, "tip") ?? "").StartsWith("Twitch ads aren't blocked"), "off: the button says so: " + S(soff, "tip"));
                Cmd("test-tw-set on 1"); Thread.Sleep(1500);
                Ok(Val(Cmd("test-tw-settings")) != null, "settings: the Twitch ads card builds");

                // ================= fixes between releases: the release's hallow-twitch.js, sha256-checked
                var newer = Regex.Replace(TwitchAdRules.BuiltIn, "^\\uFEFF?// hallow-twitch \\d+\\r?\\n", "// hallow-twitch 999\nwindow.__hallowTwitchFix = 1;\n");
                ghScript = newer; ghDigest = "sha256:" + new string('0', 64); ghOn = true;
                var u1 = Val(Cmd("test-tw-update")) ?? "";
                Ok(u1.Contains("didn't match GitHub's checksum") && I(St(0, tw), "version") < 999, "fixes: a download that doesn't match GitHub's sha256 is refused (" + u1 + ")");
                using (var sha = SHA256.Create()) ghDigest = "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(newer))).Replace("-", "").ToLowerInvariant();
                var u2 = Val(Cmd("test-tw-update")) ?? "";
                Ok(u2.StartsWith("Twitch ad handling updated (version 999)"), "fixes: a newer script from the release is taken: " + u2);
                Cmd("test-tw-reload-script"); Thread.Sleep(2500);
                Ok(Wait(() => Eval(0, tw, "String(window.__hallowTwitchFix)"), x => x == "1", 6000) == "1" && I(Wait(() => St(0, tw), x => I(x, "scriptV") == 999, 4000), "scriptV") == 999, "fixes: Twitch pages run it from their next load (version 999)");
                var u3 = Val(Cmd("test-tw-update")) ?? "";
                Ok(u3 == "none", "fixes: the same version isn't taken twice");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var id in Mine(exe)) try { using (var p = Process.GetProcessById(id)) if (p.StartTime >= t0) p.Kill(); } catch { }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v110: ALL " + pass + " PASSED" : "INTEGRATION v110: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
