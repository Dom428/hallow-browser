// Hallow Browser 1.3 tests - a minimal SQLite file WRITER, only for making real-format test fixtures (a Chromium "History" and a
// Firefox "places.sqlite" with made-up pages) for SqliteReader: database header, sqlite_master, table b-trees with an interior
// root when the rows need more than one leaf page, overflow pages for long values, every integer size, floats, text and blobs.
// (Cross-checked once with a real SQLite: PRAGMA integrity_check says ok.) And a small LZ4 block compressor for the Firefox
// .jsonlz4 fixture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HallowBrowser.Tests
{
    public class TinyTable { public string Name, Sql; public List<KeyValuePair<long, object[]>> Rows = new List<KeyValuePair<long, object[]>>(); }

    public static class TinySqlite
    {
        static void Varint(List<byte> o, long v)
        {
            ulong u = (ulong)v;
            if (u > 0x00ffffffffffffffUL)
            {
                var b9 = new byte[9]; b9[8] = (byte)u; u >>= 8;
                for (int i = 7; i >= 0; i--) { b9[i] = (byte)((u & 0x7f) | 0x80); u >>= 7; }
                o.AddRange(b9); return;
            }
            var tmp = new List<byte>(); tmp.Add((byte)(u & 0x7f)); u >>= 7;
            while (u > 0) { tmp.Add((byte)((u & 0x7f) | 0x80)); u >>= 7; }
            tmp.Reverse(); o.AddRange(tmp);
        }
        static byte[] Record(object[] vals)
        {
            var types = new List<long>(); var body = new List<byte>();
            foreach (var v in vals)
            {
                if (v == null) { types.Add(0); continue; }
                if (v is int || v is long)
                {
                    long n = Convert.ToInt64(v);
                    if (n == 0) { types.Add(8); continue; }
                    if (n == 1) { types.Add(9); continue; }
                    int len; long t;
                    if (n >= sbyte.MinValue && n <= sbyte.MaxValue) { t = 1; len = 1; }
                    else if (n >= short.MinValue && n <= short.MaxValue) { t = 2; len = 2; }
                    else if (n >= -8388608 && n <= 8388607) { t = 3; len = 3; }
                    else if (n >= int.MinValue && n <= int.MaxValue) { t = 4; len = 4; }
                    else if (n >= -140737488355328L && n <= 140737488355327L) { t = 5; len = 6; }
                    else { t = 6; len = 8; }
                    types.Add(t); for (int i = len - 1; i >= 0; i--) body.Add((byte)(n >> (8 * i))); continue;
                }
                if (v is double) { types.Add(7); var b = BitConverter.GetBytes((double)v); Array.Reverse(b); body.AddRange(b); continue; }
                if (v is string) { var b = Encoding.UTF8.GetBytes((string)v); types.Add(13 + 2L * b.Length); body.AddRange(b); continue; }
                if (v is byte[]) { var b = (byte[])v; types.Add(12 + 2L * b.Length); body.AddRange(b); continue; }
                throw new ArgumentException("type");
            }
            var hdr = new List<byte>(); foreach (var t in types) Varint(hdr, t);
            int hl = hdr.Count + 1; if (hl >= 128) hl = hdr.Count + 2;   // the header length counts itself
            var o = new List<byte>(); Varint(o, hl); o.AddRange(hdr); o.AddRange(body); return o.ToArray();
        }

        // tables in order; their root pages are filled in. Returns the file.
        public static byte[] Write(int pageSize, List<TinyTable> tables)
        {
            int usable = pageSize; var pages = new Dictionary<uint, byte[]>(); uint next = 2;
            var roots = new Dictionary<TinyTable, uint>();
            foreach (var t in tables) roots[t] = BuildTree(t.Rows, pageSize, usable, pages, ref next, false);
            // page 1: header + sqlite_master
            var master = tables.Select((t, i) => new KeyValuePair<long, object[]>(i + 1, new object[] { "table", t.Name, t.Name, (long)roots[t], t.Sql })).ToList();
            uint one = 1; BuildTree(master, pageSize, usable, pages, ref one, true);
            uint count = pages.Keys.Max();
            var file = new byte[count * pageSize];
            foreach (var kv in pages) Buffer.BlockCopy(kv.Value, 0, file, (int)((kv.Key - 1) * pageSize), pageSize);
            var h = Encoding.ASCII.GetBytes("SQLite format 3\0"); Buffer.BlockCopy(h, 0, file, 0, 16);
            file[16] = (byte)(pageSize >> 8 & 0xff); file[17] = (byte)(pageSize & 0xff); if (pageSize == 65536) { file[16] = 0; file[17] = 1; }
            file[18] = 1; file[19] = 1; file[20] = 0; file[21] = 64; file[22] = 32; file[23] = 32;
            BE(file, 24, 1); BE(file, 28, count); BE(file, 40, 1); BE(file, 44, 4); BE(file, 56, 1); BE(file, 92, 1); BE(file, 96, 3045000);
            return file;
        }
        static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        static void BE16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }

        // a table b-tree; master = page 1 (header space, must fit in one leaf). Returns the root page.
        static uint BuildTree(List<KeyValuePair<long, object[]>> rows, int pageSize, int usable, Dictionary<uint, byte[]> pages, ref uint next, bool master)
        {
            uint root = master ? 1 : next++;
            var cells = new List<KeyValuePair<long, byte[]>>();
            int X = usable - 35, M = ((usable - 12) * 32 / 255) - 23;
            foreach (var r in rows.OrderBy(x => x.Key))
            {
                var payload = Record(r.Value); int P = payload.Length;
                var c = new List<byte>(); Varint(c, P); Varint(c, r.Key);
                if (P <= X) c.AddRange(payload);
                else
                {
                    int K = M + ((P - M) % (usable - 4)); int local = K <= X ? K : M;
                    c.AddRange(payload.Take(local));
                    int at = local; uint first = next; var chain = new List<uint>();
                    while (at < P) { chain.Add(next++); at += usable - 4; }
                    var ptr = new byte[4]; BE(ptr, 0, first); c.AddRange(ptr);
                    at = local;
                    for (int i = 0; i < chain.Count; i++)
                    {
                        var pg = new byte[pageSize]; BE(pg, 0, i + 1 < chain.Count ? chain[i + 1] : 0);
                        int n = Math.Min(usable - 4, P - at); Buffer.BlockCopy(payload, at, pg, 4, n); at += n; pages[chain[i]] = pg;
                    }
                }
                cells.Add(new KeyValuePair<long, byte[]>(r.Key, c.ToArray()));
            }
            int hdr0 = master ? 100 : 0;
            // pack into leaves
            var leaves = new List<List<KeyValuePair<long, byte[]>>>(); var cur = new List<KeyValuePair<long, byte[]>>(); int used = 0;
            int cap = usable - 8 - (master ? 100 : 0);
            foreach (var c in cells)
            {
                if (cur.Count > 0 && used + c.Value.Length + 2 > cap) { leaves.Add(cur); cur = new List<KeyValuePair<long, byte[]>>(); used = 0; cap = usable - 8; }
                cur.Add(c); used += c.Value.Length + 2;
            }
            leaves.Add(cur);
            if (leaves.Count == 1) { pages[root] = Page(pageSize, hdr0, 0x0D, leaves[0].Select(x => x.Value).ToList(), 0); return root; }
            if (master) throw new InvalidOperationException("schema too big for page 1");
            var leafNos = new List<uint>();
            foreach (var l in leaves) { var n = next++; leafNos.Add(n); pages[n] = Page(pageSize, 0, 0x0D, l.Select(x => x.Value).ToList(), 0); }
            var icells = new List<byte[]>();
            for (int i = 0; i < leaves.Count - 1; i++) { var c = new List<byte>(); var p = new byte[4]; BE(p, 0, leafNos[i]); c.AddRange(p); Varint(c, leaves[i].Last().Key); icells.Add(c.ToArray()); }
            pages[root] = Page(pageSize, 0, 0x05, icells, leafNos.Last());
            return root;
        }
        static byte[] Page(int pageSize, int hdr, byte type, List<byte[]> cells, uint right)
        {
            var p = new byte[pageSize]; int hl = type == 0x05 ? 12 : 8;
            int end = pageSize; var ptrs = new List<int>();
            foreach (var c in cells) { end -= c.Length; Buffer.BlockCopy(c, 0, p, end, c.Length); ptrs.Add(end); }
            if (hdr + hl + ptrs.Count * 2 > end) throw new InvalidOperationException("page overflow");
            p[hdr] = type; BE16(p, hdr + 1, 0); BE16(p, hdr + 3, cells.Count); BE16(p, hdr + 5, end == 65536 ? 0 : end); p[hdr + 7] = 0;
            if (type == 0x05) BE(p, hdr + 8, right);
            for (int i = 0; i < ptrs.Count; i++) BE16(p, hdr + hl + i * 2, ptrs[i]);
            return p;
        }

        // ---- fixtures
        public static long ChromeTime(DateTime utc) { return (utc - new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10; }
        public static long UnixMicros(DateTime utc) { return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10; }
        // Chromium's History: the real urls table (and a visits table, as Chrome has, to make sure the reader finds the right one)
        public static byte[] ChromeHistory(IList<Tuple<string, string, int, DateTime, bool>> pages, int pageSize = 4096, bool extras = true)
        {
            var urls = new TinyTable { Name = "urls", Sql = "CREATE TABLE urls(id INTEGER PRIMARY KEY AUTOINCREMENT,url LONGVARCHAR,title LONGVARCHAR,visit_count INTEGER DEFAULT 0 NOT NULL,typed_count INTEGER DEFAULT 0 NOT NULL,last_visit_time INTEGER NOT NULL,hidden INTEGER DEFAULT 0 NOT NULL)" };
            long id = 1; foreach (var p in pages) urls.Rows.Add(new KeyValuePair<long, object[]>(id++, new object[] { null, p.Item1, p.Item2, (long)p.Item3, 0L, ChromeTime(p.Item4), p.Item5 ? 1L : 0L }));
            var meta = new TinyTable { Name = "meta", Sql = "CREATE TABLE meta(key LONGVARCHAR NOT NULL, value LONGVARCHAR)" };
            meta.Rows.Add(new KeyValuePair<long, object[]>(1, new object[] { "version", "70" }));
            var visits = new TinyTable { Name = "visits", Sql = "CREATE TABLE visits(id INTEGER PRIMARY KEY,url INTEGER NOT NULL,visit_time INTEGER NOT NULL,from_visit INTEGER,transition INTEGER DEFAULT 0 NOT NULL,segment_id INTEGER,visit_duration INTEGER DEFAULT 0 NOT NULL)" };
            visits.Rows.Add(new KeyValuePair<long, object[]>(1, new object[] { null, 1L, ChromeTime(DateTime.UtcNow), 0L, 805306368L, 0L, 1234.5 }));
            return Write(pageSize, extras ? new List<TinyTable> { meta, urls, visits } : new List<TinyTable> { urls });
        }
        public static byte[] FirefoxPlaces(IList<Tuple<string, string, int, DateTime?>> pages, int pageSize = 32768)
        {
            var t = new TinyTable { Name = "moz_places", Sql = "CREATE TABLE moz_places (   id INTEGER PRIMARY KEY, url LONGVARCHAR, title LONGVARCHAR, rev_host LONGVARCHAR, visit_count INTEGER DEFAULT 0, hidden INTEGER DEFAULT 0 NOT NULL, typed INTEGER DEFAULT 0 NOT NULL, frecency INTEGER DEFAULT -1 NOT NULL, last_visit_date INTEGER , guid TEXT, foreign_count INTEGER DEFAULT 0 NOT NULL, url_hash INTEGER DEFAULT 0 NOT NULL , description TEXT, preview_image_url TEXT, site_name TEXT, origin_id INTEGER REFERENCES moz_origins(id), recalc_frecency INTEGER NOT NULL DEFAULT 0, alt_frecency INTEGER, recalc_alt_frecency INTEGER NOT NULL DEFAULT 0)" };
            long id = 1; foreach (var p in pages) t.Rows.Add(new KeyValuePair<long, object[]>(id++, new object[] { null, p.Item1, p.Item2, "moc.elpmaxe.", (long)p.Item3, 0L, 0L, 100L, p.Item4 == null ? null : (object)UnixMicros(p.Item4.Value), "guid" + id, 0L, 123456789012L, null, null, null, 1L, 0L, null, 0L }));
            return Write(pageSize, new List<TinyTable> { t });
        }
    }

    // a simple greedy LZ4 block compressor (hash of 4 bytes, the format's end rules: the last 5 bytes are literals, no match starts
    // in the last 12)
    public static class TinyLz4
    {
        public static byte[] Compress(byte[] src)
        {
            var o = new List<byte>(); var table = new Dictionary<int, int>(); int anchor = 0, i = 0, n = src.Length;
            int limit = n - 12;
            while (i < limit)
            {
                int key = src[i] | src[i + 1] << 8 | src[i + 2] << 16 | src[i + 3] << 24; int cand;
                bool found = table.TryGetValue(key, out cand) && i - cand <= 65535 && i - cand > 0;
                table[key] = i;
                if (!found) { i++; continue; }
                int len = 4; while (i + len < n - 5 && src[cand + len] == src[i + len]) len++;
                Emit(o, src, anchor, i - anchor, i - cand, len);
                i += len; anchor = i;
            }
            Emit(o, src, anchor, n - anchor, 0, 0);
            return o.ToArray();
        }
        static void Emit(List<byte> o, byte[] src, int litStart, int lit, int off, int len)
        {
            int ml = len == 0 ? 0 : len - 4;
            byte token = (byte)((Math.Min(lit, 15) << 4) | (len == 0 ? 0 : Math.Min(ml, 15)));
            o.Add(token);
            if (lit >= 15) { int r = lit - 15; while (r >= 255) { o.Add(255); r -= 255; } o.Add((byte)r); }
            for (int k = 0; k < lit; k++) o.Add(src[litStart + k]);
            if (len == 0) return;
            o.Add((byte)(off & 0xff)); o.Add((byte)(off >> 8));
            if (ml >= 15) { int r = ml - 15; while (r >= 255) { o.Add(255); r -= 255; } o.Add((byte)r); }
        }
        public static byte[] MozLz4(byte[] plain)
        {
            var c = Compress(plain); var o = new byte[12 + c.Length];
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("mozLz40\0"), 0, o, 0, 8); Buffer.BlockCopy(BitConverter.GetBytes((uint)plain.Length), 0, o, 8, 4); Buffer.BlockCopy(c, 0, o, 12, c.Length);
            return o;
        }
    }
}
