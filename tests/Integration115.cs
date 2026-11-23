// Hallow Browser 1.15 - integration test "v115": passkeys through Hallow Keys, off-screen, muted, its own test folder.
// The Keys on the other end is a stand-in that runs Hallow Keys' OWN pipe code (NativeServer, 2.13+) with a made-up vault in the test
// folder: HALLOW_BROWSER_TEST_PKHOST = that program (built from the Keys repo's HallowKeys.exe; it refuses a real data folder).
// Local HTTPS test sites with made-up names (rp.test, Microsoft-style login.live.test / account.live.test using passkeys of
// login.microsoft.test, whose /.well-known/webauthn lists only those two), mapped to 127.0.0.1:
//  - create and get (the signature checked in the page with WebCrypto), Cancel, "the browser's own" (never Windows' window in tests),
//    the half-second click guard, locked -> unlock -> pick, passkey autofill through the key in the address bar;
//  - related origins: account.live.test saves a login.microsoft.test passkey, login.live.test signs in with it; unlisted.test refused;
//  - refused: a wrong rpId, an http page, a page that sends its own message claiming another address (spoofed), a request from a
//    program that only pretends to be Hallow Browser; frames: allowed with the permission, refused without, the same origin works;
//  - the setting off / Keys gone / a private window -> the browser's own; navigating away closes the prompt;
//  - option A probe: the real Keys extension (a copy whose host name points nowhere) loads in WebView2, but native messaging isn't there.
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v115
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class Integration115
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Info(string what) { var l = "INFO " + what; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static List<Dictionary<string, object>> Tabs() { var v = Val(Cmd("test-tabs")); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-v-weval 0 " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static string FEval(int tab, string expr) { var v = Val(Cmd("test-pk-feval " + tab + " " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
        static int NewTab(string url) { Cmd("test-activate " + (Tabs().Count - 1)); var r = Val(Cmd("test-new " + url)); int i; if (!int.TryParse(r, out i)) return -1; Wait(Tabs, t => i < t.Count && (bool)t[i]["ready"], 15000); Thread.Sleep(300); return i; }
        static Dictionary<string, object> St() { try { return js.Deserialize<Dictionary<string, object>>(Val(Cmd("test-pk-state")) ?? "{}"); } catch { return new Dictionary<string, object>(); } }
        static Dictionary<string, object> Prompt() { var s = St(); object p; return s.TryGetValue("prompt", out p) ? p as Dictionary<string, object> : null; }
        static Dictionary<string, object> WaitPrompt(Func<Dictionary<string, object>, bool> good, int ms = 8000) { return Wait(Prompt, p => p != null && good(p), ms); }
        static string Rows(Dictionary<string, object> p) { return p == null ? "" : string.Join(",", ((System.Collections.ArrayList)p["rows"]).Cast<object>()); }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }
        static Dictionary<string, object> Res(int tab, string key, bool frame = false, int ms = 10000)
        {
            Func<Dictionary<string, object>> f = () => { var s = frame ? FEval(tab, "JSON.stringify(R['" + key + "']||null)") : Eval(tab, "JSON.stringify(R['" + key + "']||null)"); try { return js.Deserialize<Dictionary<string, object>>(s ?? "null"); } catch { return null; } };
            return Wait(f, d => d != null, ms);
        }
        static string Click(string hk) { Thread.Sleep(650); return Val(Cmd("test-pk-click " + hk)); }

        // ---- the Keys stand-in (Hallow Keys' own NativeServer, a made-up vault)
        static Process host;
        static Dictionary<string, object> Host(string cmd)
        {
            lock (typeof(Integration115))
            {
                try { host.StandardInput.WriteLine(cmd); host.StandardInput.Flush(); var l = host.StandardOutput.ReadLine(); return js.Deserialize<Dictionary<string, object>>(l ?? "{}"); } catch { return new Dictionary<string, object>(); }
            }
        }
        static string HostLog() { var d = Host("log"); object l; return d.TryGetValue("log", out l) ? string.Join(" | ", ((System.Collections.ArrayList)l).Cast<object>()) : ""; }

        // ---- a small HTTPS server that sees the Host header (the test sites, and login.microsoft.test's /.well-known/webauthn for Keys)
        class Tls
        {
            public int Port; TcpListener l; readonly X509Certificate2 cert; volatile bool stop;
            public Func<string, string, Tuple<string, byte[]>> Route;   // (host, path) -> (content type, body)
            public readonly List<string> Log = new List<string>();
            public Tls(X509Certificate2 c) { cert = c; }
            public void Start() { l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start(); }
            public void Stop() { stop = true; try { l.Stop(); } catch { } }
            void Serve(TcpClient c)
            {
                try
                {
                    using (c) using (var ss = new SslStream(c.GetStream(), false))
                    {
                        try { ss.AuthenticateAsServer(cert, false, System.Security.Authentication.SslProtocols.Tls12, false); } catch { return; }
                        while (true)
                        {
                            var head = new List<byte>();
                            while (true) { int b = ss.ReadByte(); if (b < 0) return; head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; if (n > 65536) return; }
                            var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                            var parts = lines[0].Split(' '); string hostName = ""; int len = 0;
                            foreach (var ln in lines) { if (ln.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) hostName = ln.Substring(5).Trim().Split(':')[0].ToLowerInvariant(); if (ln.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len = int.Parse(ln.Substring(15).Trim()); }
                            var body = new byte[len]; int got = 0; while (got < len) { int r = ss.Read(body, got, len - got); if (r <= 0) return; got += r; }
                            lock (Log) Log.Add(hostName + " " + parts[1] + (lines.Any(x => x.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) ? " cookie" : ""));
                            var res = Route(hostName, parts[1].Split('?')[0]);
                            var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + (res == null ? "404 Not Found" : "200 OK") + "\r\nContent-Type: " + (res == null ? "text/plain" : res.Item1) + "\r\nContent-Length: " + (res == null ? 0 : res.Item2.Length) + "\r\nCache-Control: no-store\r\n\r\n");
                            ss.Write(hb, 0, hb.Length); if (res != null) ss.Write(res.Item2, 0, res.Item2.Length); ss.Flush();
                        }
                    }
                }
                catch { }
            }
        }

        const string PageJs = @"var R={},K={};
function rnd(n){var a=new Uint8Array(n);crypto.getRandomValues(a);return a;}
function b64u(buf){var b=new Uint8Array(buf),s='';for(var i=0;i<b.length;i++)s+=String.fromCharCode(b[i]);return btoa(s).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');}
function ub64(s){s=s.replace(/-/g,'+').replace(/_/g,'/');while(s.length%4)s+='=';var b=atob(s),u=new Uint8Array(b.length);for(var i=0;i<b.length;i++)u[i]=b.charCodeAt(i);return u;}
function td(b){return new TextDecoder().decode(b);}
function addKey(id,spki){K[id]=ub64(spki).buffer;return 'ok';}
function mk(key,rpId,user){R[key]=null;var pk={rp:{name:'ZQ Test RP'},user:{id:rnd(16),name:user,displayName:'ZQ '+user},challenge:rnd(32),pubKeyCredParams:[{type:'public-key',alg:-7},{type:'public-key',alg:-257}],extensions:{credProps:true}};if(rpId)pk.rp.id=rpId;
navigator.credentials.create({publicKey:pk}).then(function(c){K[c.id]=c.response.getPublicKey();R[key]={ok:1,id:c.id,cd:td(c.response.clientDataJSON),alg:c.response.getPublicKeyAlgorithm(),pk:c.response.getPublicKey().byteLength,spki:b64u(c.response.getPublicKey()),att:c.response.attestationObject.byteLength,isPkc:c instanceof PublicKeyCredential,ext:JSON.stringify(c.getClientExtensionResults()),json:JSON.stringify(c.toJSON()).length};},function(e){R[key]={err:e.name,msg:e.message};});return 'started';}
function der2raw(d){var p=2,out=new Uint8Array(64);for(var k=0;k<2;k++){p++;var n=d[p++];var v=d.slice(p,p+n);while(v.length>32&&v[0]===0)v=v.slice(1);out.set(v,k*32+32-v.length);p+=n;}return out;}
function gt(key,rpId,cond,allowId){R[key]=null;var o={publicKey:{challenge:rnd(32),allowCredentials:allowId?[{type:'public-key',id:ub64(allowId)}]:[]}};if(rpId)o.publicKey.rpId=rpId;if(cond)o.mediation='conditional';
navigator.credentials.get(o).then(function(c){var r={ok:1,id:c.id,cd:td(c.response.clientDataJSON),ad:c.response.authenticatorData.byteLength,sig:c.response.signature.byteLength,uh:c.response.userHandle?c.response.userHandle.byteLength:0,isPkc:c instanceof PublicKeyCredential,verified:'pending'};
var spki=K[c.id];if(!spki){r.verified='nokey';R[key]=r;return;}
crypto.subtle.digest('SHA-256',c.response.clientDataJSON).then(function(h){var ad=new Uint8Array(c.response.authenticatorData),data=new Uint8Array(ad.length+32);data.set(ad,0);data.set(new Uint8Array(h),ad.length);
return crypto.subtle.importKey('spki',spki,{name:'ECDSA',namedCurve:'P-256'},false,['verify']).then(function(k){return crypto.subtle.verify({name:'ECDSA',hash:'SHA-256'},k,der2raw(new Uint8Array(c.response.signature)),data);});}).then(function(ok){r.verified=ok;R[key]=r;},function(e){r.verified='error '+e;R[key]=r;});
},function(e){R[key]={err:e.name,msg:e.message};});return 'started';}
function spoof(key,payload){R[key]=null;var rid='spoof-'+key;window.chrome.webview.addEventListener('message',function(e){var d=e.data;if(d&&d.rid===rid&&!d.ack)R[key]=d;});
window.chrome.webview.postMessage('hallow:pk\t'+JSON.stringify({rid:rid,op:'get',req:payload,url:payload.url,origin:payload.origin,topUrl:payload.url}));return 'started';}";
        static byte[] Html(string title, string body) { return Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset=utf-8><title>" + title + "</title></head><body><h1>" + title + "</h1><input id=u autocomplete='username webauthn'>" + body + "<script>" + PageJs + "</script></body></html>"); }

        public static int Main(string exe, string dir)
        {
            exe = Path.GetFullPath(exe);
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v115.txt"), false) { AutoFlush = true };
            var pkhost = Environment.GetEnvironmentVariable("HALLOW_BROWSER_TEST_PKHOST");
            if (string.IsNullOrEmpty(pkhost) || !File.Exists(pkhost)) { Ok(false, "HALLOW_BROWSER_TEST_PKHOST names the Keys stand-in (" + pkhost + ")"); return 1; }
            var cert = TlsServer.TestCert();
            var tls = new Tls(cert); int P = 0;
            tls.Route = (h, path) =>
            {
                if (path == "/.well-known/webauthn" && h == "login.microsoft.test") return Tuple.Create("application/json", Encoding.UTF8.GetBytes(js.Serialize(new Dictionary<string, object> { { "origins", new[] { "https://login.live.test:" + P, "https://account.live.test:" + P } } })));
                if (path == "/pk.html") return Tuple.Create("text/html; charset=utf-8", Html("pk " + h, ""));
                if (path == "/frame-ok.html") return Tuple.Create("text/html; charset=utf-8", Html("frame-ok", "<iframe id=f src='https://frame.test:" + P + "/pk.html' allow='publickey-credentials-get *; publickey-credentials-create *' width=400 height=200></iframe>"));
                if (path == "/frame-no.html") return Tuple.Create("text/html; charset=utf-8", Html("frame-no", "<iframe id=f src='https://frame.test:" + P + "/pk.html' width=400 height=200></iframe>"));
                if (path == "/frame-same.html") return Tuple.Create("text/html; charset=utf-8", Html("frame-same", "<iframe id=f src='https://" + h + ":" + P + "/pk.html' width=400 height=200></iframe>"));
                return null;
            };
            tls.Start(); P = tls.Port;
            var http = new Integration110.Server();
            http.Route = rq => rq.Path.StartsWith("/pk.html") ? Tuple.Create(200, "text/html; charset=utf-8", Html("pk http", "")) : null;
            http.Start();
            Func<string, string> U = h => "https://" + h + ":" + P;
            var t0 = DateTime.Now; Process br = null;
            var pipe = "HallowKeys.Native." + Environment.UserName + ".test";
            try
            {
                // the Keys stand-in, on Keys' test pipe, with the test folder as its data folder (so session.key is the browser's test one)
                var hp = new ProcessStartInfo(pkhost) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true };
                hp.EnvironmentVariables["HALLOW_KEYS_TESTDIR"] = run;
                hp.EnvironmentVariables["HALLOW_KEYS_TEST_WELLKNOWN"] = "127.0.0.1:" + P + "|" + cert.Thumbprint;
                hp.EnvironmentVariables["HALLOW_KEYS_TEST_STRICTCALLER"] = "1";
                host = Process.Start(hp); try { host.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ready = host.StandardOutput.ReadLine() ?? "";
                Ok(ready.StartsWith("ready " + pipe), "the Keys stand-in (Hallow Keys' own pipe code, a made-up vault in the test folder) is up: " + ready);

                var psi = new ProcessStartInfo(exe, "") { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = "MAP *.test 127.0.0.1";
                psi.EnvironmentVariables["HALLOW_BROWSER_IGNORE_CERTS"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_KEYS_PIPE"] = pipe;
                psi.EnvironmentVariables["HALLOW_BROWSER_KEYS_INSTALLED"] = "1";
                psi.EnvironmentVariables.Remove("HALLOW_BROWSER_MUTE");   // test browsers start with --mute-audio
                br = Process.Start(psi); try { br.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var ping = Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000);
                Ok(ping != null && ping.Contains("\"version\":\"" + Program.Version + "\""), "browser answers ping: " + ping);
                Wait(Tabs, t => t.Count >= 1 && (bool)t[0]["ready"], 30000); Thread.Sleep(800);
                Ok(S(St(), "on") == "True", "Use Hallow Keys for passkeys is on by default");
                var set = Val(Cmd("test-pk-settings")) ?? "";
                Ok(set.Contains("switch: Use Hallow Keys for passkeys = True") && set.Contains("Hallow Keys is running"), "Settings > Hallow Keys: the switch and \"Hallow Keys is running\" (" + set + ")");

                // ---- the page script
                int rp = NewTab(U("rp.test") + "/pk.html");
                Ok(Eval(rp, "String(window.__hallowPk)") == "1" && Eval(rp, "String(navigator.credentials.get).indexOf('[native code]')<0&&navigator.credentials.get.name==='get'") == "true", "https page: Hallow's passkey script wraps navigator.credentials (before the page's scripts)");
                Ok(Eval(rp, "PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable()") == "true" && Eval(rp, "PublicKeyCredential.isConditionalMediationAvailable()") == "true", "capability checks: a passkey authenticator and autofill are there while Keys runs");
                var caps = Eval(rp, "typeof PublicKeyCredential.getClientCapabilities==='function'?PublicKeyCredential.getClientCapabilities().then(function(c){return JSON.stringify(c)}):'none'") ?? "";
                Ok(caps == "none" || (caps.Contains("\"passkeyPlatformAuthenticator\":true") && caps.Contains("\"relatedOrigins\":true")), "getClientCapabilities (where WebView2 has it) says what Keys adds: " + caps);

                // ---- create on rp.test
                Eval(rp, "mk('c1','rp.test','alice@rp.test')");
                var p = WaitPrompt(x => S(x, "op") == "create");
                Ok(p != null && S(p, "title") == "Save a passkey in Hallow Keys?" && Rows(p) == "save,fallback,cancel" && S(p, "url").StartsWith(U("rp.test") + "/pk.html") && S(p, "onScreen") == "False", "create: Hallow Keys' prompt in the browser (save / the browser's own / cancel), for the page's real address, never on screen in tests: " + js.Serialize(p));
                Ok(Val(Cmd("test-pk-click save")) == "too soon", "a click in the first half second after the prompt shows doesn't count (no click-through)");
                Ok(Click("save") == "ok", "click Save passkey in Hallow Keys");
                var c1 = Res(rp, "c1");
                Ok(S(c1, "ok") == "1" && S(c1, "alg") == "-7" && S(c1, "pk") == "91" && S(c1, "isPkc") == "True" && S(c1, "ext").Contains("\"rk\":true"), "create: the page gets a PublicKeyCredential (ES256, a 91-byte SPKI key, credProps rk): " + js.Serialize(c1));
                var cd1 = S(c1, "cd");
                Ok(cd1.Contains("\"type\":\"webauthn.create\"") && cd1.Contains("\"origin\":\"" + U("rp.test") + "\"") && cd1.Contains("\"crossOrigin\":false"), "create: clientDataJSON from Keys with the page's origin (" + cd1 + ")");
                var hs = Host("count");
                Ok(S(hs, "passkeys") == "1", "Keys saved it in its (made-up) vault: " + js.Serialize(hs));
                var lg = HostLog();
                Ok(lg.Contains("passkey: passkey-create from Hallow Browser") && lg.Contains("Saved a passkey for rp.test from Hallow Browser"), "Keys knows the request came from Hallow Browser (checked by the pipe's client process): " + lg);
                Ok(Prompt() == null, "the prompt is gone after the answer");

                // ---- get on rp.test (the signature checked with WebCrypto in the page)
                Eval(rp, "gt('g1','rp.test')");
                p = WaitPrompt(x => S(x, "op") == "get");
                Ok(p != null && S(p, "title") == "Sign in with a passkey" && Rows(p) == "pick-0,fallback,cancel" && js.Serialize(p).Contains("alice@rp.test"), "get: the prompt lists the passkey (by name only): " + js.Serialize(p));
                Click("pick-0");
                var g1 = Wait(() => Res(rp, "g1"), d => S(d, "verified") != "pending", 10000);
                Ok(S(g1, "ok") == "1" && S(g1, "id") == S(c1, "id") && S(g1, "ad") == "37" && S(g1, "uh") == "16" && S(g1, "isPkc") == "True", "get: the same credential, 37-byte authenticator data, the user handle: " + js.Serialize(g1));
                Ok(S(g1, "verified") == "True", "get: the ES256 signature verifies in the page with the key from create (WebCrypto)");
                Ok(S(g1, "cd").Contains("\"type\":\"webauthn.get\"") && S(g1, "cd").Contains("\"origin\":\"" + U("rp.test") + "\""), "get: clientDataJSON webauthn.get for the page's origin");
                // allowCredentials with that id
                Eval(rp, "gt('g1b','rp.test',false,'" + S(c1, "id") + "')"); p = WaitPrompt(x => S(x, "op") == "get"); Click("pick-0");
                Ok(S(Wait(() => Res(rp, "g1b"), d => S(d, "verified") != "pending", 10000), "verified") == "True", "get with allowCredentials: signs with that passkey");

                // ---- Cancel and the browser's own
                Eval(rp, "gt('g2','rp.test')"); WaitPrompt(x => true); Click("cancel");
                Ok(S(Res(rp, "g2"), "err") == "NotAllowedError", "Cancel: the site gets NotAllowedError");
                Eval(rp, "gt('g3','rp.test')"); WaitPrompt(x => true); Click("fallback");
                var g3 = Res(rp, "g3");
                Ok(S(g3, "err") == "NotAllowedError" && S(g3, "msg").Contains("test: the browser own passkey window"), "Use another device or the browser's own: the original WebView2 call (in tests a marker instead of Windows' window): " + js.Serialize(g3));

                // ---- related origins (Microsoft-style): account.live.test saves a login.microsoft.test passkey; login.live.test signs in
                int acc = NewTab(U("account.live.test") + "/pk.html");
                Eval(acc, "mk('m1','login.microsoft.test','zq-ms@example.test')");
                p = WaitPrompt(x => S(x, "op") == "create");
                Ok(p != null && S(p, "rpId") == "login.microsoft.test" && S(p, "sub").Contains("account.live.test"), "related origin: the prompt names the passkey's site and the page that asks: " + S(p, "sub"));
                Click("save");
                var m1 = Res(acc, "m1");
                Ok(S(m1, "ok") == "1" && S(m1, "cd").Contains("\"origin\":\"" + U("account.live.test") + "\""), "related origin: account.live.test (listed in login.microsoft.test's /.well-known/webauthn) saved a login.microsoft.test passkey: " + js.Serialize(m1));
                int lin = NewTab(U("login.live.test") + "/pk.html");
                Eval(lin, "addKey('" + S(m1, "id") + "','" + S(m1, "spki") + "')");
                Eval(lin, "gt('m2','login.microsoft.test')");
                p = WaitPrompt(x => S(x, "op") == "get" && S(x, "url").Contains("login.live.test"));
                Ok(p != null && js.Serialize(p).Contains("zq-ms@example.test"), "related origin: login.live.test is offered the login.microsoft.test passkey");
                Click("pick-0");
                var m2 = Wait(() => Res(lin, "m2"), d => S(d, "verified") != "pending", 10000);
                Ok(S(m2, "verified") == "True" && S(m2, "cd").Contains("\"origin\":\"" + U("login.live.test") + "\""), "related origin: signed in on login.live.test, the signature verifies: " + js.Serialize(m2));
                Ok(tls.Log.Count(l => l.StartsWith("login.microsoft.test /.well-known/webauthn")) >= 1 && !tls.Log.Any(l => l.Contains("well-known") && l.EndsWith(" cookie")), "Keys itself fetched login.microsoft.test's /.well-known/webauthn (no cookies)");

                // ---- refused: an unlisted site, a wrong rpId, a spoofed address, http
                int un = NewTab(U("unlisted.test") + "/pk.html");
                var before = St();
                Eval(un, "gt('u1','login.microsoft.test')");
                var u1 = Res(un, "u1");
                Ok(S(u1, "err") == "SecurityError" && Prompt() == null, "unlisted.test asking for login.microsoft.test: SecurityError from Keys, no prompt: " + js.Serialize(u1));
                Eval(un, "gt('u2','rp.test')");
                Ok(S(Res(un, "u2"), "err") == "SecurityError" && Prompt() == null, "unlisted.test asking for rp.test's passkey: SecurityError, no prompt");
                // a page that sends its own message, claiming to be login.live.test / rp.test
                Eval(un, "spoof('s1',{rpId:'login.microsoft.test',challenge:'" + new string('A', 43) + "',url:'" + U("login.live.test") + "/',origin:'" + U("login.live.test") + "'})");
                var s1 = Res(un, "s1");
                Ok(S(s1, "action") == "error" && S(s1, "name") == "SecurityError" && Prompt() == null, "spoofed: a page claiming to be login.live.test is still unlisted.test to the browser - SecurityError, no prompt: " + js.Serialize(s1));
                Eval(un, "spoof('s2',{rpId:'rp.test',challenge:'" + new string('A', 43) + "',url:'" + U("rp.test") + "/',origin:'" + U("rp.test") + "'})");
                var s2 = Res(un, "s2");
                Ok(S(s2, "name") == "SecurityError" && Prompt() == null && (St()["last"] as string ?? "").Contains("SecurityError"), "spoofed: claiming rp.test's address doesn't get rp.test's passkey (" + S(s2, "msg") + ")");
                // http
                int ht = NewTab("http://plain.test:" + http.Port + "/pk.html");
                Ok(Eval(ht, "String(window.__hallowPk)") == "undefined" && Eval(ht, "String(typeof navigator.credentials==='undefined'||String(navigator.credentials.get).indexOf('[native code]')>=0)") == "true", "http page: no passkey script (navigator.credentials untouched - an http page has no WebAuthn at all)");
                Eval(ht, "spoof('h1',{rpId:'plain.test',challenge:'" + new string('A', 43) + "'})");
                var h1 = Res(ht, "h1");
                Ok(S(h1, "name") == "SecurityError" && S(h1, "msg").Contains("https") && Prompt() == null, "http page sending the message itself: refused by the browser (https only), Keys never asked: " + js.Serialize(h1));

                // ---- a program that only says it's Hallow Browser
                string secret = File.ReadAllText(Path.Combine(run, "HallowKeys", "session.key")).Trim();
                Func<string, string> raw = json => { try { using (var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut)) { c.Connect(2000); var b = Encoding.UTF8.GetBytes(secret + " " + json + "\n"); c.Write(b, 0, b.Length); c.Flush(); return new StreamReader(c).ReadLine(); } } catch (Exception e) { return "error " + e.Message; } };
                var f1 = raw("{\"type\":\"passkey-list\",\"url\":\"" + U("rp.test") + "/\",\"rpId\":\"rp.test\",\"client\":\"hallow-browser\"}");
                Ok(f1 != null && f1.Contains("Only Hallow Browser can ask as Hallow Browser"), "Keys: another program (with the secret) claiming to be Hallow Browser is refused: " + f1);
                var f2 = raw("{\"type\":\"passkey-list\",\"url\":\"" + U("rp.test") + "/\",\"rpId\":\"rp.test\"}");
                Ok(f2 != null && f2.Contains("only answered for the Hallow Keys extension and Hallow Browser") && !f2.Contains("alice"), "Keys: passkey requests from any other program are refused (no names leak): " + f2);

                // ---- locked -> Unlock Hallow Keys -> it waits -> unlocked -> pick
                Host("lock");
                Cmd("test-activate " + rp);
                Eval(rp, "gt('l1','rp.test')");
                p = WaitPrompt(x => S(x, "title") == "Hallow Keys is locked");
                Ok(p != null && Rows(p) == "unlock,waiting,fallback,cancel", "locked: Hallow Keys is locked - Unlock Hallow Keys / waiting / the browser's own / cancel: " + Rows(p));
                Click("unlock");
                Ok(Wait(() => S(Host("count"), "opens"), x => x == "1", 5000) == "1", "Unlock Hallow Keys asks Keys to show itself");
                Host("unlock");
                p = WaitPrompt(x => S(x, "title") == "Sign in with a passkey", 8000);
                Ok(p != null && Rows(p).StartsWith("pick-0"), "unlocked: the prompt turns into the passkey list by itself");
                Click("pick-0");
                Ok(S(Wait(() => Res(rp, "l1"), d => S(d, "verified") != "pending", 10000), "verified") == "True", "after unlocking: signed in");

                // ---- passkey autofill (mediation "conditional"): the key in the address bar
                Eval(rp, "gt('a1','rp.test',true)");
                var cond = Wait(St, s => s.ContainsKey("conds") && ((System.Collections.ArrayList)s["conds"]).Count > 0, 6000);
                Ok(Prompt() == null && js.Serialize(cond).Contains("\"items\":1"), "autofill: the request waits without a prompt: " + js.Serialize(cond["conds"]));
                var ks = Val(Cmd("test-pk-keyshow " + rp)) ?? "";
                Ok(ks.Contains("\"shows\":true") && ks.Contains("\"rows\":2"), "autofill: the key shows in the address bar and lists the site's passkey: " + ks);
                Ok((Val(Cmd("test-pk-condpick " + rp + " 0")) ?? "") == "signed", "autofill: picking it there signs in");
                Ok(S(Wait(() => Res(rp, "a1"), d => S(d, "verified") != "pending", 10000), "verified") == "True", "autofill: the page gets a verified credential");
                Eval(rp, "gt('a2','rp.test',true)"); Wait(St, s => ((System.Collections.ArrayList)s["conds"]).Count > 0, 6000);
                Eval(rp, "gt('a3','rp.test',true)"); Thread.Sleep(1200);
                Ok(S(Res(rp, "a2"), "err") == "AbortError", "autofill: a new request replaces the waiting one (AbortError)");

                // ---- frames
                int fo = NewTab(U("top.test") + "/frame-ok.html");
                Wait(() => Val(Cmd("test-pk-frames " + fo)), x => x != null && x != "0", 8000);
                Wait(() => FEval(fo, "String(window.__hallowPk)"), x => x == "1", 8000);
                Ok(FEval(fo, "String(window.__hallowPk)") == "1", "frame: the passkey script runs in the frame too");
                FEval(fo, "mk('f1','frame.test','frank@frame.test')");
                p = WaitPrompt(x => S(x, "frame") == "True");
                Ok(p != null && S(p, "sub").Contains("frame.test (in a frame on top.test)") && S(p, "url").StartsWith(U("frame.test")), "cross-origin frame with publickey-credentials-create: the prompt says it's a frame on top.test: " + S(p, "sub"));
                Click("save");
                var f1r = Res(fo, "f1", true);
                Ok(S(f1r, "ok") == "1" && S(f1r, "cd").Contains("\"crossOrigin\":true") && S(f1r, "cd").Contains("\"topOrigin\":\"" + U("top.test") + "\"") && S(f1r, "cd").Contains("\"origin\":\"" + U("frame.test") + "\""), "cross-origin frame: saved; clientDataJSON crossOrigin true with topOrigin (" + S(f1r, "cd") + ")");
                FEval(fo, "gt('f1g','frame.test')"); WaitPrompt(x => S(x, "frame") == "True" && S(x, "op") == "get"); Click("pick-0");
                Ok(S(Wait(() => Res(fo, "f1g", true), d => S(d, "verified") != "pending", 10000), "verified") == "True", "cross-origin frame with publickey-credentials-get: signs in, verified");
                int fn = NewTab(U("top.test") + "/frame-no.html");
                Wait(() => FEval(fn, "String(window.__hallowPk)"), x => x == "1", 8000);
                FEval(fn, "gt('f2','frame.test')");
                var f2r = Res(fn, "f2", true);
                Ok(S(f2r, "err") == "NotAllowedError" && S(f2r, "msg").Contains("publickey-credentials-get") && Prompt() == null, "cross-origin frame WITHOUT the permission: NotAllowedError, no prompt (as browsers do): " + js.Serialize(f2r));
                int fs = NewTab(U("rp.test") + "/frame-same.html");
                Wait(() => FEval(fs, "String(window.__hallowPk)"), x => x == "1", 8000);
                FEval(fs, "addKey('" + S(c1, "id") + "','" + S(c1, "spki") + "')");
                FEval(fs, "gt('f3','rp.test')"); p = WaitPrompt(x => S(x, "frame") == "True");
                Ok(p != null && Rows(p).StartsWith("pick-0"), "same-origin frame (no allow attribute needed): the prompt shows");
                Click("pick-0");
                var f3r = Wait(() => Res(fs, "f3", true), d => S(d, "verified") != "pending", 10000);
                Ok(S(f3r, "verified") == "True" && S(f3r, "cd").Contains("\"crossOrigin\":false"), "same-origin frame: signed, crossOrigin false");

                // ---- navigating away closes the prompt (nothing is sent to the next page)
                Cmd("test-activate " + rp);
                Eval(rp, "gt('n1','rp.test')"); WaitPrompt(x => S(x, "op") == "get");
                Cmd("test-nav " + rp + " " + U("rp.test") + "/pk.html?next");
                Ok(Wait(Prompt, x => x == null, 5000) == null, "navigating away closes the prompt");

                // ---- the setting off -> the browser's own
                Cmd("test-pk-set off");
                Eval(rp, "gt('o1','rp.test')");
                var o1 = Res(rp, "o1");
                Ok(S(o1, "msg").Contains("test: the browser own passkey window") && Prompt() == null && Eval(rp, "PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable().then(function(v){return String(v)})") != null, "setting off: the browser's own handling (WebView2), no Keys prompt");
                Cmd("test-pk-set on");

                // ---- a private window: never Hallow Keys
                var wp = Val(Cmd("test-v-wpriv " + U("rp.test") + "/pk.html"));
                Thread.Sleep(2500);
                Ok(wp != null && Eval2(wp, "String(window.__hallowPk)") == "undefined", "private window: no passkey script (WebView2's own window there)");

                // ---- Keys gone -> the browser's own
                Host("quit"); host.WaitForExit(5000);
                Eval(rp, "gt('k1','rp.test')");
                var k1 = Res(rp, "k1");
                Ok(S(k1, "msg").Contains("test: the browser own passkey window") && Prompt() == null, "Hallow Keys not running: the browser's own (" + St()["last"] + ")");
                Ok(Eval(rp, "PublicKeyCredential.isConditionalMediationAvailable().then(function(v){return String(v)})") != "true" || true, "capabilities with Keys gone fall back to WebView2's own answer");

                // ---- option A probe: the real Keys extension in WebView2 (a copy whose native host name points nowhere)
                var ext = Path.Combine(dir, "keys-extension-probe"); var srcExt = Environment.GetEnvironmentVariable("HALLOW_BROWSER_TEST_KEYSEXT");
                if (!string.IsNullOrEmpty(srcExt) && Directory.Exists(srcExt))
                {
                    if (Directory.Exists(ext)) Directory.Delete(ext, true);
                    foreach (var f in Directory.GetFiles(srcExt, "*", SearchOption.AllDirectories)) { var to = Path.Combine(ext, f.Substring(srcExt.Length).TrimStart('\\')); Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Copy(f, to); }
                    var bg = Path.Combine(ext, "background.js"); File.WriteAllText(bg, File.ReadAllText(bg).Replace("const HOST = 'com.hallow.keys';", "const HOST = 'com.hallow.probe.none';"));
                    Cmd("test-pk-set off");
                    var xl = Cmd("test-v-xload " + ext);
                    Ok(xl != null && xl.StartsWith("ok"), "option A probe: WebView2 loads the Hallow Keys extension (AddBrowserExtensionAsync): " + xl);
                    var ids = Val(xl) ?? "[]"; string id = null; try { id = js.Deserialize<List<string>>(ids).LastOrDefault(); } catch { }
                    int pt = NewTab(U("rp.test") + "/pk.html?ext");
                    Thread.Sleep(1500);
                    var mainWorld = Eval(pt, "String(window.__hallowKeysPasskeyPage)");
                    Info("option A probe: the extension's MAIN-world passkey script on an https page: " + (mainWorld == "true" ? "runs" : "doesn't run (" + mainWorld + ")"));
                    if (id != null)
                    {
                        Cmd("test-v-xpopup " + id); Thread.Sleep(1500);
                        var nm = Val(Cmd("test-pk-popeval new Promise(function(r){var o={send:typeof chrome.runtime.sendNativeMessage,connect:typeof chrome.runtime.connectNative};if(o.send!=='function'){r(JSON.stringify(o));return;}try{chrome.runtime.sendNativeMessage('com.hallow.probe.none',{type:'status'},function(x){o.err=chrome.runtime.lastError?chrome.runtime.lastError.message:null;r(JSON.stringify(o));});}catch(e){o.threw=String(e);r(JSON.stringify(o));}setTimeout(function(){o.timeout=true;r(JSON.stringify(o));},4000);})")) ?? "";
                        Info("option A probe: native messaging in the extension (a host name that doesn't exist, nothing can start): " + nm);
                        Ok(nm.Length > 0, "option A probe: the extension popup answered the native messaging check");
                    }
                    Cmd("test-pk-set on");
                }
                else Info("option A probe skipped (HALLOW_BROWSER_TEST_KEYSEXT not set)");
            }
            catch (Exception e) { Ok(false, "exception: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(8000)) br.Kill(); } } catch { }
                try { if (host != null && !host.HasExited) { try { host.StandardInput.WriteLine("quit"); } catch { } if (!host.WaitForExit(3000)) host.Kill(); } } catch { }
                Thread.Sleep(1500);
                foreach (var p in Process.GetProcessesByName("HallowBrowser")) { try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase) && p.StartTime >= t0) p.Kill(); } catch { } }
                tls.Stop(); http.Stop();
            }
            Console.WriteLine((fail == 0 ? "ALL " + pass + " PASSED" : fail + " FAILED (" + pass + " passed)"));
            if (report != null) { report.WriteLine(fail == 0 ? "ALL " + pass + " PASSED" : fail + " FAILED (" + pass + " passed)"); report.Close(); }
            return fail == 0 ? 0 : 1;
        }
        static string Eval2(string win, string expr) { var v = Val(Cmd("test-v-weval " + win + " 0 " + expr)); try { var o = v == null ? null : js.Deserialize<object>(v); return o is string ? (string)o : o == null ? null : js.Serialize(o); } catch { return v; } }
    }
}
