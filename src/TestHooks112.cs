// Hallow Browser 1.12 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): the default-browser card and
// the new tab page's hint. Nothing here reads or writes the registration; Windows Settings is never opened in a test folder.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V112(BrowserWindow w, string cmd, string arg)
        {
            switch (cmd)
            {
                case "state":
                    return Program.OnUi(w, () =>
                    {
                        var st = DefaultBrowser.Status();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "text", st.Text }, { "isHallow", st.IsHallow }, { "name", st.Name }, { "progId", st.HttpsProgId },
                            { "dismissed", w.S.DefaultHintDismissed }, { "opened", DefaultBrowser.LastOpened }, { "start", DefaultBrowser.LastStart }, { "game", w.Game } });
                    });
                case "clear": return Program.OnUi(w, () => { DefaultBrowser.LastOpened = null; return "ok"; });
                case "hint": return Program.OnUi(w, () => { w.S.DefaultHintDismissed = arg != "1"; w.S.Save(); w.DefaultChanged(); return "ok"; });   // hint 1 = shown again
                case "look":   // light | dark - the browser's look (the new tab page is written again in it)
                    return Program.OnUi(w, () => { w.S.Look = arg == "light" ? "light" : "dark"; w.S.Save(); Program.ReloadTheme(); return "ok " + (Look.IsLight ? "light" : "dark"); });
                case "card":   // the Settings card's texts and buttons
                    return Program.OnUi(w, () =>
                    {
                        var items = w.DefaultBrowserSettings();
                        var texts = new List<string>();
                        foreach (var i in items) texts.Add(SettingsLayout.TextOf(i));
                        return "ok " + js.Serialize(texts);
                    });
                case "make":   // the card's main button, clicked
                    return Program.OnUi(w, () =>
                    {
                        var items = w.DefaultBrowserSettings();
                        var row = items.OfType<StackPanel>().FirstOrDefault(); var b = row == null ? null : row.Children.OfType<Button>().FirstOrDefault();
                        if (b == null) return "error no button";
                        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        return "ok " + (DefaultBrowser.LastOpened ?? "");
                    });
            }
            return "error unknown 1.12 test command";
        }
    }
}
