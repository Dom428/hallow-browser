// Hallow Browser 1.3 - moving in: bookmarks and history from Chrome, Edge, Brave, Opera, Opera GX, Vivaldi and Firefox on this PC.
// Read-only: the files are COPIED to a temp folder first (browsers keep them open, and Hallow Browser never writes to another
// browser's profile), read there, and the copies are deleted. Never passwords, never cookies - only bookmarks and history.
// (Passwords belong in Hallow Keys, which has its own importer.)
//   Chromium browsers: "Bookmarks" (JSON) and "History" (SQLite: the urls table, read with SqliteReader)
//   Firefox: bookmarks from the newest bookmarkbackups\*.jsonlz4 (Mozilla's LZ4 block format, decompressed here) and history
//   from places.sqlite (moz_places); or a bookmarks HTML file you exported
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowBrowser
{
    public static class Lz4
    {
        // one LZ4 block (no frame), into at most max bytes
        public static byte[] DecodeBlock(byte[] src, int start, int length, int max)
        {
            var dst = new byte[max]; int ip = start, end = start + length, op = 0;
            while (ip < end)
            {
                int token = src[ip++];
                int lit = token >> 4;
                if (lit == 15) { int b; do { if (ip >= end) throw new InvalidDataException("lz4: cut short"); b = src[ip++]; lit += b; } while (b == 255); }
                if (ip + lit > end || op + lit > max) throw new InvalidDataException("lz4: literals out of range");
                Buffer.BlockCopy(src, ip, dst, op, lit); ip += lit; op += lit;
                if (ip >= end) break;   // the last sequence has literals only
                if (ip + 2 > end) throw new InvalidDataException("lz4: cut short");
                int off = src[ip] | (src[ip + 1] << 8); ip += 2;
                if (off == 0 || off > op) throw new InvalidDataException("lz4: bad offset");
                int len = token & 15;
                if (len == 15) { int b; do { if (ip >= end) throw new InvalidDataException("lz4: cut short"); b = src[ip++]; len += b; } while (b == 255); }
                len += 4;
                if (op + len > max) throw new InvalidDataException("lz4: output too big");
                int from = op - off;
                for (int i = 0; i < len; i++) dst[op++] = dst[from + i];   // may overlap: byte by byte
            }
            if (op == max) return dst;
            var r = new byte[op]; Buffer.BlockCopy(dst, 0, r, 0, op); return r;
        }
        // Mozilla's ".jsonlz4": "mozLz40\0", the size (uint32 little-endian), one LZ4 block
        public static byte[] DecodeMozLz4(byte[] file)
        {
            if (file == null || file.Length < 12 || Encoding.ASCII.GetString(file, 0, 8) != "mozLz40\0") throw new InvalidDataException("not a mozLz4 file");
            uint size = BitConverter.ToUInt32(file, 8);
            if (size > 256u * 1024 * 1024) throw new InvalidDataException("mozLz4: too big");
            return DecodeBlock(file, 12, file.Length - 12, (int)size);
        }
    }

    public class FoundProfile
    {
        public string Browser, Profile, Dir, Kind;   // Kind: chromium | firefox
        public int Bookmarks = -1, History = -1;      // -1 = not counted (yet)
        public string Note;                           // e.g. "history is locked - close Chrome"
        public string Label { get { return Profile == null ? Browser : Browser + " (" + Profile + ")"; } }
        public string FolderName { get { return "Imported from " + Label; } }
    }

    public class ImportResult { public int Bookmarks, History, SkippedBookmarks; public List<string> Notes = new List<string>(); }

    public static class Importer
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 * 1024, RecursionLimit = 512 };
        public static readonly string[] Browsers = { "Chrome", "Edge", "Brave", "Opera", "Opera GX", "Vivaldi", "Firefox" };

        // ---- finding them (names and counts only - nothing is read beyond what the picker shows)
        // local / roaming: %LOCALAPPDATA% and %APPDATA% (tests pass fake ones)
        public static List<FoundProfile> Find(string local, string roaming)
        {
            var res = new List<FoundProfile>();
            Action<string, string, bool> chromium = (name, root, opera) =>
            {
                try
                {
                    if (!Directory.Exists(root)) return;
                    var names = ProfileNames(Path.Combine(root, "Local State"));
                    var dirs = new List<string>();
                    if (opera && IsProfile(root)) dirs.Add(root);   // Opera keeps its main profile in the root folder
                    foreach (var d in Directory.GetDirectories(root))
                    {
                        var n = Path.GetFileName(d);
                        if ((n == "Default" || n.StartsWith("Profile ", StringComparison.Ordinal)) && IsProfile(d)) dirs.Add(d);
                    }
                    var side = Path.Combine(root, "_side_profiles");
                    if (opera && Directory.Exists(side)) foreach (var d in Directory.GetDirectories(side)) if (IsProfile(d)) dirs.Add(d);
                    foreach (var d in dirs)
                    {
                        string pn; var key = d == root ? "" : Path.GetFileName(d);
                        if (!names.TryGetValue(key, out pn)) pn = key == "" || key == "Default" ? null : key;
                        if ((dirs.Count == 1 || key == "Default" || key == "") && (pn == "Person 1" || pn == "Default" || pn == "Profile 1")) pn = null;   // the browser's own name for its first profile
                        res.Add(new FoundProfile { Browser = name, Profile = pn, Dir = d, Kind = "chromium" });
                    }
                }
                catch { }
            };
            chromium("Chrome", Path.Combine(local, "Google", "Chrome", "User Data"), false);
            chromium("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"), false);
            chromium("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), false);
            chromium("Opera", Path.Combine(roaming, "Opera Software", "Opera Stable"), true);
            chromium("Opera GX", Path.Combine(roaming, "Opera Software", "Opera GX Stable"), true);
            chromium("Vivaldi", Path.Combine(local, "Vivaldi", "User Data"), false);
            try
            {
                var ff = Path.Combine(roaming, "Mozilla", "Firefox");
                var names = FirefoxNames(Path.Combine(ff, "profiles.ini"));
                var pd = Path.Combine(ff, "Profiles");
                if (Directory.Exists(pd))
                {
                    var dirs = Directory.GetDirectories(pd).Where(d => File.Exists(Path.Combine(d, "places.sqlite")) || Directory.Exists(Path.Combine(d, "bookmarkbackups"))).ToList();
                    foreach (var d in dirs)
                    {
                        string pn; if (!names.TryGetValue(Path.GetFileName(d), out pn)) { var n = Path.GetFileName(d); var dot = n.IndexOf('.'); pn = dot >= 0 ? n.Substring(dot + 1) : n; }
                        if (dirs.Count == 1) pn = null;
                        res.Add(new FoundProfile { Browser = "Firefox", Profile = pn, Dir = d, Kind = "firefox" });
                    }
                }
            }
            catch { }
            return res;
        }
        public static List<FoundProfile> FindOnThisPC()
        {
            return Find(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }
        static bool IsProfile(string d) { return File.Exists(Path.Combine(d, "Bookmarks")) || File.Exists(Path.Combine(d, "History")); }
        static Dictionary<string, string> ProfileNames(string localState)
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(localState)) return d;
                var o = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(ReadShared(localState)));
                var cache = Dig(o, "profile", "info_cache") as Dictionary<string, object>;
                if (cache != null) foreach (var kv in cache) { var n = Dig(kv.Value, "name") as string; if (!string.IsNullOrWhiteSpace(n)) d[kv.Key] = n; }
            }
            catch { }
            return d;
        }
        static Dictionary<string, string> FirefoxNames(string ini)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(ini)) return d;
                string name = null, path = null;
                Action flush = () => { if (name != null && path != null) d[Path.GetFileName(path.Replace('/', '\\').TrimEnd('\\'))] = name; name = path = null; };
                foreach (var raw in File.ReadAllLines(ini))
                {
                    var l = raw.Trim();
                    if (l.StartsWith("[")) flush();
                    else if (l.StartsWith("Name=")) name = l.Substring(5);
                    else if (l.StartsWith("Path=")) path = l.Substring(5);
                }
                flush();
            }
            catch { }
            return d;
        }
        static object Dig(object o, params string[] keys)
        {
            foreach (var k in keys) { var d = o as Dictionary<string, object>; object v; if (d == null || !d.TryGetValue(k, out v)) return null; o = v; }
            return o;
        }

        // reads a file another program has open (sharing read/write/delete) - never with write access
        public static byte[] ReadShared(string path)
        {
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream()) { f.CopyTo(ms); return ms.ToArray(); }
        }
        // a private temp folder for the copies, deleted afterwards
        public static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "HallowBrowserImport-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d); return d;
        }
        static string CopyIn(string src, string tmp)
        {
            var dst = Path.Combine(tmp, Path.GetFileName(src));
            File.WriteAllBytes(dst, ReadShared(src));
            var wal = src + "-wal"; if (File.Exists(wal)) try { File.WriteAllBytes(dst + "-wal", ReadShared(wal)); } catch { }
            return dst;
        }

        // ---- bookmarks
        public static List<ImportNode> ChromiumBookmarks(string json)
        {
            var res = new List<ImportNode>();
            var o = js.Deserialize<Dictionary<string, object>>(json);
            var roots = Dig(o, "roots") as Dictionary<string, object>; if (roots == null) return res;
            foreach (var r in new[] { "bookmark_bar", "other", "synced" })
            {
                object v; if (!roots.TryGetValue(r, out v)) continue;
                var n = ChromiumNode(v); if (n == null || n.Count == 0) continue;
                n.Title = r == "bookmark_bar" ? "Bookmarks bar" : r == "other" ? "Other bookmarks" : "Mobile bookmarks";
                n.Toolbar = r == "bookmark_bar";
                res.Add(n);
            }
            return res;
        }
        static ImportNode ChromiumNode(object v)
        {
            var d = v as Dictionary<string, object>; if (d == null) return null;
            var type = Dig(d, "type") as string; var name = Dig(d, "name") as string;
            var added = ChromeTime(Dig(d, "date_added"));
            if (type == "url") return new ImportNode { Title = name, Url = Dig(d, "url") as string, Added = added };
            if (type != "folder") return null;
            var n = new ImportNode { Folder = true, Title = name, Added = added };
            var kids = Dig(d, "children") as System.Collections.ArrayList;
            if (kids != null) foreach (var k in kids) { var c = ChromiumNode(k); if (c != null) n.Children.Add(c); }
            return n;
        }
        // Chromium times: microseconds since 1601-01-01 UTC (a string in Bookmarks, a number in History)
        public static string ChromeTime(object v)
        {
            long us; if (v == null || !long.TryParse(v.ToString(), out us) || us <= 0) return null;
            try { return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(us * 10).ToLocalTime().ToString("s"); } catch { return null; }
        }
        static string UnixMicros(object v)
        {
            long us; if (v == null || !long.TryParse(v.ToString(), out us) || us <= 0) return null;
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(us * 10).ToLocalTime().ToString("s"); } catch { return null; }
        }
        public static List<ImportNode> FirefoxBookmarks(string json)
        {
            var res = new List<ImportNode>();
            var root = js.Deserialize<Dictionary<string, object>>(json);
            var kids = Dig(root, "children") as System.Collections.ArrayList; if (kids == null) return res;
            foreach (var k in kids)
            {
                var n = FirefoxNode(k); if (n == null || n.Count == 0) continue;
                var guid = Dig(k, "guid") as string;
                n.Title = guid == "toolbar_____" ? "Bookmarks Toolbar" : guid == "menu________" ? "Bookmarks Menu" : guid == "unfiled_____" ? "Other Bookmarks" : guid == "mobile______" ? "Mobile Bookmarks" : n.Title;
                n.Toolbar = guid == "toolbar_____";
                res.Add(n);
            }
            return res;
        }
        static ImportNode FirefoxNode(object v)
        {
            var d = v as Dictionary<string, object>; if (d == null) return null;
            var type = Dig(d, "type") as string; var title = Dig(d, "title") as string;
            object da; d.TryGetValue("dateAdded", out da); var added = UnixMicros(da);
            if (type == "text/x-moz-place") { var u = Dig(d, "uri") as string; return u == null || u.StartsWith("place:") ? null : new ImportNode { Title = title, Url = u, Added = added }; }
            if (type != "text/x-moz-place-container") return null;
            var n = new ImportNode { Folder = true, Title = title, Added = added };
            var kids = Dig(d, "children") as System.Collections.ArrayList;
            if (kids != null) foreach (var k in kids) { var c = FirefoxNode(k); if (c != null) n.Children.Add(c); }
            return n;
        }
        public static string NewestBackup(string profileDir)
        {
            var d = Path.Combine(profileDir, "bookmarkbackups"); if (!Directory.Exists(d)) return null;
            return Directory.GetFiles(d, "*.jsonlz4").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ThenByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        // the profile's bookmarks as a tree (null when it has none we can read)
        public static List<ImportNode> Bookmarks(FoundProfile p, string tmp)
        {
            if (p.Kind == "chromium")
            {
                var f = Path.Combine(p.Dir, "Bookmarks"); if (!File.Exists(f)) return null;
                return ChromiumBookmarks(Encoding.UTF8.GetString(File.ReadAllBytes(CopyIn(f, tmp))));
            }
            var b = NewestBackup(p.Dir); if (b == null) return null;
            return FirefoxBookmarks(Encoding.UTF8.GetString(Lz4.DecodeMozLz4(File.ReadAllBytes(CopyIn(b, tmp)))));
        }

        // ---- history
        public static List<HistoryEntry> ChromiumHistory(SqliteReader r)
        {
            var res = new List<HistoryEntry>();
            foreach (var row in r.Read("urls"))
            {
                var u = row.ContainsKey("url") ? row["url"] as string : null;
                if (u == null || !Omni.IsWeb(u)) continue;
                if (row.ContainsKey("hidden") && row["hidden"] is long && (long)row["hidden"] != 0) continue;
                res.Add(new HistoryEntry { Url = u, Title = row.ContainsKey("title") ? row["title"] as string : null, Visits = Visits(row, "visit_count"), Last = ChromeTime(row.ContainsKey("last_visit_time") ? row["last_visit_time"] : null) });
            }
            return res;
        }
        public static List<HistoryEntry> FirefoxHistory(SqliteReader r)
        {
            var res = new List<HistoryEntry>();
            foreach (var row in r.Read("moz_places"))
            {
                var u = row.ContainsKey("url") ? row["url"] as string : null;
                if (u == null || !Omni.IsWeb(u)) continue;
                if (row.ContainsKey("hidden") && row["hidden"] is long && (long)row["hidden"] != 0) continue;
                var last = UnixMicros(row.ContainsKey("last_visit_date") ? row["last_visit_date"] : null);
                if (last == null) continue;   // bookmarked but never visited
                res.Add(new HistoryEntry { Url = u, Title = row.ContainsKey("title") ? row["title"] as string : null, Visits = Visits(row, "visit_count"), Last = last });
            }
            return res;
        }
        static int Visits(Dictionary<string, object> row, string col) { object v; return row.TryGetValue(col, out v) && v is long ? (int)Math.Max(1, Math.Min(int.MaxValue, (long)v)) : 1; }
        public static List<HistoryEntry> History(FoundProfile p, string tmp)
        {
            var f = Path.Combine(p.Dir, p.Kind == "chromium" ? "History" : "places.sqlite"); if (!File.Exists(f)) return null;
            var r = SqliteReader.Open(CopyIn(f, tmp));
            return p.Kind == "chromium" ? ChromiumHistory(r) : FirefoxHistory(r);
        }

        // the picker's counts (bookmarks, pages in the history); errors become a note
        public static void Count(FoundProfile p)
        {
            var tmp = TempDir();
            try
            {
                try { var b = Bookmarks(p, tmp); p.Bookmarks = b == null ? 0 : b.Sum(n => n.Count); } catch (IOException) { p.Bookmarks = -1; p.Note = "bookmarks are in use - close " + p.Browser + " and try again"; } catch { p.Bookmarks = -1; p.Note = "the bookmarks file couldn't be read"; }
                try { var h = History(p, tmp); p.History = h == null ? 0 : h.Count; } catch (IOException) { p.History = -1; p.Note = "history is in use - close " + p.Browser + " and try again"; } catch { p.History = -1; p.Note = (p.Note == null ? "" : p.Note + "; ") + "the history file couldn't be read"; }
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // the import itself: bookmarks into "Imported from <browser>" under Other bookmarks (no duplicates), history merged
        public static ImportResult Run(FoundProfile p, bool bookmarks, bool history, BookmarkStore store, HistoryStore hist)
        {
            var res = new ImportResult(); var tmp = TempDir();
            try
            {
                if (bookmarks)
                {
                    try
                    {
                        var tree = Bookmarks(p, tmp);
                        if (tree != null && tree.Count > 0)
                        {
                            var total = tree.Sum(n => n.Count);
                            var folder = store.FolderNamed(BookmarkStore.OtherId, p.FolderName, true);
                            res.Bookmarks = store.ImportTree(folder, tree);
                            res.SkippedBookmarks = total - res.Bookmarks;
                            if (res.Bookmarks == 0 && store.Children(folder).Count == 0) store.Delete(folder);
                        }
                        else res.Notes.Add(p.Label + " has no bookmarks to import.");
                    }
                    catch (IOException) { res.Notes.Add(p.Browser + " is using its bookmarks file - close it and try again."); }
                    catch (Exception e) { res.Notes.Add("Bookmarks couldn't be read: " + e.Message); }
                }
                if (history)
                {
                    try
                    {
                        var h = History(p, tmp);
                        if (h != null) res.History = hist.Merge(h); else res.Notes.Add(p.Label + " has no history file.");
                    }
                    catch (IOException) { res.Notes.Add(p.Browser + " is using its history file - close it and try again."); }
                    catch (Exception e) { res.Notes.Add("History couldn't be read: " + e.Message); }
                }
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
            return res;
        }
    }
}
