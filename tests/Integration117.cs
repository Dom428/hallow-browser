// Hallow Browser 1.17 - integration test "v117", off-screen like the others (muted, its own test folder, LOCAL pages only).
//  - the address bar's list: Down / Down / Up move the highlight one row at a time (before: the row's text in the box rebuilt the
//    list, so each press started over, and the text box ate Up / Down before the list saw them), the box shows the row, Esc goes
//    back to what you typed, Enter opens the highlighted row;
//  - magnet links: a page's magnet: link goes to Hallow Torrents (recorded only - tests never start anything) instead of Windows'
//    magnet app, unless "Open magnet links in Hallow Torrents" is off.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v117
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
    public static class Integration117
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-v-weval 0 " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Cmd("test-mute " + i); Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Cmd("test-mute " + i); return i; }
        static Dictionary<string, object> J(string reply) { try { return js.Deserialize<Dictionary<string, object>>(Val(reply) ?? "{}"); } catch { return new Dictionary<string, object>(); } }
        static Tuple<int, string, byte[]> T(string type, string text, int code = 200) { return Tuple.Create(code, type, Encoding.UTF8.GetBytes(text)); }
        static int I(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v117.txt"), false) { AutoFlush = true };
            const string Mag = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Test+file";
            var srv = new Integration110.Server(); string U = null;
            srv.Route = rq =>
            {
                var pure = rq.Path.Split('?')[0];
                if (pure == "/mag") return T("text/html; charset=utf-8", "<!DOCTYPE html><html><body><a id='m' href='" + Mag + "'>Get magnet link</a></body></html>");
                if (pure.StartsWith("/page")) return T("text/html; charset=utf-8", "<!DOCTYPE html><html><head><title>" + pure.Substring(1) + "</title></head><body>" + pure + "</body></html>");
                return T("text/plain", "not found", 404);
            };
            srv.Start(); U = "http://127.0.0.1:" + srv.Port;
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                Cmd("test-mute 0");

                // ---- the address bar: history rows to move through
                foreach (var p in new[] { "/page-alpha", "/page-alps", "/page-altitude" }) { Cmd("test-x-omnikeys Enter " + U + p); Thread.Sleep(1200); }   // typed addresses are what the list suggests
                var typed = U.Substring(7) + "/page-al";   // "127.0.0.1:port/page-al"
                var r0 = J(Cmd("test-x-omnikeys Down " + typed));
                var rows = (r0.ContainsKey("rows") ? ((System.Collections.ArrayList)r0["rows"]).Cast<object>().Select(x => x.ToString()).ToList() : new List<string>());
                Ok(rows.Count >= 3, "the list has rows for what's typed: " + string.Join(" | ", rows));
                Ok(I(r0, "index") == 0 && S(r0, "text") == rows[0].Substring(rows[0].IndexOf(':') + 1), "Down: the first row is highlighted and its text is in the box: " + js.Serialize(r0["steps"]));
                var r1 = J(Cmd("test-x-omnikeys Down,Down,Up " + typed));
                Ok(I(r1, "index") == 1 && (bool)r1["open"], "Down, Down, Up: the second row - each press moves one row (before: every press started over at the first): " + js.Serialize(r1["steps"]));
                var steps = (System.Collections.ArrayList)r1["steps"];
                Ok(steps.Cast<Dictionary<string, object>>().All(s => (bool)s["handled"]), "the list takes Up / Down before the text box does");
                var r2 = J(Cmd("test-x-omnikeys Down,Down,Escape " + typed));
                Ok(I(r2, "index") == -1 && S(r2, "text") == typed && (bool)r2["open"], "Esc after moving: back to what you typed, the list stays open: " + js.Serialize(r2["steps"]));
                var pick = rows.Count > 1 ? rows[1].Substring(rows[1].IndexOf(':') + 1) : "";
                var r3 = J(Cmd("test-x-omnikeys Down,Down,Enter " + typed));
                var went = Wait(Tabs, t => t.Any(x => (bool)x["active"] && (x["url"] ?? "").ToString().Contains(pick.Substring(pick.LastIndexOf('/')))), 10000);
                Ok(!(bool)r3["open"] && went.Any(x => (bool)x["active"] && (x["url"] ?? "").ToString().Contains(pick.Substring(pick.LastIndexOf('/')))), "Enter opens the highlighted row (" + pick + ")");

                // ---- magnet links
                var m0 = J(Cmd("test-x-magnets"));
                Ok(I(m0, "handed") == 0 && S(m0, "toWindows") == "False", "\"Open magnet links in Hallow Torrents\" is on by default, nothing handed yet");
                int mt = NewTab(U + "/mag");
                Eval(mt, "(function(){document.getElementById('m').click();return 'ok'})()");
                var m1 = Wait(() => J(Cmd("test-x-magnets")), d => I(d, "handed") >= 1, 8000);
                Ok(I(m1, "handed") == 1 && S(m1, "last") == Mag, "a page's magnet link goes to Hallow Torrents: " + js.Serialize(m1));
                Ok(Tabs().Any(x => (x["url"] ?? "").ToString().EndsWith("/mag")), "the page stays where it was");
                // switched off, the link would go to Windows' magnet app (a real program on this PC) - so only the switch is checked, nothing is clicked
                Cmd("test-x-set magnets-windows true");
                var m2 = J(Cmd("test-x-magnets"));
                Ok(S(m2, "toWindows") == "True" && I(m2, "handed") == 1, "the switch turns off (no click: Windows' magnet app is never started from a test): " + js.Serialize(m2));
                Cmd("test-x-set magnets-windows false");
            }
            catch (Exception e) { Ok(false, "crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                try { srv.Stop(); } catch { }
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
            }
            Console.WriteLine(pass + " passed, " + fail + " failed"); if (report != null) { report.WriteLine(pass + " passed, " + fail + " failed"); report.Dispose(); }
            return fail == 0 ? 0 : 1;
        }
    }
}
