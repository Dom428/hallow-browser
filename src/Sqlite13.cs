// Hallow Browser 1.3 - a minimal, read-only SQLite file reader (no SQLite library ships with .NET Framework, and Hallow Browser
// downloads nothing): enough to read one table of another browser's history file - Chromium's "History" (urls) and Firefox's
// "places.sqlite" (moz_places). It only ever reads a COPY in a temp folder, never the other browser's own file.
// Reads: the header, table b-trees (interior and leaf pages), overflow pages, records (every serial type), UTF-8 / UTF-16 text,
// and a "-wal" file next to the copy (the newest committed page images win, checksums checked). Not: indexes, free lists, writing.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HallowBrowser
{
    public class SqliteReader
    {
        readonly byte[] db; readonly int pageSize, usable; readonly Encoding text;
        readonly Dictionary<uint, byte[]> wal = new Dictionary<uint, byte[]>();
        readonly long pageCount;
        public int WalPages { get { return wal.Count; } }

        public SqliteReader(byte[] file, byte[] walFile = null)
        {
            if (file == null || file.Length < 512 || Encoding.ASCII.GetString(file, 0, 16) != "SQLite format 3\0") throw new InvalidDataException("not an SQLite database");
            db = file;
            int ps = (db[16] << 8) | db[17]; pageSize = ps == 1 ? 65536 : ps;
            if (pageSize < 512 || (pageSize & (pageSize - 1)) != 0) throw new InvalidDataException("bad page size");
            usable = pageSize - db[20];
            uint enc = BE32(db, 56);
            text = enc == 2 ? Encoding.Unicode : enc == 3 ? Encoding.BigEndianUnicode : Encoding.UTF8;
            pageCount = db.Length / pageSize;
            if (walFile != null) ReadWal(walFile);
        }
        public static SqliteReader Open(string path)
        {
            var wal = path + "-wal";
            return new SqliteReader(File.ReadAllBytes(path), File.Exists(wal) && new FileInfo(wal).Length > 32 ? File.ReadAllBytes(wal) : null);
        }

        static uint BE32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        static int BE16(byte[] b, int o) { return b[o] << 8 | b[o + 1]; }

        // the write-ahead log: frames whose salts match the header and whose running checksum is right, up to the last commit
        void ReadWal(byte[] w)
        {
            if (w.Length < 32) return;
            uint magic = BE32(w, 0); if (magic != 0x377f0682 && magic != 0x377f0683) return;
            bool big = (magic & 1) == 1;
            int ps = (int)BE32(w, 8); if (ps != pageSize) return;
            uint salt1 = BE32(w, 16), salt2 = BE32(w, 20);
            uint s0 = 0, s1 = 0; Checksum(w, 0, 24, big, ref s0, ref s1);
            if (s0 != BE32(w, 24) || s1 != BE32(w, 28)) return;
            var pending = new Dictionary<uint, byte[]>();
            for (int o = 32; o + 24 + ps <= w.Length; o += 24 + ps)
            {
                uint pg = BE32(w, o), commit = BE32(w, o + 4);
                if (BE32(w, o + 8) != salt1 || BE32(w, o + 12) != salt2) break;
                Checksum(w, o, 8, big, ref s0, ref s1); Checksum(w, o + 24, ps, big, ref s0, ref s1);
                if (s0 != BE32(w, o + 16) || s1 != BE32(w, o + 20)) break;
                var page = new byte[ps]; Buffer.BlockCopy(w, o + 24, page, 0, ps); pending[pg] = page;
                if (commit != 0) { foreach (var kv in pending) wal[kv.Key] = kv.Value; pending.Clear(); }
            }
        }
        static void Checksum(byte[] b, int off, int len, bool big, ref uint s0, ref uint s1)
        {
            for (int i = off; i + 8 <= off + len; i += 8)
            {
                uint x0 = big ? BE32(b, i) : BitConverter.ToUInt32(b, i), x1 = big ? BE32(b, i + 4) : BitConverter.ToUInt32(b, i + 4);
                unchecked { s0 += x0 + s1; s1 += x1 + s0; }
            }
        }

        byte[] Page(uint n)
        {
            byte[] p; if (wal.TryGetValue(n, out p)) return p;
            if (n < 1 || n > pageCount) throw new InvalidDataException("page " + n + " is outside the file");
            p = new byte[pageSize]; Buffer.BlockCopy(db, (int)((n - 1) * pageSize), p, 0, pageSize); return p;
        }

        static long Varint(byte[] b, ref int o)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) { byte c = b[o++]; v = (v << 7) | (long)(c & 0x7f); if ((c & 0x80) == 0) return v; }
            return (v << 8) | b[o++];
        }

        // every row of the table whose b-tree starts at root: (rowid, values)
        public IEnumerable<KeyValuePair<long, object[]>> Rows(uint root) { return Walk(root, new HashSet<uint>(), 0); }   // in rowid order
        IEnumerable<KeyValuePair<long, object[]>> Walk(uint pg, HashSet<uint> seen, int depth)
        {
            if (depth > 40 || !seen.Add(pg)) yield break;   // a loop or a broken file: stop
            var p = Page(pg); int h = pg == 1 ? 100 : 0;
            byte type = p[h]; int cells = BE16(p, h + 3);
            if (type == 0x05)
            {
                uint right = BE32(p, h + 8);
                for (int i = 0; i < cells; i++)
                {
                    int c = BE16(p, h + 12 + i * 2);
                    foreach (var r in Walk(BE32(p, c), seen, depth + 1)) yield return r;
                }
                foreach (var r in Walk(right, seen, depth + 1)) yield return r;
            }
            else if (type == 0x0D)
            {
                for (int i = 0; i < cells; i++)
                {
                    int c = BE16(p, h + 8 + i * 2);
                    long size = Varint(p, ref c); long rowid = Varint(p, ref c);
                    var payload = Payload(p, c, size);
                    yield return new KeyValuePair<long, object[]>(rowid, Record(payload));
                }
            }
            else throw new InvalidDataException("not a table page (" + type + ")");
        }
        byte[] Payload(byte[] p, int at, long size)
        {
            if (size < 0 || size > 64L * 1024 * 1024) throw new InvalidDataException("bad record size");
            int P = (int)size, X = usable - 35;
            var res = new byte[P];
            if (P <= X) { Buffer.BlockCopy(p, at, res, 0, P); return res; }
            int M = ((usable - 12) * 32 / 255) - 23, K = M + ((P - M) % (usable - 4));
            int local = K <= X ? K : M;
            Buffer.BlockCopy(p, at, res, 0, local);
            int got = local; uint next = BE32(p, at + local); int guard = 0;
            while (got < P && next != 0 && guard++ < 100000)
            {
                var o = Page(next); int n = Math.Min(usable - 4, P - got);
                Buffer.BlockCopy(o, 4, res, got, n); got += n; next = BE32(o, 0);
            }
            if (got < P) throw new InvalidDataException("overflow chain ends early");
            return res;
        }
        object[] Record(byte[] r)
        {
            int o = 0; long hl = Varint(r, ref o); var types = new List<long>();
            while (o < hl) types.Add(Varint(r, ref o));
            int d = (int)hl; var vals = new object[types.Count];
            for (int i = 0; i < types.Count; i++)
            {
                long t = types[i];
                switch (t)
                {
                    case 0: vals[i] = null; break;
                    case 1: vals[i] = (long)(sbyte)r[d]; d += 1; break;
                    case 2: vals[i] = (long)(short)(r[d] << 8 | r[d + 1]); d += 2; break;
                    case 3: { int v = r[d] << 16 | r[d + 1] << 8 | r[d + 2]; if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000); vals[i] = (long)v; d += 3; break; }
                    case 4: vals[i] = (long)(int)BE32(r, d); d += 4; break;
                    case 5: { long v = 0; for (int k = 0; k < 6; k++) v = (v << 8) | r[d + k]; if ((v & 0x800000000000L) != 0) v |= unchecked((long)0xFFFF000000000000UL); vals[i] = v; d += 6; break; }
                    case 6: { long v = 0; for (int k = 0; k < 8; k++) v = (v << 8) | r[d + k]; vals[i] = v; d += 8; break; }
                    case 7: { var b = new byte[8]; for (int k = 0; k < 8; k++) b[k] = r[d + 7 - k]; vals[i] = BitConverter.ToDouble(b, 0); d += 8; break; }
                    case 8: vals[i] = 0L; break;
                    case 9: vals[i] = 1L; break;
                    default:
                        if (t >= 12 && t % 2 == 0) { int n = (int)((t - 12) / 2); var b = new byte[n]; Buffer.BlockCopy(r, d, b, 0, n); vals[i] = b; d += n; }
                        else if (t >= 13) { int n = (int)((t - 13) / 2); vals[i] = text.GetString(r, d, n); d += n; }
                        else throw new InvalidDataException("bad serial type");
                        break;
                }
            }
            return vals;
        }

        // ---- the schema (sqlite_master on page 1)
        public class TableInfo { public string Name, Sql; public uint Root; public List<string> Columns = new List<string>(); public int RowidColumn = -1; }
        public List<TableInfo> Tables()
        {
            var res = new List<TableInfo>();
            foreach (var row in Rows(1))
            {
                var v = row.Value; if (v.Length < 5 || !(v[0] is string) || (string)v[0] != "table") continue;
                var t = new TableInfo { Name = v[1] as string, Root = v[3] is long ? (uint)(long)v[3] : 0, Sql = v[4] as string ?? "" };
                ParseColumns(t); res.Add(t);
            }
            return res;
        }
        public TableInfo Table(string name) { return Tables().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)); }
        // "CREATE TABLE urls(id INTEGER PRIMARY KEY AUTOINCREMENT,url LONGVARCHAR,...)" -> the column names; an INTEGER PRIMARY KEY
        // column is the rowid (the record keeps NULL there)
        static void ParseColumns(TableInfo t)
        {
            var s = t.Sql; int a = s.IndexOf('('), b = s.LastIndexOf(')'); if (a < 0 || b <= a) return;
            var body = s.Substring(a + 1, b - a - 1); var parts = new List<string>(); int depth = 0; var cur = new StringBuilder();
            foreach (var ch in body) { if (ch == '(') depth++; if (ch == ')') depth--; if (ch == ',' && depth == 0) { parts.Add(cur.ToString()); cur.Clear(); } else cur.Append(ch); }
            parts.Add(cur.ToString());
            foreach (var raw in parts)
            {
                var p = raw.Trim(); if (p.Length == 0) continue;
                var first = Regex.Match(p, "^(\"[^\"]+\"|`[^`]+`|\\[[^\\]]+\\]|\\S+)").Value.Trim('"', '`', '[', ']');
                var up = first.ToUpperInvariant();
                if (up == "PRIMARY" || up == "UNIQUE" || up == "CHECK" || up == "FOREIGN" || up == "CONSTRAINT") continue;
                if (Regex.IsMatch(p, @"^\S+\s+INTEGER\s+PRIMARY\s+KEY", RegexOptions.IgnoreCase)) t.RowidColumn = t.Columns.Count;
                t.Columns.Add(first);
            }
        }
        // rows of a table as name -> value (missing columns: null), the rowid column filled in
        public IEnumerable<Dictionary<string, object>> Read(string table)
        {
            var t = Table(table); if (t == null || t.Root == 0) yield break;
            foreach (var row in Rows(t.Root))
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < t.Columns.Count; i++) d[t.Columns[i]] = i < row.Value.Length ? row.Value[i] : null;
                if (t.RowidColumn >= 0) d[t.Columns[t.RowidColumn]] = row.Key;
                yield return d;
            }
        }
    }
}
