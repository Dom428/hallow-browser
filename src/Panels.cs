// Hallow Browser - the flyouts (menu, downloads, Hallow Keys, Shield, the quick panel), the Settings page and --snap's made-up page.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public static class Panels
    {
        static Popup open;
        static Popup Show(UIElement target, UIElement child, double width)
        {
            if (open != null) open.IsOpen = false;
            var p = Ui.Pop(target, Ui.Flyout(child, width)); p.Placement = PlacementMode.Bottom;
            p.HorizontalOffset = target is FrameworkElement ? ((FrameworkElement)target).ActualWidth - width : 0;
            open = p; p.IsOpen = true; return p;
        }
        public static void CloseFlyouts() { if (open != null) open.IsOpen = false; }
        public static Popup ShowFlyout(UIElement target, UIElement child, double width) { return Show(target, child, width); }
        static FrameworkElement Sep() { var b = new Border { Height = 1, Margin = new Thickness(6, 5, 6, 5) }; b.SetResourceReference(Border.BackgroundProperty, "LineDim"); return b; }
        static TextBlock Head(string text) { var t = Ui.Label(text); t.Margin = new Thickness(12, 8, 12, 6); return t; }

        // ---------------------------------------------------------------- the menu
        public static void Menu(BrowserWindow w, UIElement at)
        {
            var sp = new StackPanel(); Popup p = null;
            Action<Action> run = a => { p.IsOpen = false; a(); };
            sp.Children.Add(Ui.MenuRow("plus", "New tab", "Ctrl+T", () => run(() => w.NewTab(Omni.NewTabUrl, true))));
            sp.Children.Add(Ui.MenuRow("window-restore", "New window", "Ctrl+N", () => run(() => Program.OpenWindow(false, null, null))));          // 1.4
            sp.Children.Add(Ui.MenuRow("mask", "New private window", "Ctrl+Shift+N", () => run(() => Program.OpenWindow(true, null, null))));
            var re = Ui.MenuRow("rotate-left", "Reopen closed tab", "Ctrl+Shift+T", () => run(w.ReopenClosed)); re.IsEnabled = w.ClosedCount > 0; sp.Children.Add(re);
            sp.Children.Add(Sep());
            sp.Children.Add(Ui.MenuRow("download", "Downloads", "Ctrl+J", () => run(() => DownloadsFlyout(w, at))));
            sp.Children.Add(Ui.MenuRow("magnifying-glass", "Find in page", "Ctrl+F", () => run(w.ShowFind)));
            sp.Children.Add(Ui.MenuRow("up-right-from-square", "Pop out video", null, () => run(() => { if (w.Active != null) Pip.PopOut(w, w.Active); })));
            sp.Children.Add(Ui.MenuRow("table-columns", w.S.SidebarOn ? "Hide the sidebar" : "Show the sidebar", "Ctrl+Shift+S", () => run(() => w.Side.Toggle())));
            sp.Children.Add(Ui.MenuRow("fire", "Hot tabs (CPU and memory)", "Shift+Esc", () => run(() => HotTabs.Flyout(w, at))));
            sp.Children.Add(Sep());
            // 1.2: the power features
            sp.Children.Add(Ui.MenuRow("terminal", "Command palette", "Ctrl+K", () => run(() => Palette.Show(w))));
            sp.Children.Add(Ui.MenuRow("table-list", w.S.VerticalTabs ? "Tabs in the title bar" : "Vertical tabs", null, () => run(w.ToggleVerticalTabs)));
            sp.Children.Add(Ui.MenuRow("table-columns", w.SplitOn ? "Close split view" : "Split view with the next tab", "Ctrl+click", () => run(() => { if (w.SplitOn) w.CloseSplit(); else if (w.Active != null && w.Tabs.Count > 1) w.AddToSplit(w.Tabs[(w.Tabs.IndexOf(w.Active) + 1) % w.Tabs.Count]); })));
            sp.Children.Add(Ui.MenuRow("camera", "Screenshot", "Ctrl+Shift+X", () => run(w.ShowScreenshot)));
            var rd = Ui.MenuRow("book-open", w.Active != null && w.Active.ReaderOn ? "Leave reader mode" : "Reader mode", "F9", () => run(() => w.ToggleReader(w.Active))); rd.IsEnabled = w.Active != null && (w.Active.Readable || w.Active.ReaderOn); sp.Children.Add(rd);
            sp.Children.Add(Ui.MenuRow("bookmark", "Bookmarks manager", "Ctrl+Shift+O", () => run(w.ShowBookmarks)));
            sp.Children.Add(Ui.MenuRow("bookmark", w.S.BookmarksBar ? "Hide the bookmarks bar" : "Show the bookmarks bar", "Ctrl+Shift+B", () => run(w.ToggleBookmarksBar)));
            sp.Children.Add(Ui.MenuRow("file-import", "Import from another browser...", null, () => run(() => w.ShowImport(at))));
            sp.Children.Add(Ui.MenuRow("briefcase", "Workspaces and saved sessions", null, () => run(() => w.ShowWorkspaces(false))));
            // zoom row
            var zg = new Grid { Margin = new Thickness(12, 4, 8, 4) };
            zg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); zg.ColumnDefinitions.Add(new ColumnDefinition()); zg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zg.Children.Add(Ui.Icon("magnifying-glass-plus", 13, "Lilac"));
            var zl = new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(zl, 1); zg.Children.Add(zl);
            var zv = new TextBlock { Text = Math.Round(w.CurrentZoom * 100) + "%", VerticalAlignment = VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Center };
            var zr = Ui.Row(Ui.Tool("minus", "Zoom out (Ctrl+-)", () => { w.Zoom(-1); zv.Text = Math.Round(w.CurrentZoom * 100) + "%"; }, 11), zv, Ui.Tool("plus", "Zoom in (Ctrl++)", () => { w.Zoom(1); zv.Text = Math.Round(w.CurrentZoom * 100) + "%"; }, 11));
            Grid.SetColumn(zr, 2); zg.Children.Add(zr); sp.Children.Add(zg);
            sp.Children.Add(Ui.MenuRow("star", "Print...", "Ctrl+P", () => run(() => { if (w.Active != null && w.Active.Core != null) w.Active.Core.ShowPrintUI(); })));
            sp.Children.Add(Sep());
            sp.Children.Add(Ui.MenuRow("shield-halved", "Hallow Shield settings", null, () => run(() => OpenShieldSettings(w))));
            sp.Children.Add(Ui.MenuRow("puzzle-piece", "Extensions", null, () => run(() => w.NewTab(Omni.ExtensionsUrl, true))));              // 1.4
            sp.Children.Add(Ui.MenuRow("sliders", "Site settings", null, () => run(() => w.NewTab(Omni.SitesUrl, true))));
            sp.Children.Add(Ui.MenuRow("broom", "Clear browsing data...", "Ctrl+Shift+Del", () => run(() => ClearData(w))));
            sp.Children.Add(Ui.MenuRow("gear", "Settings", "Ctrl+,", () => run(() => w.NewTab(Omni.SettingsUrl, true))));
            if (w.Update != null) sp.Children.Add(Ui.MenuRow("download", "Update to " + w.Update.Version.ToString(2), null, () => run(() => DoUpdate(w))));
            sp.Children.Add(Sep());
            sp.Children.Add(Ui.MenuRow("xmark", "Exit", null, () => run(Program.Quit)));   // 1.4: every window (their tabs are kept for next time)
            p = Show(at, sp, 290);
        }
        public static void DoUpdate(BrowserWindow w)
        {
            w.SaveSession();
            var err = Updater.Install(w.Update);
            if (err != null) w.Toast(err, 8); else w.Toast("Updating - Hallow Browser restarts with your tabs.", 6);
        }

        public static void TabMenu(BrowserWindow w, Tab t)
        {
            var sp = new StackPanel(); Popup p = null;
            Action<Action> run = a => { p.IsOpen = false; a(); };
            sp.Children.Add(Ui.MenuRow("plus", "New tab to the right", null, () => run(() => w.NewTab(Omni.NewTabUrl, true, w.Tabs.IndexOf(t) + 1))));
            sp.Children.Add(Ui.MenuRow("arrow-rotate-right", "Reload", null, () => run(() => { if (t.Core != null) t.Core.Reload(); })));
            sp.Children.Add(Ui.MenuRow("plus", "Duplicate", null, () => run(() => w.Duplicate(t))));
            sp.Children.Add(Ui.MenuRow(t.Muted ? "volume-high" : "volume-xmark", t.Muted ? "Unmute site" : "Mute tab", "Ctrl+Shift+M", () => run(() => w.ToggleMute(t))));
            sp.Children.Add(Ui.MenuRow("volume-xmark", "Mute all other tabs", null, () => run(() => w.MuteOthers(t))));
            sp.Children.Add(Ui.MenuRow("up-right-from-square", t.PoppedOut ? "Bring video back" : "Pop out video", null, () => run(() => { if (t.PoppedOut) Pip.Return(w, t); else Pip.PopOut(w, t); })));
            sp.Children.Add(Ui.MenuRow("moon", "Put to sleep", null, () => run(() => w.Discard(t))));
            // 1.2: groups, split view, workspaces, vertical tabs
            sp.Children.Add(Sep());
            sp.Children.Add(Ui.MenuRow("layer-group", "Add to a new group", null, () => run(() => w.NewGroup(t, null))));
            foreach (var g in w.Groups.ToList()) { var gg = g; if (t.Group != g) sp.Children.Add(Ui.MenuRow("layer-group", "Add to " + g.Name, null, () => run(() => w.AddToGroup(t, gg)))); }
            if (t.Group != null) sp.Children.Add(Ui.MenuRow("layer-group", "Remove from " + t.Group.Name, null, () => run(() => w.RemoveFromGroup(t))));
            if (w.SplitOn && (t == w.SplitL || t == w.SplitR)) { sp.Children.Add(Ui.MenuRow("right-left", "Swap the split sides", null, () => run(w.SwapSplit))); sp.Children.Add(Ui.MenuRow("xmark", "Close split view", null, () => run(w.CloseSplit))); }
            else if (t != w.Active) sp.Children.Add(Ui.MenuRow("table-columns", "Split view with this tab", "Ctrl+click", () => run(() => w.AddToSplit(t))));
            if (w.Spaces != null) foreach (var ws in w.Spaces.Items.Where(x => x.Id != w.SpaceId).ToList()) { var id = ws.Id; sp.Children.Add(Ui.MenuRow("briefcase", "Move to " + ws.Name, null, () => run(() => w.MoveToWorkspace(t, id)))); }
            sp.Children.Add(Ui.MenuRow("table-list", w.S.VerticalTabs ? "Tabs in the title bar" : "Vertical tabs", null, () => run(w.ToggleVerticalTabs)));
            sp.Children.Add(Sep());
            sp.Children.Add(Ui.MenuRow("xmark", "Close tab", "Ctrl+W", () => run(() => w.CloseTab(t))));
            sp.Children.Add(Ui.MenuRow("xmark", "Close other tabs", null, () => run(() => w.CloseOthers(t))));
            sp.Children.Add(Ui.MenuRow("rotate-left", "Reopen closed tab", "Ctrl+Shift+T", () => run(w.ReopenClosed)));
            if (open != null) open.IsOpen = false;
            p = new Popup { Child = Ui.Flyout(sp, 250), PlacementTarget = t.Header, Placement = PlacementMode.MousePoint, StaysOpen = false, AllowsTransparency = true };
            open = p; p.IsOpen = true;
        }

        // ---------------------------------------------------------------- downloads
        static StackPanel dlList; static BrowserWindow dlWin;
        public static void DownloadsFlyout(BrowserWindow w, UIElement at)
        {
            w.SeenDownloads();
            dlWin = w; dlList = new StackPanel();
            var sp = new StackPanel(); sp.Children.Add(Head("Downloads")); sp.Children.Add(dlList);
            sp.Children.Add(Sep());
            sp.Children.Add(Ui.MenuRow("folder-open", "Open the Downloads folder", null, () => { try { Process.Start(Engine.Downloads()); } catch { } }));
            RefreshDownloads(w);
            Show(at, sp, 360);
        }
        public static void RefreshDownloads(BrowserWindow w)
        {
            if (dlList == null || dlWin != w || open == null || !open.IsOpen) return;
            dlList.Children.Clear();
            if (w.Downloads.Count == 0) { var m = Ui.Muted("Nothing downloaded yet.", 12); m.Margin = new Thickness(12, 2, 12, 8); dlList.Children.Add(m); return; }
            foreach (var d in w.Downloads.Take(8))
            {
                var dd = d;
                var g = new Grid { Margin = new Thickness(12, 4, 8, 6) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel();
                info.Children.Add(new TextBlock { Text = d.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                var line = d.State == "running" ? (d.Total > 0 ? Ui.Size(d.Got) + " of " + Ui.Size(d.Total) : Ui.Size(d.Got)) : d.State == "done" ? Ui.Size(d.Got > 0 ? d.Got : d.Total) + " - done" : d.State;
                info.Children.Add(Ui.Muted(line, 11));
                if (d.State == "running" && d.Total > 0)
                {
                    var bar = new ProgressBar { Height = 3, Maximum = d.Total, Value = d.Got, Margin = new Thickness(0, 4, 0, 0), BorderThickness = new Thickness(0) };
                    bar.SetResourceReference(Control.ForegroundProperty, "AccentBrush"); bar.SetResourceReference(Control.BackgroundProperty, "LineDim"); info.Children.Add(bar);
                }
                g.Children.Add(info);
                var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(acts, 1);
                if (d.State == "done") { acts.Children.Add(Ui.Tool("up-right-from-square", "Open", () => { try { Process.Start(dd.File); } catch { } }, 12)); acts.Children.Add(Ui.Tool("folder-open", "Show in folder", () => { try { Process.Start("explorer.exe", "/select,\"" + dd.File + "\""); } catch { } }, 12)); }
                if (d.State == "running") acts.Children.Add(Ui.Tool("xmark", "Cancel", () => { try { dd.Op.Cancel(); } catch { } }, 12));
                g.Children.Add(acts);
                dlList.Children.Add(g);
            }
        }

        // ---------------------------------------------------------------- Hallow Keys (the key in the address bar)
        public static KeysFlyoutState LastKeys;
        public class KeysFlyoutState { public Tab Tab; public string Origin; public List<KeysItem> Items; public Popup Popup; }
        // the click on the key: the page's origin is pinned now
        public static KeysFlyoutState Keys(BrowserWindow w, UIElement at)
        {
            var t = w.Active; if (t == null) return null;
            var st = new KeysFlyoutState { Tab = t, Origin = Omni.Origin(t.Url), Items = t.KeysItems ?? new List<KeysItem>() };
            var sp = new StackPanel(); sp.Children.Add(Head("Hallow Keys"));
            if (t.KeysLocked || st.Items.Count == 0)
            {
                var m = Ui.Muted(t.KeysLocked ? "Hallow Keys is locked. Unlock it, then click the key again." : "No logins for this site.", 12); m.Margin = new Thickness(12, 0, 12, 8); sp.Children.Add(m);
            }
            foreach (var it in st.Items)
            {
                var item = it;
                var row = Ui.MenuRow("key", (it.Username.Length > 0 ? it.Username : "(no username)") + "  ·  " + it.Title, null, null);
                row.Click += async (a, b) => { st.Popup.IsOpen = false; var msg = await Pick(w, st, item); w.Toast(msg, 4); };
                sp.Children.Add(row);
            }
            var note = Ui.Muted("Fills only on " + (st.Origin ?? "this page") + ", only when you pick a login.", 10.5); note.Margin = new Thickness(12, 6, 12, 6); sp.Children.Add(note);
            st.Popup = at != null && PresentationSource.FromVisual(at) != null ? Show(at, sp, 340) : null;
            LastKeys = st;
            return st;
        }
        // the click on a login
        public static Task<string> Pick(BrowserWindow w, KeysFlyoutState st, KeysItem item)
        {
            var t = st.Tab;
            return KeysLink.Fill(item, true, st.Origin, () => t.Core != null ? t.Core.Source : t.Url, w.S.KeysOn,
                script => t.Core == null ? Task.FromResult<string>(null) : t.Core.ExecuteScriptAsync(script));
        }

        // ---------------------------------------------------------------- Hallow Shield: its own popup in a small window under the button
        static Window shieldWin; static WebView2 shieldView; public static string ShieldTabNote;
        public static async Task<WebView2> Shield(BrowserWindow w, FrameworkElement at)
        {
            if (w.ShieldState != "extension" || w.ShieldId == null)
            {
                var sp = new StackPanel(); sp.Children.Add(Head("Hallow Shield"));
                var m = Ui.Muted(w.ShieldState == "fallback" ? w.ShieldNote + "\nBlocked so far: " + w.FallbackBlocked : w.ShieldNote ?? "Shield is starting...", 12); m.Margin = new Thickness(12, 0, 12, 10); sp.Children.Add(m);
                if (at != null) Show(at, sp, 340);
                return null;
            }
            if (shieldWin != null && shieldWin.Owner != w) { try { shieldWin.Owner = w; } catch { } }   // 1.4: the window you clicked in
            if (shieldWin == null)
            {
                shieldWin = new Window { Owner = w, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Width = 324, Height = 600, Topmost = false };
                shieldWin.SetResourceReference(Window.BackgroundProperty, "Panel");
                var border = new Border { BorderThickness = new Thickness(1) }; border.SetResourceReference(Border.BorderBrushProperty, "Line");
                shieldView = new WebView2(); var c = (Color)w.FindResource("PageBg"); shieldView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                border.Child = shieldView; shieldWin.Content = border;
                shieldWin.Deactivated += (a, b) => { if (!BrowserSettings.Offscreen) shieldWin.Hide(); };
                shieldWin.KeyDown += (a, b) => { if (b.Key == Key.Escape) shieldWin.Hide(); };
                var me = shieldWin; shieldWin.Closed += (a, b) => { if (shieldWin == me) { shieldWin = null; shieldView = null; } };   // its owner window closed
            }
            if (BrowserSettings.Offscreen) { shieldWin.Left = -32000; shieldWin.Top = -32000; shieldWin.ShowActivated = false; }
            else if (at != null)
            {
                var pt = at.PointToScreen(new Point(at.ActualWidth, at.ActualHeight + 4));
                var src = PresentationSource.FromVisual(w); var m = src != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
                var p = m.Transform(pt); shieldWin.Left = p.X - shieldWin.Width; shieldWin.Top = p.Y;
            }
            shieldWin.Show(); if (!BrowserSettings.Offscreen) shieldWin.Activate();
            await shieldView.EnsureCoreWebView2Async(Engine.Env);
            var core = shieldView.CoreWebView2;
            // Shield's popup asks the browser for "the active tab"; in WebView2 that's this little window, so the popup is told the page's tab
            var pageUrl = w.Active == null ? null : w.Active.Url;
            await NavigateAndWait(core, w.ShieldPage("popup.html"));
            string tabId = null;
            try
            {
                var tabs = await Eval(core, "chrome.tabs.query({}).then(function(ts){return JSON.stringify(ts.map(function(t){return {id:t.id,url:t.url||t.pendingUrl||''};}));})");
                var list = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(new JavaScriptSerializer().Deserialize<string>(tabs) ?? "[]");
                var hit = list.FirstOrDefault(x => x.ContainsKey("url") && pageUrl != null && (x["url"] as string) == pageUrl);
                if (hit != null) tabId = hit["id"].ToString();
                ShieldTabNote = "tabs seen by Shield: " + list.Count + (hit != null ? ", page tab id " + tabId : ", the page's tab wasn't found");
            }
            catch (Exception e) { ShieldTabNote = "chrome.tabs.query failed: " + e.Message; }
            if (tabId != null) await NavigateAndWait(core, w.ShieldPage("popup.html") + "?tab=" + tabId);
            return shieldView;
        }
        public static void HideShield() { if (shieldWin != null) shieldWin.Hide(); }
        public static Task NavigateAndWait(CoreWebView2 core, string url)
        {
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> h = null;
            h = (a, b) => { core.NavigationCompleted -= h; tcs.TrySetResult(b.IsSuccess); };
            core.NavigationCompleted += h; core.Navigate(url);
            return Task.WhenAny(tcs.Task, Task.Delay(8000));
        }
        // evaluates JavaScript (awaiting promises) through the DevTools protocol; returns the value as JSON
        public static async Task<string> Eval(CoreWebView2 core, string expr)
        {
            var js = new JavaScriptSerializer();
            var r = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", js.Serialize(new Dictionary<string, object> { { "expression", expr }, { "awaitPromise", true }, { "returnByValue", true } }));
            var d = js.Deserialize<Dictionary<string, object>>(r);
            object res; if (d.TryGetValue("exceptionDetails", out res)) throw new Exception("script error: " + js.Serialize(res));
            var result = d["result"] as Dictionary<string, object>; object v;
            return result != null && result.TryGetValue("value", out v) ? js.Serialize(v) : "null";
        }
        public static void OpenShieldSettings(BrowserWindow w)
        {
            var u = w.ShieldPage("options.html");
            if (u == null) { w.Toast(w.ShieldNote ?? "Shield isn't loaded.", 6); return; }
            w.NewTab(u, true);
        }

        // ---------------------------------------------------------------- the quick panel: game mode and the Hallow family
        public static void Quick(BrowserWindow w, UIElement at)
        {
            var sp = new StackPanel();
            sp.Children.Add(Head("Game mode"));
            var gm = Ui.Muted(w.Game != null ? "On - " + w.Game + " is running. " + w.Tabs.Count(t => t.Suspended) + " tab(s) asleep, priority " + w.PriorityName + "." : w.S.GameLight ? "Waiting for a game. Background tabs sleep while you play." : "Off.", 12);
            gm.Margin = new Thickness(12, 0, 12, 6); sp.Children.Add(gm);
            var row = new StackPanel { Margin = new Thickness(12, 0, 12, 4) };
            row.Children.Add(Ui.Switch("Gaming-light mode", null, w.S.GameLight, on => { w.S.GameLight = on; w.S.Save(); w.Tick(true); }));
            row.Children.Add(Ui.Switch("CPU limiter", "The browser always runs at a lower priority", w.S.CpuLimiter, on => { w.S.CpuLimiter = on; w.S.Save(); w.Tick(true); }));
            sp.Children.Add(row);
            var mem = w.MemoryMb();
            var mm = Ui.Muted("Browser memory: " + mem + " MB" + (w.S.RamLimitMb > 0 ? " (limit " + w.S.RamLimitMb / 1024 + " GB)" : " (no limit)"), 11.5); mm.Margin = new Thickness(12, 0, 12, 6); sp.Children.Add(mm);
            var cap = Ui.Muted(w.CapApplied > 0 ? "Bandwidth cap on: " + (int)(w.CapApplied / 1024) + " KB/s per tab" : w.S.BandwidthKBps > 0 ? "Bandwidth cap: " + w.S.BandwidthKBps + " KB/s per tab" + (w.S.BandwidthGameOnly ? ", while a game runs" : "") : "Bandwidth cap: off", 11.5); cap.Margin = new Thickness(12, 0, 12, 6); sp.Children.Add(cap);
            Popup qp = null;
            sp.Children.Add(Ui.MenuRow("fire", "Hot tabs - what uses CPU and memory", "Shift+Esc", () => { if (qp != null) qp.IsOpen = false; HotTabs.Flyout(w, at); }));
            sp.Children.Add(Sep());
            sp.Children.Add(Head("Hallow"));
            int shown = 0;
            var guard = Family.Status("Guard");
            if (guard != null) { object sc, lb; guard.TryGetValue("score", out sc); guard.TryGetValue("scoreLabel", out lb); sp.Children.Add(Ui.MenuRow("shield-halved", "Guard: " + (sc ?? "?") + "/100" + (lb != null ? " - " + lb : ""), null, () => Family.Send("Guard", "open", 1500))); shown++; }
            var mixer = Family.Status("Mixer");
            if (mixer != null) { var np = NowPlaying(mixer); sp.Children.Add(Ui.MenuRow("music", np ?? "Hallow Mixer", null, () => Family.Send("Mixer", "open", 1500))); shown++; }
            if (Family.Running("Clips"))
            {
                Popup pp = null;
                sp.Children.Add(Ui.MenuRow("clapperboard", "Save a clip", null, () => { var r = Family.Send("Clips", "save", 8000); if (pp != null) pp.IsOpen = false; w.Toast(r != null && r.StartsWith("ok") ? "Clip saved." : "Clips: " + (Family.Error(r, "not running") ?? "saved"), 4); }));
                shown++;
                pp = open;
            }
            if (Family.Running("Hub")) { sp.Children.Add(Ui.MenuRow("gamepad", "Open Hallow Hub", null, () => Family.Send("Hub", "open", 1500))); shown++; }
            if (shown == 0) { var m = Ui.Muted("Other Hallow apps show up here when they're running (Guard score, Mixer, Clips, Hub).", 11.5); m.Margin = new Thickness(12, 0, 12, 8); sp.Children.Add(m); }
            qp = Show(at, sp, 330);
        }
        static string NowPlaying(Dictionary<string, object> mixer)
        {
            foreach (var k in new[] { "nowPlaying", "playing", "track" })
            {
                object v; if (!mixer.TryGetValue(k, out v) || v == null) continue;
                var d = v as Dictionary<string, object>;
                if (d != null) { object t, a; d.TryGetValue("title", out t); d.TryGetValue("artist", out a); if (t != null) return "Now playing: " + t + (a != null ? " - " + a : ""); }
                else if (v is string && ((string)v).Length > 0) return "Now playing: " + v;
            }
            return null;
        }

        // ---------------------------------------------------------------- clear browsing data
        public static void ClearData(BrowserWindow w)
        {
            var dlg = new Window { Owner = w, Title = "Clear browsing data", Width = 420, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
            dlg.SetResourceReference(Window.BackgroundProperty, "Panel"); dlg.SetResourceReference(Window.ForegroundProperty, "Text"); Ui.DarkTitle(dlg);
            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(Ui.T("Clear browsing data", 16, true, null, 6));
            sp.Children.Add(Ui.Muted("Everything this browser keeps for websites: history, cookies (you'll be signed out), cached files, site data, downloads list and the address-bar suggestions. Your settings, open tabs and Hallow Shield's settings stay.", 12, 14));
            var go = Ui.Btn("Clear everything", null, true, "broom");
            go.Click += async (a, b) => { go.IsEnabled = false; var p = w.Profile; if (p != null) await Program.ClearAll(w, p); dlg.Close(); w.Toast("Browsing data cleared.", 4); };
            sp.Children.Add(Ui.Row(go, Ui.Btn("Cancel", dlg.Close)));
            dlg.Content = sp; dlg.ShowDialog();
        }

        // ---------------------------------------------------------------- Settings (an internal tab)
        public static FrameworkElement SettingsPage(BrowserWindow w)
        {
            var S = w.S;
            var col = new StackPanel { MaxWidth = 760, Margin = new Thickness(28, 26, 28, 40), HorizontalAlignment = HorizontalAlignment.Center };
            var h = Ui.Row(new Image { Source = Ui.AppIcon(), Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) }, Ui.T("HALLOW", 22, true, "AccentBrush"), Ui.T(" BROWSER", 22, false, "Text"));
            col.Children.Add(h);
            var sub = Ui.Muted("SETTINGS // v" + Program.Version + "  -  engine: Microsoft Edge WebView2 " + (Engine.RuntimeVersion ?? "?"), 11, 18); sub.FontFamily = (FontFamily)w.FindResource("MonoFont"); col.Children.Add(sub);
            Func<string, UIElement[], Border> card = (title, items) => { var sp = new StackPanel(); sp.Children.Add(Ui.Label(title)); foreach (var i in items) sp.Children.Add(i); return Ui.Card(sp); };
            Action save = () => S.Save();

            col.Children.Add(card("Search", new UIElement[] {
                Ui.Muted("What the address bar searches with.", 12, 8),
                Ui.Chips(BrowserSettings.Engines.Select(e => new KeyValuePair<string, string>(e, Omni.EngineName(e))).ToList(), S.Search, v => { S.Search = v; save(); Engine.WriteNewTab(S, Program.AccentHex); }) }));

            col.Children.Add(card("Start-up", new UIElement[] {
                Ui.Switch("Open the tabs from last time", "Session restore: tabs you had open come back (each loads when you click it).", S.RestoreSession, on => { S.RestoreSession = on; save(); }) }));

            col.Children.Add(card("Tabs and workspaces", TabsSettings(w)));

            col.Children.Add(card("Bookmarks and new tab", w.BookmarkSettings()));   // 1.3

            col.Children.Add(card("Sync", w.SyncSettings()));                         // 1.3

            col.Children.Add(card("Privacy", new UIElement[] {
                Ui.Switch("Block third-party cookies", "Sites can't follow you around with cookies from other sites. Applies after a restart.", S.BlockThirdPartyCookies, on => { S.BlockThirdPartyCookies = on; save(); }),
                Ui.Switch("Ask sites not to track or sell my data", "Sends Global Privacy Control (Sec-GPC) and Do Not Track.", S.DoNotTrack, on => { S.DoNotTrack = on; save(); }),
                Ui.Switch("Strict tracking prevention", "Microsoft's tracking prevention on Strict instead of Balanced (may break some sites).", S.StrictTracking, on => { S.StrictTracking = on; save(); w.ApplyPageColors(); }),
                Ui.Switch("Clear browsing data when I close the browser", null, S.ClearOnExit, on => { S.ClearOnExit = on; save(); }),
                Ui.Switch("Keep a history of the pages I visit", "Only on this PC (in the profile folder), so Ctrl+K can find them again. At most 5,000 pages.", S.KeepHistory, on => { S.KeepHistory = on; save(); }),
                Ui.Row(Ui.Btn("Clear browsing data now...", () => ClearData(w), false, "broom"), Ui.Btn("Clear history", () => w.ClearHistory(), false, "clock-rotate-left")),
                Ui.Muted("The page engine is Microsoft's WebView2 runtime, which has its own diagnostic data settings (Windows Settings > Privacy > Diagnostics). Hallow Browser has no server and sends nothing anywhere.", 11.5, 0) }));

            // 1.4: HTTPS-only and secure DNS, site settings, extensions, private windows, the game corner
            col.Children.Add(card("Security", w.SecuritySettings()));
            col.Children.Add(card("Sites, extensions and windows", new UIElement[] {
                Ui.Muted("Sites ask before they use your camera, microphone, location, notifications and so on. Your answers, and the pop-ups, sound and JavaScript you set per site, are in Site settings (the padlock in the address bar shows the current site's).", 12, 8),
                Ui.Row(Ui.Btn("Site settings", () => w.NewTab(Omni.SitesUrl, true), false, "sliders"), Ui.Btn("Extensions", () => w.NewTab(Omni.ExtensionsUrl, true), false, "puzzle-piece"), Ui.Btn("New private window", () => Program.OpenWindow(true, null, null), false, "mask")),
                Ui.Muted("Ctrl+N opens a new window and Ctrl+Shift+N a private one (nothing kept: no history, session, bookmark or sync changes - downloads stay). Drag a tab out of the strip to give it its own window, or into another window's strip.", 11.5, 0) }));
            col.Children.Add(card("Game corner", w.GameCornerSettings()));

            col.Children.Add(card("Look", new UIElement[] {
                Ui.Muted("The browser", 12, 6),
                Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("follow", "Follow Hallow"), new KeyValuePair<string, string>("light", "Light"), new KeyValuePair<string, string>("dark", "Dark") }, S.Look, v => { S.Look = v; save(); w.Dispatcher.BeginInvoke(new Action(Program.ReloadTheme)); }),
                Ui.Muted("Follow Hallow = the light / dark mode picked in Hallow Toolkit (theme.json), or Windows' when that says \"Same as Windows\".", 11.5, 10),
                Ui.Muted("Pages", 12, 6),
                Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("follow", "Follow the browser"), new KeyValuePair<string, string>("dark", "Dark when the site allows"), new KeyValuePair<string, string>("auto", "Like Windows"), new KeyValuePair<string, string>("light", "Light") }, S.PageColors, v => { S.PageColors = v; save(); w.ApplyPageColors(); }),
                Ui.Switch("Use the Hallow accent", "The colour picked in Hallow Toolkit (theme.json), live.", S.UseHallowAccent, on => { S.UseHallowAccent = on; save(); Program.ReloadTheme(); }) }));

            col.Children.Add(card("Speed and gaming", new UIElement[] {
                Ui.Switch("Gaming-light mode", "While a game runs: background tabs sleep, the browser drops to a lower priority. Tabs playing sound stay awake.", S.GameLight, on => { S.GameLight = on; save(); w.Tick(true); }),
                Ui.Switch("CPU limiter", "The browser always runs below normal priority, so games and other apps come first.", S.CpuLimiter, on => { S.CpuLimiter = on; save(); w.Tick(true); }),
                Ui.Muted("Put background tabs to sleep after", 12, 6),
                Ui.Chips(BrowserSettings.SleepChoices.Select(m => new KeyValuePair<int, string>(m, m == 0 ? "Never" : m + " min")).ToList(), S.SleepMinutes, v => { S.SleepMinutes = v; save(); }),
                Ui.Muted("RAM limiter: above this, the least recently used tabs sleep, then unload (they reload when you click them)", 12, 6),
                Ui.Chips(BrowserSettings.RamChoices.Select(m => new KeyValuePair<int, string>(m, m == 0 ? "Off" : m / 1024 + " GB")).ToList(), S.RamLimitMb, v => { S.RamLimitMb = v; save(); w.Tick(true); }),
                Ui.Switch("Warm up the connection to the top suggestion", "Starts connecting while you type, so the page opens sooner.", S.Preconnect, on => { S.Preconnect = on; save(); }),
                Ui.Muted("Bandwidth limiter: a download cap for each tab and panel", 12, 6),
                Ui.Chips(BrowserSettings.BandwidthChoices.Select(k => new KeyValuePair<int, string>(k, k == 0 ? "Off" : k >= 1024 ? k / 1024 + " MB/s" : k + " KB/s")).ToList(), S.BandwidthKBps, v => { S.BandwidthKBps = v; save(); w.Tick(true); }),
                Ui.Switch("Only while a game runs", "Off = always.", S.BandwidthGameOnly, on => { S.BandwidthGameOnly = on; save(); w.Tick(true); }),
                Ui.Muted("Honestly: WebView2 can't throttle the whole browser. The cap uses the engine's network emulation (DevTools) in every tab and panel separately - five busy tabs can use five times the cap - and it doesn't cover the engine's own traffic or other apps.", 11.5, 0) }));

            col.Children.Add(card("Sidebar", SidebarSettings(w)));

            col.Children.Add(card("Live notifications", LiveSettings(w)));

            var shieldInfo = w.ShieldState == "extension" ? "Hallow Shield " + w.ShieldVersion + " is built in and always on (pause it per site in its panel)." : w.ShieldNote ?? "Starting...";
            col.Children.Add(card("Hallow Shield", new UIElement[] {
                Ui.Muted(shieldInfo, 12, 8),
                Ui.Row(Ui.Btn("Shield settings", () => OpenShieldSettings(w), true, "shield-halved"), Ui.Btn("Check for a Shield update", () => { w.Toast("Checking...", 2); Task.Run(() => ShieldHost.CheckForUpdate()).ContinueWith(r => w.Dispatcher.BeginInvoke(new Action(() => w.Toast(r.Result ?? "Shield is up to date.", 5)))); }, false, "arrows-rotate")),
                new Border { Height = 8 },
                Ui.Switch("Get new Shield versions between browser updates", "From Shield's own GitHub release, checked against GitHub's sha256.", S.ShieldAutoUpdate, on => { S.ShieldAutoUpdate = on; save(); }),
                Ui.Switch("Share Shield's counts with Hallow Hub and Remote", "Blocked today / this week and cookie banners answered go to %APPDATA%\\HallowShield\\status.json on this PC (the file Shield's family bridge writes) about every 5 minutes. Counts only - never a site.", S.ShareShieldCounts, on => { S.ShareShieldCounts = on; save(); if (on) { var _ = w.RelayShield(); } }),
                Ui.Muted(w.ShieldCounts ? "\"Blocked on this page\" works: Shield is loaded with the feedback permission it asks for (WebView2 has no permission prompt, so the browser grants it in its own copy of Shield)." : "", 11, 0) }));

            col.Children.Add(card("Hallow Keys", new UIElement[] {
                Ui.Switch("Fill logins from Hallow Keys", "A key shows in the address bar when Keys has a login for the page. Nothing is filled until you click it and pick a login, and only on that exact site.", S.KeysOn, on => { S.KeysOn = on; save(); }) }));

            col.Children.Add(card("About", new UIElement[] {
                Ui.Muted("Hallow Browser " + Program.Version + " - a Chromium browser on Microsoft Edge WebView2 (runtime " + (Engine.RuntimeVersion ?? "?") + ", SDK " + Program.SdkVersion + ").", 12, 4),
                Ui.Muted("Hallow Browser never makes itself your default browser. To use it for links, pick it in Windows Settings > Apps > Default apps.", 12, 8),
                Ui.Row(Ui.Btn("Check for updates", () => { w.Toast("Checking...", 2); Task.Run(() => Updater.Latest()).ContinueWith(r => w.Dispatcher.BeginInvoke(new Action(() => { if (Updater.IsNewer(r.Result)) { w.Update = r.Result; w.Toast("Hallow Browser " + r.Result.Version.ToString(2) + " is out - open the menu to update.", 6); } else w.Toast(r.Result == null ? "GitHub couldn't be reached." : "You have the newest version.", 4); }))); }, false, "arrows-rotate"),
                        Ui.Btn("Open data folder", () => { try { Process.Start(BrowserSettings.Dir); } catch { } }, false, "folder-open")),
                new Border { Height = 8 },
                Ui.Muted("Credits: Microsoft Edge WebView2 SDK (Microsoft.Web.WebView2 " + Program.SdkVersion + ", BSD-style licence, redistributed as allowed); Hallow Shield's lists (see its LISTS-LICENSE); icons Font Awesome Free (CC BY 4.0).", 11, 0) }));

            var sv = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            sv.SetResourceReference(Control.BackgroundProperty, "Bg");
            return sv;
        }

        // ---------------------------------------------------------------- 1.2: Settings > Tabs and workspaces
        static UIElement[] TabsSettings(BrowserWindow w)
        {
            var S = w.S; var items = new List<UIElement>();
            items.Add(Ui.Switch("Vertical tabs", "Tabs in a column on the left (with their site icons) instead of the title bar. Also in the tab's right-click menu.", S.VerticalTabs, on => { if (on != S.VerticalTabs) w.ToggleVerticalTabs(); }));
            items.Add(Ui.Switch("Icons only", "The tab column shows just the site icons (hover for the title).", S.VTabsCollapsed, on => { if (on != S.VTabsCollapsed) w.ToggleVTabsCollapsed(); }));
            items.Add(Ui.Muted("Tab groups: right-click a tab > Add to a new group. Click a group's name to fold it away, right-click it to rename, recolour, put the whole group to sleep or ungroup; drag a tab onto it to add the tab. Groups are saved with the session.", 11.5, 10));
            var names = w.Spaces == null ? "Default" : string.Join(", ", w.Spaces.Items.Select(x => x.Name + (x.Id == w.SpaceId ? " (here)" : "")));
            items.Add(Ui.Muted("Workspaces: " + names, 12, 4));
            items.Add(Ui.Muted("Separate sets of tabs and groups (e.g. Gaming, Work) on the same profile, so you stay signed in everywhere. Switch, add, rename and save sessions with the switcher at the top left.", 11.5, 6));
            items.Add(Ui.Row(Ui.Btn("Workspaces and saved sessions...", () => w.ShowWorkspaces(false), false, "briefcase")));
            items.Add(new Border { Height = 10 });
            items.Add(Ui.Muted("Reader mode (F9): text size", 12, 6));
            items.Add(Ui.Chips(BrowserSettings.ReaderSizes.Select(x => new KeyValuePair<int, string>(x, x + " px")).ToList(), S.ReaderFontSize, v => { S.ReaderFontSize = v; S.Save(); }));
            items.Add(Ui.Muted("Reader mode: text width", 12, 6));
            items.Add(Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("narrow", "Narrow"), new KeyValuePair<string, string>("medium", "Medium"), new KeyValuePair<string, string>("wide", "Wide") }, S.ReaderWidth, v => { S.ReaderWidth = v; S.Save(); }));
            items.Add(new Border { Height = 10 });
            items.Add(Ui.Muted("Screenshots (Ctrl+Shift+X) you save go to Pictures\\Hallow Browser.", 12, 6));
            items.Add(Ui.Row(Ui.Btn("Open the screenshots folder", () => { try { System.IO.Directory.CreateDirectory(Shots.Folder); Process.Start(Shots.Folder); } catch { } }, false, "folder-open")));
            return items.ToArray();
        }

        // ---------------------------------------------------------------- 1.1: Settings > Sidebar
        static UIElement[] SidebarSettings(BrowserWindow w)
        {
            var S = w.S; var items = new List<UIElement>();
            items.Add(Ui.Switch("Show the sidebar (Ctrl+Shift+S)", "Pinned sites beside the page. Each panel stays signed in like a tab (same profile) and loads only when you open it.", S.SidebarOn, on => { if (on != S.SidebarOn) w.Side.Toggle(); }));
            var list = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
            Action fill = null;
            fill = () =>
            {
                list.Children.Clear();
                foreach (var site in S.SidebarSites.ToList())
                {
                    var st = site;
                    var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.Children.Add(Ui.Icon(SidebarRules.Brand(st.Url) ?? "globe", 13, "Lilac"));
                    var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; tx.Children.Add(new TextBlock { Text = st.Name }); tx.Children.Add(Ui.Muted(st.Url + (st.Mobile ? "  -  phone version" : ""), 11));
                    Grid.SetColumn(tx, 1); g.Children.Add(tx);
                    var acts = Ui.Row(
                        Ui.Tool(st.Mobile ? "desktop" : "mobile-screen", st.Mobile ? "Use the desktop version" : "Use the phone version", () => { var p = w.Side.Panels.FirstOrDefault(x => x.Site == st); if (p != null) w.Side.SetMobile(p, !st.Mobile); else { st.Mobile = !st.Mobile; S.Save(); } fill(); }, 12),
                        Ui.Tool("trash-can", "Remove from the sidebar", () => { var p = w.Side.Panels.FirstOrDefault(x => x.Site == st); if (p != null) w.Side.Remove(p); else { S.SidebarSites.Remove(st); S.Save(); w.Side.Rebuild(); } fill(); }, 12));
                    Grid.SetColumn(acts, 2); g.Children.Add(acts);
                    list.Children.Add(g);
                }
                if (S.SidebarSites.Count == 0) list.Children.Add(Ui.Muted("No sites. Add one below (or put the usual ones back).", 12));
            };
            fill();
            items.Add(list);
            var box = new TextBox { Style = Ui.S("Field"), Width = 300, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            var msg = Ui.Muted("", 11.5);
            Action add = () => { var site = SidebarRules.FromAddress(box.Text, S.SidebarSites); if (site == null) { msg.Text = "That isn't a web address (http or https)."; return; } msg.Text = ""; box.Text = ""; w.Side.Add(site); fill(); };
            box.KeyDown += (a, b) => { if (b.Key == Key.Enter) { add(); b.Handled = true; } };
            items.Add(Ui.Row(box, Ui.Btn("Add a site", add, true, "plus"), Ui.Btn("Put the usual ones back", () => { foreach (var d in SidebarRules.Defaults()) if (!S.SidebarSites.Any(x => x.Id == d.Id)) S.SidebarSites.Add(d); S.Save(); w.Side.Rebuild(); fill(); }, false, "rotate-left")));
            items.Add(msg);
            items.Add(Ui.Muted("Side", 12, 6));
            items.Add(Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("auto", "Away from vertical tabs"), new KeyValuePair<string, string>("left", "Left"), new KeyValuePair<string, string>("right", "Right") }, S.SidebarSide, v => { S.SidebarSide = v; S.Save(); w.ApplyTabLayout(); }));
            items.Add(Ui.Muted("Hidden panels go to sleep after", 12, 6));
            items.Add(Ui.Chips(BrowserSettings.PanelSleepChoices.Select(m => new KeyValuePair<int, string>(m, m == 0 ? "Never" : m + " min")).ToList(), S.SidebarSleepMinutes, v => { S.SidebarSleepMinutes = v; S.Save(); }));
            items.Add(Ui.Muted("In game mode hidden panels sleep at once. A panel playing sound (Spotify, a stream) stays awake. Badges show counts from the page title, like \"(3) Discord\".", 11.5, 0));
            return items.ToArray();
        }

        // ---------------------------------------------------------------- 1.1: Settings > Live notifications
        static UIElement[] LiveSettings(BrowserWindow w)
        {
            var S = w.S; var items = new List<UIElement>();
            items.Add(Ui.Switch("Tell me when a channel I follow goes live", "Checks each channel's own page every 5 minutes (no account, no API key, no other service). Through Hallow Hub's icon when Hub runs, otherwise a Windows notification.", S.LiveOn, on => { S.LiveOn = on; S.Save(); if (on && w.Live != null) System.Threading.Tasks.Task.Run(() => w.Live.RunOnce()); }));
            var list = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
            Action fill = null;
            fill = () =>
            {
                list.Children.Clear();
                foreach (var ch in S.LiveChannels.ToList())
                {
                    var c = ch; string seen = null; if (w.Live != null) w.Live.LastSeen.TryGetValue(c, out seen);
                    var chip = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 3, 4, 3), Margin = new Thickness(0, 0, 6, 6) };
                    chip.SetResourceReference(Border.BorderBrushProperty, seen == "live" ? "Red" : "Line"); chip.SetResourceReference(Border.BackgroundProperty, "PanelHi");
                    var x = Ui.Tool("xmark", "Stop following", () => { S.LiveChannels = S.LiveChannels.Where(y => y != c).ToList(); S.Save(); fill(); }, 9); x.Width = 20; x.Height = 20;
                    chip.Child = Ui.Row(Ui.Icon(LiveCheck.IsTwitch(c) ? "twitch" : "youtube", 12, "Lilac"), new TextBlock { Text = "  " + LiveCheck.Name(c) + (seen == "live" ? "  - LIVE" : ""), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, x);
                    list.Children.Add(chip);
                }
                if (S.LiveChannels.Count == 0) list.Children.Add(Ui.Muted("No channels yet.", 12));
            };
            fill();
            items.Add(list);
            var box = new TextBox { Style = Ui.S("Field"), Width = 300, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            var msg = Ui.Muted("A Twitch name (shroud, twitch.tv/shroud) or a YouTube handle (@name, youtube.com/@name).", 11.5);
            Action add = () =>
            {
                var c = LiveCheck.Normalize(box.Text);
                if (c == null) { msg.Text = "That isn't a Twitch name or a YouTube @handle."; return; }
                if (!S.LiveChannels.Contains(c, StringComparer.OrdinalIgnoreCase)) S.LiveChannels = S.LiveChannels.Concat(new[] { c }).ToList();
                S.Save(); box.Text = ""; fill();
            };
            box.KeyDown += (a, b) => { if (b.Key == Key.Enter) { add(); b.Handled = true; } };
            items.Add(Ui.Row(box, Ui.Btn("Follow", add, true, "plus")));
            items.Add(msg);
            items.Add(Ui.Muted("Honestly: this reads Twitch's and YouTube's public channel pages, so it can stop working when they change those pages (until a browser update fixes it).", 11.5, 0));
            return items.ToArray();
        }

        // --snap: a made-up chat for the sidebar picture (WPF-drawn, no real site or person)
        public static FrameworkElement MockChat()
        {
            var g = new Grid(); g.SetResourceReference(Panel.BackgroundProperty, "Panel");
            var col = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            col.Children.Add(Ui.T("# squad", 13, true, "Text", 10));
            foreach (var m in new[] { new[] { "nova", "ready in 5? new map is up" }, new[] { "kite", "queueing now, bring the drone" }, new[] { "you", "2 min, finishing the patch notes" }, new[] { "nova", "gg last round" } })
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                row.Children.Add(Ui.T(m[0], 12, true, m[0] == "you" ? "AccentBrush" : "Lilac", 2));
                row.Children.Add(Ui.T(m[1], 12.5, false, "Text"));
                col.Children.Add(row);
            }
            col.Children.Add(Ui.Muted("Made-up chat for a screenshot", 11, 0));
            g.Children.Add(col); return g;
        }

        // ---------------------------------------------------------------- --snap: a made-up page (WPF-drawn, no real site)
        public static FrameworkElement MockPage(string kind)
        {
            var g = new Grid(); g.SetResourceReference(Panel.BackgroundProperty, "Bg");
            var col = new StackPanel { MaxWidth = 820, Margin = new Thickness(40, 34, 40, 20), HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(Ui.T("HALLOW NEWS", 12, true, "Lilac", 6));
            col.Children.Add(Ui.T("Season 7 patch notes: faster matchmaking, new maps and a quieter lobby", 26, true, "Text", 10));
            col.Children.Add(Ui.Muted("Made-up page for a screenshot  -  5 min read", 12, 18));
            var hero = new Border { Height = 210, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 18) };
            hero.Background = Look.IsLight ? new LinearGradientBrush(Color.FromRgb(0xE6, 0xDC, 0xF7), Color.FromRgb(0xF6, 0xF4, 0xFA), 35) : new LinearGradientBrush(Color.FromRgb(0x3A, 0x26, 0x5C), Color.FromRgb(0x14, 0x10, 0x1B), 35);
            var hs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            hs.Children.Add(Ui.Icon("gamepad", 64, "AccentBrush")); hero.Child = hs; col.Children.Add(hero);
            foreach (var p in new[] { "The new season brings two maps, a reworked lobby and matchmaking that finds a game in half the time. Here's everything that changed.",
                "Ranked play now starts at level 10, and the season pass has 20 more tiers. Teams of two can queue together in every mode.",
                "Performance: the new shader cache cuts loading times on most PCs, and the frame limiter now works in menus too." })
                col.Children.Add(Ui.T(p, 14.5, false, "Text", 12));
            var ad = new Border { Height = 70, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0) };
            ad.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            var adt = Ui.Muted("An ad was here - Hallow Shield blocked it", 12); adt.HorizontalAlignment = HorizontalAlignment.Center; adt.VerticalAlignment = VerticalAlignment.Center; ad.Child = adt;
            col.Children.Add(ad);
            g.Children.Add(col);
            return g;
        }
    }
}
