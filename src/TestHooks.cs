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
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class TestHooks
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
            return "error unknown test command";
        }
        static List<HotRow> lastHot;
        static int ProcessCount() { try { return Engine.Env.GetProcessInfos().Count; } catch { return -1; } }
    }
}
