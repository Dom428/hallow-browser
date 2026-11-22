// Hallow Browser 1.12.1 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): the memory target per tab.
using System.Collections.Generic;
using System.Linq;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string V1121(BrowserWindow w, string cmd, string arg)
        {
            switch (cmd)
            {
                case "mem":   // per tab: shown, memory target low; the priority
                    return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> {
                        { "priority", w.PriorityName }, { "game", w.Game },
                        { "tabs", w.Tabs.Select(t => new Dictionary<string, object> { { "active", t == w.Active }, { "low", w.MemoryLow(t) } }).ToList() } }));
            }
            return "error unknown 1.12.1 test command";
        }
    }
}
