// Hallow Browser 1.3 - integration test for moving in and syncing, off-screen like the others (never shown, never focused,
// below-normal priority), with its own test folders, profiles and pipes, against local test pages only. Imports read FAKE browser
// profiles made in the test folder (HALLOW_BROWSER_IMPORT_ROOT) - never the real ones. Two "PCs" (two test profiles, one after
// the other) sync through a test folder.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> movein
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration13
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null ? new Dictionary<string, object>() : js.Deserialize<Dictionary<string, object>>(v); } catch { return new Dictionary<string, object>(); } }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); }
        static int I(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        // 1.16.4 (tests): the browser's own automatic sync can still be running on a busy PC (the hook waits 10 s, then says "error not
        // run", or "error timeout" after 40 s while it goes on) - ask again for up to 2 minutes
        static string SyncNowCmd() { var sw = Stopwatch.StartNew(); string r; do { r = Cmd("test-m-sync-now", 120000); if (r == null || !(r.StartsWith("error not run") || r.StartsWith("error timeout"))) break; Console.WriteLine("note sync-now: " + r + " after " + sw.ElapsedMilliseconds + " ms - asking again"); Thread.Sleep(1000); } while (sw.ElapsedMilliseconds < 120000); return r; }
        static string Str(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static List<string> Tree(Dictionary<string, object> st) { object o; return st.TryGetValue("tree", out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Cast<string>().ToList() : new List<string>(); }
        static string Eval(int tab, string expr) { return Val(Cmd("test-eval " + tab + " " + expr)); }

        public static int Main(string exe, string dir)
        {
            // two "PCs": their folders have the same name, so this process's test pipe reaches either (one runs at a time)
            // 1.16.4 (tests): under a deep work folder (the all-suites runs) this suite died silently - the fake profiles' deepest file
            // passed Windows' 260-character limit - and WebView2 hung loading extensions (Shield's page for the sync, measured) from a
            // profile that deep. A real profile is short (%LOCALAPPDATA%\HallowBrowser). So the browsers, fake profiles and sync folder go
            // to a short folder in %TEMP% then (removed at the end); the report stays next to the work folder.
            var work = Path.GetFullPath(dir).Length > 90 ? Path.Combine(Path.GetTempPath(), "hb13-" + Process.GetCurrentProcess().Id) : dir;
            var pcA = Path.Combine(work, "pcA", "run"); var pcB = Path.Combine(work, "pcB", "run");
            Directory.CreateDirectory(pcA); Directory.CreateDirectory(pcB);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", pcA);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-movein.txt"), false) { AutoFlush = true };
            var fakeRoot = Path.Combine(work, "fake-profiles");
            if (work != dir) Note("deep work folder: the browsers run in " + work);
            var fake = V13.MakeFakeProfiles(fakeRoot); var fakeCount = Directory.GetFiles(fake, "*", SearchOption.AllDirectories).Length;
            var syncFolder = Path.Combine(work, "SyncFolder"); Directory.CreateDirectory(syncFolder);
            // the other Hallow apps' status files (for the new tab page's strip), in PC A's test folder
            Directory.CreateDirectory(Path.Combine(pcA, "HallowGuard")); Directory.CreateDirectory(Path.Combine(pcA, "HallowShield"));
            File.WriteAllText(Path.Combine(pcA, "HallowGuard", "status.json"), "{\"app\":\"Guard\",\"score\":88,\"scoreLabel\":\"Looking good\"}");
            File.WriteAllText(Path.Combine(pcA, "HallowShield", "status.json"), "{\"blockedToday\":4321,\"updated\":\"" + DateTime.Now.ToString("s") + "\"}");
            var srv = new TinyServer();
            Func<string, string> page = t => "<!DOCTYPE html><html><head><title>" + t + "</title></head><body><h1>" + t + "</h1></body></html>";
            srv.Route = (path, h) =>
            {
                foreach (var n in new[] { "alpha", "bravo", "charlie", "delta" }) if (path.StartsWith("/" + n + ".html")) return Tuple.Create("text/html", page(char.ToUpper(n[0]) + n.Substring(1) + " page"), "");
                return Tuple.Create<string, string, string>(null, "not found", "");
            };
            srv.Start(); var Lh = "http://127.0.0.1:" + srv.Port;
            Process br = null;
            Func<string, string, Process> launch = (d, arg) =>
            {
                var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = d;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = Path.Combine(d, "game.txt");
                psi.EnvironmentVariables["HALLOW_BROWSER_IMPORT_ROOT"] = fake;
                var p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return p;
            };
            Func<bool> up = () => Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000) != null && Wait(Tabs, t => t.Any(x => (bool)x["active"] && (bool)x["ready"]), 25000).Count > 0;
            Func<string, int> open = url => { var v = Val(Cmd("test-new " + url)); int i = v == null ? -1 : int.Parse(v); Wait(Tabs, t => i >= 0 && i < t.Count && (bool)t[i]["ready"], 15000); return i; };
            Func<Dictionary<string, object>> state = () => J(Cmd("test-m-bm-state"));
            try
            {
                File.WriteAllText(Path.Combine(pcA, "game.txt"), ""); File.WriteAllText(Path.Combine(pcB, "game.txt"), "");
                br = launch(pcA, Lh + "/alpha.html");
                Ok(up(), "PC A: browser up (off-screen)");
                Wait(state, s => B(s, "loaded"), 10000);

                // ---- moving in: the picker's list and counts, import, no duplicates
                var found = Val(Cmd("test-m-import-find"));
                var list = found == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(found);
                Ok(list.Count == 5 && (string)list[0]["label"] == "Chrome" && I(list[0], "bookmarks") == 4 && I(list[0], "history") == 40, "import picker: 5 fake profiles with counts (Chrome: 4 bookmarks, 40 pages)");
                var r1 = J(Cmd("test-m-import-run 0 1 1"));
                Ok(I(r1, "bookmarks") == 4 && I(r1, "history") == 40, "import Chrome: 4 bookmarks and 40 pages of history");
                var r2 = J(Cmd("test-m-import-run 0 1 1"));
                Ok(I(r2, "bookmarks") == 0 && I(r2, "skipped") == 4 && I(r2, "history") == 0, "import Chrome again: nothing twice");
                var r3 = J(Cmd("test-m-import-run 4 1 0"));
                Ok(I(r3, "bookmarks") == 3, "import Firefox (jsonlz4 bookmarks): 3");
                var st = state(); var tree = Tree(st);
                // 1.13: where the other browser had them - Chrome's bar onto the (empty) bar, Firefox's toolbar into a "From Firefox" folder on it
                Ok(tree.Any(t => t.StartsWith("bar/Games|Delta Force|")) && tree.Any(t => t.StartsWith("bar/From Firefox/")) && !tree.Any(t => t.Contains("Imported from")), "imported where the browsers had them (1.13: Chrome's bar onto the bar, Firefox's toolbar as \"From Firefox\" on it), structure kept");
                var barBefore = I(st, "barItems");
                var hist = J(Cmd("test-p-history"));
                Ok(I(hist, "count") >= 40, "history: the imported pages are there for Ctrl+K (" + I(hist, "count") + ")");
                var pal = Val(Cmd("test-p-palette Chrome history 17"));
                Ok(pal != null && pal.Contains("chist17.example"), "Ctrl+K finds an imported history page");

                // ---- the bookmarks bar
                Ok(B(st, "barShown") == (I(st, "count") > 0), "bookmarks bar: on by itself once there are bookmarks (1.9; imported ones count), off without");
                Ok(Cmd("test-m-bm-bar on") == "ok True", "Ctrl+Shift+B's action shows the bar");
                var ids = new List<string>();
                foreach (var n in new[] { "alpha", "bravo", "charlie" }) ids.Add(Val(Cmd("test-m-bm-add bar " + Lh + "/" + n + ".html " + n + " page")));
                var fid = Val(Cmd("test-m-bm-folder bar Raid stuff"));
                st = Wait(state, s => I(s, "barItems") == barBefore + 4, 5000);
                Ok(I(st, "barItems") == barBefore + 4, "the bar shows the imported ones, 3 bookmarks and a folder (" + I(st, "barItems") + ")");
                Cmd("test-m-bm-move " + ids[2] + " bar 0");
                Cmd("test-m-bm-move " + ids[1] + " " + fid + " 0");
                Cmd("test-m-bm-rename " + fid + " Raid night");
                st = state(); tree = Tree(st);
                var barOrder = string.Join(",", tree.Where(t => t.StartsWith("bar|")).Select(t => t.Split('|')[1]));
                Ok(barOrder.StartsWith("charlie page,") && barOrder.EndsWith(",alpha page,Raid night") && barOrder.Split(',').Length == barBefore + 3, "drag to reorder and into a folder, rename: " + barOrder);
                Ok(tree.Any(t => t.StartsWith("bar/Raid night|bravo page|")), "the bookmark is inside the renamed folder");
                var before = Tabs().Count; Cmd("test-m-bm-click " + ids[0]);
                Ok(Wait(Tabs, t => t.Count == before + 1, 5000).Count == before + 1, "clicking a bar bookmark opens it");

                // ---- the manager page and HTML files
                var mi = open("hallow://bookmarks"); var tabs = Tabs();
                Ok(mi >= 0 && (string)tabs[mi]["url"] == "hallow://bookmarks" && (string)tabs[mi]["title"] == "Bookmarks", "hallow://bookmarks opens the bookmarks manager in a tab");
                var exp = Path.Combine(work, "export.html");
                Ok(Val(Cmd("test-m-bm-export " + exp)) == exp && File.ReadAllText(exp).StartsWith("<!DOCTYPE NETSCAPE-Bookmark-file-1>") && File.ReadAllText(exp).Contains("Raid night"), "export: a Netscape bookmarks file");
                Ok(Val(Cmd("test-m-bm-import " + exp)) == "0", "importing our own export again adds nothing (no duplicates)");

                // ---- the new tab page: tiles from history, the Hallow strip, pin / remove from the page itself
                for (int k = 0; k < 3; k++) { Cmd("test-nav 0 " + Lh + "/delta.html"); Cmd("test-nav 0 " + Lh + "/alpha.html"); }
                Cmd("test-p-history-save"); Thread.Sleep(500); Cmd("test-m-nt-refresh"); Thread.Sleep(800);
                var nt = J(Cmd("test-m-nt-data"));
                var tiles = nt.ContainsKey("tiles") ? ((System.Collections.ArrayList)nt["tiles"]).Cast<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();
                var fam = nt.ContainsKey("family") ? ((System.Collections.ArrayList)nt["family"]).Cast<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>();
                Ok(tiles.Count == NewTabData.MaxTiles && tiles.All(t => ((string)t["u"]).StartsWith("http")), "new tab data: speed-dial tiles from the history, imported pages included (" + tiles.Count + ")");
                Ok(fam.Any(f => (string)f["k"] == "GUARD" && ((string)f["v"]).StartsWith("88/100")) && fam.Any(f => (string)f["k"] == "SHIELD" && ((string)f["v"]).StartsWith("4,321")), "new tab data: Guard score and Shield's blocked today from their status files" + (fam.Count == 0 ? " (no strip: " + js.Serialize(nt).Substring(0, Math.Min(300, js.Serialize(nt).Length)) + ")" : " " + js.Serialize(fam)));
                var ni = Val(Cmd("test-new")); int nti = ni == null ? -1 : int.Parse(ni);
                var shown = Wait(() => Eval(nti, "document.querySelectorAll('.tl').length"), v => v != null && v != "0" && v != "1", 8000);
                Ok(shown != null && int.Parse(shown) >= 2, "the new tab page draws the tiles (" + shown + ")");
                var famText = Eval(nti, "document.getElementById('fam').textContent");
                Ok(famText != null && famText.Contains("GUARD") && famText.Contains("4,321 blocked today"), "the new tab page shows the Hallow strip: " + famText);
                var clock = Eval(nti, "document.getElementById('clk').textContent.length");
                Ok(clock != null && int.Parse(clock) >= 4, "the clock is shown");
                var firstUrl = Eval(nti, "document.querySelector('.tl').href");
                Eval(nti, "document.querySelector('.tl .pn').click(),1");
                var set = Wait(() => J(Cmd("test-m-settings")), s => s.ContainsKey("pinned") && ((System.Collections.ArrayList)s["pinned"]).Count > 0, 5000);
                Ok(set.ContainsKey("pinned") && ((System.Collections.ArrayList)set["pinned"]).Cast<string>().Any(u => firstUrl != null && firstUrl.Contains(u.Replace("\"", ""))), "pin from the page itself (the star on a tile)");
                Eval(nti, "(function(){var t=[].slice.call(document.querySelectorAll('.tl')).filter(function(x){return x.className.indexOf('pinned')<0;});if(t.length)t[0].querySelector('.x').click();return t.length;})()");
                set = Wait(() => J(Cmd("test-m-settings")), s => s.ContainsKey("hidden") && ((System.Collections.ArrayList)s["hidden"]).Count > 0, 5000);
                Ok(set.ContainsKey("hidden") && ((System.Collections.ArrayList)set["hidden"]).Count == 1, "remove a tile from the page itself (the x)");
                Ok(Val(Cmd("test-eval 0 (function(){try{chrome.webview.postMessage('hallow:nt-pin\\thttps://evil.example/\\tEvil');}catch(e){}return 1;})()")) != null && !Val(Cmd("test-m-settings")).Contains("evil.example"), "an ordinary page can't pin tiles (only the new tab page is listened to)");

                // ---- speed: new tabs (pre-warmed), with tiles and the strip
                // 1.16.4 (tests): as in the main suite - wait for the pre-warmed view to be ready and for this tab's own render mark (reading
                // at once could return the previous tab's); 9 samples, the median; 100 ms, or a quarter of a cold tab when the PC is busy
                Func<string, double> mark = k => { try { var pf = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-perf"))); var m = (Dictionary<string, object>)pf["marks"]; return m.ContainsKey(k) ? Convert.ToDouble(m[k]) : -1; } catch { return -1; } };
                Func<string, double> sample = arg => { var prev = mark("newtab-last"); Cmd("test-new" + arg); return Wait(() => mark("newtab-last"), v => v >= 0 && v != prev, 10000); };
                var marks = new List<double>();
                for (int i = 0; i < 9; i++)
                {
                    Wait(() => mark("prewarm-ready") >= mark("prewarm-made") && mark("prewarm-made") >= 0, r => r, 15000); Thread.Sleep(300);
                    marks.Add(sample(""));
                }
                var lastPre = Tabs().Count - 1;
                var cold = new List<double>();
                for (int i = 0; i < 3; i++) cold.Add(sample(" " + Lh + "/alpha.html?cold=" + i));
                var coldMed = cold.OrderBy(x => x).ElementAt(cold.Count / 2);
                var med = marks.OrderBy(x => x).ElementAt(marks.Count / 2);
                Note("cold new tab (a fresh view): " + string.Join(", ", cold.Select(x => x.ToString("0.0"))));
                Note("new tab with tiles (pre-warmed, ms in the browser): " + string.Join(", ", marks.Select(x => x.ToString("0.0"))) + " - median " + med.ToString("0.0"));
                File.WriteAllText(Path.Combine(dir, "..", "perf-newtab13.json"), js.Serialize(marks));
                Ok(med >= 0 && (med < 100 || med < coldMed / 4), "new tab opens in under 100 ms with tiles, or under a quarter of a cold tab when the PC is busy (median " + med.ToString("0.0") + " ms, cold " + coldMed.ToString("0.0") + " ms)");
                var lastNt = lastPre; var drawn = Wait(() => Eval(lastNt, "document.querySelectorAll('.tl').length"), v => v != null && v != "0", 5000);
                Ok(drawn != null && drawn != "0", "a pre-warmed new tab has its tiles too");

                // ---- sync (encrypted) to the folder
                Ok(Cmd("test-m-sync-set " + syncFolder + " integration secret 42") == "ok", "sync on: folder + password");
                var rs1 = SyncNowCmd(); var s1 = J(rs1);   // 1.16.4 (tests): a slow sync on a busy PC gets 2 minutes; its reply is shown
                var sf = Path.Combine(syncFolder, SyncCore.FileName);
                Ok(Str(s1, "state") == "synced" && B(s1, "wrote") && B(s1, "encrypted") && File.Exists(sf), "PC A synced: the encrypted file is in the folder" + (Str(s1, "state") == "synced" ? "" : " (" + rs1 + ")"));
                var raw = File.ReadAllText(sf);
                Ok(!raw.Contains("127.0.0.1") && !raw.Contains("Raid night") && !raw.Contains("chromebar"), "the sync file shows no addresses or titles");
                Ok(!raw.Contains("chist") , "history isn't in the sync file");
                Ok(!Directory.GetFiles(pcA, "*.json").Any(f => File.ReadAllText(f).Contains("integration secret")), "the sync password isn't written in plain text in the data folder");

                // ---- PC A restarts: the bar and the bookmarks are still there
                Cmd("quit"); br.WaitForExit(15000); Ok(br.HasExited, "PC A: quit");
                br = launch(pcA, ""); Ok(up(), "PC A: up again");
                st = Wait(state, s => B(s, "loaded") && I(s, "barItems") > 0, 10000);
                Ok(B(st, "barShown") && I(st, "barItems") == barBefore + 3, "the bookmarks bar is back after a restart (" + I(st, "barItems") + " items)");
                var countA = I(st, "count");
                Cmd("quit"); br.WaitForExit(15000);

                // ---- PC B: a fresh profile joins the same folder with the same password
                br = launch(pcB, Lh + "/bravo.html"); Ok(up(), "PC B: browser up (another test profile)");
                Wait(state, s => B(s, "loaded"), 10000);
                Ok(I(state(), "count") == 0, "PC B starts with no bookmarks");
                Cmd("test-m-sync-set " + syncFolder + " integration secret 42");
                var s2 = J(SyncNowCmd());
                st = Wait(state, s => I(s, "count") == countA, 8000);
                Ok(Str(s2, "state") == "synced" && I(s2, "incoming") > 0 && I(st, "count") == countA, "PC B got PC A's " + countA + " bookmarks through the folder");
                Ok(Tree(st).Any(t => t.StartsWith("bar/Raid night|bravo page|")), "...with the folders and order");
                Ok(B(st, "barSetting"), "...and PC A's settings (the bookmarks bar is on)");
                var setB = J(Cmd("test-m-settings"));
                Ok(setB.ContainsKey("pinned") && ((System.Collections.ArrayList)setB["pinned"]).Count == 1, "...and the pinned new tab tile");
                // B changes something, A picks it up later
                var bid = Val(Cmd("test-m-bm-add bar " + Lh + "/delta.html From PC B"));
                var s3 = J(SyncNowCmd());
                Ok(Str(s3, "state") == "synced" && B(s3, "wrote"), "PC B's change written to the folder");
                Cmd("test-m-sync-set off");
                Cmd("quit"); br.WaitForExit(15000);
                br = launch(pcA, ""); Ok(up(), "PC A: up again");
                Wait(state, s => B(s, "loaded"), 10000);
                var s4 = J(SyncNowCmd());
                st = Wait(state, s => Tree(s).Any(t => t.Contains("|From PC B|")), 8000);
                Ok(Tree(st).Any(t => t.Contains("|From PC B|")), "PC A got PC B's new bookmark");
                var cr = Path.Combine(pcA, "crashes"); var crB = Path.Combine(pcB, "crashes");
                Ok((!Directory.Exists(cr) || Directory.GetFiles(cr).Length == 0) && (!Directory.Exists(crB) || Directory.GetFiles(crB).Length == 0), "no crash reports");
                var fakeFiles = Directory.GetFiles(fake, "*", SearchOption.AllDirectories).Length;
                Ok(fakeFiles == fakeCount && fakeCount == 15, "the fake browser profiles were left as they were (" + fakeFiles + " files)");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                srv.Stop();
                if (work != dir) { System.Threading.Thread.Sleep(2000); try { Directory.Delete(work, true); } catch { } }
                var msg = fail == 0 ? "INTEGRATION movein: ALL " + pass + " PASSED" : "INTEGRATION movein: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
