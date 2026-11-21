// Hallow Browser 1.1 - integration test for the new parts, off-screen like the others (never shown, never focused, below-normal
// priority), with its own test folder, profile and pipes, against local pages only - the sidebar's panels are local test pages (never
// the real Discord / Twitch...), the "Twitch" and "YouTube" channel pages are fake pages on the test server, and a stand-in Hallow Hub
// on the test pipe receives the notifications.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> sidebar
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration11
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static List<Dictionary<string, object>> Panels() { var v = Val(Cmd("test-sidebar")); return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); }
        static Dictionary<string, object> Look() { var v = Val(Cmd("test-look")); return v == null ? new Dictionary<string, object>() : js.Deserialize<Dictionary<string, object>>(v); }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }

        public static int Main(string exe, string dir)
        {
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", dir);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-sidebar.txt"), false) { AutoFlush = true };
            var srv = new TinyServer(); int P = 0;
            var live = new Dictionary<string, bool> { { "hallowtest", false }, { "@HallowTest", false } };
            var big = new string('x', 2 * 1024 * 1024);
            srv.Route = (path, h) =>
            {
                var cookie = h.ContainsKey("Cookie") ? h["Cookie"] : "";
                if (path.StartsWith("/page.html")) return Tuple.Create("text/html", "<!DOCTYPE html><html><head><title>Test page</title><meta name=color-scheme content='dark light'></head><body><h1>Test page</h1></body></html>", "");
                if (path.StartsWith("/panel-cookie"))
                    return cookie.Contains("sid=hallow")
                        ? Tuple.Create("text/html", "<!DOCTYPE html><title>signed in</title>signed in", "")
                        : Tuple.Create("text/html", "<!DOCTYPE html><title>signed out</title>signing in", "Set-Cookie: sid=hallow; Max-Age=86400; Path=/; HttpOnly\r\n");
                if (path.StartsWith("/panel-audio")) return Tuple.Create("text/html", "<!DOCTYPE html><title>Music</title><script>window.go=function(){var c=new AudioContext();var o=c.createOscillator();var g=c.createGain();g.gain.value=0.1;o.connect(g);g.connect(c.destination);o.start();return 1;};</script>", "");
                if (path.StartsWith("/panel-badge")) return Tuple.Create("text/html", "<!DOCTYPE html><title>(3) Chat</title>chat", "");
                if (path.StartsWith("/panel-ua")) return Tuple.Create("text/html", "<!DOCTYPE html><title>UA</title><script>document.title=navigator.userAgent</script>", "");
                if (path.StartsWith("/big.bin")) return Tuple.Create("application/octet-stream", big, "");
                if (path.StartsWith("/bigdl.bin")) return Tuple.Create("application/octet-stream", big.Substring(0, 1024 * 1024), "Content-Disposition: attachment; filename=\"hallow-big.bin\"\r\n");
                if (path.StartsWith("/twitch/"))
                {
                    var n = path.Substring(8); bool on; live.TryGetValue(n, out on);
                    return Tuple.Create("text/html", "<!DOCTYPE html><html><head><title>" + n + " - Twitch</title><meta property=\"og:description\" content=\"Test stream\"></head><body>" +
                        (on ? "<script type=\"application/ld+json\">[{\"@type\":\"VideoObject\",\"publication\":{\"@type\":\"BroadcastEvent\",\"isLiveBroadcast\":true}}]</script>" : "") + "</body></html>", "");
                }
                if (path.StartsWith("/yt/"))
                {
                    var n = path.Substring(4).Replace("/live", ""); bool on; live.TryGetValue(n, out on);
                    return Tuple.Create("text/html", "<!DOCTYPE html><html><body><script>var ytInitialPlayerResponse={\"microformat\":{\"liveBroadcastDetails\":{\"isLiveNow\":" + (on ? "true" : "false") + "}}};</script></body></html>", "");
                }
                return Tuple.Create<string, string, string>(null, "not found", "");
            };
            srv.Start(); P = srv.Port;
            var L = "http://127.0.0.1:" + P;
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            // the panels are local test pages; live notifications on for two fake channels
            File.WriteAllText(Path.Combine(dir, "settings.json"), js.Serialize(new Dictionary<string, object> {
                { "LiveOn", true }, { "LiveChannels", new[] { "twitch:hallowtest", "youtube:@HallowTest" } }, { "SidebarSleepMinutes", 1 },
                { "SidebarSites", new object[] {
                    new Dictionary<string, object> { { "Id", "cookie" }, { "Name", "Cookie" }, { "Url", L + "/panel-cookie" }, { "Width", 380 } },
                    new Dictionary<string, object> { { "Id", "music" }, { "Name", "Music" }, { "Url", L + "/panel-audio" }, { "Width", 380 } },
                    new Dictionary<string, object> { { "Id", "chat" }, { "Name", "Chat" }, { "Url", L + "/panel-badge" }, { "Width", 380 } },
                    new Dictionary<string, object> { { "Id", "phone" }, { "Name", "Phone" }, { "Url", L + "/panel-ua" }, { "Mobile", true }, { "Width", 380 } } } } }));
            var hallow = Path.Combine(dir, "Hallow"); Directory.CreateDirectory(hallow);
            var theme = Path.Combine(hallow, "theme.json"); File.WriteAllText(theme, "{\"accent\":\"#F07820\",\"mode\":\"dark\"}");
            Process br = null;
            Func<string, Process> launch = arg =>
            {
                var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = dir;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                psi.EnvironmentVariables["HALLOW_BROWSER_LIVE_TWITCH"] = L + "/twitch/";
                psi.EnvironmentVariables["HALLOW_BROWSER_LIVE_YOUTUBE"] = L + "/yt/";
                psi.EnvironmentVariables["HALLOW_BROWSER_LIVE_SECONDS"] = "3600";   // the test asks for each check itself
                var p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return p;
            };
            try
            {
                var page = L + "/page.html";
                br = launch(page);
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 20000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count > 0 && (bool)t[0]["ready"], 30000);
                var ps = Panels();
                Ok(ps.Count == 4 && ps.All(p => !(bool)p["loaded"]), "4 panels in the sidebar, none loaded before they're opened (fast start)");

                // ---- sign-in persists across restarts (same profile as the tabs)
                Cmd("test-sidebar-show 0");
                ps = Wait(Panels, x => (bool)x[0]["ready"] && S(x[0], "title") != null, 15000);
                Note("cookie panel first visit: " + S(ps[0], "title") + " " + js.Serialize(ps[0]) + " toast: " + Val(Cmd("test-state")));
                Ok(srv.Hits("/panel-cookie").Count >= 1, "the panel loaded its site when opened");
                Cmd("quit"); br.WaitForExit(15000); Ok(br.HasExited, "quit");
                srv.Clear();
                br = launch("");
                Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 20000);
                ps = Wait(Panels, x => x.Count > 0 && (bool)x[0]["ready"], 25000);   // the open panel comes back by itself
                var hit = srv.Hits("/panel-cookie").FirstOrDefault();
                Note("after restart: cookie sent [" + (hit == null ? "(no request)" : hit["cookie"]) + "], title " + S(ps[0], "title"));
                Ok(hit != null && hit["cookie"].Contains("sid=hallow") && S(ps[0], "title") == "signed in", "sidebar panel stays signed in across a restart (persistent cookie on the browser's profile)");
                Ok((bool)ps[0]["shown"], "the panel that was open comes back open");

                // ---- unread badge, phone user agent
                Cmd("test-sidebar-show 2"); var bp = Wait(Panels, x => S(x[2], "unread") != null, 10000);
                Ok(S(bp[2], "unread") == "3", "badge from the page title \"(3) Chat\": " + S(bp[2], "unread"));
                Cmd("test-sidebar-show 3"); var up = Wait(Panels, x => (S(x[3], "title") ?? "").Contains("Mozilla"), 10000);
                Note("phone panel's navigator.userAgent: " + S(up[3], "title"));
                Ok((S(up[3], "title") ?? "").Contains("Android") && (S(up[3], "title") ?? "").Contains("Mobile"), "phone panel: the page sees a mobile user agent");
                Cmd("test-sidebar-mobile 3 0"); up = Wait(Panels, x => (S(x[3], "title") ?? "").Contains("Windows"), 10000);
                Ok((S(up[3], "title") ?? "").Contains("Windows NT") && !(S(up[3], "title") ?? "").Contains("Android"), "switched to desktop: the page sees the desktop user agent");

                // ---- sleeping: after N minutes hidden; at once in game mode; never while playing sound
                Cmd("test-tick"); Thread.Sleep(800);
                ps = Panels();
                Ok(!B(ps[0], "asleep") && !B(ps[2], "asleep"), "hidden for less than the sleep time: still awake");
                Cmd("test-sidebar-backdate 0 2"); Cmd("test-tick"); var sl = Wait(Panels, x => B(x[0], "suspended"), 6000);
                Ok(B(sl[0], "suspended") && B(sl[0], "asleep"), "hidden longer than the sleep time (1 min): the panel sleeps (TrySuspendAsync)");
                Cmd("test-sidebar-show 1"); Wait(Panels, x => (bool)x[1]["ready"], 10000);
                Cmd("test-sidebar-mute 1");   // muted before it plays: nothing is heard
                Cmd("test-sidebar-eval 1 window.go()");
                var au = Wait(Panels, x => B(x[1], "audible"), 8000);
                Ok(B(au[1], "audible"), "the music panel is playing (muted)");
                Cmd("test-sidebar-show 2");   // music and the phone panel are hidden now, just a moment ago
                Cmd("test-tick"); Thread.Sleep(800);
                Ok(!B(Panels()[3], "asleep"), "no game: a panel hidden a moment ago stays awake");
                File.WriteAllText(gameFile, "Delta Force"); Cmd("test-sidebar-show 0"); Cmd("test-tick"); Thread.Sleep(2500); Cmd("test-tick");
                var gp = Wait(Panels, x => B(x[2], "suspended") && B(x[3], "suspended"), 6000);
                Note("game mode panels: " + js.Serialize(gp.Select(p => new object[] { p["name"], p["shown"], p["audible"], p["suspended"] })));
                Ok(B(gp[2], "suspended") && B(gp[3], "suspended"), "game mode: hidden panels sleep at once");
                Ok(!B(gp[1], "suspended") && B(gp[1], "audible"), "game mode: the hidden panel playing sound stays awake");
                Ok(!B(gp[0], "suspended"), "the shown panel stays awake");
                Cmd("test-sidebar-show 2"); var wk = Wait(Panels, x => !B(x[2], "suspended"), 5000);
                Ok(!B(wk[2], "suspended"), "opening a sleeping panel wakes it");
                File.WriteAllText(gameFile, ""); Cmd("test-tick");
                var st = File.ReadAllText(Path.Combine(dir, "status.json"));
                Ok(st.Contains("\"panels\":4") && !st.Contains("127.0.0.1") && !st.Contains("Chat\""), "status.json: panel counts, no addresses or titles");
                Ok(Val(Cmd("test-sidebar-toggle")) == "False" && !(bool)Panels().Any(p => (bool)p["shown"]) && Val(Cmd("test-sidebar-toggle")) == "True", "Ctrl+Shift+S hides the sidebar (and its panel) and shows it again");

                // ---- hot tabs killer
                var bi = int.Parse(Val(Cmd("test-new " + page))); Wait(Tabs, t => t.Count > bi && (bool)t[bi]["ready"], 8000);
                Cmd("test-activate 0");
                Cmd("test-sidebar-show 2"); Thread.Sleep(800);
                var hot = js.Deserialize<List<Dictionary<string, object>>>(Val(Cmd("test-hot")) ?? "[]");
                Note("hot tabs: " + js.Serialize(hot.Select(r => r["kind"] + " " + r["name"] + " pid " + r["pid"] + " cpu " + Convert.ToDouble(r["cpu"]).ToString("0.0") + "% " + r["memMb"] + " MB")));
                var tabRows = hot.Where(r => (string)r["kind"] == "tab").ToList(); var panelRows = hot.Where(r => (string)r["kind"] == "panel").ToList();
                Ok(tabRows.Count >= 2 && tabRows.All(r => Convert.ToInt64(r["memMb"]) > 0 && Convert.ToInt32(r["pid"]) > 0), "hot tabs: every loaded tab with its renderer process and memory");
                Ok(panelRows.Count >= 2, "hot tabs: sidebar panels are listed too (" + panelRows.Count + ")");
                Ok(hot.All(r => Convert.ToDouble(r["cpu"]) >= 0), "hot tabs: CPU measured for every row (two looks a second apart)");
                Ok(hot.Any(r => (string)r["kind"] == "engine"), "hot tabs: the engine's own processes as one row");
                // put the background test tab to sleep with the button
                int sleepRow = -1; for (int i = 0; i < hot.Count; i++) if ((string)hot[i]["kind"] == "tab") sleepRow = i;   // the last tab row
                var tabsBefore = Tabs();
                var act = Cmd("test-hot-act " + sleepRow + " sleep"); Note("sleep button: " + act);
                var after = Tabs();
                Ok(act != null && act.StartsWith("ok") && (after.Count(t => (bool)t["discarded"]) > tabsBefore.Count(t => (bool)t["discarded"]) || act.Contains("stays awake")), "hot tabs: the sleep button puts a background tab to sleep");
                var cnt = Tabs().Count; var hot2 = js.Deserialize<List<Dictionary<string, object>>>(Val(Cmd("test-hot")) ?? "[]");
                var closeRow = hot2.FindIndex(r => (string)r["kind"] == "tab");
                Cmd("test-hot-act " + closeRow + " close");
                Ok(Tabs().Count == cnt - 1, "hot tabs: the close button closes that tab");
                if (Tabs().Count == 0 || !Tabs().Any(t => ((string)t["url"]).StartsWith(page))) Cmd("test-new " + page);
                Wait(Tabs, t => t.Any(x => (bool)x["active"] && (bool)x["ready"]), 8000);

                // ---- bandwidth cap: a 2 MB fetch in the page, without and with a 256 KB/s cap
                var ai = Tabs().FindIndex(t => (bool)t["active"]);
                Cmd("test-nav " + ai + " " + page);
                string fetchJs = "(async()=>{const t=performance.now();const r=await fetch('/big.bin?'+Math.random());const b=await r.arrayBuffer();return Math.round(performance.now()-t)+'|'+b.byteLength})()";
                var free = Val(Cmd("test-eval " + ai + " " + fetchJs));
                Cmd("test-settings BandwidthGameOnly=true"); Cmd("test-settings BandwidthKBps=256"); Cmd("test-tick");
                Ok(Val(Cmd("test-cap")) == "-1", "game-only cap and no game: no cap");
                Cmd("test-settings BandwidthGameOnly=false"); Cmd("test-tick"); Thread.Sleep(500);
                Ok(Val(Cmd("test-cap")) == "262144", "cap on: 256 KB/s (" + Val(Cmd("test-cap")) + " bytes/s)");
                var capped = Val(Cmd("test-eval " + ai + " " + fetchJs, 60000));
                Note("2 MB fetch: uncapped " + free + " ms, capped at 256 KB/s " + capped + " ms");
                Func<string, double> ms = v => { try { return double.Parse(js.Deserialize<string>(v).Split('|')[0]); } catch { return -1; } };
                Ok(ms(free) >= 0 && ms(capped) >= 6000 && ms(capped) > ms(free) * 4, "the cap measurably slows a 2 MB transfer (" + ms(free) + " ms -> " + ms(capped) + " ms; 2 MB at 256 KB/s = 8 s)");
                // a real download (attachment) under the cap: measured and reported as it is
                var sw = Stopwatch.StartNew();
                Cmd("test-eval " + ai + " (function(){var a=document.createElement('a');a.href='/bigdl.bin';document.body.appendChild(a);a.click();return 1})()");
                var dl = Wait(() => Val(Cmd("test-downloads")) ?? "", v => v.Contains("hallow-big.bin") && v.Contains("\"done\""), 30000);
                Note("1 MB download (attachment) with the 256 KB/s cap: " + sw.ElapsedMilliseconds + " ms (" + (dl.Contains("\"done\"") ? "done" : "not done") + ")");
                capDownloadMs = sw.ElapsedMilliseconds;
                Cmd("test-settings BandwidthKBps=0"); Cmd("test-tick"); Thread.Sleep(500);
                var freeAgain = Val(Cmd("test-eval " + ai + " " + fetchJs));
                Ok(Val(Cmd("test-cap")) == "-1" && ms(freeAgain) >= 0 && ms(freeAgain) < 3000, "cap off: full speed again (" + ms(freeAgain) + " ms)");

                // ---- live notifications against fake channel pages: no Hub -> the tray path, Hub -> Hub's notify
                var l0 = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-live")) ?? "{}");
                Note("live check (both offline): " + js.Serialize(l0));
                Ok(js.Serialize(l0).Contains("\"twitch:hallowtest\":\"offline\"") && js.Serialize(l0).Contains("\"youtube:@HallowTest\":\"offline\"") && l0["notify"] == null, "offline channels: seen as offline, no notification");
                live["hallowtest"] = true;
                var l1 = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-live")) ?? "{}");
                Ok(js.Serialize(l1).Contains("\"twitch:hallowtest\":\"live\"") && S(l1, "notify") == "tray: hallowtest is live", "Twitch channel goes live, Hub not running: tray notification (" + S(l1, "notify") + ")");
                var l2 = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-live")) ?? "{}");
                Ok(S(l2, "notify") == S(l1, "notify"), "still live: no second notification");
                var hubGot = new List<string>();
                Family.Serve("Hub", line => { lock (hubGot) hubGot.Add(line); return line.StartsWith("notify ") ? "ok" : "error unknown command"; });
                Thread.Sleep(500);
                live["@HallowTest"] = true;
                var l3 = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-live")) ?? "{}");
                string got; lock (hubGot) got = string.Join(" | ", hubGot);
                Note("stand-in Hub received: " + got);
                Ok(S(l3, "notify") == "hub: @HallowTest is live" && got.StartsWith("notify Browser\t@HallowTest is live\t"), "YouTube channel goes live, Hub running: Hub's notify (" + S(l3, "notify") + ")");
                live["hallowtest"] = false; Cmd("test-live"); live["hallowtest"] = true; Cmd("test-live");
                lock (hubGot) got = string.Join(" | ", hubGot);
                Ok(got.Contains("notify Browser\thallowtest is live"), "offline, then live again: notified again");
                Ok(srv.Hits("/twitch/").All(x => x["host"].StartsWith("127.0.0.1")), "the checker only fetched the (fake) channel pages");

                // ---- light / dark: theme.json's mode, live; pages follow
                Ok(!B(Look(), "light") && S(Look(), "scheme") == "Dark", "theme.json mode dark: dark browser, dark pages");
                File.WriteAllText(theme, "{\"accent\":\"#F07820\",\"mode\":\"light\"}");
                var lk = Wait(Look, x => B(x, "light"), 10000);
                Ok(B(lk, "light") && S(lk, "bg") == "#FFF6F4FA" && S(lk, "scheme") == "Light", "theme.json mode light: the browser turns light live (" + S(lk, "bg") + "), pages get Light");
                Cmd("test-nav " + ai + " " + page);
                Ok(Val(Cmd("test-eval " + ai + " matchMedia('(prefers-color-scheme: light)').matches")) == "true", "a page sees prefers-color-scheme: light");
                Cmd("test-capture " + ai + " " + Path.Combine(dir, "..", "page-light.png"));
                Cmd("test-settings Look=dark"); Cmd("test-eval " + ai + " 1");
                var ov = Wait(Look, x => !B(x, "light"), 8000);
                Ok(!B(ov, "light"), "the browser's own Look (Dark) wins over Hallow's light mode");
                Cmd("test-settings Look=follow");
                File.WriteAllText(theme, "{\"accent\":\"#F07820\",\"mode\":\"dark\"}");
                Ok(!B(Wait(Look, x => !B(x, "light"), 8000), "light"), "back to dark");
                Cmd("quit"); br.WaitForExit(15000); Ok(br.HasExited, "quit closes the browser");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION sidebar: ALL " + pass + " PASSED" : "INTEGRATION sidebar: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
        static long capDownloadMs;
    }
}
