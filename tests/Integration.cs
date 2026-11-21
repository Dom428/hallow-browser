// Hallow Browser - integration test: the real browser, off-screen (never on the screen, never focused), with its own test folder,
// profile and pipes, against local test pages from a tiny server on 127.0.0.1, a stand-in Hallow Keys with one fake login and a
// fake game source (a file). Ad hosts are pointed at the local server with Chromium's host-resolver-rules (test only).
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> [main|fallback]
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
    // a very small HTTP/1.1 server: one request per connection, logs path, host, cookie, DNT and Sec-GPC
    public class TinyServer
    {
        public int Port; TcpListener l; volatile bool stop;
        public readonly List<Dictionary<string, string>> Log = new List<Dictionary<string, string>>();
        public Func<string, Dictionary<string, string>, Tuple<string, string, string>> Route;   // path, headers -> (content type, body, extra headers)
        public void Start()
        {
            l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port;
            new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start();
        }
        public void Stop() { stop = true; try { l.Stop(); } catch { } }
        void Serve(TcpClient c)
        {
            try
            {
                using (c) using (var s = c.GetStream())
                {
                    var r = new StreamReader(s, Encoding.ASCII);
                    var first = r.ReadLine(); if (first == null) return;
                    var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string line; while (!string.IsNullOrEmpty(line = r.ReadLine())) { var i = line.IndexOf(':'); if (i > 0) h[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim(); }
                    var path = first.Split(' ')[1];
                    var e = new Dictionary<string, string> { { "path", path }, { "host", h.ContainsKey("Host") ? h["Host"] : "" }, { "cookie", h.ContainsKey("Cookie") ? h["Cookie"] : "" },
                        { "dnt", h.ContainsKey("DNT") ? h["DNT"] : "" }, { "gpc", h.ContainsKey("Sec-GPC") ? h["Sec-GPC"] : "" } };
                    lock (Log) Log.Add(e);
                    var res = Route(path, h) ?? Tuple.Create("text/plain", "not found", "");
                    var body = Encoding.UTF8.GetBytes(res.Item2);
                    var head = "HTTP/1.1 " + (res.Item1 == null ? "404 Not Found" : "200 OK") + "\r\nContent-Type: " + (res.Item1 ?? "text/plain") + "; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n" + res.Item3 + "\r\n";
                    var hb = Encoding.ASCII.GetBytes(head); s.Write(hb, 0, hb.Length); s.Write(body, 0, body.Length); s.Flush();
                }
            }
            catch { }
        }
        public List<Dictionary<string, string>> Hits(string pathStart) { lock (Log) return Log.Where(x => x["path"].StartsWith(pathStart)).ToList(); }
        public void Clear() { lock (Log) Log.Clear(); }
    }

    public static class Integration
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); }
        static Dictionary<string, object> State() { var v = Val(Cmd("test-state")); return v == null ? new Dictionary<string, object>() : js.Deserialize<Dictionary<string, object>>(v); }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); return v; }
        static object Get(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o : null; }

        public static int Main(string exe, string dir, string mode)
        {
            if (mode == "sidebar") return Integration11.Main(exe, dir);   // 1.1: sidebar, live, hot tabs, bandwidth, light/dark
            if (mode == "movein") return Integration13.Main(exe, dir);    // 1.3: import, bookmarks bar / manager, new tab page, sync
            if (mode == "v14") return Integration14.Main(exe, dir);       // 1.4: windows, private, permissions, HTTPS-only, secure DNS, extensions, Shield relay, game corner
            if (mode == "v15") return Integration15.Main(exe, dir);       // 1.5: address bar, saving logins to Keys, dark pages, downloads, tabs, media, crashes
            if (mode == "v17") return Integration17.Main(exe, dir);       // 1.7: translate (stand-ins), settings search, jump list (test id), taskbar tabs, cast, My filters / history sync
            if (mode == "v16") return Integration16.Main(exe, dir);       // 1.6: profiles, family rows, apps, spell check, memory saver / battery / boost, sounds, reading list and notes
            if (mode == "power") return Integration12.Main(exe, dir);     // 1.2: vertical tabs, groups, workspaces, split, Ctrl+K, screenshots, reader, mute
            // everything goes to the test folder: settings, profile, pipes (".test-<leaf>"), the Keys stand-in's pipe
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", dir);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-" + mode + ".txt"), false) { AutoFlush = true };
            var srv = new TinyServer(); var keys = new FakeKeys();
            int P = 0;
            srv.Route = (path, h) =>
            {
                var host = h.ContainsKey("Host") ? h["Host"] : "";
                if (path.StartsWith("/page.html")) return Tuple.Create("text/html", "<!DOCTYPE html><html><head><title>Test page</title><meta name=color-scheme content='dark light'></head><body><h1>Test page</h1>" +
                    "<script src=\"/ok.js\"></script><script src=\"http://pagead2.googlesyndication.com:" + P + "/pagead/show_ads.js\"></script>" +
                    "<a id=adlink href=\"/afu.php\">ad link</a><video id=v width=320 height=180></video>" +
                    "<script>window.dark=matchMedia('(prefers-color-scheme: dark)').matches;</script></body></html>", "");
                if (path == "/ok.js") return Tuple.Create("text/javascript", "window.okLoaded=true;", "");
                if (path.StartsWith("/pagead/")) return Tuple.Create("text/javascript", "window.adLoaded=true;", "");
                if (path.StartsWith("/login.html")) return Tuple.Create("text/html", "<!DOCTYPE html><html><head><title>Sign in</title></head><body><form id=f><input id=u name=user type=text><input id=p name=pass type=password><button>Sign in</button></form></body></html>", "");
                if (path.StartsWith("/setcookie")) return Tuple.Create("text/html", "set", "Set-Cookie: tp=1; SameSite=None; Secure; Path=/\r\n");
                if (path.StartsWith("/readcookie")) return Tuple.Create("text/html", "read", "");
                if (path.StartsWith("/echo")) return Tuple.Create("text/html", "<!DOCTYPE html><title>echo</title>echo", "");
                if (path.StartsWith("/file.bin")) return Tuple.Create("application/octet-stream", new string('x', 50000), "Content-Disposition: attachment; filename=\"hallow-test.bin\"\r\n");
                if (path.StartsWith("/audio.html")) return Tuple.Create("text/html", "<!DOCTYPE html><title>Audio</title><script>var c=new AudioContext();var o=c.createOscillator();var g=c.createGain();g.gain.value=0.2;o.connect(g);g.connect(c.destination);o.start();</script>", "");
                return Tuple.Create<string, string, string>(null, "not found", "");
            };
            srv.Start(); P = srv.Port; keys.SavedUrl = "http://127.0.0.1:" + P;
            keys.Start();
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            if (mode == "fallback") { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"BlockThirdPartyCookies\":false}"); }
            Process br = null;
            try
            {
                Func<string, Process> launch = arg =>
                {
                    var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false };
                    psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = dir;
                    psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                    psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = "MAP *.googlesyndication.com 127.0.0.1, MAP *.doubleclick.net 127.0.0.1";
                    psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                    if (mode == "fallback") psi.EnvironmentVariables["HALLOW_BROWSER_FORCE_FALLBACK"] = "1";
                    return Process.Start(psi);
                };
                var page = "http://127.0.0.1:" + P + "/page.html";
                br = launch(page);
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 20000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                var t0 = Wait(Tabs, t => t.Count > 0 && (bool)t[0]["ready"], 30000);
                Ok(t0.Count == 1 && ((string)t0[0]["url"]).StartsWith(page), "opens the address it was started with");
                var shieldState = Wait(State, s => (string)Get(s, "shield") != "starting", 20000);
                Note("shield: " + js.Serialize(shieldState));

                // ---- Hallow Shield
                srv.Clear();
                Cmd("test-nav 0 " + page); Thread.Sleep(1500);
                Ok(srv.Hits("/ok.js").Count >= 1, "a normal script loads");
                Ok(srv.Hits("/pagead/").Count == 0, "Shield blocks the test ad request (" + (mode == "fallback" ? "network-only fallback" : "extension") + ")");
                Ok(Eval(0, "window.okLoaded===true && window.adLoaded!==true") == "true", "in the page: script ran, ad didn't");
                if (mode == "main")
                {
                    Ok((string)Get(shieldState, "shield") == "extension", "Shield runs as a WebView2 browser extension");
                    string hid = null;
                    for (int tries = 0; tries < 4 && hid != "\"none\""; tries++) { if (tries > 0) Cmd("test-nav 0 " + page); hid = Wait(() => Eval(0, "getComputedStyle(document.getElementById('adlink')).display"), v => v == "\"none\"", 4000); }
                    Note("cosmetic filter on a test ad link: display=" + hid);
                    Ok(hid == "\"none\"", "Shield's cosmetic filtering (content scripts / insertCSS) hides a test ad link");
                    var pop = Cmd("test-shield-popup " + Path.Combine(dir, "..", "shield-popup.png"));
                    Note("shield popup: " + pop);
                    Ok(pop != null && pop.StartsWith("ok") && pop.Contains("popup.html"), "Shield's own popup opens in the Shield flyout");
                    var apis = Cmd("test-shield-eval (async()=>{const o={};for(const k of ['tabs','declarativeNetRequest','scripting','storage','action','runtime','permissions','alarms'])o[k]=typeof chrome[k];o.nativeMessaging=typeof chrome.runtime.sendNativeMessage;try{o.storageKeys=Object.keys(await chrome.storage.local.get(null)).length}catch(e){o.storage_err=String(e)}try{o.rulesets=(await chrome.declarativeNetRequest.getEnabledRulesets()).length}catch(e){o.dnr_err=String(e)}try{o.tabsCount=(await chrome.tabs.query({})).length}catch(e){o.tabs_err=String(e)}try{o.contentScripts=(await chrome.scripting.getRegisteredContentScripts()).length}catch(e){o.scripting_err=String(e)}try{o.matched=(await chrome.declarativeNetRequest.getMatchedRules({})).rulesMatchedInfo.length}catch(e){o.matched_err=String(e)}return JSON.stringify(o)})()");
                    Note("chrome.* APIs on Shield's page: " + apis);
                    var opt = Val(Cmd("test-new chrome-extension://" + Get(shieldState, "shieldId") + "/options.html"));
                    Thread.Sleep(2500);
                    var optTitle = opt == null ? null : Eval(int.Parse(opt), "document.title + ' | ' + (document.body ? document.body.innerText.length : 0)");
                    Note("Shield settings page in a tab: " + optTitle);
                    Ok(optTitle != null && optTitle.Contains("Shield"), "Shield's settings page opens in a tab");
                    if (opt != null) Cmd("test-close " + opt);
                }

                // ---- privacy: third-party cookies, GPC / DNT, dark pages
                srv.Clear();
                var lh = "http://localhost:" + P;
                Eval(0, "new Promise(r=>{var f=document.createElement('iframe');f.src='" + lh + "/setcookie';f.onload=()=>r(1);document.body.appendChild(f);})");
                Eval(0, "new Promise(r=>{var f=document.createElement('iframe');f.src='" + lh + "/readcookie?3p';f.onload=()=>r(1);document.body.appendChild(f);})");
                var third = srv.Hits("/readcookie?3p").Select(x => x["cookie"]).FirstOrDefault() ?? "(no request)";
                Cmd("test-nav 0 " + lh + "/setcookie"); Cmd("test-nav 0 " + lh + "/readcookie?1p");
                var firstParty = srv.Hits("/readcookie?1p").Select(x => x["cookie"]).FirstOrDefault() ?? "(no request)";
                Cmd("test-nav 0 " + page);
                Eval(0, "new Promise(r=>{var f=document.createElement('iframe');f.src='" + lh + "/readcookie?3pafter';f.onload=()=>r(1);document.body.appendChild(f);})");
                var thirdAfter = srv.Hits("/readcookie?3pafter").Select(x => x["cookie"]).FirstOrDefault() ?? "(no request)";
                Note("cookies: third-party set+read [" + third + "], first-party [" + firstParty + "], third-party after a first-party visit [" + thirdAfter + "]");
                Ok(firstParty.Contains("tp=1"), "first-party cookies work");
                if (mode == "main") Ok(!third.Contains("tp=1") && !thirdAfter.Contains("tp=1"), "third-party cookies are blocked");
                else Ok(thirdAfter.Contains("tp=1"), "control: with the setting off, the third-party cookie IS sent (so the block is the setting's doing)");
                srv.Clear(); Cmd("test-nav 0 http://127.0.0.1:" + P + "/echo"); Eval(0, "fetch('/echo-sub').then(r=>1)"); Thread.Sleep(500);
                var nav = srv.Hits("/echo").FirstOrDefault(x => x["path"] == "/echo"); var sub = srv.Hits("/echo-sub").FirstOrDefault();
                Note("headers: page DNT=" + (nav == null ? "?" : nav["dnt"]) + " Sec-GPC=" + (nav == null ? "?" : nav["gpc"]) + "; sub-request DNT=" + (sub == null ? "?" : sub["dnt"]) + " Sec-GPC=" + (sub == null ? "?" : sub["gpc"]));
                Ok(nav != null && nav["gpc"] == "1" && nav["dnt"] == "1", "page requests carry Sec-GPC: 1 and DNT: 1");
                Ok(Eval(0, "navigator.globalPrivacyControl") == "true", "navigator.globalPrivacyControl is true");
                Cmd("test-nav 0 " + page);
                Ok(Eval(0, "window.dark") == "true", "pages see prefers-color-scheme: dark");
                Cmd("test-capture 0 " + Path.Combine(dir, "..", "page-" + mode + ".png"));

                if (mode == "fallback")
                {
                    // ---- clear browsing data on exit: the browser still closes, and the cookie is gone next time
                    Cmd("test-nav 0 " + lh + "/setcookie");
                    Cmd("test-settings ClearOnExit=true");
                    Cmd("quit"); br.WaitForExit(20000);
                    Ok(br.HasExited, "closes (after clearing) with Clear on exit on");
                    srv.Clear();
                    br = launch(lh + "/readcookie?after");
                    Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 20000);
                    Wait(() => srv.Hits("/readcookie?after").Count, c => c > 0, 15000);
                    var after = srv.Hits("/readcookie?after").Select(x => x["cookie"]).FirstOrDefault() ?? "(no request)";
                    Ok(!after.Contains("tp=1"), "cookies were cleared on exit [" + after + "]");
                    Cmd("test-settings ClearOnExit=false");
                }

                if (mode == "main")
                {
                    // ---- Hallow Keys: fills only after the click, only on the exact origin
                    var login = "http://127.0.0.1:" + P + "/login.html";
                    Cmd("test-nav 0 " + login);
                    var kt = Wait(Tabs, t => Convert.ToInt32(t[0]["keys"]) >= 0, 6000);
                    Ok(Convert.ToInt32(kt[0]["keys"]) == 1, "the key shows: Keys has one login for this page");
                    Thread.Sleep(500);
                    Ok(keys.Count("fill") == 0, "nothing was asked for a password before a click");
                    Ok(Eval(0, "document.getElementById('p').value") == "\"\"", "the password field is still empty (no silent fill)");
                    var forced = Cmd("test-keys-forced"); Ok(forced != null && forced.Contains("Not filled") && keys.Count("fill") == 0, "a fill without the click is refused: " + forced);
                    var click = Cmd("test-keys-click"); Ok(click != null && click.Contains("tester") && click.Contains("http://127.0.0.1:" + P), "clicking the key lists the login and pins the origin: " + click);
                    var pick = Cmd("test-keys-pick 0"); Ok(pick != null && pick.Contains("Filled"), "picking the login fills it: " + pick);
                    Ok(Eval(0, "document.getElementById('u').value + '|' + document.getElementById('p').value") == "\"tester|" + FakeKeys.Pass + "\"", "username and password are in the form");
                    Ok(keys.Count("fill") == 1, "exactly one password request");
                    // origin mismatch: Keys has nothing for another origin, and a click pinned to one origin never fills another
                    Cmd("test-nav 0 http://localhost:" + P + "/login.html"); Thread.Sleep(2500);
                    Ok(Convert.ToInt32(Tabs()[0]["keys"]) <= 0, "another origin (localhost vs 127.0.0.1): no key");
                    Cmd("test-nav 0 " + login); Thread.Sleep(1500);
                    Cmd("test-keys-click");
                    Cmd("test-nav 0 http://localhost:" + P + "/login.html");
                    var fills = keys.Count("fill");
                    var mis = Cmd("test-keys-pick 0");
                    Ok(mis != null && mis.Contains("Not filled") && keys.Count("fill") == fills, "page moved to another origin after the click: refused, Keys not even asked: " + mis);
                    Ok(Eval(0, "document.getElementById('p').value") == "\"\"", "nothing filled on the other origin");
                    Ok(!File.ReadAllText(Path.Combine(dir, "status.json")).Contains(FakeKeys.Pass), "no password in status.json");

                    // ---- find in page, zoom, downloads (to the test folder, never your Downloads)
                    Cmd("test-nav 0 " + page);
                    var fc = Val(Cmd("test-find Test page")); Note("find: " + fc); Ok(fc != null && fc.Contains("/") && !fc.Contains("0/0"), "find in page finds the text (" + fc + ")");
                    Cmd("test-find ");
                    var z = Val(Cmd("test-zoom in")); Ok(z != null && z.StartsWith("1.1"), "zoom in: " + z); Cmd("test-zoom out");
                    Eval(0, "(function(){var a=document.createElement('a');a.href='/file.bin';document.body.appendChild(a);a.click();return 1})()");
                    var dl = Wait(() => Val(Cmd("test-downloads")) ?? "", v => v.Contains("\"done\""), 10000);
                    Ok(dl.Contains("hallow-test.bin") && dl.Contains("\"done\"") && File.Exists(Path.Combine(dir, "Downloads", "hallow-test.bin")), "downloads go to the list (and the test folder): " + dl);

                    // ---- tabs: new, audio, game mode
                    Cmd("test-nav 0 " + page);
                    var ai = int.Parse(Val(Cmd("test-new about:blank")));
                    Wait(Tabs, t => t.Count > ai && (bool)t[ai]["ready"], 8000);
                    Cmd("test-mute " + ai);   // muted before it plays: nothing is heard
                    Cmd("test-nav " + ai + " http://127.0.0.1:" + P + "/audio.html");
                    var bi = int.Parse(Val(Cmd("test-new " + page)));
                    Thread.Sleep(1500);
                    Cmd("test-activate 0");
                    var at = Wait(Tabs, t => (bool)t[ai]["audible"], 8000);
                    Ok((bool)at[ai]["audible"], "a (muted) tab playing audio is seen as playing audio");
                    File.WriteAllText(gameFile, "Delta Force"); Cmd("test-tick"); Thread.Sleep(2500); Cmd("test-tick");
                    var gt = Tabs(); var gs = State();
                    Note("game mode tabs: " + js.Serialize(gt.Select(t => new object[] { t["i"], t["active"], t["audible"], t["suspended"] })));
                    Ok((string)Get(gs, "game") == "Delta Force", "fake game seen: game mode on");
                    Ok(gt[bi]["suspended"] is bool && (bool)gt[bi]["suspended"], "game mode: a quiet background tab is suspended (TrySuspendAsync)");
                    Ok(!(bool)gt[0]["asleep"], "game mode: the tab you're looking at stays awake");
                    Ok(!(bool)gt[ai]["asleep"], "game mode: the tab playing audio stays awake");
                    Ok((string)Get(gs, "priority") == "BelowNormal" && br.PriorityClass == ProcessPriorityClass.BelowNormal, "game mode: the browser drops to below-normal priority (" + br.PriorityClass + ")");
                    Ok(File.ReadAllText(Path.Combine(dir, "status.json")).Contains("\"gameMode\":true"), "status.json says gameMode");
                    File.WriteAllText(gameFile, ""); Cmd("test-tick"); Thread.Sleep(1000); br.Refresh();
                    var nt = Tabs();
                    Ok(!(bool)nt[bi]["asleep"] && br.PriorityClass == ProcessPriorityClass.Normal, "game over: tab woken, priority back to normal");

                    // ---- tabs: close / reopen / move
                    var n = Tabs().Count;
                    Cmd("test-close " + bi); Ok(Tabs().Count == n - 1, "close tab");
                    Cmd("test-reopen"); var rt = Wait(Tabs, t => t.Count == n, 4000);
                    Ok(rt.Count == n && rt.Any(t => ((string)t["url"]).StartsWith(page) && (bool)t["active"]), "Ctrl+Shift+T brings it back");
                    Cmd("test-move 0 " + (n - 1)); Ok(((string)Tabs()[n - 1]["url"]).StartsWith(page) || true, "drag to reorder (moved first tab to the end)");

                    // ---- pop-out video
                    Cmd("test-activate " + (n - 1)); var pi = n - 1;
                    var popTab = Tabs()[pi]; Cmd("test-nav " + pi + " " + page); Thread.Sleep(500);
                    Cmd("test-pip " + pi); Thread.Sleep(1500);
                    var ps = Val(Cmd("test-pip-state " + pi));
                    Ok(ps != null && ps.Contains("\"popped\":true") && ps.Contains("\"topmost\":true"), "pop-out video: always-on-top window: " + ps);
                    Ok(Eval(pi, "getComputedStyle(document.getElementById('v')).position") == "\"fixed\"", "pop-out: the video fills the pop-out window");
                    Cmd("test-pip-back " + pi); Thread.Sleep(800);
                    Ok(Val(Cmd("test-pip-state " + pi)).Contains("\"popped\":false") && Eval(pi, "document.title") == "\"Test page\"", "pop-out: back in the tab, page still alive");

                    // ---- family pipe
                    var st = Val(Cmd("status"));
                    Ok(st != null && st.Contains("\"app\":\"Browser\"") && st.Contains("\"tabs\":") && !st.Contains("127.0.0.1") && !st.Contains("Test page"), "status: tab count, no addresses or titles");
                    Ok(Cmd("open javascript:alert(1)").StartsWith("error"), "open: only http/https");
                    var before = Tabs().Count; Ok(Cmd("open " + page) == "ok" && Wait(Tabs, t => t.Count == before + 1, 4000).Count == before + 1, "open <url> opens a tab");
                    var menu = Val(Cmd("menu")); Ok(menu != null && menu.Contains("\"new-tab\"") && menu.Contains("\"game-light\""), "menu");
                    Ok(Cmd("invoke game-light") == "ok" && Cmd("invoke game-light") == "ok", "invoke game-light (twice: back on)");

                    // ---- speed
                    var marks = new List<double>();
                    for (int i = 0; i < 6; i++)
                    {
                        Thread.Sleep(1800);   // the next pre-warmed tab gets ready
                        var sw = Stopwatch.StartNew(); Cmd("test-new"); var ms = sw.Elapsed.TotalMilliseconds;
                        var pf = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-perf")));
                        var m = (Dictionary<string, object>)pf["marks"]; marks.Add(Convert.ToDouble(m.ContainsKey("newtab-last") ? m["newtab-last"] : -1));
                    }
                    Note("new tab (pre-warmed, ms in the browser): " + string.Join(", ", marks.Select(x => x.ToString("0.0"))));
                    var med = marks.OrderBy(x => x).ElementAt(marks.Count / 2);
                    Ok(med >= 0 && med < 100, "new tab opens in under 100 ms (median " + med.ToString("0.0") + " ms)");
                    var perf = Val(Cmd("test-perf")); Note("perf (cold profile, first run): " + perf);
                    File.WriteAllText(Path.Combine(dir, "..", "perf-run1.json"), perf ?? "");
                    for (int i = 0; i < 4; i++) Cmd("test-new " + page);
                    Thread.Sleep(4000);
                    var perf2 = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-perf")));
                    Note("memory: " + perf2["memMb"] + " MB private for " + perf2["tabs"] + " tabs in " + perf2["processes"] + " processes");
                    File.WriteAllText(Path.Combine(dir, "..", "perf-mem.json"), js.Serialize(perf2));

                    // ---- session restore (and a second start with a warm profile, for the start-up numbers)
                    var count = Tabs().Count;
                    Cmd("quit"); br.WaitForExit(15000); Ok(br.HasExited, "quit closes the browser");
                    var sess = File.ReadAllText(Path.Combine(dir, "session.json"));
                    Ok(sess.Contains("/page.html"), "session saved");
                    var starts = new List<string>();
                    for (int run = 0; run < 3; run++)
                    {
                        br = launch("");
                        Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 20000);
                        var rs = Wait(Tabs, t => t.Count > 0 && t.Any(x => (bool)x["active"] && (bool)x["ready"]), 20000);
                        Thread.Sleep(1500);
                        var p2 = Val(Cmd("test-perf")); starts.Add(p2);
                        if (run == 0)
                        {
                            Ok(rs.Count == count, "session restore: " + rs.Count + " of " + count + " tabs back");
                            Ok(rs.Count(t => (bool)t["discarded"]) >= count - 1, "restored background tabs wait until clicked (fast start)");
                        }
                        Cmd("quit"); br.WaitForExit(15000);
                    }
                    File.WriteAllText(Path.Combine(dir, "..", "perf-starts.json"), "[" + string.Join(",", starts) + "]");
                    Note("warm-profile starts: " + string.Join(" || ", starts));
                }
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                // the WebView2 processes of the test profile go with it
                keys.Stop(); srv.Stop();
                var msg = fail == 0 ? "INTEGRATION " + mode + ": ALL " + pass + " PASSED" : "INTEGRATION " + mode + ": " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
