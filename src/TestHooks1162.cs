// Hallow Browser 1.16.2 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): a log of the page requests (cookies never logged)
// as Hallow hands them to the network (address, method, headers), to check sign-in requests leave unchanged.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V1162(BrowserWindow w, string cmd, string arg)
        {
            switch (cmd)
            {
                case "on": lock (BrowserWindow.NetLog) { BrowserWindow.NetLogOn = true; BrowserWindow.NetLog.Clear(); } return "ok";
                case "state": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "signInLeftAlone", w.SignInRequestsLeftAlone }, { "pageClosesKept", w.PageClosesKept }, { "busy", w.Tabs.Select(t => t.SignInBusy).ToList() }, { "asleep", w.Tabs.Select(t => t.Suspended).ToList() }, { "windows", Program.Windows.Count }, { "foreignRefused", w.ForeignClosesRefused }, { "dark", w.Tabs.Select(t => t.DarkApplied == true).ToList() }, { "light", Look.IsLight }, { "closeTrace", BrowserWindow.CloseTrace.ToList() } }));
                case "colors": return Program.OnUi(w, () => { var p = arg.Split(' '); w.S.PageColors = p[0]; if (p.Length > 1) w.S.ForceDark = p[1] == "true"; w.S.Save(); w.ApplyPageColors(); w.DarkChanged(); return "ok"; });
                case "get": lock (BrowserWindow.NetLog) return "ok " + js.Serialize(BrowserWindow.NetLog.ToList());
            }
            return "error unknown test command";
        }
    }
}
