// Hallow Browser 1.2 - the plain parts of the power features (unit-tested): tab groups, fuzzy search for Ctrl+K, the local
// history store (in the profile folder), bookmarks, workspace names and the reader-mode document.
// Nothing here is ever sent anywhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Windows.Controls;

namespace HallowBrowser
{
    // ---------------------------------------------------------------- tab groups
    public class TabGroup
    {
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name = "Group", Color = "blue";
        public bool Collapsed;
        public Border Chip;   // the group's label in the strip / column
    }

    public static class GroupRules
    {
        public static readonly string[] Colors = { "blue", "red", "yellow", "green", "pink", "purple", "cyan", "orange", "grey" };
        public static string Normalize(string c) { c = (c ?? "").Trim().ToLowerInvariant(); if (c == "gray") c = "grey"; return Array.IndexOf(Colors, c) >= 0 ? c : "blue"; }
        // the theme resource with the group's colour (light and dark values in Theme.xaml / Look.KeyLight)
        public static string Key(string c) { c = Normalize(c); return "Group" + char.ToUpperInvariant(c[0]) + c.Substring(1); }
        public static string Label(string c) { c = Normalize(c); return char.ToUpperInvariant(c[0]) + c.Substring(1); }
        public static string NextColor(IEnumerable<string> used)
        {
            var u = new HashSet<string>(used.Select(Normalize));
            foreach (var c in Colors) if (!u.Contains(c)) return c;
            return Colors[u.Count % Colors.Length];
        }
        public static string Name(string typed, int n) { var s = (typed ?? "").Trim(); if (s.Length > 40) s = s.Substring(0, 40); return s.Length == 0 ? "Group " + n : s; }

        // a group's tabs stay next to each other: the group sits where its first tab is, its tabs keep their order
        public static List<T> Contiguous<T>(IList<T> items, Func<T, object> group)
        {
            var res = new List<T>(); var done = new HashSet<object>();
            foreach (var x in items)
            {
                var g = group(x);
                if (g == null) { res.Add(x); continue; }
                if (!done.Add(g)) continue;
                foreach (var y in items) if (Equals(group(y), g)) res.Add(y);
            }
            return res;
        }
        // the group a dragged tab ends up in: dropped on a group's label = that group; between two tabs of one group = that group;
        // next to its own group's tabs = stays; a group's only tab takes its group along; anywhere else = no group
        public static G Drop<G>(G prev, G next, G own, G chip, bool ownHasOthers) where G : class
        {
            if (chip != null) return chip;
            if (prev != null && prev == next) return prev;
            if (own != null && (prev == own || next == own || !ownHasOthers)) return own;
            return null;
        }
    }

    // ---------------------------------------------------------------- fuzzy search (Ctrl+K)
    public static class Fuzzy
    {
        // -1 = no match, higher = better. Every word of the query has to match: as a piece of the text (best, more at the start of
        // a word), or as letters in order close together ("hbr" finds "Hallow Browser").
        public static int Score(string query, string text)
        {
            if (string.IsNullOrWhiteSpace(query)) return 0;
            if (string.IsNullOrEmpty(text)) return -1;
            var t = text.ToLowerInvariant(); int total = 0;
            foreach (var w in query.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = Word(w, t); if (s < 0) return -1; total += s;
            }
            return total;
        }
        static int Word(string w, string t)
        {
            int idx = t.IndexOf(w, StringComparison.Ordinal);
            if (idx >= 0)
            {
                int s = 100 + w.Length * 10;
                if (idx == 0) s += 60; else if (!char.IsLetterOrDigit(t[idx - 1])) s += 40;
                return s - Math.Min(30, t.Length / 10);
            }
            int ti = 0, sc = 0, run = 0, last = -2, first = -1;
            foreach (var c in w)
            {
                int f = t.IndexOf(c, ti); if (f < 0) return -1;
                if (first < 0) first = f;
                if (f == last + 1) { run++; sc += 5 + run * 3; } else { run = 0; sc += 1; }
                if (f == 0 || !char.IsLetterOrDigit(t[f - 1])) sc += 8;
                last = f; ti = f + 1;
            }
            if (last - first > w.Length * 2 + 4) return -1;   // letters too far apart: not a match
            return Math.Max(1, sc - (last - first) / 4);
        }
    }

    // ---------------------------------------------------------------- the local history store
    [DataContract] public class HistoryEntry { [DataMember] public string Url; [DataMember] public string Title; [DataMember] public string Last; [DataMember] public int Visits; }
    [DataContract] class HistoryFile { [DataMember] public List<HistoryEntry> Items = new List<HistoryEntry>(); }

