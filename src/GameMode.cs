// Hallow Browser - gaming-light mode, tab sleeping and the RAM / CPU limiters.
//  - A game is "running" like Hallow Guard / Hub see it: a known game process (Delta Force, Discovery...) or Hallow Hub's status
//    saying something is being played. While one runs: background tabs are suspended (TrySuspendAsync), the browser's processes
//    drop to below-normal priority and a "Game mode" chip shows. Tabs playing audio and popped-out videos always stay awake.
//  - Tab sleeping: background tabs you haven't looked at for N minutes are suspended too (Settings; never the audible ones).
//  - RAM limiter (soft, GX-style): above the cap, the least recently used background tabs are suspended, then discarded
//    (the page is closed and reloads when you click its tab).
//  - CPU limiter: the browser's processes always run at below-normal priority, so games and other apps come first.
// The decisions are plain functions (unit-tested); the window applies them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace HallowBrowser
{
    public interface IGameSource { string Game(); }   // the running game's name, or null

    public class WindowsGames : IGameSource
    {
        // process name -> the name shown on the chip
        public static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "DeltaForceClient-Win64-Shipping", "Delta Force" }, { "Discovery-d", "Discovery" } };
        public string Game()
        {
            foreach (var g in Known)
            {
                var ps = Process.GetProcessesByName(g.Key); var any = ps.Length > 0; foreach (var p in ps) p.Dispose();
                if (any) return g.Value;
            }
            // Hallow Hub knows which game is being played (its status.json, only while Hub runs)
            try
            {
                var f = Path.Combine(Family.DataDir("Hub"), "status.json");
                if (!File.Exists(f) || DateTime.Now - File.GetLastWriteTime(f) > TimeSpan.FromHours(12)) return null;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                object running, playing;
                if (d != null && d.TryGetValue("running", out running) && running is bool && (bool)running && d.TryGetValue("playing", out playing))
                {
                    var list = (playing as System.Collections.IEnumerable ?? new object[0]).Cast<object>().ToList();
                    if (list.Count > 0)
                    {
                        var first = list[0]; var dd = first as Dictionary<string, object>; object n;
                        if (dd != null && dd.TryGetValue("name", out n) && n != null) return n.ToString();
                        return first as string ?? "a game";
                    }
                }
            }
            catch { }
            return null;
        }
    }
    // tests: the game named in a file (empty / missing = none)
    public class FileGames : IGameSource
    {
        readonly string file; public FileGames(string file) { this.file = file; }
        public string Game() { try { var t = File.Exists(file) ? File.ReadAllText(file).Trim() : ""; return t.Length == 0 ? null : t; } catch { return null; } }
    }
    public class FakeGames : IGameSource { public string Value; public string Game() { return Value; } }

    // what the planner needs to know about a tab
    public class TabState
    {
        public int Id; public bool Active, Audible, Suspended, Discarded, PoppedOut, Loading; public DateTime LastActive;
    }

    public static class Planner
    {
        // tabs that may sleep at all: not the one you're looking at, not playing audio, not popped out, not discarded already
        static bool CanSleep(TabState t) { return !t.Active && !t.Audible && !t.PoppedOut && !t.Discarded; }

        // which tabs to suspend now. game = a game is running (and gaming-light is on); sleepMinutes 0 = never by time
        public static List<int> ToSuspend(IEnumerable<TabState> tabs, bool game, DateTime now, int sleepMinutes)
        {
            return tabs.Where(t => CanSleep(t) && !t.Suspended && (game || (sleepMinutes > 0 && now - t.LastActive >= TimeSpan.FromMinutes(sleepMinutes))))
                .Select(t => t.Id).ToList();
        }
        // which suspended tabs to wake when a game ends (the ones game mode put to sleep and that haven't been idle long enough to sleep anyway)
        public static List<int> ToWakeAfterGame(IEnumerable<TabState> tabs, DateTime now, int sleepMinutes, ICollection<int> sleptByGame)
        {
            return tabs.Where(t => t.Suspended && sleptByGame.Contains(t.Id) && !(sleepMinutes > 0 && now - t.LastActive >= TimeSpan.FromMinutes(sleepMinutes)))
                .Select(t => t.Id).ToList();
        }
        // RAM limiter: when usedMb is over capMb, the least recently used background tabs: first suspend those still awake, then discard
        // suspended ones (a suspended tab still holds memory). Each tab is assumed to hold an equal share. capMb 0 = off.
        public static void OverCap(IList<TabState> tabs, long usedMb, int capMb, out List<int> suspend, out List<int> discard)
        {
            suspend = new List<int>(); discard = new List<int>();
            if (capMb <= 0 || usedMb <= capMb) return;
            var live = tabs.Count(t => !t.Discarded); if (live == 0) return;
            double share = (double)usedMb / live; double over = usedMb - capMb;
            var lru = tabs.Where(CanSleep).OrderBy(t => t.LastActive).ToList();
            foreach (var t in lru) if (!t.Suspended) suspend.Add(t.Id);
            // suspending frees part of a tab's memory (count a third); discarding frees it all
            double freed = suspend.Count * share / 3;
            foreach (var t in lru)
            {
                if (freed >= over) break;
                discard.Add(t.Id); freed += t.Suspended ? share : share * 2 / 3;
            }
        }
        // the priority for the browser's processes
        public static ProcessPriorityClass Priority(bool game, bool cpuLimiter)
        {
            return game || cpuLimiter ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
        }
        public static string Chip(string game) { return game == null ? null : "GAME MODE"; }
    }
}
