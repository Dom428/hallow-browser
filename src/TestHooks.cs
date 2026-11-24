// Hallow Browser - developer test commands on the family pipe. They exist ONLY when HALLOW_BROWSER_TESTDIR is set (a test folder
// with its own profile and pipes); the installed browser never answers them. Clicks are made by calling the same code the
// buttons call (the key in the address bar, a login in its list).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        // runs an async UI job and waits (up to 40 s) for its reply
        static string Async(BrowserWindow w, Func<Task<string>> f)
        {
            var tcs = new TaskCompletionSource<string>();
            w.Dispatcher.BeginInvoke(new Action(async () => { try { tcs.TrySetResult(await f()); } catch (Exception e) { tcs.TrySetResult("error " + e.GetType().Name + ": " + e.Message); } }));
            return tcs.Task.Wait(40000) ? tcs.Task.Result : "error timeout";
        }
        static Tab TabAt(BrowserWindow w, string s) { int i; return int.TryParse(s, out i) && i >= 0 && i < w.Tabs.Count ? w.Tabs[i] : null; }
        static string Split(string arg, out string rest) { var sp = arg.IndexOf(' '); rest = sp < 0 ? "" : arg.Substring(sp + 1); return sp < 0 ? arg : arg.Substring(0, sp); }

        public static string Run(BrowserWindow w, string cmd, string arg)
        {
            string rest;
            switch (cmd)
            {
                case "test-tabs":
                    return Program.OnUi(w, () => "ok " + js.Serialize(w.Tabs.Select((t, i) => new Dictionary<string, object> {
                        { "i", i }, { "url", t.Url }, { "title", t.Title }, { "active", t == w.Active }, { "suspended", t.Core != null ? (object)t.Core.IsSuspended : null }, { "asleep", t.Suspended },
                        { "discarded", t.Discarded }, { "audible", t.Audible }, { "muted", t.Muted }, { "loading", t.Loading }, { "ready", t.Ready }, { "poppedOut", t.PoppedOut },
                        { "keys", t.KeysItems == null ? -1 : t.KeysItems.Count } }).ToList()));
                case "test-state":
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "game", w.Game }, { "priority", w.PriorityName }, { "processPriority", Process.GetCurrentProcess().PriorityClass.ToString() },
                        { "shield", w.ShieldState }, { "shieldId", w.ShieldId }, { "shieldVersion", w.ShieldVersion }, { "shieldNote", w.ShieldNote }, { "fallbackBlocked", w.FallbackBlocked },
                        { "toast", w.LastToast }, { "zoom", w.CurrentZoom }, { "address", w.Address.Text }, { "closed", w.ClosedCount }, { "downloads", w.Downloads.Count } }));
                case "test-new":
                    return Program.OnUi(w, () => { var t = w.NewTab(arg.Length == 0 ? Omni.NewTabUrl : arg, true); return "ok " + w.Tabs.IndexOf(t); });
                case "test-nav":
                {
                    var t = TabAt(w, Split(arg, out rest)); if (t == null) return "error no tab";
                    return Async(w, async () => { if (t.Core == null) return "error no view"; await Panels.NavigateAndWait(t.Core, rest); return "ok"; });
                }
                case "test-type":   // the address bar, as if typed and Enter pressed
                    return Program.OnUi(w, () => { w.Navigate(arg, false); return "ok"; });
                case "test-activate": return Program.OnUi(w, () => { var t = TabAt(w, arg); if (t == null) return "error no tab"; w.Activate(t); return "ok"; });
                case "test-close": return Program.OnUi(w, () => { var t = TabAt(w, arg); if (t == null) return "error no tab"; w.CloseTab(t); return "ok"; });
                case "test-reopen": return Program.OnUi(w, () => { w.ReopenClosed(); return "ok"; });
                case "test-move": { var t = TabAt(w, Split(arg, out rest)); int to; if (t == null || !int.TryParse(rest, out to)) return "error"; return Program.OnUi(w, () => { w.MoveTab(t, to); return "ok"; }); }
                case "test-eval":
                {
                    var t = TabAt(w, Split(arg, out rest)); if (t == null) return "error no tab";
                    return Async(w, async () => t.Core == null ? "error no view" : "ok " + await Panels.Eval(t.Core, rest));
                }
                case "test-mute": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { if (t.Core == null) return "error no view"; t.Core.IsMuted = true; return "ok"; }); }
                case "test-capture":
                {
                    var t = TabAt(w, Split(arg, out rest)); if (t == null) return "error no tab";
                    return Async(w, async () =>
                    {
                        if (t.Core == null) return "error no view";
                        Directory.CreateDirectory(Path.GetDirectoryName(rest));
                        using (var f = File.Create(rest)) await t.Core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, f);
                        return "ok " + new FileInfo(rest).Length;
                    });
                }
                case "test-tick": return Program.OnUi(w, () => { w.Tick(true); return "ok"; });
                case "test-keys-click":   // the click on the key in the address bar
                    return Program.OnUi(w, () => { var st = Panels.Keys(w, null); return st == null ? "error no tab" : "ok " + js.Serialize(new Dictionary<string, object> { { "origin", st.Origin }, { "items", st.Items.Select(i => i.Username + "|" + i.Title).ToList() } }); });
                case "test-keys-pick":    // the click on a login in that list
                {
                    int n; if (!int.TryParse(arg, out n)) return "error";
                    return Async(w, async () => { var st = Panels.LastKeys; if (st == null || n >= st.Items.Count) return "error nothing to pick"; return "ok " + await Panels.Pick(w, st, st.Items[n]); });
                }
                case "test-keys-forced":  // a fill that was never clicked (must be refused)
                    return Async(w, async () => { var t = w.Active; return "ok " + await KeysLink.Fill(new KeysItem { Id = "t1", Title = "x", Username = "x" }, false, Omni.Origin(t.Url), () => t.Url, w.S.KeysOn, s => t.Core.ExecuteScriptAsync(s)); });
                case "test-shield-popup":
                    return Async(w, async () =>
                    {
                        var v = await Panels.Shield(w, null); if (v == null) return "error " + w.ShieldState + ": " + w.ShieldNote;
                        await Task.Delay(1500);
                        var text = await Panels.Eval(v.CoreWebView2, "document.body ? document.body.innerText.slice(0, 600) : ''");
                        if (arg.Length > 0) using (var f = File.Create(arg)) await v.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, f);
                        var res = "ok " + js.Serialize(new Dictionary<string, object> { { "note", Panels.ShieldTabNote }, { "url", v.CoreWebView2.Source }, { "text", js.Deserialize<string>(text) } });
                        Panels.HideShield();
                        return res;
                    });
                case "test-shield-eval":   // runs JavaScript on Shield's own popup page (to see which chrome.* APIs work)
                    return Async(w, async () => { var v = await Panels.Shield(w, null); if (v == null) return "error no shield"; var r = await Panels.Eval(v.CoreWebView2, arg); Panels.HideShield(); return "ok " + r; });
                case "test-pip": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { Pip.PopOut(w, t); return "ok"; }); }
                case "test-pip-back": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { Pip.Return(w, t); return "ok"; }); }
                case "test-pip-state": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { var pw = Pip.Window(t); return "ok " + js.Serialize(new Dictionary<string, object> { { "popped", t.PoppedOut }, { "topmost", pw != null && pw.Topmost }, { "window", pw != null } }); }); }
                case "test-perf":
                    return Program.OnUi(w, () => { Dictionary<string, double> p; lock (Engine.Perf) p = new Dictionary<string, double>(Engine.Perf); return "ok " + js.Serialize(new Dictionary<string, object> { { "marks", p }, { "memMb", w.MemoryMb() }, { "tabs", w.Tabs.Count }, { "processes", ProcessCount() } }); });
                case "test-settings":   // key=value for test runs (bool/int/string)
                {
                    var kv = arg.Split(new[] { '=' }, 2); if (kv.Length != 2) return "error";
                    return Program.OnUi(w, () =>
                    {
                        var f = typeof(BrowserSettings).GetField(kv[0]); if (f == null) return "error no field";
                        object v = f.FieldType == typeof(bool) ? (object)(kv[1] == "true") : f.FieldType == typeof(int) ? (object)int.Parse(kv[1]) : kv[1];
                        f.SetValue(w.S, v); w.S.Save(); return "ok";
                    });
                }
                case "test-find": Program.OnUi(w, () => w.FindFor(arg)); System.Threading.Thread.Sleep(1200); return Program.OnUi(w, () => "ok " + js.Serialize(w.FindCount));
                case "test-downloads": return Program.OnUi(w, () => "ok " + js.Serialize(w.Downloads.Select(d => new Dictionary<string, object> { { "name", d.Name }, { "state", d.State }, { "file", d.File } }).ToList()));
                case "test-zoom": return Program.OnUi(w, () => { w.Zoom(arg == "in" ? 1 : -1); return "ok " + w.CurrentZoom; });
                case "test-discard": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { w.Discard(t); return "ok"; }); }

                // ---- 1.1: the sidebar, hot tabs, the bandwidth cap, live notifications, light / dark
                case "test-sidebar":
                    return Program.OnUi(w, () => "ok " + js.Serialize(w.Side.Panels.Select((p, i) => new Dictionary<string, object> {
                        { "i", i }, { "id", p.Site.Id }, { "name", p.Site.Name }, { "url", p.Site.Url }, { "mobile", p.Site.Mobile }, { "width", p.Site.Width }, { "shown", p.Shown }, { "loaded", p.View != null },
                        { "ready", p.Ready }, { "asleep", p.Asleep }, { "suspended", p.Core != null ? (object)p.Core.IsSuspended : null }, { "audible", p.Audible }, { "unread", p.Unread }, { "title", p.Title },
                        { "ua", p.Core != null ? p.Core.Settings.UserAgent : null } }).ToList()));
                case "test-sidebar-add":   // <0|1 mobile> <name> <url>
                {
                    var mob = Split(arg, out rest); string url; var name = Split(rest, out url);
                    return Program.OnUi(w, () => { var p = w.Side.Add(new SidebarSite { Id = SidebarRules.NewId(name, w.S.SidebarSites), Name = name, Url = url, Mobile = mob == "1", Width = 400 }); return "ok " + w.Side.Panels.IndexOf(p); });
                }
                case "test-sidebar-show": return Program.OnUi(w, () => { int i; if (!int.TryParse(arg, out i) || i < 0 || i >= w.Side.Panels.Count) return "error no panel"; w.Side.Show(w.Side.Panels[i]); return "ok"; });
                case "test-sidebar-mute": return Program.OnUi(w, () => { int i; if (!int.TryParse(arg, out i) || i < 0 || i >= w.Side.Panels.Count || w.Side.Panels[i].Core == null) return "error no panel view"; w.Side.Panels[i].Core.IsMuted = true; return "ok"; });
                case "test-sidebar-hide": return Program.OnUi(w, () => { w.Side.Hide(); return "ok"; });
                case "test-sidebar-toggle": return Program.OnUi(w, () => { w.Side.Toggle(); return "ok " + w.S.SidebarOn; });
                case "test-sidebar-mobile": return Program.OnUi(w, () => { var a = arg.Split(' '); int i; if (a.Length < 2 || !int.TryParse(a[0], out i) || i < 0 || i >= w.Side.Panels.Count) return "error"; w.Side.SetMobile(w.Side.Panels[i], a[1] == "1"); return "ok"; });
                case "test-sidebar-backdate":   // <i> <minutes>: as if it had been hidden that long
                    return Program.OnUi(w, () => { var a = arg.Split(' '); int i, m; if (a.Length < 2 || !int.TryParse(a[0], out i) || !int.TryParse(a[1], out m) || i < 0 || i >= w.Side.Panels.Count) return "error"; w.Side.Panels[i].HiddenAt = DateTime.Now.AddMinutes(-m); return "ok"; });
                case "test-sidebar-eval":
                {
                    int i; if (!int.TryParse(Split(arg, out rest), out i)) return "error";
                    return Async(w, async () => { if (i < 0 || i >= w.Side.Panels.Count || w.Side.Panels[i].Core == null) return "error no panel view"; return "ok " + await Panels.Eval(w.Side.Panels[i].Core, rest); });
                }
                case "test-hot":   // two looks a second apart (CPU needs two)
                    return Async(w, async () => { await HotTabs.Sample(w); await Task.Delay(1000); var rows = await HotTabs.Sample(w); lastHot = rows; return "ok " + HotTabs.Json(rows); });
                case "test-hot-act":   // <row> sleep|close - the buttons in the flyout
                    return Program.OnUi(w, () => { var a = arg.Split(' '); int i; if (lastHot == null || a.Length < 2 || !int.TryParse(a[0], out i) || i < 0 || i >= lastHot.Count) return "error"; return "ok " + HotTabs.Act(w, lastHot[i], a[1]); });
                case "test-cap": return Program.OnUi(w, () => "ok " + w.CapApplied);
                case "test-live":   // checks every channel now (on this thread, not the UI's)
                {
                    if (w.Live == null) return "error no watcher";
                    w.Live.RunOnce();
                    return "ok " + js.Serialize(new Dictionary<string, object> { { "seen", w.Live.LastSeen.ToDictionary(x => x.Key, x => (object)x.Value) }, { "notify", w.Live.LastNotify } });
                }
                case "test-look":
                    return Program.OnUi(w, () => { string scheme = null; try { var p = w.Profile; if (p != null) scheme = p.PreferredColorScheme.ToString(); } catch { } return "ok " + js.Serialize(new Dictionary<string, object> { { "light", Look.IsLight }, { "scheme", scheme }, { "bg", ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Bg")).Color.ToString() } }); });
            }
            if (cmd.StartsWith("test-p-")) return Power(w, cmd.Substring(7), arg);
            if (cmd.StartsWith("test-mm-")) return V1121(w, cmd.Substring(8), arg);   // 1.12.1 (before test-m-)
            if (cmd.StartsWith("test-h-")) return V113(w, cmd.Substring(7), arg);     // 1.13
            if (cmd.StartsWith("test-fav-")) return V114(w, cmd.Substring(9), arg);   // 1.14
            if (cmd.StartsWith("test-m-")) return MoveIn(w, cmd.Substring(7), arg);   // 1.3
            if (cmd.StartsWith("test-v-")) return V14(w, cmd.Substring(7), arg);      // 1.4
            if (cmd.StartsWith("test-x-")) return V15(w, cmd.Substring(7), arg);      // 1.5
            if (cmd.StartsWith("test-y-")) return V16(w, cmd.Substring(7), arg);      // 1.6
            if (cmd.StartsWith("test-z-")) return V17(w, cmd.Substring(7), arg);      // 1.7
            if (cmd.StartsWith("test-g-")) return V18(w, cmd.Substring(7), arg);      // 1.8
            if (cmd.StartsWith("test-a-")) return A19(w, cmd.Substring(7), arg);      // 1.9
            if (cmd.StartsWith("test-twr-")) return V1142(w, cmd.Substring(9), arg);  // 1.14.2: Report a Twitch problem
            if (cmd.StartsWith("test-pk-")) return V115(w, cmd.Substring(8), arg);    // 1.15: passkeys through Hallow Keys
            if (cmd.StartsWith("test-cws-")) return V116(w, cmd.Substring(9), arg);    // 1.16: Chrome Web Store installs (a local stand-in store)
            if (cmd.StartsWith("test-netlog")) { var a1162 = arg.Trim(); var sp1162 = a1162.IndexOf(' '); return V1162(w, sp1162 < 0 ? a1162 : a1162.Substring(0, sp1162), sp1162 < 0 ? "" : a1162.Substring(sp1162 + 1)); }       // 1.16.2: the page requests as they leave
            if (cmd.StartsWith("test-nt-")) return V1151(w, cmd.Substring(8), arg);   // 1.15.1: the new tab page (layout, names, Shield count, tiles)
            if (cmd.StartsWith("test-tw63-")) return V1163(w, cmd.Substring(10), arg);   // 1.16.3: channel extensions over the video, the cover check
            if (cmd.StartsWith("test-tw-")) return V110(w, cmd.Substring(8), arg);    // 1.10
            if (cmd.StartsWith("test-d-")) return V112(w, cmd.Substring(7), arg);     // 1.12
            return "error unknown test command";
        }

        // ---- 1.2: vertical tabs, groups, workspaces, sessions, split, Ctrl+K, history, screenshots, reader, mute
        static Dictionary<string, object> Layout(BrowserWindow w)
        {
            Func<FrameworkElement, double> width = e => e == null ? -1 : e.ActualWidth;
            return new Dictionary<string, object> {
                { "vertical", w.S.VerticalTabs }, { "collapsed", w.S.VTabsCollapsed }, { "sidebarRight", w.Side.Right }, { "workspace", w.CurrentSpace == null ? null : w.CurrentSpace.Name },
                { "workspaces", w.Spaces == null ? new List<object>() : w.Spaces.Items.Select(x => (object)new Dictionary<string, object> { { "name", x.Name }, { "tabs", x.Id == w.SpaceId ? w.Tabs.Count : WorkspaceRules.TabCount(x.Session) } }).ToList() },
                { "groups", w.Groups.Select(g => (object)new Dictionary<string, object> { { "name", g.Name }, { "color", g.Color }, { "collapsed", g.Collapsed }, { "tabs", w.Tabs.Select((t, i) => t.Group == g ? i : -1).Where(i => i >= 0).ToList() }, { "chipShown", g.Chip != null && g.Chip.Parent != null } }).ToList() },
                { "tabs", w.Tabs.Select((t, i) => (object)new Dictionary<string, object> { { "i", i }, { "url", t.Url }, { "title", t.Title }, { "group", t.Group == null ? null : t.Group.Name }, { "headerShown", t.Header.Visibility == Visibility.Visible && t.Header.Parent != null },
                    { "discarded", t.Discarded }, { "muted", t.Muted }, { "audible", t.Audible }, { "soundButton", t.SoundBtn.Visibility == Visibility.Visible },
                    { "readable", t.Readable }, { "reader", t.ReaderOn }, { "viewVisible", t.View != null && t.View.Visibility == Visibility.Visible }, { "viewWidth", t.View == null ? -1 : t.View.ActualWidth }, { "column", t.View == null ? -1 : Grid.GetColumn(t.View) }, { "active", t == w.Active } }).ToList() },
                { "split", w.SplitOn }, { "splitL", w.SplitOn ? w.Tabs.IndexOf(w.SplitL) : -1 }, { "splitR", w.SplitOn ? w.Tabs.IndexOf(w.SplitR) : -1 },
                { "contentWidth", w.ContentGrid.ActualWidth } };
        }
        static string Power(BrowserWindow w, string cmd, string arg)
        {
            string rest;
            switch (cmd)
            {
                case "layout": return Program.OnUi(w, () => "ok " + js.Serialize(Layout(w)));
                case "vertical": return Program.OnUi(w, () => { if ((arg == "on") != w.S.VerticalTabs) w.ToggleVerticalTabs(); return "ok"; });
                case "icons": return Program.OnUi(w, () => { if ((arg == "on") != w.S.VTabsCollapsed) w.ToggleVTabsCollapsed(); return "ok"; });
                case "group-new": { var t = TabAt(w, Split(arg, out rest)); if (t == null) return "error no tab"; return Program.OnUi(w, () => { var g = w.NewGroup(t, rest); return "ok " + w.Groups.IndexOf(g); }); }
                case "group-add": { var t = TabAt(w, Split(arg, out rest)); int g; if (t == null || !int.TryParse(rest, out g)) return "error"; return Program.OnUi(w, () => { if (g < 0 || g >= w.Groups.Count) return "error no group"; w.AddToGroup(t, w.Groups[g]); return "ok"; }); }
                case "group-color": { int g; var c = Split(arg, out rest); if (!int.TryParse(c, out g)) return "error"; return Program.OnUi(w, () => { w.Groups[g].Color = GroupRules.Normalize(rest); w.RenderStrip(); w.SaveSession(); return "ok"; }); }
                case "group-collapse": { int g; if (!int.TryParse(arg, out g)) return "error"; return Program.OnUi(w, () => { if (g < 0 || g >= w.Groups.Count) return "error no group"; w.ToggleCollapse(w.Groups[g]); return "ok " + w.Groups[g].Collapsed; }); }
                case "group-sleep": { int g; if (!int.TryParse(arg, out g)) return "error"; return Program.OnUi(w, () => { if (g < 0 || g >= w.Groups.Count) return "error no group"; return "ok " + w.SleepGroup(w.Groups[g]); }); }
                // a drag as the mouse does it: the tab moves to <index> (or onto group <g>'s label: "chip <g>"), then the drop
                case "drag":
                {
                    var t = TabAt(w, Split(arg, out rest)); if (t == null) return "error no tab";
                    return Program.OnUi(w, () =>
                    {
                        var dt = typeof(BrowserWindow).GetField("dropChip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var da = typeof(BrowserWindow).GetField("dragActive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        da.SetValue(w, true);
                        if (rest.StartsWith("chip ")) dt.SetValue(w, w.Groups[int.Parse(rest.Substring(5))]);
                        else { int to = int.Parse(rest); w.Tabs.Remove(t); w.Tabs.Insert(Math.Max(0, Math.Min(to, w.Tabs.Count)), t); }
                        w.DropTab(t); return "ok " + w.Tabs.IndexOf(t);
                    });
                }
                case "ws-new": return Async(w, async () => { var ws = w.NewWorkspace(arg, null); for (int i = 0; i < 100 && w.SpaceId != ws.Id; i++) await Task.Delay(50); await Task.Delay(300); return "ok " + ws.Name; });
                case "ws-switch": return Async(w, async () => { var ws = w.Spaces.Items.FirstOrDefault(x => x.Name == arg); if (ws == null) return "error no workspace"; await w.SwitchWorkspace(ws.Id); return "ok " + w.Tabs.Count; });
                case "session-save": return Program.OnUi(w, () => "ok " + w.SaveNamed(arg));
                case "session-restore": return Async(w, async () => { await w.RestoreSaved(arg); return "ok " + (w.CurrentSpace == null ? "" : w.CurrentSpace.Name); });
                case "split": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { w.AddToSplit(t); return "ok " + w.SplitOn; }); }
                case "split-swap": return Program.OnUi(w, () => { w.SwapSplit(); return "ok"; });
                case "split-close": return Program.OnUi(w, () => { w.CloseSplit(); return "ok"; });
                case "palette": return Program.OnUi(w, () => "ok " + js.Serialize(Palette.Search(w, arg, 12).Select(x => new Dictionary<string, object> { { "kind", x.Kind }, { "title", x.Title }, { "sub", x.Sub } }).ToList()));
                case "palette-run": { int n; if (!int.TryParse(Split(arg, out rest), out n)) return "error"; return Program.OnUi(w, () => { var r = Palette.Search(w, rest, 12); if (n >= r.Count) return "error no item"; r[n].Run(); return "ok " + r[n].Kind + ": " + r[n].Title; }); }
                case "history": return Program.OnUi(w, () => w.History == null ? "ok {\"count\":-1}" : "ok " + js.Serialize(new Dictionary<string, object> { { "count", w.History.Count }, { "loaded", w.History.Loaded }, { "first", w.History.All().Take(5).Select(h => h.Url + "|" + h.Title + "|" + h.Visits).ToList() } }));
                case "history-clear": return Program.OnUi(w, () => { w.ClearHistory(); return "ok"; });
                case "history-save": return Program.OnUi(w, () => { if (w.History != null) w.History.Save(); return "ok"; });
                case "bookmark": return Program.OnUi(w, () => { w.ToggleBookmark(w.Active); return "ok " + w.IsBookmarked(w.Active); });
                // shot <visible|full|region> <save|copy> [x y w h in fractions]
                case "shot":
                {
                    var p = arg.Split(' ');
                    Rect? r = null; if (p.Length >= 6) r = new Rect(double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture), double.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture), double.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture));
                    return Async(w, async () => "ok " + await Shots.Take(w, p[0], p.Length > 1 && p[1] == "save", r) + "|" + Shots.LastCopiedBytes);
                }
                case "reader":
                {
                    var t = TabAt(w, arg); if (t == null) return "error no tab";
                    return Async(w, async () =>
                    {
                        var d = await w.ToggleReaderAsync(t);
                        if (d == null) return "error no doc";
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "ok", d.Ok }, { "on", t.ReaderOn }, { "title", d.Title }, { "site", d.Site }, { "words", d.Words }, { "blocks", d.Blocks.Count }, { "kinds", d.Blocks.Select(b => b.T).Distinct().ToList() }, { "text", Reader.Text(d) }, { "viewShown", t.ReaderView != null && t.ReaderView.Visibility == Visibility.Visible } });
                    });
                }
                case "reader-font": return Program.OnUi(w, () => { var t = w.Active; if (t == null || t.ReaderView == null) return "error"; var v = (FlowDocumentScrollViewer)t.ReaderView.Tag; return "ok " + v.Document.FontSize + " " + v.Document.Tag + " " + (v.Document.Foreground is System.Windows.Media.SolidColorBrush ? ((System.Windows.Media.SolidColorBrush)v.Document.Foreground).Color.ToString() : ""); });
                case "mute-others": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => "ok " + w.MuteOthers(t)); }
                case "sound-click": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { if (t.SoundBtn.Visibility != Visibility.Visible) return "error no speaker"; t.SoundBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); return "ok"; }); }
            }
            return "error unknown power test command";
        }
        static List<HotRow> lastHot;
        static int ProcessCount() { try { return Engine.Env.GetProcessInfos().Count; } catch { return -1; } }
    }
}
