// Hallow Browser 1.5 - downloads: pause, resume and cancel; a speed limit while you play; Microsoft Defender checks each finished
// download (MpCmdRun custom scan of that one file, at Idle priority - Hallow Guard 1.x's pipe can only start a quick scan, so
// the browser asks Defender itself); hallow://downloads lists every download with Show in folder.
// The speed limit: WebView2 has no per-download throttle, and DevTools network emulation only slows downloads that start as a
// page navigation (measured: a link with "download" isn't slowed at all). So the limit pauses and resumes each download many times
// a second to hold the speed (pausing stops reading from the connection, so the sender slows down too) - measured: 1,048,576
// bytes in 4 s at a 256 KB/s limit, exactly on target.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    [DataContract]
    public class DownloadRecord
    {
        [DataMember] public string Name, File, State, Scan, Started;   // State: done | failed | cancelled | running; Scan: clean | threat | error | scanning | off
        [DataMember] public long Size;
        [DataMember] public string ScanNote;
    }
    [DataContract] public class DownloadList { [DataMember] public List<DownloadRecord> Items = new List<DownloadRecord>(); }

    // the downloads list (%APPDATA%\HallowBrowser\downloads.json): file names, folders, sizes, Defender's answer - never addresses.
    // Private windows' downloads aren't listed. Cleared with "Clear browsing data".
    public static class DownloadHistory
    {
        public static string FilePath { get { return Path.Combine(BrowserSettings.Dir, "downloads.json"); } }
        static List<DownloadRecord> items;
        public static List<DownloadRecord> Items { get { if (items == null) { var l = BrowserSettings.Read<DownloadList>(FilePath); items = l == null || l.Items == null ? new List<DownloadRecord>() : l.Items.Where(x => x != null && x.File != null).ToList(); } return items; } }
        public static void Add(DownloadRecord r) { Items.Insert(0, r); if (items.Count > 300) items.RemoveRange(300, items.Count - 300); Save(); }
        public static void Save() { if (items == null) return; BrowserSettings.WriteFile(FilePath, new DownloadList { Items = items.ToList() }, typeof(DownloadList)); }
        public static void Remove(DownloadRecord r) { Items.Remove(r); Save(); }
        public static void Clear() { items = new List<DownloadRecord>(); try { File.Delete(FilePath); } catch { } }
        public static void Use(List<DownloadRecord> made) { items = made; }   // --snap: made-up downloads (never read from or written to disk)
    }

    // Microsoft Defender's command-line scanner: the newest platform folder's MpCmdRun.exe (like Hallow Guard), one file, Idle priority
    public static class Defender
    {
        public static string Find()
        {
            if (BrowserSettings.TestDir != null) return BrowserSettings.MpCmdRunTest;   // tests: a stand-in only, never a real scan
            try
            {
                var plat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
                if (Directory.Exists(plat))
                {
                    var best = Directory.GetDirectories(plat).Select(d => { Version v; return new { d, v = Version.TryParse(Path.GetFileName(d).Split('-')[0], out v) ? v : null }; })
                        .Where(x => x.v != null && File.Exists(Path.Combine(x.d, "MpCmdRun.exe"))).OrderByDescending(x => x.v).FirstOrDefault();
                    if (best != null) return Path.Combine(best.d, "MpCmdRun.exe");
                }
            }
            catch { }
            var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            return File.Exists(pf) ? pf : null;
        }
        public static string Args(string file)
        {
            if (string.IsNullOrEmpty(file) || file.IndexOf('"') >= 0) throw new ArgumentException("bad path");
            return "-Scan -ScanType 3 -File \"" + file + "\"";
        }
        // MpCmdRun's exit code (0 = nothing found, 2 = threats found) and output -> clean | threat | error
        public static string Parse(int exit, IEnumerable<string> lines, out string name)
        {
            name = null; int found = 0;
            foreach (var l in lines ?? new string[0])
            {
                var m = Regex.Match(l ?? "", @"^\s*Threat\s*:\s*(.+?)\s*$"); if (m.Success && name == null) name = m.Groups[1].Value;
                var c = Regex.Match(l ?? "", @"found (\d+) threats?", RegexOptions.IgnoreCase); if (c.Success) found = Math.Max(found, int.Parse(c.Groups[1].Value));
            }
            if (exit == 2 || found > 0 || name != null) return "threat";
            return exit == 0 ? "clean" : "error";
        }
        // runs the scan (blocking; call it off the UI thread)
        public static string Scan(string file, out string note)
        {
            note = null;
            var exe = Find(); if (exe == null || !File.Exists(exe)) { note = "Microsoft Defender's scanner (MpCmdRun.exe) wasn't found"; return "error"; }
            if (!File.Exists(file)) { note = "the file is gone"; return "error"; }
            try
            {
                var psi = new ProcessStartInfo(exe, Args(file)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    try { p.PriorityClass = ProcessPriorityClass.Idle; } catch { }
                    var lines = new List<string>();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (lines) lines.Add(e.Data); };
                    p.BeginOutputReadLine();
                    if (!p.WaitForExit(10 * 60 * 1000)) { try { p.Kill(); } catch { } note = "the scan took too long"; return "error"; }
                    p.WaitForExit();
                    string name; var r = Parse(p.ExitCode, lines, out name);
                    note = r == "threat" ? (name ?? "a threat") : r == "error" ? "Defender's exit code " + p.ExitCode : null;
                    return r;
                }
            }
            catch (Exception e) { note = e.Message; return "error"; }
        }
    }

    // the limit while you play: how many bytes each running download may have by now (all downloads together get the limit)
    public static class DownloadLimit
    {
        public static bool ShouldPause(long gotNow, long gotAtStart, double secondsSince, double bytesPerSecondEach) { return gotNow > gotAtStart + (long)(bytesPerSecondEach * secondsSince); }
        public static double Each(int kbps, int running) { return kbps <= 0 || running <= 0 ? 0 : kbps * 1024.0 / running; }
        public static string Name(int kbps) { return kbps == 0 ? "No limit" : kbps < 0 ? "Pause them" : kbps >= 1024 ? kbps / 1024 + " MB/s" : kbps + " KB/s"; }
    }

    public partial class BrowserWindow
    {
        public static int ScansRun;   // tests
        DispatcherTimer dlLimitTimer;
        // a new download: our own list (never the engine's dialog), pause / resume / cancel, the limit while gaming, Defender at the end
        void OnDownload(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            var own = Program.WindowOf(sender as CoreWebView2); if (own != null && own != this) { own.OnDownload(sender, e); return; }   // 1.4: a tab moved here from another window
            e.Handled = true;
            var op = e.DownloadOperation;
            var d = new DownloadItem { Op = op, File = e.ResultFilePath, Name = Path.GetFileName(e.ResultFilePath), Total = (long)(op.TotalBytesToReceive ?? 0), Private = Private, Source = sender as CoreWebView2 };
            if (!Private) { d.Record = new DownloadRecord { Name = d.Name, File = d.File, State = "running", Started = DateTime.Now.ToString("s"), Size = d.Total }; DownloadHistory.Add(d.Record); }
            Downloads.Insert(0, d); dlBadge.Visibility = Visibility.Visible;
            op.BytesReceivedChanged += (a, b) => { d.Got = op.BytesReceived; d.Total = (long)(op.TotalBytesToReceive ?? (ulong)d.Got); Panels.RefreshDownloads(this); };
            op.StateChanged += (a, b) => DownloadStateChanged(d);
            Toast("Downloading " + d.Name + "...", 3);
            EnsureLimitTimer();
        }
        void DownloadStateChanged(DownloadItem d)
        {
            var op = d.Op; var st = op.State; var old = d.State;
            if (st == CoreWebView2DownloadState.Completed) d.State = "done";
            else if (st == CoreWebView2DownloadState.Interrupted)
            {
                var why = op.InterruptReason;
                d.State = why == CoreWebView2DownloadInterruptReason.UserPaused || (op.CanResume && (d.UserPaused || d.LimitPaused)) ? "paused" : why == CoreWebView2DownloadInterruptReason.UserCanceled ? "cancelled" : "failed";
            }
            else d.State = "running";
            if (d.State != old)
            {
                if (d.State == "done") { Toast("Downloaded " + d.Name, 4); if (S.DownloadScan) ScanDownload(d); else d.Scan = "off"; }
                if (d.Record != null && d.State != "paused") { d.Record.State = d.State; d.Record.Size = Math.Max(d.Got, d.Total); if (d.State == "done" && !S.DownloadScan) d.Record.Scan = "off"; DownloadHistory.Save(); }
            }
            Panels.RefreshDownloads(this); RefreshDownloadsPage();
        }
        public void PauseDownload(DownloadItem d) { if (d == null || d.State != "running") return; d.UserPaused = true; try { d.Op.Pause(); } catch { } }
        public void ResumeDownload(DownloadItem d)
        {
            if (d == null) return; d.UserPaused = false; d.LimitPaused = false;
            try { if (d.Op.CanResume || d.State == "paused") d.Op.Resume(); } catch { }
            d.LimitBase = d.Got; d.LimitFrom = DateTime.Now; EnsureLimitTimer();
        }
        public void CancelDownload(DownloadItem d) { if (d == null) return; d.UserPaused = false; d.LimitPaused = false; try { d.Op.Cancel(); } catch { } }

        // Defender: one file, in the background, Idle priority; the badge says clean / threat found
        void ScanDownload(DownloadItem d)
        {
            d.Scan = "scanning"; if (d.Record != null) { d.Record.Scan = "scanning"; DownloadHistory.Save(); }
            var file = d.File;
            Task.Run(() => { string note; var r = Defender.Scan(file, out note); return Tuple.Create(r, note); }).ContinueWith(x => Dispatcher.BeginInvoke(new Action(() =>
            {
                ScansRun++;
                d.Scan = x.Result.Item1; d.ScanNote = x.Result.Item2;
                if (d.Record != null) { d.Record.Scan = d.Scan; d.Record.ScanNote = d.ScanNote; DownloadHistory.Save(); }
                if (d.Scan == "threat") Toast("Microsoft Defender found a threat in " + d.Name + " (" + d.ScanNote + "). Defender deals with it - don't open the file.", 10);
                Panels.RefreshDownloads(this); RefreshDownloadsPage();
            })));
        }

        // ---- the limit while gaming (all downloads in this window together); off = nothing runs
        void EnsureLimitTimer()
        {
            if (dlLimitTimer != null) { if (!dlLimitTimer.IsEnabled) dlLimitTimer.Start(); return; }
            dlLimitTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            dlLimitTimer.Tick += (a, b) => LimitTick();
            dlLimitTimer.Start();
        }
        public string LimitState = "off"; bool limitWasOn; DateTime lastLog; public readonly List<string> LimitLog = new List<string>(); public int LimitPauses, LimitResumes; public string LimitNote;   // tests: what the limit saw
        // the engine's own state, every tick (a pause and a resume close together can land out of order, so the wish is checked again)
        static bool OpPaused(DownloadItem d) { try { return d.Op.State == CoreWebView2DownloadState.Interrupted && d.Op.CanResume; } catch { return false; } }
        static bool OpRunning(DownloadItem d) { try { return d.Op.State == CoreWebView2DownloadState.InProgress; } catch { return false; } }
        void LimitTick()
        {
            var live = Downloads.Where(x => !x.UserPaused && (OpRunning(x) || (x.LimitPaused && OpPaused(x)))).ToList();
            int kbps = S.DownloadGameKBps; bool on = Game != null && kbps != 0;
            if (BrowserSettings.TestDir != null && (DateTime.Now - lastLog).TotalMilliseconds > 400) { lastLog = DateTime.Now; foreach (var d in Downloads.Where(x => x.State == "running" || x.State == "paused")) { string st; long b; try { st = d.Op.State + "/" + d.Op.InterruptReason; b = d.Op.BytesReceived; } catch { st = "?"; b = -1; } LimitLog.Add(DateTime.Now.ToString("ss.f") + " on=" + on + " " + st + " " + b / 1024 + "K lp=" + d.LimitPaused + " up=" + d.UserPaused); } if (LimitLog.Count > 60) LimitLog.RemoveRange(0, LimitLog.Count - 60); }
            if (on && !limitWasOn) foreach (var d in live) { try { d.Got = d.Op.BytesReceived; } catch { } d.LimitFrom = DateTime.Now; d.LimitBase = d.Got; }   // counting starts when the game does
            limitWasOn = on;
            if (live.Count == 0 && !Downloads.Any(x => x.State == "running" || x.State == "paused")) { dlLimitTimer.Stop(); LimitState = "idle"; return; }
            if (!on)
            {
                foreach (var d in live.Where(x => x.LimitPaused)) { d.LimitPaused = false; LimitResumes++; try { d.Op.Resume(); } catch { } }
                LimitState = "off"; dlLimitTimer.Interval = TimeSpan.FromMilliseconds(500); return;
            }
            dlLimitTimer.Interval = TimeSpan.FromMilliseconds(100);
            if (kbps < 0) { foreach (var d in live) { d.LimitPaused = true; if (OpRunning(d)) { LimitPauses++; try { d.Op.Pause(); } catch { } } } LimitState = "paused"; return; }
            var each = DownloadLimit.Each(kbps, live.Count); var now = DateTime.Now; LimitState = "limited";
            foreach (var d in live)
            {
                try { d.Got = d.Op.BytesReceived; } catch { }   // the engine's count right now (its change events come in bursts)
                // a fresh window now and then when it is behind (unused time is not saved up for a burst); when ahead the debt stays, so the average holds
                if (d.LimitFrom == default(DateTime) || ((now - d.LimitFrom).TotalSeconds > 5 && !DownloadLimit.ShouldPause(d.Got, d.LimitBase, (now - d.LimitFrom).TotalSeconds, each))) { d.LimitFrom = now; d.LimitBase = d.Got; }
                bool over = DownloadLimit.ShouldPause(d.Got, d.LimitBase, (now - d.LimitFrom).TotalSeconds, each);
                d.LimitPaused = over;
                if (over && OpRunning(d)) { LimitPauses++; try { d.Op.Pause(); } catch (Exception ex) { LimitNote = "pause: " + ex.Message; } }
                else if (!over && OpPaused(d)) { LimitResumes++; try { d.Op.Resume(); } catch (Exception ex) { LimitNote = "resume: " + ex.Message; } }
            }
        }

        // ---- hallow://downloads
        StackPanel dlPageList;
        FrameworkElement DownloadsPage()
        {
            var col = new StackPanel { MaxWidth = 860, Margin = new Thickness(28, 26, 28, 40), HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(Ui.Row(Ui.Icon("download", 18, "AccentBrush"), Ui.T("  DOWNLOADS", 20, true, "Text")));
            col.Children.Add(Ui.Muted("Everything you downloaded in Hallow Browser (private windows' downloads aren't listed). " + (S.DownloadScan ? "Microsoft Defender checks each file when it's done." : "Defender checks are off (Settings > Downloads)."), 12, 10));
            col.Children.Add(Ui.Row(Ui.Btn("Open the Downloads folder", () => { try { Process.Start(Engine.Downloads()); } catch { } }, false, "folder-open"),
                Ui.Btn("Clear the list", () => { DownloadHistory.Clear(); RefreshDownloadsPage(); }, false, "broom")));
            dlPageList = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; col.Children.Add(dlPageList);
            RefreshDownloadsPage();
            var sv = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            sv.SetResourceReference(Control.BackgroundProperty, "Bg");
            return sv;
        }
        public int DownloadsPageRows { get { return dlPageList == null ? -1 : dlPageList.Children.Count; } }
        public void RefreshDownloadsPage()
        {
            foreach (var w in Program.Windows.Concat(new[] { this }).Distinct().ToList()) w.FillDownloadsPage();
        }
        void FillDownloadsPage()
        {
            if (dlPageList == null) return;
            dlPageList.Children.Clear();
            var live = Program.Windows.SelectMany(x => x.Downloads).Where(x => !x.Private || Private).ToList();
            var rows = new List<Tuple<DownloadItem, DownloadRecord>>();
            foreach (var d in live) rows.Add(Tuple.Create(d, d.Record));
            foreach (var r in DownloadHistory.Items) if (!live.Any(d => d.Record == r)) rows.Add(Tuple.Create((DownloadItem)null, r));
            if (rows.Count == 0) { dlPageList.Children.Add(Ui.Card(Ui.Muted("Nothing downloaded yet.", 12, 0))); return; }
            foreach (var row in rows.Take(300)) dlPageList.Children.Add(DownloadRow(row.Item1, row.Item2, true));
        }

        // one row (the flyout and the page share it)
        public FrameworkElement DownloadRow(DownloadItem d, DownloadRecord r, bool page)
        {
            string name = d != null ? d.Name : r.Name, file = d != null ? d.File : r.File, state = d != null ? d.State : r.State, scan = d != null ? d.Scan : r.Scan, note = d != null ? d.ScanNote : r.ScanNote;
            long got = d != null ? d.Got : r.Size, total = d != null ? d.Total : r.Size;
            if (d == null && state == "running") state = "failed";   // a download that was running when the browser closed
            var g = new Grid { Margin = page ? new Thickness(0, 0, 0, 8) : new Thickness(12, 4, 8, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = page ? 13 : 12.5 });
            var line = state == "running" ? (total > 0 ? Ui.Size(got) + " of " + Ui.Size(total) : Ui.Size(got)) + (d != null && d.LimitPaused ? " - slowed while you play" : "")
                : state == "paused" ? "Paused - " + Ui.Size(got) + (total > 0 ? " of " + Ui.Size(total) : "") + (d != null && d.LimitPaused && !d.UserPaused ? " (game running)" : "")
                : state == "done" ? Ui.Size(got > 0 ? got : total) + " - done" : state == "cancelled" ? "Cancelled" : "Failed";
            var sub = Ui.Row(Ui.Muted(line, 11));
            var badge = ScanBadge(scan, note); if (badge != null) { badge.Margin = new Thickness(8, 0, 0, 0); sub.Children.Add(badge); }
            info.Children.Add(sub);
            if (page) { var folder = Ui.Muted(Path.GetDirectoryName(file ?? "") ?? "", 10.5); folder.TextTrimming = TextTrimming.CharacterEllipsis; info.Children.Add(folder); }
            if ((state == "running" || state == "paused") && total > 0)
            {
                var bar = new ProgressBar { Height = 3, Maximum = total, Value = got, Margin = new Thickness(0, 4, 0, 0), BorderThickness = new Thickness(0) };
                bar.SetResourceReference(Control.ForegroundProperty, state == "paused" ? "Muted" : "AccentBrush"); bar.SetResourceReference(Control.BackgroundProperty, "LineDim"); info.Children.Add(bar);
            }
            g.Children.Add(info);
            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(acts, 1);
            bool exists = false; try { exists = file != null && File.Exists(file); } catch { }
            if (state == "done" && exists)
            {
                if (scan != "threat") acts.Children.Add(Ui.Tool("up-right-from-square", "Open", () => { try { Process.Start(file); } catch { } }, 12));
                acts.Children.Add(Ui.Tool("folder-open", "Show in folder", () => ShowInFolder(file), 12));
            }
            if (d != null && state == "running") acts.Children.Add(Ui.Tool("pause", "Pause", () => PauseDownload(d), 12));
            if (d != null && state == "paused") acts.Children.Add(Ui.Tool("play", "Resume", () => ResumeDownload(d), 12));
            if (d != null && (state == "running" || state == "paused")) acts.Children.Add(Ui.Tool("xmark", "Cancel", () => CancelDownload(d), 12));
            if (page && d == null && r != null) acts.Children.Add(Ui.Tool("trash-can", "Remove from the list (the file stays)", () => { DownloadHistory.Remove(r); RefreshDownloadsPage(); }, 12));
            g.Children.Add(acts);
            if (!page) return g;
            var card = Ui.Card(g, 12); card.Margin = new Thickness(0, 0, 0, 8); return card;
        }
        static FrameworkElement ScanBadge(string scan, string note)
        {
            if (scan == null || scan == "off") return null;
            string icon = scan == "clean" ? "circle-check" : scan == "threat" ? "shield-virus" : scan == "scanning" ? "arrows-rotate" : "triangle-exclamation";
            string fg = scan == "clean" ? "GreenText" : scan == "threat" ? "Red" : scan == "scanning" ? "Muted" : "AmberText";
            string text = scan == "clean" ? "Defender: clean" : scan == "threat" ? "Defender: threat found" : scan == "scanning" ? "Defender is checking..." : "Not checked";
            var b = Ui.Row(Ui.Icon(icon, 10, fg), Ui.T(" " + text, 11, scan == "threat", fg));
            b.ToolTip = scan == "threat" ? "Microsoft Defender found " + (note ?? "a threat") + " - it quarantines or removes it as your Defender settings say." : scan == "error" ? "Defender couldn't check it: " + (note ?? "unknown") : scan == "clean" ? "Microsoft Defender scanned this file and found nothing." : null;
            return b;
        }
        public static void ShowInFolder(string file)
        {
            try { if (File.Exists(file)) Process.Start("explorer.exe", "/select,\"" + file + "\""); else if (Directory.Exists(Path.GetDirectoryName(file))) Process.Start(Path.GetDirectoryName(file)); } catch { }
        }
    }
}
