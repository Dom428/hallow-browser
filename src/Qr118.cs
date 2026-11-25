// Hallow Browser 1.18 - a small QR code encoder for "Share this page" (drawn on this PC - no web service is asked for a picture).
// Byte mode, error correction level M, versions 1-40, the eight masks scored with the standard penalty rules. Written from the QR
// code specification (ISO/IEC 18004) the way the well-known open reference implementations lay it out: function patterns, the
// codewords in their zig-zag order, Reed-Solomon over GF(256) with 0x11D, the format bits (BCH 0x537, masked with 0x5412) and the
// version bits (BCH 0x1F25). Unit tests read a code back (format bits, unmasking, de-interleaving, the RS syndromes) and compare.
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace HallowBrowser
{
    public class QrCode
    {
        public int Version, Size, Mask;
        public bool[,] Dark;        // [y, x]
        public bool[,] Function;    // [y, x]: finder, timing, alignment, format and version modules
        public bool Get(int x, int y) { return x >= 0 && y >= 0 && x < Size && y < Size && Dark[y, x]; }
    }

    public static class Qr
    {
        public const int MaxBytes = 1200;   // longer links are shared as text only (a version 25+ code is hard to scan from a screen)
        // error correction level M: ECC codewords per block, number of blocks (index = version)
        internal static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 };
        internal static readonly int[] Blocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 };
        const int FormatEccM = 0;   // L = 1, M = 0, Q = 3, H = 2

        public static int RawModules(int v)
        {
            int r = (16 * v + 128) * v + 64;
            if (v >= 2) { int n = v / 7 + 2; r -= (25 * n - 10) * n - 55; if (v >= 7) r -= 36; }
            return r;
        }
        public static int DataCodewords(int v) { return RawModules(v) / 8 - EccPerBlock[v] * Blocks[v]; }
        public static int[] AlignmentPositions(int v)
        {
            if (v == 1) return new int[0];
            int n = v / 7 + 2, size = v * 4 + 17;
            int step = v == 32 ? 26 : (int)Math.Ceiling((v * 4 + 4) / (double)(n * 2 - 2)) * 2;
            var r = new int[n]; r[0] = 6;
            for (int i = n - 1, pos = size - 7; i >= 1; i--, pos -= step) r[i] = pos;
            return r;
        }

        // ---- GF(256) and Reed-Solomon
        internal static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
            return z & 0xFF;
        }
        internal static byte[] Divisor(int degree)
        {
            var r = new byte[degree]; r[degree - 1] = 1; int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++) { r[j] = (byte)Mul(r[j], root); if (j + 1 < degree) r[j] ^= r[j + 1]; }
                root = Mul(root, 0x02);
            }
            return r;
        }
        internal static byte[] Remainder(IList<byte> data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (var b in data)
            {
                int f = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1); r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], f);
            }
            return r;
        }

        public static QrCode Encode(string text) { return text == null ? null : Encode(Encoding.UTF8.GetBytes(text)); }
        public static QrCode Encode(byte[] data)
        {
            if (data == null || data.Length > MaxBytes) return null;
            int v = 1;
            for (; v <= 40; v++) { int bits = 4 + (v < 10 ? 8 : 16) + data.Length * 8; if (bits <= DataCodewords(v) * 8) break; }
            if (v > 40) return null;
            // the bit stream: mode 0100 (bytes), the count, the bytes, a terminator, then padding
            var bb = new List<bool>();
            Action<int, int> put = (val, n) => { for (int i = n - 1; i >= 0; i--) bb.Add(((val >> i) & 1) != 0); };
            put(4, 4); put(data.Length, v < 10 ? 8 : 16); foreach (var b in data) put(b, 8);
            int cap = DataCodewords(v) * 8;
            put(0, Math.Min(4, cap - bb.Count));
            put(0, (8 - bb.Count % 8) % 8);
            for (int pad = 0xEC; bb.Count < cap; pad ^= 0xEC ^ 0x11) put(pad, 8);
            var cw = new byte[bb.Count / 8];
            for (int i = 0; i < bb.Count; i++) if (bb[i]) cw[i >> 3] |= (byte)(1 << (7 - (i & 7)));
            var all = Interleave(cw, v);
            var q = new QrCode { Version = v, Size = v * 4 + 17 };
            q.Dark = new bool[q.Size, q.Size]; q.Function = new bool[q.Size, q.Size];
            DrawFunction(q);
            DrawCodewords(q, all);
            // the mask with the lowest penalty
            int best = 0, bestScore = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(q, m); DrawFormat(q, m);
                int p = Penalty(q); if (p < bestScore) { bestScore = p; best = m; }
                ApplyMask(q, m);   // XOR again = undone
            }
            ApplyMask(q, best); DrawFormat(q, best); q.Mask = best;
            return q;
        }
        // the data codewords split into blocks, each with its ECC, then interleaved column by column
        internal static byte[] Interleave(byte[] data, int v)
        {
            int nb = Blocks[v], ecc = EccPerBlock[v], raw = RawModules(v) / 8, nShort = nb - raw % nb, shortLen = raw / nb;
            var div = Divisor(ecc); var blocks = new List<byte[]>(); int k = 0;
            for (int i = 0; i < nb; i++)
            {
                int len = shortLen - ecc + (i < nShort ? 0 : 1);
                var dat = new byte[len]; Array.Copy(data, k, dat, 0, len); k += len;
                var e = Remainder(dat, div);
                var blk = new byte[shortLen + 1];   // a short block has a placeholder before its ECC (skipped below)
                Array.Copy(dat, 0, blk, 0, len); Array.Copy(e, 0, blk, shortLen + 1 - ecc, ecc);
                blocks.Add(blk);
            }
            var r = new List<byte>();
            for (int i = 0; i <= shortLen; i++)
                for (int j = 0; j < nb; j++)
                    if (i != shortLen - ecc || j >= nShort) r.Add(blocks[j][i]);
            return r.ToArray();
        }
        static void SetF(QrCode q, int x, int y, bool dark) { q.Dark[y, x] = dark; q.Function[y, x] = true; }
        static void DrawFunction(QrCode q)
        {
            int n = q.Size;
            for (int i = 0; i < n; i++) { SetF(q, 6, i, i % 2 == 0); SetF(q, i, 6, i % 2 == 0); }
            foreach (var c in new[] { new[] { 3, 3 }, new[] { n - 4, 3 }, new[] { 3, n - 4 } })
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = c[0] + dx, yy = c[1] + dy;
                        if (xx >= 0 && xx < n && yy >= 0 && yy < n) SetF(q, xx, yy, d != 2 && d != 4);
                    }
            var al = AlignmentPositions(q.Version); int na = al.Length;
            for (int i = 0; i < na; i++)
                for (int j = 0; j < na; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == na - 1) || (i == na - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) SetF(q, al[i] + dx, al[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormat(q, 0);
            if (q.Version >= 7)
            {
                int rem = q.Version; for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                long bits = ((long)q.Version << 12) | (long)rem;
                for (int i = 0; i < 18; i++) { bool b = ((bits >> i) & 1) != 0; int a = n - 11 + i % 3, c = i / 3; SetF(q, a, c, b); SetF(q, c, a, b); }
            }
        }
        public static int FormatBits(int mask)
        {
            int data = FormatEccM << 3 | mask, rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            return ((data << 10) | rem) ^ 0x5412;
        }
        static void DrawFormat(QrCode q, int mask)
        {
            int bits = FormatBits(mask), n = q.Size;
            Func<int, bool> B = i => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) SetF(q, 8, i, B(i));
            SetF(q, 8, 7, B(6)); SetF(q, 8, 8, B(7)); SetF(q, 7, 8, B(8));
            for (int i = 9; i < 15; i++) SetF(q, 14 - i, 8, B(i));
            for (int i = 0; i < 8; i++) SetF(q, n - 1 - i, 8, B(i));
            for (int i = 8; i < 15; i++) SetF(q, 8, n - 15 + i, B(i));
            SetF(q, 8, n - 8, true);   // the dark module
        }
        // the zig-zag: two-module columns from the right, up then down, skipping the vertical timing column
        public static List<int[]> DataOrder(QrCode q)
        {
            var r = new List<int[]>(); int n = q.Size;
            for (int right = n - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < n; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j; bool up = ((right + 1) & 2) == 0; int y = up ? n - 1 - vert : vert;
                        if (!q.Function[y, x]) r.Add(new[] { x, y });
                    }
            }
            return r;
        }
        static void DrawCodewords(QrCode q, byte[] data)
        {
            int i = 0;
            foreach (var p in DataOrder(q)) { if (i < data.Length * 8) q.Dark[p[1], p[0]] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0; i++; }
        }
        public static bool MaskBit(int m, int x, int y)
        {
            switch (m)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return x * y % 2 + x * y % 3 == 0;
                case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
            }
        }
        static void ApplyMask(QrCode q, int m)
        {
            for (int y = 0; y < q.Size; y++) for (int x = 0; x < q.Size; x++) if (!q.Function[y, x] && MaskBit(m, x, y)) q.Dark[y, x] = !q.Dark[y, x];
        }
        // the standard penalty: runs of 5+, 2x2 blocks, finder-like 1:1:3:1:1 patterns with 4 light modules beside, dark balance
        static int Penalty(QrCode q)
        {
            int n = q.Size, p = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int a = 0; a < n; a++)
                {
                    int run = 1;
                    for (int b = 1; b <= n; b++)
                    {
                        bool same = b < n && At(q, pass, a, b) == At(q, pass, a, b - 1);
                        if (same) run++; else { if (run >= 5) p += 3 + (run - 5); run = 1; }
                    }
                    for (int b = 0; b + 7 <= n; b++)
                    {
                        if (!(At(q, pass, a, b) && !At(q, pass, a, b + 1) && At(q, pass, a, b + 2) && At(q, pass, a, b + 3) && At(q, pass, a, b + 4) && !At(q, pass, a, b + 5) && At(q, pass, a, b + 6))) continue;
                        bool before = true, after = true;
                        for (int k = 1; k <= 4; k++) { if (b - k >= 0 && At(q, pass, a, b - k)) before = false; if (b + 6 + k < n && At(q, pass, a, b + 6 + k)) after = false; }
                        if (before || after) p += 40;
                    }
                }
            for (int y = 0; y + 1 < n; y++) for (int x = 0; x + 1 < n; x++) { bool c = q.Dark[y, x]; if (c == q.Dark[y, x + 1] && c == q.Dark[y + 1, x] && c == q.Dark[y + 1, x + 1]) p += 3; }
            int dark = 0; for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) if (q.Dark[y, x]) dark++;
            int total = n * n, k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            p += Math.Max(0, k2) * 10;
            return p;
        }
        static bool At(QrCode q, int pass, int a, int b) { return pass == 0 ? q.Dark[a, b] : q.Dark[b, a]; }

        // a picture of it: dark modules on white with the 4-module quiet zone, crisp at any size
        public static ImageSource Image(QrCode q)
        {
            if (q == null) return null;
            var g = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int y = 0; y < q.Size; y++) for (int x = 0; x < q.Size; x++) if (q.Dark[y, x]) g.Children.Add(new RectangleGeometry(new Rect(x + 4, y + 4, 1, 1)));
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, q.Size + 8, q.Size + 8))));
            dg.Children.Add(new GeometryDrawing(Brushes.Black, null, g));
            var img = new DrawingImage(dg); img.Freeze();
            return img;
        }

        // ---- reading one back (tests): the format bits, unmasking, the codewords, de-interleaving, the RS syndromes and the bytes
        public static string Read(QrCode q, out string why)
        {
            why = null;
            int f = 0; for (int i = 0; i <= 5; i++) if (q.Get(8, i)) f |= 1 << i;
            if (q.Get(8, 7)) f |= 1 << 6; if (q.Get(8, 8)) f |= 1 << 7; if (q.Get(7, 8)) f |= 1 << 8;
            for (int i = 9; i < 15; i++) if (q.Get(14 - i, 8)) f |= 1 << i;
            int mask = -1; for (int m = 0; m < 8; m++) if (FormatBits(m) == f) mask = m;
            if (mask < 0) { why = "format bits"; return null; }
            int f2 = 0, n = q.Size; for (int i = 0; i < 8; i++) if (q.Get(n - 1 - i, 8)) f2 |= 1 << i; for (int i = 8; i < 15; i++) if (q.Get(8, n - 15 + i)) f2 |= 1 << i;
            if (f2 != f) { why = "second format copy"; return null; }
            int v = (n - 17) / 4; var order = DataOrder(q); int raw = RawModules(v) / 8;
            var cw = new byte[raw];
            for (int i = 0; i < raw * 8; i++) { var p = order[i]; bool bit = q.Dark[p[1], p[0]] ^ MaskBit(mask, p[0], p[1]); if (bit) cw[i >> 3] |= (byte)(1 << (7 - (i & 7))); }
            int nb = Blocks[v], ecc = EccPerBlock[v], nShort = nb - raw % nb, shortLen = raw / nb;
            var blocks = new List<List<byte>>(); for (int j = 0; j < nb; j++) blocks.Add(new List<byte>());
            int k = 0;
            for (int i = 0; i <= shortLen; i++) for (int j = 0; j < nb; j++) if (i != shortLen - ecc || j >= nShort) blocks[j].Add(cw[k++]);
            var data = new List<byte>();
            for (int j = 0; j < nb; j++)
            {
                var blk = blocks[j]; int dl = blk.Count - ecc;
                // syndromes S_i = c(alpha^i), i = 0 .. ecc-1, are all zero for a valid code word
                int a = 1;
                for (int i = 0; i < ecc; i++)
                {
                    int s = 0; foreach (var c in blk) s = Mul(s, a) ^ c;
                    if (s != 0) { why = "block " + j + " syndrome " + i; return null; }
                    a = Mul(a, 2);
                }
                data.AddRange(blk.GetRange(0, dl));
            }
            var bits = new List<bool>(); foreach (var b in data) for (int i = 7; i >= 0; i--) bits.Add(((b >> i) & 1) != 0);
            int pos = 0; Func<int, int> take = c => { int r = 0; for (int i = 0; i < c; i++) r = (r << 1) | (bits[pos++] ? 1 : 0); return r; };
            if (take(4) != 4) { why = "mode"; return null; }
            int len = take(v < 10 ? 8 : 16); var outb = new byte[len];
            for (int i = 0; i < len; i++) outb[i] = (byte)take(8);
            return Encoding.UTF8.GetString(outb);
        }
    }
}
