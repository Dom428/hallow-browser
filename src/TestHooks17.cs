// Hallow Browser 1.7 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): translation against local
// stand-ins (a fake LibreTranslate server and fake DeepL / Google endpoints - never the real services), the settings page's sections
// and search, Ctrl+K's settings, the jump list (written to a TEST AppUserModelID and deleted after - never the browser's own list),
// tab previews in the taskbar (messages sent to the stand-in windows by the test itself), "Open in Edge to cast" (written to a log,
// Edge is never started), Shield's My filters through Shield's page, and history sync.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        static string V17(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                // ---- translation
                case "tr-set": return Program.OnUi(w, () => { w.S.TranslateProvider = p.Length > 0 && p[0] != "none" ? p[0] : ""; if (p.Length > 1) w.S.TranslateLibreUrl = p[1]; w.S.Fix(); w.S.Save(); if (w.Active != null) w.UpdateTranslateButtonPublic(w.Active); return "ok " + w.S.TranslateProvider; });
                case "tr-key": return Program.OnUi(w, () => { Translator.Keys.Set(p[0], p.Length > 1 ? p[1] : null); return "ok " + (Translator.Keys.Masked(p[0]) ?? "none"); });
                case "tr-to": return Program.OnUi(w, () => { w.S.TranslateTo = arg; w.S.Fix(); w.S.Save(); return "ok " + Translator.Target(w.S); });
                case "tr-always": return Program.OnUi(w, () => { w.S.TranslateAlways.Remove(p[0]); if (p.Length > 1 && p[1] == "1") w.S.TranslateAlways.Add(p[0]); w.S.Save(); return "ok " + string.Join(",", w.S.TranslateAlways); });
                case "tr": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Async(w, async () => "ok " + await w.TranslateTab(t, false)); }
                case "tr-orig": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Async(w, async () => "ok " + await w.ShowOriginal(t)); }
                case "tr-state":
                {
                    var t = TabAt(w, arg); if (t == null) return "error no tab";
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "translated", t.Translated }, { "busy", t.TranslateBusy }, { "lang", t.PageLang }, { "to", t.TranslatedTo }, { "note", t.TranslateNote }, { "sent", Translator.Sent },
                        { "ready", Translator.Ready(w.S) }, { "button", w.TranslateButtonVisible }, { "tip", w.TranslateButtonTip }, { "provider", Translator.ProviderName(w.S) }, { "keyFile", File.Exists(Path.Combine(BrowserSettings.LocalDir, "translate-keys.bin")) } }));
                }
                case "tr-flyout": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { w.TranslateFlyout(t); var f = Panels.OpenFlyoutContent; return "ok " + js.Serialize(f == null ? "" : SettingsLayout.TextOf(f)); }); }
                case "tr-close": return Program.OnUi(w, () => { Panels.CloseFlyouts(); return "ok"; });
                // ---- settings page and Ctrl+K
                case "settings-open": return Program.OnUi(w, () => { w.OpenSettingsAt(arg); return "ok"; });
                case "settings-state":
                    return Program.OnUi(w, () =>
                    {
                        var pg = w.SettingsPg; if (pg == null) return "error no page";
                        var t = w.Tabs.FirstOrDefault(x => x.Url == Omni.SettingsUrl);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "query", pg.Box.Text }, { "count", pg.Count.Text }, { "shown", pg.Shown }, { "active", w.Active == t },
                            { "cards", pg.Cards.Where(c => c.Card.Visibility == Visibility.Visible).Select(c => c.Section + "|" + c.Title).ToList() },
                            { "bright", pg.Cards.Where(c => c.Card.Visibility == Visibility.Visible).SelectMany(c => c.Rows.Where(r => r.Opacity > 0.9).Select(r => SettingsLayout.LabelOf(r, null))).Where(x => x != null).ToList() },
                            { "sections", pg.Heads.Where(h => h.Value.Visibility == Visibility.Visible).Select(h => h.Key).ToList() }, { "offset", pg.Scroll.VerticalOffset }, { "empty", pg.Empty.Visibility == Visibility.Visible } });
                    });
                case "settings-all": return Program.OnUi(w, () => w.SettingsPg == null ? "error no page" : "ok " + js.Serialize(w.SettingsPg.Cards.Select(c => c.Section + "|" + c.Title).ToList()));
                case "settings-jump": return Program.OnUi(w, () => { var pg = w.SettingsPg; if (pg == null) return "error"; var e = SettingsLayout.JumpFirst(pg); pg.Scroll.UpdateLayout(); double y = -1; try { y = e.TransformToAncestor(pg.Scroll).Transform(new Point(0, 0)).Y; } catch { } return "ok " + js.Serialize(new Dictionary<string, object> { { "target", e == null ? null : SettingsLayout.TextOf(e) }, { "offset", pg.Scroll.VerticalOffset }, { "y", y }, { "inView", y >= 0 && y < pg.Scroll.ViewportHeight - 20 } }); });
                case "settings-type": return Program.OnUi(w, () => { var pg = w.SettingsPg; if (pg == null) return "error"; pg.Box.Text = arg; return "ok " + pg.Shown; });
                case "settings-section": return Program.OnUi(w, () => { var pg = w.SettingsPg; if (pg == null || !pg.Heads.ContainsKey(arg)) return "error"; SettingsLayout.ScrollTo(pg, pg.Heads[arg]); pg.Scroll.UpdateLayout(); return "ok " + pg.Scroll.VerticalOffset; });
                case "palette":
                    return Program.OnUi(w, () => "ok " + js.Serialize(Palette.Search(w, arg, 12).Select(x => new Dictionary<string, object> { { "kind", x.Kind }, { "title", x.Title }, { "sub", x.Sub } }).ToList()));
                case "palette-run":   // runs the first result of that kind for the query (as Enter on it would)
                    return Program.OnUi(w, () => { var it = Palette.Search(w, p.Length > 1 ? arg.Substring(p[0].Length + 1) : "", 12).FirstOrDefault(x => x.Kind == p[0]); if (it == null) return "error none"; it.Run(); return "ok " + it.Title; });
                case "index": return Program.OnUi(w, () => "ok " + w.SettingsIndex().Count);
                // ---- jump list (a test AppUserModelID only)
                case "jump":
                    return Program.OnUi(w, () =>
                    {
                        var ok = w.UpdateJumpList();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "ok", ok }, { "note", BrowserWindow.JumpListNote }, { "appId", JumpList.LastAppId }, { "count", JumpList.LastCount }, { "profilesAsTasks", JumpList.LastProfilesAsTasks }, { "error", JumpList.LastError },
                            { "items", JumpList.Items(Program.ExePath, Program.Profiles).Select(i => i.Title + "|" + i.Args + "|" + (i.Category ?? "Tasks")).ToList() } });
                    });
                case "jump-delete": return Program.OnUi(w, () => BrowserSettings.JumpListTestId == null ? "error no test id" : JumpList.Delete(BrowserSettings.JumpListTestId) ? "ok" : "error " + JumpList.LastError);
                // ---- tab previews in the taskbar
                case "thumbs":
                    return Program.OnUi(w, () =>
                    {
                        if (arg.Length > 0) { w.S.TaskbarTabs = arg == "1"; w.S.Save(); }
                        w.SyncThumbs();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "on", w.S.TaskbarTabs }, { "registered", w.ThumbsRegistered }, { "registerHr", w.ThumbRegisterHr }, { "titles", w.Thumbs.Values.Select(x => x.LastTitle).ToList() }, { "tabs", w.Tabs.Count } });
                    });
                case "thumb-msg":   // <tab> thumb|activate|close: the message Windows would send to that tab's stand-in window
                {
                    var t = TabAt(w, p[0]); if (t == null) return "error no tab";
                    var h = Program.OnUi(w, () => { TabThumb th; return w.Thumbs.TryGetValue(t, out th) ? th.Handle.ToInt64().ToString() : "0"; });
                    var hw = new IntPtr(long.Parse(h)); if (hw == IntPtr.Zero) return "error no stand-in";
                    var m = p.Length > 1 ? p[1] : "thumb";
                    if (m == "thumb") SendMessage(hw, 0x0323, IntPtr.Zero, new IntPtr((200 << 16) | 100));
                    else if (m == "activate") SendMessage(hw, 0x0006, new IntPtr(1), IntPtr.Zero);
                    else if (m == "close") SendMessage(hw, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    System.Threading.Thread.Sleep(300);
                    return Program.OnUi(w, () => { TabThumb th; w.Thumbs.TryGetValue(t, out th); return "ok " + js.Serialize(new Dictionary<string, object> { { "hr", th == null ? 99 : th.LastHr }, { "requests", th == null ? -1 : th.Requests }, { "active", w.Tabs.IndexOf(w.Active) }, { "tabs", w.Tabs.Count } }); });
                }
                // ---- cast
                case "cast": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Program.OnUi(w, () => { w.OpenInEdgeToCast(t); return "ok " + BrowserWindow.LastCastLaunch; }); }
                // ---- Shield's My filters and history sync
                case "shf-get": return Async(w, async () => { var r = await w.ReadShieldFilters(); return r == null ? "error " + BrowserWindow.ShieldFiltersNote : "ok " + js.Serialize(r); });
                case "shf-set": return Async(w, async () => { var ok = await w.WriteShieldFilters(arg.Replace("\\n", "\n")); return ok ? "ok" : "error " + BrowserWindow.ShieldFiltersNote; });
                case "set":
                    return Program.OnUi(w, () =>
                    {
                        var v = p.Length > 1 ? p[1] : "";
                        switch (p[0])
                        {
                            case "syncfilters": w.S.SyncShieldFilters = v == "1"; break;
                            case "synchist": w.S.SyncHistory = v == "1"; break;
                            case "synchistmax": w.S.SyncHistoryMax = int.Parse(v); break;
                            case "jumplist": w.S.JumpList = v == "1"; break;
                            default: return "error unknown setting";
                        }
                        w.S.Fix(); w.S.Save(); return "ok";
                    });
                case "hist-add": return Program.OnUi(w, () => { w.EnsureStoresPublic(); if (!w.History.Loaded) w.History.Load(); w.History.Add(p[0], p.Length > 1 ? arg.Substring(p[0].Length + 1) : null, DateTime.Now); w.History.Save(); return "ok " + w.History.Count; });
                case "hist": return Program.OnUi(w, () => { w.EnsureStoresPublic(); if (!w.History.Loaded) w.History.Load(); return "ok " + js.Serialize(w.History.All().Select(x => new Dictionary<string, object> { { "u", x.Url }, { "t", x.Title }, { "v", x.Visits } }).ToList()); });
                case "side": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "native", w.Side.NativeOpen }, { "tabs", w.Tabs.Count }, { "windows", Program.Windows.Count }, { "private", Program.Windows.Count(x => x.Private) } }));
                case "shf-eval": return Async(w, async () => { var r = await w.ShieldPageEvalPublic(arg); return r == null ? "error no shield" : "ok " + r; });
                case "hist-clear": return Program.OnUi(w, () => { w.ClearHistory(); return "ok"; });
            }
            return "error unknown 1.7 test command";
        }
    }
}
