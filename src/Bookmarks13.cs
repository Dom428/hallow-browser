// Hallow Browser 1.3 - bookmarks with folders: the bookmarks bar and "Other bookmarks", folders, rename, reorder, and the
// Netscape bookmark file (the HTML every browser exports and imports). %APPDATA%\HallowBrowser\bookmarks.json.
// Stored flat (each bookmark or folder names its parent and has a position), so sync can merge them one by one.
// Nothing here is ever sent anywhere.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace HallowBrowser
{
    // a bookmark or a folder. 1.2's file had Url / Title / Added only: those still load (into the bookmarks bar).
    [DataContract]
    public class Bookmark
    {
        [DataMember] public string Url; [DataMember] public string Title; [DataMember] public string Added;
        [DataMember] public string Id; [DataMember] public string Parent; [DataMember] public double Pos; [DataMember] public bool Folder;
        public Bookmark Clone() { return new Bookmark { Url = Url, Title = Title, Added = Added, Id = Id, Parent = Parent, Pos = Pos, Folder = Folder }; }
    }
    [DataContract] class BookmarkFile { [DataMember] public int Version = 2; [DataMember] public List<Bookmark> Items = new List<Bookmark>(); }

    public class BookmarkStore
    {
        public const string BarId = "bar", OtherId = "other";
        public static bool IsRoot(string id) { return id == BarId || id == OtherId; }
        public static string RootName(string id) { return id == BarId ? "Bookmarks bar" : "Other bookmarks"; }

        readonly string path; readonly object gate = new object(); List<Bookmark> items = new List<Bookmark>(); bool loaded;
        public event Action Changed;                 // after every change (the bar redraws, sync is told)
        public bool Frozen;                          // tests / --snap: never written
        public BookmarkStore(string file) { path = file; }
        public static string DefaultPath { get { return Path.Combine(BrowserSettings.Dir, "bookmarks.json"); } }
        public bool Loaded { get { lock (gate) return loaded; } }
        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        public void Load()
        {
            var f = BrowserSettings.Read<BookmarkFile>(path);
            lock (gate)
            {
                if (loaded) return; loaded = true;
                if (f == null || f.Items == null) return;
                var ids = new HashSet<string>(items.Select(x => x.Id)); double pos = 0;
                foreach (var b in f.Items)
                {
                    if (b == null) continue;
                    if (string.IsNullOrEmpty(b.Id)) { b.Id = NewId(); b.Parent = BarId; b.Pos = pos; }   // 1.2: a flat list, newest first -> the bar in that order
                    pos += 1;
                    if (b.Folder) b.Url = null; else if (!Omni.IsWeb(b.Url)) continue;
                    if (string.IsNullOrEmpty(b.Parent)) b.Parent = OtherId;
                    if (!ids.Add(b.Id)) continue;
                    items.Add(b);
                }
            }
        }
        void Touch(bool save) { if (save) Save(); var c = Changed; if (c != null) try { c(); } catch { } }
        public void Save()
        {
            if (Frozen) return;
            BookmarkFile f; lock (gate) f = new BookmarkFile { Items = items.Select(x => x.Clone()).ToList() };
            BrowserSettings.WriteFile(path, f, typeof(BookmarkFile));
        }

        // ---- reading
        public int Count { get { lock (gate) return items.Count(x => !x.Folder); } }
        Bookmark Get(string id) { return items.FirstOrDefault(x => x.Id == id); }
        public Bookmark Find(string id) { lock (gate) { var b = Get(id); return b == null ? null : b.Clone(); } }
        bool Exists(string id) { return IsRoot(id) || items.Any(x => x.Id == id && x.Folder); }
        // a folder's children in order; "Other bookmarks" also shows anything whose folder is gone (a delete on another PC)
        List<Bookmark> Kids(string parent)
        {
            var parents = new HashSet<string>(items.Where(x => x.Folder).Select(x => x.Id));
            return items.Where(x => x.Parent == parent || (parent == OtherId && !IsRoot(x.Parent) && !parents.Contains(x.Parent)))
                .OrderBy(x => x.Pos).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        }
        public List<Bookmark> Children(string parent) { lock (gate) return Kids(parent).Select(x => x.Clone()).ToList(); }
        // every bookmark (not folders) in tree order: the bar first, then Other bookmarks
        public List<Bookmark> All()
        {
            lock (gate)
            {
                var res = new List<Bookmark>(); var seen = new HashSet<string>();
                Action<string> walk = null;
                walk = p => { foreach (var x in Kids(p)) { if (!seen.Add(x.Id)) continue; if (x.Folder) walk(x.Id); else res.Add(x.Clone()); } };
                walk(BarId); walk(OtherId);
                return res;
            }
        }
        public List<Bookmark> Everything() { lock (gate) return items.Select(x => x.Clone()).ToList(); }
        public bool Has(string url) { if (url == null) return false; lock (gate) return items.Any(x => !x.Folder && x.Url == url); }
        // the folder's path, e.g. "Bookmarks bar / Games"
        public string PathOf(string id)
        {
            lock (gate)
            {
                var parts = new List<string>(); var cur = id; int guard = 0;
                while (cur != null && guard++ < 64)
                {
                    if (IsRoot(cur)) { parts.Insert(0, RootName(cur)); break; }
                    var b = Get(cur); if (b == null) { parts.Insert(0, RootName(OtherId)); break; }
                    parts.Insert(0, b.Title ?? "Folder"); cur = b.Parent;
                }
                return string.Join(" / ", parts);
            }
        }
        bool IsInside(string id, string folder)   // is id the folder or somewhere under it?
        {
            var cur = folder; int guard = 0;
            while (cur != null && !IsRoot(cur) && guard++ < 256) { if (cur == id) return true; var b = Get(cur); cur = b == null ? null : b.Parent; }
            return false;
        }

        // ---- changing
        double PosAt(string parent, int index, string except)
        {
            var k = Kids(parent).Where(x => x.Id != except).ToList();
            if (index < 0 || index > k.Count) index = k.Count;
            if (k.Count == 0) return 0;
            if (index == 0) return k[0].Pos - 1;
            if (index == k.Count) return k[k.Count - 1].Pos + 1;
            var a = k[index - 1].Pos; var b = k[index].Pos;
            if (b - a > 1e-9) return (a + b) / 2;
            // positions too close together: number the folder again (rare)
            for (int i = 0; i < k.Count; i++) k[i].Pos = i * 2;
            return index * 2 - 1;
        }
        static string Now() { return DateTime.Now.ToString("s"); }
        public Bookmark Add(string parent, string title, string url, int index = -1)
        {
            if (!Omni.IsWeb(url)) return null;
            Bookmark b;
            lock (gate)
            {
                if (!Exists(parent)) parent = OtherId;
                b = new Bookmark { Id = NewId(), Parent = parent, Url = url, Title = Clean(title, Omni.HostOf(url)), Added = Now() };
                b.Pos = PosAt(parent, index, null); items.Add(b);
            }
            Touch(true); return b.Clone();
        }
        public Bookmark AddFolder(string parent, string name, int index = -1)
        {
            Bookmark b;
            lock (gate)
            {
                if (!Exists(parent)) parent = OtherId;
                b = new Bookmark { Id = NewId(), Parent = parent, Folder = true, Title = Clean(name, "New folder"), Added = Now() };
                b.Pos = PosAt(parent, index, null); items.Add(b);
            }
            Touch(true); return b.Clone();
        }
        static string Clean(string s, string fallback) { s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim(); if (s.Length > 300) s = s.Substring(0, 300); return s.Length == 0 ? (fallback ?? "Untitled") : s; }
        public bool Rename(string id, string title)
        {
            lock (gate) { var b = Get(id); if (b == null) return false; var t = Clean(title, b.Folder ? "Folder" : Omni.HostOf(b.Url)); if (t == b.Title) return true; b.Title = t; }
            Touch(true); return true;
        }
        public bool SetUrl(string id, string url)
        {
            if (!Omni.IsWeb(url)) return false;
            lock (gate) { var b = Get(id); if (b == null || b.Folder) return false; b.Url = url; }
            Touch(true); return true;
        }
        // moves a bookmark or folder to position index of a folder (a folder never into itself)
        public bool Move(string id, string parent, int index)
        {
            lock (gate)
            {
                var b = Get(id); if (b == null || !Exists(parent)) return false;
                if (b.Folder && IsInside(b.Id, parent)) return false;
                b.Pos = PosAt(parent, index, b.Id); b.Parent = parent;
            }
            Touch(true); return true;
        }
        // a bookmark, or a folder with everything in it
        public int Delete(string id)
        {
            int n;
            lock (gate)
            {
                var gone = new HashSet<string> { id }; bool more = true;
                while (more) { more = false; foreach (var x in items) if (!gone.Contains(x.Id) && gone.Contains(x.Parent)) { gone.Add(x.Id); more = true; } }
                n = items.RemoveAll(x => gone.Contains(x.Id));
            }
            if (n > 0) Touch(true);
            return n;
        }
        // Ctrl+D: adds to the end of the bookmarks bar, or removes every bookmark of the page; true = bookmarked now
        public bool Toggle(string url, string title)
        {
            if (!Omni.IsWeb(url)) return false;
            bool had; lock (gate) had = items.RemoveAll(x => !x.Folder && x.Url == url) > 0;
            if (had) { Touch(true); return false; }
            Add(BarId, title, url); return true;
        }
        public void Remove(string url) { int n; lock (gate) n = items.RemoveAll(x => !x.Folder && x.Url == url); if (n > 0) Touch(true); }
        // sync: the whole set as merged (ids, parents and positions as they came)
        public void Replace(IEnumerable<Bookmark> all)
        {
            lock (gate) { items = all.Where(b => b != null && !string.IsNullOrEmpty(b.Id) && (b.Folder || Omni.IsWeb(b.Url))).GroupBy(b => b.Id).Select(g => g.First().Clone()).ToList(); loaded = true; }
            Touch(true);
        }
        // a folder by name under a parent, made if it isn't there (imports reuse their folder)
        public string FolderNamed(string parent, string name, bool create)
        {
            lock (gate)
            {
                var f = Kids(parent).FirstOrDefault(x => x.Folder && string.Equals(x.Title, name, StringComparison.OrdinalIgnoreCase));
                if (f != null || !create) return f == null ? null : f.Id;
            }
            return AddFolder(parent, name).Id;
        }

        // ---- importing a tree (other browsers, bookmark files): into a folder, never a bookmark twice
        // (a page already bookmarked anywhere is skipped; folders that would stay empty aren't made)
        public int ImportTree(string parent, IList<ImportNode> nodes)
        {
            int added = 0;
            lock (gate)
            {
                var have = new HashSet<string>(items.Where(x => !x.Folder).Select(x => x.Url), StringComparer.Ordinal);
                Action<string, IList<ImportNode>> walk = null;
                walk = (into, list) =>
                {
                    foreach (var n in list)
                    {
                        if (n == null) continue;
                        if (n.Folder)
                        {
                            if (!HasNew(n, have)) continue;
                            var ex = Kids(into).FirstOrDefault(x => x.Folder && string.Equals(x.Title, Clean(n.Title, "Folder"), StringComparison.OrdinalIgnoreCase));
                            string fid;
                            if (ex != null) fid = ex.Id;
                            else { var f = new Bookmark { Id = NewId(), Parent = into, Folder = true, Title = Clean(n.Title, "Folder"), Added = n.Added ?? Now() }; f.Pos = PosAt(into, -1, null); items.Add(f); fid = f.Id; }
                            walk(fid, n.Children);
                        }
                        else
                        {
                            var u = NormalUrl(n.Url); if (u == null || !have.Add(u)) continue;
                            var b = new Bookmark { Id = NewId(), Parent = into, Url = u, Title = Clean(n.Title, Omni.HostOf(u)), Added = n.Added ?? Now() };
                            b.Pos = PosAt(into, -1, null); items.Add(b); added++;
                        }
                    }
                };
                if (!Exists(parent)) parent = OtherId;
                walk(parent, nodes);
            }
            if (added > 0) Touch(true);
            return added;
        }
        static bool HasNew(ImportNode n, HashSet<string> have)
        {
            if (!n.Folder) { var u = NormalUrl(n.Url); return u != null && !have.Contains(u); }
            return n.Children.Any(c => HasNew(c, have));
        }
        // http/https only, as the address bar would show it (other browsers sometimes keep "http://x.com" without the slash)
        public static string NormalUrl(string u) { Uri x; if (u == null || !Uri.TryCreate(u.Trim(), UriKind.Absolute, out x) || (x.Scheme != "http" && x.Scheme != "https")) return null; return x.AbsoluteUri; }

        // ---- the Netscape bookmark file (what Chrome, Edge, Firefox... export and import)
        public string ExportHtml()
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<!-- This is an automatically generated file.\n     It will be read and overwritten.\n     DO NOT EDIT! -->\n");
            sb.Append("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE>\n<H1>Bookmarks</H1>\n<DL><p>\n");
            lock (gate)
            {
                var now = Unix(DateTime.Now);
                Action<string, int> walk = null;
                walk = (p, depth) =>
                {
                    var ind = new string(' ', depth * 4);
                    foreach (var x in Kids(p))
                    {
                        if (x.Folder)
                        {
                            sb.Append(ind).Append("<DT><H3 ADD_DATE=\"").Append(Unix(x.Added)).Append("\">").Append(WebUtility.HtmlEncode(x.Title ?? "")).Append("</H3>\n");
                            sb.Append(ind).Append("<DL><p>\n"); walk(x.Id, depth + 1); sb.Append(ind).Append("</DL><p>\n");
                        }
                        else sb.Append(ind).Append("<DT><A HREF=\"").Append(WebUtility.HtmlEncode(x.Url)).Append("\" ADD_DATE=\"").Append(Unix(x.Added)).Append("\">").Append(WebUtility.HtmlEncode(x.Title ?? "")).Append("</A>\n");
                    }
                };
                sb.Append("    <DT><H3 ADD_DATE=\"").Append(now).Append("\" PERSONAL_TOOLBAR_FOLDER=\"true\">Bookmarks bar</H3>\n    <DL><p>\n"); walk(BarId, 2); sb.Append("    </DL><p>\n");
                var other = Kids(OtherId);
                if (other.Count > 0) { sb.Append("    <DT><H3 ADD_DATE=\"").Append(now).Append("\">Other bookmarks</H3>\n    <DL><p>\n"); walk(OtherId, 2); sb.Append("    </DL><p>\n"); }
            }
            sb.Append("</DL><p>\n");
            return sb.ToString();
        }
        static long Unix(string iso) { DateTime d; return iso != null && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? Unix(d) : Unix(DateTime.Now); }
        static long Unix(DateTime d) { return (long)(d.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        // an HTML bookmark file into this store. Into an empty store the toolbar folder becomes the bookmarks bar and
        // "Other bookmarks" goes back to Other bookmarks (a round trip of our own export); otherwise all of it goes into
        // the folder "into" (a new "Imported ..." folder). Returns how many bookmarks were added.
        public int ImportHtml(string html, string intoName)
        {
            var nodes = BookmarkHtml.Parse(html);
            bool empty; lock (gate) empty = items.Count == 0;
            if (empty)
            {
                int n = 0; var rest = new List<ImportNode>();
                foreach (var x in nodes)
                {
                    if (x.Folder && x.Toolbar) n += ImportTree(BarId, x.Children);
                    else if (x.Folder && x.Title == "Other bookmarks") n += ImportTree(OtherId, x.Children);
                    else rest.Add(x);
                }
                if (rest.Count > 0) n += ImportTree(OtherId, rest);
                return n;
            }
            var folder = FolderNamed(OtherId, intoName, true);
            var added = ImportTree(folder, nodes);
            if (added == 0 && Children(folder).Count == 0) Delete(folder);
            return added;
        }
    }

    // a bookmark tree read from somewhere else (another browser, an HTML file)
    public class ImportNode
    {
        public string Title, Url, Added; public bool Folder, Toolbar;
        public List<ImportNode> Children = new List<ImportNode>();
        public int Count { get { return Folder ? Children.Sum(c => c.Count) : 1; } }
        public static ImportNode Dir(string title, IEnumerable<ImportNode> kids) { var n = new ImportNode { Folder = true, Title = title }; n.Children.AddRange(kids); return n; }
    }

    public static class BookmarkHtml
    {
        static readonly Regex tag = new Regex(@"<(/?)(DL|H3|A)\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex attr = new Regex(@"([A-Za-z_]+)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled);
        public static List<ImportNode> Parse(string html)
        {
            var top = new ImportNode { Folder = true }; var stack = new Stack<ImportNode>(); stack.Push(top);
            ImportNode pending = null; bool started = false;
            html = html ?? "";
            var m = tag.Match(html);
            while (m.Success)
            {
                var close = m.Groups[1].Value == "/"; var name = m.Groups[2].Value.ToUpperInvariant(); var at = Attrs(m.Groups[3].Value);
                if (name == "DL")
                {
                    if (!close)
                    {
                        if (!started) { started = true; pending = null; }   // the outer list
                        else if (pending != null) { stack.Push(pending); pending = null; }
                        else stack.Push(stack.Peek());                   // a list without a heading: same folder
                    }
                    else if (stack.Count > 1) stack.Pop();
                    m = m.NextMatch(); continue;
                }
                if (close) { m = m.NextMatch(); continue; }
                var end = html.IndexOf("</" + name, m.Index + m.Length, StringComparison.OrdinalIgnoreCase);
                var text = end < 0 ? "" : Regex.Replace(html.Substring(m.Index + m.Length, end - m.Index - m.Length), "<[^>]*>", "");
                text = WebUtility.HtmlDecode(text).Trim();
                string v;
                var added = at.TryGetValue("ADD_DATE", out v) ? FromUnix(v) : null;
                if (name == "H3")
                {
                    pending = new ImportNode { Folder = true, Title = text, Added = added, Toolbar = at.TryGetValue("PERSONAL_TOOLBAR_FOLDER", out v) && v.Equals("true", StringComparison.OrdinalIgnoreCase) };
                    stack.Peek().Children.Add(pending);
                }
                else if (name == "A" && at.TryGetValue("HREF", out v))
                {
                    pending = null;
                    stack.Peek().Children.Add(new ImportNode { Url = WebUtility.HtmlDecode(v), Title = text, Added = added });
                }
                m = m.NextMatch();
            }
            return top.Children;
        }
        static Dictionary<string, string> Attrs(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in attr.Matches(s ?? "")) d[a.Groups[1].Value] = a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Success ? a.Groups[4].Value : a.Groups[5].Value;
            return d;
        }
        static string FromUnix(string v)
        {
            long n; if (!long.TryParse(v, out n) || n <= 0) return null;
            if (n > 100000000000L) n /= 1000000;   // some browsers write microseconds
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n).ToLocalTime().ToString("s"); } catch { return null; }
        }
    }
}
