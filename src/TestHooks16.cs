// Hallow Browser 1.6 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): profiles and their
// processes, the page menu's own items (through DevTools input into the off-screen page - never the real mouse), spell check
// words, memory saver / battery (a fake battery file), startup boost, installed sites and app windows (Start menu shortcuts in the
// test folder), the reading list and notes, the Hallow family rows (stand-in Hub / Clips / Overlay; launch links are written to
// a log file, never opened) and the sounds (played at volume 0).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V16(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries); string rest;
            switch (cmd)
            {
                case "info":
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "profile", BrowserSettings.ProfileId }, { "pipe", Program.PipeApp(BrowserSettings.ProfileId) }, { "dir", BrowserSettings.Dir }, { "local", BrowserSettings.LocalDir },
                        { "webview", w.Tabs.Where(t => t.Core != null).Select(t => t.Core.Profile.ProfileName).FirstOrDefault() }, { "profilePath", w.Tabs.Where(t => t.Core != null).Select(t => t.Core.Profile.ProfilePath).FirstOrDefault() },
                        { "profiles", (Program.Profiles ?? ProfileStore.Load()).Items.Select(x => x.Id + "|" + x.Name + "|" + x.Color).ToList() }, { "chip", w.ProfileChipText }, { "title", w.Title }, { "boosted", Program.Boosted }, { "launched", Program.LaunchedProfiles },
                        { "history", HistoryStore.DefaultPath }, { "shield", w.ShieldState }, { "shieldId", w.ShieldId }, { "browserPid", w.Tabs.Where(t => t.Core != null).Select(t => (int)t.Core.BrowserProcessId).FirstOrDefault() }, { "migrated", (Program.Profiles ?? ProfileStore.Load()).Migrated }, { "backup", (Program.Profiles ?? ProfileStore.Load()).Backup } }));
                case "addprofile": return Program.OnUi(w, () => { var s = ProfileStore.Load(); var pr = s.Add(arg, null); if (pr == null) return "error"; s.Save(); Program.Profiles = s; foreach (var x in Program.Windows) x.ProfileChrome(); return "ok " + pr.Id; });
                case "launch": return Program.OnUi(w, () => Program.LaunchProfile(p[0], p.Length > 1 ? p[1] : null) ? "ok" : "error");
                case "openinprofile": return Program.OnUi(w, () => w.OpenInProfile(p[0], p[1]) ? "ok" : "error");
                case "menu": return "ok " + js.Serialize(new Dictionary<string, object> { { "items", BrowserWindow.LastMenuItems }, { "word", BrowserWindow.LastMenuWord } });
                case "addword": return Program.OnUi(w, () => { w.AddWord(arg); return "ok"; });
                case "spell":
                    return Program.OnUi(w, () =>
                    {
                        var f = Path.Combine(BrowserSettings.EngineProfileFolder(BrowserSettings.WebViewProfile), "Custom Dictionary.txt");
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "words", w.S.SpellWords }, { "file", File.Exists(f) ? File.ReadAllText(f) : null }, { "on", w.S.SpellCheck }, { "langs", SpellRules.WindowsLanguages() }, { "lang", Engine.SpellLanguage() } });
                    });
                case "set":
                    return Program.OnUi(w, () =>
                    {
                        var k = p[0]; var v = p.Length > 1 ? p[1] : "";
                        switch (k)
                        {
                            case "memory": w.S.MemorySaver = v; break;
                            case "battery": w.S.BatterySaver = v == "1"; break;
                            case "boost": w.S.StartupBoost = v == "1"; break;
                            case "sounds": w.S.Sounds = v == "1"; break;
                            case "volume": w.S.SoundVolume = int.Parse(v); break;
                            case "ambience": w.S.Ambience = v == "1"; Vibe.Ambience(w); break;
                            case "ambvolume": w.S.AmbienceVolume = int.Parse(v); break;
                            case "spell": w.S.SpellCheck = v == "1"; break;
                            case "hallow": w.S.NewTabHallow = v == "1"; break;
                            case "sleep": w.S.SleepMinutes = int.Parse(v); break;
                            default: return "error unknown setting";
                        }
                        w.S.Fix(); w.S.Save(); w.Tick(true); return "ok";
                    });
                // ---- memory saver and battery
                case "mem":
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "level", w.S.MemorySaver }, { "minutes", w.SleepMinutesNow() }, { "battery", Power16.OnBattery }, { "batteryMode", w.BatteryMode }, { "slowed", w.SlowedCount },
                        { "tabs", w.Tabs.Select(t => new Dictionary<string, object> { { "asleep", t.Suspended }, { "discarded", t.Discarded }, { "saved", t.SavedMb }, { "last", t.LastMemMb }, { "tip", w.SleepTip(t) } }).ToList() } }));
                case "sleep": { var t = TabAt(w, arg); if (t == null) return "error"; return Async(w, async () => { await w.SleepNow(t); await Task.Delay(3500); return "ok " + t.SavedMb; }); }
                case "tabmem": { var t = TabAt(w, arg); if (t == null) return "error"; return Async(w, async () => "ok " + await w.TabMemoryMb(t)); }
                case "throttle": { var t = TabAt(w, arg); if (t == null) return "error"; return Async(w, async () => t.Core == null ? "error" : "ok " + await Panels.Eval(t.Core, "(function(){var a=performance.now(),n=0;while(performance.now()-a<200)n++;return n;})()")); }
                // ---- startup boost
                case "exit": w.Dispatcher.BeginInvoke(new Action(Program.Quit)); return "ok";   // the menu's Exit
                case "boost": return "ok " + js.Serialize(new Dictionary<string, object> { { "boosted", Program.Boosted }, { "mb", Program.BoostMemoryMb() }, { "ws", Program.BoostWorkingSetMb() }, { "windows", Program.Windows.Count }, { "env", Engine.Env != null }, { "wake", Engine.Perf.ContainsKey("boost-wake") ? (object)Engine.Perf["boost-wake"] : null } });
                // ---- installed sites
                case "install": { var t = TabAt(w, arg); if (t == null) return "error"; return Async(w, async () => { var a = await w.InstallSite(t); return a == null ? "error" : "ok " + js.Serialize(a); }); }
                case "apps": return "ok " + js.Serialize(SiteApps.Load().Select(a => new Dictionary<string, object> { { "app", a }, { "lnkArgs", a.Shortcut == null ? null : SiteApps.ReadShortcut(a.Shortcut, "Arguments") }, { "lnkTarget", a.Shortcut == null ? null : SiteApps.ReadShortcut(a.Shortcut, "TargetPath") }, { "lnkIcon", a.Shortcut == null ? null : SiteApps.ReadShortcut(a.Shortcut, "IconLocation") }, { "ico", a.Ico != null && File.Exists(a.Ico) ? new FileInfo(a.Ico).Length : 0 } }).ToList());
                case "uninstall": return Program.OnUi(w, () => BrowserWindow.UninstallApp(arg) ? "ok" : "error");
                case "openapp": return Program.OnUi(w, () => { Program.OpenApp(arg); return "ok"; });
                case "appspage": return Async(w, async () => { w.NewTab(Omni.AppsUrl, true); await Task.Delay(300); var x = Program.Windows.FirstOrDefault(y => y.AppsPageRows >= 0); if (x != null) x.FillApps(); return "ok " + (x == null ? -1 : x.AppsPageRows); });
                case "wins":
                    return Program.OnUi(w, () => "ok " + js.Serialize(Program.Windows.Select(x => new Dictionary<string, object> { { "app", x.AppMode ? x.App.Name : null }, { "title", x.Title }, { "private", x.Private }, { "tabs", x.Tabs.Select(t => t.Url).ToList() }, { "chip", x.ProfileChipText }, { "first", x == Program.First }, { "profile", x.Tabs.Where(t => t.Core != null).Select(t => t.Core.Profile.ProfileName).FirstOrDefault() }, { "inprivate", x.Tabs.Where(t => t.Core != null).Select(t => (bool?)t.Core.Profile.IsInPrivateModeEnabled).FirstOrDefault() } }).ToList()));
                case "appeval": { var a = Program.Windows.FirstOrDefault(x => x.AppMode); if (a == null) return "error no app window"; return Async(a, async () => a.Active == null || a.Active.Core == null ? "error no view" : "ok " + await Panels.Eval(a.Active.Core, arg)); }
                case "appcdp": { var a = Program.Windows.FirstOrDefault(x => x.AppMode); if (a == null) return "error no app window"; var m = Split(arg, out rest); return Async(a, async () => a.Active == null || a.Active.Core == null ? "error no view" : "ok " + await a.Active.Core.CallDevToolsProtocolMethodAsync(m, rest.Length == 0 ? "{}" : rest)); }
                case "appclose": return Program.OnUi(w, () => { foreach (var a in Program.Windows.Where(x => x.AppMode).ToList()) a.Close(); return "ok"; });
                // ---- reading list and notes
                case "readadd": return Program.OnUi(w, () => { var t = TabAt(w, arg); if (t == null) return "error"; w.SaveForLater(t); return "ok " + ReadingList.Count(false); });
                case "readlist": return "ok " + js.Serialize(ReadingList.Items);
                case "readmark": return Program.OnUi(w, () => { ReadingList.SetRead(p[1], p[0] == "1"); w.Side.RefreshNative(); return "ok"; });
                case "panel": return Program.OnUi(w, () => { if (arg == "hide") w.Side.Hide(); else w.Side.ShowNative(arg); return "ok " + js.Serialize(new Dictionary<string, object> { { "open", w.Side.NativeOpen }, { "text", NativeTexts(w) } }); });
                case "note": return Program.OnUi(w, () => { w.Side.ShowNative("notes"); return w.Side.TypeNote(arg) ? "ok" : "error no note box"; });
                case "notes": return "ok " + js.Serialize(PageNotes.All);
                case "notesreset": ReadingList.Reset(); PageNotes.Reset(); return "ok";
                // ---- the Hallow family rows
                case "family": return "ok " + js.Serialize(new Dictionary<string, object> { { "games", NewTabFamily.LastGames.Select(g => g.Id + "|" + g.Name + "|" + (g.Pic ?? "") + "|" + g.Playing).ToList() }, { "clips", NewTabFamily.LastClips.Select(c => c.Id + "|" + c.Name + "|" + (c.Pic ?? "")).ToList() }, { "launches", NewTabFamily.Launches }, { "hubPlays", NewTabFamily.HubPlays }, { "clipOpens", NewTabFamily.ClipOpens }, { "overlay", NewTabFamily.OverlayToggles }, { "last", NewTabFamily.LastLaunch }, { "writes", BrowserWindow.FamilyWrites } });
                case "famrefresh": return Async(w, async () => { w.RefreshFamily(0); await Task.Delay(1200); var f = Path.Combine(Engine.NewTabDir, "family.json"); return "ok " + (File.Exists(f) ? File.ReadAllText(f) : "null"); });
                // ---- sounds
                case "vibe": return "ok " + js.Serialize(new Dictionary<string, object> { { "played", Vibe.Played }, { "opened", Vibe.Opened }, { "skipped", Vibe.Skipped }, { "last", Vibe.Last }, { "ambience", Vibe.AmbiencePlaying } });
                case "play": return Program.OnUi(w, () => { Vibe.Play(arg, w); return "ok"; });
            }
            return "error unknown command";
        }
        static List<string> NativeTexts(BrowserWindow w) { return w.Side.NativeTexts(); }
    }

    public partial class BrowserWindow
    {
        public Task SleepNow(Tab t) { return Suspend(t, false); }
    }
    public partial class Sidebar
    {
        public List<string> NativeTexts()
        {
            var res = new List<string>(); if (nativeView == null) return res;
            Action<System.Windows.DependencyObject> walk = null;
            walk = d => { var tb = d as System.Windows.Controls.TextBlock; if (tb != null && !string.IsNullOrEmpty(tb.Text)) res.Add(tb.Text); var tx = d as System.Windows.Controls.TextBox; if (tx != null) res.Add("[box]" + tx.Text); foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(d)) { if (c is string) res.Add((string)c); else if (c is System.Windows.DependencyObject) walk((System.Windows.DependencyObject)c); } };
            walk(nativeView); return res;
        }
    }
}
