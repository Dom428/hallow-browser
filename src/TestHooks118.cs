// Hallow Browser 1.18 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): the Brave-style top bar. Clicks
// are made by raising the buttons' own Click (or right-click) events; nothing uses the real mouse, keyboard or clipboard.
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
        static string FlyText() { var f = Panels.OpenFlyoutContent; return f == null ? null : SettingsLayout.TextOf(f); }
        static void Press(ButtonBase b) { b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        static string V118(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                case "state": return Program.OnUi(w, () => "ok " + js.Serialize(w.TopBarState()));
                case "addr": return Program.OnUi(w, () => "ok " + js.Serialize(w.AddressState(arg)));
                case "show": return Program.OnUi(w, () => { w.SetToolbarShown(p[0], p[1] == "1"); return "ok " + js.Serialize(w.ToolbarShown()); });
                case "move": return Program.OnUi(w, () => { w.MoveToolbarButton(p[0], int.Parse(p[1])); return "ok " + js.Serialize(w.ToolbarShown()); });
                case "reset": return Program.OnUi(w, () => { w.ResetToolbar(); return "ok " + js.Serialize(w.ToolbarShown()); });
                case "customize": return Program.OnUi(w, () => { w.CustomizeToolbar(null); return "ok " + js.Serialize(FlyText()); });
                case "customize-toggle":   // <id>: the switch in the open Customize panel, as a click would flip it
                    return Program.OnUi(w, () =>
                    {
                        var f = Panels.OpenFlyoutContent as DependencyObject; if (f == null) return "error no panel";
                        var row = FindAll<Grid>(f).FirstOrDefault(g => (g.Tag as string) == "tb:" + arg); if (row == null) return "error no row";
                        var sw = row.Children.OfType<CheckBox>().First(); sw.IsChecked = sw.IsChecked != true; sw.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        return "ok " + js.Serialize(w.ToolbarShown());
                    });
                case "rclick":   // <id>: a right-click on that toolbar button (the toolbar's menu)
                    return Program.OnUi(w, () =>
                    {
                        var e = w.ToolbarItem(arg); if (e == null) return "error no button";
                        var ev = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = Mouse.MouseUpEvent, Source = e };   // MouseUp bubbles; WPF turns it into MouseRightButtonUp on each element
                        e.RaiseEvent(ev);
                        return "ok " + js.Serialize(w.LastToolbarMenu);
                    });
                case "menu-run":   // <label>: a row of the toolbar's right-click menu
                    return Program.OnUi(w, () => { var b = w.ToolbarMenuRow(arg); if (b == null) return "error no row"; Press(b); return "ok " + js.Serialize(w.ToolbarShown()) + "\t" + js.Serialize(FlyText()); });
                case "click":   // <id>: a click on that toolbar button; the flyout's text
                    return Program.OnUi(w, () => { var b = w.ToolbarButton(arg); if (b == null) return "error no button"; Press(b); return "ok " + js.Serialize(FlyText()); });
                case "close": return Program.OnUi(w, () => { Panels.CloseFlyouts(); return "ok"; });
                case "zoomclick": return Program.OnUi(w, () => { w.ClickZoomAction(); return "ok " + w.CurrentZoom; });
                case "share":
                    return Program.OnUi(w, () =>
                    {
                        w.ShareFlyout(null);
                        var f = Panels.OpenFlyoutContent as DependencyObject; if (f == null) return "error no flyout";
                        var hasQr = FindAll<Image>(f).Any(i => (i.Tag as string) == "qr");
                        var url = w.Active.Url; var q = Qr.Encode(url); string why; var back = q == null ? null : Qr.Read(q, out why);
                        BrowserWindow.LastCopied = null;
                        var copy = FindAll<Button>(f).FirstOrDefault(b => SettingsLayout.TextOf(b).Contains("Copy link")); if (copy != null) Press(copy);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "qr", hasQr }, { "version", q == null ? 0 : q.Version }, { "readBack", back }, { "url", url }, { "copied", BrowserWindow.LastCopied }, { "toast", w.LastToast } });
                    });
                case "pin": return Program.OnUi(w, () => { var x = w.S.Extensions.FirstOrDefault(e => e.Id == p[0]); if (x == null) return "error no extension"; w.SetPinned(x, p[1] == "1"); return "ok " + js.Serialize(w.PinButtons.Select(b => b.Tag as string).ToList()); });
                case "pinflyout":   // <id>: the pin toggle in the open puzzle-piece list
                    return Program.OnUi(w, () =>
                    {
                        var f = Panels.OpenFlyoutContent as DependencyObject; if (f == null) return "error no flyout";
                        var t = FindAll<ToggleButton>(f).FirstOrDefault(b => (b.Tag as string) == "pin:" + arg); if (t == null) return "error no pin";
                        t.IsChecked = t.IsChecked != true; Press(t);
                        return "ok " + js.Serialize(w.PinButtons.Select(b => b.Tag as string).ToList());
                    });
                case "pinclick":   // <i>: the i-th pinned extension's toolbar button -> its popup
                {
                    Button pb = null; Program.OnUi(w, () => { int i = int.Parse(arg); if (i < w.PinButtons.Count) { pb = w.PinButtons[i]; Press(pb); } return "ok"; });
                    if (pb == null) return "error no pinned button";
                    for (int k = 0; k < 40; k++) { System.Threading.Thread.Sleep(250); var src = Program.OnUi(w, () => { var c = BrowserWindow.ExtPopupCore; try { return c == null ? "" : c.Source; } catch { return ""; } }); if (src.StartsWith("chrome-extension://")) return "ok " + src; }
                    return "error no popup";
                }
                case "shieldcount": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Async(w, async () => { await w.CountShield(t); return "ok " + js.Serialize(new Dictionary<string, object> { { "n", t.ShieldBlocked }, { "fallback", t.FallbackBlockedHere }, { "badge", w.ShieldBadgeNumber }, { "note", w.ShieldCountNote }, { "state", w.ShieldState } }); }); }
                case "width": return Program.OnUi(w, () => { w.Width = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); w.UpdateLayout(); return "ok " + w.ActualWidth; });
                case "strip": return Program.OnUi(w, () => { w.UpdateLayout(); return "ok " + js.Serialize(w.StripState()); });
                case "hover": return Program.OnUi(w, () => { var t = TabAt(w, p[0]); if (t == null) return "error no tab"; w.HoverTabForTest(t, p[1] == "1"); return "ok " + js.Serialize(w.StripState()); });
                case "scroll": return Program.OnUi(w, () => { w.ScrollTabs(int.Parse(arg)); w.UpdateLayout(); return "ok " + js.Serialize(w.StripState()); });
                case "tabsearch": return Program.OnUi(w, () => { w.OpenTabSearch(); return "ok " + js.Serialize(new Dictionary<string, object> { { "text", w.AddressShown }, { "rows", w.SuggestRowsShown() }, { "open", w.SuggestOpen } }); });
                case "dlflyout": return Program.OnUi(w, () => { w.ShowDownloads(); return "ok " + js.Serialize(new Dictionary<string, object> { { "rows", Panels.DownloadsShown }, { "text", FlyText() } }); });
                case "siteinfo":
                    return Async(w, async () =>
                    {
                        w.OpenSiteInfo(); await Task.Delay(700);
                        var f = BrowserWindow.LastSiteInfo; return "ok " + js.Serialize(f == null ? null : SettingsLayout.TextOf(f));
                    });
                case "installcheck": return Program.OnUi(w, () => "ok " + js.Serialize(w.Tabs.Select(t => t.Installable).ToList()));
            }
            return "error unknown test command";
        }
        static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            if (root is T) yield return (T)root;
            foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var x in FindAll<T>(c)) yield return x;
        }
    }
}

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        public string LastToolbarMenu;
        public FrameworkElement ToolbarItem(string id) { FrameworkElement e; return tbItems.TryGetValue(id, out e) ? e : null; }
        public ButtonBase ToolbarButton(string id)
        {
            if (id == "extensions") return extBtn;
            if (id == "menu") return menuBtn;
            if (id == "tabsearch") return tabSearchBtn;
            return ToolbarItem(id) as ButtonBase;
        }
        public void OpenSiteInfo() { if (lockBtn != null) lockBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        public Button ToolbarMenuRow(string label) { return lastTbMenu == null ? null : lastTbMenu.Children.OfType<Button>().FirstOrDefault(b => SettingsLayout.TextOf(b).Contains(label)); }
        public Dictionary<string, object> TopBarState()
        {
            var t = Active;
            return new Dictionary<string, object> {
                { "toolbar", ToolbarShown() }, { "saved", S.Toolbar }, { "pinned", S.PinnedExtensions }, { "pinButtons", PinButtons.Select(b => b.Tag as string).ToList() },
                { "address", Address.Text }, { "addressFocused", Address.IsKeyboardFocusWithin }, { "notSecure", notSecureText != null && notSecureText.Visibility == Visibility.Visible },
                { "pageActions", PageActionsShown() }, { "zoom", CurrentZoom }, { "shieldBadge", ShieldBadgeNumber }, { "shieldState", ShieldState }, { "shieldNote", ShieldCountNote },
                { "ring", DownloadRing }, { "downloads", Downloads.Select(d => d.State + ":" + d.Got + "/" + d.Total).ToList() }, { "profile", ProfileButtonLetter },
                { "tabSearchButton", tabSearchBtn != null && tabSearchBtn.IsVisible }, { "active", Tabs.IndexOf(t) }, { "url", t == null ? null : t.Url } };
        }
    }
}
