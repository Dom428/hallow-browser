// Hallow Browser 1.9 - fixes from real use: the bookmarks bar shows by itself once there are bookmarks (unless you hid it), tab groups
// are easy to find (tab menu, main menu, Ctrl+K, a one-time tip, auto-group by site), the game-mode icon is small, optional and only
// there while game mode is really on (Hallow Hub's "playing" only counts while Hub runs, and never for a game whose process is gone).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HallowBrowser
{
    public static class Rules19
    {
        // the bookmarks bar turns itself on when there is at least one bookmark (yours or imported) - unless you hid it on purpose
        public static bool AutoShowBar(bool barOn, bool hiddenOnPurpose, int bookmarks, bool privateWindow)
        {
            return !barOn && !hiddenOnPurpose && bookmarks > 0 && !privateWindow;
        }
        public static bool ChipVisible(string game, bool chipSetting) { return game != null && chipSetting; }
        // Hallow Hub's "playing" list: only while Hub itself runs (its status.json stays behind when it closes), and a game the browser
        // knows by its process (Delta Force, Discovery) counts only while that process is there
        public static string FromHub(IList<string> playing, bool hubAlive, Func<string, bool> knownProcessRunning)
        {
            if (!hubAlive || playing == null) return null;
            foreach (var n in playing)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                bool known = WindowsGames.Known.Values.Any(v => string.Equals(v, n, StringComparison.OrdinalIgnoreCase));
                if (known && !knownProcessRunning(n)) continue;
                return n;
            }
            return null;
        }
        // auto-group: web tabs not in a group yet, by site (host without www.), only sites with two or more tabs
        public static List<KeyValuePair<string, List<int>>> BySite(IList<string> urls, IList<bool> grouped)
        {
            var res = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase); var order = new List<string>();
            for (int i = 0; i < urls.Count; i++)
            {
                if (grouped[i] || !Omni.IsWeb(urls[i])) continue;
                var h = Omni.HostOf(urls[i]); if (string.IsNullOrEmpty(h)) continue;
                if (h.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) h = h.Substring(4);
                List<int> l; if (!res.TryGetValue(h, out l)) { res[h] = l = new List<int>(); order.Add(h); }
                l.Add(i);
            }
            return order.Where(h => res[h].Count >= 2).Select(h => new KeyValuePair<string, List<int>>(h, res[h])).ToList();
        }
        public static string GroupName(string host)
        {
            var parts = host.Split('.'); var core = parts.Length >= 2 ? parts[parts.Length - 2] : host;
            return core.Length == 0 ? host : char.ToUpperInvariant(core[0]) + core.Substring(1);
        }
    }

    public partial class BrowserWindow
    {
        bool GameChipVisible(string game, bool setting) { return Rules19.ChipVisible(game, setting); }
        public bool GameChipShown { get { return gameChip != null && gameChip.Visibility == Visibility.Visible; } }

        // ---- the bookmarks bar by itself
        void AutoBookmarksBar()
        {
            if (snap || Bookmarks == null || !Bookmarks.Loaded) return;
            if (!Rules19.AutoShowBar(S.BookmarksBar, S.BookmarksBarHidden, Bookmarks.Count, Private)) return;
            S.BookmarksBar = true; S.Save(); ApplyBookmarksBar(); ApplyBookmarksBarAll();
        }

        // ---- tab groups
        public TabGroup GroupFromMenu(Tab t) { var g = NewGroup(t, null); S.GroupTipShown = true; S.Save(); return g; }
        public void AddToGroupFlyout(Tab t)
        {
            var sp = new StackPanel(); System.Windows.Controls.Primitives.Popup p = null;
            Action<Action> run = a => { p.IsOpen = false; a(); };
            foreach (var g in Groups.ToList()) { var gg = g; if (t.Group != g) sp.Children.Add(Ui.MenuRow("layer-group", gg.Name, null, () => run(() => { AddToGroup(t, gg); S.GroupTipShown = true; S.Save(); }))); }
            sp.Children.Add(Ui.MenuRow("plus", "New group", null, () => run(() => GroupFromMenu(t))));
            p = Panels.ShowFlyout((UIElement)t.Header ?? this, sp, 230);
        }
        public int AutoGroupBySite()
        {
            var plan = Rules19.BySite(Tabs.Select(x => x.Url).ToList(), Tabs.Select(x => x.Group != null || x.Pinned).ToList());   // 1.19: pinned tabs stay out
            if (plan.Count == 0) { Toast("Nothing to group: no site has two or more tabs outside a group.", 4); return 0; }
            var snapshot = Tabs.ToList();
            foreach (var kv in plan)
            {
                var g = NewGroup(snapshot[kv.Value[0]], Rules19.GroupName(kv.Key));
                foreach (var i in kv.Value.Skip(1)) AddToGroup(snapshot[i], g);
            }
            S.GroupTipShown = true; S.Save();
            Toast(plan.Count == 1 ? "Grouped the tabs of " + plan[0].Key + "." : "Made " + plan.Count + " groups, one per site.", 4);
            return plan.Count;
        }
        public void GroupTabsFlyout(UIElement at)
        {
            var sp = new StackPanel(); System.Windows.Controls.Primitives.Popup p = null;
            Action<Action> run = a => { p.IsOpen = false; a(); };
            sp.Children.Add(Ui.MenuRow("layer-group", "Add this tab to a new group", null, () => run(() => GroupFromMenu(Active))));
            sp.Children.Add(Ui.MenuRow("layer-group", "Auto-group tabs by site", null, () => run(() => AutoGroupBySite())));
            var tip = Ui.Muted("Or right-click any tab > Add tab to new group / Add to group. Click a group's name to fold it, right-click it to rename or recolour.", 11.5, 0); tip.Margin = new Thickness(12, 6, 12, 6); tip.MaxWidth = 260; sp.Children.Add(tip);
            p = Panels.ShowFlyout(at ?? (UIElement)menuBtn, sp, 290);
        }
        // the one-time tip (when you have 6 tabs and have never grouped any)
        void GroupTip()
        {
            if (snap || Private || AppMode || S.GroupTipShown || Groups.Count > 0 || Tabs.Count < 6) return;
            S.GroupTipShown = true; S.Save();
            Toast("Tip: lots of tabs? Right-click a tab > Add tab to new group, or Menu > Group tabs > Auto-group by site.", 8);
        }
        public bool GroupTipWasShown { get { return S.GroupTipShown; } }
    }
}
