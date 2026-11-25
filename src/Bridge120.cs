// Hallow Browser 1.20 - the extension bridge. Hallow stays your main browser; this narrows the gap with Chrome extensions that
// WebView2 can't fully run:
//  - side panels: an extension whose manifest has "side_panel": {"default_path": ...} (or that sets a path with chrome.sidePanel.setOptions
//    from one of its pages Hallow opened) gets "Open side panel" - its page opens in a sidebar panel of Hallow's own (a WebView2 on the
//    same environment and profile, so the page has its extension APIs: chrome.storage, chrome.runtime ...). WebView2 has no
//    chrome.sidePanel, so a small shim of the browser's own is put into those pages (and the extension's popup): open / close /
//    setOptions / getOptions / setPanelBehavior / getPanelBehavior resolve harmlessly; open, setOptions({path}) and setOptions({enabled})
//    reach Hallow (chrome.webview.postMessage, else a DevTools binding) and act on its panel. Only messages from that extension's own
//    chrome-extension:// pages count.
//  - "Open in Chrome": the current tab's address goes to Google Chrome, Brave or Microsoft Edge (found at their standard install paths or
//    in Windows' App Paths - read only); Settings > General > "Open in another browser" picks which. The address only - no profile
//    flags. From a private window Hallow asks first (the other browser's normal window isn't private) and offers its own private
//    window (--incognito for Chrome / Brave, --inprivate for Edge) as a separate choice.
//  - a 3-level verdict per extension from its manifest: "Works fully" / "Works with Hallow's bridge" (the side panel is its only gap) /
//    "Use Open in Chrome" (it needs chrome.debugger, chrome.identity / oauth2, native messaging or another API WebView2 lacks), with
//    the reasons - on the Chrome Web Store card before adding, and per extension on hallow://extensions.
// Tests: HALLOW_BROWSER_OPENIN_RECORD=<file> writes the launch down instead of starting anything and HALLOW_BROWSER_OPENIN_FOUND
// ("chrome=<path>;edge=<path>") stands in for what's installed - both honoured only with HALLOW_BROWSER_TESTDIR; in a test run
// without a record file nothing is ever launched.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        [DataMember] public string OpenIn;   // 1.20: "chrome" | "brave" | "edge" | null = automatic (Chrome, else Brave, else Edge)
        void Fix120() { if (OpenIn != null && Array.IndexOf(HallowBrowser.OpenIn.Ids, OpenIn) < 0) OpenIn = null; }
    }

    public partial class SidePanel
    {
        public string ExtId;             // 1.20: an extension's side panel (chrome-extension://<id>/...)
        public FrameworkElement Note;    // ... the line above it when the extension needs what WebView2 lacks
        public string ShimChannel;       // how the shim reached Hallow last ("webview" | "binding")
    }

    // ================================================================ the verdict
    public class Compat
    {
        public int Level;                                   // ExtCompat.Full / Bridge / Chrome
        public List<string> Reasons = new List<string>();   // why this level (missing APIs; the side panel)
        public List<string> Notes = new List<string>();     // less important (optional permissions, Manifest V2)
        public List<string> Keys = new List<string>();      // the manifest keys behind the reasons (tests)
        public string SidePanelPath;                        // side_panel.default_path, or null
        public bool AskClaude;                              // Claude in Chrome: point to Hallow's own Ask Claude
        public string Known;                                // a sentence about a well-known extension
        public string Title { get { return ExtCompat.Titles[Level]; } }
    }

    public static class ExtCompat
    {
        public const int Full = 0, Bridge = 1, Chrome = 2;
        public static readonly string[] Titles = { "Works fully", "Works with Hallow's bridge", "Use Open in Chrome" };
        public const string ClaudeId = "fcoeoabgfenejglbffodgkkbkcdhcgfn";   // Claude in Chrome (Anthropic) on the Chrome Web Store
        public const string ClaudeKnown = "Claude in Chrome acts on pages through chrome.debugger, lives in Chrome's side panel and talks to the Claude desktop app through native messaging. WebView2 has neither the debugger nor native messaging, so even with Hallow's side panel it can't do its job here. Use it in Chrome (\"Open in Chrome\"), and for chatting about a page use Hallow's own Ask Claude sidebar.";
        // what WebView2 extensions don't have (1.16's list, in the words used here); the side panel is bridged, the rest isn't
        static readonly string[][] HardList = {
            new[] { "debugger", "chrome.debugger (controlling pages, like automation) - WebView2 has no debugger API for extensions" },
            new[] { "nativeMessaging", "native messaging (talking to a desktop app) - WebView2 can't connect an extension to a desktop app" },
            new[] { "identity", "Google sign-in for extensions (chrome.identity) - not in WebView2" },
            new[] { "management", "managing other extensions (chrome.management) - not in WebView2" },
            new[] { "tabGroups", "tab groups (chrome.tabGroups) - not in WebView2" },
            new[] { "offscreen", "hidden helper pages (chrome.offscreen) - not in WebView2" },
            new[] { "tabCapture", "recording a tab (chrome.tabCapture) - not in WebView2" },
            new[] { "desktopCapture", "recording the screen (chrome.desktopCapture) - not in WebView2" },
            new[] { "pageCapture", "saving pages as MHTML (chrome.pageCapture) - not in WebView2" },
            new[] { "gcm", "push messages from Google (chrome.gcm) - not in WebView2" },
            new[] { "ttsEngine", "being a text-to-speech voice (chrome.ttsEngine) - not in WebView2" },
            new[] { "enterprise.platformKeys", "enterprise keys - not in WebView2" },
            new[] { "webAuthenticationProxy", "passkey proxying - not in WebView2" },
            new[] { "documentScan", "scanners (chrome.documentScan) - not in WebView2" },
            new[] { "printing", "printers (chrome.printing) - not in WebView2" } };
        public const string SideReasonBridge = "side panel (chrome.sidePanel) - not in WebView2, so Hallow opens its panel page in Hallow's sidebar instead";
        public const string SideReasonChrome = "side panel (chrome.sidePanel) - Hallow can open its panel page in the sidebar, but that alone isn't enough here";
        public const string OAuthReason = "Google sign-in for extensions (an \"oauth2\" key, chrome.identity) - not in WebView2";

        static IEnumerable<string> Strs(Dictionary<string, object> m, string k) { object o; return m != null && m.TryGetValue(k, out o) && o is System.Collections.IEnumerable && !(o is string) ? ((System.Collections.IEnumerable)o).OfType<string>() : Enumerable.Empty<string>(); }
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as Dictionary<string, object> : null; }
        public static string SidePanelPath(Dictionary<string, object> m)
        {
            var p = WebStore.Str(Obj(m, "side_panel"), "default_path");
            if (string.IsNullOrWhiteSpace(p) || p.Contains("://") || p.Contains("..") || p.StartsWith("//")) return null;
            return p.TrimStart('/');
        }
        // the verdict from a manifest (and the extension's ID, for the well-known ones)
        public static Compat Of(Dictionary<string, object> m, string id)
        {
            var c = new Compat();
            var req = Strs(m, "permissions").Where(p => !p.Contains("://") && p != "<all_urls>").ToList();
            var opt = Strs(m, "optional_permissions").Where(p => !p.Contains("://") && p != "<all_urls>").ToList();
            c.SidePanelPath = SidePanelPath(m);
            bool side = c.SidePanelPath != null || req.Contains("sidePanel") || opt.Contains("sidePanel");
            var hard = new List<string>();
            foreach (var h in HardList) if (req.Contains(h[0])) { hard.Add(h[1]); c.Keys.Add(h[0]); }
            if (m != null && m.ContainsKey("oauth2") && !req.Contains("identity")) { hard.Add(OAuthReason); c.Keys.Add("oauth2"); }
            foreach (var h in HardList) if (opt.Contains(h[0]) && !req.Contains(h[0])) c.Notes.Add("It may ask later for " + h[1].Split(new[] { " - " }, StringSplitOptions.None)[0] + " - that part won't work in Hallow.");
            object mv; if (m != null && m.TryGetValue("manifest_version", out mv) && mv is int && (int)mv == 2) c.Notes.Add("Manifest V2: Chromium is retiring these extensions, so it may stop loading after an engine update.");
            bool known = id == ClaudeId;
            if (known) { c.AskClaude = true; c.Known = ClaudeKnown; }
            if (hard.Count > 0 || known)
            {
                c.Level = Chrome;
                // the reasons in a fixed order: debugger, side panel, native messaging, the rest
                var dbg = hard.Where(x => x.StartsWith("chrome.debugger")).ToList();
                c.Reasons.AddRange(dbg);
                if (side) { c.Reasons.Add(SideReasonChrome); c.Keys.Add("sidePanel"); }
                c.Reasons.AddRange(hard.Except(dbg));
                if (known)
                {
                    // whatever its manifest says, Claude in Chrome needs these (its published permissions)
                    if (!c.Keys.Contains("debugger")) { c.Reasons.Insert(0, HardList[0][1]); c.Keys.Add("debugger"); }
                    if (!c.Keys.Contains("sidePanel")) { c.Reasons.Insert(1, SideReasonChrome); c.Keys.Add("sidePanel"); }
                    if (!c.Keys.Contains("nativeMessaging")) { c.Reasons.Insert(2, HardList[1][1]); c.Keys.Add("nativeMessaging"); }
                }
            }
            else if (side)
            {
                c.Level = Bridge; c.Reasons.Add(SideReasonBridge); c.Keys.Add("sidePanel");
                if (c.SidePanelPath == null) c.Notes.Add("It picks its panel page while it runs: Hallow's bridge catches that from the extension's own pages (its popup), not from its background.");
            }
            return c;
        }

        // installed extensions: their manifest.json (cached by file time); --snap puts made-up manifests here
        public static readonly Dictionary<string, Dictionary<string, object>> SnapManifests = new Dictionary<string, Dictionary<string, object>>();
        static readonly Dictionary<string, Tuple<DateTime, Dictionary<string, object>>> cache = new Dictionary<string, Tuple<DateTime, Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, object> ReadManifest(string folder)
        {
            if (folder == null) return null;
            Dictionary<string, object> snap; if (SnapManifests.TryGetValue(folder, out snap)) return snap;
            try
            {
                var f = Path.Combine(folder, "manifest.json"); if (!File.Exists(f)) return null;
                var t = File.GetLastWriteTimeUtc(f);
                lock (cache) { Tuple<DateTime, Dictionary<string, object>> c; if (cache.TryGetValue(f, out c) && c.Item1 == t) return c.Item2; }
                var d = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                lock (cache) cache[f] = Tuple.Create(t, d);
                return d;
            }
            catch { return null; }
        }
        public static Compat Of(UserExtension x) { if (x == null) return null; var m = ReadManifest(x.Folder); return m == null ? null : Of(m, x.Id); }
        public static Dictionary<string, object> Parse(string json) { try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); } catch { return null; } }
    }

    // ================================================================ Open in Chrome / Brave / Edge
    public static class OpenIn
    {
        public static readonly string[] Ids = { "chrome", "brave", "edge" };
        public static string Name(string id) { return id == "chrome" ? "Google Chrome" : id == "brave" ? "Brave" : id == "edge" ? "Microsoft Edge" : "another browser"; }
        public static string Short(string id) { return id == "chrome" ? "Chrome" : id == "brave" ? "Brave" : id == "edge" ? "Edge" : "Chrome"; }
        public static string PrivateFlag(string id) { return id == "edge" ? "--inprivate" : "--incognito"; }
        public static string PrivateName(string id) { return id == "edge" ? "an InPrivate window" : id == "brave" ? "a private window" : "an Incognito window"; }
        static string Exe(string id) { return id == "chrome" ? "chrome.exe" : id == "brave" ? "brave.exe" : "msedge.exe"; }
        // the standard places each one installs to (per machine, 32-bit per machine, per user)
        public static List<string> Candidates(string id, string pf, string pf86, string local)
        {
            var rel = id == "chrome" ? @"Google\Chrome\Application\chrome.exe" : id == "brave" ? @"BraveSoftware\Brave-Browser\Application\brave.exe" : @"Microsoft\Edge\Application\msedge.exe";
            var l = new List<string>();
            foreach (var root in id == "edge" ? new[] { pf86, pf, local } : new[] { pf, pf86, local }) if (!string.IsNullOrEmpty(root)) l.Add(Path.Combine(root, rel));
            return l;
        }
        public static readonly string TestFound = BrowserSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_BROWSER_OPENIN_FOUND") : null;
        public static readonly string Record = BrowserSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_BROWSER_OPENIN_RECORD") : null;
        public static Dictionary<string, string> SnapFound;   // --snap: made-up
        static Dictionary<string, string> found; static DateTime foundAt;
        // what's installed: id -> exe (looked up at most once a minute; registry and files are only read)
        public static Dictionary<string, string> Found
        {
            get
            {
                if (SnapFound != null) return SnapFound;
                if (TestFound != null) return ParseFound(TestFound);
                if (found != null && DateTime.Now - foundAt < TimeSpan.FromMinutes(1)) return found;
                var d = new Dictionary<string, string>();
                foreach (var id in Ids) { var e = Find(id); if (e != null) d[id] = e; }
                found = d; foundAt = DateTime.Now;
                return d;
            }
        }
        public static Dictionary<string, string> ParseFound(string s)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in (s ?? "").Split(';')) { var i = part.IndexOf('='); if (i <= 0) continue; var k = part.Substring(0, i).Trim().ToLowerInvariant(); if (Array.IndexOf(Ids, k) >= 0 && part.Length > i + 1) d[k] = part.Substring(i + 1).Trim(); }
            return d;
        }
        static string Find(string id)
        {
            try
            {
                foreach (var key in new[] { @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + Exe(id), @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + Exe(id), @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + Exe(id) })
                {
                    var v = Microsoft.Win32.Registry.GetValue(key, "", null) as string;   // read only
                    if (!string.IsNullOrWhiteSpace(v)) { v = Environment.ExpandEnvironmentVariables(v.Trim().Trim('"')); if (File.Exists(v) && string.Equals(Path.GetFileName(v), Exe(id), StringComparison.OrdinalIgnoreCase)) return v; }
                }
            }
            catch { }
            foreach (var c in Candidates(id, Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("LOCALAPPDATA")))
                try { if (File.Exists(c)) return c; } catch { }
            return null;
        }
        // which one: your pick when it's installed, else Chrome, Brave, Edge - the first one found
        public static string Pick(string setting, Dictionary<string, string> have)
        {
            if (have == null) return null;
            if (setting != null && have.ContainsKey(setting)) return setting;
            return Ids.FirstOrDefault(have.ContainsKey);
        }
        // the command line: the address only (http / https), optionally the browser's own private flag - never profile flags
        public static string Args(string id, string url, bool priv)
        {
            var u = (url ?? "").Replace("\"", "%22").Replace(" ", "%20");
            return (priv ? PrivateFlag(id) + " " : "") + "\"" + u + "\"";
        }
        public static bool CanOpen(string url)
        {
            Uri u; return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        }
        public static int Launches;
        // null when it started (or was written down in a test), else what went wrong
        public static string Launch(string id, string exe, string url, bool priv)
        {
            if (!CanOpen(url)) return "Only web pages (http and https) can be opened in another browser.";
            var args = Args(id, url, priv);
            if (BrowserSettings.TestDir != null)
            {
                // tests: written down, never started (and with no record file, nothing happens at all)
                if (Record != null) try { File.AppendAllText(Record, id + "\t" + exe + "\t" + args + "\r\n"); } catch (Exception e) { return "couldn't record: " + e.Message; }
                Launches++; return null;
            }
            try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false }); Launches++; return null; }
            catch (Exception e) { return Name(id) + " couldn't be started: " + e.Message; }
        }
    }

    // ================================================================ the window's side
    public partial class BrowserWindow
    {
        public string OpenInId { get { return OpenIn.Pick(S.OpenIn, OpenIn.Found); } }
        public string OpenInLabel { get { return "Open in " + OpenIn.Short(OpenInId); } }
        public string LastOpenIn;   // tests: "<id> <private|normal> <url>"
        public void OpenInBrowser(Tab t, string mode = null, UIElement at = null) { OpenInBrowserUrl(t == null ? null : t.Url, mode, at); }
        // mode: null = ask first in a private window, "normal" = the other browser's normal window, "private" = its own private window
        public void OpenInBrowserUrl(string url, string mode = null, UIElement at = null)
        {
            if (!OpenIn.CanOpen(url)) { Toast("Open a web page first - only http and https pages can go to another browser.", 4); return; }
            var have = OpenIn.Found; var id = OpenIn.Pick(S.OpenIn, have);
            if (id == null) { Toast("Chrome, Brave or Edge wasn't found on this PC.", 5); return; }
            if (Private && mode == null) { OpenInPrivateAsk(url, id, at); return; }
            bool priv = mode == "private";
            var err = OpenIn.Launch(id, have[id], url, priv);
            if (err != null) { Toast(err, 6); return; }
            LastOpenIn = id + " " + (priv ? "private" : "normal") + " " + url;
            Toast(priv ? "Opened in " + OpenIn.PrivateName(id) + " of " + OpenIn.Short(id) + "." : Private ? "Opened in a normal " + OpenIn.Short(id) + " window - it isn't private there." : "Opened in " + OpenIn.Short(id) + ".", 4);
        }
        // from a private window: say that the other browser won't be private, and offer its own private window as a separate choice
        public FrameworkElement OpenInPrivatePanel(string url, string id, Action close)
        {
            var sp = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            var t = Ui.T("Open in " + OpenIn.Short(id) + " from a private window?", 13.5, true, "Text", 6); t.TextWrapping = TextWrapping.Wrap; sp.Children.Add(t);
            var m = Ui.Muted(OpenIn.Name(id) + " won't be private: its normal window keeps this page in its own history and cookies. Its " + OpenIn.PrivateName(id).Replace("an ", "").Replace("a ", "") + " doesn't.", 12, 10); m.TextWrapping = TextWrapping.Wrap; sp.Children.Add(m);
            var bp = Ui.Btn("Open in " + OpenIn.PrivateName(id) + " of " + OpenIn.Short(id), () => { if (close != null) close(); OpenInBrowserUrl(url, "private"); }, true, "mask"); bp.Tag = "openin:private"; bp.HorizontalAlignment = HorizontalAlignment.Left; bp.Margin = new Thickness(0, 0, 0, 6);
            var bn = Ui.Btn("Open in a normal " + OpenIn.Short(id) + " window", () => { if (close != null) close(); OpenInBrowserUrl(url, "normal"); }, false, "globe"); bn.Tag = "openin:normal"; bn.HorizontalAlignment = HorizontalAlignment.Left; bn.Margin = new Thickness(0, 0, 0, 6);
            var bc = Ui.Btn("Cancel", () => { if (close != null) close(); }, false, "xmark"); bc.Tag = "openin:cancel"; bc.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(bp); sp.Children.Add(bn); sp.Children.Add(bc);
            return sp;
        }
        void OpenInPrivateAsk(string url, string id, UIElement at)
        {
            Panels.CloseFlyouts();
            Popup pop = null;
            pop = Panels.ShowFlyout(at ?? MenuButton, OpenInPrivatePanel(url, id, () => { if (pop != null) pop.IsOpen = false; }), 360);
        }
        // the page's right-click menu: "Open page in Chrome" / "Open link in Chrome"
        void Hook120(CoreWebView2 c) { c.ContextMenuRequested += (a, e) => OpenInMenu120(e); }
        public static string LastOpenInMenu;
        void OpenInMenu120(CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                if (Engine.Env == null || AppMode) return;
                var id = OpenInId; if (id == null) return;
                var target = e.ContextMenuTarget; string url = null; bool link = false;
                if (target.HasLinkUri && OpenIn.CanOpen(target.LinkUri)) { url = target.LinkUri; link = true; }
                else if (target.Kind == CoreWebView2ContextMenuTargetKind.Page && !target.IsEditable && OpenIn.CanOpen(target.PageUri)) url = target.PageUri;
                if (url == null) return;
                var label = OpenInMenuLabel(id, link);
                var item = Engine.Env.CreateContextMenuItem(label, null, CoreWebView2ContextMenuItemKind.Command);
                var u = url;
                item.CustomItemSelected += (s, x) => Dispatcher.BeginInvoke(new Action(() => OpenInBrowserUrl(u)));
                e.MenuItems.Add(item);
                LastOpenInMenu = label;
            }
            catch { }
        }
        public static string OpenInMenuLabel(string id, bool link) { return (link ? "Open link in " : "Open page in ") + OpenIn.Short(id); }
        public UIElement OpenInRow120(Tab t, Action<Action> run)
        {
            if (t == null || !OpenIn.CanOpen(t.Url) || OpenInId == null) return null;
            return Ui.MenuRow("globe", OpenInLabel, null, () => run(() => OpenInBrowser(t, null, MenuButton)));
        }

        // Settings > General > "Open in another browser"
        public UIElement[] OpenInSettings120()
        {
            var items = new List<UIElement>();
            var have = OpenIn.Found;
            var intro = Ui.Muted("\"" + OpenInLabel + "\" (a tab's menu, right-click on a page, Ctrl+K, and the note on extensions that need Chrome) hands the page's address to another browser on this PC - only the address, not your Hallow tabs, sign-ins or cookies.", 12, 8); intro.TextWrapping = TextWrapping.Wrap; items.Add(intro);
            var choices = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", "Automatic") };
            foreach (var id in OpenIn.Ids) if (have.ContainsKey(id)) choices.Add(new KeyValuePair<string, string>(id, OpenIn.Short(id)));
            items.Add(Ui.Chips(choices, S.OpenIn != null && have.ContainsKey(S.OpenIn) ? S.OpenIn : "", v => { S.OpenIn = v == "" ? null : v; S.Save(); }));
            var missing = OpenIn.Ids.Where(id => !have.ContainsKey(id)).Select(OpenIn.Name).ToList();
            var auto = OpenIn.Pick(null, have);
            var m2 = Ui.Muted("Automatic: Chrome if it's installed, else Brave, else Edge" + (auto != null ? " - here " + OpenIn.Name(auto) + "." : " - none of them was found on this PC.") + (missing.Count > 0 && missing.Count < 3 ? " Not found: " + string.Join(", ", missing) + "." : ""), 11.5, 6); m2.TextWrapping = TextWrapping.Wrap; items.Add(m2);
            var m3 = Ui.Muted("From a private window Hallow asks first: the other browser's normal window isn't private, so you can pick its own Incognito / InPrivate window instead.", 11.5, 0); m3.TextWrapping = TextWrapping.Wrap; items.Add(m3);
            return items.ToArray();
        }

        // ---- the verdict, drawn (the store card, hallow://extensions)
        public FrameworkElement VerdictView(Compat c, bool card)
        {
            var sp = new StackPanel { Margin = new Thickness(0, card ? 8 : 6, 0, 2), Tag = "verdict:" + c.Level };
            string icon = c.Level == ExtCompat.Full ? "circle-check" : c.Level == ExtCompat.Bridge ? "plug" : "triangle-exclamation";
            string fg = c.Level == ExtCompat.Full ? "GreenText" : c.Level == ExtCompat.Bridge ? "AccentBrush" : "AmberText";
            var ic = Ui.Icon(icon, 12, fg); ic.Margin = new Thickness(0, 2, 7, 0); ic.VerticalAlignment = VerticalAlignment.Top;
            var head = Ui.T(c.Title, card ? 13 : 12.5, true, fg); head.TextWrapping = TextWrapping.Wrap;
            var badge = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(1), Child = Ui.Row(ic, head) };
            badge.SetResourceReference(Border.BorderBrushProperty, "LineDim"); badge.SetResourceReference(Border.BackgroundProperty, "PanelHi");
            sp.Children.Add(badge);
            Func<string, double, double, TextBlock> line = (s, size, bottom) => { var tb = Ui.Muted(s, size, bottom); tb.TextWrapping = TextWrapping.Wrap; tb.MaxWidth = 640; tb.HorizontalAlignment = HorizontalAlignment.Left; return tb; };
            if (c.Level == ExtCompat.Full) sp.Children.Add(line("It asks only for extension features WebView2 has.", 11.5, 0));
            if (c.Level == ExtCompat.Chrome && card) { var w2 = line(WebStore.GapWarning, 12, 2); w2.SetResourceReference(TextBlock.ForegroundProperty, "Text"); w2.Margin = new Thickness(0, 6, 0, 2); sp.Children.Add(w2); }
            if (c.Reasons.Count > 0)
            {
                var lbl = line(c.Level == ExtCompat.Chrome ? "Missing in Hallow Browser (WebView2):" : "Bridged by Hallow:", 11.5, 2); lbl.Margin = new Thickness(0, 6, 0, 2); sp.Children.Add(lbl);
                foreach (var r in c.Reasons) sp.Children.Add(line("\u2022 " + r, 11.5, 1));
            }
            foreach (var n in c.Notes) { var nl = line(n, 11, 1); nl.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(nl); }
            if (c.Known != null) { var k = line(c.Known, 11.5, 0); k.Margin = new Thickness(0, 6, 0, 0); k.SetResourceReference(TextBlock.ForegroundProperty, "Text"); sp.Children.Add(k); }
            var btns = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (c.Level == ExtCompat.Chrome)
            {
                var id = OpenInId;
                if (id != null) { var b = Ui.Btn("Open this page in " + OpenIn.Short(id), () => OpenInBrowser(LastWebTab(), null, MenuButton), false, "globe", "The last web page you looked at goes to " + OpenIn.Name(id) + " (the address only)"); b.Tag = "verdict:openin"; b.Margin = new Thickness(0, 0, 6, 4); btns.Children.Add(b); }
                else btns.Children.Add(line("Chrome, Brave or Edge wasn't found on this PC.", 11, 0));
            }
            if (c.AskClaude)
            {
                var b = Ui.Btn("Ask Claude in Hallow's sidebar", AskClaudeFromVerdict, false, "comments", S.AiOn ? "Opens Hallow's own Ask Claude panel" : "Ask Claude is off - opens its settings (your own Anthropic API key)"); b.Tag = "verdict:askclaude"; b.Margin = new Thickness(0, 0, 6, 4); btns.Children.Add(b);
            }
            if (btns.Children.Count > 0) sp.Children.Add(btns);
            return sp;
        }
        public Tab LastWebTab() { return Tabs.Where(x => OpenIn.CanOpen(x.Url)).OrderByDescending(x => x.LastActive).FirstOrDefault(); }
        public void AskClaudeFromVerdict()
        {
            if (Private || AppMode) { Toast("Ask Claude isn't in private or app windows.", 4); return; }
            if (S.AiOn) Side.ShowNative("claude"); else OpenSettingsAt("Ask Claude");
        }

        // ---- side panels
        public static string SidePanelUrl(string id, string path) { return "chrome-extension://" + id + "/" + (path ?? "").TrimStart('/'); }
        // path set at run time by setOptions (extension id -> path), and setPanelBehavior({openPanelOnActionClick})
        public static readonly Dictionary<string, string> PanelPaths = new Dictionary<string, string>();
        public static readonly Dictionary<string, bool> PanelOnAction = new Dictionary<string, bool>();
        public string SidePanelPathOf(UserExtension x)
        {
            if (x == null) return null;
            string p; if (PanelPaths.TryGetValue(x.Id, out p)) return p;
            var c = ExtCompat.Of(x); return c == null ? null : c.SidePanelPath;
        }
        public SidePanel OpenExtSidePanel(UserExtension x)
        {
            Panels.CloseFlyouts();
            if (x == null) return null;
            if (Private || AppMode) { Toast("Extensions don't run in private or app windows.", 4); return null; }
            if (!x.Enabled) { Toast(x.Name + " is switched off (hallow://extensions).", 4); return null; }
            var path = SidePanelPathOf(x);
            if (path == null) { Toast(x.Name + " has no side panel page.", 4); return null; }
            return Side.ShowExtPanel(x, path, ExtCompat.Of(x));
        }
        public const string ShimMark = "__hallowSidePanelShim";
        // the shim: chrome.sidePanel for pages WebView2 opens without it. Its calls resolve (and call a callback, as Chrome's do);
        // open / close / setOptions / setPanelBehavior are told to Hallow.
        public static string SidePanelShim(string defaultPath)
        {
            var dp = new JavaScriptSerializer().Serialize(defaultPath);
            return "(function(){try{if(location.protocol!=='chrome-extension:')return;var c=window.chrome=window.chrome||{};if(c.sidePanel&&c.sidePanel.__hallow)return;" +
                "var real=c.sidePanel&&typeof c.sidePanel.open==='function'?c.sidePanel:null;" +
                "var st={path:null,enabled:true,open:false};" +
                "function send(o){o.href=location.href;var s=JSON.stringify(o);try{if(window.chrome&&chrome.webview&&typeof chrome.webview.postMessage==='function'){chrome.webview.postMessage('hallow:sidepanel\\t'+s);return 'webview';}}catch(e){}" +
                "try{if(typeof window.__hallowSidePanelBridge==='function'){window.__hallowSidePanelBridge(s);return 'binding';}}catch(e){}return 'none';}" +
                "function done(v,cb){if(typeof cb==='function'){try{cb(v);}catch(e){}}return Promise.resolve(v);}" +
                "function ev(){var l=[];return{addListener:function(f){if(typeof f==='function'&&l.indexOf(f)<0)l.push(f);},removeListener:function(f){var i=l.indexOf(f);if(i>=0)l.splice(i,1);},hasListener:function(f){return l.indexOf(f)>=0;},hasListeners:function(){return l.length>0;}};}" +
                "var sp={__hallow:true,__real:!!real," +
                "open:function(o,cb){window.__hallowSidePanelLast=send({op:'open'});return done(undefined,cb);}," +
                "close:function(o,cb){window.__hallowSidePanelLast=send({op:'close'});return done(undefined,cb);}," +
                "setOptions:function(o,cb){o=o||{};if(typeof o.path==='string')st.path=o.path;if(typeof o.enabled==='boolean')st.enabled=o.enabled;window.__hallowSidePanelLast=send({op:'setOptions',path:typeof o.path==='string'?o.path:null,enabled:typeof o.enabled==='boolean'?o.enabled:null});return done(undefined,cb);}," +
                "getOptions:function(o,cb){if(typeof o==='function'){cb=o;}return done({enabled:st.enabled,path:st.path||" + dp + "||undefined},cb);}," +
                "setPanelBehavior:function(b,cb){b=b||{};st.open=!!b.openPanelOnActionClick;window.__hallowSidePanelLast=send({op:'behavior',open:st.open});return done(undefined,cb);}," +
                "getPanelBehavior:function(cb){return done({openPanelOnActionClick:st.open},cb);}," +
                "setLayout:function(o,cb){return done(undefined,cb);},getLayout:function(cb){return done({side:'right'},cb);}," +
                "onOpened:ev(),onClosed:ev()};" +
                "try{Object.defineProperty(c,'sidePanel',{value:sp,configurable:true,writable:true,enumerable:true});}catch(e){c.sidePanel=sp;}" +
                "window." + ShimMark + "=true;}catch(e){}})();";
        }
        // a message from the shim: only from that extension's own pages
        public string LastBridge;
        public void SidePanelBridge(string extId, string json, string source, string channel, SidePanel from)
        {
            if (extId == null || source == null || !source.StartsWith("chrome-extension://" + extId + "/", StringComparison.OrdinalIgnoreCase)) return;
            var x = S.Extensions.FirstOrDefault(e => e.Id == extId); if (x == null || !x.Enabled || Private) return;
            Dictionary<string, object> m; try { m = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); } catch { return; }
            if (m == null) return;
            var op = m.ContainsKey("op") ? m["op"] as string : null;
            if (from != null) from.ShimChannel = channel;
            LastBridge = op + " " + channel;
            switch (op)
            {
                case "open": OpenExtSidePanel(x); break;
                case "close": { var p = Side.ExtPanelOf(extId); if (p != null && Side.Open == p) Side.Hide(); break; }
                case "setOptions":
                {
                    var path = m.ContainsKey("path") ? m["path"] as string : null;
                    if (path != null && !path.Contains("://") && !path.Contains("..")) { PanelPaths[extId] = path.TrimStart('/'); var p = Side.ExtPanelOf(extId); if (p != null) Side.ShowExtPanel(x, PanelPaths[extId], null, false); }
                    if (m.ContainsKey("enabled") && m["enabled"] is bool && !(bool)m["enabled"]) { var p = Side.ExtPanelOf(extId); if (p != null && Side.Open == p) Side.Hide(); }
                    break;
                }
                case "behavior": PanelOnAction[extId] = m.ContainsKey("open") && m["open"] is bool && (bool)m["open"]; break;
            }
        }
        // the shim in a view showing extension pages (the side panel, the popup): before its pages load, and both channels
        public async Task HookShim120(CoreWebView2 core, string defaultPath, Func<string> extIdOf, SidePanel panel)
        {
            var shim = SidePanelShim(defaultPath);
            try { await core.AddScriptToExecuteOnDocumentCreatedAsync(shim); } catch { }
            core.WebMessageReceived += (a, b) =>
            {
                string m = null; try { m = b.TryGetWebMessageAsString(); } catch { }
                if (m == null || !m.StartsWith("hallow:sidepanel\t")) return;
                string src = null; try { src = b.Source; } catch { }
                SidePanelBridge(extIdOf(), m.Substring(17), src, "webview", panel);
            };
            try
            {
                var rcv = core.GetDevToolsProtocolEventReceiver("Runtime.bindingCalled");
                rcv.DevToolsProtocolEventReceived += (a, b) =>
                {
                    try
                    {
                        var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(b.ParameterObjectAsJson);
                        if (d == null || (d["name"] as string) != "__hallowSidePanelBridge") return;
                        var src = core.Source;
                        Dispatcher.BeginInvoke(new Action(() => SidePanelBridge(extIdOf(), d["payload"] as string, src, "binding", panel)));
                    }
                    catch { }
                };
                await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                await core.CallDevToolsProtocolMethodAsync("Runtime.addBinding", "{\"name\":\"__hallowSidePanelBridge\"}");
            }
            catch { }
            // a page that was already on its way when the script was added gets it now (it does nothing twice)
            core.NavigationCompleted += (a, b) => { try { if (core.Source.StartsWith("chrome-extension://")) { var _ = core.ExecuteScriptAsync(shim); } } catch { } };
        }
        static readonly HashSet<CoreWebView2> popupHooked = new HashSet<CoreWebView2>();
        async Task HookPopup120(CoreWebView2 core)
        {
            if (core == null || popupHooked.Contains(core)) return;
            popupHooked.Add(core);
            await HookShim120(core, null, () => { try { var s = core.Source; return s.StartsWith("chrome-extension://") ? s.Substring(19).Split('/')[0] : null; } catch { return null; } }, null);
        }
        // the extension's action click (flyout row, pinned button): its side panel when it asked for that with setPanelBehavior
        public bool PanelOnActionClick(UserExtension x)
        {
            bool on; return x != null && PanelOnAction.TryGetValue(x.Id, out on) && on && SidePanelPathOf(x) != null;
        }
        // right-click on a pinned extension's button
        public FrameworkElement PinnedExtMenu(UserExtension x, Action close)
        {
            var sp = new StackPanel();
            Action<Action> run = a => { if (close != null) close(); a(); };
            if (x.Popup != null) sp.Children.Add(Ui.MenuRow("up-right-from-square", "Open " + x.Name, null, () => run(() => { var _ = ExtPopup(x, null); })));
            if (SidePanelPathOf(x) != null) { var r = Ui.MenuRow("table-columns", "Open side panel", null, () => run(() => OpenExtSidePanel(x))); r.Tag = "sidepanel:" + x.Id; sp.Children.Add(r); }
            if (x.Options != null) sp.Children.Add(Ui.MenuRow("gear", "Options", null, () => run(() => NewTab("chrome-extension://" + x.Id + "/" + x.Options.TrimStart('/'), true))));
            var c = ExtCompat.Of(x);
            if (c != null && c.Level == ExtCompat.Chrome && OpenInId != null) sp.Children.Add(Ui.MenuRow("globe", "Use it in " + OpenIn.Short(OpenInId) + " (open this page there)", null, () => run(() => OpenInBrowser(LastWebTab(), null, MenuButton))));
            sp.Children.Add(Ui.MenuRow("thumbtack", "Unpin from the toolbar", null, () => run(() => SetPinned(x, false))));
            sp.Children.Add(Ui.MenuRow("puzzle-piece", "Manage extensions", null, () => run(() => NewTab(Omni.ExtensionsUrl, true))));
            return sp;
        }
        public void ShowPinnedExtMenu(UserExtension x, UIElement at)
        {
            Panels.CloseFlyouts();
            Popup pop = null;
            pop = Panels.ShowFlyout(at, PinnedExtMenu(x, () => { if (pop != null) pop.IsOpen = false; }), 260);
        }

        // ================================================================ --snap (made-up extensions, nothing loaded)
        public static readonly string[] SnapExtIds = { "aaaabbbbccccddddeeeeffffgggghhhh", "iiiijjjjkkkkllllmmmmnnnnoooopppp", "qqqqrrrrssssttttuuuuvvvvwwwwxxxx", ExtCompat.ClaudeId };
        public static void SnapExtSetup(BrowserSettings s)
        {
            ExtCompat.SnapManifests["snap-notes"] = ExtCompat.Parse("{\"manifest_version\":3,\"permissions\":[\"storage\",\"sidePanel\"],\"side_panel\":{\"default_path\":\"panel.html\"}}");
            ExtCompat.SnapManifests["snap-dark"] = ExtCompat.Parse("{\"manifest_version\":3,\"permissions\":[\"storage\",\"scripting\"],\"host_permissions\":[\"<all_urls>\"]}");
            ExtCompat.SnapManifests["snap-pw"] = ExtCompat.Parse("{\"manifest_version\":3,\"permissions\":[\"storage\",\"nativeMessaging\",\"debugger\"]}");
            ExtCompat.SnapManifests["snap-claude"] = ExtCompat.Parse(SnapClaudeManifest);
            s.Extensions = new List<UserExtension> {
                new UserExtension { Id = SnapExtIds[0], Name = "Tab Notes Panel", Version = "2.1", Folder = "snap-notes", Description = "Notes in a side panel (made-up).", Source = @"D:\extensions\tab-notes-panel", Popup = "popup.html", Enabled = true },
                new UserExtension { Id = SnapExtIds[1], Name = "Dark Mode Everywhere", Version = "4.9.2", Folder = "snap-dark", Description = "Dark pages everywhere (made-up).", Source = "Chrome Web Store", Store = true, Popup = "popup.html", Enabled = true },
                new UserExtension { Id = SnapExtIds[2], Name = "Desktop Vault Connector", Version = "1.0", Folder = "snap-pw", Description = "Talks to a desktop app (made-up).", Source = @"D:\extensions\vault-connector", Popup = "popup.html", Enabled = true },
                new UserExtension { Id = ExtCompat.ClaudeId, Name = "Claude", Version = "1.0", Folder = "snap-claude", Description = "Claude in Chrome (its manifest written by hand for this picture).", Source = "Chrome Web Store", Store = true, Enabled = true } };
            s.PinnedExtensions = new List<string> { SnapExtIds[0] };
            OpenIn.SnapFound = new Dictionary<string, string> { { "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe" }, { "edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" } };
        }
        public const string SnapClaudeManifest = "{\"manifest_version\":3,\"name\":\"Claude\",\"permissions\":[\"sidePanel\",\"storage\",\"activeTab\",\"scripting\",\"debugger\",\"tabGroups\",\"tabs\",\"alarms\",\"notifications\",\"webNavigation\",\"declarativeNetRequestWithHostAccess\",\"offscreen\",\"nativeMessaging\"],\"host_permissions\":[\"<all_urls>\"],\"side_panel\":{\"default_path\":\"sidepanel.html\"},\"background\":{\"service_worker\":\"service-worker-loader.js\",\"type\":\"module\"}}";
        public void SnapBridge(string what)
        {
            switch (what)
            {
                case "side-panel":
                case "side-panel-gap":
                {
                    var x = S.Extensions.First(e => e.Id == (what == "side-panel" ? SnapExtIds[0] : ExtCompat.ClaudeId));
                    var c = ExtCompat.Of(x);
                    var p = Side.ShowExtPanel(x, c.SidePanelPath, c, false);   // not shown for real: no WebView is made in --snap
                    Side.SnapExtPanel(p, SnapPanelMock(x, what == "side-panel"));
                    break;
                }
                case "extensions":
                {
                    var page = ExtensionsPage(); page.Tag = "mock"; content.Children.Add(page);
                    var t = Tabs[0]; t.Url = Omni.ExtensionsUrl; t.Title = "Extensions"; t.TitleBlock.Text = t.Title; SetTabIcon(t);
                    Activate(t); Address.Text = Omni.ExtensionsUrl;
                    break;
                }
                case "store-card":
                {
                    var man = ExtCompat.Parse(SnapClaudeManifest);
                    Pending = new PendingExt { Id = ExtCompat.ClaudeId, Name = "Claude", Version = "1.0.60", Crx = new Crx.Result(), Asks = WebStore.Asks(man), Gaps = WebStore.Gaps(man), Compat = ExtCompat.Of(man, ExtCompat.ClaudeId) };
                    var page = ExtensionsPage(); page.Tag = "mock"; content.Children.Add(page);
                    var t = Tabs[0]; t.Url = Omni.ExtensionsUrl; t.Title = "Extensions"; t.TitleBlock.Text = t.Title; SetTabIcon(t);
                    Activate(t); Address.Text = Omni.ExtensionsUrl;
                    Pending = null;
                    break;
                }
                case "tab-menu":
                {
                    var sp = new StackPanel(); Action<Action> run = a => { };
                    sp.Children.Add(Ui.MenuRow("plus", "New tab to the right", null, () => { }));
                    foreach (var r in TabMenuRows119(Active, run)) sp.Children.Add(r);
                    var f = Ui.Flyout(sp, 250); f.HorizontalAlignment = HorizontalAlignment.Left; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(60, 4, 0, 0); f.Tag = "mock"; Grid.SetColumnSpan(f, 3); content.Children.Add(f);
                    break;
                }
                case "private-ask":
                {
                    var f = Ui.Flyout(OpenInPrivatePanel(Active.Url, OpenInId ?? "chrome", null), 360); f.HorizontalAlignment = HorizontalAlignment.Right; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(0, 4, 8, 0); f.Tag = "mock"; Grid.SetColumnSpan(f, 3); content.Children.Add(f);
                    break;
                }
                case "pinned-menu":
                {
                    var x = S.Extensions.First(e => e.Id == SnapExtIds[0]);
                    var f = Ui.Flyout(PinnedExtMenu(x, null), 260); f.HorizontalAlignment = HorizontalAlignment.Right; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(0, 4, 120, 0); f.Tag = "mock"; Grid.SetColumnSpan(f, 3); content.Children.Add(f);
                    break;
                }
                case "ext-flyout":
                {
                    var f = Ui.Flyout(ExtPanel(null), 300); f.HorizontalAlignment = HorizontalAlignment.Right; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(0, 4, 150, 0); f.Tag = "mock"; Grid.SetColumnSpan(f, 3); content.Children.Add(f);
                    break;
                }
            }
        }
        // what a side panel page could look like (WPF-drawn, never a real extension)
        static FrameworkElement SnapPanelMock(UserExtension x, bool notes)
        {
            var sp = new StackPanel { Margin = new Thickness(14) };
            if (notes)
            {
                sp.Children.Add(Ui.T("Tab notes", 15, true, "Text", 4));
                sp.Children.Add(Ui.Muted("Saved with chrome.storage - made-up notes for this picture.", 11.5, 10));
                foreach (var n in new[] { "Patch 7.2: ranked starts at level 10", "Buy the season pass after the sale", "Clip the 1v3 from Tuesday's stream" })
                {
                    var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6), Child = Ui.T(n, 12.5, false, "Text") };
                    b.SetResourceReference(Border.BackgroundProperty, "PanelHi"); sp.Children.Add(b);
                }
                var box = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 7), BorderThickness = new Thickness(1), Child = Ui.Muted("Write a note...", 12) };
                box.SetResourceReference(Border.BorderBrushProperty, "Line"); sp.Children.Add(box);
            }
            else
            {
                sp.Children.Add(Ui.T("Claude", 15, true, "Text", 4));
                var m = Ui.Muted("(The extension's own panel page would be here - its parts that act on pages can't work in WebView2.)", 11.5, 10); m.TextWrapping = TextWrapping.Wrap; sp.Children.Add(m);
            }
            var g = new Grid(); g.SetResourceReference(Panel.BackgroundProperty, "Bg"); g.Children.Add(sp);
            return g;
        }
    }

    // ================================================================ the sidebar's side: extension panels
    public partial class Sidebar
    {
        public SidePanel ExtPanelOf(string extId) { return Panels.FirstOrDefault(p => p.ExtId == extId); }
        ContentControl noteHost;
        void BuildNoteHost120(Grid hostGrid)
        {
            noteHost = new ContentControl { Focusable = false, IsTabStop = false };
            Grid.SetRow(noteHost, 1); hostGrid.Children.Add(noteHost);
        }
        void ShowNote120(SidePanel p) { if (noteHost != null) noteHost.Content = p == null ? null : p.Note; }
        // an extension's side panel: one per extension, not in your list of sites (like the Twitch chat)
        public SidePanel ShowExtPanel(UserExtension x, string path, Compat c, bool show = true)
        {
            if (w.Private || w.AppMode || x == null || path == null) return null;
            var url = BrowserWindow.SidePanelUrl(x.Id, path);
            var p = ExtPanelOf(x.Id);
            if (p == null) { p = new SidePanel { Site = new SidebarSite { Id = "ext-" + x.Id, Name = x.Name, Url = url, Width = 380 }, Transient = true, ExtId = x.Id }; Panels.Add(p); }
            else if (p.Site.Url != url) { p.Site.Url = url; if (p.Core != null) try { p.Core.Navigate(url); } catch { } }
            if (c != null) p.Note = c.Level == ExtCompat.Chrome ? GapNote(x, c) : null;
            if (show && Open != p) Show(p);
            else if (Open == p) ShowNote120(p);
            return p;
        }
        // the line above a panel whose extension needs what WebView2 lacks
        FrameworkElement GapNote(UserExtension x, Compat c)
        {
            var sp = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            var keys = c.Keys.Where(k => k != "sidePanel").Select(k => k == "nativeMessaging" ? "native messaging" : k == "oauth2" || k == "identity" ? "Google sign-in" : "chrome." + k).Distinct().ToList();
            var ic = Ui.Icon("triangle-exclamation", 11, "AmberText"); ic.Margin = new Thickness(0, 2, 6, 0); ic.VerticalAlignment = VerticalAlignment.Top;
            var t = Ui.T(x.Name + " needs " + (keys.Count == 0 ? "Chrome features" : string.Join(", ", keys.Take(3))) + " - not in WebView2, so its panel may not work here.", 11.5, false, "Text"); t.TextWrapping = TextWrapping.Wrap;
            var row = new DockPanel(); DockPanel.SetDock(ic, System.Windows.Controls.Dock.Left); row.Children.Add(ic); row.Children.Add(t); sp.Children.Add(row);
            var bs = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var id = w.OpenInId;
            if (id != null) { var b = Ui.Btn("Open this page in " + OpenIn.Short(id), () => w.OpenInBrowser(w.Active != null && OpenIn.CanOpen(w.Active.Url) ? w.Active : w.LastWebTab(), null, w.MenuButton), false, "globe"); b.Tag = "note:openin"; b.Margin = new Thickness(0, 0, 6, 0); bs.Children.Add(b); }
            if (c.AskClaude) { var b = Ui.Btn("Ask Claude instead", w.AskClaudeFromVerdict, false, "comments"); b.Tag = "note:askclaude"; bs.Children.Add(b); }
            if (bs.Children.Count > 0) sp.Children.Add(bs);
            var border = new Border { Child = sp, BorderThickness = new Thickness(0, 0, 0, 1), Tag = "gapnote" };
            border.SetResourceReference(Border.BackgroundProperty, "PanelHi"); border.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            return border;
        }
        public void SnapExtPanel(SidePanel p, FrameworkElement mock)
        {
            if (p == null) return;
            Open = p; p.Shown = true;
            host.Visibility = splitter.Visibility = Visibility.Visible; hostCol.Width = new GridLength(p.Site.Width); splitCol.Width = GridLength.Auto;
            headTitle.Text = p.Site.Name + "  -  side panel"; uaBtn.Visibility = Visibility.Collapsed;
            ShowNote120(p);
            viewBox.Children.Clear(); viewBox.Children.Add(mock);
        }
        public string NoteText { get { return noteHost == null || noteHost.Content == null ? null : SettingsLayout.TextOf((DependencyObject)noteHost.Content); } }
        public string HeadText { get { return headTitle.Text; } }
    }
}