    // the pages you visited (address, title, last visit, visits), newest first, at most 5,000 - in the profile folder
    // (%LOCALAPPDATA%\HallowBrowser\Profile\hallow-history.json). Ctrl+K searches it; "Clear history" empties it.
    public class HistoryStore
    {
        public const int Max = 5000;
        readonly string path; readonly object gate = new object();
        List<HistoryEntry> items = new List<HistoryEntry>(); bool loaded, dirty;
        public HistoryStore(string file) { path = file; }
        public static string DefaultPath { get { return Path.Combine(BrowserSettings.HistoryDir, "hallow-history.json"); } }   // 1.6: another profile keeps its own
        public bool Loaded { get { lock (gate) return loaded; } }
        public int Count { get { lock (gate) return items.Count; } }

        // reads the file; pages added before it was read stay in front (they're newer)
        public void Load()
        {
            var f = BrowserSettings.Read<HistoryFile>(path);
            lock (gate)
            {
                if (loaded) return; loaded = true;
                if (f == null || f.Items == null) return;
                var have = new HashSet<string>(items.Select(x => x.Url), StringComparer.Ordinal);
                foreach (var e in f.Items) if (e != null && Omni.IsWeb(e.Url) && have.Add(e.Url)) items.Add(e);
                if (items.Count > Max) items.RemoveRange(Max, items.Count - Max);
            }
        }
        public void Add(string url, string title, DateTime when)
        {
            if (!Omni.IsWeb(url)) return;
            lock (gate)
            {
                var i = items.FindIndex(x => x.Url == url);
                HistoryEntry e;
                if (i >= 0) { e = items[i]; items.RemoveAt(i); e.Visits++; } else e = new HistoryEntry { Url = url, Visits = 1 };
                if (!string.IsNullOrWhiteSpace(title)) e.Title = title.Length > 300 ? title.Substring(0, 300) : title;
                e.Last = when.ToString("s");
                items.Insert(0, e);
                if (items.Count > Max) items.RemoveRange(Max, items.Count - Max);
                dirty = true;
            }
        }
        public void SetTitle(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            lock (gate) { var e = items.FirstOrDefault(x => x.Url == url); if (e != null && e.Title != title) { e.Title = title.Length > 300 ? title.Substring(0, 300) : title; dirty = true; } }
        }
        public List<HistoryEntry> All() { lock (gate) return items.ToList(); }
        public List<KeyValuePair<HistoryEntry, int>> Search(string q, int max)
        {
            List<HistoryEntry> copy; lock (gate) copy = items.ToList();
            var res = new List<KeyValuePair<HistoryEntry, int>>();
            foreach (var e in copy)
            {
                int s = Math.Max(Fuzzy.Score(q, e.Title), Fuzzy.Score(q, Omni.Display(e.Url)) - 10);
                if (s < 0) continue;
                res.Add(new KeyValuePair<HistoryEntry, int>(e, s + Math.Min(e.Visits, 10) * 3));
            }
            return res.OrderByDescending(x => x.Value).Take(max).ToList();
        }
        // 1.3: pages imported from another browser - new addresses are added, known ones keep the most visits and the newest
        // visit; newest first again, at most Max. Returns how many pages are new.
        public int Merge(IEnumerable<HistoryEntry> pages)
        {
            if (!Loaded) Load();
            int added = 0;
            lock (gate)
            {
                var by = new Dictionary<string, HistoryEntry>(StringComparer.Ordinal);
                foreach (var e in items) if (!by.ContainsKey(e.Url)) by[e.Url] = e;
                foreach (var p in pages.Where(x => x != null && Omni.IsWeb(x.Url)).OrderByDescending(x => x.Last ?? "", StringComparer.Ordinal).Take(Max))
                {
                    HistoryEntry e;
                    if (by.TryGetValue(p.Url, out e))
                    {
                        e.Visits = Math.Max(e.Visits, p.Visits);
                        if (string.CompareOrdinal(p.Last ?? "", e.Last ?? "") > 0) e.Last = p.Last;
                        if (string.IsNullOrWhiteSpace(e.Title) && !string.IsNullOrWhiteSpace(p.Title)) e.Title = p.Title;
                        continue;
                    }
                    e = new HistoryEntry { Url = p.Url, Title = p.Title != null && p.Title.Length > 300 ? p.Title.Substring(0, 300) : p.Title, Last = p.Last ?? DateTime.Now.ToString("s"), Visits = Math.Max(1, p.Visits) };
                    by[e.Url] = e; items.Add(e); added++;
                }
                items = items.OrderByDescending(x => x.Last ?? "", StringComparer.Ordinal).ToList();
                if (items.Count > Max) items.RemoveRange(Max, items.Count - Max);
                dirty = true; loaded = true;
            }
            Save();
            return added;
        }
        public void Clear()
        {
            lock (gate) { items.Clear(); dirty = false; loaded = true; }
            try { File.Delete(path); } catch { }
        }
        public void Save()
        {
            HistoryFile f;
            lock (gate) { if (!dirty || !loaded) return; dirty = false; f = new HistoryFile { Items = items.ToList() }; }
            BrowserSettings.WriteFile(path, f, typeof(HistoryFile));
        }
    }

