// Hallow Browser 1.10 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): Twitch ad handling against a
// local fake Twitch (pages, usher, GraphQL and the GitHub release stand-in on 127.0.0.1 - never the real Twitch or GitHub).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V110(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                case "set":   // on 0|1, fixes 0|1, allow <channel> 0|1 (pages load again, like the switches do)
                    return Program.OnUi(w, () =>
                    {
                        var v = p.Length > 1 ? p[p.Length - 1] : "";
                        switch (p[0])
                        {
                            case "on": w.S.TwitchAds = v == "1"; w.S.Save(); w.TwitchSettingsChanged(); return "ok";
                            case "fixes": w.S.TwitchFixes = v == "1"; w.S.Save(); return "ok";
                            case "allow": return w.SupportChannel(p[1], v == "1", null) ? "ok " + p[1] : "error bad channel";
                            default: return "error unknown setting";
                        }
                    });
                case "support":   // <window> <tab> 0|1 - the Twitch button's "Support <channel>: allow ads"
                    return Program.OnUi(w, () =>
                    {
                        var x = WinAt(p[0]); int ti; if (x == null || !int.TryParse(p[1], out ti) || ti >= x.Tabs.Count) return "error no tab";
                        var t = x.Tabs[ti]; var ch = Twitch.Channel(t.Url); if (ch == null) return "error not a channel";
                        return x.SupportChannel(ch, p[2] == "1", t) ? "ok " + ch : "error";
                    });
                case "state":   // <window> <tab>
                    return Program.OnUi(w, () =>
                    {
                        var x = WinAt(p[0]); int ti; if (x == null || !int.TryParse(p[1], out ti) || ti >= x.Tabs.Count) return "error no tab";
                        var t = x.Tabs[ti]; bool coreMuted = false; try { coreMuted = t.Core != null && t.Core.IsMuted; } catch { }
                        if (x.Active == t) x.UpdateTwitchButton(t);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "note", t.TwNote }, { "swaps", t.TwSwaps }, { "covers", t.TwCovers }, { "covered", t.TwCovered }, { "mutedByUs", t.TwMutedByUs },
                            { "coreMuted", coreMuted }, { "scriptV", t.TwScriptV }, { "blocked", BrowserWindow.TwBlocked }, { "on", x.S.TwitchAds }, { "allow", x.S.TwitchAllow }, { "private", x.Private },
                            { "button", x.Active == t && x.TwitchButtonVisible }, { "tip", x.Active == t ? x.TwitchButtonTip : null }, { "status", x.TwitchStatus(t) }, { "version", TwitchAdRules.CurrentVersion }, { "source", TwitchAdRules.Source } });
                    });
                case "update": return "ok " + (TwitchAdRules.CheckForUpdate() ?? "none");
                case "reload-script": TwitchAdRules.Reload(); return Program.OnUi(w, () => { w.TwitchSettingsChanged(); return "ok " + TwitchAdRules.CurrentVersion; });
                case "settings": return Program.OnUi(w, () => "ok " + w.TwitchAdSettings().Length);
                case "flyout": return Program.OnUi(w, () => { w.TwitchFlyout(w.Active); return "ok"; });
            }
            return "error unknown 1.10 test command";
        }
    }
}
