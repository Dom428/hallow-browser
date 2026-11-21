// Hallow Browser 1.1 - the hot tabs killer and the bandwidth limiter.
//  - Hot tabs: every page process of the engine (CoreWebView2Environment.GetProcessExtendedInfosAsync) with the tabs and sidebar
//    panels whose main frame lives in it (CoreWebView2.FrameId), its CPU (the process's processor time between two looks) and its
//    private memory. Pages that share a process show the same numbers, marked "shared". One click puts a tab to sleep or closes it.
//  - Bandwidth limiter: WebView2 has no throttle for the whole browser. What it does have, per WebView, is the DevTools protocol's
//    Network.emulateNetworkConditions - so the cap is applied to every tab and panel separately (each one gets the full cap), for
//    what its pages load (also downloads they start, as Chromium counts them). Not limited: the engine's own traffic outside pages
//    (updates, Shield's lists, service workers in some cases) and other apps.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public class HotRow
    {
        public string Kind;          // tab | panel | engine
        public string Name;
        public int Pid; public double Cpu = -1; public long MemMb; public bool Shared, Asleep, Active, Audible;
        public Tab Tab; public SidePanel Panel;
    }

    public static class HotTabs
    {
        static readonly Dictionary<int, TimeSpan> lastCpu = new Dictionary<int, TimeSpan>();
        static DateTime lastAt = DateTime.MinValue;

        // CPU % of the whole machine between two looks (null on the first look at a process)
        public static double? CpuPercent(TimeSpan before, TimeSpan after, TimeSpan wall, int cores)
        {
            if (wall.TotalMilliseconds < 50 || cores < 1 || after < before) return null;
            return Math.Max(0, Math.Min(100, (after - before).TotalMilliseconds / (wall.TotalMilliseconds * cores) * 100));
        }
        // hottest first: CPU, then memory
        public static List<HotRow> Sort(IEnumerable<HotRow> rows) { return rows.OrderByDescending(r => r.Kind == "engine" ? -1 : 0).ThenByDescending(r => Math.Max(0, r.Cpu)).ThenByDescending(r => r.MemMb).ToList(); }

        public static async Task<List<HotRow>> Sample(BrowserWindow w)
        {
            var rows = new List<HotRow>();
            if (Engine.Env == null) return rows;
            IReadOnlyList<CoreWebView2ProcessExtendedInfo> infos;
            try { infos = await Engine.Env.GetProcessExtendedInfosAsync(); } catch { return rows; }
            // the main frame of each tab and panel
            var owners = new Dictionary<uint, object>();
            foreach (var t in w.Tabs) { var c = t.Core; if (c != null) try { owners[c.FrameId] = t; } catch { } }
            foreach (var p in w.Side.Panels) { var c = p.Core; if (c != null) try { owners[c.FrameId] = p; } catch { } }
            var now = DateTime.Now; var wall = now - lastAt; lastAt = now;
            double engineCpu = 0; long engineMem = 0; int engineProcs = 0;
            var seen = new HashSet<int>();
            foreach (var x in infos)
            {
                int pid; CoreWebView2ProcessKind kind;
                try { pid = x.ProcessInfo.ProcessId; kind = x.ProcessInfo.Kind; } catch { continue; }
                seen.Add(pid);
                long mem = 0; double? cpu = null;
                try
                {
                    using (var pr = Process.GetProcessById(pid))
                    {
                        mem = pr.PrivateMemorySize64 / (1024 * 1024);
                        var tt = pr.TotalProcessorTime; TimeSpan b;
                        if (lastCpu.TryGetValue(pid, out b)) cpu = CpuPercent(b, tt, wall, Environment.ProcessorCount);
                        lastCpu[pid] = tt;
                    }
                }
                catch { continue; }
                var mine = new List<object>();
                if (kind == CoreWebView2ProcessKind.Renderer)
                {
                    try
                    {
                        foreach (var f in x.AssociatedFrameInfos)
                        {
                            if (f.ParentFrameInfo != null) continue;
                            object o; if (owners.TryGetValue(f.FrameId, out o) && !mine.Contains(o)) mine.Add(o);
                        }
                    }
                    catch { }
                }
                if (mine.Count == 0) { engineCpu += cpu ?? 0; engineMem += mem; engineProcs++; continue; }
                foreach (var o in mine)
                {
                    var r = new HotRow { Pid = pid, Cpu = cpu ?? -1, MemMb = mem, Shared = mine.Count > 1 };
                    var t = o as Tab; var p = o as SidePanel;
                    if (t != null) { r.Kind = "tab"; r.Tab = t; r.Name = t.Title; r.Asleep = t.Suspended; r.Active = t == w.Active; r.Audible = t.Audible; }
                    else { r.Kind = "panel"; r.Panel = p; r.Name = p.Site.Name + (string.IsNullOrWhiteSpace(p.Title) ? "" : " - " + p.Title); r.Asleep = p.Asleep; r.Active = p.Shown; r.Audible = p.Audible; }
                    rows.Add(r);
                }
            }
            foreach (var k in lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) lastCpu.Remove(k);
            rows.Add(new HotRow { Kind = "engine", Name = "Engine, GPU, extensions (" + engineProcs + " processes)", Cpu = engineCpu, MemMb = engineMem });
            return Sort(rows);
        }

        // the buttons
        public static string Act(BrowserWindow w, HotRow r, string what)
        {
            if (r.Tab != null)
            {
                if (what == "close") { w.CloseTab(r.Tab); return "closed"; }
                if (r.Tab == w.Active) return "the tab you're looking at stays awake";
                w.Discard(r.Tab); return r.Tab.Discarded ? "asleep" : "kept awake (playing sound or popped out)";
            }
            if (r.Panel != null)
            {
                if (what == "close") { w.Side.Unload(r.Panel); return "unloaded"; }
                if (r.Panel.Shown) w.Side.Hide();
                var _ = w.Side.Sleep(r.Panel); return "asleep";
            }
            return "the engine itself can't be closed from here";
        }

        // ---- the flyout (menu > Hot tabs, Shift+Esc): refreshed every 2 s while it's open
        public static Popup Flyout(BrowserWindow w, UIElement at)
        {
            var list = new StackPanel();
            var sp = new StackPanel();
            var head = Ui.Label("Hot tabs"); head.Margin = new Thickness(12, 8, 12, 2); sp.Children.Add(head);
            var sum = Ui.Muted("Looking...", 11); sum.Margin = new Thickness(12, 0, 12, 6); sp.Children.Add(sum);
            sp.Children.Add(new ScrollViewer { Content = list, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
            var note = Ui.Muted("CPU and memory per page process (tabs sharing one show the same numbers). Sleep frees most of a page's memory; it reloads when you open it.", 10.5); note.Margin = new Thickness(12, 6, 12, 6); sp.Children.Add(note);
            var pop = Panels.ShowFlyout(at, sp, 460);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            Func<Task> refresh = null;
            refresh = async () =>
            {
                var rows = await Sample(w);
                list.Children.Clear();
                double cpu = rows.Sum(r => Math.Max(0, r.Shared ? 0 : r.Cpu)); long mem = rows.Where(r => !r.Shared).Sum(r => r.MemMb) + rows.Where(r => r.Shared).GroupBy(r => r.Pid).Sum(g => g.First().MemMb);
                sum.Text = "Whole browser: " + (lastCpuValid ? cpu.ToString("0.0") + "% CPU, " : "") + mem + " MB  -  " + rows.Count(r => r.Kind != "engine") + " pages";
                foreach (var r in rows) list.Children.Add(Row(w, r, refresh));
                lastCpuValid = true;
            };
            timer.Tick += async (a, b) => { if (!pop.IsOpen) { timer.Stop(); return; } await refresh(); };
            pop.Closed += (a, b) => timer.Stop();
            lastCpuValid = false;
            var _ = refresh(); timer.Start();
            return pop;
        }
        static bool lastCpuValid;
        static FrameworkElement Row(BrowserWindow w, HotRow r, Func<Task> refresh)
        {
            var g = new Grid { Margin = new Thickness(10, 3, 6, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            var icon = Ui.Icon(r.Kind == "panel" ? "table-columns" : r.Kind == "engine" ? "microchip" : "globe", 12, r.Cpu >= 25 ? "Red" : r.Cpu >= 8 ? "Amber" : "Lilac");
            g.Children.Add(icon);
            var name = new TextBlock { Text = (r.Name ?? "?") + (r.Asleep ? "  (asleep)" : "") + (r.Shared ? "  (shared)" : ""), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = r.Name };
            if (r.Kind == "engine") name.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Grid.SetColumn(name, 1); g.Children.Add(name);
            var cpu = Ui.Muted(r.Cpu < 0 ? "-" : r.Cpu.ToString("0.0") + "%", 11.5); cpu.TextAlignment = TextAlignment.Right; cpu.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(cpu, 2); g.Children.Add(cpu);
            var mem = Ui.Muted(r.MemMb + " MB", 11.5); mem.TextAlignment = TextAlignment.Right; mem.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(mem, 3); g.Children.Add(mem);
            if (r.Kind != "engine")
            {
                var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var sl = Ui.Tool("moon", r.Kind == "panel" ? "Sleep this panel" : "Put this tab to sleep", async () => { w.Toast(Act(w, r, "sleep"), 3); await Task.Delay(300); await refresh(); }, 11); sl.Width = 26; sl.Height = 24;
                sl.IsEnabled = !r.Asleep && !(r.Tab != null && r.Active);
                var cl = Ui.Tool("xmark", r.Kind == "panel" ? "Unload this panel" : "Close this tab", async () => { Act(w, r, "close"); await Task.Delay(300); await refresh(); }, 11); cl.Width = 26; cl.Height = 24;
                acts.Children.Add(sl); acts.Children.Add(cl);
                Grid.SetColumn(acts, 4); g.Children.Add(acts);
            }
            return g;
        }
        // --snap: the flyout with made-up rows, drawn over the made-up page
        public static FrameworkElement SnapPanel()
        {
            var rows = new List<HotRow> {
                new HotRow { Kind = "tab", Name = "Lo-fi beats to game to", Cpu = 31.2, MemMb = 412, Audible = true },
                new HotRow { Kind = "panel", Name = "Twitch - hallowgg playing Delta Force", Cpu = 12.4, MemMb = 356 },
                new HotRow { Kind = "tab", Name = "Hallow News - patch notes", Cpu = 1.8, MemMb = 148, Active = true },
                new HotRow { Kind = "panel", Name = "Discord - #squad", Cpu = 0.6, MemMb = 231 },
                new HotRow { Kind = "tab", Name = "Delta Force - season guide", Cpu = 0, MemMb = 64, Asleep = true },
                new HotRow { Kind = "engine", Name = "Engine, GPU, extensions (5 processes)", Cpu = 2.1, MemMb = 298 } };
            var list = new StackPanel();
            var head = Ui.Label("Hot tabs"); head.Margin = new Thickness(12, 8, 12, 2); list.Children.Add(head);
            var sum = Ui.Muted("Whole browser: 48.1% CPU, 1509 MB  -  5 pages", 11); sum.Margin = new Thickness(12, 0, 12, 6); list.Children.Add(sum);
            foreach (var r in rows) list.Children.Add(Row(null, r, null));
            var b = Ui.Flyout(list, 460); b.HorizontalAlignment = HorizontalAlignment.Right; b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(0, 6, 10, 0);
            return b;
        }
        public static string Json(List<HotRow> rows)
        {
            return new JavaScriptSerializer().Serialize(rows.Select(r => new Dictionary<string, object> { { "kind", r.Kind }, { "name", r.Name }, { "pid", r.Pid }, { "cpu", r.Cpu }, { "memMb", r.MemMb }, { "shared", r.Shared }, { "asleep", r.Asleep } }).ToList());
        }
    }

    public static class Throttle
    {
        // the cap in bytes per second for DevTools (-1 = no cap): only while a game runs unless "always"
        public static double Want(int kbps, bool gameOnly, bool game) { return kbps <= 0 || (gameOnly && !game) ? -1 : kbps * 1024.0; }
        public static string Params(double bytesPerSecond)
        {
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "offline", false }, { "latency", 0 }, { "downloadThroughput", bytesPerSecond }, { "uploadThroughput", -1 } });
        }
        public static async Task Apply(CoreWebView2 c, double bytesPerSecond)
        {
            if (c == null) return;
            try
            {
                if (bytesPerSecond > 0) { await c.CallDevToolsProtocolMethodAsync("Network.enable", "{}"); await c.CallDevToolsProtocolMethodAsync("Network.emulateNetworkConditions", Params(bytesPerSecond)); }
                else { await c.CallDevToolsProtocolMethodAsync("Network.emulateNetworkConditions", Params(-1)); await c.CallDevToolsProtocolMethodAsync("Network.disable", "{}"); }
            }
            catch { }
        }
    }
}
