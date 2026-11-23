// Hallow Browser - Hallow Keys autofill. WebView2 has no native messaging, so the browser itself plays the part of Keys' native
// host: it talks to the running Hallow Keys over the same pipe and protocol the Keys extension's host uses ("HallowKeys.Native.<user>",
// one line: <session secret> <json>, one JSON line back; the secret is the per-session file Keys writes for this Windows user).
// The checks the extension identity gives Keys are done here instead:
//  - the page address sent to Keys is always the tab's real top-level address (from the browser, never from the page);
//  - Keys decides which logins belong to that address (its own strict rules), and answers only while it's unlocked;
//  - a fill happens ONLY after you click the key in the address bar and then a login (never by itself, never silently);
//  - the origin (scheme://host:port) is pinned when you click the key; if the tab is on another origin when you pick the login,
//    nothing is asked or filled; the fill script checks the origin again inside the page and only fills the top frame.
//  - nothing from Keys is logged, kept or written anywhere; the password lives only in the fill script's text for that one call.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HallowBrowser
{
    public class KeysItem { public string Id, Title, Username; }

    public static partial class KeysLink
    {
        // tests use their own stand-in Keys on a ".browsertest" pipe with fake logins (never the real vault)
        public static string PipeName { get { return TestPipe ?? "HallowKeys.Native." + Environment.UserName + (BrowserSettings.TestDir != null ? ".browsertest" : ""); } }   // 1.15 tests: HALLOW_BROWSER_KEYS_PIPE
        public static string SecretFile
        {
            get
            {
                var dir = BrowserSettings.TestDir != null ? Path.Combine(BrowserSettings.TestDir, "HallowKeys") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HallowKeys");
                return Path.Combine(dir, "session.key");
            }
        }
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, int timeout);
        public static bool Running()
        {
            if (WaitNamedPipe(@"\\.\pipe\" + PipeName, 1)) return true;
            return Marshal.GetLastWin32Error() != 2;
        }

        // one request; null = Keys isn't running / didn't answer
        public static Dictionary<string, object> Ask(Dictionary<string, object> msg, int ms = 5000)
        {
            try
            {
                if (!Running()) return null;
                string secret; try { secret = File.ReadAllText(SecretFile).Trim(); } catch { return null; }
                if (secret.Length == 0) return null;
                using (var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    c.Connect(1500);
                    var line = Encoding.UTF8.GetBytes(secret + " " + js.Serialize(msg) + "\n");
                    c.Write(line, 0, line.Length); c.Flush(); Array.Clear(line, 0, line.Length);
                    var r = new StreamReader(c, new UTF8Encoding(false)); var t = r.ReadLineAsync();
                    if (!t.Wait(ms) || t.Result == null) return null;
                    return js.Deserialize<Dictionary<string, object>>(t.Result);
                }
            }
            catch { return null; }
        }
        static string S(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }
        static bool B(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }

        // "off" (not running), "locked", "ready"
        public static string State()
        {
            var r = Ask(new Dictionary<string, object> { { "type", "status" } }, 2000);
            if (r == null) return "off";
            return B(r, "locked") ? "locked" : "ready";
        }
        // logins for this page: titles and usernames only (Keys never sends passwords for a match)
        public static List<KeysItem> Match(string pageUrl)
        {
            if (!Omni.IsWeb(pageUrl)) return new List<KeysItem>();
            var r = Ask(new Dictionary<string, object> { { "type", "match" }, { "url", pageUrl } });
            var list = new List<KeysItem>();
            object items; if (r == null || !B(r, "ok") || !r.TryGetValue("items", out items)) return list;
            foreach (var o in (items as System.Collections.IEnumerable) ?? new object[0])
            {
                var d = o as Dictionary<string, object>; if (d == null) continue;
                list.Add(new KeysItem { Id = S(d, "id"), Title = S(d, "title"), Username = S(d, "username") });
            }
            return list;
        }

        // ---- the rules for a fill (unit-tested). null = go ahead; otherwise why not.
        public static string WhyNotFill(bool clicked, string pinnedOrigin, string currentUrl, bool keysOn)
        {
            if (!keysOn) return "Hallow Keys autofill is off in Settings";
            if (!clicked) return "a fill needs your click";
            if (pinnedOrigin == null) return "this page can't be filled";
            var now = Omni.Origin(currentUrl);
            if (now == null) return "this page can't be filled";
            if (now != pinnedOrigin) return "the page changed to another site - nothing was filled";
            return null;
        }

        // asks Keys for one login (only for the pinned origin, only after the click) and fills it into the page.
        // run: executes a script in the tab's top frame and returns its JSON result. Returns a short message for the user.
        public static async Task<string> Fill(KeysItem item, bool clicked, string pinnedOrigin, Func<string> currentUrl, bool keysOn, Func<string, Task<string>> run)
        {
            var why = WhyNotFill(clicked, pinnedOrigin, currentUrl(), keysOn);
            if (why != null) return "Not filled: " + why + ".";
            var url = currentUrl();
            var r = await Task.Run(() => Ask(new Dictionary<string, object> { { "type", "fill" }, { "url", url }, { "id", item.Id } }));
            if (r == null) return "Hallow Keys didn't answer.";
            if (!B(r, "ok")) { var m = S(r, "msg"); return B(r, "locked") ? "Hallow Keys is locked - unlock it and try again." : m.Length > 0 ? m + "." : "Hallow Keys said no."; }
            // the tab may have moved on while Keys answered
            why = WhyNotFill(clicked, pinnedOrigin, currentUrl(), keysOn);
            if (why != null) { r.Clear(); return "Not filled: " + why + "."; }
            var script = FillScript(pinnedOrigin, S(r, "username"), S(r, "password"));
            r.Clear();
            string res; try { res = await run(script); } catch { res = null; } finally { script = null; }
            if (res == null) return "The page didn't let Hallow Browser fill it.";
            if (res.Contains("filled")) return "Filled " + (item.Username.Length > 0 ? item.Username : item.Title) + ".";
            if (res.Contains("origin")) return "Not filled: the page changed to another site.";
            return "No login form found on this page.";
        }

        // the in-page part: top frame only, the same origin again, a visible password field; fills like typing (input/change events)
        public static string FillScript(string origin, string user, string pass)
        {
            return "(function(){try{" +
                "if(window.top!==window||location.origin!==" + js.Serialize(origin) + ")return 'origin';" +
                "var vis=function(e){var r=e.getBoundingClientRect();return r.width>0&&r.height>0&&getComputedStyle(e).visibility!=='hidden'&&!e.disabled&&!e.readOnly;};" +
                "var pw=[].slice.call(document.querySelectorAll('input[type=password]')).filter(vis);" +
                "if(!pw.length)return 'nofields';var p=pw[0];" +
                "var scope=p.form||document;var all=[].slice.call(scope.querySelectorAll('input')).filter(vis);var u=null;" +
                "for(var i=all.indexOf(p)-1;i>=0;i--){var t=(all[i].type||'text').toLowerCase();if(t==='text'||t==='email'||t==='tel'){u=all[i];break;}}" +
                "var set=function(e,v){var d=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value');d.set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};" +
                "var user=" + js.Serialize(user ?? "") + ";if(u&&user)set(u,user);set(p," + js.Serialize(pass ?? "") + ");return 'filled';" +
                "}catch(e){return 'error';}})()";
        }
        // does the page have a password field? (top frame; checked after each load, and once more a moment later for pages that draw late)
        public const string HasPasswordScript = "!!document.querySelector('input[type=password]')";
    }
}
