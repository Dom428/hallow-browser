// Hallow Browser 1.8 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): SponsorBlock against a local
// stand-in server and a YouTube-like test page, the Twitch chat panel and theatre mode on a Twitch-like test page, Clip it against a
// stand-in Hallow Clips pipe (the test suffix - never the real Clips), and the game launch handoff (launch links go to a log, a game
// is never started; Steam's running flag comes from a test file).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V18(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                case "set":
                    return Program.OnUi(w, () =>
                    {
                        var v = p.Length > 1 ? p[1] : "";
                        switch (p[0])
                        {
                            case "sponsor": w.S.SponsorSkip = v == "1"; break;
                            case "mode": w.S.SponsorMode = v; break;
                            case "cats": w.S.SponsorCats = v.Split(',').ToList(); break;
                            case "twitchchat": w.S.TwitchChat = v == "1"; break;
                            case "grace": Handoff.Grace = TimeSpan.FromSeconds(double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)); return "ok";
                            default: return "error unknown setting";
                        }
                        w.S.Fix(); w.S.Save();
                        if (p[0] == "twitchchat") { if (w.Active != null) w.UpdateBarPublic18(w.Active); } else w.SponsorSettingsChanged();
                        return "ok";
                    });
                case "sb":
                {
                    var t = TabAt(w, arg); if (t == null) return "error no tab";
                    return Program.OnUi(w, () => { List<string> rq; lock (SponsorBlock.Requests) rq = SponsorBlock.Requests.ToList(); return "ok " + js.Serialize(new Dictionary<string, object> { { "vid", t.SbVid }, { "count", t.SbCount }, { "note", t.SbNote }, { "requests", rq }, { "on", w.S.SponsorSkip }, { "mode", w.S.SponsorMode }, { "cats", w.S.SponsorCats } }); });
                }
                case "sb-check": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Async(w, async () => "ok " + await w.SponsorCheck(t, t.Core, true)); }
                case "sb-clear": SponsorBlock.ClearCache(); lock (SponsorBlock.Requests) SponsorBlock.Requests.Clear(); return "ok";
                case "view":
                    return Program.OnUi(w, () =>
                    {
                        string chatSrc = null; try { if (w.Side.Chat != null && w.Side.Chat.Core != null) chatSrc = w.Side.Chat.Core.Source; } catch { }
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "theatre", w.Theatre }, { "chrome", w.ChromeShown }, { "caption", w.CaptionNow }, { "fullscreen", w.IsFullscreen },
                            { "open", w.Side.Open == null ? null : w.Side.Open.Site.Id }, { "chatChannel", w.Side.ChatChannel }, { "chatUrl", w.Side.Chat == null ? null : w.Side.Chat.Site.Url }, { "chatSrc", chatSrc },
                            { "host", w.Side.HostShown }, { "rail", w.Side.RailShown }, { "sidebarOn", w.S.SidebarOn }, { "openId", w.S.SidebarOpenId }, { "sites", w.S.SidebarSites.Count },
                            { "clipBtn", w.ClipButtonVisible }, { "theatreBtn", w.TheatreButtonVisible }, { "toast", w.LastToast } });
                    });
                case "side-hide": return Program.OnUi(w, () => { w.Side.Hide(); return "ok"; });   // the panel's X
                case "side-show": return Program.OnUi(w, () => { var sp = w.Side.Panels.FirstOrDefault(x => x.Site.Id == arg); if (sp == null) return "error"; w.Side.Show(sp); return "ok"; });
                case "theatre": return Program.OnUi(w, () => { w.SetTheatre(arg == "1"); return "ok " + w.Theatre; });
                case "fs": return Program.OnUi(w, () => { w.SetFullscreen(arg == "1"); return "ok"; });
                case "clip": { var t = TabAt(w, arg); if (t == null) return "error no tab"; return Async(w, async () => "ok " + await w.ClipIt(t)); }
                case "fam": return Program.OnUi(w, () => { NewTabFamily.Build(w.S, DateTime.Now); return "ok " + string.Join(",", NewTabFamily.LastGames.Select(g => g.Id)); });
                case "play":
                {
                    var tcs = new TaskCompletionSource<string>();
                    w.Dispatcher.BeginInvoke(new Action(() => w.PlayFromNewTab(arg, r => tcs.TrySetResult(r))));
                    if (!tcs.Task.Wait(20000)) return "error timeout";
                    return GameState(w, tcs.Task.Result);
                }
                case "game": return Program.OnUi(w, () => { w.Tick(true); return null; }) ?? GameState(w, null);
            }
            return "error unknown 1.8 test command";
        }
        static string GameState(BrowserWindow w, string result)
        {
            return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "result", result }, { "game", w.Game }, { "active", BrowserWindow.Launched.Active }, { "seen", BrowserWindow.Launched.Seen },
                { "id", BrowserWindow.Launched.Id }, { "priority", w.PriorityName }, { "toast", w.LastToast }, { "last", NewTabFamily.LastLaunch } }));
        }
    }
}
