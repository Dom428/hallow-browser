// Hallow Browser 1.2 - integration test for the power features, off-screen like the others (never shown, never focused, below-normal
// priority), with its own test folder, profile and pipes, against local test pages only. Screenshots go to the test folder; the
// clipboard is never touched (a test folder makes "copy" only count the bytes).
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> power
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration12
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> Lay() { var v = Val(Cmd("test-p-layout")); return v == null ? new Dictionary<string, object>() : js.Deserialize<Dictionary<string, object>>(v); }
        static List<Dictionary<string, object>> L(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Cast<Dictionary<string, object>>().ToList() : new List<Dictionary<string, object>>(); }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); }
        static List<Dictionary<string, object>> Pal(string q) { var v = Val(Cmd("test-p-palette " + q)); return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : null; }
        static int I(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        static double D(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToDouble(o) : -1; }
        static string Tabs2(Dictionary<string, object> lay) { return string.Join(" ", L(lay, "tabs").Select(t => Path.GetFileName(S(t, "url") ?? "") + (S(t, "group") != null ? "[" + S(t, "group") + "]" : ""))); }
        static List<int> GroupTabs(Dictionary<string, object> lay, string name) { var g = L(lay, "groups").FirstOrDefault(x => S(x, "name") == name); return g == null ? new List<int>() : ((System.Collections.ArrayList)g["tabs"]).Cast<object>().Select(Convert.ToInt32).ToList(); }
        static bool AllReady() { var t = Tabs(); return t.Count > 0 && t.All(x => (bool)x["ready"] || (bool)x["discarded"]); }

        public static int Main(string exe, string dir)
        {
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", dir);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-power.txt"), false) { AutoFlush = true };
            var srv = new TinyServer();
            var para = "The respawn timer is the quietest part of any match, and the most honest. For ten seconds you watch the map without a gun, and you learn more about the round than in the minute before it.";
            Func<string, string> page = t => "<!DOCTYPE html><html><head><title>" + t + "</title></head><body><h1>" + t + "</h1><p>A small local test page.</p></body></html>";
            srv.Route = (path, h) =>
            {
                if (path.StartsWith("/a.html")) return Tuple.Create("text/html", page("Alpha page"), "");
                if (path.StartsWith("/b.html")) return Tuple.Create("text/html", page("Bravo page"), "");
                if (path.StartsWith("/c.html")) return Tuple.Create("text/html", page("Charlie page"), "");
                if (path.StartsWith("/d.html")) return Tuple.Create("text/html", page("Delta page"), "");
                if (path.StartsWith("/e.html")) return Tuple.Create("text/html", page("Echo work page"), "");
                if (path.StartsWith("/zebra.html")) return Tuple.Create("text/html", page("Zebra history page"), "");
                if (path.StartsWith("/tall.html")) return Tuple.Create("text/html", "<!DOCTYPE html><html><head><title>Tall page</title></head><body style='margin:0'><div style='height:4000px;background:linear-gradient(#f07820,#2a2336)'>top</div></body></html>", "");
                if (path.StartsWith("/audio.html")) return Tuple.Create("text/html", "<!DOCTYPE html><title>Music</title><script>window.go=function(){var c=new AudioContext();var o=c.createOscillator();var g=c.createGain();g.gain.value=0.05;o.connect(g);g.connect(c.destination);o.start();return 1;};</script>", "");
                if (path.StartsWith("/article.html"))
                    return Tuple.Create("text/html", "<!DOCTYPE html><html><head><title>Respawn | Hallow Test News</title><meta property='og:title' content='The quiet art of the respawn'><meta property='og:site_name' content='Hallow Test News'><meta name='author' content='Kit Tester'></head><body>" +
                        "<header><nav class='site-nav'><a href='/'>Home</a> | <a href='/news'>News</a> | <a href='/reviews'>Reviews</a></nav></header>" +
                        "<div class='ad-banner advert'>Advertisement: buy more RGB now</div>" +
                        "<div class='layout'><aside class='sidebar'><h3>Trending</h3><ul><li><a href='/1'>Ten best mice</a></li><li><a href='/2'>Patch notes</a></li></ul></aside>" +
                        "<article class='post'><h1>The quiet art of the respawn</h1><p class='byline'>By Kit Tester</p>" +
                        "<p>" + para + "</p><p>Good players use it. They look at the kill feed, they count the enemy team, and they pick the next fight before the timer ends. It is a habit, and like every habit it starts small.</p>" +
                        "<h2>What to watch</h2><ul><li>Where your team is, and where it is not.</li><li>Which enemy has the long gun.</li></ul>" +
                        "<blockquote>The best players never really die; they just take notes.</blockquote>" +
                        "<p>None of this needs a new setting or a better mouse. It needs ten seconds of attention, every time, for a week. After that it is automatic, and the timer feels shorter than it is.</p>" +
                        "<pre>respawn_timer = 10</pre><p>Next week: why the minimap is the most important screen in the game, and how to read it at a glance while still aiming.</p></article></div>" +
                        "<section class='comments'><h3>Comments</h3><p>Great article! First comment, please like and subscribe to my channel.</p></section>" +
                        "<footer><p>Subscribe to our newsletter for more tips every single week, straight to your inbox.</p></footer></body></html>", "");
                return Tuple.Create<string, string, string>(null, "not found", "");
            };
            srv.Start(); var Lh = "http://127.0.0.1:" + srv.Port;
            Process br = null;
            Func<string, Process> launch = arg =>
            {
                var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = dir;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = Path.Combine(dir, "game.txt");
                var p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return p;
            };
            File.WriteAllText(Path.Combine(dir, "game.txt"), "");
            Func<bool> restart = () =>
            {
                Cmd("quit"); br.WaitForExit(15000); var down = br.HasExited;
                br = launch("");
                var up = Wait(() => Cmd("ping", 2000), rr => rr != null && rr.StartsWith("ok"), 20000) != null;
                Wait(Tabs, t => t.Any(x => (bool)x["active"] && (bool)x["ready"]), 20000);
                return down && up;
            };
            Func<string, int> open = url => { var v = Val(Cmd("test-new " + url)); int i = v == null ? -1 : int.Parse(v); Wait(Tabs, t => i >= 0 && i < t.Count && (bool)t[i]["ready"], 15000); return i; };
            try
            {
                br = launch(Lh + "/a.html");
                var ping = Wait(() => Cmd("ping", 2000), rr => rr != null && rr.StartsWith("ok"), 20000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count > 0 && (bool)t[0]["ready"], 30000);
                open(Lh + "/b.html"); open(Lh + "/c.html"); open(Lh + "/d.html");
                var lay = Lay(); Note("start: " + Tabs2(lay));

                // ---- vertical tabs
                Cmd("test-p-vertical on"); lay = Lay();
                Ok(B(lay, "vertical") && B(lay, "sidebarRight"), "vertical tabs on: the tab column on the left, the sidebar moves to the right");
                Ok(L(lay, "tabs").All(t => B(t, "headerShown")), "every tab is shown in the column");
                Cmd("test-p-icons on"); Ok(B(Lay(), "collapsed"), "the column collapses to icons only"); Cmd("test-p-icons off");

                // ---- groups: new, add, drag in / out, drag onto a label, colour, collapse
                Ok(Val(Cmd("test-p-group-new 1 Raid")) == "0" && Cmd("test-p-group-add 2 0") == "ok", "group Raid with tabs 1 and 2");
                Cmd("test-p-drag 3 2"); lay = Lay();
                Ok(string.Join(",", GroupTabs(lay, "Raid")) == "1,2,3", "dragging a tab between two of the group's tabs adds it: " + Tabs2(lay));
                Cmd("test-p-drag 1 0"); lay = Lay();
                Ok(S(L(lay, "tabs")[0], "group") == null && GroupTabs(lay, "Raid").Count == 2, "dragging a tab out of the group takes it out: " + Tabs2(lay));
                Ok(Val(Cmd("test-p-group-new 0 Loot")) == "1", "second group Loot");
                Cmd("test-p-drag 1 chip 1"); lay = Lay();
                Ok(GroupTabs(lay, "Loot").Count == 2, "dragging a tab onto a group's label adds it: " + Tabs2(lay));
                Cmd("test-p-group-color 1 green");
                var raidFirst = GroupTabs(lay, "Raid")[0]; Cmd("test-activate " + raidFirst);
                Ok(Val(Cmd("test-p-group-collapse 1")) == "True", "collapse Loot");
                lay = Lay(); var lootIdx = GroupTabs(lay, "Loot");
                Ok(lootIdx.All(i => !B(L(lay, "tabs")[i], "headerShown")) && L(lay, "groups").All(g => B(g, "chipShown")), "a collapsed group's tabs are hidden, its label stays");
                var before = Tabs2(lay); Note("before restart: " + before);

                // ---- vertical tabs and groups survive a restart
                Ok(restart(), "restart");
                lay = Lay();
                var gl = L(lay, "groups");
                Ok(B(lay, "vertical"), "vertical tabs still on after a restart");
                Ok(Tabs2(lay) == before, "tabs and groups in the same order after a restart: " + Tabs2(lay));
                var loot = gl.FirstOrDefault(g => S(g, "name") == "Loot"); var raid = gl.FirstOrDefault(g => S(g, "name") == "Raid");
                Ok(loot != null && raid != null && B(loot, "collapsed") && S(loot, "color") == "green" && !B(raid, "collapsed"), "group names, colour and collapsed state saved with the session");

                // ---- sleep a whole group
                var raidTabs = GroupTabs(lay, "Raid");
                foreach (var i in raidTabs) { Cmd("test-activate " + i); Wait(Tabs, t => (bool)t[i]["ready"], 15000); }
                Cmd("test-activate 0"); Thread.Sleep(500);
                var slept = Val(Cmd("test-p-group-sleep " + gl.IndexOf(raid)));
                var tt = Tabs();
                Ok(slept == raidTabs.Count.ToString() && raidTabs.All(i => (bool)tt[i]["discarded"]), "put the whole group to sleep: " + slept + " tabs unloaded");
                Cmd("test-activate " + raidTabs[0]); Ok(Wait(Tabs, t => (bool)t[raidTabs[0]]["ready"], 15000)[raidTabs[0]]["discarded"].Equals(false), "a slept group tab loads again when clicked");

                // ---- workspaces: separate sessions, same profile
                var defCount = L(Lay(), "tabs").Count; var defOrder = Tabs2(Lay());
                Ok(Val(Cmd("test-p-ws-new Work")) == "Work", "new workspace Work");
                lay = Lay(); Ok(S(lay, "workspace") == "Work" && L(lay, "tabs").Count == 1 && L(lay, "groups").Count == 0, "Work starts with one new tab and no groups");
                open(Lh + "/e.html"); var workOrder = Tabs2(Lay());
                Ok(Val(Cmd("test-p-ws-switch Default")) == defCount.ToString() && Tabs2(Lay()) == defOrder, "back to Default: its own tabs and groups (" + Tabs2(Lay()) + ")");
                Ok(Val(Cmd("test-p-ws-switch Work")) == "2" && Tabs2(Lay()).Contains("e.html") && !Tabs2(Lay()).Contains("b.html"), "Work keeps its own session (" + Tabs2(Lay()) + ")");
                var wsj = File.ReadAllText(Path.Combine(dir, "workspaces.json"));
                Ok(wsj.Contains("\"Work\"") && wsj.Contains("e.html") && wsj.Contains("b.html"), "workspaces.json holds both workspaces' sessions");
                Ok(restart(), "restart in Work");
                lay = Lay(); Ok(S(lay, "workspace") == "Work" && Tabs2(lay) == workOrder, "the current workspace and its tabs come back after a restart");
                Ok(Val(Cmd("test-p-ws-switch Default")) == defCount.ToString(), "and Default is still there");
                var st = Val(Cmd("status")); Ok(st != null && st.Contains("\"workspaces\":2") && st.Contains("\"verticalTabs\":true") && st.Contains("\"groups\":2") && !st.Contains("Work") && !st.Contains("Raid"), "status: counts only (no workspace or group names): " + st);

                // ---- saved sessions
                Ok((Val(Cmd("test-p-session-save Raid night")) ?? "") == "Raid night", "save the session as Raid night");
                Ok(File.ReadAllText(Path.Combine(dir, "saved-sessions.json")).Contains("Raid night"), "saved-sessions.json written");
                Ok(Val(Cmd("test-p-session-restore Raid night")) == "Raid night" && Tabs2(Lay()) == defOrder, "restoring it opens its tabs and groups as a workspace (" + Tabs2(Lay()) + ")");
                Cmd("test-p-ws-switch Default");

                // ---- split view
                Cmd("test-p-vertical off");
                var a = open(Lh + "/a.html"); var b = open(Lh + "/b.html");
                Cmd("test-activate " + a);
                Ok(Val(Cmd("test-p-split " + b)) == "True", "Ctrl+click a tab: split view");
                lay = Wait(Lay, x => { var ts = L(x, "tabs"); return ts.Count > b && B(ts[a], "viewVisible") && B(ts[b], "viewVisible"); }, 8000);
                var ta = L(lay, "tabs")[a]; var tb = L(lay, "tabs")[b]; var cw = D(lay, "contentWidth");
                Note("split: content " + cw + " px, left " + D(ta, "viewWidth") + " (column " + I(ta, "column") + "), right " + D(tb, "viewWidth") + " (column " + I(tb, "column") + ")");
                Ok(B(lay, "split") && I(ta, "column") == 0 && I(tb, "column") == 2 && Math.Abs(D(ta, "viewWidth") - D(tb, "viewWidth")) < 12 && D(ta, "viewWidth") > cw * 0.4, "split view: two pages side by side, half each");
                Ok(Val(Cmd("test-eval " + a + " document.title")) == "\"Alpha page\"" && Val(Cmd("test-eval " + b + " document.title")) == "\"Bravo page\"", "split view: both pages loaded");
                Cmd("test-p-split-swap"); lay = Lay();
                Ok(I(L(lay, "tabs")[a], "column") == 2 && I(L(lay, "tabs")[b], "column") == 0, "swap the sides");
                Cmd("test-activate 0"); lay = Lay(); Ok(!B(L(lay, "tabs")[a], "viewVisible") && !B(L(lay, "tabs")[b], "viewVisible"), "another tab shows alone; the split waits");
                Cmd("test-activate " + b); lay = Lay(); Ok(B(L(lay, "tabs")[a], "viewVisible") && B(L(lay, "tabs")[b], "viewVisible"), "back to a split tab: both sides again");
                Ok(restart(), "restart with a split");
                lay = Lay(); Ok(B(lay, "split"), "the split view is saved with the session");
                Cmd("test-p-split-close"); Ok(!B(Lay(), "split"), "close the split view");

                // ---- Ctrl+K: a tab, a history entry, a command
                var z = open(Lh + "/zebra.html"); Thread.Sleep(600); Cmd("test-close " + z);
                Wait(() => Val(Cmd("test-p-history")) ?? "", h => h.Contains("zebra.html"), 8000);
                var r = Pal("Bravo");
                Ok(r.Count > 0 && S(r[0], "kind") == "Tab" && S(r[0], "title") == "Bravo page", "Ctrl+K finds an open tab: " + (r.Count > 0 ? S(r[0], "kind") + " " + S(r[0], "title") : "nothing"));
                r = Pal("zebra");
                Ok(r.Any(x => S(x, "kind") == "History" && S(x, "title") == "Zebra history page"), "Ctrl+K finds a page from history: " + string.Join("; ", r.Select(x => S(x, "kind") + " " + S(x, "title"))));
                r = Pal("vertical tabs");
                Ok(r.Count > 0 && S(r[0], "kind") == "Command", "Ctrl+K finds a command: " + (r.Count > 0 ? S(r[0], "title") : "nothing"));
                Ok((Val(Cmd("test-p-palette-run 0 vertical tabs")) ?? "").StartsWith("Command") && B(Lay(), "vertical"), "running the command works (vertical tabs on)");
                r = Pal("gaming light"); Ok(r.Any(x => S(x, "kind") == "Command" && (S(x, "title") ?? "").StartsWith("Setting: Gaming-light mode")), "settings are commands too");
                r = Pal("discord"); Ok(r.Any(x => S(x, "kind") == "Panel"), "sidebar panels are in it");
                Cmd("test-activate 0"); Cmd("test-p-palette-run 0 zebra");
                Ok(Wait(Tabs, t => ((string)t[0]["url"]).Contains("zebra.html"), 8000)[0]["url"].ToString().Contains("zebra"), "picking the history entry opens it");
                Cmd("test-p-history-save"); var hf = Path.Combine(dir, "Local", "Profile", "hallow-history.json");
                Ok(File.Exists(hf) && File.ReadAllText(hf).Contains("zebra.html"), "history is kept in the profile folder");
                Cmd("test-p-bookmark"); r = Pal("zebra"); Ok(r.Any(x => S(x, "kind") == "Bookmark"), "Ctrl+D bookmarks the page and Ctrl+K finds the bookmark");
                Cmd("test-p-history-clear"); Ok(Val(Cmd("test-p-history")).Contains("\"count\":0") && !File.Exists(hf), "Clear history empties the store and its file");
                Cmd("test-new " + Lh + "/a.html"); Thread.Sleep(300); Cmd("test-close " + (Tabs().Count - 1));
                Ok(!Pal("zebra").Any(x => S(x, "kind") == "History"), "after clearing, history has no zebra page");

                // ---- screenshots
                var tl = open(Lh + "/tall.html"); Thread.Sleep(500);
                var vis = (Val(Cmd("test-p-shot visible save")) ?? "").Split('|');
                Ok(vis.Length >= 4 && vis[0] == "saved" && File.Exists(vis[1]) && vis[1].StartsWith(Path.Combine(dir, "Pictures", "Hallow Browser")), "visible area saved as PNG to Pictures\\Hallow Browser (test folder): " + string.Join("|", vis));
                Func<string, bool> isPng = f => { try { var bs = File.ReadAllBytes(f); return bs.Length > 8 && bs[0] == 0x89 && bs[1] == 0x50 && bs[2] == 0x4E && bs[3] == 0x47; } catch { return false; } };
                Ok(vis.Length >= 4 && isPng(vis[1]), "it is a PNG");
                var full = (Val(Cmd("test-p-shot full save")) ?? "").Split('|');
                Ok(full.Length >= 4 && full[0] == "saved" && isPng(full[1]) && int.Parse(full[3]) > int.Parse(vis[3]) * 2 && int.Parse(full[3]) >= 3900, "full page is taller than the viewport: " + full.Skip(2).FirstOrDefault() + " x " + full.Skip(3).FirstOrDefault() + " vs visible " + vis[2] + " x " + vis[3]);
                var reg = (Val(Cmd("test-p-shot region copy 0.25 0.25 0.5 0.5")) ?? "").Split('|');
                Ok(reg.Length >= 5 && reg[0] == "copied" && Math.Abs(int.Parse(reg[2]) - int.Parse(vis[2]) / 2) <= 2 && Math.Abs(int.Parse(reg[3]) - int.Parse(vis[3]) / 2) <= 2 && int.Parse(reg[4]) > 0, "a region is cropped and copied (clipboard not touched in tests): " + string.Join("|", reg));
                Ok(Directory.GetFiles(Path.Combine(dir, "Pictures", "Hallow Browser"), "*.png").Length == 2, "two PNG files saved, never overwritten");

                // ---- reader mode
                var ar = open(Lh + "/article.html");
                var rl = Wait(Lay, x => B(L(x, "tabs")[ar], "readable"), 8000);
                Ok(B(L(rl, "tabs")[ar], "readable"), "the reader icon shows on an article page");
                var rd = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-p-reader " + ar)) ?? "{}");
                var text = S(rd, "text") ?? "";
                Note("reader: " + S(rd, "title") + " / " + S(rd, "site") + ", " + I(rd, "words") + " words, " + I(rd, "blocks") + " blocks: " + text.Replace("\n", " // "));
                Ok(B(rd, "ok") && B(rd, "on") && S(rd, "title") == "The quiet art of the respawn" && S(rd, "site") == "Hallow Test News", "reader mode extracts the test article (title, site)");
                Ok(text.Contains(para) && text.Contains("Next week: why the minimap") && text.Contains("Which enemy has the long gun.") && text.Contains("take notes") && text.Contains("respawn_timer = 10"), "the article's paragraphs, list, quote and code are in it");
                Ok(!text.Contains("Advertisement") && !text.Contains("Ten best mice") && !text.Contains("Great article") && !text.Contains("newsletter") && !text.Contains("Reviews"), "ads, navigation, sidebar, comments and footer are left out");
                lay = Lay(); Ok(B(L(lay, "tabs")[ar], "reader") && !B(L(lay, "tabs")[ar], "viewVisible") && B(rd, "viewShown"), "the clean view replaces the page");
                var rf = Val(Cmd("test-p-reader-font")) ?? ""; Ok(rf.StartsWith("18 760"), "reader: 18 px text, medium width: " + rf);
                Cmd("test-p-reader " + ar); lay = Lay(); Ok(!B(L(lay, "tabs")[ar], "reader") && B(L(lay, "tabs")[ar], "viewVisible"), "leaving reader mode shows the page again");
                var na = js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-p-reader 0")) ?? "{}");
                Ok(!B(na, "ok") && !B(na, "on"), "a page that isn't an article stays as it is");

                // ---- mute
                var m1 = open(Lh + "/audio.html"); var m2 = open(Lh + "/audio.html");
                Cmd("test-eval " + m1 + " go()"); Cmd("test-eval " + m2 + " go()");
                var au = Wait(Tabs, t => (bool)t[m1]["audible"] && (bool)t[m2]["audible"], 10000);
                Ok(HallowBrowser.Tests.Silent.On || (bool)au[m1]["audible"] && (bool)au[m2]["audible"], "two tabs playing sound" + HallowBrowser.Tests.Silent.Note);
                lay = Lay(); Ok(HallowBrowser.Tests.Silent.On || B(L(lay, "tabs")[m1], "soundButton") && B(L(lay, "tabs")[m2], "soundButton"), "a speaker shows on audible tabs" + HallowBrowser.Tests.Silent.Note);
                Ok(Val(Cmd("test-p-mute-others " + m1)) != "0", "Mute all other tabs");
                var mu = Wait(Tabs, t => (bool)t[m2]["muted"], 5000);
                Ok((bool)mu[m2]["muted"] && !(bool)mu[m1]["muted"], "the other tab is muted, this one isn't");
                Cmd("test-p-sound-click " + m2); mu = Wait(Tabs, t => !(bool)t[m2]["muted"], 5000);
                Ok(!(bool)mu[m2]["muted"], "clicking the speaker unmutes the tab");
                Cmd("test-p-sound-click " + m1); mu = Wait(Tabs, t => (bool)t[m1]["muted"], 5000);
                Ok(HallowBrowser.Tests.Silent.On || (bool)mu[m1]["muted"], "clicking the speaker mutes the tab" + HallowBrowser.Tests.Silent.Note);
                var cr = Path.Combine(dir, "crashes"); Ok(!Directory.Exists(cr) || Directory.GetFiles(cr).Length == 0, "no crash reports");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                srv.Stop();
                var msg = fail == 0 ? "INTEGRATION power: ALL " + pass + " PASSED" : "INTEGRATION power: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
