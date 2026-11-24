// Hallow Browser 1.15.1 - integration test "v1151", off-screen like the others (never shown, never focused, every page muted), its
// own test folder, made-up data and a local test server only:
//  - stand-in Hallow apps' files (Hub with two games known only by their Steam ids, Clips, Overlay, Guard, Shield with another
//    browser's count) and a FAKE Steam folder (a second library with an app manifest, a made-up appinfo.vdf);
//  - Steam names on the page (no "App <id>"), duplicate Gmail tiles merged;
//  - Shield: a local page whose ad request Shield blocks, Shield's own stored count, the relay, the new tab chip = Hallow Browser's
//    own count (not the other browser's); whether Shield's minute alarm counts without the relay (WebView2); the new-tab relay;
//  - the page rendered off-screen at 800x500, 1085x630, 1366x768, 1920x1080 and 3840x2160 (zoom 0.5), dark and light: no element
//    boxes overlap, the strip is a footer under the content, no sideways page scroll; PNGs for a look.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v1151
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
    public static class Integration1151
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 60000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static Dictionary<string, object> JS(string text) { try { return text == null ? null : js.Deserialize<Dictionary<string, object>>(text); } catch { return null; } }
        static object G(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o : null; }
        static string S(Dictionary<string, object> d, string k) { return G(d, k) as string; }
        static long N(Dictionary<string, object> d, string k) { var o = G(d, k); try { return o == null ? -1 : Convert.ToInt64(o); } catch { return -1; } }
        static List<object> A(Dictionary<string, object> d, string k) { var a = G(d, k) as System.Collections.ArrayList; return a == null ? new List<object>() : a.Cast<object>().ToList(); }

        public static int Main(string exe, string dir)
        {
            Directory.CreateDirectory(dir);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v1151.txt"), false) { AutoFlush = true };
            var shots = Path.Combine(dir, "..", "ntp-shots"); Directory.CreateDirectory(shots);
            var srv = new TinyServer(); int P = 0;
            srv.Route = (path, h) =>
            {
                if (path.StartsWith("/ad.html")) return Tuple.Create("text/html", "<!DOCTYPE html><html><head><title>ad page</title><script src=\"http://pagead2.googlesyndication.com:" + P + "/pagead/show_ads.js\"></script><script src=\"http://pagead2.googlesyndication.com:" + P + "/pagead/js/adsbygoogle.js\"></script></head><body>ads</body></html>", "");
                if (path.StartsWith("/pagead/")) return Tuple.Create("text/javascript", "window.adLoaded=true;", "");
                if (path.StartsWith("/page.html")) return Tuple.Create("text/html", "<!DOCTYPE html><title>Test page</title>ok", "");
                return Tuple.Create<string, string, string>(null, "not found", "");
            };
            srv.Start(); P = srv.Port;

            // ---- stand-in Hallow apps and a FAKE Steam (never the real ones)
            var today = DateTime.Now.ToString("yyyy-MM-dd"); var nowZ = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            var hub = Path.Combine(run, "HallowHub"); Directory.CreateDirectory(Path.Combine(hub, "meta", "1422450")); Directory.CreateDirectory(Path.Combine(hub, "meta", "9000099"));
            var jpg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=");
            File.WriteAllBytes(Path.Combine(hub, "meta", "1422450", "shot0.jpg"), jpg); File.WriteAllBytes(Path.Combine(hub, "meta", "9000099", "shot0.jpg"), jpg);
            File.WriteAllText(Path.Combine(hub, "hub.json"), "{\"Games\":{\"steam:1422450\":{\"CustomName\":\"\",\"Hidden\":false,\"LastPlayed\":\"\",\"Minutes\":0},\"steam:1966720\":{\"CustomName\":\"\",\"Hidden\":false,\"LastPlayed\":\"\"},\"steam:9000099\":{\"Hidden\":false}," +
                "\"steam:2507950\":{\"Name\":\"Delta Force\",\"LastPlayed\":\"2026-11-20T21:00:00\",\"Minutes\":900},\"steam:2073850\":{\"Name\":\"THE FINALS: A Rather Long Game Title For Two Lines\",\"LastPlayed\":\"2026-11-19T21:00:00\"}," +
                "\"steam:2357570\":{\"Name\":\"Overwatch\",\"LastPlayed\":\"2026-11-18T21:00:00\"},\"steam:2767030\":{\"Name\":\"Marvel Rivals\",\"LastPlayed\":\"2026-11-17T21:00:00\"},\"steam:3219630\":{\"Name\":\"Halloween: The Game\",\"LastPlayed\":\"2026-11-16T21:00:00\"}}}");
            File.WriteAllText(Path.Combine(hub, "status.json"), "{\"app\":\"Hub\",\"version\":\"2.17\",\"running\":false,\"playing\":[],\"recent\":[]}");
            var clips = Path.Combine(run, "HallowClips"); Directory.CreateDirectory(clips);
            File.WriteAllBytes(Path.Combine(clips, "t1.jpg"), jpg);
            File.WriteAllText(Path.Combine(clips, "status.json"), "{\"app\":\"Clips\",\"clips\":[{\"file\":\"" + Path.Combine(clips, "Delta Force 2026-11-23.mp4").Replace("\\", "\\\\") + "\",\"game\":\"Delta Force\",\"time\":\"" + DateTime.Now.AddMinutes(-30).ToString("s") + "\",\"durationSec\":30,\"thumb\":\"" + Path.Combine(clips, "t1.jpg").Replace("\\", "\\\\") + "\"}," +
                "{\"file\":\"" + Path.Combine(clips, "b.mp4").Replace("\\", "\\\\") + "\",\"game\":\"Overwatch\",\"time\":\"" + DateTime.Now.AddDays(-1).ToString("s") + "\",\"durationSec\":60}]}");
            Directory.CreateDirectory(Path.Combine(run, "HallowOverlay")); File.WriteAllText(Path.Combine(run, "HallowOverlay", "status.json"), "{\"app\":\"Overlay\",\"visible\":false}");
            Directory.CreateDirectory(Path.Combine(run, "HallowGuard")); File.WriteAllText(Path.Combine(run, "HallowGuard", "status.json"), "{\"app\":\"Guard\",\"score\":91,\"scoreLabel\":\"Looking good\"}");
            Directory.CreateDirectory(Path.Combine(run, "HallowShield"));
            File.WriteAllText(Path.Combine(run, "HallowShield", "status.json"), "{\"version\":\"1.8\",\"updated\":\"" + nowZ + "\",\"browsers\":[{\"name\":\"Chrome\",\"profile\":\"Default\",\"version\":\"1.8\",\"level\":\"optimal\",\"blockedToday\":962,\"blockedWeek\":5000,\"bannersToday\":0,\"updated\":\"" + nowZ + "\",\"day\":\"" + today + "\",\"id\":\"c0ffee00c0ffee00\"}],\"blockedToday\":962,\"blockedWeek\":5000,\"bannersToday\":0}");
            var steam = Path.Combine(dir, "FakeSteam"); var lib2 = Path.Combine(dir, "FakeLibrary");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps")); Directory.CreateDirectory(Path.Combine(steam, "appcache")); Directory.CreateDirectory(Path.Combine(lib2, "steamapps"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib2.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
            File.WriteAllText(Path.Combine(lib2, "steamapps", "appmanifest_1422450.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"1422450\"\n\t\"name\"\t\t\"Deadlock\"\n}\n");
            File.WriteAllBytes(Path.Combine(steam, "appcache", "appinfo.vdf"), V1151.AppInfo(29, Tuple.Create(1966720u, "Lethal Company"), Tuple.Create(730u, "Counter-Strike 2")));
            File.WriteAllText(Path.Combine(run, "settings.json"), "{\"NewTabHallow\":true,\"NewTabFamily\":true,\"NewTabTopSites\":true,\"NewTabClock\":true,\"GameCorner\":false,\"DefaultHintDismissed\":true}");

            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "http://127.0.0.1:" + P + "/page.html") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_STEAM_ROOT"] = steam;
                psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = "MAP *.googlesyndication.com 127.0.0.1, MAP *.doubleclick.net 127.0.0.1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = Path.Combine(dir, "game.txt"); File.WriteAllText(Path.Combine(dir, "game.txt"), "");
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                var sh = Wait(() => J(Cmd("test-v-shstate")), s => s != null && S(s, "state") == "extension", 25000);
                Ok(sh != null && S(sh, "state") == "extension", "Shield is loaded (extension)");
                Ok(Cmd("test-mute 0") == "ok", "the test page is muted");

                // ---- made-up history with the two Gmail addresses
                var now = DateTime.Now.ToString("s");
                var hist = new List<Dictionary<string, object>>();
                Action<string, string, int> hadd = (u, t, n) => hist.Add(new Dictionary<string, object> { { "Url", u }, { "Title", t }, { "Visits", n }, { "Last", now } });
                hadd("https://mail.google.com/mail/u/0/#inbox", "Inbox (121) - someone@example.com - Gmail", 60);
                hadd("https://gmail.com/", "Inbox (121) - someone@example.com - Gmail", 40);
                hadd("https://ext.example/", "EXT Torrents", 30); hadd("http://192.168.68.62:8096/web/index.html#!/home", "Media server", 25);
                hadd("https://dash.example/dashboard", "Hallow - Dashboard", 22); hadd("https://wisp.example/client/servers/x/console", "Wispbyte - Console", 20);
                hadd("https://hallow.example/", "Hallow — Services", 18); hadd("https://192.168.4.197:9443/desktop/?v=1", "UGREEN NAS", 15); hadd("https://news.example/", "News", 9);
                Note("history: " + Cmd("test-nt-hist " + js.Serialize(hist)));
                Thread.Sleep(2500);   // the family rows' background read

                var wr = J(Cmd("test-nt-write")); var data = JS(S(wr, "data")); var fam = JS(S(wr, "family"));
                var tiles = A(data, "tiles").Cast<Dictionary<string, object>>().ToList();
                Note("tiles: " + string.Join(" | ", tiles.Select(t => S(t, "t"))));
                Ok(tiles.Count(t => (S(t, "t") ?? "").StartsWith("Inbox")) == 1 && tiles.Count == 8, "tiles: the two Gmail tiles (gmail.com and the inbox) are one; 8 tiles");
                var games = A(fam, "games").Cast<Dictionary<string, object>>().ToList();
                Note("games: " + string.Join(" | ", games.Select(g => S(g, "id") + "=" + S(g, "n"))));
                Ok(games.Any(g => S(g, "id") == "1422450" && S(g, "n") == "Deadlock"), "Steam names: 1422450 = Deadlock (its app manifest in the second library)");
                Ok(games.Any(g => S(g, "id") == "1966720" && S(g, "n") == "Lethal Company"), "Steam names: 1966720 = Lethal Company (Steam's app cache, appinfo.vdf)");
                Ok(games.Any(g => S(g, "id") == "9000099" && S(g, "n") == "Steam game 9000099" && S(g, "p") != null), "Steam names: none anywhere = \"Steam game <id>\", the cover still shown");
                Ok(!games.Any(g => (S(g, "n") ?? "").StartsWith("App ")), "Steam names: never \"App <id>\"");
                var fam0 = A(data, "family").Cast<Dictionary<string, object>>().ToList();
                Note("strip before: " + js.Serialize(fam0));
                Ok(!fam0.Any(x => S(x, "k") == "SHIELD" && S(x, "v").StartsWith("962")), "Shield chip: another browser's 962 is not shown as Hallow Browser's");

                // ---- Shield: blocks on a local page; Shield's own count; the relay; the chip
                var st0 = Wait(() => J(Cmd("test-nt-shieldstats")), s => s != null && N(s, "relays") >= 1, 90000); Note("Shield's stats before: " + js.Serialize(st0));
                srv.Clear(); Cmd("test-nav 0 http://127.0.0.1:" + P + "/ad.html"); Thread.Sleep(2000);
                Ok(srv.Hits("/pagead/").Count == 0, "Shield blocks the test page's two ad requests");
                // without the relay: does Shield count by itself (its minute alarm) inside WebView2?
                var alarm = Wait(() => J(Cmd("test-nt-shieldstats")), s => s != null && N(s, "today") > N(st0, "today"), 130000);
                Note("Shield's own count after ~" + (alarm == null ? "?" : "") + " its alarm: " + js.Serialize(alarm));
                Ok(alarm != null && N(alarm, "today") >= N(st0, "today") + 1 && N(alarm, "relays") == N(st0, "relays"), "Shield counts the blocks by itself (its minute alarm reads the engine's matched rules), no relay needed (" + N(alarm, "today") + ")");
                var rel = Wait(() => J(Cmd("test-nt-relay", 60000)), r => r != null && S(r, "report") != null, 40000); Note("relay: " + js.Serialize(rel));
                var rep = JS(S(rel, "report"));
                Ok(rep != null && N(rep, "blockedToday") >= 1 && S(rep, "day") == today, "relay: Hallow Browser's report has today's blocks (" + N(rep, "blockedToday") + ", day " + S(rep, "day") + ")");
                Ok(S(rel, "chip") == N(rep, "blockedToday") + " blocked today", "new tab chip = Hallow Browser's own count (" + S(rel, "chip") + "), not the PC's sum with Chrome's 962");
                var file = File.ReadAllText(Path.Combine(run, "HallowShield", "status.json")); var fj = JS(file);
                var mine = A(fj, "browsers").Cast<Dictionary<string, object>>().FirstOrDefault(x => S(x, "name") == "Hallow Browser");
                Ok(mine != null && N(mine, "blockedToday") == N(rep, "blockedToday") && S(mine, "day") == today && N(fj, "blockedToday") == 962 + N(mine, "blockedToday"), "status.json: our entry (today's local day) next to Chrome's, the top-level sum adds both");
                // a new tab page open: the relay runs by itself (at most every 2 minutes)
                Cmd("test-new https://newtab.hallow/index.html"); Cmd("test-mute 1"); Thread.Sleep(1500); Cmd("test-m-nt-refresh");
                var ntr = Wait(() => J(Cmd("test-nt-relay-count")) ?? J(Cmd("test-nt-relay", 60000)), r => r != null && N(r, "ntRelays") >= 1, 20000);
                Ok(ntr != null && N(ntr, "ntRelays") >= 1, "a new tab page open: Shield's count is relayed by itself (" + N(ntr, "ntRelays") + ")");
                var mute = "n/a"; Note("new tab tab muted: " + mute);

                // ---- the layout: off-screen renders, dark and light
                var w2 = J(Cmd("test-nt-write")); var chips = A(JS(S(w2, "data")), "family").Cast<Dictionary<string, object>>().ToList();
                Note("strip: " + js.Serialize(chips));
                var sizes = new[] { Tuple.Create(800, 500, 1.0), Tuple.Create(1085, 630, 1.0), Tuple.Create(1366, 768, 1.0), Tuple.Create(1920, 1080, 1.0), Tuple.Create(1920, 1080, 0.5) };
                foreach (var light in new[] { false, true })
                    foreach (var sz in sizes)
                    {
                        var label = (sz.Item3 == 0.5 ? "3840x2160" : sz.Item1 + "x" + sz.Item2) + (light ? " light" : " dark");
                        var png = Path.Combine(shots, label.Replace(' ', '-') + ".png");
                        var r = J(Cmd("test-nt-render " + sz.Item1 + " " + sz.Item2 + " " + sz.Item3.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (light ? "1" : "0") + " " + png, 60000));
                        if (r == null) { Ok(false, "layout " + label + ": rendered"); continue; }
                        var ov = A(r, "overlaps").Cast<string>().ToList();
                        Note("layout " + label + ": viewport " + N(r, "vw") + "x" + N(r, "vh") + ", " + N(r, "n") + " boxes, page height " + N(r, "scrollH") + (ov.Count > 0 ? ", overlaps: " + string.Join("; ", ov.Take(5)) : ""));
                        Ok(ov.Count == 0 && N(r, "n") >= 20, "layout " + label + ": no element boxes overlap (" + N(r, "n") + " boxes)");
                        Ok(G(r, "footerBelow") is bool && (bool)G(r, "footerBelow") && !(bool)G(r, "hscroll"), "layout " + label + ": the strip is a footer under the content; no sideways page scroll");
                        var gt = A(r, "games").Cast<Dictionary<string, object>>().ToList();
                        Ok(gt.Count >= 8 && gt.All(g => N(g, "h") >= 30) && A(r, "rows").Cast<Dictionary<string, object>>().All(x => S(x, "ox") == "auto"), "layout " + label + ": game / clip cards have two-line titles; the rows scroll sideways");
                        var ch = A(r, "chips").Cast<string>().ToList();
                        Ok(ch.Any(c => c.StartsWith("SHIELD") && !c.Contains("962")) && ch.Any(c => c.StartsWith("GUARD")) && A(r, "sections").Cast<string>().Contains("HALLOW OVERLAY"), "layout " + label + ": GUARD and SHIELD chips and the Overlay row are all there (" + string.Join(" / ", ch) + ")");
                        Ok(File.Exists(png) && new FileInfo(png).Length > 5000, "layout " + label + ": picture saved");
                        if (sz.Item3 == 0.5) Ok(N(r, "vw") >= 3800, "layout 4K: the page saw a 3840 px wide window (" + N(r, "vw") + ")");
                    }
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION v1151: ALL " + pass + " PASSED" : "INTEGRATION v1151: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
