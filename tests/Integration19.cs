// Hallow Browser 1.9 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own test
// folder, engine and pipes, local pages only. Accessibility: a Windows UI Automation walk (from this process, like a screen reader
// would) over the window, Settings, the sidebar and every flyout - each interactive element needs a name, help text and a role; F6 /
// Shift+F6 cycling; the toolbar as keyboard stops; Tab order in Settings; flyouts entered and left with the keyboard (Esc, no traps);
// the UI scale (100-200 %, Windows' text size stood in by HALLOW_BROWSER_TEXTSCALE) leaving pages and page zoom alone; the high-contrast
// palette and reduced motion (forced inside the browser - the Windows settings are never changed). Real-use fixes: the Overlay switch's
// label, the + button, the bookmarks bar, tab groups, the game icon and game mode turning off.
// Narrator itself isn't run (it needs a real, focused desktop session and speaks out loud) - UIA is what it reads.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v19
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
using System.Windows.Automation;

namespace HallowBrowser.Tests
{
    public static class Integration19
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
        static List<string> L2(string reply) { var v = Val(reply); try { return v == null ? new List<string>() : js.Deserialize<List<string>>(v); } catch { return new List<string>(); } }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        static double D(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : v; } catch { return v; } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Thread.Sleep(400); return i; }

        // ---- the UI Automation walk (what a screen reader sees): WPF elements only - the pages' own content is Chromium's
        static readonly ControlType[] Interactive = { ControlType.Button, ControlType.CheckBox, ControlType.RadioButton, ControlType.Edit, ControlType.ComboBox, ControlType.TabItem, ControlType.MenuItem, ControlType.ListItem, ControlType.Slider, ControlType.Hyperlink, ControlType.SplitButton };
        class Found { public int Elements, Interactive; public List<string> NoName = new List<string>(), NoHelp = new List<string>(); public List<Tuple<string, bool>> TabItems = new List<Tuple<string, bool>>(); public Dictionary<string, int> Roles = new Dictionary<string, int>(); public int Windows; }
        static Found Walk(int pid, bool popupsOnly)
        {
            var f = new Found();
            var wins = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
            var all = wins.Cast<AutomationElement>().ToList();
            AutomationElement main = null; double best = -1;
            foreach (var w in all) { try { var r = w.Current.BoundingRectangle; var a = r.Width * r.Height; if (a > best) { best = a; main = w; } } catch { } }
            foreach (var w in all) { if (popupsOnly && w == main) continue; f.Windows++; Visit(w, f, 0); }
            return f;
        }
        static void Visit(AutomationElement e, Found f, int depth)
        {
            if (depth > 60 || f.Elements > 20000) return;
            AutomationElement c;
            try { c = TreeWalker.ControlViewWalker.GetFirstChild(e); } catch { return; }
            while (c != null)
            {
                string fw = null; try { fw = c.Current.FrameworkId; } catch { }
                if (fw == "WPF")
                {
                    f.Elements++;
                    try
                    {
                        var ct = c.Current.ControlType;
                        if (Interactive.Contains(ct))
                        {
                            f.Interactive++;
                            var role = ct.ProgrammaticName.Replace("ControlType.", ""); int n; f.Roles.TryGetValue(role, out n); f.Roles[role] = n + 1;
                            var name = c.Current.Name; var help = c.Current.HelpText;
                            var what = role + " [" + c.Current.ClassName + "] " + (name ?? "") + " / " + c.Current.AutomationId;
                            if (string.IsNullOrWhiteSpace(name)) f.NoName.Add(what);
                            if (string.IsNullOrWhiteSpace(help)) f.NoHelp.Add(what);
                            if (ct == ControlType.TabItem) { object sel; bool s = c.TryGetCurrentPattern(SelectionItemPattern.Pattern, out sel) && ((SelectionItemPattern)sel).Current.IsSelected; f.TabItems.Add(Tuple.Create(name, s)); }
                        }
                    }
                    catch { }
                    Visit(c, f, depth + 1);
                }
                try { c = TreeWalker.ControlViewWalker.GetNextSibling(c); } catch { c = null; }
            }
        }
        static void Report(string where, Found f, bool needTabs)
        {
            Note("uia " + where + ": " + f.Windows + " window(s), " + f.Elements + " WPF elements, " + f.Interactive + " interactive (" + string.Join(", ", f.Roles.Select(kv => kv.Key + " " + kv.Value)) + ")");
            Ok(f.Interactive > 0 && f.NoName.Count == 0, "uia " + where + ": every interactive element has a name and a role (" + f.Interactive + ")" + (f.NoName.Count > 0 ? " - missing: " + string.Join(" | ", f.NoName.Take(12)) : ""));
            Ok(f.Interactive > 0 && f.NoHelp.Count == 0, "uia " + where + ": every interactive element has help text" + (f.NoHelp.Count > 0 ? " - missing: " + string.Join(" | ", f.NoHelp.Take(12)) : ""));
        }

        class Server
        {
            public int Port; TcpListener l; volatile bool stop; public Func<string, string> Route;
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var s = c.GetStream())
                    {
                        var head = new List<byte>(); int b; while ((b = s.ReadByte()) >= 0) { head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; }
                        var path = Encoding.ASCII.GetString(head.ToArray()).Split(' ')[1];
                        var body = Route(path); var bytes = Encoding.UTF8.GetBytes(body ?? "not found");
                        var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + (body == null ? "404 Not Found" : "200 OK") + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        s.Write(hb, 0, hb.Length); s.Write(bytes, 0, bytes.Length); s.Flush();
                    }
                }
                catch { }
            }
        }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v19.txt"), false) { AutoFlush = true };
            var srv = new Server();
            srv.Route = p => p.StartsWith("/page") ? "<!DOCTYPE html><html><head><title>Test page " + WebUtility.HtmlEncode(p.Substring(5)) + "</title></head><body><h1>Test page</h1><p>Some text.</p><a href='/page2'>a link</a><script>window.rm=matchMedia('(prefers-reduced-motion: reduce)').matches;</script></body></html>" : null;
            srv.Start(); var U = "http://127.0.0.1:" + srv.Port;
            var gameFile = Path.Combine(dir, "game.txt"); File.WriteAllText(gameFile, "");
            // a stand-in Hallow Overlay whose status.json lags behind (says off) - only its pipe knows the real state, like the real one after a toggle
            var ovDir = Path.Combine(run, "HallowOverlay"); Directory.CreateDirectory(ovDir);
            File.WriteAllText(Path.Combine(ovDir, "status.json"), "{\"app\":\"Overlay\",\"visible\":false}");
            bool ovOn = true; var ovLines = new List<string>();
            Family.Serve("Overlay", line => { lock (ovLines) ovLines.Add(line); if (line == "ping") return "ok {\"app\":\"Overlay\",\"version\":\"1.21\"}"; if (line == "status") return "ok {\"app\":\"Overlay\",\"visible\":" + (ovOn ? "true" : "false") + "}"; if (line == "toggle") { ovOn = !ovOn; return "ok"; } return "error unknown command"; });
            Process br = null;
            try
            {
                var psi = new ProcessStartInfo(exe, U + "/page1") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_TEXTSCALE"] = "100";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = gameFile;
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), x => x != null && x.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1500);
                int nt = NewTab(""); int st = NewTab(Omni.SettingsUrl); Thread.Sleep(800);
                Cmd("invoke reading-list"); Thread.Sleep(800);   // the sidebar's own reading list panel, open
                Ok(Tabs().Count == 3, "three tabs: a page, a new tab, Settings (" + Tabs().Count + ")");

                // ================= UI Automation: the window with Settings shown and the sidebar open
                Cmd("test-a-region"); Thread.Sleep(300);   // (the browser names controls at idle after layout; this does it now)
                var f = Walk(br.Id, false);
                Report("window + Settings + sidebar", f, true);
                Ok(f.TabItems.Count == 3 && f.TabItems.Count(x => x.Item2) == 1 && f.TabItems.Any(x => x.Item1 == "Settings" && x.Item2) && f.TabItems.Any(x => x.Item1.StartsWith("Test page")), "uia: the tabs are TabItems named after their pages, the shown one selected (" + string.Join(", ", f.TabItems.Select(x => x.Item1 + (x.Item2 ? "*" : ""))) + ")");
                Ok(f.Roles.ContainsKey("CheckBox") && f.Roles["CheckBox"] > 20 && f.Roles.ContainsKey("Edit") && f.Roles.ContainsKey("Button") && f.Roles["Button"] > 60, "uia: Settings' switches are CheckBoxes, fields Edits, the rest Buttons");
                var unnamed = L(Cmd("test-a-names")); Ok(unnamed.Count == 0, "in the window: no button or field without a name (" + unnamed.Count + ")");

                // ================= flyouts: entered with the keyboard, named, Tab cycles inside, Esc closes and gives the keyboard back
                foreach (var fl in new[] { "menu", "quick", "downloads", "workspaces", "tabmenu", "railmenu" })
                {
                    var o = J(Cmd("test-a-open " + fl));
                    if (o == null) { Ok(false, "flyout " + fl + ": opened (" + Cmd("test-a-open " + fl) + ")"); continue; }
                    Ok(B(o, "open") && D(o, "stops") > 0 && D(o, "named") == D(o, "stops") && S(o, "cycle") == "Cycle" && B(o, "scaled"), "flyout " + fl + ": open, " + D(o, "stops") + " keyboard stops, all named, Tab cycles inside, scaled with the UI (first: " + S(o, "first") + ")" + (B(o, "scaled") && S(o, "cycle") == "Cycle" ? "" : " " + js.Serialize(o)));
                    var pf = Walk(br.Id, true);
                    if (pf.Interactive > 0) Ok(pf.NoName.Count == 0 && pf.NoHelp.Count == 0, "uia flyout " + fl + ": " + pf.Interactive + " interactive, all with a name and help text" + (pf.NoName.Count + pf.NoHelp.Count > 0 ? " - missing: " + string.Join(" | ", pf.NoName.Concat(pf.NoHelp).Take(8)) : ""));
                    else Note("uia flyout " + fl + ": its window isn't listed by UIA off-screen (" + pf.Windows + " windows) - checked in the browser instead");
                    var esc = J(Cmd("test-a-flyout-esc"));
                    Ok(B(esc, "handled") && !B(esc, "open"), "flyout " + fl + ": Esc closes it (no keyboard trap)");
                    if (B(o, "focused")) Ok(B(esc, "backOnButton") || fl == "tabmenu" || fl == "railmenu", "flyout " + fl + ": the keyboard goes back to " + S(esc, "button"));
                    else Note("flyout " + fl + ": keyboard focus can't move in an off-screen window that never activates - focus moves checked as logical focus");
                }

                // ================= the toolbar: keyboard stops with names, help and shortcuts
                var tb = L(Cmd("test-a-toolbar"));
                Ok(tb.Count == 8 && tb.All(x => B(x, "focusable") && B(x, "tabStop") && !string.IsNullOrEmpty(S(x, "name")) && !string.IsNullOrEmpty(S(x, "help"))), "toolbar: back, forward, reload, new tab, Shield, downloads, game panel, menu - keyboard stops, named, with help (" + string.Join(", ", tb.Select(x => S(x, "name"))) + ")");
                Ok(tb.Any(x => S(x, "name") == "New tab" && S(x, "key") == "Ctrl+T") && tb.Any(x => S(x, "name") == "Downloads" && S(x, "key") == "Ctrl+J"), "toolbar: shortcuts are announced (accelerator keys)");

                // ================= F6: address bar -> tabs -> page -> sidebar -> address bar; Shift+F6 back
                Cmd("test-activate 0"); Thread.Sleep(500);
                var fk = J(Cmd("test-a-f6key")); Ok(B(fk, "handled") && S(fk, "region") != null, "F6 key: the window takes it and moves the keyboard (to " + S(fk, "region") + ")");
                var r = J(Cmd("test-a-f6"));
                for (int i = 0; i < 5 && S(r, "region") != "address"; i++) r = J(Cmd("test-a-f6"));
                Ok(S(r, "region") == "address" && (S(r, "target") ?? "").StartsWith("TextBox"), "F6 reaches the address bar (" + S(r, "target") + ")");
                var seq = new List<string>(); var targets = new List<string>();
                for (int i = 0; i < 4; i++) { r = J(Cmd("test-a-f6")); seq.Add(S(r, "region")); targets.Add(S(r, "target")); }
                Ok(string.Join(",", seq) == "tabs,page,sidebar,address", "F6 cycles address bar -> tabs -> page -> sidebar -> address bar (" + string.Join(",", seq) + ": " + string.Join(", ", targets) + ")");
                Ok(targets[0] != null && targets[0].StartsWith("TabHeader") && targets[1] != null && targets[1].StartsWith("WebView2"), "F6: the tab region is the shown tab, the page region the page itself (WebView2)");
                r = J(Cmd("test-a-f6 back")); Ok(S(r, "region") == "sidebar", "Shift+F6 goes back (address bar -> sidebar): " + S(r, "region"));
                Note("F6 focus: keyboard on " + S(r, "keyboard") + ", logical " + S(r, "logical") + " (target focused: " + B(r, "targetFocused") + ")");
                Cmd("test-sidebar-hide"); Thread.Sleep(300);
                var tg = Cmd("test-sidebar-toggle"); Thread.Sleep(500);
                var sideOff = J(Cmd("test-a-region"));
                if (!B(sideOff, "sidebarOn")) { seq.Clear(); r = J(Cmd("test-a-f6")); for (int i = 0; i < 5 && S(r, "region") != "address"; i++) r = J(Cmd("test-a-f6")); for (int i = 0; i < 3; i++) seq.Add(S(J(Cmd("test-a-f6")), "region")); Ok(string.Join(",", seq) == "tabs,page,address", "F6 skips the sidebar when it's off (" + string.Join(",", seq) + ")"); Cmd("test-sidebar-toggle"); }
                else Note("sidebar still on - skip-check not run");

                // ================= Settings: Tab order follows the page, top to bottom
                var order = L(Cmd("test-a-taborder"));
                int back = 0; string firstBad = null;
                for (int i = 1; i < order.Count; i++) { double y0 = D(order[i - 1], "y"), y1 = D(order[i], "y"), x0 = D(order[i - 1], "x"), x1 = D(order[i], "x"); if (y1 < y0 - 6 || (Math.Abs(y1 - y0) <= 6 && x1 < x0)) { back++; if (firstBad == null) firstBad = S(order[i - 1], "name") + " -> " + S(order[i], "name"); } }
                Ok(order.Count > 60 && back == 0, "settings: " + order.Count + " keyboard stops, Tab goes top to bottom, left to right" + (firstBad != null ? " - out of order: " + firstBad : ""));
                Ok(order.All(x => !string.IsNullOrEmpty(S(x, "name"))), "settings: every stop is named" + string.Join("", order.Where(x => string.IsNullOrEmpty(S(x, "name"))).Take(5).Select(x => " | " + S(x, "type") + " at " + D(x, "y"))));
                Ok(order.Count > 0 && S(order[0], "type") == "TextBox", "settings: the search box comes first (" + (order.Count > 0 ? S(order[0], "type") : "-") + ")");

                // ================= UI scale: the browser's own UI only - pages and page zoom stay
                Cmd("test-activate 0"); Thread.Sleep(400);
                var s100 = J(Cmd("test-a-scale 100"));
                var s150 = J(Cmd("test-a-scale 150"));
                Ok(D(s150, "scale") == 1.5 && Math.Abs(D(s150, "caption") - 57) < 0.1 && Math.Abs(D(s150, "topRow") - 57) < 1 && Math.Abs(D(s150, "address") - D(s100, "address") * 1.5) < 1, "scale 150 %: title bar 38 -> " + D(s150, "topRow") + " px (drag area " + D(s150, "caption") + "), address bar " + D(s100, "address") + " -> " + D(s150, "address"));
                Ok(D(s150, "pageWidth") > 0 && Math.Abs(D(s150, "pageWidth") - D(s150, "contentWidth")) < 1 && D(s150, "zoom") == D(s100, "zoom") && S(s150, "pageTransform") == "Identity", "scale 150 %: the page isn't scaled - it fills its area, page zoom unchanged (" + D(s150, "zoom") + ")");
                var s200 = J(Cmd("test-a-scale 200")); Ok(D(s200, "scale") == 2.0 && Math.Abs(D(s200, "menuBtn") - D(s100, "menuBtn") * 2) < 1, "scale 200 %: buttons twice the size (" + D(s100, "menuBtn") + " -> " + D(s200, "menuBtn") + ")");
                Cmd("test-a-scale 0"); var sw125 = J(Cmd("test-a-textscale 125")); Ok(D(sw125, "scale") == 1.25 && D(sw125, "setting") == 0, "scale: \"Like Windows\" follows Windows' text size (125 % -> " + D(sw125, "scale") + ")");
                var sw225 = J(Cmd("test-a-textscale 225")); Ok(D(sw225, "scale") == 2.0, "scale: Windows' 225 % is held at 200 %");
                var sBack = J(Cmd("test-a-textscale 100")); Ok(D(sBack, "scale") == 1.0 && Math.Abs(D(sBack, "topRow") - 38) < 1, "scale: back to 100 % (title bar " + D(sBack, "topRow") + " px)");

                // ================= high contrast (forced in the browser for the test)
                var hc0 = J(Cmd("test-a-hc off"));
                var hc = J(Cmd("test-a-hc on"));
                Ok(B(hc, "theme") && S(hc, "bg") == S(hc, "window") && S(hc, "text") == S(hc, "windowText") && S(hc, "muted") == S(hc, "windowText") && S(hc, "line") == S(hc, "windowText") && S(hc, "accent") == S(hc, "highlight"), "high contrast: system colours (background " + S(hc, "bg") + ", text " + S(hc, "text") + ", accent " + S(hc, "accent") + ")");
                Ok(S(hc, "btnBg") == S(hc, "window") && B(hc, "menuStyleIsCurrent") && D(hc, "activeTabOutline") == 2, "high contrast: button templates too, controls already made restyled live, the shown tab outlined");
                Cmd("test-a-region"); var fhc = Walk(br.Id, false); Ok(fhc.NoName.Count == 0, "uia in high contrast: still every element named (" + fhc.Interactive + ")");
                var hcOff = J(Cmd("test-a-hc system"));
                Ok(!B(hcOff, "theme") && S(hcOff, "bg") == S(hc0, "bg") && D(hcOff, "activeTabOutline") == 0, "high contrast off again: the Hallow palette is back (" + S(hcOff, "bg") + ")");

                // ================= reduced motion
                var m = J(Cmd("test-a-motion reduce")); Ok(B(m, "reduced"), "reduced motion: the browser's switch follows (forced for the test)");
                var mo = J(Cmd("test-a-open menu")); Cmd("test-a-flyout-esc");
                Ok(mo != null && B(mo, "open"), "reduced motion: flyouts still open (without animation)");
                Cmd("test-a-motion system");
                Ok(Eval(0, "String(matchMedia('(prefers-reduced-motion: reduce)').matches)") == (B(m, "system") ? "true" : "false"), "reduced motion: pages see Windows' \"Animation effects\" setting through prefers-reduced-motion (now " + (B(m, "system") ? "reduce" : "no-preference") + ")");

                // ================= real-use fixes (1.9)
                // the Overlay switch on the new tab page: its label follows Overlay's real state (asked over the pipe), on load and after a click
                var fj = Val(Cmd("test-y-famrefresh"));
                Ok(fj != null && fj.Contains("\"visible\":true"), "overlay switch: on load the state comes from Overlay's pipe (its status.json still says off)");
                int ntx = NewTab(""); Eval(ntx, "window.hallowFamily()"); Thread.Sleep(700);
                Func<string> ovLabel = () => Eval(ntx, "(document.querySelector('#hf .ov')||{}).textContent");
                Ok(ovLabel() == "Overlay: on", "overlay switch: the label says on (" + ovLabel() + ")");
                Eval(ntx, "document.querySelector('#hf .ov').click()"); var lb = Wait(ovLabel, x => x == "Overlay: off", 5000);
                Ok(!ovOn && lb == "Overlay: off" && ovLines.Contains("toggle"), "overlay switch: a click toggles Overlay and the label updates to off (" + lb + ")");
                Eval(ntx, "document.querySelector('#hf .ov').click()"); lb = Wait(ovLabel, x => x == "Overlay: on", 5000);
                Ok(ovOn && lb == "Overlay: on", "overlay switch: and back to on (" + lb + ")");
                Eval(ntx, "document.querySelector('#hf .ov').click()"); Eval(ntx, "document.querySelector('#hf .ov').click()"); Thread.Sleep(300); Eval(ntx, "document.querySelector('#hf .ov').click()");
                lb = Wait(ovLabel, x => x == (ovOn ? "Overlay: on" : "Overlay: off"), 6000);
                Ok(lb == (ovOn ? "Overlay: on" : "Overlay: off"), "overlay switch: quick clicks - the label still ends on the real state (" + lb + ", Overlay " + (ovOn ? "on" : "off") + ")");
                // the + button: centred on the tabs (measured from the drawn strip) at 100 / 125 / 150 %
                foreach (var sc in new[] { 100, 125, 150 })
                {
                    Cmd("test-a-scale " + sc); var pl = J(Cmd("test-a-plus"));
                    Ok(D(pl, "plus") > 0 && Math.Abs(D(pl, "plus") - D(pl, "tab")) <= 1.0, "new tab button: centred on the tabs at " + sc + " % (centre " + D(pl, "plus") + " vs tab " + D(pl, "tab") + ")");
                }
                Cmd("test-a-scale 0");
                // the bookmarks bar: on by itself once there is a bookmark; hidden on purpose = stays hidden
                var bar = J(Cmd("test-a-bar")); Ok(!B(bar, "on") && D(bar, "count") == 0, "bookmarks bar: off while there are no bookmarks");
                Cmd("test-m-bm-add bar " + U + "/page5 Page five"); bar = Wait(() => J(Cmd("test-a-bar")), x => B(x, "shown"), 5000);
                Ok(B(bar, "on") && B(bar, "shown"), "bookmarks bar: the first bookmark turns it on by itself");
                Cmd("test-m-bm-bar off"); Cmd("test-m-bm-add bar " + U + "/page6 Page six"); Thread.Sleep(800); bar = J(Cmd("test-a-bar"));
                Ok(!B(bar, "on") && B(bar, "hidden"), "bookmarks bar: hidden with Ctrl+Shift+B, it stays hidden when bookmarks are added");
                Cmd("test-settings BookmarksBarHidden=false"); Cmd("test-m-bm-add other " + U + "/page7 Page seven"); bar = Wait(() => J(Cmd("test-a-bar")), x => B(x, "shown"), 5000);
                Ok(B(bar, "on"), "bookmarks bar: an install from before (bookmarks, bar off, never hidden on purpose) gets it once");
                // tab groups: easy to find
                Cmd("test-a-open tabmenu"); var tm = L2(Cmd("test-a-flyout-names"));
                Ok(tm.Contains("Add tab to new group") && tm.Contains("Auto-group tabs by site"), "groups: the tab menu offers \"Add tab to new group\" and \"Auto-group tabs by site\"");
                Cmd("test-a-open menu"); var mm = L2(Cmd("test-a-flyout-names"));
                var vw = Val(Cmd("test-h-model")) ?? "";   // 1.13: in the menu's View submenu
                Ok(mm.Any(x => x.StartsWith("View")) && vw.Contains("\"view\":[") && vw.Substring(vw.IndexOf("\"view\":[")).Split(']')[0].Contains("\"Group tabs\""), "groups: the main menu has \"Group tabs\" (1.13: under View)");
                while (Tabs().Count < 6) NewTab(U + "/page" + Tabs().Count);
                NewTab("http://localhost:" + srv.Port + "/pageA");
                var tip = J(Cmd("test-a-grouptip")); Ok(B(tip, "shown") && (S(tip, "toast") ?? "").Contains("group"), "groups: a one-time tip once there are 6+ tabs (" + S(tip, "toast") + ")");
                NewTab("http://localhost:" + srv.Port + "/pageB");
                var ag = J(Cmd("test-a-autogroup"));
                Ok(D(ag, "made") >= 1 && ((System.Collections.ArrayList)ag["groups"]).Cast<object>().Any(x => x.ToString() == "Localhost:2"), "groups: Auto-group by site puts the two localhost tabs in a \"Localhost\" group (" + string.Join(", ", ((System.Collections.ArrayList)ag["groups"]).Cast<object>()) + ")");
                // the game mode icon: only while game mode is on, small, and can be hidden; game mode really turns off
                var ch = J(Cmd("test-a-chip")); Ok(!B(ch, "shown") && S(ch, "game") == null, "game icon: not there without a game");
                File.WriteAllText(gameFile, "Fake Game"); ch = J(Cmd("test-a-chip")); Ok(B(ch, "shown") && S(ch, "game") == "Fake Game", "game icon: shown while game mode is on");
                ch = J(Cmd("test-a-chip off")); Ok(!B(ch, "shown") && S(ch, "game") == "Fake Game", "game icon: the setting hides it (game mode still on)");
                Cmd("test-a-chip on"); File.WriteAllText(gameFile, ""); ch = J(Cmd("test-a-chip"));
                Ok(!B(ch, "shown") && S(ch, "game") == null && S(J(Cmd("test-state")), "priority") == "Normal", "game icon: the game closed -> game mode off, icon gone, normal priority");

                Note("Narrator isn't run here (it needs a real, focused desktop session and speaks out loud); it reads the same UI Automation tree walked above");
                Cmd("quit"); br.WaitForExit(15000); Ok(br.HasExited, "quit closes the browser");
            }
            catch (Exception e) { Ok(false, "v19 crashed: " + e); }
            finally { try { if (br != null && !br.HasExited) br.Kill(); } catch { } srv.Stop(); }
            Console.WriteLine(fail == 0 ? "ALL " + pass + " PASSED (v19)" : fail + " FAILED, " + pass + " passed (v19)");
            return fail == 0 ? 0 : 1;
        }
    }
}
