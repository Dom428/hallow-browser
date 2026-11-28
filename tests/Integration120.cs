// Hallow Browser 1.20 - integration test "v120", off-screen like the others (muted: test browsers start with --mute-audio, each tab and
// panel is muted too), its own test folder, LOCAL pages only (a 127.0.0.1 server of its own). Three small unpacked extensions are
// written into the test folder (one with a side panel, one needing chrome.debugger / native messaging, one plain) - nothing comes
// from the Chrome Web Store. "Open in Chrome" never starts a browser: HALLOW_BROWSER_OPENIN_FOUND names made-up paths and
// HALLOW_BROWSER_OPENIN_RECORD writes each launch down instead.
//  - side panels: the side-panel extension's page opens in Hallow's sidebar (flyout, pinned button's menu, hallow://extensions) as a
//    chrome-extension:// page with working chrome.storage and chrome.runtime, and the chrome.sidePanel shim (getOptions, setOptions
//    path / enabled, setPanelBehavior; open() from its popup opens Hallow's panel);
//  - the verdicts of the three extensions, and of Claude in Chrome from a manifest written by hand (its published permissions);
//  - Open in Chrome: the command recorded (the address only), the tab menu, Ctrl+K, More tools, the verdict's and the note's buttons;
//    a private window asks first and its choices give --incognito / --inprivate or a plain normal window; the setting kept.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v120
using System;
using System.Collections;
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
    public static class Integration120
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string s) { Console.WriteLine("     " + s); if (report != null) report.WriteLine("     " + s); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(300); v = f(); } return v; }
        static List<Dictionary<string, object>> RawTabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static int NewTab(string url) { var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Cmd("test-mute " + i); Wait(RawTabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Cmd("test-mute " + i); return i; }
        static Dictionary<string, object> J(string reply) { try { return js.Deserialize<Dictionary<string, object>>(Val(reply) ?? "{}") ?? new Dictionary<string, object>(); } catch { return new Dictionary<string, object>(); } }
        static List<string> L(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is ArrayList ? ((ArrayList)o).Cast<object>().Select(x => x == null ? null : x.ToString()).ToList() : new List<string>(); }
        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        static double D(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string Eval(string cmd, string expr) { var r = Val(Cmd(cmd + " " + expr)); try { return r == null ? null : js.Deserialize<object>(r) as string ?? r; } catch { return r; } }

        class Srv
        {
            public int Port; TcpListener l; volatile bool stop;
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var s = c.GetStream())
                    {
                        var head = new List<byte>(); int b; while ((b = s.ReadByte()) >= 0) { head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; }
                        var first = Encoding.ASCII.GetString(head.ToArray()).Split('\r')[0].Split(' ');
                        var path = first.Length > 1 ? first[1].Split('?')[0] : "/";
                        var body = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>" + path.Trim('/') + "</title></head><body><h1>" + path + "</h1></body></html>");
                        var h = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        s.Write(h, 0, h.Length); s.Write(body, 0, body.Length); s.Flush();
                    }
                }
                catch { }
            }
        }

        // ---- the three test extensions (MV3: scripts in files, no inline script)
        static void Write(string dir, string name, string text) { var f = Path.Combine(dir, name); Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, text, new UTF8Encoding(false)); }
        static string MakeSide(string root)
        {
            var d = Path.Combine(root, "side-ext");
            Write(d, "manifest.json", "{\"manifest_version\":3,\"name\":\"Side Panel Test\",\"version\":\"1.0\",\"description\":\"1.20 test: a side panel\",\"permissions\":[\"storage\",\"sidePanel\"],\"side_panel\":{\"default_path\":\"panel.html\"},\"action\":{\"default_popup\":\"popup.html\"}}");
            Write(d, "panel.html", "<!DOCTYPE html><html><head><meta charset=utf-8><title>Side panel</title><script src=\"panel.js\"></script></head><body><h1>Side panel test</h1></body></html>");
            Write(d, "panel2.html", "<!DOCTYPE html><html><head><meta charset=utf-8><title>Side panel two</title></head><body><h1>Second page</h1></body></html>");
            // at load: whether chrome.sidePanel is there already (the shim ran before the page's own script), and chrome.storage round trip
            Write(d, "panel.js", "window.__atLoad=(window.chrome&&chrome.sidePanel&&chrome.sidePanel.__hallow)?'shim':'none';" +
                "chrome.storage.local.set({loaded:'panel-'+Date.now()}).then(function(){return chrome.storage.local.get('loaded');}).then(function(r){window.__stored=r.loaded||'';document.title='Side panel '+(r.loaded?'stored':'empty');}).catch(function(e){window.__stored='error '+e;});");
            Write(d, "popup.html", "<!DOCTYPE html><html><head><meta charset=utf-8><title>Popup</title></head><body>Popup</body></html>");
            return d;
        }
        static string MakeGap(string root)
        {
            var d = Path.Combine(root, "gap-ext");
            Write(d, "manifest.json", "{\"manifest_version\":3,\"name\":\"Debugger Test\",\"version\":\"1.0\",\"description\":\"1.20 test: needs chrome.debugger and native messaging\",\"permissions\":[\"storage\",\"debugger\",\"nativeMessaging\"],\"side_panel\":{\"default_path\":\"panel.html\"}}");
            Write(d, "panel.html", "<!DOCTYPE html><html><head><meta charset=utf-8><title>Gap panel</title></head><body>Gap</body></html>");
            return d;
        }
        static string MakePlain(string root)
        {
            var d = Path.Combine(root, "plain-ext");
            Write(d, "manifest.json", "{\"manifest_version\":3,\"name\":\"Plain Test\",\"version\":\"1.0\",\"description\":\"1.20 test: storage only\",\"permissions\":[\"storage\"],\"action\":{\"default_popup\":\"popup.html\"}}");
            Write(d, "popup.html", "<!DOCTYPE html><html><head><meta charset=utf-8><title>Plain popup</title></head><body>Plain</body></html>");
            return d;
        }
        // Claude in Chrome's manifest, written by hand from its published permissions (never downloaded); only read for the verdict
        const string ClaudeManifest = "{\"manifest_version\":3,\"name\":\"Claude\",\"version\":\"1.0.0\",\"description\":\"Hand-written fixture of Claude in Chrome's manifest (permissions as published)\"," +
            "\"permissions\":[\"sidePanel\",\"storage\",\"activeTab\",\"scripting\",\"debugger\",\"tabGroups\",\"tabs\",\"alarms\",\"notifications\",\"webNavigation\",\"declarativeNetRequestWithHostAccess\",\"offscreen\",\"nativeMessaging\"]," +
            "\"host_permissions\":[\"<all_urls>\"],\"side_panel\":{\"default_path\":\"sidepanel.html\"},\"background\":{\"service_worker\":\"service-worker-loader.js\",\"type\":\"module\"}}";
        const string ClaudeId = "fcoeoabgfenejglbffodgkkbkcdhcgfn";

        static Process Start(string exe, string run, string found, string record)
        {
            var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
            psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
            psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
            psi.EnvironmentVariables["HALLOW_BROWSER_OPENIN_FOUND"] = found;
            psi.EnvironmentVariables["HALLOW_BROWSER_OPENIN_RECORD"] = record;
            psi.EnvironmentVariables.Remove("HALLOW_BROWSER_MUTE");   // test browsers start with --mute-audio
            var p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            return p;
        }
        static void Quit(Process br) { try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(10000)) br.Kill(); } } catch { } }
        static List<string> Recorded(string f) { try { return File.Exists(f) ? File.ReadAllLines(f).Where(x => x.Length > 0).ToList() : new List<string>(); } catch { return new List<string>(); } }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v120.txt"), false) { AutoFlush = true };
            var exts = Path.Combine(dir, "exts"); Directory.CreateDirectory(exts);
            string sideDir = MakeSide(exts), gapDir = MakeGap(exts), plainDir = MakePlain(exts);
            var claudeDir = Path.Combine(exts, "claude-fixture"); Write(claudeDir, "manifest.json", ClaudeManifest);
            var record = Path.Combine(dir, "openin-record.txt"); try { File.Delete(record); } catch { }
            // made-up paths: even a bug couldn't start anything there
            var fake = Path.Combine(dir, "no-such-browsers");
            string chromeExe = Path.Combine(fake, "chrome.exe"), braveExe = Path.Combine(fake, "brave.exe"), edgeExe = Path.Combine(fake, "msedge.exe");
            var found = "chrome=" + chromeExe + ";brave=" + braveExe + ";edge=" + edgeExe;
            var srv = new Srv(); srv.Start(); var U = "http://127.0.0.1:" + srv.Port;
            var t0 = DateTime.Now; Process br = null;
            try
            {
                br = Start(exe, run, found, record);
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\"") && (Program.Version == "1.20" || Program.Version == "1.21"), "browser answers ping as 1.20 (or 1.21): " + ping);
                Wait(RawTabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(1000);
                Cmd("test-mute 0");
                Cmd("test-settings HttpsOnly=false");   // the local pages are plain http

                // ---- the three extensions, loaded unpacked from the test folder
                var ids = new Dictionary<string, string>();
                foreach (var kv in new[] { Tuple.Create("side", sideDir), Tuple.Create("gap", gapDir), Tuple.Create("plain", plainDir) })
                {
                    var r = Cmd("test-v-xload " + kv.Item2, 60000);
                    Ok(r != null && r.StartsWith("ok"), "loaded the " + kv.Item1 + " test extension: " + r);
                }
                var ex = js.Deserialize<List<Dictionary<string, object>>>(Val(Cmd("test-xb-exts")) ?? "[]");
                foreach (var x in ex) { var n = S(x, "name"); ids[n == "Side Panel Test" ? "side" : n == "Debugger Test" ? "gap" : "plain"] = S(x, "id"); }
                Ok(ids.Count == 3, "three extensions with their IDs (" + string.Join(", ", ids.Select(k => k.Key + "=" + k.Value)) + ")");
                string side = ids.ContainsKey("side") ? ids["side"] : "?", gap = ids.ContainsKey("gap") ? ids["gap"] : "?", plain = ids.ContainsKey("plain") ? ids["plain"] : "?";

                // ---- verdicts
                var vs = J(Cmd("test-xb-verdict " + side)); var vg = J(Cmd("test-xb-verdict " + gap)); var vp = J(Cmd("test-xb-verdict " + plain));
                Ok(D(vp, "level") == 0 && S(vp, "title") == "Works fully", "plain extension: Works fully (" + S(vp, "title") + ")");
                Ok(D(vs, "level") == 1 && S(vs, "title") == "Works with Hallow's bridge" && S(vs, "sidePanel") == "panel.html", "side-panel extension: Works with Hallow's bridge, panel.html (" + string.Join(" | ", L(vs, "reasons")) + ")");
                Ok(D(vg, "level") == 2 && S(vg, "title") == "Use Open in Chrome" && L(vg, "keys").Contains("debugger") && L(vg, "keys").Contains("nativeMessaging"), "debugger / nativeMessaging extension: Use Open in Chrome (" + string.Join(" | ", L(vg, "reasons")) + ")");
                var vc = J(Cmd("test-xb-verdict-file " + ClaudeId + " " + Path.Combine(claudeDir, "manifest.json")));
                var cr = L(vc, "reasons");
                Ok(D(vc, "level") == 2 && B(vc, "askClaude") && cr.Count >= 3 && cr[0].StartsWith("chrome.debugger") && cr[1].StartsWith("side panel") && cr[2].StartsWith("native messaging"), "Claude in Chrome (hand-written manifest): Use Open in Chrome - debugger, side panel, native messaging (" + string.Join(" | ", cr.Take(3)) + ")");
                Ok((S(vc, "known") ?? "").Contains("Ask Claude"), "... and it points to Hallow's Ask Claude sidebar");

                // ---- verdicts on hallow://extensions and the store card
                var ep = J(Cmd("test-xb-extpage"));
                var levels = ((ArrayList)(ep.ContainsKey("verdicts") ? ep["verdicts"] : new ArrayList())).Cast<Dictionary<string, object>>().Select(x => S(x, "level")).ToList();
                Ok(levels.Count == 3 && levels.Contains("verdict:0") && levels.Contains("verdict:1") && levels.Contains("verdict:2"), "hallow://extensions shows a verdict per extension (" + string.Join(", ", levels) + ")");
                Ok(L(ep, "sidePanels").Contains(side) && L(ep, "sidePanels").Contains(gap) && !L(ep, "sidePanels").Contains(plain), "... and \"Side panel\" for the two with a panel");
                var pc = J(Cmd("test-xb-pending " + ClaudeId + " " + claudeDir));
                Ok(S(pc, "level") == "verdict:2" && (S(pc, "text") ?? "").Contains("Use Open in Chrome") && (S(pc, "text") ?? "").Contains("chrome.debugger") && (S(pc, "text") ?? "").Contains("native messaging") && L(pc, "buttons").Contains("verdict:openin") && L(pc, "buttons").Contains("verdict:askclaude"), "the store card before adding Claude: Use Open in Chrome with its reasons, Open in Chrome and Ask Claude buttons");
                var ps = J(Cmd("test-xb-pending " + side + " " + sideDir));
                Ok(S(ps, "level") == "verdict:1" && (S(ps, "text") ?? "").Contains("Works with Hallow's bridge") && !(S(ps, "text") ?? "").Contains("probably won't work"), "the store card for a side-panel extension: Works with Hallow's bridge (no \"won't work\" warning)");
                var pa = J(Cmd("test-xb-pending-press verdict:askclaude " + ClaudeId + " " + claudeDir));
                Ok(B(pa, "settingsTab") && S(pa, "settingsQuery") == "Ask Claude", "Claude's \"Ask Claude in Hallow's sidebar\" (Ask Claude off): opens Settings at Ask Claude");
                Cmd("test-settings AiOn=true");
                var pa2 = J(Cmd("test-xb-pending-press verdict:askclaude " + ClaudeId + " " + claudeDir));
                Ok(S(pa2, "native") == "claude", "... with Ask Claude on: opens Hallow's Ask Claude sidebar");
                Cmd("test-y-panel hide"); Cmd("test-settings AiOn=false");
                for (int i = RawTabs().Count - 1; i >= 1; i--) Cmd("test-close " + i);

                // ---- side panel: the puzzle piece's list
                int pageTab = NewTab(U + "/page-a");
                var fl = J(Cmd("test-xb-flyout"));
                Ok(L(fl, "sidePanels").Contains(side) && L(fl, "sidePanels").Contains(gap) && !L(fl, "sidePanels").Contains(plain), "the extensions list offers \"Open side panel\" for the side-panel and debugger extensions, not the plain one");
                Cmd("test-xb-flyout-sp " + side);
                var pn = Wait(() => J(Cmd("test-xb-panel")), d => B(d, "ready") && (S(d, "source") ?? "").StartsWith("chrome-extension://"), 20000);
                Cmd("test-xb-panel-mute");
                Ok(B(pn, "open") && S(pn, "ext") == side && S(pn, "source") == "chrome-extension://" + side + "/panel.html" && B(pn, "hostShown"), "Open side panel: chrome-extension://" + side + "/panel.html in Hallow's sidebar (" + S(pn, "source") + ")");
                Ok(S(pn, "note") == null, "no gap note for a bridge-level extension");
                var stored = Wait(() => Eval("test-xb-panel-eval", "window.__stored||''"), s => !string.IsNullOrEmpty(s), 8000);
                Ok(stored != null && stored.StartsWith("panel-"), "the panel page's own script used chrome.storage at load: " + stored);
                var rt = Eval("test-xb-panel-eval", "chrome.storage.local.set({v120:'kept'}).then(function(){return chrome.storage.local.get('v120');}).then(function(r){return r.v120+' '+chrome.runtime.id;})");
                Ok(rt == "kept " + side, "chrome.storage and chrome.runtime work in the panel (" + rt + ")");
                var shim = Eval("test-xb-panel-eval", "JSON.stringify({mark:window.__hallowSidePanelShim===true,hallow:!!(chrome.sidePanel&&chrome.sidePanel.__hallow),fns:['open','close','setOptions','getOptions','setPanelBehavior','getPanelBehavior'].filter(function(f){return typeof chrome.sidePanel[f]==='function';}).length,atLoad:window.__atLoad,webview:!!(window.chrome&&chrome.webview),binding:typeof window.__hallowSidePanelBridge})");
                Note("shim: " + shim);
                var sh = js.Deserialize<Dictionary<string, object>>(shim ?? "{}") ?? new Dictionary<string, object>();
                Ok(B(sh, "mark") && B(sh, "hallow") && D(sh, "fns") == 6, "the chrome.sidePanel shim is there (6 functions)");
                Ok(S(sh, "atLoad") == "shim", "... already when the page's own script ran (added before the page loaded)");
                var go = Eval("test-xb-panel-eval", "chrome.sidePanel.getOptions({}).then(function(o){return o.path+' '+o.enabled;})");
                Ok(go == "panel.html True" || go == "panel.html true", "getOptions resolves with the manifest's path (" + go + ")");
                var cb = Eval("test-xb-panel-eval", "new Promise(function(res){chrome.sidePanel.getPanelBehavior(function(b){res('cb '+b.openPanelOnActionClick);});})");
                Ok(cb == "cb false", "callbacks are called too (" + cb + ")");
                // setOptions({path}) reaches Hallow: its panel goes to the new page
                Eval("test-xb-panel-eval", "chrome.sidePanel.setOptions({path:'panel2.html'}).then(function(){return 'ok';})");
                var p2 = Wait(() => J(Cmd("test-xb-panel")), d => (S(d, "source") ?? "").EndsWith("/panel2.html"), 8000);
                Ok((S(p2, "source") ?? "").EndsWith("/panel2.html") && (S(p2, "bridge") ?? "").StartsWith("setOptions"), "setOptions({path}) reaches Hallow and the panel shows that page (through " + S(p2, "channel") + ")");
                Note("bridge channel: " + S(p2, "channel") + ", last message: " + S(p2, "bridge"));
                Cmd("test-xb-panel-mute");
                Eval("test-xb-panel-eval", "chrome.sidePanel.setPanelBehavior({openPanelOnActionClick:true}).then(function(){return 'ok';})");
                var pb = Wait(() => J(Cmd("test-xb-panel")), d => (S(d, "bridge") ?? "").StartsWith("behavior"), 6000);
                var onAction = pb.ContainsKey("onAction") ? pb["onAction"] as Dictionary<string, object> : null;
                Ok(onAction != null && onAction.ContainsKey(side) && (bool)onAction[side], "setPanelBehavior({openPanelOnActionClick:true}) is kept");
                Eval("test-xb-panel-eval", "chrome.sidePanel.setOptions({enabled:false}).then(function(){return 'ok';})");
                var pd = Wait(() => J(Cmd("test-xb-panel")), d => !B(d, "open"), 6000);
                Ok(!B(pd, "open"), "setOptions({enabled:false}) closes Hallow's panel");
                // open() from the extension's popup opens Hallow's panel
                var pop = Val(Cmd("test-xb-popup " + side));
                Ok(pop != null && pop.StartsWith("chrome-extension://" + side + "/popup.html"), "the extension's popup opened: " + pop);
                var pshim = Eval("test-xb-popup-eval", "String(!!(chrome.sidePanel&&chrome.sidePanel.__hallow))");
                Ok(pshim == "true", "the shim is in the popup too");
                Eval("test-xb-popup-eval", "chrome.sidePanel.open({windowId:1}).then(function(){return 'ok';})");
                var po = Wait(() => J(Cmd("test-xb-panel")), d => B(d, "open") && S(d, "ext") == side, 8000);
                Ok(B(po, "open") && S(po, "ext") == side && (S(po, "bridge") ?? "").StartsWith("open"), "chrome.sidePanel.open() in the popup opens Hallow's side panel");
                Cmd("test-xb-popup-hide"); Cmd("test-xb-panel-mute");
                Cmd("test-xb-panel-hide");
                // the pinned button's right-click menu
                var pm = J(Cmd("test-xb-pinmenu " + side + " Open side panel"));
                Ok((S(pm, "text") ?? "").Contains("Open side panel") && (S(pm, "text") ?? "").Contains("Unpin from the toolbar"), "the pinned extension's right-click menu: Open side panel, Unpin ... (" + S(pm, "text") + ")");
                var pp = Wait(() => J(Cmd("test-xb-panel")), d => B(d, "open"), 8000);
                Ok(B(pp, "open") && S(pp, "ext") == side, "... and Open side panel opens it");
                Cmd("test-xb-panel-mute"); Cmd("test-xb-panel-hide");

                // ---- the debugger extension's panel: opens, with a note and Open in Chrome
                Cmd("test-activate " + pageTab);
                var og = Val(Cmd("test-xb-open-sp " + gap));
                var pgp = Wait(() => J(Cmd("test-xb-panel")), d => B(d, "ready"), 15000);
                Cmd("test-xb-panel-mute");
                Ok(B(pgp, "open") && S(pgp, "ext") == gap && (S(pgp, "source") ?? "").EndsWith("/panel.html"), "the debugger extension's panel page opens too (" + S(pgp, "source") + ")");
                Ok((S(pgp, "note") ?? "").Contains("chrome.debugger") && (S(pgp, "note") ?? "").Contains("native messaging") && (S(pgp, "note") ?? "").Contains("may not work"), "... with a clear note above it: " + S(pgp, "note"));
                var np = J(Cmd("test-xb-note-press note:openin"));
                Ok(S(np, "last") == "chrome normal " + U + "/page-a", "the note's \"Open this page in Chrome\": " + S(np, "last"));
                Cmd("test-xb-panel-hide");

                // ---- Open in Chrome
                var fd = J(Cmd("test-xb-openin-found"));
                Ok(S(fd, "pick") == "chrome" && S(fd, "menuLabelPage") == "Open page in Chrome" && S(fd, "menuLabelLink") == "Open link in Chrome", "automatic: Chrome (the page's menu: \"Open page in Chrome\" / \"Open link in Chrome\")");
                int before = Recorded(record).Count;
                var oi = J(Cmd("test-xb-openin " + pageTab));
                var rec = Recorded(record);
                Ok(S(oi, "last") == "chrome normal " + U + "/page-a" && rec.Count == before + 1 && rec.Last() == "chrome\t" + chromeExe + "\t\"" + U + "/page-a\"", "Open in Chrome: the command is the address only (recorded: " + (rec.Count > 0 ? rec.Last() : "nothing") + ")");
                var tm = Cmd("test-nn-tabmenu-run " + pageTab + " Open in Chrome");
                Ok(tm != null && tm.StartsWith("ok") && Recorded(record).Count == before + 2, "the tab's menu has Open in Chrome, and it runs");
                var pal = Val(Cmd("test-p-palette open in chrome")) ?? "";
                Ok(pal.Contains("Open in Chrome"), "Ctrl+K finds Open in Chrome");
                var pr = Val(Cmd("test-p-palette-run 0 open in chrome")) ?? "";
                Ok(pr.Contains("Open in Chrome") && Recorded(record).Count == before + 3, "... and runs it (" + pr + ")");
                var more = Val(Cmd("test-xb-menu-more")) ?? "";
                Ok(more.Contains("Open in Chrome"), "Menu > More tools > Open in Chrome");
                var vp2 = J(Cmd("test-xb-verdict-press " + gap + " verdict:openin"));
                Ok((S(vp2, "last") ?? "").StartsWith("chrome normal http"), "the verdict's \"Open this page in Chrome\" button: " + S(vp2, "last"));
                Ok(Val(Cmd("test-xb-openin-set brave")) == "brave", "Settings: Open in Brave");
                var ob = J(Cmd("test-xb-openin " + pageTab));
                Ok(S(ob, "label") == "Open in Brave" && Recorded(record).Last() == "brave\t" + braveExe + "\t\"" + U + "/page-a\"", "... and the next one goes to Brave: " + Recorded(record).Last());
                var st = Val(Cmd("test-xb-openin-settings")) ?? "";
                Ok(st.Contains("Automatic") && st.Contains("Chrome") && st.Contains("Brave") && st.Contains("Edge") && st.Contains("isn't private"), "the settings card lists Automatic / Chrome / Brave / Edge and the private-window rule");
                var sj = File.ReadAllText(Path.Combine(run, "settings.json"));
                Ok(sj.Contains("\"OpenIn\":\"brave\""), "the choice is saved in settings.json");

                // ---- a private window: it asks first
                Cmd("test-v-wpriv " + U + "/page-p"); Thread.Sleep(2500);
                Cmd("test-xb-openin-set chrome");
                int b2 = Recorded(record).Count;
                var ask = J(Cmd("test-xb-openin-private " + U + "/page-p"));
                Ok(S(ask, "last") == null && Recorded(record).Count == b2 && (S(ask, "ask") ?? "").Contains("won't be private") && L(ask, "tags").Contains("openin:private") && L(ask, "tags").Contains("openin:normal"), "from a private window nothing opens at once - it says Chrome won't be private and offers both (" + S(ask, "ask") + ")");
                var pv = J(Cmd("test-xb-openin-press openin:private"));
                Ok(Recorded(record).Last() == "chrome\t" + chromeExe + "\t--incognito \"" + U + "/page-p\"", "\"Open in an Incognito window of Chrome\": --incognito (" + Recorded(record).Last() + ")");
                J(Cmd("test-xb-openin-private " + U + "/page-p"));
                var nv = J(Cmd("test-xb-openin-press openin:normal"));
                Ok(Recorded(record).Last() == "chrome\t" + chromeExe + "\t\"" + U + "/page-p\"" && (S(nv, "toast") ?? "").Contains("isn't private"), "\"Open in a normal Chrome window\": no flag, and it says it isn't private there (" + S(nv, "toast") + ")");
                Cmd("test-xb-openin-set edge");
                J(Cmd("test-xb-openin-private " + U + "/page-p"));
                J(Cmd("test-xb-openin-press openin:private"));
                Ok(Recorded(record).Last() == "edge\t" + edgeExe + "\t--inprivate \"" + U + "/page-p\"", "Edge's private window: --inprivate (" + Recorded(record).Last() + ")");
                int b3 = Recorded(record).Count;
                J(Cmd("test-xb-openin-private " + U + "/page-p")); J(Cmd("test-xb-openin-press openin:cancel"));
                Ok(Recorded(record).Count == b3, "Cancel opens nothing");
                Ok(Recorded(record).All(l => !l.Contains("--profile") && !l.Contains("--user-data-dir") && !l.Contains("--app")), "no recorded command carries a profile flag");
                Cmd("test-xb-openin-set brave");

                // ---- kept across a restart: the setting
                Quit(br); br = null; Thread.Sleep(1500);
                br = Start(exe, run, found, record);
                Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Wait(RawTabs, t => t.Count >= 1, 30000); Thread.Sleep(1500);
                for (int i = 0; i < RawTabs().Count; i++) Cmd("test-mute " + i);
                var fd2 = J(Cmd("test-xb-openin-found"));
                Ok(S(fd2, "setting") == "brave" && S(fd2, "pick") == "brave", "after a restart: still Open in Brave");
                var ex2 = J(Cmd("test-xb-verdict " + side));
                Ok(D(ex2, "level") == 1, "after a restart: the extensions and their verdicts are back");
                // nothing was ever started: no browser at the made-up paths, and the recorded launches only
                Ok(!Directory.Exists(fake), "the made-up browser folder doesn't exist - nothing could have been started");
            }
            catch (Exception e) { Ok(false, "crashed: " + e); }
            finally
            {
                Quit(br);
                try { srv.Stop(); } catch { }
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
            }
            Console.WriteLine(pass + " passed, " + fail + " failed"); if (report != null) { report.WriteLine(pass + " passed, " + fail + " failed"); report.Dispose(); }
            return fail == 0 ? 0 : 1;
        }
    }
}
