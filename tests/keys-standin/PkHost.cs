// A stand-in Hallow Keys for Hallow Browser's passkey tests: Hallow Keys' OWN NativeServer (the pipe the extension's host and Hallow
// Browser talk to) with a MADE-UP vault in the test folder (HALLOW_KEYS_TESTDIR - never the real %APPDATA%\HallowKeys). No window.
// stdin: lock | unlock | log | count | quit. stdout: "ready", then one JSON line per command.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using HallowKeys;

static class PkHost
{
    static int Main()
    {
        var dir = Environment.GetEnvironmentVariable("HALLOW_KEYS_TESTDIR");
        if (string.IsNullOrEmpty(dir) || KeysSettings.TestDir == null || !KeysSettings.Dir.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("no test folder"); return 2; }
        if (KeysSettings.Dir.IndexOf(@"\AppData\Roaming\", StringComparison.OrdinalIgnoreCase) >= 0) { Console.WriteLine("refusing a real data folder"); return 2; }
        var vp = Path.Combine(dir, "pkvault", "vault.hkvault"); Directory.CreateDirectory(Path.GetDirectoryName(vp));
        foreach (var x in new[] { vp, vp + ".bak" }) if (File.Exists(x)) File.Delete(x);
        string code; var v = Vault.Create(vp, Encoding.UTF8.GetBytes("made-up test master password 115"), out code);
        Vault cur = v; var log = new List<string>(); var gate = new object(); int opens = 0;
        var ns = new NativeServer(fn => { lock (gate) return fn(); }, () => cur, () => { lock (log) { opens++; log.Add("open asked"); } }, s => { lock (log) log.Add("saved: " + s); });
        ns.Log = s => { lock (log) log.Add(s); }; RelatedOrigins.Log = s => { lock (log) log.Add(s); };
        ns.Start();
        Console.WriteLine("ready " + NativeHost.PipeName); Console.Out.Flush();
        var js = new JavaScriptSerializer();
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            line = line.Trim();
            if (line == "quit") break;
            if (line == "lock") cur = null;
            if (line == "unlock") cur = v;
            object res;
            lock (gate) lock (log)
                res = new Dictionary<string, object> { { "locked", cur == null }, { "passkeys", v.Data.Items.Count(i => i.Type == "passkey") }, { "opens", opens }, { "log", line == "log" ? log.ToList() : new List<string>() },
                    { "rpIds", v.Data.Items.Where(i => i.Type == "passkey").Select(i => ItemTypes.Val(i, "rpId")).ToList() } };
            Console.WriteLine(js.Serialize(res)); Console.Out.Flush();
        }
        ns.Stop();
        return 0;
    }
}
