// Hallow Browser 1.5 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): the address bar's rows, saving
// logins to a stand-in Keys, dark pages, downloads, previews and the drag picture, gestures through DevTools input (only into the
// off-screen page - never the real mouse), reopening windows, media controls, crash pages and crash recovery, private flyouts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V15(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries); string rest;
            switch (cmd)
            {
                case "omni": return Program.OnUi(w, () => "ok " + js.Serialize(w.BuildSuggestions(arg).Select(r => new Dictionary<string, object> { { "kind", r.Kind }, { "text", r.Text }, { "fill", r.Fill } }).ToList()));
                case "omnigo": { int n; var t = Split(arg, out rest); if (!int.TryParse(t, out n)) return "error"; return Program.OnUi(w, () => { var r = w.BuildSuggestions(rest); if (n >= r.Count) return "error no row"; r[n].Go(); return "ok " + r[n].Kind; }); }
                case "suggest": return Async(w, async () => { w.FetchSuggestions(arg); for (int i = 0; i < 40 && !w.EngineRows.Any(); i++) await Task.Delay(100); await Task.Delay(200); return "ok " + js.Serialize(new Dictionary<string, object> { { "fetches", BrowserWindow.SuggestFetches }, { "rows", w.EngineRows }, { "allowed", w.SuggestionsAllowed } }); });
                case "fetches": return "ok " + BrowserWindow.SuggestFetches;
                // ---- saving logins
                case "login": return Program.OnUi(w, () => { var x = Program.Windows.FirstOrDefault(y => y.PendingLogin != null); var o = x == null ? null : x.PendingLogin; return "ok " + js.Serialize(new Dictionary<string, object> { { "pending", o != null }, { "host", o == null ? null : o.Host }, { "user", o == null ? null : o.User }, { "state", o == null ? null : o.State }, { "offers", BrowserWindow.LoginOffers }, { "refused", BrowserWindow.LoginRefused }, { "note", w.LastLoginNote }, { "never", w.S.NeverSave } }); });
                case "loginanswer": return Async(w, async () => { var x = Program.Windows.FirstOrDefault(y => y.PendingLogin != null); if (x == null) return "error nothing asked"; return "ok " + await x.AnswerLogin(arg); });
                // ---- dark pages
                case "dark": return Program.OnUi(w, () => { var t = TabAt(w, arg); return t == null ? "error" : "ok " + js.Serialize(new Dictionary<string, object> { { "applied", t.DarkApplied }, { "wants", w.WantsDark(t.Url) }, { "global", w.S.ForceDark } }); });
                case "darktoggle": return Program.OnUi(w, () => { if (arg == "global") w.ToggleForceDark(); else { var t = TabAt(w, arg); if (t == null) return "error"; w.ToggleSiteDark(t); } return "ok"; });
                // ---- downloads
                case "dl":
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "items", w.Downloads.Select(d => new Dictionary<string, object> { { "name", d.Name }, { "state", d.State }, { "got", Got(d) }, { "total", d.Total }, { "scan", d.Scan }, { "note", d.ScanNote }, { "limit", d.LimitPaused }, { "user", d.UserPaused } }).ToList() },
                        { "history", DownloadHistory.Items.Select(r => r.Name + "|" + r.State + "|" + r.Scan).ToList() }, { "scans", BrowserWindow.ScansRun }, { "limit", w.LimitState }, { "game", w.Game }, { "lp", w.LimitPauses }, { "lr", w.LimitResumes }, { "ln", w.LimitNote }, { "log", w.LimitLog } }));
                case "dlact": { int i; if (!int.TryParse(p[0], out i)) return "error"; return Program.OnUi(w, () => { if (i < 0 || i >= w.Downloads.Count) return "error no download"; var d = w.Downloads[i]; if (p[1] == "pause") w.PauseDownload(d); else if (p[1] == "resume") w.ResumeDownload(d); else if (p[1] == "cancel") w.CancelDownload(d); else return "error"; return "ok"; }); }
                case "dlpage": return Async(w, async () => { w.NewTab(Omni.DownloadsUrl, true); await Task.Delay(300); w.RefreshDownloadsPage(); return "ok " + w.DownloadsPageRows; });
                // ---- previews, hover, drag picture
                case "preview": return Async(w, async () => { var t = TabAt(w, arg); if (t == null) return "error"; if (t.Preview == null) await w.TakePreview(t); var bs = t.Preview as System.Windows.Media.Imaging.BitmapSource; return "ok " + js.Serialize(new Dictionary<string, object> { { "has", t.Preview != null }, { "w", bs == null ? 0 : bs.PixelWidth }, { "h", bs == null ? 0 : bs.PixelHeight }, { "taken", BrowserWindow.PreviewsTaken } }); });
                case "hover": return Program.OnUi(w, () => { var t = TabAt(w, arg); if (t == null) return "error"; var card = w.HoverCard(t); return "ok " + js.Serialize(new Dictionary<string, object> { { "image", Find<Image>(card).Any() }, { "text", string.Join("|", Find<TextBlock>(card).Select(x => x.Text)) } }); });
                case "ghost": return Program.OnUi(w, () => { var t = TabAt(w, arg); if (t == null) return "error"; var card = w.GhostCard(t); return "ok " + js.Serialize(new Dictionary<string, object> { { "image", Find<Image>(card).Any() }, { "text", string.Join("|", Find<TextBlock>(card).Select(x => x.Text)) }, { "shown", w.GhostShown } }); });
                // ---- DevTools input into the off-screen page (mouse gestures) and other DevTools calls
                case "cdp": { var t = TabAt(w, p[0]); if (t == null) return "error"; var m = p[1]; var prm = p.Length > 2 ? p[2] : "{}"; return Async(w, async () => t.Core == null ? "error no view" : "ok " + await t.Core.CallDevToolsProtocolMethodAsync(m, prm)); }
                case "gesture":
                {
                    var t = TabAt(w, p[0]); if (t == null) return "error"; var path = p[1];   // e.g. "L" or "DR"
                    return Async(w, async () =>
                    {
                        if (t.Core == null) return "error no view";
                        double x = 400, y = 300; Func<string, double, double, int, Task> ev = (type, ex, ey, buttons) => t.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", js.Serialize(new Dictionary<string, object> { { "type", type }, { "x", ex }, { "y", ey }, { "button", "right" }, { "buttons", buttons }, { "clickCount", 1 } }));
                        await ev("mousePressed", x, y, 2);
                        foreach (var ch in path) for (int i = 0; i < 6; i++) { if (ch == 'L') x -= 15; else if (ch == 'R') x += 15; else if (ch == 'D') y += 15; else if (ch == 'U') y -= 15; await t.Core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", js.Serialize(new Dictionary<string, object> { { "type", "mouseMoved" }, { "x", x }, { "y", y }, { "button", "right" }, { "buttons", 2 } })); await Task.Delay(15); }
                        try { await ev("mouseReleased", x, y, 0); } catch { }   // a "close" gesture closes the view while it answers
                        await Task.Delay(600);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "done", BrowserWindow.GesturesDone }, { "last", BrowserWindow.LastGesture }, { "menus", BrowserWindow.MenusRequested } });
                    });
                }
                // ---- windows
                case "gstate": return "ok " + js.Serialize(new Dictionary<string, object> { { "done", BrowserWindow.GesturesDone }, { "last", BrowserWindow.LastGesture }, { "menus", BrowserWindow.MenusRequested } });
                case "reopenwin": return Program.OnUi(w, () => { var nw = Program.ReopenClosedWindow(); return nw == null ? "error none" : "ok " + Program.Windows.IndexOf(nw); });
                case "closedwins": return "ok " + Program.ClosedWindows.Count;
                // ---- media
                case "media": return Async(w, async () => { var l = BrowserWindow.MediaTabs(); var o = new List<object>(); foreach (var x in l) { var st = await Media.State(x.Item2); o.Add(new Dictionary<string, object> { { "w", Program.Windows.IndexOf(x.Item1) }, { "i", x.Item1.Tabs.IndexOf(x.Item2) }, { "title", st == null ? null : st.Title }, { "playing", st != null && st.Playing }, { "video", st != null && st.Video }, { "next", st != null && st.Next } }); } return "ok " + js.Serialize(new Dictionary<string, object> { { "tabs", o }, { "button", w.MediaButtonShown } }); });
                case "mediaact": { var x = WinAt(p[0]); int ti; if (x == null || !int.TryParse(p[1], out ti) || ti >= x.Tabs.Count) return "error"; return Async(w, async () => "ok " + await Media.Do(x.Tabs[ti], p[2])); }
                case "autopip": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "count", BrowserWindow.AutoPips }, { "popped", w.Tabs.Select(t => t.PoppedOut).ToList() } }));
                // ---- crashes
                case "crash": { var t = TabAt(w, arg); if (t == null) return "error"; return Program.OnUi(w, () => { if (t.Core == null) return "error no view"; try { var _ = t.Core.CallDevToolsProtocolMethodAsync("Page.crash", "{}"); } catch { } return "ok"; }); }
                case "crashstate": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "pages", BrowserWindow.CrashPagesShown }, { "tabs", w.Tabs.Select(t => t.CrashView != null).ToList() }, { "viewsHidden", w.Tabs.Select(t => t.View == null || t.View.Visibility != Visibility.Visible).ToList() } }));
                case "crashreload": return Program.OnUi(w, () => { var t = TabAt(w, arg); if (t == null) return "error"; w.ReloadCrashed(t); return "ok"; });
                case "info": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "info", w.InfoShown }, { "marker", File.Exists(Program.RunningMarker) } }));
                case "infoclick": return Program.OnUi(w, () => { var n = w.ClickInfoButton(0); return n ? "ok" : "error no button"; });
                // ---- private flyouts
                case "flyout":
                    return Program.OnUi(w, () =>
                    {
                        var x = WinAt(arg); if (x == null) return "error";
                        var sp = new StackPanel(); var row = Ui.MenuRow("plus", "New tab", null, null); sp.Children.Add(row);
                        var pop = Ui.Pop(x.MenuButton, Ui.Flyout(sp, 200));
                        var fixedUp = PrivateFlyouts.Fix(pop);
                        var bg = ((FrameworkElement)pop.Child).TryFindResource("Panel") as SolidColorBrush; var fg = ((FrameworkElement)pop.Child).TryFindResource("Text") as SolidColorBrush;
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "private", x.Private }, { "fixed", fixedUp }, { "panel", bg == null ? null : bg.Color.ToString() }, { "text", fg == null ? null : fg.Color.ToString() } });
                    });
                case "set":
                    return Program.OnUi(w, () =>
                    {
                        var v = p.Length > 1 ? p[1] : ""; var S = w.S;
                        switch (p[0])
                        {
                            case "suggest": S.SearchSuggest = v == "true"; break;
                            case "save": S.SaveLogins = v == "true"; break;
                            case "dark": S.ForceDark = v == "true"; w.DarkChanged(); break;
                            case "scan": S.DownloadScan = v == "true"; break;
                            case "dllimit": S.DownloadGameKBps = int.Parse(v); break;
                            case "previews": S.TabPreviews = v == "true"; break;
                            case "gestures": S.Gestures = v == "true"; w.GesturesChanged(); break;
                            case "autopip": S.AutoPip = v == "true"; break;
                            case "restore": S.RestoreSession = v == "true"; break;
                            case "never": S.NeverSave.Clear(); break;
                            case "keyword": { var kv = v.Split('|'); S.Keywords.RemoveAll(k => k.Key == kv[0]); S.Keywords.Add(new SearchKeyword { Key = kv[0], Url = kv[1], Name = "Local" }); S.Fix(); break; }
                            default: return "error";
                        }
                        S.Save(); return "ok";
                    });
            }
            return "error unknown 1.5 test command";
        }
        static IEnumerable<T> Find<T>(DependencyObject d) where T : DependencyObject
        {
            if (d == null) yield break;
            if (d is T) yield return (T)d;
            foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) foreach (var x in Find<T>(c)) yield return x;
        }
    }
}
namespace HallowBrowser
{
    public static partial class TestHooks { static long Got(DownloadItem d) { try { return d.Op.BytesReceived; } catch { return d.Got; } } }
}