    // bookmarks (Ctrl+D): 1.3 moved them to Bookmarks13.cs (folders, the bar, sync)

    // ---------------------------------------------------------------- workspaces
    public static class WorkspaceRules
    {
        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 8); }
        // trimmed, at most 40 characters, never empty, never the same as another (" 2", " 3"...)
        public static string UniqueName(string wanted, IEnumerable<string> existing)
        {
            var b = (wanted ?? "").Trim(); if (b.Length > 40) b = b.Substring(0, 40).Trim(); if (b.Length == 0) b = "Workspace";
            var have = new HashSet<string>(existing.Where(x => x != null), StringComparer.OrdinalIgnoreCase);
            var n = b; for (int i = 2; have.Contains(n); i++) n = b + " " + i;
            return n;
        }
        public static int TabCount(Session s) { return s == null || s.Tabs == null ? 0 : s.Tabs.Count(t => t != null && t.Url != null); }
        // "3,5" -> (3, 5) when both are tabs of the session and different
        public static bool ParseSplit(string split, int count, out int l, out int r)
        {
            l = r = -1; if (string.IsNullOrEmpty(split)) return false;
            var p = split.Split(','); if (p.Length != 2 || !int.TryParse(p[0], out l) || !int.TryParse(p[1], out r)) { l = r = -1; return false; }
            if (l < 0 || r < 0 || l >= count || r >= count || l == r) { l = r = -1; return false; }
            return true;
        }
    }

    // ---------------------------------------------------------------- reader mode: what the extractor (Reader.Extract, in the page) found
    public class ReaderBlock { public string T, X, Src; }   // T: p | h2 | h3 | li | quote | pre | img | cap
    public class ReaderDoc
    {
        public bool Ok; public string Title, Byline, Site; public int Words;
        public List<ReaderBlock> Blocks = new List<ReaderBlock>();
        public int Minutes { get { return Math.Max(1, (int)Math.Round(Words / 230.0)); } }
        // the result of ExecuteScriptAsync (a JSON string holding the extractor's JSON)
        public static ReaderDoc Parse(string scriptResult)
        {
            var d = new ReaderDoc();
            try
            {
                var js = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                var inner = scriptResult != null && scriptResult.StartsWith("\"") ? js.Deserialize<string>(scriptResult) : scriptResult;
                var o = js.Deserialize<Dictionary<string, object>>(inner ?? "");
                if (o == null) return d;
                Func<string, string> S = k => { object v; return o.TryGetValue(k, out v) && v != null ? v.ToString() : null; };
                d.Title = S("title"); d.Byline = S("byline"); d.Site = S("site");
                object w; if (o.TryGetValue("words", out w)) try { d.Words = Convert.ToInt32(w); } catch { }
                object bl; var arr = o.TryGetValue("blocks", out bl) ? bl as System.Collections.ArrayList : null;
                var kinds = new HashSet<string> { "p", "h2", "h3", "li", "quote", "pre", "img", "cap" };
                if (arr != null)
                    foreach (var item in arr)
                    {
                        var b = item as Dictionary<string, object>; if (b == null) continue;
                        object t, x, src; b.TryGetValue("t", out t); b.TryGetValue("x", out x); b.TryGetValue("src", out src);
                        var kind = t as string; if (kind == null || !kinds.Contains(kind)) continue;
                        d.Blocks.Add(new ReaderBlock { T = kind, X = (x as string) ?? "", Src = src as string });
                    }
                object ok; d.Ok = o.TryGetValue("ok", out ok) && ok is bool && (bool)ok && d.Blocks.Count > 0;
            }
            catch { d.Ok = false; }
            return d;
        }
    }
}
