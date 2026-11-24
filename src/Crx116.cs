// Hallow Browser 1.16 - "Add from Chrome Web Store", the way Chromium browsers without Google's store UI do it (Brave, Vivaldi...):
// only when you click (a pasted store link or ID on hallow://extensions, or "Add to Hallow Browser" on a chromewebstore.google.com
// extension page), the browser asks Google's official update endpoint (clients2.google.com/service/update2/crx) for that one
// extension's CRX file, checks the CRX3 signature and that the file really is that extension ID (SHA-256 of the signing key),
// shows what it asks for, and only after you say Add unpacks it into the browser's Extensions folder and loads it with
// AddBrowserExtensionAsync. The manifest gets the store key ("key"), so the extension keeps its store ID in Hallow Browser.
// Updates: "Check for updates" (or the optional daily check) asks the same endpoint; a newer version goes through the same checks.
// Nothing else is sent: the request carries the extension ID and the engine's version, as Chromium's own update checks do.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace HallowBrowser
{
    public static class Crx
    {
        public class Result { public string Id, Error, Version, Name; public byte[] PublicKey, Zip; public bool DeveloperSigned; public int Proofs, OtherSigned; }
        public static readonly Regex IdRx = new Regex("^[a-p]{32}$");

        // the 16 bytes of a CRX id as Chromium writes them: each nibble 0-f as a-p
        public static string IdOf(byte[] b, int n = 16)
        {
            var sb = new StringBuilder(); for (int i = 0; i < n; i++) { sb.Append((char)('a' + (b[i] >> 4))); sb.Append((char)('a' + (b[i] & 15))); }
            return sb.ToString();
        }
        public static string IdOfKey(byte[] spki) { using (var sha = SHA256.Create()) return IdOf(sha.ComputeHash(spki)); }

        // ---- a minimal protobuf reader (CrxFileHeader / SignedData)
        static ulong Varint(byte[] b, ref int p, int end) { ulong v = 0; int s = 0; while (p < end) { byte x = b[p++]; v |= (ulong)(x & 0x7f) << s; if ((x & 0x80) == 0) return v; s += 7; if (s > 63) break; } throw new FormatException("bad varint"); }
        static List<KeyValuePair<int, byte[]>> Fields(byte[] b, int start, int end)
        {
            var res = new List<KeyValuePair<int, byte[]>>(); int p = start;
            while (p < end)
            {
                var tag = Varint(b, ref p, end); int field = (int)(tag >> 3), wt = (int)(tag & 7);
                if (wt == 2) { var len = (int)Varint(b, ref p, end); if (len < 0 || p + len > end) throw new FormatException("bad length"); var v = new byte[len]; Buffer.BlockCopy(b, p, v, 0, len); p += len; res.Add(new KeyValuePair<int, byte[]>(field, v)); }
                else if (wt == 0) Varint(b, ref p, end);
                else if (wt == 5) p += 4; else if (wt == 1) p += 8;
                else throw new FormatException("bad wire type");
            }
            return res;
        }

        // ---- the signing key (DER SubjectPublicKeyInfo): RSA or ECDSA P-256
        class Der { public int Tag, Start, Len; }
        static Der Read(byte[] b, ref int p)
        {
            var d = new Der { Tag = b[p++] }; int len = b[p++];
            if ((len & 0x80) != 0) { int n = len & 0x7f; if (n > 4) throw new FormatException(); len = 0; for (int i = 0; i < n; i++) len = (len << 8) | b[p++]; }
            d.Start = p; d.Len = len; if (p + len > b.Length) throw new FormatException(); return d;
        }
        static byte[] Slice(byte[] b, int s, int n) { var r = new byte[n]; Buffer.BlockCopy(b, s, r, 0, n); return r; }
        static byte[] Unsigned(byte[] x) { int i = 0; while (i < x.Length - 1 && x[i] == 0) i++; return Slice(x, i, x.Length - i); }
        static readonly byte[] RsaOid = { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01 }, EcOid = { 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01 }, P256 = { 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07 };
        // true when the signature over data checks out with that key
        public static bool Verify(byte[] spki, byte[] data, byte[] sig, bool ecdsa)
        {
            try
            {
                int p = 0; var top = Read(spki, ref p); if (top.Tag != 0x30) return false;
                var alg = Read(spki, ref p); p = alg.Start + alg.Len; int q = alg.Start; var oid = Read(spki, ref q); var oidB = Slice(spki, oid.Start, oid.Len); q = oid.Start + oid.Len;
                var bits = Read(spki, ref p); if (bits.Tag != 0x03) return false;
                var key = Slice(spki, bits.Start + 1, bits.Len - 1);
                if (!ecdsa && oidB.SequenceEqual(RsaOid))
                {
                    int k = 0; var seq = Read(key, ref k); var n = Read(key, ref k); var nb = Unsigned(Slice(key, n.Start, n.Len)); k = n.Start + n.Len; var e = Read(key, ref k); var eb = Unsigned(Slice(key, e.Start, e.Len));
                    if (nb.Length < 256) return false;   // 2048-bit keys or more
                    using (var rsa = new RSACng()) { rsa.ImportParameters(new RSAParameters { Modulus = nb, Exponent = eb }); return rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
                }
                if (ecdsa && oidB.SequenceEqual(EcOid))
                {
                    var curve = Read(spki, ref q); if (!Slice(spki, curve.Start, curve.Len).SequenceEqual(P256)) return false;
                    if (key.Length != 65 || key[0] != 4) return false;
                    // the signature is DER (r, s); .NET wants r || s, 32 bytes each
                    int s0 = 0; var sq = Read(sig, ref s0); var r = Read(sig, ref s0); s0 = r.Start + r.Len; var s = Read(sig, ref s0);
                    Func<Der, byte[]> fix = d => { var v = Unsigned(Slice(sig, d.Start, d.Len)); if (v.Length > 32) return null; var o = new byte[32]; Buffer.BlockCopy(v, 0, o, 32 - v.Length, v.Length); return o; };
                    var rb = fix(r); var sb = fix(s); if (rb == null || sb == null) return false;
                    using (var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = Slice(key, 1, 32), Y = Slice(key, 33, 32) } }))
                        return ec.VerifyData(data, rb.Concat(sb).ToArray(), HashAlgorithmName.SHA256);
                }
                return false;
            }
            catch { return false; }
        }

        // a CRX3 file checked: the header's proofs, the signed id, the signature that matches the id
        public static Result Parse(byte[] b, string expectedId)
        {
            var res = new Result();
            try
            {
                if (b == null || b.Length < 12 || b[0] != 'C' || b[1] != 'r' || b[2] != '2' || b[3] != '4') { res.Error = "not a CRX file"; return res; }
                var ver = BitConverter.ToUInt32(b, 4); if (ver != 3) { res.Error = "CRX version " + ver + " (only CRX3 is accepted)"; return res; }
                var hl = BitConverter.ToUInt32(b, 8); if (hl > b.Length - 12 || hl > 1024 * 1024) { res.Error = "bad CRX header"; return res; }
                int hs = 12, he = 12 + (int)hl;
                var zip = Slice(b, he, b.Length - he); res.Zip = zip;
                var f = Fields(b, hs, he);
                var signed = f.FirstOrDefault(x => x.Key == 10000).Value; if (signed == null) { res.Error = "the CRX has no signed id"; return res; }
                var crxId = Fields(signed, 0, signed.Length).FirstOrDefault(x => x.Key == 1).Value; if (crxId == null || crxId.Length != 16) { res.Error = "the CRX has no signed id"; return res; }
                res.Id = IdOf(crxId);
                if (expectedId != null && res.Id != expectedId) { res.Error = "the file is extension " + res.Id + ", not " + expectedId; return res; }
                byte[] data; using (var ms = new MemoryStream()) { var pre = Encoding.ASCII.GetBytes("CRX3 SignedData\0"); ms.Write(pre, 0, pre.Length); ms.Write(BitConverter.GetBytes((uint)signed.Length), 0, 4); ms.Write(signed, 0, signed.Length); ms.Write(zip, 0, zip.Length); data = ms.ToArray(); }
                foreach (var proof in f.Where(x => x.Key == 2 || x.Key == 3))
                {
                    res.Proofs++;
                    var pf = Fields(proof.Value, 0, proof.Value.Length);
                    var key = pf.FirstOrDefault(x => x.Key == 1).Value; var sig = pf.FirstOrDefault(x => x.Key == 2).Value; if (key == null || sig == null) continue;
                    var kid = IdOfKey(key);
                    if (!Verify(key, data, sig, proof.Key == 3)) { if (kid == res.Id) { res.Error = "the signature doesn't match the file (changed or damaged)"; return res; } continue; }
                    if (kid == res.Id) { res.DeveloperSigned = true; res.PublicKey = key; }
                    else res.OtherSigned++;   // e.g. the Chrome Web Store's own publisher proof
                }
                if (!res.DeveloperSigned) { res.Error = "no valid signature from the extension's own key"; return res; }
                if (zip.Length < 22 || zip[0] != 'P' || zip[1] != 'K') { res.Error = "the CRX holds no zip"; return res; }
            }
            catch (Exception e) { res.Error = "the CRX couldn't be read (" + e.Message + ")"; }
            return res;
        }
    }
}
