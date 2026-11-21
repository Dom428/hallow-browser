// A stand-in Hallow Keys for tests: the same pipe protocol as Keys' native host ("<secret> <json>" in, one JSON line out) on the
// TEST pipe (HallowKeys.Native.<user>.browsertest) with ONE made-up login. It never touches the real Keys, its vault or its pipe.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public class FakeKeys
    {
        public string Secret = Guid.NewGuid().ToString("N").ToUpperInvariant();
        public bool Locked;
        public readonly List<string> Log = new List<string>();   // request types and urls only
        public string SavedUrl = "http://127.0.0.1:5555";
        public const string User = "tester", Pass = "fake-PW-123";
        volatile bool stop; Thread th;
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();

        public void WriteSecret() { Directory.CreateDirectory(Path.GetDirectoryName(KeysLink.SecretFile)); File.WriteAllText(KeysLink.SecretFile, Secret); }
        public void Start()
        {
            if (!KeysLink.PipeName.EndsWith(".browsertest")) throw new Exception("not a test pipe");
            WriteSecret();
            th = new Thread(Loop) { IsBackground = true }; th.Start();
            for (int i = 0; i < 50 && !KeysLink.Running(); i++) Thread.Sleep(20);
        }
        public void Stop() { stop = true; try { using (var c = new NamedPipeClientStream(".", KeysLink.PipeName)) c.Connect(100); } catch { } }
        void Loop()
        {
            while (!stop)
            {
                try
                {
                    using (var p = new NamedPipeServerStream(KeysLink.PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte))
                    {
                        p.WaitForConnection(); if (stop) return;
                        var r = new StreamReader(p, new UTF8Encoding(false)); var line = r.ReadLine() ?? "";
                        var sp = line.IndexOf(' ');
                        string reply;
                        if (sp < 0 || line.Substring(0, sp) != Secret) reply = "{\"ok\":false,\"msg\":\"not allowed\"}";
                        else reply = Answer(js.Deserialize<Dictionary<string, object>>(line.Substring(sp + 1)));
                        var b = Encoding.UTF8.GetBytes(reply + "\n"); p.Write(b, 0, b.Length); p.Flush();
                        try { p.WaitForPipeDrain(); } catch { }
                    }
                }
                catch { Thread.Sleep(50); }
            }
        }
        string Answer(Dictionary<string, object> m)
        {
            var type = m.ContainsKey("type") ? m["type"] as string : ""; var url = m.ContainsKey("url") ? m["url"] as string : "";
            lock (Log) Log.Add(type + " " + url);
            if (type == "status") return js.Serialize(new Dictionary<string, object> { { "ok", true }, { "running", true }, { "locked", Locked } });
            if (Locked) return "{\"ok\":false,\"running\":true,\"locked\":true,\"msg\":\"Hallow Keys is locked\"}";
            bool match = KeysDomain.Matches(SavedUrl, url);
            if (type == "match") return js.Serialize(new Dictionary<string, object> { { "ok", true }, { "items", match ? new object[] { new Dictionary<string, object> { { "id", "t1" }, { "title", "Test login" }, { "username", User }, { "totp", false } } } : new object[0] } });
            if (type == "fill")
            {
                if (!match || (m.ContainsKey("id") ? m["id"] as string : "") != "t1") return "{\"ok\":false,\"msg\":\"That login isn't for this site\"}";
                return js.Serialize(new Dictionary<string, object> { { "ok", true }, { "username", User }, { "password", Pass }, { "totp", null } });
            }
            return "{\"ok\":false,\"msg\":\"Unknown request\"}";
        }
        public int Count(string type) { lock (Log) return Log.Count(l => l.StartsWith(type + " ")); }
    }
}
