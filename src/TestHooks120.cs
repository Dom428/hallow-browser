// Hallow Browser 1.20 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): the extension bridge - side panels,
// the chrome.sidePanel shim, the verdicts, "Open in Chrome" (written down to HALLOW_BROWSER_OPENIN_RECORD, never started). Clicks are
// made by raising the buttons' own Click events; nothing uses the real mouse or keyboard.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static Dictionary<string, object> CompatJson(Compat c)
        {
            if (c == null) return null;
            return new Dictionary<string, object> { { "level", c.Level }, { "title", c.Title }, { "reasons", c.Reasons }, { "notes", c.Notes }, { "keys", c.Keys }, { "sidePanel", c.SidePanelPath }, { "askClaude", c.AskClaude }, { "known", c.Known } };
        }
        static BrowserWindow PrivateWin() { return Program.Windows.FirstOrDefault(x => x.Private && !x.Gone); }
        static string V120(BrowserWindow w, string cmd, string arg)
        {
            string rest; var first = Split(arg, out rest);
            switch (cmd)
            {
                case "verdict":   // <extension id>: from its installed manifest
                    return Program.OnUi(w, () => { var x = w.S.Extensions.FirstOrDefault(e => e.Id == arg); if (x == null) return "error no extension"; return "ok " + js.Serialize(CompatJson(ExtCompat.Of(x))); });
                case "verdict-file":   // <id> <manifest.json path>: a saved manifest (a fixture)
                {
                    var m = ExtCompat.Parse(System.IO.File.ReadAllText(rest));
                    return "ok " + js.Serialize(CompatJson(ExtCompat.Of(m, first)));
                }
                case "exts": return Program.OnUi(w, () => "ok " + js.Serialize(w.S.Extensions.Select(x => (object)new Dictionary<string, object> { { "id", x.Id }, { "name", x.Name }, { "enabled", x.Enabled }, { "sidePanel", w.SidePanelPathOf(x) } }).ToList()));
                case "flyout":   // the puzzle piece's list: which rows have "Open side panel"
                    return Program.OnUi(w, () =>
                    {
                        Panels.CloseFlyouts();
                        var f = Panels.ShowFlyout(w.ToolbarItem("extensions") ?? (UIElement)w.MenuButton, w.ExtPanel(() => Panels.CloseFlyouts()), 300);
                        var c = Panels.OpenFlyoutContent; if (c == null) return "error no flyout";
                        var sps = FindAll<Button>(c).Where(b => (b.Tag as string ?? "").StartsWith("sidepanel:")).Select(b => ((string)b.Tag).Substring(10)).ToList();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "text", SettingsLayout.TextOf(c) }, { "sidePanels", sps } });
                    });
                case "flyout-sp":   // <id>: a click on that row's side panel button
                    return Program.OnUi(w, () =>
                    {
                        var c = Panels.OpenFlyoutContent; if (c == null) return "error no flyout";
                        var b = FindAll<Button>(c).FirstOrDefault(x => (x.Tag as string) == "sidepanel:" + arg); if (b == null) return "error no button";
                        Press(b); return "ok";
                    });
                case "pinmenu":   // <id>: pin it, right-click its toolbar button; with a label in rest, click that row
                    return Program.OnUi(w, () =>
                    {
                        var x = w.S.Extensions.FirstOrDefault(e => e.Id == first); if (x == null) return "error no extension";
                        if (!w.IsPinned(x)) w.SetPinned(x, true);
                        w.UpdateLayout();
                        var b = w.PinButtons.FirstOrDefault(p => (p.Tag as string) == x.Id); if (b == null) return "error no pinned button";
                        b.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonUpEvent });
                        var c = Panels.OpenFlyoutContent; if (c == null) return "error no menu";
                        var text = SettingsLayout.TextOf(c);
                        if (rest.Length > 0) { var row = FindAll<Button>(c).FirstOrDefault(r => SettingsLayout.TextOf(r).Contains(rest)); if (row == null) return "error no row: " + text; Press(row); }
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "text", text } });
                    });
                case "open-sp": return Program.OnUi(w, () => { var x = w.S.Extensions.FirstOrDefault(e => e.Id == arg); var p = w.OpenExtSidePanel(x); return p == null ? "error " + w.LastToast : "ok " + w.Side.Panels.IndexOf(p); });
                case "panel":   // the extension panel shown now
                    return Program.OnUi(w, () =>
                    {
                        var p = w.Side.Open;
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "open", p != null }, { "ext", p == null ? null : p.ExtId }, { "url", p == null ? null : p.Site.Url }, { "source", p != null && p.Core != null ? p.Core.Source : null },
                            { "ready", p != null && p.Ready }, { "loaded", p != null && p.View != null }, { "channel", p == null ? null : p.ShimChannel }, { "note", w.Side.NoteText }, { "head", w.Side.HeadText }, { "hostShown", w.Side.HostShown },
                            { "extPanels", w.Side.Panels.Count(x => x.ExtId != null) }, { "bridge", w.LastBridge }, { "paths", BrowserWindow.PanelPaths }, { "onAction", BrowserWindow.PanelOnAction }, { "native", w.Side.NativeOpen } });
                    });
                case "panel-eval":   // <js>: in the open extension panel (DevTools Runtime.evaluate, promises awaited)
                    return Async(w, async () => { var p = w.Side.Open; if (p == null || p.Core == null) return "error no panel view"; return "ok " + await Panels.Eval(p.Core, arg); });
                case "panel-mute": return Program.OnUi(w, () => { var p = w.Side.Open; if (p == null || p.Core == null) return "error"; p.Core.IsMuted = true; return "ok"; });
                case "panel-hide": return Program.OnUi(w, () => { w.Side.Hide(); return "ok"; });
                case "popup":   // <id>: the extension's popup (with the shim)
                    return Async(w, async () => { var x = w.S.Extensions.FirstOrDefault(e => e.Id == arg); if (x == null) return "error no extension"; var src = await w.ExtPopup(x, null); var c = BrowserWindow.ExtPopupCore; if (c != null) c.IsMuted = true; return "ok " + src; });
                case "popup-eval": return Async(w, async () => { var c = BrowserWindow.ExtPopupCore; if (c == null) return "error no popup"; return "ok " + await Panels.Eval(c, arg); });
                case "popup-hide": return Program.OnUi(w, () => { BrowserWindow.HideExtPopup120(); return "ok"; });
                // ---- Open in Chrome
                case "openin":   // <tab> [normal|private]: as the tab's menu would (w: the normal window)
                {
                    var t = TabAt(w, first); if (t == null) return "error no tab";
                    return Program.OnUi(w, () => { w.LastOpenIn = null; w.OpenInBrowser(t, rest.Length == 0 ? null : rest, w.MenuButton); return "ok " + js.Serialize(new Dictionary<string, object> { { "last", w.LastOpenIn }, { "toast", w.LastToast }, { "ask", FlyText() }, { "id", w.OpenInId }, { "label", w.OpenInLabel } }); });
                }
                case "openin-private":   // <url>: in the private window (it asks first)
                {
                    var pw = PrivateWin(); if (pw == null) return "error no private window";
                    return Program.OnUi(pw, () => { pw.LastOpenIn = null; pw.OpenInBrowserUrl(arg, null, pw.MenuButton); return "ok " + js.Serialize(new Dictionary<string, object> { { "last", pw.LastOpenIn }, { "ask", FlyText() }, { "tags", Panels.OpenFlyoutContent == null ? new List<string>() : FindAll<Button>(Panels.OpenFlyoutContent).Select(b => b.Tag as string).Where(s => s != null).ToList() } }); });
                }
                case "openin-press":   // <tag>: a button in the open flyout (the private-window question)
                {
                    var pw = PrivateWin() ?? w;
                    return Program.OnUi(pw, () =>
                    {
                        var c = Panels.OpenFlyoutContent; if (c == null) return "error no flyout";
                        var b = FindAll<Button>(c).FirstOrDefault(x => (x.Tag as string) == arg); if (b == null) return "error no button";
                        pw.LastOpenIn = null; Press(b);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "last", pw.LastOpenIn }, { "toast", pw.LastToast }, { "open", Panels.OpenFlyoutContent != null } });
                    });
                }
                case "openin-found": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "found", OpenIn.Found }, { "pick", w.OpenInId }, { "setting", w.S.OpenIn }, { "launches", OpenIn.Launches }, { "menuLabelPage", BrowserWindow.OpenInMenuLabel(w.OpenInId, false) }, { "menuLabelLink", BrowserWindow.OpenInMenuLabel(w.OpenInId, true) } }));
                case "openin-set": return Program.OnUi(w, () => { w.S.OpenIn = arg.Length == 0 || arg == "auto" ? null : arg; w.S.Save(); return "ok " + w.OpenInId; });
                case "openin-settings": return Program.OnUi(w, () => { var sp = new StackPanel(); foreach (var i in w.OpenInSettings120()) sp.Children.Add(i); return "ok " + SettingsLayout.TextOf(sp); });
                case "menu-more":   // the main menu's More tools labels
                    return Program.OnUi(w, () => "ok " + js.Serialize(Menu113.Labels(Menu113.Model(w).Where(m => m.Id == "more").SelectMany(m => m.Kids)).ToList()));
                // ---- verdicts drawn: hallow://extensions and the store card
                case "extpage":
                    return Program.OnUi(w, () =>
                    {
                        var page = w.ExtensionsPageForTest();
                        var verdicts = FindAll<StackPanel>(page).Where(s => (s.Tag as string ?? "").StartsWith("verdict:")).Select(s => (object)new Dictionary<string, object> { { "level", s.Tag }, { "text", SettingsLayout.TextOf(s) } }).ToList();
                        var sps = FindAll<Button>(page).Where(b => (b.Tag as string ?? "").StartsWith("sidepanel:")).Select(b => ((string)b.Tag).Substring(10)).ToList();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "verdicts", verdicts }, { "sidePanels", sps } });
                    });
                case "pending":   // <id> <folder>: the store card as it would be for that unpacked folder (no download)
                    return Program.OnUi(w, () =>
                    {
                        var m = ExtCompat.ReadManifest(rest); if (m == null) return "error no manifest";
                        BrowserWindow.Pending = new PendingExt { Id = first, Name = WebStore.Str(m, "name"), Version = WebStore.Str(m, "version"), Crx = new Crx.Result(), Asks = WebStore.Asks(m), Gaps = WebStore.Gaps(m), Compat = ExtCompat.Of(m, first) };
                        var card = w.PendingCardForTest();
                        BrowserWindow.Pending = null;
                        var v = FindAll<StackPanel>(card).FirstOrDefault(s => (s.Tag as string ?? "").StartsWith("verdict:"));
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "text", SettingsLayout.TextOf(card) }, { "level", v == null ? null : v.Tag }, { "buttons", FindAll<Button>(card).Select(b => b.Tag as string).Where(s => s != null).ToList() } });
                    });
                case "pending-press":   // <tag> <id> <folder>: a button on that store card
                {
                    string r2; var id2 = Split(rest, out r2);
                    return Program.OnUi(w, () =>
                    {
                        var m = ExtCompat.ReadManifest(r2); if (m == null) return "error no manifest";
                        BrowserWindow.Pending = new PendingExt { Id = id2, Name = WebStore.Str(m, "name"), Version = WebStore.Str(m, "version"), Crx = new Crx.Result(), Asks = WebStore.Asks(m), Gaps = WebStore.Gaps(m), Compat = ExtCompat.Of(m, id2) };
                        var card = w.PendingCardForTest(); BrowserWindow.Pending = null;
                        var b = FindAll<Button>(card).FirstOrDefault(x => (x.Tag as string) == first); if (b == null) return "error no button";
                        w.LastOpenIn = null; Press(b);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "last", w.LastOpenIn }, { "native", w.Side.NativeOpen }, { "settingsQuery", w.SettingsQuery }, { "settingsTab", w.Tabs.Any(t => t.Url == Omni.SettingsUrl) } });
                    });
                }
                case "verdict-press":   // <id> <tag>: a button in that extension's verdict on hallow://extensions
                    return Program.OnUi(w, () =>
                    {
                        var page = w.ExtensionsPageForTest();
                        var b = FindAll<Button>(page).Where(x => (x.Tag as string) == rest).FirstOrDefault(); if (b == null) return "error no button";
                        w.LastOpenIn = null; Press(b);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "last", w.LastOpenIn }, { "native", w.Side.NativeOpen }, { "settingsQuery", w.SettingsQuery }, { "toast", w.LastToast } });
                    });
                case "note-press":   // <tag>: a button in the open panel's gap note
                    return Program.OnUi(w, () =>
                    {
                        var n = w.Side.Open == null ? null : w.Side.Open.Note; if (n == null) return "error no note";
                        var b = FindAll<Button>(n).FirstOrDefault(x => (x.Tag as string) == arg); if (b == null) return "error no button";
                        w.LastOpenIn = null; Press(b);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "last", w.LastOpenIn }, { "native", w.Side.NativeOpen } });
                    });
            }
            return "error unknown 1.20 test command";
        }
    }
}

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        // tests: hallow://extensions and the store card drawn on their own (the open page keeps its list)
        public FrameworkElement ExtensionsPageForTest() { var keep = extList; try { return ExtensionsPage(); } finally { extList = keep; } }
        public FrameworkElement PendingCardForTest() { return PendingCard(); }
        public static void HideExtPopup120() { if (extWin != null) try { extWin.Hide(); } catch { } }
    }
}
