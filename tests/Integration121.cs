// Hallow Browser 1.21 - integration test "v121": a new tab page puts the caret in the address bar (and doesn't when switched off).
// Off-screen like the others (muted, its own test folder). Off-screen windows never own the keyboard, so the test checks the
// browser asked for the address bar (the same call Ctrl+L makes), not where Windows' keyboard focus ended up.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v121
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration121
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail;
        static void Ok(bool c, string what) { if (c) pass++; else fail++; Console.WriteLine((c ? "ok   " : "FAIL ") + what); }
        static string Cmd(string line, int ms = 30000) { return Family.Send("Browser", line, ms); }
        static Dictionary<string, object> J(string r) { try { return js.Deserialize<Dictionary<string, object>>(r != null && r.StartsWith("ok ") ? r.Substring(3) : "{}"); } catch { return new Dictionary<string, object>(); } }
        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe); var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            var t0 = DateTime.Now; Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, "--mute-audio") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run; psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                br = Process.Start(psi);
                string p = null; for (int i = 0; i < 100 && (p == null || !p.StartsWith("ok")); i++) { Thread.Sleep(250); p = Cmd("ping", 2000); }
                Ok(p != null && p.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + p);
                var a = J(Cmd("test-x-newtab-focus"));
                Ok(a.ContainsKey("on") && (bool)a["on"] && Convert.ToInt32(a["requests"]) == 1, "on by default: a new tab asks for the address bar once: " + js.Serialize(a));
                Cmd("test-x-set newtab-page-focus true");
                var b = J(Cmd("test-x-newtab-focus"));
                Ok(b.ContainsKey("on") && !(bool)b["on"] && Convert.ToInt32(b["requests"]) == 0, "switched off: the page keeps focus: " + js.Serialize(b));
                Cmd("test-x-set newtab-page-focus false");
            }
            catch (Exception e) { Ok(false, "crashed: " + e.Message); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                foreach (var pr in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(pr.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && pr.StartTime >= t0) pr.Kill(); } catch { } }
            }
            Console.WriteLine(pass + " passed, " + fail + " failed"); return fail == 0 ? 0 : 1;
        }
    }
}
