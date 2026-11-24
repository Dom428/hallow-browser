// Hallow Browser 1.16 - unit tests: Chrome Web Store installs. CRX3 files are MADE HERE with throw-away test keys (RSA 2048 and
// ECDSA P-256) - nothing is downloaded: the signature and ID checks (a changed byte, another extension's file, no proof from the
// extension's own key, CRX2, junk), store links and IDs, the update answer, versions, the unpacking (nothing outside its folder,
// the store key put in the manifest so the ID stays), and what an extension asks for in words.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    // makes CRX3 files like the store's (tests only)
    public static class CrxMaker
    {
        static byte[] Len(int n) { if (n < 128) return new[] { (byte)n }; var b = new List<byte>(); while (n > 0) { b.Insert(0, (byte)(n & 0xff)); n >>= 8; } b.Insert(0, (byte)(0x80 | b.Count)); return b.ToArray(); }
        static byte[] Tlv(byte tag, params byte[][] parts) { var body = parts.SelectMany(x => x).ToArray(); return new[] { tag }.Concat(Len(body.Length)).Concat(body).ToArray(); }
        static byte[] Int(byte[] v) { var x = v.SkipWhile((b, i) => b == 0 && i < v.Length - 1).ToArray(); if ((x[0] & 0x80) != 0) x = new byte[] { 0 }.Concat(x).ToArray(); return Tlv(0x02, x); }
        public static byte[] RsaSpki(RSAParameters p)
        {
            var alg = Tlv(0x30, Tlv(0x06, new byte[] { 0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x01 }), new byte[] { 0x05, 0x00 });
            return Tlv(0x30, alg, Tlv(0x03, new byte[] { 0 }, Tlv(0x30, Int(p.Modulus), Int(p.Exponent))));
        }
        public static byte[] EcSpki(ECParameters p)
        {
            var alg = Tlv(0x30, Tlv(0x06, new byte[] { 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01 }), Tlv(0x06, new byte[] { 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07 }));
            return Tlv(0x30, alg, Tlv(0x03, new byte[] { 0, 4 }, p.Q.X, p.Q.Y));
        }
        static byte[] Varint(ulong v) { var b = new List<byte>(); do { var x = (byte)(v & 0x7f); v >>= 7; if (v != 0) x |= 0x80; b.Add(x); } while (v != 0); return b.ToArray(); }
        static byte[] Field(int f, byte[] v) { return Varint((ulong)f << 3 | 2).Concat(Varint((ulong)v.Length)).Concat(v).ToArray(); }
        public class Key { public RSA Rsa; public ECDsa Ec; public byte[] Spki; public string Id { get { return Crx.IdOfKey(Spki); } } }
        public static Key NewRsa() { RSA r = new RSACng(2048); return new Key { Rsa = r, Spki = RsaSpki(r.ExportParameters(false)) }; }
        public static Key NewEc() { var e = ECDsa.Create(ECCurve.NamedCurves.nistP256); return new Key { Ec = e, Spki = EcSpki(e.ExportParameters(false)) }; }
        static byte[] Sign(Key k, byte[] data)
        {
            if (k.Rsa != null) return k.Rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var rs = k.Ec.SignData(data, HashAlgorithmName.SHA256); return Tlv(0x30, Int(rs.Take(32).ToArray()), Int(rs.Skip(32).ToArray()));
        }
        // signedId: the id written into the signed header (default: the key's); extra: another proof (like the store's own)
        public static byte[] Build(byte[] zip, Key k, byte[] signedId = null, Key extra = null, bool flipZipByte = false)
        {
            byte[] id16; using (var sha = SHA256.Create()) id16 = signedId ?? sha.ComputeHash(k.Spki).Take(16).ToArray();
            var signed = Field(1, id16);
            var data = Encoding.ASCII.GetBytes("CRX3 SignedData\0").Concat(BitConverter.GetBytes((uint)signed.Length)).Concat(signed).Concat(zip).ToArray();
            var header = new List<byte>();
            foreach (var key in new[] { k, extra }.Where(x => x != null)) header.AddRange(Field(key.Rsa != null ? 2 : 3, Field(1, key.Spki).Concat(Field(2, Sign(key, data))).ToArray()));
            header.AddRange(Field(10000, signed));
            var z = (byte[])zip.Clone(); if (flipZipByte) z[z.Length / 2] ^= 0x5a;
            return Encoding.ASCII.GetBytes("Cr24").Concat(BitConverter.GetBytes(3u)).Concat(BitConverter.GetBytes((uint)header.Count)).Concat(header).Concat(z).ToArray();
        }
        public static byte[] Zip(Dictionary<string, string> files)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true)) foreach (var f in files) { var e = z.CreateEntry(f.Key); using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(f.Value); }
                return ms.ToArray();
            }
        }
        // a small harmless test extension: a content script that marks pages, a popup that reports which chrome.* APIs it has
        public static Dictionary<string, string> TestExtension(string version, string extraPerms = "")
        {
            return new Dictionary<string, string> {
                { "manifest.json", "{\"manifest_version\":3,\"name\":\"__MSG_appName__\",\"default_locale\":\"en\",\"version\":\"" + version + "\",\"description\":\"Hallow test extension (made up)\",\"permissions\":[\"storage\"" + extraPerms + "],\"action\":{\"default_popup\":\"popup.html\"}," +
                    "\"content_scripts\":[{\"matches\":[\"http://127.0.0.1/*\",\"http://*.localhost/*\"],\"js\":[\"cs.js\"],\"run_at\":\"document_end\"}],\"background\":{\"service_worker\":\"bg.js\"}}" },
                { "_locales/en/messages.json", "{\"appName\":{\"message\":\"Hallow Store Test\"}}" },
                { "cs.js", "document.documentElement.setAttribute('data-hallow-store','" + version + "');" },
                { "bg.js", "chrome.runtime.onInstalled.addListener(function(){chrome.storage.local.set({installed:Date.now()});});" },
                { "popup.html", "<!DOCTYPE html><html><head><title>Store test popup</title></head><body>Store test popup " + version + "<script src=\"popup.js\"></script></body></html>" },
                { "popup.js", "window.apis={runtime:!!chrome.runtime,storage:!!(chrome.storage&&chrome.storage.local),tabs:!!chrome.tabs,native:!!(chrome.runtime&&chrome.runtime.sendNativeMessage),identity:!!chrome.identity,sidePanel:!!chrome.sidePanel,alarms:!!chrome.alarms,dnr:!!chrome.declarativeNetRequest,scripting:!!chrome.scripting};try{chrome.runtime.sendNativeMessage('com.hallow.none',{},function(){window.apis.nativeErr=chrome.runtime.lastError?chrome.runtime.lastError.message:'answered';});}catch(e){window.apis.nativeErr='throws: '+e.message;}" } };
        }
    }

    public static class V116
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }
        public static void All() { CrxTests(); LinkTests(); UnpackTests(); }

        static void CrxTests()
        {
            var zip = CrxMaker.Zip(CrxMaker.TestExtension("1.0"));
            var k = CrxMaker.NewRsa(); var store = CrxMaker.NewRsa();
            var good = CrxMaker.Build(zip, k, null, store);
            var r = Crx.Parse(good, k.Id);
            Ok(r.Error == null && r.Id == k.Id && r.DeveloperSigned && r.Proofs == 2 && r.OtherSigned == 1 && r.PublicKey.SequenceEqual(k.Spki), "store 1.16: a CRX3 with the extension's own RSA proof (and a second, store-like one) checks out (" + r.Error + ")");
            Ok(Crx.IdRx.IsMatch(k.Id), "store 1.16: the ID is 32 letters a-p (SHA-256 of the key, like Chromium)");
            Ok(Crx.Parse(CrxMaker.Build(zip, k, null, null, true), k.Id).Error.Contains("signature"), "store 1.16: one changed byte in the zip = refused (signature)");
            var other = CrxMaker.NewRsa();
            Ok((Crx.Parse(good, other.Id).Error ?? "").Contains("not " + other.Id), "store 1.16: another extension's file for this ID = refused");
            byte[] fake; using (var sha = SHA256.Create()) fake = sha.ComputeHash(other.Spki).Take(16).ToArray();
            Ok(Crx.Parse(CrxMaker.Build(zip, k, fake), other.Id).Error != null, "store 1.16: a file that claims another ID but isn't signed by that ID's key = refused");
            var ec = CrxMaker.NewEc(); var rec = Crx.Parse(CrxMaker.Build(zip, ec), ec.Id);
            Ok(rec.Error == null && rec.DeveloperSigned, "store 1.16: an ECDSA P-256 proof checks out too (" + rec.Error + ")");
            Ok(Crx.Parse(CrxMaker.Build(zip, ec, null, null, true), ec.Id).Error != null, "store 1.16: ECDSA - a changed byte = refused");
            var crx2 = (byte[])good.Clone(); crx2[4] = 2;
            Ok(Crx.Parse(crx2, k.Id).Error.Contains("CRX3"), "store 1.16: CRX2 (old, weaker format) = refused");
            Ok(Crx.Parse(Encoding.ASCII.GetBytes("<html>not found</html>"), k.Id).Error != null && Crx.Parse(good.Take(40).ToArray(), k.Id).Error != null && Crx.Parse(null, k.Id).Error != null, "store 1.16: junk, a cut file, nothing = refused, no crash");
        }

        static void LinkTests()
        {
            var id = "fooolghllnmhmmndgjiamiiodkpenpbb";
            Eq(WebStore.IdFrom("https://chromewebstore.google.com/detail/nordpass-password-manager/" + id), id, "store 1.16: a store link -> its ID");
            Eq(WebStore.IdFrom("https://chromewebstore.google.com/detail/" + id + "?hl=en"), id, "store 1.16: a short store link with ?hl -> its ID");
            Eq(WebStore.IdFrom("https://chrome.google.com/webstore/detail/x/" + id), id, "store 1.16: the old store address -> its ID");
            Eq(WebStore.IdFrom("  " + id + " "), id, "store 1.16: a bare ID");
            Ok(WebStore.IdFrom("https://evil.example/detail/x/" + id) == null && WebStore.IdFrom("https://chromewebstore.google.com.evil.example/detail/x/" + id) == null && WebStore.IdFrom("abc") == null && WebStore.IdFrom("javascript:alert(1)") == null, "store 1.16: other sites, look-alike hosts and junk aren't store links");
            var u = WebStore.DownloadUrl(id);
            Ok(u.Contains("response=redirect") && u.Contains("acceptformat=crx3") && u.Contains("id%3D" + id) && !u.Contains("&uid") && u.StartsWith(WebStore.Base), "store 1.16: the request is the update endpoint's own (the ID and the engine's version only)");
            Ok(WebStore.Endpoint == "https://clients2.google.com/service/update2/crx", "store 1.16: Google's official endpoint");
            var xml = "<?xml version=\"1.0\"?><gupdate xmlns=\"http://www.google.com/update2/response\" protocol=\"2.0\"><app appid=\"" + id + "\" status=\"ok\"><updatecheck codebase=\"https://clients2.googleusercontent.com/crx/x.crx\" version=\"5.12.3\" status=\"ok\"/></app></gupdate>";
            Eq(WebStore.ParseUpdate(xml, id), "5.12.3", "store 1.16: the update answer's version");
            Ok(WebStore.ParseUpdate(xml.Replace("status=\"ok\"/", "status=\"noupdate\"/"), id) == null && WebStore.ParseUpdate(xml, "a" + id.Substring(1)) == null && WebStore.ParseUpdate("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><x>&e;</x>", id) == null, "store 1.16: no update, another app, a hostile document = nothing");
            Ok(WebStore.Newer("1.10", "1.9") && !WebStore.Newer("1.2", "1.2.0") && WebStore.Newer("2.0.0.1", "2") && !WebStore.Newer("1.0", "1.1"), "store 1.16: versions compared numerically");
        }

        static void UnpackTests()
        {
            var k = CrxMaker.NewRsa();
            var c = Crx.Parse(CrxMaker.Build(CrxMaker.Zip(CrxMaker.TestExtension("1.0")), k), k.Id);
            var folder = WebStore.Unpack(c, "Hallow Store Test");
            var man = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            Ok(folder.StartsWith(ExtStore.Dir) && File.Exists(Path.Combine(folder, "popup.js")) && File.Exists(Path.Combine(folder, "_locales", "en", "messages.json")), "store 1.16: unpacked into the browser's Extensions folder");
            Ok((string)man["key"] == Convert.ToBase64String(k.Spki) && Crx.IdOfKey(Convert.FromBase64String((string)man["key"])) == k.Id, "store 1.16: the store key goes into the manifest, so the extension keeps its store ID");
            string err; var info = ExtStore.Read(folder, out err);
            Ok(info != null && info.Name == "Hallow Store Test" && info.Version == "1.0" && info.Popup == "popup.html", "store 1.16: its name (from _locales), version and popup are read");
            var bad = CrxMaker.Zip(new Dictionary<string, string> { { "manifest.json", "{\"manifest_version\":3,\"name\":\"x\",\"version\":\"1\"}" }, { "../../evil.txt", "x" } });
            var cb = Crx.Parse(CrxMaker.Build(bad, k), k.Id); string ex = null;
            try { WebStore.Unpack(cb, "evil"); } catch (Exception e) { ex = e.Message; }
            Ok(ex != null && !File.Exists(Path.Combine(ExtStore.Dir, "..", "evil.txt")) && !Directory.GetDirectories(ExtStore.Dir).Any(d => Path.GetFileName(d).StartsWith("evil-")), "store 1.16: a file pointing outside the folder = nothing unpacked at all (" + ex + ")");
            var asks = WebStore.Asks(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"permissions\":[\"storage\",\"tabs\",\"nativeMessaging\",\"<all_urls>\"],\"optional_permissions\":[\"history\"],\"content_scripts\":[{\"matches\":[\"https://*/*\"]}]}"));
            Ok(asks.Contains("Read and change everything on every website you open") && asks.Contains("See your open tabs' addresses and titles") && asks.Contains("Talk to a desktop app on this PC") && asks.Any(a => a.StartsWith("May ask later")), "store 1.16: what it asks for, in words (" + string.Join("; ", asks) + ")");
            var few = WebStore.Asks(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"host_permissions\":[\"https://*.example.com/*\"]}"));
            Ok(few.Count == 1 && few[0].Contains("*.example.com"), "store 1.16: named sites are listed");
            var gaps = WebStore.Gaps(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"permissions\":[\"nativeMessaging\",\"storage\"],\"optional_permissions\":[\"identity\"]}"));
            Ok(gaps.Count == 2 && gaps.Any(g => g.StartsWith("native messaging")) && gaps.Any(g => g.Contains("chrome.identity")), "store 1.16: what won't work in WebView2 is said before adding");
            try { Directory.Delete(folder, true); } catch { }
        }
    }
}
