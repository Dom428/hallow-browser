// Hallow Browser 1.4 - integration test, off-screen like the others (never shown, never focused, below-normal priority), its own test
// folder, profile and pipes, local pages and local stand-ins only: secure DNS through a local DoH stand-in (self-signed certificate,
// accepted only in tests), HTTPS-only against a local http / https pair, a geolocation permission request answered through the
// prompt's own code (never Windows' location), site rules, more windows and tab moves, a private window that leaves nothing behind,
// a test unpacked extension, Shield's per-page count and the status.json relay, and the game corner against stand-in Steam / Epic
// endpoints (never the real ones).
//   BrowserTests.exe --integration <HallowBrowser.exe> <work folder> v14
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    // a tiny HTTPS server (the https half of the pair, the DoH stand-in)
    public class TlsServer
    {
        public int Port; TcpListener l; readonly X509Certificate2 cert; volatile bool stop;
        public readonly List<string> Log = new List<string>();
        public Func<string, string, byte[], Tuple<string, byte[]>> Route;
        public TlsServer(X509Certificate2 c) { cert = c; }
        public void Start()
        {
            l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port;
            new Thread(() => { while (!stop) { try { var c = l.AcceptTcpClient(); new Thread(() => Serve(c)) { IsBackground = true }.Start(); } catch { } } }) { IsBackground = true }.Start();
        }
        public void Stop() { stop = true; try { l.Stop(); } catch { } }
        void Serve(TcpClient c)
        {
            try
            {
                lock (Log) Log.Add("accept");
                using (c) using (var ss = new SslStream(c.GetStream(), false))
                {
                    try { ss.AuthenticateAsServer(cert, false, System.Security.Authentication.SslProtocols.Tls12, false); } catch (Exception e) { lock (Log) Log.Add("TLS " + e.GetType().Name + ": " + e.Message + (e.InnerException != null ? " / " + e.InnerException.Message : "")); return; }
                    while (true)
                    {
                        var head = new List<byte>();
                        while (true) { int b = ss.ReadByte(); if (b < 0) return; head.Add((byte)b); int n = head.Count; if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break; if (n > 65536) return; }
                        var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                        var parts = lines[0].Split(' '); int len = 0;
                        foreach (var ln in lines) if (ln.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len = int.Parse(ln.Substring(15).Trim());
                        var body = new byte[len]; int got = 0; while (got < len) { int r = ss.Read(body, got, len - got); if (r <= 0) return; got += r; }
                        lock (Log) Log.Add(parts[0] + " " + parts[1]);
                        var res = Route(parts[0], parts[1], body);
                        var hb = Encoding.ASCII.GetBytes("HTTP/1.1 " + (res == null ? "404 Not Found" : "200 OK") + "\r\nContent-Type: " + (res == null ? "text/plain" : res.Item1) + "\r\nContent-Length: " + (res == null ? 0 : res.Item2.Length) + "\r\nCache-Control: no-store\r\n\r\n");
                        ss.Write(hb, 0, hb.Length); if (res != null) ss.Write(res.Item2, 0, res.Item2.Length); ss.Flush();
                    }
                }
            }
            catch { }
        }
        // a throw-away self-signed certificate for the stand-ins (made in memory; never put in a certificate store)
        public static X509Certificate2 TestCert()
        {
            using (var rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest("CN=hallow-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("secure.test"); san.AddDnsName("plain.test"); san.AddIpAddress(IPAddress.Loopback);
                req.CertificateExtensions.Add(san.Build());
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                using (var c = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(3)))
                    return new X509Certificate2(c.Export(X509ContentType.Pfx, "hallow"), "hallow", X509KeyStorageFlags.UserKeySet);
            }
        }
        // DNS: *.doh.test -> 127.0.0.1, anything else "no such name"
        public static readonly List<string> Asked = new List<string>();
        public static byte[] DnsAnswer(byte[] q)
        {
            int p = 12; var name = new StringBuilder();
            while (q[p] != 0) { int n = q[p++]; if (name.Length > 0) name.Append('.'); name.Append(Encoding.ASCII.GetString(q, p, n)); p += n; }
            p++; int qtype = (q[p] << 8) | q[p + 1]; p += 4;
            var nm = name.ToString().ToLowerInvariant(); lock (Asked) Asked.Add(nm);
            bool ours = nm.EndsWith(".doh.test");
            var r = new List<byte>(q.Take(p)); r[2] = 0x81; r[3] = (byte)(ours ? 0x80 : 0x83);
            bool a = ours && qtype == 1;
            r[6] = 0; r[7] = (byte)(a ? 1 : 0); r[8] = 0; r[9] = 0; r[10] = 0; r[11] = 0;
            if (a) r.AddRange(new byte[] { 0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 127, 0, 0, 1 });
            return r.ToArray();
        }
    }

    public static class Integration14
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        static int pass, fail; static StreamWriter report;
        static void Ok(bool c, string what) { var l = (c ? "ok   " : "FAIL ") + what; if (c) pass++; else fail++; Console.WriteLine(l); if (report != null) report.WriteLine(l); }
        static void Note(string what) { Console.WriteLine("note " + what); if (report != null) report.WriteLine("note " + what); }
        static string Cmd(string line, int ms = 45000) { return Family.Send("Browser", line, ms); }
        static string Val(string reply) { return reply != null && reply.StartsWith("ok ") ? reply.Substring(3) : null; }
        static T Wait<T>(Func<T> f, Func<T, bool> good, int ms) { var sw = Stopwatch.StartNew(); T v = f(); while (!good(v) && sw.ElapsedMilliseconds < ms) { Thread.Sleep(250); v = f(); } return v; }
        static Dictionary<string, object> J(string reply) { var v = Val(reply); try { return v == null || v == "null" ? null : js.Deserialize<Dictionary<string, object>>(v); } catch { return null; } }
        static List<Dictionary<string, object>> L(string reply) { var v = Val(reply); try { return v == null ? new List<Dictionary<string, object>>() : js.Deserialize<List<Dictionary<string, object>>>(v); } catch { return new List<Dictionary<string, object>>(); } }
        static List<Dictionary<string, object>> Tabs() { return L(Cmd("test-tabs")); }
        static List<Dictionary<string, object>> Wins() { return L(Cmd("test-v-wlist")); }
        static List<Dictionary<string, object>> WTabs(Dictionary<string, object> w) { return ((System.Collections.ArrayList)w["tabs"]).Cast<Dictionary<string, object>>().ToList(); }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static int I(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToInt32(o) : -99; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        static string Eval(int tab, string expr) { var v = Val(Cmd("test-eval " + tab + " " + expr)); try { return v == null ? null : js.Deserialize<object>(v) as string ?? v; } catch { return v; } }
        static string Nav(int tab, string url) { return Cmd("test-nav " + tab + " " + url); }
        static string Title(int tab) { var t = Tabs(); return tab < t.Count ? (string)t[tab]["title"] : null; }
        static string Url(int tab) { var t = Tabs(); return tab < t.Count ? (string)t[tab]["url"] : null; }

        public static int Main(string exe, string dir)
        {
            var run = Path.Combine(dir, "run"); Directory.CreateDirectory(run);
            Environment.SetEnvironmentVariable("HALLOW_BROWSER_TESTDIR", run);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            report = new StreamWriter(Path.Combine(dir, "..", "integration-v14.txt"), false) { AutoFlush = true };
            var cert = TlsServer.TestCert();
            Func<string, string> page = t => "<!DOCTYPE html><html><head><title>" + t + "</title></head><body><h1>" + t + "</h1></body></html>";
            Func<string, Tuple<string, string, string>> H = s => Tuple.Create("text/html", s, "");
            var srv = new TinyServer(); var srv2 = new TinyServer(); var srv3 = new TinyServer();
            srv.Route = (path, h) =>
            {
                if (path.StartsWith("/page.html")) return H(page("Test page"));
                if (path.StartsWith("/hello.html")) return H(page("Plain hello"));
                if (path.StartsWith("/geo.html")) return H("<html><head><title>geo</title></head><body><script>window.R='wait';navigator.geolocation.getCurrentPosition(function(){R='pos'},function(e){R='err'+e.code});</script></body></html>");
                if (path.StartsWith("/notif.html")) return H("<html><head><title>notif</title></head><body><script>window.R='wait';Notification.requestPermission().then(function(x){R=x});</script></body></html>");
                if (path.StartsWith("/js.html")) return H("<html><head><title>no-js</title></head><body><script>document.title='js-ran'</script></body></html>");
                if (path.StartsWith("/popup.html")) return H("<html><head><title>popup</title></head><body><script>setTimeout(function(){window.open('/page.html?pop')},150)</script></body></html>");
                if (path.StartsWith("/cookie.html")) return Tuple.Create("text/html", page("cookie"), "Set-Cookie: hallowtest=1; Max-Age=3600; Path=/\r\n");
                if (path.StartsWith("/ad.html")) return H("<html><head><title>ad page</title><script src=\"http://pagead2.googlesyndication.com:" + srv.Port + "/pagead/show_ads.js\"></script></head><body>ads</body></html>");
                if (path.StartsWith("/pagead/")) return Tuple.Create("text/javascript", "window.adran=1", "");
                if (path.StartsWith("/file.bin")) return Tuple.Create("application/octet-stream", new string('x', 4096), "Content-Disposition: attachment; filename=\"private-download.bin\"\r\n");
                if (path.StartsWith("/steam"))
                {
                    var q = path.Contains("appid=") ? path.Substring(path.IndexOf("appid=") + 6).Split('&')[0] : "?";
                    return Tuple.Create("application/json", "{\"appnews\":{\"appid\":" + q + ",\"newsitems\":[{\"gid\":\"1\",\"title\":\"Stand-in patch notes for " + q + "\",\"url\":\"https://store.steampowered.com/news/app/" + q + "/view/1\",\"date\":" + (1795000000 + q.Length) + ",\"feedlabel\":\"Community Announcements\"}],\"count\":1}}", "");
                }
                if (path.StartsWith("/epic"))
                {
                    var now = DateTime.UtcNow;
                    return Tuple.Create("application/json", "{\"data\":{\"Catalog\":{\"searchStore\":{\"elements\":[{\"title\":\"Stand-in Free Game\",\"productSlug\":\"stand-in-free\",\"promotions\":{\"promotionalOffers\":[{\"promotionalOffers\":[{\"startDate\":\"" + now.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.000Z") + "\",\"endDate\":\"" + now.AddDays(5).ToString("yyyy-MM-ddTHH:mm:ss.000Z") + "\",\"discountSetting\":{\"discountType\":\"PERCENTAGE\",\"discountPercentage\":0}}]}],\"upcomingPromotionalOffers\":[]}}]}}}}", "");
                }
                return Tuple.Create<string, string, string>(null, "not found", "");
            };
            srv2.Route = (path, h) => path.StartsWith("/plain.html") ? H(page("Plain page")) : Tuple.Create<string, string, string>(null, "nf", "");
            srv3.Route = (path, h) => path.StartsWith("/page.html") ? H(page("Test page")) : Tuple.Create<string, string, string>(null, "nf", "");
            srv.Start(); srv2.Start(); srv3.Start();
            var https = new TlsServer(cert) { Route = (m, p, b) => p.StartsWith("/hello.html") ? Tuple.Create("text/html", Encoding.UTF8.GetBytes(page("Secure hello"))) : null }; https.Start();
            var doh = new TlsServer(cert);
            doh.Route = (m, p, b) =>
            {
                byte[] q = null;
                if (m == "POST") q = b;
                else { var i = p.IndexOf("dns="); if (i >= 0) { var s = p.Substring(i + 4).Split('&')[0].Replace('-', '+').Replace('_', '/'); while (s.Length % 4 != 0) s += "="; q = Convert.FromBase64String(s); } }
                return q == null ? null : Tuple.Create("application/dns-message", TlsServer.DnsAnswer(q));
            };
            doh.Start();
            var closed = new TcpListener(IPAddress.Loopback, 0); closed.Start(); int closedPort = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();   // nothing listens there: "https doesn't answer"
            var Lh = "http://127.0.0.1:" + srv.Port; int P = srv.Port, P2 = srv2.Port;
            // the test folder: settings (secure DNS on, through the stand-in), a fake Hub library, a fake Shield status from another browser
            File.WriteAllText(Path.Combine(run, "settings.json"), "{\"SecureDns\":\"cloudflare\",\"HttpsOnly\":true}");
            Directory.CreateDirectory(Path.Combine(run, "HallowHub")); Directory.CreateDirectory(Path.Combine(run, "HallowShield"));
            File.WriteAllText(Path.Combine(run, "HallowHub", "hub.json"), "{\"Games\":{\"steam:2507950\":{\"Name\":\"Delta Force\",\"LastPlayed\":\"2026-11-20T20:00:00\",\"Minutes\":600},\"steam:2073850\":{\"Name\":\"THE FINALS\",\"LastPlayed\":\"2026-11-19T20:00:00\"},\"steam:1422450\":{\"Name\":\"Hidden one\",\"Hidden\":true},\"epic:Fortnite\":{\"Name\":\"Fortnite\"}}}");
            var nowIso = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"); var today = DateTime.Now.ToString("yyyy-MM-dd");
            File.WriteAllText(Path.Combine(run, "HallowShield", "status.json"), "{\"version\":\"1.7\",\"updated\":\"" + nowIso + "\",\"browsers\":[{\"name\":\"Chrome\",\"profile\":\"Default\",\"version\":\"1.7\",\"level\":\"optimal\",\"blockedToday\":100,\"blockedWeek\":1000,\"bannersToday\":3,\"updated\":\"" + nowIso + "\",\"day\":\"" + today + "\",\"id\":\"c0ffee00c0ffee00\"}],\"blockedToday\":100,\"blockedWeek\":1000,\"bannersToday\":3}");
            // a test extension (a folder the test makes)
            var extSrc = Path.Combine(dir, "test-extension"); Directory.CreateDirectory(extSrc);
            File.WriteAllText(Path.Combine(extSrc, "manifest.json"), "{\"manifest_version\":3,\"name\":\"Hallow Test Extension\",\"version\":\"1.0\",\"description\":\"Marks pages\",\"content_scripts\":[{\"matches\":[\"<all_urls>\"],\"js\":[\"c.js\"],\"run_at\":\"document_start\"}],\"action\":{\"default_popup\":\"pop.html\"}}");
            File.WriteAllText(Path.Combine(extSrc, "c.js"), "document.documentElement.setAttribute('data-hallow-ext','1');");
            File.WriteAllText(Path.Combine(extSrc, "pop.html"), "<!DOCTYPE html><html><body>Hello from the test extension</body></html>");
            var extFiles = Directory.GetFiles(extSrc).Length;
            Process br = null;
            Func<string, Process> launch = arg =>
            {
                var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false };
                psi.EnvironmentVariables["HALLOW_BROWSER_TESTDIR"] = run;
                psi.EnvironmentVariables["HALLOW_BROWSER_OFFSCREEN"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_FAKEGAME"] = Path.Combine(run, "game.txt");
                psi.EnvironmentVariables["HALLOW_BROWSER_HOSTRULES"] = "MAP secure.test 127.0.0.1, MAP plain.test 127.0.0.1, MAP *.googlesyndication.com 127.0.0.1";
                psi.EnvironmentVariables["HALLOW_BROWSER_IGNORE_CERTS"] = "1";
                psi.EnvironmentVariables["HALLOW_BROWSER_DOH_TEMPLATE"] = "https://127.0.0.1:" + doh.Port + "/dns-query{?dns}";
                psi.EnvironmentVariables["HALLOW_BROWSER_HTTPS_PORTS"] = P + "=" + https.Port + "," + P2 + "=" + closedPort;
                psi.EnvironmentVariables["HALLOW_BROWSER_STEAM_NEWS"] = Lh + "/steam";
                psi.EnvironmentVariables["HALLOW_BROWSER_EPIC_FREE"] = Lh + "/epic";
                var p = Process.Start(psi); try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                return p;
            };
            Func<bool> up = () => Wait(() => Cmd("ping", 2000), r => r != null && r.StartsWith("ok"), 25000) != null && Wait(Tabs, t => t.Any(x => (bool)x["active"] && (bool)x["ready"]), 25000).Count > 0;
            try
            {
                File.WriteAllText(Path.Combine(run, "game.txt"), "");
                br = launch(Lh + "/page.html");
                Ok(up(), "browser up (off-screen)");

                // ---- secure DNS through the local DoH stand-in
                var set = J(Cmd("test-v-settings"));
                Ok(S(set, "dnsApplied") == "cloudflare", "secure DNS: the setting was applied at start");
                var ls = File.ReadAllText(Path.Combine(run, "Local", "Profile", "EBWebView", "Local State"));
                Ok(ls.Contains("\"dns_over_https\"") && ls.Contains("127.0.0.1:" + doh.Port), "secure DNS: the engine's Local State has mode secure + the (stand-in) template");
                Nav(0, "http://site.doh.test:" + srv3.Port + "/page.html?doh");
                Ok(Title(0) == "Test page", "secure DNS: a name only the DoH stand-in knows opens (" + Title(0) + ")");
                lock (doh.Log) Note("doh server: " + string.Join(" | ", doh.Log.Take(8)));
                lock (TlsServer.Asked) Ok(TlsServer.Asked.Contains("site.doh.test"), "secure DNS: the lookup went to the DoH stand-in (" + TlsServer.Asked.Distinct().Count() + " names asked there)");
                Nav(0, "http://nowhere.hallow-nx.test:" + srv3.Port + "/page.html");
                Ok(Title(0) != "Test page", "secure DNS: a name the stand-in doesn't know doesn't open (no fallback to Windows' DNS)");

                // ---- HTTPS-only
                Nav(0, "http://secure.test:" + P + "/hello.html");
                var u0 = Wait(() => Url(0), u => u != null && u.StartsWith("https://secure.test"), 8000);
                for (int tries = 1; tries < 3 && Wait(() => Title(0), t => t == "Secure hello", 3000) != "Secure hello"; tries++) { lock (https.Log) Note("https try " + tries + " failed (stand-in log: " + string.Join(" | ", https.Log) + ") - again"); Nav(0, Lh + "/page.html"); Nav(0, "http://secure.test:" + P + "/hello.html"); u0 = Wait(() => Url(0), u => u != null && u.StartsWith("https://secure.test"), 8000); }
                Ok(u0 == "https://secure.test:" + https.Port + "/hello.html" && Wait(() => Title(0), t => t == "Secure hello", 5000) == "Secure hello", "https-only: http://secure.test was opened over https (" + u0 + ")");
                lock (https.Log) Note("https server: " + string.Join(" | ", https.Log));
                Ok(srv.Hits("/hello.html").Count == 0, "https-only: the http version was never asked for");
                var hs = J(Cmd("test-v-https")); Ok(I(hs, "upgrades") >= 1, "https-only: counted as an upgrade");
                Cmd("test-nav 0 http://plain.test:" + P2 + "/plain.html");
                hs = Wait(() => J(Cmd("test-v-https")), x => x != null && ((System.Collections.ArrayList)x["blocked"])[0] != null, 10000);
                Note("https trace: " + Val(Cmd("test-v-https")));
                var blockedUrl = hs == null ? null : ((System.Collections.ArrayList)hs["blocked"])[0] as string;
                Ok(blockedUrl == "http://plain.test:" + P2 + "/plain.html", "https-only: no https there - the browser asks first (" + blockedUrl + ")");
                var itxt = Wait(() => Eval(0, "document.body?document.body.innerText:''"), t => t != null && t.Contains("secure"), 5000);
                Ok(itxt != null && itxt.Contains("This site doesn't support a secure connection") && itxt.Contains("Continue to the site"), "https-only: the page says so and offers to continue");
                Ok(Url(0) == "http://plain.test:" + P2 + "/plain.html", "https-only: the address bar keeps the http address meanwhile");
                Ok(srv2.Hits("/plain.html").Count == 0, "https-only: nothing was sent over http before you agreed");
                Ok(Val(Cmd("test-v-httpsclick 0 go")) == "\"clicked\"", "https-only: clicking the page's own Continue button");
                Ok(Wait(() => Title(0), t => t == "Plain page", 8000) == "Plain page" && srv2.Hits("/plain.html").Count == 1, "https-only: ...goes on over http");
                int asked = I(J(Cmd("test-v-https")), "asked"); Nav(0, "http://plain.test:" + P2 + "/plain.html?again");
                Ok(Title(0) == "Plain page" && I(J(Cmd("test-v-https")), "asked") == asked, "https-only: the same site isn't asked about again until the browser restarts");

                // ---- site permissions: a local page asks for the location (answered through the prompt's code - never Windows' location)
                Nav(0, Lh + "/page.html");
                Cmd("test-nav 0 " + Lh + "/geo.html");
                var ask = Wait(() => J(Cmd("test-v-ask")), a => a != null, 8000);
                Ok(ask != null && S(ask, "kind") == "Geolocation" && S(ask, "host") == "127.0.0.1", "permissions: the location request waits for the Hallow prompt (" + (ask == null ? "none" : S(ask, "kind") + " from " + S(ask, "host")) + ")");
                Ok(Cmd("test-v-answer block") == "ok", "permissions: Block");
                Ok(Wait(() => Eval(0, "window.R"), r => r != null && r != "wait", 5000) == "err1", "permissions: the page was refused (PERMISSION_DENIED)");
                var perms = Val(Cmd("test-v-perms " + Lh));
                Ok(perms != null && perms.Contains("Geolocation") && perms.Contains("Deny"), "permissions: Block is saved in the profile (" + perms + ")");
                Cmd("test-nav 0 " + Lh + "/geo.html?2"); Thread.Sleep(1500);
                Ok(J(Cmd("test-v-ask")) == null && Eval(0, "window.R") == "err1", "permissions: a saved answer isn't asked again");
                Cmd("test-nav 0 " + Lh + "/notif.html");
                Ok(Wait(() => J(Cmd("test-v-ask")), a => a != null, 6000) != null, "permissions: notifications ask too");
                Cmd("test-v-answer once");
                Ok(Wait(() => Eval(0, "window.R"), r => r != "wait", 5000) == "granted" && !Val(Cmd("test-v-perms " + Lh)).Contains("Notifications"), "permissions: Allow this time - granted, not saved");
                Cmd("test-nav 0 " + Lh + "/notif.html?2"); Wait(() => J(Cmd("test-v-ask")), a => a != null, 6000); Cmd("test-v-answer allow");
                Ok(Wait(() => Eval(0, "window.R"), r => r != "wait", 5000) == "granted" && Val(Cmd("test-v-perms " + Lh)).Contains("Notifications " + Lh + "/ Allow"), "permissions: Allow - saved");
                var sites = Val(Cmd("test-v-sites"));
                Ok(sites != null && sites.Contains("127.0.0.1") && sites.Contains("plain.test"), "site settings: lists the sites with settings (" + sites + ")");
                Ok(Val(Cmd("test-v-sitespage")) == "True", "site settings: hallow://settings/sites opens");
                Cmd("test-v-reset 127.0.0.1");
                Ok(Val(Cmd("test-v-perms " + Lh)) == "[]", "site settings: Reset clears the site's permissions");

                // ---- site rules: JavaScript, pop-ups, sound, cookies
                Cmd("test-v-rule 127.0.0.1 script block"); Nav(0, Lh + "/js.html");
                Ok(Title(0) == "no-js", "site settings: JavaScript blocked for the site - its scripts don't run");
                Cmd("test-v-rule 127.0.0.1 script null"); Nav(0, Lh + "/js.html?2");
                Ok(Wait(() => Title(0), t => t == "js-ran", 3000) == "js-ran", "site settings: allowed again - they run");
                int tabsBefore = Tabs().Count; Nav(0, Lh + "/popup.html"); Thread.Sleep(1200);
                var pp = J(Cmd("test-v-popups"));
                Ok(I(pp, "blocked") == 1 && Tabs().Count == tabsBefore, "pop-ups: one the page opened by itself (no click) was blocked (" + Val(Cmd("test-v-popups")) + ", tabs " + Tabs().Count + " vs " + tabsBefore + ")");
                Cmd("test-v-rule 127.0.0.1 popups allow"); Nav(0, Lh + "/popup.html?2");
                Ok(Wait(() => Tabs().Count, n => n == tabsBefore + 1, 4000) == tabsBefore + 1, "pop-ups: allowed for the site - it opens (as a tab)");
                Cmd("test-close " + (Tabs().Count - 1)); Cmd("test-activate 0");
                Cmd("test-v-rule 127.0.0.1 popups null"); Cmd("test-v-rule 127.0.0.1 sound mute"); Nav(0, Lh + "/page.html?m");
                Ok(Wait(() => (bool)Tabs()[0]["muted"], m => m, 3000), "sound: muted for the site");
                Cmd("test-v-rule 127.0.0.1 sound null"); Nav(0, Lh + "/page.html?m2");
                Ok(Wait(() => (bool)Tabs()[0]["muted"], m => !m, 3000) == false, "sound: unmuted when the rule goes");
                Nav(0, Lh + "/cookie.html");
                var ck = Val(Cmd("test-v-cookies 127.0.0.1")); Ok(ck != null && int.Parse(ck) >= 1, "cookies: the site's cookies are counted (" + ck + ")");
                Cmd("test-v-delcookies 127.0.0.1"); Ok(Val(Cmd("test-v-cookies 127.0.0.1")) == "0", "cookies: deleted per site");

                // ---- Shield: blocked on this page, and the status.json relay
                var sh = J(Cmd("test-v-shstate"));
                Ok(S(sh, "state") == "extension" && B(sh, "counts"), "Shield: loaded with the feedback permission granted (its own copy)");
                srv.Clear(); Nav(0, Lh + "/ad.html");
                Ok(srv.Hits("/pagead/").Count == 0, "Shield: still blocks the test ad request");
                var cnt = J(Cmd("test-v-shcount 0"));
                Note("Shield popup count: " + js.Serialize(cnt));
                int nBlocked; Ok(cnt != null && int.TryParse(S(cnt, "count"), out nBlocked) && nBlocked >= 1 && (S(cnt, "text") ?? "").Contains("blocked on this page"), "Shield: \"blocked on this page\" shows a number (" + S(cnt, "count") + ")");
                var rel = J(Cmd("test-v-relay", 60000)); var file = S(rel, "file");
                Note("relay: " + S(rel, "report") + " / " + S(rel, "note"));
                var st = file == null ? null : js.Deserialize<Dictionary<string, object>>(file);
                var bl = st == null ? new List<Dictionary<string, object>>() : ((System.Collections.ArrayList)st["browsers"]).Cast<Dictionary<string, object>>().ToList();
                var mine = bl.FirstOrDefault(x => S(x, "name") == "Hallow Browser");
                Ok(mine != null && bl.Any(x => S(x, "name") == "Chrome" && S(x, "id") == "c0ffee00c0ffee00"), "relay: status.json has our entry next to the other browser's (merged, not overwritten)");
                Ok(mine != null && I(mine, "blockedToday") >= 1 && S(mine, "level") != null && S(mine, "day") == today, "relay: our blocked-today count, level and day (" + (mine == null ? "-" : I(mine, "blockedToday").ToString()) + ")");
                Ok(st != null && I(st, "blockedToday") == 100 + I(mine, "blockedToday"), "relay: the top-level sum adds both browsers (" + I(st, "blockedToday") + ")");
                Ok(file != null && !file.Contains("127.0.0.1") && !file.Contains("ad.html"), "relay: no address or site in the file");

                // ---- your own extensions
                var xl = Cmd("test-v-xload " + extSrc);
                Ok(xl != null && xl.StartsWith("ok "), "extensions: a test unpacked extension loads (" + xl + ")");
                var xs = J(Cmd("test-v-xlist")); var mineX = ((System.Collections.ArrayList)xs["mine"]).Cast<Dictionary<string, object>>().FirstOrDefault();
                var xid = S(mineX, "id"); var xfolder = S(mineX, "folder");
                Ok(xid != null && B(mineX, "enabled") && B(mineX, "exists") && xfolder.StartsWith(Path.Combine(run, "Local", "Extensions")), "extensions: copied into the browser's data and kept there");
                Ok(((System.Collections.ArrayList)xs["engine"]).Cast<string>().Any(e => e.StartsWith("Hallow Test Extension|" + xid + "|True")) && ((System.Collections.ArrayList)xs["engine"]).Cast<string>().Any(e => e.StartsWith("Hallow Shield|")), "extensions: the engine runs it next to Shield");
                Nav(0, Lh + "/page.html?ext");
                Ok(Wait(() => Eval(0, "document.documentElement.getAttribute('data-hallow-ext')"), v => v == "1", 4000) == "1", "extensions: its content script runs on pages");
                var pop = J(Cmd("test-v-xpopup " + xid));
                Ok(pop != null && (S(pop, "src") ?? "").StartsWith("chrome-extension://" + xid + "/pop.html") && (S(pop, "text") ?? "").Contains("Hello from the test extension"), "extensions: its popup opens in the flyout");
                Cmd("test-v-xenable " + xid + " off"); Nav(0, Lh + "/page.html?ext2");
                Ok(Eval(0, "document.documentElement.getAttribute('data-hallow-ext')") == null || Eval(0, "String(document.documentElement.getAttribute('data-hallow-ext'))") == "null", "extensions: switched off - it doesn't run");
                Cmd("test-v-xenable " + xid + " on"); Cmd("test-v-xremove " + xid);
                xs = J(Cmd("test-v-xlist"));
                Ok(((System.Collections.ArrayList)xs["mine"]).Count == 0 && !((System.Collections.ArrayList)xs["engine"]).Cast<string>().Any(e => e.Contains(xid)) && !Directory.Exists(xfolder), "extensions: removed - from the engine and the browser's copy");
                Ok(Directory.GetFiles(extSrc).Length == extFiles, "extensions: your original folder was left alone");

                // ---- more windows
                Ok(Wins().Count == 1, "windows: one at start");
                Cmd("test-v-wnew " + Lh + "/page.html?w2");
                var ws = Wait(Wins, w => w.Count == 2 && WTabs(w[1]).Count == 1 && (bool)WTabs(w[1])[0]["ready"], 15000);
                Ok(ws.Count == 2 && (string)WTabs(ws[1])[0]["url"] == Lh + "/page.html?w2" && S(ws[1], "spaceName") == "Window 2", "windows: Ctrl+N's new window, with its own workspace (" + S(ws.LastOrDefault(), "spaceName") + ")");
                var ni = int.Parse(Val(Cmd("test-new " + Lh + "/page.html?mover"))); Wait(Tabs, t => t.Count > ni && (bool)t[ni]["ready"], 8000);
                Eval(ni, "window.__marker=42,1");
                Ok(Val(Cmd("test-v-wmove 0 " + ni + " 1 0")) == "1", "windows: a tab moves into the other window");
                ws = Wins();
                Ok(WTabs(ws[1]).Count == 2 && (string)WTabs(ws[1])[0]["url"] == Lh + "/page.html?mover" && !WTabs(ws[0]).Any(t => (string)t["url"] == Lh + "/page.html?mover"), "windows: ...at the slot it was dropped in, gone from the first");
                var mk = Val(Cmd("test-v-weval 1 0 window.__marker"));
                Ok(mk == "42", "windows: the page moved as it was (not reloaded: " + mk + ")");
                Ok(Val(Cmd("test-v-wmove 1 0 new")) == "2", "windows: dragging a tab out makes a new window");
                ws = Wait(Wins, w => w.Count == 3 && WTabs(w[2]).Count == 1, 10000);
                Ok(ws.Count == 3 && (string)WTabs(ws[2])[0]["url"] == Lh + "/page.html?mover" && WTabs(ws[1]).Count == 1 && Val(Cmd("test-v-weval 2 0 window.__marker")) == "42", "windows: ...with the page still as it was");
                Cmd("test-v-wclose 2"); ws = Wait(Wins, w => w.Count == 2, 8000);
                Ok(ws.Count == 2, "windows: closing a window (not the last) keeps the others");
                Cmd("test-v-savenow"); Thread.Sleep(600);
                var sess = File.ReadAllText(Path.Combine(run, "session.json"));
                Ok(sess.Contains("\"Windows\"") && sess.Contains("page.html?w2") && !sess.Contains("page.html?mover"), "windows: session.json keeps both windows (the closed one's tab is gone with it)");

                // ---- a private window
                Cmd("test-v-wpriv " + Lh + "/page.html?private-secret");
                ws = Wait(Wins, w => w.Count == 3 && WTabs(w[2]).Count == 1 && (bool)WTabs(w[2])[0]["ready"], 15000);
                Ok(ws.Count == 3 && B(ws[2], "private") && S(ws[2], "space") == null, "private: Ctrl+Shift+N's window (InPrivate profile, no workspace)");
                Ok(S(ws[2], "shield") == "fallback", "private: Shield runs as its network-only fallback there (extensions don't run in InPrivate)");
                srv.Clear(); Cmd("test-v-wnav 2 0 " + Lh + "/ad.html?private-secret");
                Ok(srv.Hits("/ad.html").Count == 1 && srv.Hits("/pagead/").Count == 0, "private: Shield's fallback blocks the test ad request");
                Cmd("test-v-weval 2 0 (document.cookie='psecret=1',1)");
                Ok(Val(Cmd("test-v-weval 2 0 document.cookie")).Contains("psecret") && Val(Cmd("test-v-weval 0 0 document.cookie")) == "\"\"", "private: its cookies are its own (the normal window doesn't see them)");
                var dlBefore = Directory.Exists(Path.Combine(run, "Downloads")) ? Directory.GetFiles(Path.Combine(run, "Downloads")).Length : 0;
                Cmd("test-v-wnav 2 0 " + Lh + "/file.bin");
                Ok(Wait(() => Directory.Exists(Path.Combine(run, "Downloads")) ? Directory.GetFiles(Path.Combine(run, "Downloads"), "private-download*.bin").Length : 0, n => n > 0, 10000) > 0, "private: downloads are still kept (in the Downloads folder)");
                Thread.Sleep(5000); Cmd("test-v-savenow"); Thread.Sleep(800);
                var files = new[] { "session.json", "workspaces.json", "settings.json" }.Select(f => Path.Combine(run, f)).Concat(new[] { Path.Combine(run, "Local", "Profile", "hallow-history.json") }).Where(File.Exists).ToList();
                Ok(files.Count >= 3 && files.All(f => !File.ReadAllText(f).Contains("private-secret")), "private: nothing about it in the session, workspaces, settings or history files");
                Ok(Val(Cmd("test-v-hist private-secret")) == "0" && Val(Cmd("test-v-typed private-secret")) == "0", "private: not in the history or the typed addresses");
                Cmd("test-v-wclose 2"); Wait(Wins, w => w.Count == 2, 8000);
                Ok(files.All(f => !File.ReadAllText(f).Contains("private-secret")), "private: still nothing after it closed");

                // ---- the game corner (off by default; stand-in Steam and Epic)
                Ok(srv.Hits("/steam").Count == 0 && srv.Hits("/epic").Count == 0, "game corner: off by default - nothing was fetched");
                Cmd("test-v-set corner true");
                var cc = Val(Cmd("test-v-corner", 60000));
                var steamHits = srv.Hits("/steam");
                Ok(cc != null && cc.Contains("Stand-in patch notes for 2507950") && cc.Contains("Stand-in patch notes for 2073850") && !cc.Contains("1422450") && cc.Contains("Stand-in Free Game"), "game corner: news for the Hub library's Steam games (hidden one left out) and Epic's free game");
                Ok(steamHits.Count == 2 && steamHits.All(h => System.Text.RegularExpressions.Regex.IsMatch(h["path"], @"^/steam\?appid=\d+&count=3&maxlength=1&format=json$") && h["cookie"] == ""), "game corner: only the app ids went out (no cookies, nothing else)");
                var nt = Val(Cmd("test-v-ntdata"));
                Ok(nt != null && nt.Contains("\"games\"") && nt.Contains("Stand-in Free Game"), "game corner: in the new tab page's data");
                var ntab = int.Parse(Val(Cmd("test-new")));
                var gtxt = Wait(() => Eval(ntab, "document.getElementById('gc').textContent"), t => t != null && t.Contains("GAME NEWS"), 8000);
                Ok(gtxt != null && gtxt.Contains("Stand-in patch notes") && gtxt.Contains("FREE GAMES") && gtxt.Contains("Stand-in Free Game"), "game corner: the new tab page shows it");
                int hitsNow = srv.Hits("/steam").Count; Cmd("test-m-nt-refresh"); Thread.Sleep(1500);
                Ok(srv.Hits("/steam").Count == hitsNow, "game corner: not fetched again within 6 hours");

                // ---- quit and back: the windows come back
                Cmd("quit"); br.WaitForExit(20000); Ok(br.HasExited, "quit (every window)");
                br = launch(""); Ok(up(), "up again");
                ws = Wait(Wins, w => w.Count == 2, 15000);
                Ok(ws.Count == 2 && WTabs(ws[1]).Any(t => (string)t["url"] == Lh + "/page.html?w2") && !WTabs(ws[0]).Any(t => ((string)t["url"]).Contains("w2")), "windows: both windows come back with their own tabs");
                Ok(!ws.Any(w => B(w, "private")), "private: no private window comes back");
                var cr = Path.Combine(run, "crashes");
                Ok(!Directory.Exists(cr) || Directory.GetFiles(cr).Length == 0, "no crash reports");
            }
            catch (Exception e) { Ok(false, "integration crashed: " + e); }
            finally
            {
                try { if (br != null && !br.HasExited) { Cmd("quit", 3000); if (!br.WaitForExit(10000)) br.Kill(); } } catch { }
                srv.Stop(); srv2.Stop(); srv3.Stop(); https.Stop(); doh.Stop(); try { cert.Dispose(); } catch { }
                var msg = fail == 0 ? "INTEGRATION v14: ALL " + pass + " PASSED" : "INTEGRATION v14: " + fail + " FAILED, " + pass + " passed";
                Console.WriteLine(msg); if (report != null) { report.WriteLine(msg); report.Dispose(); }
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
