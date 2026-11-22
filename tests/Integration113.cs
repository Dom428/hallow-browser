// Hallow Browser 1.13 - integration test "v113", off-screen like the others (never shown, never focused), its own test folder and
// made-up data only (never the user's bookmarks, history or another browser's profile):
//  - the one-time bar repair at start: a test profile holding a 1.12-style import (Other > "Imported from Brave (Personal)" >
//    "Bookmarks bar" > 17 bookmarks) with the bar off -> the 17 on the bar, in order, the empty folders gone, the bar on, a backup;
//  - the menu: at most 14 entries on top, every 1.12 item reachable, every menu action in Ctrl+K, shortcuts in the submenus; opened at
//    768 px and 520 px high it fits or scrolls and Settings can be reached; a submenu opens beside it; the toolbar's Settings button;
//  - the History page with 5,000 made-up pages: fast, virtualized, grouped by day; search, remove one, clear a time range;
//  - importing a made-up Brave "Bookmarks" file: bar items onto the bar ("From Brave" folder when the bar has bookmarks), the toast's
//    "Show bookmarks" opening the manager at that folder;
//  - the new tab page's default-browser hint going away when Windows' answer changes to Hallow (a made-up answer) and coming back.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v113
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration113
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 60000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-v-weval 0 " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static object G(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o : null; }
        static double D(Dictionary<string, object> d, string k) { var o = G(d, k); return o == null ? -1 : Convert.ToDouble(o); }
        static bool B(Dictionary<string, object> d, string k) { var o = G(d, k); return o is bool && (bool)o; }
        static List<string> L(Dictionary<string, object> d, string k) { var a = G(d, k) as System.Collections.ArrayList; return a == null ? new List<string>() : a.Cast<object>().Select(x => x is string ? (string)x : js.Serialize(x)).ToList(); }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v113.txt"), false) { AutoFlush = true };
            // the test profile as a 1.12 import from Brave left it (made-up bookmarks), the bookmarks bar off
            var bmFile = Path.Combine(run, "bookmarks.json");
            var old = new BookmarkStore(bmFile); old.Load();
            var imp = old.AddFolder(BookmarkStore.OtherId, "Imported from Brave (Personal)"); var obar = old.AddFolder(imp.Id, "Bookmarks bar");
            for (int i = 0; i < 17; i++) old.Add(obar.Id, "Made-up bar " + i, "https://madeup" + i + ".example/");
            old.Add(BookmarkStore.OtherId, "Kept elsewhere", "https://kept.example/"); old.Save();
            File.WriteAllText(Path.Combine(run, "settings.json"), "{\"BookmarksBar\":false,\"BookmarksBarHidden\":true}");
            var braveFile = Path.Combine(dir, "brave-bookmarks.json"); File.WriteAllText(braveFile, V113.BraveJson(5));
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_DEFAULT_PROGID"] = "BraveHTML";   // a made-up answer from Windows: Brave is the default
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1500);

                // ================= the one-time bar repair
                var tree = Wait(() => J(Cmd("test-h-bm-tree")), x => x != null && G(x, "repair") != null, 10000);
                var bar = L(tree, "bar"); var other = L(tree, "other");
                Ok(bar.Count == 17 && bar[0] == "Made-up bar 0" && bar[16] == "Made-up bar 16", "bar repair: the 17 bookmarks of the imported \"Bookmarks bar\" are on the bar, in order (" + bar.Count + "; " + G(tree, "repair") + ")");
                Ok(other.Count == 1 && other[0] == "Kept elsewhere" && D(tree, "count") == 18, "bar repair: the empty import folders are gone, the other bookmark stays, nothing lost (" + D(tree, "count") + " bookmarks)");
                Ok(B(tree, "barOn"), "bar repair: the bookmarks bar is switched on");
                var bk = Path.Combine(run, "bookmarks.before-1.13.json");
                Ok(File.Exists(bk) && File.ReadAllText(bk).Contains("Imported from Brave (Personal)"), "bar repair: bookmarks.json was backed up first");
                var st = File.ReadAllText(Path.Combine(run, "settings.json"));
                Ok(st.Contains("\"BarRepair113\":true"), "bar repair: it is marked done (runs once)");

                // ================= the menu
                var m = J(Cmd("test-h-model"));
                Note("menu top level: " + string.Join(" | ", L(m, "top")));
                Ok(D(m, "topEntries") <= 14 && D(m, "topEntries") >= 10, "menu: " + D(m, "topEntries") + " entries on the top level (at most 14), plus the zoom row");
                Ok(L(m, "missingInMenu").Count == 0, "menu: every item of the 1.12 menu is in the new menu (a submenu or the top): missing " + string.Join(", ", L(m, "missingInMenu")));
                Ok(L(m, "missing").Count == 0, "menu: ... and in the menu or Ctrl+K");
                Ok(L(m, "leavesNotInPalette").Count == 0, "menu: every menu action is in Ctrl+K: missing " + string.Join(", ", L(m, "leavesNotInPalette")));
                var keys = G(m, "keys") as Dictionary<string, object>;
                Ok(keys != null && (string)keys["Show all history"] == "Ctrl+H" && (string)keys["Bookmarks manager"] == "Ctrl+Shift+O" && (string)keys["Hot tabs (CPU and memory)"] == "Shift+Esc" && (string)keys["Settings"] == "Ctrl+," && (string)keys["Reopen closed tab"] == "Ctrl+Shift+T", "menu: the shortcuts are shown in the submenus (Ctrl+H, Ctrl+Shift+O, Shift+Esc, Ctrl+Shift+T) and Settings has Ctrl+,");
                var top = L(m, "top");
                Ok(top.Take(3).SequenceEqual(new[] { "New tab", "New window", "New private window" }) && top[top.Count - 1] == "Exit" && top[top.Count - 3] == "Settings" && top[top.Count - 2] == "Help & about >", "menu: new tab / window / private window on top; Settings, Help & about, Exit at the bottom");
                foreach (var h in new[] { 768, 520 })
                {
                    var o = J(Cmd("test-h-open " + h));
                    bool fits = !B(o, "scrollable");
                    Ok(o != null && D(o, "maxHeight") <= h && (fits || B(o, "settingsVisibleAfterScroll")) && B(o, "settingsVisibleAfterScroll"), "menu at " + h + " px: " + (fits ? "fits (" + D(o, "extent").ToString("0") + " px)" : "scrolls (" + D(o, "viewport").ToString("0") + " of " + D(o, "extent").ToString("0") + " px)") + " and Settings can be reached");
                    if (h == 520) Ok(B(o, "scrollable"), "menu at 520 px: it scrolls instead of running off the window");
                    Cmd("test-h-close");
                }
                var soRaw = Cmd("test-h-open 768 more"); if (!(soRaw ?? "").StartsWith("ok")) Note("submenu: " + soRaw);
                var so = J(soRaw);
                Ok(so != null && (string)G(so, "sub") == "more" && D(so, "subExtent") > 100, "menu: More tools opens as a submenu beside the menu (" + D(so, "subExtent").ToString("0") + " px)");
                Cmd("test-h-close");
                var hist = J(Cmd("test-h-open 768 history")); Cmd("test-h-close");
                Ok(hist != null && (string)G(hist, "sub") == "history", "menu: History opens its submenu");
                Ok((G(m, "settingsButton") as string) == "Settings (Ctrl+,)", "toolbar: a Settings (gear) button, one click to Settings");

                // ================= the History page
                var fill = Val(Cmd("test-h-history-fill 5000")) ?? "";
                Note("history filled: " + fill);
                var hp = J(Cmd("test-h-history-open"));
                Ok(hp != null && D(hp, "rows") >= 4990 && D(hp, "days") >= 55, "history page: " + D(hp, "rows") + " pages in " + D(hp, "days") + " days");
                Ok(D(hp, "buildMs") < 400 && D(hp, "shownMs") < 1500, "history page: 5,000 pages listed in " + D(hp, "buildMs").ToString("0") + " ms (on screen in " + D(hp, "shownMs").ToString("0") + " ms)");
                Ok(D(hp, "realized") > 0 && D(hp, "realized") < 80, "history page: virtualized - only " + D(hp, "realized") + " rows are drawn");
                Note("history page first rows: " + string.Join(" | ", L(hp, "first")));
                var hs = J(Cmd("test-h-history-search patch notes"));
                Ok(hs != null && D(hs, "rows") >= 600 && D(hs, "rows") <= 800 && D(hs, "ms") < 300, "history page: search finds " + D(hs, "rows") + " pages in " + D(hs, "ms").ToString("0") + " ms");
                Cmd("test-h-history-search ");
                var rm = Val(Cmd("test-h-history-remove https://site1.example/page/1")) ?? "";
                Ok(rm.StartsWith("True "), "history page: one page removed (" + rm + ")");
                var ch = (Val(Cmd("test-h-history-clear hour")) ?? "").Split(' ');
                Ok(ch.Length == 2 && int.Parse(ch[0]) >= 3 && int.Parse(ch[0]) <= 6, "history page: \"the last hour\" cleared " + ch[0] + " pages (" + ch[1] + " left)");
                var ca = (Val(Cmd("test-h-history-clear all")) ?? "").Split(' ');
                Ok(ca.Length == 2 && ca[1] == "0", "history page: \"all time\" clears everything (" + ca[0] + ")");

                // ================= importing (a made-up Brave Bookmarks file): the bar has 17 now -> a "From Brave" folder on it
                var im = J(Cmd("test-h-import-json " + braveFile));
                Ok(im != null && D(im, "onBar") == 7 && D(im, "inOther") == 3 && B(im, "barFolder"), "import: Brave's 7 bar items into a \"From Brave\" folder on the bar (it had bookmarks), 3 into Other bookmarks");
                var toast = (string)G(im, "toast") ?? "";
                Ok(toast.Contains("on your bookmarks bar") && toast.Contains("From Brave") && toast.Contains("Other bookmarks") && L(im, "actions").Contains("Show bookmarks"), "import: the toast says where they went and has \"Show bookmarks\": " + toast);
                var press = Val(Cmd("test-h-toast-press Show bookmarks")) ?? "";
                Ok(press.StartsWith("True hallow://bookmarks ") && !press.EndsWith(" bar") && !press.EndsWith(" other"), "import: \"Show bookmarks\" opens the manager at that folder (" + press + ")");
                var tree2 = J(Cmd("test-h-bm-tree"));
                Ok(L(tree2, "bar").Count == 18 && L(tree2, "bar")[17].Contains("From Brave"), "import: the bar is the 17 plus the \"From Brave\" folder");

                // ================= the default-browser hint follows Windows' answer
                var dataFile = Path.Combine(run, "Local", "newtab", "data.json");
                Cmd("test-new " + Omni.NewTabUrl); Thread.Sleep(1500);
                var nt = Tabs().FindIndex(t => (t["url"] as string ?? "").StartsWith(Omni.NewTabUrl));
                var d1 = Wait(() => File.Exists(dataFile) ? File.ReadAllText(dataFile) : "", x => x.Contains("\"defaultHint\":{"), 8000);
                Ok(d1.Contains("\"defaultHint\":{"), "default hint: shown while Windows says Brave is the default");
                var c1 = Val(Cmd("test-h-progid HallowBrowserURL"));
                var d2 = Wait(() => File.Exists(dataFile) ? File.ReadAllText(dataFile) : "", x => x.Contains("\"defaultHint\":null"), 8000);
                Ok(c1 == "True" && d2.Contains("\"defaultHint\":null"), "default hint: gone as soon as Windows says Hallow (UserChoiceLatest\\ProgId = HallowBrowserURL), checked again on focus");
                var shown = nt < 0 ? "?" : Wait(() => Eval(nt, "(document.getElementById('dh')||{}).textContent||''"), x => x == "", 6000);
                Ok(shown == "", "default hint: the open new tab page drops it too");
                var c2 = Val(Cmd("test-h-progid -"));
                var d3 = Wait(() => File.Exists(dataFile) ? File.ReadAllText(dataFile) : "", x => x.Contains("\"defaultHint\":{"), 8000);
                Ok(c2 == "True" && d3.Contains("\"defaultHint\":{"), "default hint: back if another browser becomes the default again");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                var msg = fail == 0 ? "INTEGRATION v113: ALL " + pass + " PASSED" : "INTEGRATION v113: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
