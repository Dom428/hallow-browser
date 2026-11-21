// Hallow Browser 1.3 - sync between your PCs through a folder you choose (OneDrive, Dropbox, Syncthing, a NAS share...), no server.
// The same model as Hallow Keys 2.0's sync, simplified (its own code, not Keys'):
//   - everything that syncs is a list of records (a bookmark or folder, a setting, a saved session, a workspace, the sidebar's panels),
//     each with a version vector (how many changes each PC made to it), when and on which PC it last changed
//   - deleting leaves a tombstone, so the delete reaches the other PCs; an edit the delete hadn't seen wins over it
//   - one side has seen everything the other has -> the newer one is kept
//   - both changed it since they last met -> the later change wins; for a bookmark or a saved session the other one is kept as
//     "(conflict copy from PC-NAME)" - the same copy, with the same id, on every PC
//   - cloud clients' conflict files ("Hallow Browser sync (1).hbsync", "...-DESKTOP-ABC.hbsync", Dropbox's "conflicted copy",
//     Syncthing's ".sync-conflict-") are merged in and moved out of the folder to this PC's data folder (sync-merged)
//   - the folder's copy is written whole (a temp file, then replace), retried while a cloud client holds it
//   - optional encryption: a sync password -> PBKDF2-SHA256 (600,000 rounds) -> AES-256-GCM. Without the password the file in the
//     folder is unreadable. The password is kept on this PC only, protected by Windows (DPAPI, this user).
// Never synced: cookies, passwords, open tabs of other PCs' windows, window sizes and other per-PC settings; history only opt-in and
// only encrypted (1.7, Sync17.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace HallowBrowser
{
    [DataContract]
    public class SyncRecord
    {
        [DataMember] public string Id;          // "bm:<id>", "set:<name>", "saved:<name>", "ws:<id>", "side"
        [DataMember] public string Kind;        // bm | set | saved | ws | side
        [DataMember] public string Data;        // the content (JSON); null for a tombstone
        [DataMember] public bool Gone;          // a tombstone
        [DataMember] public string Vv = "";     // version vector "dev:n,dev:n"
        [DataMember] public long Time;          // UTC ticks of the change
        [DataMember] public string Dev = "", DevName = "";
        [DataMember] public string Hash;        // of Kind + Data (worked out again on reading)
        public SyncRecord Clone() { return (SyncRecord)MemberwiseClone(); }
        public static string HashOf(string kind, string data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes((kind ?? "") + "\n" + (data ?? ""))), 0, 16).Replace("-", "").ToLowerInvariant();
        }
    }
    [DataContract] public class SyncDoc { [DataMember] public string Format = "hallow-browser-sync"; [DataMember] public int Version = 1; [DataMember] public string Saved; [DataMember] public List<SyncRecord> Records = new List<SyncRecord>(); }
    // this PC's memory of the last round (per folder): every record's revision as merged then
    [DataContract] public class SyncState { [DataMember] public string Folder; [DataMember] public List<SyncRecord> Known = new List<SyncRecord>(); [DataMember] public string Last; }

    public static class Vv
    {
        public static SortedDictionary<string, long> Parse(string s)
        {
            var d = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var part in (s ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int c = part.LastIndexOf(':'); long n;
                if (c > 0 && long.TryParse(part.Substring(c + 1), out n) && n > 0) { var k = part.Substring(0, c); long o; d[k] = d.TryGetValue(k, out o) ? Math.Max(o, n) : n; }
            }
            return d;
        }
        public static string Format(IDictionary<string, long> d) { return string.Join(",", d.Where(x => x.Value > 0).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value)); }
        public static string Bump(string vv, string dev) { var d = Parse(vv); long n; d[dev] = (d.TryGetValue(dev, out n) ? n : 0) + 1; return Format(d); }
        public static string Max(string a, string b) { var x = Parse(a); foreach (var kv in Parse(b)) { long n; x[kv.Key] = x.TryGetValue(kv.Key, out n) ? Math.Max(n, kv.Value) : kv.Value; } return Format(x); }
        // 0 same, 1 a has seen all of b and more, -1 the other way round, 2 both have changes the other hasn't seen
        public static int Compare(string a, string b)
        {
            var x = Parse(a); var y = Parse(b); bool am = false, bm = false;
            foreach (var k in x.Keys.Union(y.Keys)) { long p, q; x.TryGetValue(k, out p); y.TryGetValue(k, out q); if (p > q) am = true; else if (q > p) bm = true; }
            return am && bm ? 2 : am ? 1 : bm ? -1 : 0;
        }
    }

    public class SyncOptions
    {
        public string Dev, DevName, Password;
        public Func<SyncRecord, string, string> CopyData;   // a conflict copy's content (rec, "PC name"), or null = no copy for this kind
        public string KeepDir;                              // where merged conflict files go (this PC's data folder)
        public int HistoryMax;                              // 1.7: history syncs here with this cap; 0 = not here (other PCs' pages pass through, encrypted)
    }
    public class SyncOutcome
    {
        public string State = "synced";   // synced | waiting | error
        public string Message = "";
        public List<SyncRecord> Result = new List<SyncRecord>();   // every live record after the merge
        public HashSet<string> ChangedKinds = new HashSet<string>();   // kinds whose records this PC must take
        public int Conflicts, ConflictFiles, Incoming;
        public bool Wrote, Encrypted;
        public SyncState State2;
    }

    public static class SyncCore
    {
        public const string FileName = "Hallow Browser sync.hbsync";
        public static int WriteTries = 8;
        static bool Singleton(string kind) { return kind == "set" || kind == "side" || kind == "shf"; }
        // 1.7: kinds that never leave tombstones: a page falling out of the history cap, or My filters that couldn't be read this time,
        // aren't deletes. And kinds that are never written to an unencrypted folder.
        public static bool NoTombstone(string kind) { return kind == "hist" || kind == "shf"; }
        public static bool EncryptedOnly(string kind) { return kind == "hist"; }
        static readonly System.Text.RegularExpressions.Regex lastRx = new System.Text.RegularExpressions.Regex("\"l\":\"([^\"]*)\"");
        static string LastVisit(string data) { if (data == null) return ""; var m = lastRx.Match(data); return m.Success ? m.Groups[1].Value : ""; }
        // history: the newest max pages (by when they last changed); max 0 = not syncing here, leave them as they are
        public static List<SyncRecord> Cap(List<SyncRecord> merged, int historyMax, bool encrypted)
        {
            var res = merged.Where(r => !(r.Kind == "hist" && r.Gone) && (encrypted || !EncryptedOnly(r.Kind))).ToList();
            if (historyMax > 0 && encrypted)
            {
                // by last visit (the same order this PC picks its own newest pages in), so the cap settles instead of swapping pages
                var keep = new HashSet<string>(res.Where(r => r.Kind == "hist").OrderByDescending(r => LastVisit(r.Data), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).Take(historyMax).Select(r => r.Id));
                res = res.Where(r => r.Kind != "hist" || keep.Contains(r.Id)).ToList();
            }
            return res;
        }

        // brings this PC's records up to date against what it knew: new, changed (hash differs), gone (tombstone)
        public static List<SyncRecord> Stamp(List<SyncRecord> local, SyncState st, string dev, string devName, DateTime nowUtc, bool joining = false)
        {
            var now = nowUtc.Ticks; bool first = joining && st.Known.Count == 0;
            var known = st.Known.GroupBy(k => k.Id).ToDictionary(g => g.Key, g => g.First());
            var res = new List<SyncRecord>(); var seen = new HashSet<string>();
            foreach (var l in local)
            {
                if (l == null || string.IsNullOrEmpty(l.Id) || !seen.Add(l.Id)) continue;
                var r = new SyncRecord { Id = l.Id, Kind = l.Kind, Data = l.Data, Hash = SyncRecord.HashOf(l.Kind, l.Data) };
                SyncRecord k;
                if (known.TryGetValue(l.Id, out k) && !k.Gone && k.Hash == r.Hash) { r.Vv = k.Vv; r.Time = k.Time; r.Dev = k.Dev; r.DevName = k.DevName; }
                else
                {
                    r.Vv = Vv.Bump(k == null ? "" : k.Vv, dev); r.Dev = dev; r.DevName = devName;
                    // a PC joining a folder for the first time takes the folder's settings (its own count as older)
                    r.Time = first && Singleton(l.Kind) ? 0 : now;
                }
                res.Add(r);
            }
            foreach (var k in known.Values)
            {
                if (seen.Contains(k.Id)) continue;
                if (NoTombstone(k.Kind)) continue;   // 1.7: not here this time isn't a delete
                if (k.Gone) { if (k.Time > now - TimeSpan.FromDays(365).Ticks) res.Add(k.Clone()); continue; }   // tombstones are let go after a year
                res.Add(new SyncRecord { Id = k.Id, Kind = k.Kind, Gone = true, Vv = Vv.Bump(k.Vv, dev), Time = now, Dev = dev, DevName = devName, Hash = "" });
            }
            return res;
        }

        static bool Later(SyncRecord a, SyncRecord b)
        {
            if (a.Time != b.Time) return a.Time > b.Time;
            int d = string.CompareOrdinal(a.Dev, b.Dev); if (d != 0) return d > 0;
            return string.CompareOrdinal(a.Hash ?? "", b.Hash ?? "") >= 0;
        }
        // two copies into one: the same result on both PCs, whichever merges first
        public static List<SyncRecord> Merge(List<SyncRecord> a, List<SyncRecord> b, Func<SyncRecord, string, string> copyData, ref int conflicts, Func<SyncRecord, SyncRecord, string> combine = null)
        {
            foreach (var r in b) if (!r.Gone) r.Hash = SyncRecord.HashOf(r.Kind, r.Data);
            var A = a.Where(x => x != null && x.Id != null).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var B = b.Where(x => x != null && x.Id != null).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var outp = new Dictionary<string, SyncRecord>(); var copies = new List<SyncRecord>();
            foreach (var id in A.Keys.Concat(B.Keys).Distinct())
            {
                SyncRecord x, y; A.TryGetValue(id, out x); B.TryGetValue(id, out y);
                if (x == null) { outp[id] = y; continue; }
                if (y == null) { outp[id] = x; continue; }
                int c = Vv.Compare(x.Vv, y.Vv);
                SyncRecord w;
                if (x.Gone && y.Gone) { w = (Later(x, y) ? x : y).Clone(); w.Vv = Vv.Max(x.Vv, y.Vv); }
                else if (x.Gone || y.Gone)
                {
                    var tomb = x.Gone ? x : y; var live = x.Gone ? y : x; int t = Vv.Compare(tomb.Vv, live.Vv);
                    if (t == 0 || t == 1) w = tomb;
                    else { w = live.Clone(); if (t == 2) w.Vv = Vv.Max(live.Vv, tomb.Vv); }   // an edit the delete hadn't seen: kept
                }
                else if (c == 1) w = x;
                else if (c == -1) w = y;
                else if (x.Hash == y.Hash) { w = x.Clone(); w.Vv = Vv.Max(x.Vv, y.Vv); }
                else if (combine != null && combine(x, y) != null)
                {
                    // 1.7: both changed and the kind can be put together (a history page: most visits, newest visit) - the same result on both PCs
                    var data = combine(x, y); var xw = Later(x, y);
                    w = (xw ? x : y).Clone(); w.Data = data; w.Hash = SyncRecord.HashOf(w.Kind, data); w.Vv = Vv.Max(x.Vv, y.Vv); w.Time = Math.Max(x.Time, y.Time);
                }
                else
                {
                    var xw = Later(x, y); var win = xw ? x : y; var lose = xw ? y : x;
                    w = win.Clone(); w.Vv = Vv.Max(x.Vv, y.Vv);
                    if (c == 2)
                    {
                        conflicts++;
                        var cd = copyData == null ? null : copyData(lose, string.IsNullOrEmpty(lose.DevName) ? "another PC" : lose.DevName);
                        if (cd != null)
                        {
                            string cid; using (var sha = SHA256.Create()) cid = lose.Kind + ":c" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("conflict|" + id + "|" + lose.Hash)), 0, 8).Replace("-", "").ToLowerInvariant();
                            copies.Add(new SyncRecord { Id = cid, Kind = lose.Kind, Data = cd, Vv = lose.Vv, Time = lose.Time, Dev = lose.Dev, DevName = lose.DevName, Hash = SyncRecord.HashOf(lose.Kind, cd) });
                        }
                    }
                }
                outp[id] = w;
            }
            foreach (var cp in copies) if (!outp.ContainsKey(cp.Id)) outp[cp.Id] = cp;
            return outp.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        }
        public static string Digest(IEnumerable<SyncRecord> rs) { return string.Join(";", rs.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Id + "=" + r.Vv + (r.Gone ? "x" : ""))); }

        public static string Provider(string folder)
        {
            var f = (folder ?? "").ToLowerInvariant();
            if (f.Contains("onedrive")) return "OneDrive";
            if (f.Contains("dropbox")) return "Dropbox";
            if (f.Contains("google drive") || f.Contains("my drive")) return "Google Drive";
            if (f.Contains("sync")) return "Syncthing";
            if (f.StartsWith(@"\\")) return "the network folder";
            return "the sync folder";
        }
        public static bool LooksLikeCloud(string folder) { var p = Provider(folder); return p != "the sync folder" && p != "Syncthing" && p != "the network folder"; }
        // cloud clients' copies of our file (never our own temp files)
        public static List<string> ConflictFiles(string folder)
        {
            try
            {
                var bas = Path.GetFileNameWithoutExtension(FileName);
                return Directory.GetFiles(folder, "*.hbsync").Where(f => { var n = Path.GetFileName(f); return !n.Equals(FileName, StringComparison.OrdinalIgnoreCase) && n.StartsWith(bas, StringComparison.OrdinalIgnoreCase); })
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }

        // ---- the file: JSON, or "HBSYNC1\n" + rounds + salt + nonce + AES-GCM ciphertext + tag
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HBSYNC1\n");
        public static bool IsEncrypted(byte[] b) { return b != null && b.Length >= Magic.Length && b.Take(Magic.Length).SequenceEqual(Magic); }
        static readonly DataContractJsonSerializer docSer = new DataContractJsonSerializer(typeof(SyncDoc));
        public static byte[] Serialize(SyncDoc d) { using (var ms = new MemoryStream()) { lock (docSer) docSer.WriteObject(ms, d); return ms.ToArray(); } }
        public static SyncDoc Deserialize(byte[] b)
        {
            using (var ms = new MemoryStream(b)) { SyncDoc d; lock (docSer) d = (SyncDoc)docSer.ReadObject(ms); if (d == null || d.Format != "hallow-browser-sync") throw new InvalidDataException("not a Hallow Browser sync file"); if (d.Records == null) d.Records = new List<SyncRecord>(); return d; }
        }
        // reads a copy: plain, or encrypted with the password (wrong / no password -> CryptographicException)
        public static SyncDoc Read(byte[] b, string password)
        {
            if (!IsEncrypted(b)) return Deserialize(b);
            if (string.IsNullOrEmpty(password)) throw new CryptographicException("needs the sync password");
            return Deserialize(SyncCrypto.Open(b, password));
        }

        // one round against the folder
        public static SyncOutcome Run(List<SyncRecord> local, SyncState st, string folder, SyncOptions o)
        {
            var res = new SyncOutcome(); var prov = Provider(folder);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { res.State = "waiting"; res.Message = "Waiting for " + prov + " (the folder isn't there right now)"; return res; }
            if (st.Folder != null && !string.Equals(st.Folder, folder, StringComparison.OrdinalIgnoreCase)) st = new SyncState();   // another folder: start fresh
            st.Folder = folder;
            var main = Path.Combine(folder, FileName);
            var mine = Stamp(local, st, o.Dev, o.DevName, DateTime.UtcNow, File.Exists(main));   // joining a folder that has a copy: its settings win
            var merged = mine; int conflicts = 0; string remoteDigest = null; var mergedFiles = new List<string>(); bool remoteEncrypted = false;
            if (File.Exists(main))
            {
                byte[] bytes;
                try { bytes = Importer.ReadShared(main); }
                catch (IOException) { res.State = "waiting"; res.Message = "Waiting for " + prov + " (the sync file is in use)"; return res; }
                SyncDoc doc;
                remoteEncrypted = IsEncrypted(bytes);
                try { doc = Read(bytes, o.Password); }
                catch (CryptographicException)
                {
                    res.State = "error";
                    res.Message = string.IsNullOrEmpty(o.Password) ? "The copy in " + prov + " is encrypted - enter the sync password in Settings > Sync" : "The sync password doesn't open the copy in " + prov + " - it was set with another password";
                    return res;
                }
                catch (InvalidDataException e) { res.State = "error"; res.Message = e.Message + " - nothing was changed in " + prov; return res; }
                catch { res.State = "waiting"; res.Message = "Waiting for " + prov + " to finish copying the sync file"; return res; }
                remoteDigest = Digest(doc.Records);
                merged = Merge(mine, doc.Records, o.CopyData, ref conflicts, SyncData.Combine);
            }
            foreach (var cf in ConflictFiles(folder))
            {
                try { var doc = Read(Importer.ReadShared(cf), o.Password); merged = Merge(merged, doc.Records, o.CopyData, ref conflicts, SyncData.Combine); mergedFiles.Add(cf); }
                catch { }   // still being written, or another password: left alone
            }
            merged = Cap(merged, o.HistoryMax, !string.IsNullOrEmpty(o.Password));   // 1.7: history's size cap; never unencrypted
            res.Conflicts = conflicts; res.ConflictFiles = mergedFiles.Count;
            // what this PC has to take
            var before = mine.Where(r => !r.Gone).ToDictionary(r => r.Id, r => r.Hash);
            var after = merged.Where(r => !r.Gone).ToList();
            foreach (var r in after) { string h; if (!before.TryGetValue(r.Id, out h) || h != r.Hash) { res.ChangedKinds.Add(r.Kind); res.Incoming++; } }
            var afterIds = new HashSet<string>(after.Select(r => r.Id));
            foreach (var r in mine) if (!r.Gone && !afterIds.Contains(r.Id)) { res.ChangedKinds.Add(r.Kind); res.Incoming++; }
            res.Result = after;
            st.Known = merged.Select(r => { var c = r.Clone(); c.Data = null; return c; }).ToList();
            st.Last = DateTime.Now.ToString("s");
            res.State2 = st;
            // the folder's copy: written when it's behind (or should be encrypted now, or not any more)
            bool wantEnc = !string.IsNullOrEmpty(o.Password);
            if (remoteDigest != Digest(merged) || mergedFiles.Count > 0 || (remoteDigest != null && remoteEncrypted != wantEnc))
            {
                var doc = new SyncDoc { Saved = DateTime.UtcNow.ToString("s") + "Z", Records = merged };
                var bytes = Serialize(doc);
                if (wantEnc) bytes = SyncCrypto.Seal(bytes, o.Password, ExistingSalt(main));
                if (!WriteShared(main, bytes, o.Dev)) { res.State = "waiting"; res.Message = "Waiting for " + prov + " (it's holding the sync file - trying again soon)"; return res; }
                res.Wrote = true;
                foreach (var cf in mergedFiles) Archive(cf, o.KeepDir);
            }
            res.Encrypted = wantEnc;
            res.State = "synced"; res.Message = "Synced with " + prov;
            return res;
        }
        // keeps the salt of the copy that's there (every PC then derives the key once)
        static byte[] ExistingSalt(string main) { try { if (!File.Exists(main)) return null; var b = Importer.ReadShared(main); return SyncCrypto.SaltOf(b); } catch { return null; } }
        public static bool WriteShared(string path, byte[] bytes, string dev)
        {
            var tmp = Path.Combine(Path.GetDirectoryName(path), "~" + Path.GetFileName(path) + "." + (dev ?? "x").Substring(0, Math.Min(8, (dev ?? "x").Length)) + ".tmp");
            try { using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); } }
            catch { try { File.Delete(tmp); } catch { } return false; }
            for (int i = 0; i < WriteTries; i++)
            {
                try { if (File.Exists(path)) File.Replace(tmp, path, null, true); else File.Move(tmp, path); return true; }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                System.Threading.Thread.Sleep(150 * (i + 1));
            }
            try { File.Delete(tmp); } catch { }
            return false;
        }
        // a merged conflict file: kept on this PC (sync-merged, the newest 20), then taken out of the folder
        static void Archive(string path, string keepDir)
        {
            try
            {
                var keep = Path.Combine(keepDir ?? BrowserSettings.Dir, "sync-merged"); Directory.CreateDirectory(keep);
                File.Copy(path, Path.Combine(keep, DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Path.GetFileName(path)), true);
                File.Delete(path);
                foreach (var old in Directory.GetFiles(keep).OrderByDescending(f => f, StringComparer.Ordinal).Skip(20)) try { File.Delete(old); } catch { }
            }
            catch { }
        }
    }

    // PBKDF2-SHA256 -> AES-256-GCM (Windows' CNG, bcrypt.dll - .NET Framework has no AES-GCM class)
    public static class SyncCrypto
    {
        public static int Iterations = 600000;
        const int SaltSize = 16, NonceSize = 12, TagSize = 16;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("HBSYNC1\n");
        static readonly Dictionary<string, byte[]> keys = new Dictionary<string, byte[]>();   // derived keys, this run only
        static byte[] Key(string password, byte[] salt, int rounds)
        {
            var id = Convert.ToBase64String(new SHA256Managed().ComputeHash(Encoding.UTF8.GetBytes(password + "\u0001" + Convert.ToBase64String(salt) + "\u0001" + rounds)));
            lock (keys)
            {
                byte[] k; if (keys.TryGetValue(id, out k)) return k;
                using (var d = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, rounds, HashAlgorithmName.SHA256)) k = d.GetBytes(32);
                if (keys.Count > 8) keys.Clear();
                keys[id] = k; return k;
            }
        }
        public static byte[] SaltOf(byte[] b)
        {
            if (!SyncCore.IsEncrypted(b) || b.Length < Magic.Length + 4 + SaltSize) return null;
            var s = new byte[SaltSize]; Buffer.BlockCopy(b, Magic.Length + 4, s, 0, SaltSize); return s;
        }
        public static byte[] Seal(byte[] plain, string password, byte[] salt = null)
        {
            if (salt == null || salt.Length != SaltSize) { salt = new byte[SaltSize]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(salt); }
            var nonce = new byte[NonceSize]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(nonce);
            int rounds = Iterations;
            var head = new byte[Magic.Length + 4 + SaltSize];
            Buffer.BlockCopy(Magic, 0, head, 0, Magic.Length); Buffer.BlockCopy(BitConverter.GetBytes(rounds), 0, head, Magic.Length, 4); Buffer.BlockCopy(salt, 0, head, Magic.Length + 4, SaltSize);
            byte[] c, t; Gcm.Encrypt(Key(password, salt, rounds), nonce, plain, head, out c, out t);
            var o = new byte[head.Length + NonceSize + c.Length + TagSize];
            Buffer.BlockCopy(head, 0, o, 0, head.Length); Buffer.BlockCopy(nonce, 0, o, head.Length, NonceSize);
            Buffer.BlockCopy(c, 0, o, head.Length + NonceSize, c.Length); Buffer.BlockCopy(t, 0, o, head.Length + NonceSize + c.Length, TagSize);
            return o;
        }
        public static byte[] Open(byte[] b, string password)
        {
            int hl = Magic.Length + 4 + SaltSize;
            if (!SyncCore.IsEncrypted(b) || b.Length < hl + NonceSize + TagSize) throw new CryptographicException("not an encrypted sync file");
            int rounds = BitConverter.ToInt32(b, Magic.Length);
            if (rounds < 1000 || rounds > 10000000) throw new CryptographicException("bad rounds");
            var head = new byte[hl]; Buffer.BlockCopy(b, 0, head, 0, hl);
            var salt = new byte[SaltSize]; Buffer.BlockCopy(b, Magic.Length + 4, salt, 0, SaltSize);
            var nonce = new byte[NonceSize]; Buffer.BlockCopy(b, hl, nonce, 0, NonceSize);
            var c = new byte[b.Length - hl - NonceSize - TagSize]; Buffer.BlockCopy(b, hl + NonceSize, c, 0, c.Length);
            var t = new byte[TagSize]; Buffer.BlockCopy(b, b.Length - TagSize, t, 0, TagSize);
            return Gcm.Decrypt(Key(password, salt, rounds), nonce, c, t, head);
        }

        // the password on this PC: protected by Windows for this user (never in the sync folder, never in settings.json)
        static string PwPath { get { return Path.Combine(BrowserSettings.LocalDir, "sync-password.bin"); } }
        public static void SavePassword(string pw)
        {
            try
            {
                if (string.IsNullOrEmpty(pw)) { File.Delete(PwPath); return; }
                Directory.CreateDirectory(BrowserSettings.LocalDir);
                File.WriteAllBytes(PwPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(pw), Encoding.ASCII.GetBytes("HallowBrowserSync"), DataProtectionScope.CurrentUser));
            }
            catch { }
        }
        public static string LoadPassword()
        {
            try { return File.Exists(PwPath) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PwPath), Encoding.ASCII.GetBytes("HallowBrowserSync"), DataProtectionScope.CurrentUser)) : null; }
            catch { return null; }
        }
    }

    static class Gcm
    {
        [StructLayout(LayoutKind.Sequential)]
        struct AUTH_INFO { public int cbSize, dwInfoVersion; public IntPtr pbNonce; public int cbNonce; public IntPtr pbAuthData; public int cbAuthData; public IntPtr pbTag; public int cbTag; public IntPtr pbMacContext; public int cbMacContext; public int cbAAD; public long cbData; public int dwFlags; }
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)] static extern uint BCryptOpenAlgorithmProvider(out IntPtr alg, string id, string impl, uint flags);
        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)] static extern uint BCryptSetProperty(IntPtr h, string prop, byte[] input, int size, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptGenerateSymmetricKey(IntPtr alg, out IntPtr key, IntPtr obj, int objSize, byte[] secret, int secretSize, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptEncrypt(IntPtr key, byte[] input, int inSize, ref AUTH_INFO info, IntPtr iv, int ivSize, byte[] output, int outSize, out int result, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptDecrypt(IntPtr key, byte[] input, int inSize, ref AUTH_INFO info, IntPtr iv, int ivSize, byte[] output, int outSize, out int result, uint flags);
        [DllImport("bcrypt.dll")] static extern uint BCryptDestroyKey(IntPtr key);
        static IntPtr alg = IntPtr.Zero; static readonly object gate = new object();
        static IntPtr Alg()
        {
            lock (gate)
            {
                if (alg != IntPtr.Zero) return alg;
                IntPtr a; Check(BCryptOpenAlgorithmProvider(out a, "AES", null, 0));
                var mode = Encoding.Unicode.GetBytes("ChainingModeGCM\0"); Check(BCryptSetProperty(a, "ChainingMode", mode, mode.Length, 0));
                alg = a; return a;
            }
        }
        static void Check(uint st) { if (st != 0) throw new CryptographicException("CNG error 0x" + st.ToString("X8")); }
        static AUTH_INFO Info(GCHandle n, int nl, GCHandle a, int al, GCHandle t)
        {
            return new AUTH_INFO { cbSize = Marshal.SizeOf(typeof(AUTH_INFO)), dwInfoVersion = 1, pbNonce = n.AddrOfPinnedObject(), cbNonce = nl, pbAuthData = al > 0 ? a.AddrOfPinnedObject() : IntPtr.Zero, cbAuthData = al, pbTag = t.AddrOfPinnedObject(), cbTag = 16 };
        }
        public static void Encrypt(byte[] key, byte[] nonce, byte[] plain, byte[] aad, out byte[] cipher, out byte[] tag)
        {
            plain = plain ?? new byte[0]; aad = aad ?? new byte[0];
            IntPtr k; Check(BCryptGenerateSymmetricKey(Alg(), out k, IntPtr.Zero, 0, key, key.Length, 0));
            var t = new byte[16]; var c = new byte[plain.Length];
            GCHandle hn = GCHandle.Alloc(nonce, GCHandleType.Pinned), ha = GCHandle.Alloc(aad, GCHandleType.Pinned), ht = GCHandle.Alloc(t, GCHandleType.Pinned);
            try { var info = Info(hn, nonce.Length, ha, aad.Length, ht); int got; Check(BCryptEncrypt(k, plain, plain.Length, ref info, IntPtr.Zero, 0, c, c.Length, out got, 0)); }
            finally { hn.Free(); ha.Free(); ht.Free(); BCryptDestroyKey(k); }
            cipher = c; tag = t;
        }
        public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] cipher, byte[] tag, byte[] aad)
        {
            aad = aad ?? new byte[0];
            IntPtr k; Check(BCryptGenerateSymmetricKey(Alg(), out k, IntPtr.Zero, 0, key, key.Length, 0));
            var p = new byte[cipher.Length]; var t = (byte[])tag.Clone(); uint st;
            GCHandle hn = GCHandle.Alloc(nonce, GCHandleType.Pinned), ha = GCHandle.Alloc(aad, GCHandleType.Pinned), ht = GCHandle.Alloc(t, GCHandleType.Pinned);
            try { var info = Info(hn, nonce.Length, ha, aad.Length, ht); int got; st = BCryptDecrypt(k, cipher, cipher.Length, ref info, IntPtr.Zero, 0, p, p.Length, out got, 0); }
            finally { hn.Free(); ha.Free(); ht.Free(); BCryptDestroyKey(k); }
            if (st != 0) { Array.Clear(p, 0, p.Length); throw new CryptographicException("wrong password, or the file was changed"); }
            return p;
        }
    }
}
