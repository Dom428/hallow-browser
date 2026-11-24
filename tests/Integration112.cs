// Hallow Browser 1.12 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own
// test folder, engine and pipes, local pages and files only. What Windows hands the browser: a second start with a local file (spaces,
// #, unicode; quoted, unquoted and the ProgIDs' "--single-argument %1"), a PDF / SVG / WebP, addresses and junk - each opening as a
// tab in the running window (no second window) or refused, never searched. The default-browser card and the new tab page's hint
// (the default stood in by HALLOW_BROWSER_DEFAULT_PROGID; Windows Settings is never opened - the browser records what it would
// open), pictures of the hint in both looks, and the registration command line against a sandbox key under
// HKCU\Software\HallowBrowserTests (deleted after). The real HKCU registration and the user's UserChoice are compared before / after.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v112
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace HallowBrowser.Tests
{
    public static class Integration112
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null || v == "null" ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : v; } catch { return v; } }
        static bool Ready(int i) { var t = Tabs(); return i < t.Count && (bool)t[i]["ready"]; }

        static string exe, run;
        // a second start of the browser, like Windows starts it for a link or a file (same test folder = the same running browser)
        static int Second(string arguments, out long ms)
        {
            var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = false };
            psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run; psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
            psi.EnvironmentVariables["HALLOW_BROWSER_DEFAULT_PROGID"] = "BraveHTML";
            var sw = Stopwatch.StartNew(); var p = Process.Start(psi);
            if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } ms = sw.ElapsedMilliseconds; return -1; }
            ms = sw.ElapsedMilliseconds; return p.ExitCode;
        }
        static int Cli(string arguments, string regRoot)
        {
            var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = false };
            psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
            if (regRoot != null) psi.EnvironmentVariables["HALLOW_BROWSER_REG_ROOT"] = regRoot;
            var p = Process.Start(psi); if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return -1; }
            return p.ExitCode;
        }
        static int OurProcesses() { int n = 0; foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) n++; } catch { } } return n; }
        // opened as the last tab? -> its index (or -1)
        static int Opened(int before, string url, string what)
        {
            var t = Wait(Tabs, x => x.Count > before, 10000);
            if (t.Count <= before) { Ok(false, what + ": a new tab (" + t.Count + " tabs)"); return -1; }
            int i = t.Count - 1; var got = S(t[i], "url");
            Ok(got == url, what + ": opened as a new tab at " + got + (got == url ? "" : " (expected " + url + ")"));
            Wait(() => Ready(i), x => x, 15000); Thread.Sleep(300);
            return i;
        }

        // a real (tiny) PDF: one page with "Hallow PDF" on it, correct cross-reference offsets
        static byte[] Pdf()
        {
            var objs = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
                null, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
            var stream = "BT /F1 24 Tf 40 70 Td (Hallow PDF) Tj ET"; objs[3] = "<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream";
            var sb = new StringBuilder("%PDF-1.4\n"); var offs = new List<int>();
            for (int i = 0; i < objs.Length; i++) { offs.Add(sb.Length); sb.Append((i + 1) + " 0 obj\n" + objs[i] + "\nendobj\n"); }
            int x = sb.Length; sb.Append("xref\n0 " + (objs.Length + 1) + "\n0000000000 65535 f \n");
            foreach (var o in offs) sb.Append(o.ToString("D10") + " 00000 n \n");
            sb.Append("trailer\n<< /Size " + (objs.Length + 1) + " /Root 1 0 R >>\nstartxref\n" + x + "\n%%EOF\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }
        // the smallest lossless WebP (1x1)
        static readonly byte[] Webp = Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==");

        static string RealSnapshot()
        {
            var parts = new List<string>();
            foreach (var p in new[] { @"Software\Classes\HallowBrowserURL\shell\open\command", @"Software\Classes\HallowBrowserHTML\shell\open\command", DefaultBrowser.ClientKey + @"\shell\open\command", DefaultBrowser.ClientKey + @"\InstallInfo" })
                using (var k = Registry.CurrentUser.OpenSubKey(p)) parts.Add(p + "=" + (k == null ? "-" : string.Join(",", k.GetValueNames().OrderBy(n => n).Select(n => n + ":" + k.GetValue(n)))));
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications")) parts.Add("ra=" + (k == null ? "-" : k.GetValue(DefaultBrowser.AppName) as string));
            foreach (var pr in new[] { "http", "https" })
                foreach (var n in new[] { @"\UserChoice", @"\UserChoiceLatest", @"\UserChoiceLatest\ProgId" })
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + pr + n))
                        parts.Add(pr + n + "=" + (k == null ? "-" : string.Join(",", k.GetValueNames().OrderBy(x => x).Select(x => x + ":" + k.GetValue(x)))));
            foreach (var ext in new[] { ".html", ".htm", ".pdf" })
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext + @"\UserChoice")) parts.Add(ext + "=" + (k == null ? "-" : k.GetValue("ProgId") + "/" + k.GetValue("Hash")));
            return string.Join("|", parts);
        }

        public static int Main(string exePath, string dir)
        {
            exe = Path.GetFullPath(exePath);
            run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v112.txt"), false) { AutoFlush = true };
            var shots = Path.Combine(dir, "..", "shots-v112"); Directory.CreateDirectory(shots);
            var realBefore = RealSnapshot();
            var srv = new TinyServer();
            srv.Route = (path, h) => path.StartsWith("/page") ? Tuple.Create("text/html", "<!DOCTYPE html><title>Served " + path.Substring(1).Split('?')[0] + "</title><h1>served</h1>", "") : Tuple.Create<string, string, string>(null, "not found", "");
            srv.Start(); var U = "http://127.0.0.1:" + srv.Port;
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            // the local files: spaces, #, %, unicode in the folder and the names
            var files = Path.Combine(dir, "My Files #1 üñïçødé €"); Directory.CreateDirectory(files);
            var html = Path.Combine(files, "page #2 100% café.html");
            File.WriteAllText(html, "<!DOCTYPE html><meta charset=utf-8><title>Local page é</title><h1>local</h1>", new UTF8Encoding(false));
            var pdf = Path.Combine(files, "doc ü.pdf"); File.WriteAllBytes(pdf, Pdf());
            var svg = Path.Combine(files, "logo.svg"); File.WriteAllText(svg, "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><title>Hallow SVG</title><circle cx='20' cy='20' r='18' fill='#F07820'/></svg>");
            var webp = Path.Combine(files, "pic.webp"); File.WriteAllBytes(webp, Webp);
            var sandbox = @"Software\HallowBrowserTests\it-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, U + "/page1") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                psi.EnvironmentVariables["HALLOW_BROWSER_DEFAULT_PROGID"] = "BraveHTML";   // the default browser, stood in (nothing is read from Windows' choice here)
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), x => x != null && x.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                var st = J(Cmd("test-d-state"));
                Ok(S(st, "start") == "skip: test", "start-up: in a test folder the browser never touches the registration (" + S(st, "start") + ")");

                // ================= a second start with a file / address: a new tab in this window, no second window
                long ms; int n0 = Tabs().Count;
                var code = Second("\"" + html + "\"", out ms);
                Ok(code == 0 && ms < 15000, "second start (quoted path with spaces, #, %, unicode): handed over and exited (exit " + code + ", " + ms + " ms)");
                var want = DefaultBrowser.FileUri(html);
                int ti = Opened(n0, want, "local file");
                Ok(want.StartsWith("file:///") && want.Contains("%23") && want.Contains("%25") && want.Contains("%C3%A9") && want.Contains("%20") && new Uri(want).LocalPath == html, "local file: the address is exact (" + want + ")");
                if (ti >= 0) Ok(Eval(ti, "document.title") == "Local page é", "local file: the page loaded (title " + Eval(ti, "document.title") + ")");
                Ok(Wait(OurProcesses, c => c == 1, 5000) == 1, "one browser process: the second start didn't make a second window or stay running");

                // what the ProgIDs pass: --single-argument %1 (unquoted; Windows may hand over a path with spaces)
                n0 = Tabs().Count; code = Second("--single-argument " + html, out ms);
                Ok(code == 0, "--single-argument with an unquoted path (exit " + code + ")");
                Opened(n0, want, "--single-argument path");
                n0 = Tabs().Count; code = Second("--single-argument " + U + "/page2?a=1&b=2 --cmd quit", out ms);
                Ok(code == 0, "--single-argument with an address followed by switches (exit " + code + ")");
                int ai = Opened(n0, U + "/page2?a=1&b=2%20--cmd%20quit", "--single-argument address (the switches stay part of it)");
                Ok(Cmd("ping", 3000) != null, "--single-argument: \"--cmd quit\" in a link did NOT quit the browser");
                n0 = Tabs().Count; code = Second(U + "/page3", out ms); Opened(n0, U + "/page3", "an http address");
                n0 = Tabs().Count; code = Second(html, out ms);   // no quotes at all: split at its spaces by Windows, joined back
                Ok(code == 0, "an unquoted path (split into several arguments) (exit " + code + ")");
                Opened(n0, want, "unquoted path joined back");

                // junk: refused - nothing opened, nothing searched
                n0 = Tabs().Count; var hits0 = srv.Log.Count;
                code = Second("javascript:alert(1) \"how to cook rice\" cats data:text/html,x mailto:a@b.c hallow://settings", out ms);
                Thread.Sleep(1500); var tj = Tabs();
                Ok(code == 0 && tj.Count == n0 && !tj.Any(x => ((S(x, "url") ?? "").Contains("duckduckgo") || (S(x, "url") ?? "").Contains("google.com/search"))), "junk arguments: refused, no tab, no search (" + tj.Count + " tabs)");

                // PDF (WebView2's own PDF viewer), SVG, WebP
                n0 = Tabs().Count; Second("\"" + pdf + "\"", out ms);
                int pi = Opened(n0, DefaultBrowser.FileUri(pdf), "PDF");
                Thread.Sleep(1500);
                var dls = Directory.Exists(Path.Combine(run, "Downloads")) ? Directory.GetFiles(Path.Combine(run, "Downloads")).Length : 0;
                Ok(dls == 0, "PDF: shown in the tab, not downloaded (" + dls + " files in the test downloads folder)");
                if (pi >= 0) { var ct = Eval(pi, "document.contentType"); Note("PDF tab: document.contentType = " + ct + ", title = " + Eval(pi, "document.title")); Ok(ct == "application/pdf", "PDF: the tab shows a PDF document (WebView2's PDF viewer)"); Cmd("test-capture " + pi + " " + Path.Combine(shots, "pdf-tab.png")); }
                n0 = Tabs().Count; Second("\"" + svg + "\"", out ms);
                int si = Opened(n0, DefaultBrowser.FileUri(svg), "SVG");
                if (si >= 0) Ok(Eval(si, "document.documentElement.tagName") == "svg", "SVG: shown as an SVG document");
                n0 = Tabs().Count; Second("\"" + webp + "\"", out ms);
                int wi = Opened(n0, DefaultBrowser.FileUri(webp), "WebP");
                if (wi >= 0) Ok(Eval(wi, "document.images.length===1&&document.images[0].naturalWidth===1") == "true", "WebP: shown as an image");

                // the pipe's open: file addresses yes, other schemes no
                Ok((Cmd("open " + want) ?? "").StartsWith("ok"), "pipe: open <file address> accepted");
                Ok((Cmd("open javascript:alert(1)") ?? "").StartsWith("error"), "pipe: open javascript: refused");

                // ================= Settings > General > Default browser
                var card = Val(Cmd("test-d-card")) ?? "";
                Ok(card.Contains("Your default browser is Brave.") && card.Contains("Make Hallow my default browser") && card.Contains("Set default") && card.Contains("Import from Brave"), "settings card: status (Brave), the button, what to click, Import from Brave");
                Cmd("test-d-clear"); var made = Val(Cmd("test-d-make"));
                Ok(made == DefaultBrowser.SettingsUri, "settings card: the button goes to " + made + " (recorded - Windows Settings is never opened in a test)");

                // ================= the new tab page's hint
                Cmd("test-m-nt-refresh"); Thread.Sleep(600);
                var nt = J(Cmd("test-m-nt-data")); var dh = nt == null ? null : nt["defaultHint"] as Dictionary<string, object>;
                Ok(dh != null && S(dh, "name") == "Brave", "hint: in the new tab data while Brave is the default");
                var r = Val(Cmd("test-new ")); int ni; int.TryParse(r, out ni);
                Wait(() => Ready(ni), x => x, 15000); Thread.Sleep(800);
                var hintText = Wait(() => Eval(ni, "document.getElementById('dh').textContent"), x => !string.IsNullOrEmpty(x) && x != "null" && x.Contains("Brave"), 6000);
                Ok(hintText != null && hintText.Contains("Make Hallow Browser your default browser?") && hintText.Contains("Brave"), "hint: shown on the new tab page (" + hintText + ")");
                Cmd("test-capture " + ni + " " + Path.Combine(shots, "newtab-hint.png"));
                Ok(Val(Cmd("test-d-look light")) == "light", "look: light");
                Eval(ni, "location.reload()"); Thread.Sleep(1500); Wait(() => Eval(ni, "document.getElementById('dh').textContent"), x => x != null && x.Contains("Brave"), 6000);
                Cmd("test-capture " + ni + " " + Path.Combine(shots, "newtab-hint-light.png"));
                Cmd("test-d-look dark"); Eval(ni, "location.reload()"); Thread.Sleep(1500);
                Wait(() => Eval(ni, "document.getElementById('dh').textContent"), x => x != null && x.Contains("Brave"), 6000);
                Ok(File.Exists(Path.Combine(shots, "newtab-hint.png")) && File.Exists(Path.Combine(shots, "newtab-hint-light.png")), "hint: pictures in dark and light");
                Cmd("test-d-clear");
                Eval(ni, "document.querySelector('#dh .go').click()");
                var op = Wait(() => J(Cmd("test-d-state")), x => S(x, "opened") != null, 5000);
                Ok(S(op, "opened") == DefaultBrowser.SettingsUri, "hint: its button goes to Windows' Default apps page for Hallow Browser (recorded)");
                Eval(ni, "document.querySelector('#dh .x').click()");
                var dis = Wait(() => J(Cmd("test-d-state")), x => B(x, "dismissed"), 5000);
                Ok(B(dis, "dismissed"), "hint: the X dismisses it (saved)");
                Thread.Sleep(800); Eval(ni, "location.reload()"); Thread.Sleep(1500);
                var after = Eval(ni, "document.getElementById('dh').textContent");
                Ok(after == "" && J(Cmd("test-m-nt-data"))["defaultHint"] == null, "hint: gone for good after a reload (" + after + ")");
                // never during a game
                Cmd("test-d-hint 1"); Thread.Sleep(500);
                File.WriteAllText(gameFile, "Fake Game"); Cmd("test-tick");
                var g = Wait(() => J(Cmd("test-d-state")), x => S(x, "game") != null, 15000);
                var ntg = J(Cmd("test-m-nt-data"));
                Ok(S(g, "game") != null && ntg != null && ntg["defaultHint"] == null, "hint: not while a game runs (game mode on)");
                File.WriteAllText(gameFile, ""); Cmd("test-tick"); Wait(() => J(Cmd("test-d-state")), x => S(x, "game") == null, 15000);
                Ok(J(Cmd("test-m-nt-data"))["defaultHint"] != null, "hint: back after the game (it was turned on again in Settings)");
                Cmd("test-d-hint 0");

                // ================= the registration command line, into a sandbox key (never HKCU's real keys)
                Ok(Cli("--register", null) == 3, "--register in a test folder without a sandbox root: refused (exit 3)");
                Ok(Cli("--register", @"Software\Classes") == 3, "--register with a root outside HKCU\\Software\\HallowBrowserTests: refused");
                Ok(Cli("--register", sandbox) == 0, "--register into the sandbox: exit 0");
                var sb = RealRegistry.Sandbox(sandbox);
                Ok(DefaultBrowser.Missing(sb, exe).Count == 0 && DefaultBrowser.RegisteredExe(sb) == exe, "--register: the sandbox holds the complete registration for this exe");
                Ok(Cli("--hide-icons", sandbox) == 0 && Equals(sb.Get(DefaultBrowser.ClientKey + @"\InstallInfo", "IconsVisible"), 0), "--hide-icons: IconsVisible 0");
                Ok(Cli("--show-icons", sandbox) == 0 && Equals(sb.Get(DefaultBrowser.ClientKey + @"\InstallInfo", "IconsVisible"), 1) && File.Exists(Path.Combine(run, @"Shortcuts\Desktop\Hallow Browser.lnk")) && File.Exists(Path.Combine(run, @"Shortcuts\Programs\Hallow Browser.lnk")), "--show-icons: IconsVisible 1, the shortcuts made (in the test folder)");
                Ok(Cli("--hide-icons", sandbox) == 0 && !File.Exists(Path.Combine(run, @"Shortcuts\Desktop\Hallow Browser.lnk")), "--hide-icons: our shortcut removed again");
                Ok(Cli("--register", sandbox) == 0 && Equals(sb.Get(DefaultBrowser.ClientKey + @"\InstallInfo", "IconsVisible"), 0), "--register again keeps the icons hidden");
                Ok(Cli("--unregister", sandbox) == 0 && !sb.Exists(@"Software\Classes\HallowBrowserURL") && !sb.Exists(DefaultBrowser.ClientKey) && sb.Get(@"Software\RegisteredApplications", "Hallow Browser") == null, "--unregister: everything of ours gone from the sandbox");

                Cmd("quit"); br.WaitForExit(15000); Ok(br.HasExited, "quit closes the browser");
            }
            catch (Exception e) { Ok(false, "v112 crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) br.Kill(); } catch { }
                srv.Stop();
                try { Registry.CurrentUser.DeleteSubKeyTree(sandbox, false); } catch { }
                try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\HallowBrowserTests")) if (k != null && k.SubKeyCount == 0 && k.ValueCount == 0) { k.Close(); Registry.CurrentUser.DeleteSubKey(@"Software\HallowBrowserTests", false); } } catch { }
            }
            Ok(RealSnapshot() == realBefore, "the real HKCU registration and the user's UserChoice (http, https, .html, .htm, .pdf) are exactly as before");
            using (var k = Registry.CurrentUser.OpenSubKey(sandbox)) Ok(k == null, "the sandbox key is deleted");
            Console.WriteLine(fail == 0 ? "ALL " + pass + " PASSED (v112)" : fail + " FAILED, " + pass + " passed (v112)");
            return fail == 0 ? 0 : 1;
        }
    }
}
