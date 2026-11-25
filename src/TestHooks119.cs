// Hallow Browser 1.19 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): pinned tabs, cookie banners,
// Copy clean link, auto picture-in-picture, group by site and the Ask Claude panel (against a local stand-in only). Clicks are made by
// raising the buttons' own Click events; nothing uses the real mouse, keyboard or clipboard. The API key is never echoed back.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V119(BrowserWindow w, string cmd, string arg)
        {
            string rest; var first = Split(arg, out rest);
            switch (cmd)
            {
                case "tabs": return Program.OnUi(w, () => { w.UpdateLayout(); return "ok " + js.Serialize(w.Tabs119()); });
                case "pin": { var t = TabAt(w, first); if (t == null) return "error no tab"; return Program.OnUi(w, () => { w.SetPinnedTab(t, rest == "1"); w.UpdateLayout(); return "ok " + w.Tabs.IndexOf(t); }); }
                case "consent": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "total", BrowserWindow.ConsentTotal }, { "last", BrowserWindow.LastConsent }, { "tabs", w.Tabs.Select(t => (object)new Dictionary<string, object> { { "declined", t.ConsentDeclined }, { "here", t.ConsentHere } }).ToList() } }));
                case "consent-site": return Program.OnUi(w, () => { w.SetConsentForSite(first, rest == "1"); return "ok"; });
                case "clean": return "ok " + LinkClean.Clean(arg);
                case "copyclean": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { BrowserWindow.LastCopied = null; w.CopyCleanLink(t); return "ok " + BrowserWindow.LastCopied; }); }
                case "share-clean":
                    return Program.OnUi(w, () =>
                    {
                        w.ShareFlyout(null);
                        var f = Panels.OpenFlyoutContent as DependencyObject; if (f == null) return "error no flyout";
                        var text = SettingsLayout.TextOf(f);
                        BrowserWindow.LastCopied = null;
                        var b = FindAll<Button>(f).FirstOrDefault(x => SettingsLayout.TextOf(x).Contains("Copy clean link")); if (b == null) return "error no button";
                        Press(b);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "copied", BrowserWindow.LastCopied }, { "text", text }, { "toast", w.LastToast } });
                    });
                case "tabmenu-run":   // <i> <label>: the tab's right-click menu, then a click on the row with that label
                {
                    var t = TabAt(w, first); if (t == null) return "error no tab";
                    return Program.OnUi(w, () =>
                    {
                        BrowserWindow.LastCopied = null;
                        Panels.TabMenu(w, t);
                        var f = Panels.OpenFlyoutContent as DependencyObject; if (f == null) return "error no menu";
                        var text = SettingsLayout.TextOf(f);
                        var b = FindAll<Button>(f).FirstOrDefault(x => SettingsLayout.TextOf(x).Contains(rest));
                        if (b == null) { Panels.CloseFlyouts(); return "error no row: " + text; }
                        Press(b); Panels.CloseFlyouts();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "menu", text }, { "copied", BrowserWindow.LastCopied } });
                    });
                }
                case "stripmenu": return Program.OnUi(w, () => { var rows = w.StripMenuRows119(a => a()); return "ok " + js.Serialize(rows.Select(r => SettingsLayout.TextOf(r)).ToList()); });
                case "addrcopy": return Program.OnUi(w, () => "ok " + w.AddressCopyText(arg));
                case "autopip": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "count", BrowserWindow.AutoPips }, { "last", w.LastAutoPip }, { "popped", w.Tabs.Select(t => t.PoppedOut).ToList() }, { "active", w.Tabs.IndexOf(w.Active) }, { "game", w.Game } }));
                case "gesture":   // <i> <js>: runs the script as a click would (ExecuteScriptAsync counts as a user gesture)
                {
                    var t = TabAt(w, first); if (t == null) return "error no tab";
                    return Async(w, async () => { if (t.Core == null) return "error no view"; return "ok " + await t.Core.ExecuteScriptAsync(rest); });
                }
                case "group": return Program.OnUi(w, () => { var n = w.AutoGroupBySite(); return "ok " + js.Serialize(new Dictionary<string, object> { { "made", n }, { "groups", w.Groups.Select(g => g.Name + ":" + w.Tabs.Count(t => t.Group == g)).ToList() } }); });
                case "ungroup": return Program.OnUi(w, () => { var n = w.UngroupAll(); return "ok " + js.Serialize(new Dictionary<string, object> { { "undone", n }, { "groups", w.Groups.Count } }); });
                // ---- Ask Claude (a local stand-in, HALLOW_BROWSER_ANTHROPIC_URL)
                case "ai-key": return Program.OnUi(w, () => "ok " + w.AiSaveKey(arg));   // the key itself is never echoed
                case "ai-open": return Program.OnUi(w, () => { w.Side.ShowNative("claude"); return "ok " + w.Side.NativeOpen; });
                case "ai-usepage": return Async(w, async () => "ok " + await w.AiUsePage());
                case "ai-send": return Program.OnUi(w, () => "ok " + w.AiSend(arg));
                case "ai-model": return Program.OnUi(w, () => { w.AiSetModel(arg); return "ok " + w.AiModelNow; });
                case "ai-new": return Program.OnUi(w, () => { w.AiNewChat(); return "ok"; });
                case "ai-state":
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> {
                        { "busy", w.AiBusy }, { "hasKey", AiKey.Has() }, { "keyFile", AiKey.FilePath }, { "open", w.Side.NativeOpen }, { "attached", w.AiPageAttached }, { "deltas", w.AiDeltas }, { "updates", w.AiUpdates },
                        { "model", w.AiModelNow }, { "status", w.AiStatus },
                        { "turns", w.AiTurns.Select(t => (object)new Dictionary<string, object> { { "role", t.Role }, { "text", t.Text }, { "note", t.Note }, { "failed", t.Failed }, { "page", t.PageText != null }, { "blocks", t.Blocks == null ? 0 : t.Blocks.Count } }).ToList() },
                        { "view", w.Side.NativeOpen == "claude" ? w.Side.NativeText() : null } }));
            }
            return "error unknown 1.19 test command";
        }
    }
}

namespace HallowBrowser
{
    public partial class Sidebar { public string NativeText() { return nativeView == null ? null : SettingsLayout.TextOf(nativeView) + " " + string.Join(" ", AllText(nativeView)); } static IEnumerable<string> AllText(DependencyObject d) { if (d == null) yield break; var tb = d as TextBox; if (tb != null) yield return tb.Text; foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) foreach (var s in AllText(c)) yield return s; } }

    public partial class BrowserWindow
    {
        public List<Dictionary<string, object>> Tabs119()
        {
            return Tabs.Select((t, i) => new Dictionary<string, object> {
                { "i", i }, { "url", t.Url }, { "title", t.Title }, { "pinned", t.Pinned }, { "active", t == Active }, { "ready", t.Ready },
                { "width", Math.Round(t.Header.ActualWidth > 0 ? t.Header.ActualWidth : t.Header.Width, 1) }, { "closeShown", t.CloseBtn.Visibility == Visibility.Visible }, { "titleShown", t.TitleBlock.Visibility == Visibility.Visible },
                { "group", t.Group == null ? null : t.Group.Name }, { "headerParent", t.Header.Parent == null ? null : t.Header.Parent.GetType().Name }, { "vertical", S.VerticalTabs } }).ToList();
        }
    }
}
