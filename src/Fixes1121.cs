// Hallow Browser 1.12.1 - the Twitch player could show as a plain white box (real use). The page part (src\hallow-twitch.js, version
// 3) now never hides a box that holds the video, hides empty ad frames sitting over it and nudges a stuck video. Here, in the browser:
// the memory target. 1.1 set every tab to a "low" memory target while a game ran (and 1.6 on battery) - including the page you are
// looking at, and a tab that became visible stayed low. Low is meant for pages nobody sees: the engine then drops what it has drawn
// and cached for them. Now only pages that aren't shown get it, and a page gets the normal target back the moment it is shown.
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class MemoryRules
    {
        // a page's memory target: low only when nobody can see it and something asks for less (a game / the CPU limiter, battery saver)
        public static bool Low(bool shown, bool lessWanted, bool battery) { return !shown && (lessWanted || battery); }
    }

    public partial class BrowserWindow
    {
        readonly Dictionary<CoreWebView2, bool> memLow = new Dictionary<CoreWebView2, bool>();
        public bool MemoryLow(Tab t) { bool v; return t != null && t.Core != null && memLow.TryGetValue(t.Core, out v) && v; }
        // called when the priority changes, when a tab is shown and every tick
        void ApplyMemoryTargets()
        {
            bool less = appliedPriority.HasValue && appliedPriority.Value != System.Diagnostics.ProcessPriorityClass.Normal;
            foreach (var t in Tabs)
            {
                var c = t.Core; if (c == null) continue;
                bool low = MemoryRules.Low(IsShown(t) || t.PoppedOut, less, BatteryMode && !t.Audible), cur;
                if (memLow.TryGetValue(c, out cur) && cur == low) continue;
                memLow[c] = low;
                try { c.MemoryUsageTargetLevel = low ? CoreWebView2MemoryUsageTargetLevel.Low : CoreWebView2MemoryUsageTargetLevel.Normal; } catch { }
            }
            if (memLow.Count > Tabs.Count + 8) { var live = new HashSet<CoreWebView2>(); foreach (var t in Tabs) if (t.Core != null) live.Add(t.Core); foreach (var k in new List<CoreWebView2>(memLow.Keys)) if (!live.Contains(k)) memLow.Remove(k); }
        }
    }
}
