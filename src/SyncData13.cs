// Hallow Browser 1.3 - what sync carries, as records (Sync13.cs merges them): bookmarks and folders, the settings that make
// sense on every PC, saved sessions, workspaces and the sidebar's panels. And back: merged records into this PC's stores.
// Never: cookies, Hallow Keys data, addresses you typed, window places, per-PC choices (RAM limiter, CPU limiter), translation keys,
// the sync settings themselves. 1.6: page notes. 1.7: Hallow Shield's My filters (read / written through Shield - Sync17.cs) and,
// opt-in and only encrypted, history.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace HallowBrowser
{
    public partial class SyncInput
    {
        public BrowserSettings Settings; public BookmarkStore Bookmarks;
        public List<SavedSession> Saved; public WorkspaceStore Spaces; public string CurrentSpace; public Session CurrentLive;
        public List<PageNote> Notes;   // 1.6: notes per page
    }

    public static class SyncData
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        // the settings that sync (by field name); everything else stays on its PC
        public static readonly string[] SettingKeys = {
            "Search", "RestoreSession", "ClearOnExit", "BlockThirdPartyCookies", "DoNotTrack", "StrictTracking", "PageColors", "Look", "UseHallowAccent",
            "GameLight", "SleepMinutes", "Preconnect", "KeysOn", "ShieldAutoUpdate", "SidebarOn", "SidebarSleepMinutes", "LiveOn", "LiveChannels",
            "BandwidthKBps", "BandwidthGameOnly", "VerticalTabs", "VTabsCollapsed", "SidebarSide", "ReaderFontSize", "ReaderWidth", "KeepHistory",
            "BookmarksBar", "NewTabTopSites", "NewTabClock", "NewTabFamily", "NewTabPinned", "NewTabHidden" };
        public static readonly string[] Kinds = { "bm", "set", "saved", "ws", "side", "note", "shf", "hist" };   // 1.6: "note"; 1.7: Shield's My filters, history
        static FieldInfo F(string n) { return typeof(BrowserSettings).GetField(n, BindingFlags.Public | BindingFlags.Instance); }

        static string Ser(object o) { return js.Serialize(o); }
        public static string BookmarkData(Bookmark b)
        {
            var d = new SortedDictionary<string, object>(StringComparer.Ordinal) { { "a", b.Added }, { "f", b.Folder }, { "p", b.Parent }, { "pos", b.Pos.ToString("R", System.Globalization.CultureInfo.InvariantCulture) }, { "t", b.Title } };
            if (!b.Folder) d["u"] = b.Url;
            return Ser(d);
        }
        public static Bookmark BookmarkFrom(SyncRecord r)
        {
            var d = js.Deserialize<Dictionary<string, object>>(r.Data);
            Func<string, object> V = k => { object v; return d.TryGetValue(k, out v) ? v : null; };
            return new Bookmark { Id = r.Id.Substring(3), Added = V("a") as string, Folder = V("f") is bool && (bool)V("f"), Parent = V("p") as string ?? BookmarkStore.OtherId,
                Pos = PosOf(V("pos")), Title = V("t") as string, Url = V("u") as string };
        }

        static double PosOf(object v) { double d; return v != null && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0; }

        // ---- this PC's records
        public static List<SyncRecord> Gather(SyncInput i)
        {
            var res = new List<SyncRecord>();
            if (i.Bookmarks != null) foreach (var b in i.Bookmarks.Everything()) res.Add(new SyncRecord { Id = "bm:" + b.Id, Kind = "bm", Data = BookmarkData(b) });
            if (i.Settings != null)
            {
                foreach (var k in SettingKeys) { var f = F(k); if (f != null) res.Add(new SyncRecord { Id = "set:" + k, Kind = "set", Data = Ser(f.GetValue(i.Settings)) }); }
                if (i.Settings.SidebarSites != null) res.Add(new SyncRecord { Id = "side", Kind = "side", Data = Ser(i.Settings.SidebarSites.Select(x => new SortedDictionary<string, object>(StringComparer.Ordinal) { { "Id", x.Id }, { "Mobile", x.Mobile }, { "Name", x.Name }, { "Url", x.Url } }).ToList()) });
            }
            if (i.Saved != null)
                foreach (var s in i.Saved.GroupBy(x => (x.Name ?? "").ToLowerInvariant()).Select(g => g.First()))
                    res.Add(new SyncRecord { Id = "saved:" + (s.Name ?? "").ToLowerInvariant(), Kind = "saved", Data = Ser(s) });
            // 1.6: a note per page (its id from the page's address, the same on every PC)
            if (i.Notes != null)
                foreach (var n in i.Notes.Where(x => x != null && x.Key != null && !string.IsNullOrEmpty(x.Text)))
                    res.Add(new SyncRecord { Id = "note:" + PageNotes.IdOf(n.Key), Kind = "note", Data = Ser(new SortedDictionary<string, object>(StringComparer.Ordinal) { { "k", n.Key }, { "t", n.Title }, { "u", n.Url }, { "x", n.Text } }) });
            // 1.7: Shield's My filters (only when they could be read) and history (only when it syncs - encrypted)
            if (i.ShieldFilters != null) res.Add(new SyncRecord { Id = "shf", Kind = "shf", Data = Ser(i.ShieldFilters.Replace("\r\n", "\n")) });
            if (i.History != null && i.HistoryMax > 0) res.AddRange(SyncHistory.Records(i.History.All(), i.HistoryMax));
            if (i.Spaces != null && i.Spaces.Items != null)
                foreach (var w in i.Spaces.Items)
                {
                    var sess = w.Id == i.CurrentSpace && i.CurrentLive != null ? i.CurrentLive : w.Session;
                    res.Add(new SyncRecord { Id = "ws:" + w.Id, Kind = "ws", Data = Ser(new Workspace { Id = w.Id, Name = w.Name, Session = sess }) });
                }
            return res;
        }
        // 1.7: kinds whose two changed versions are put together instead of one winning
        public static string Combine(SyncRecord x, SyncRecord y) { return x.Kind == "hist" && y.Kind == "hist" && !x.Gone && !y.Gone ? SyncHistory.Combine(x, y) : null; }
        // a hash per kind of what this PC has (so a merge isn't applied over an edit made meanwhile)
        public static Dictionary<string, string> KindHashes(List<SyncRecord> rs)
        {
            return Kinds.ToDictionary(k => k, k => SyncRecord.HashOf(k, string.Join("\u0001", rs.Where(r => r.Kind == k).OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Id + "=" + r.Data))));
        }

        // the content of a conflict copy: bookmarks and saved sessions keep the other version next to the winner
        public static string CopyData(SyncRecord lose, string from)
        {
            try
            {
                if (lose.Kind == "bm")
                {
                    var b = BookmarkFrom(lose); if (b.Folder) return null;
                    b.Title = (b.Title ?? "Untitled") + " (conflict copy from " + from + ")"; b.Pos += 0.001;
                    return BookmarkData(b);
                }
                if (lose.Kind == "saved")
                {
                    var s = js.Deserialize<SavedSession>(lose.Data); s.Name = (s.Name ?? "Session") + " (conflict copy from " + from + ")";
                    return Ser(s);
                }
            }
            catch { }
            return null;
        }

        // ---- merged records into this PC (only the kinds that changed). Returns what changed, for a short note.
        public static List<string> Apply(SyncInput i, List<SyncRecord> result, HashSet<string> kinds)
        {
            var done = new List<string>();
            if (kinds.Contains("bm") && i.Bookmarks != null)
            {
                i.Bookmarks.Replace(result.Where(r => r.Kind == "bm").Select(r => { try { return BookmarkFrom(r); } catch { return null; } }).Where(b => b != null));
                done.Add("bookmarks");
            }
            if ((kinds.Contains("set") || kinds.Contains("side")) && i.Settings != null)
            {
                foreach (var r in result.Where(r => r.Kind == "set"))
                {
                    var f = F(r.Id.Substring(4)); if (f == null || Array.IndexOf(SettingKeys, f.Name) < 0) continue;
                    try { f.SetValue(i.Settings, js.Deserialize(r.Data, f.FieldType)); } catch { }
                }
                var side = result.FirstOrDefault(r => r.Kind == "side");
                if (side != null && kinds.Contains("side"))
                {
                    try
                    {
                        var list = js.Deserialize<List<SidebarSite>>(side.Data);
                        // the panels you have keep their objects (and open views) and their width - a width stays per PC
                        var have = (i.Settings.SidebarSites ?? new List<SidebarSite>()).Where(x => x.Id != null).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
                        var merged = new List<SidebarSite>();
                        foreach (var x in list)
                        {
                            SidebarSite old;
                            if (x.Id != null && have.TryGetValue(x.Id, out old)) { old.Name = x.Name; old.Url = x.Url; old.Mobile = x.Mobile; merged.Add(old); }
                            else { x.Width = 420; merged.Add(x); }
                        }
                        i.Settings.SidebarSites = merged;
                    }
                    catch { }
                }
                i.Settings.Fix();
                done.Add("settings");
            }
            if (kinds.Contains("note") && i.Notes != null)
            {
                var have = i.Notes.Where(x => x.Key != null).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First());
                var list = new List<PageNote>();
                foreach (var r in result.Where(r => r.Kind == "note"))
                {
                    try
                    {
                        var d = js.Deserialize<Dictionary<string, object>>(r.Data); object k, t, u, x; d.TryGetValue("k", out k); d.TryGetValue("t", out t); d.TryGetValue("u", out u); d.TryGetValue("x", out x);
                        var key = k as string; if (key == null || !(x is string)) continue;
                        PageNote old; var upd = have.TryGetValue(key, out old) && old.Text == (string)x ? old.Updated : DateTime.Now.ToString("s");
                        list.Add(new PageNote { Key = key, Title = t as string, Url = u as string ?? key, Text = (string)x, Updated = upd });
                    }
                    catch { }
                }
                i.Notes.Clear(); i.Notes.AddRange(list);
                done.Add("notes");
            }
            // 1.7: My filters come in (the window writes them into Shield); pages from other PCs are merged into history
            if (kinds.Contains("shf") && i.ShieldFilters != null)   // only when this PC syncs them and could read Shield (else they just pass through)
            {
                var r = result.FirstOrDefault(x => x.Kind == "shf" && x.Id == "shf");
                if (r != null) { try { var text = js.Deserialize<string>(r.Data); if (text != null && text != (i.ShieldFilters ?? "").Replace("\r\n", "\n")) { i.ShieldFiltersIn = text; done.Add("Shield's My filters"); } } catch { } }
            }
            if (kinds.Contains("hist") && i.History != null && i.HistoryMax > 0)
            {
                var n = i.History.MergeSynced(result.Where(x => x.Kind == "hist").Select(SyncHistory.From).Where(x => x != null).ToList());
                if (n > 0) done.Add("history");
            }
            if (kinds.Contains("saved") && i.Saved != null)
            {
                i.Saved.Clear();
                foreach (var r in result.Where(r => r.Kind == "saved")) try { var s = js.Deserialize<SavedSession>(r.Data); if (s != null && s.Session != null) i.Saved.Add(s); } catch { }
                done.Add("saved sessions");
            }
            if (kinds.Contains("ws") && i.Spaces != null)
            {
                var cur = i.Spaces.Items.FirstOrDefault(x => x.Id == i.CurrentSpace);
                var list = new List<Workspace>();
                foreach (var r in result.Where(r => r.Kind == "ws"))
                {
                    Workspace w; try { w = js.Deserialize<Workspace>(r.Data); } catch { continue; }
                    if (w == null || string.IsNullOrEmpty(w.Id)) continue;
                    if (cur != null && w.Id == cur.Id) { cur.Name = w.Name; list.Add(cur); }   // the workspace you're in keeps its open tabs
                    else list.Add(w);
                }
                if (cur != null && !list.Contains(cur)) list.Insert(0, cur);   // never removed under you
                i.Spaces.Items = list;
                done.Add("workspaces");
            }
            return done;
        }
    }
}
