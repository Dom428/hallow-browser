// Hallow Browser 1.4 - Shield's "blocked on this page" number and its Hallow family counts.
// WebView2 has no way to grant an extension an optional permission, and Shield's own request for it (chrome.permissions.request,
// even from its popup with a click) never gets an answer in WebView2 - there's no permission prompt. So the browser loads Shield from
// its own copy (LocalAppData\HallowBrowser\ShieldLoaded\<version>) where "declarativeNetRequestFeedback" is a required permission:
// then Shield's popup can ask the engine what it blocked on the page (getMatchedRules) and Shield keeps its blocked counts. The copy
// is added over the installed Shield (same extension id), which keeps Shield's settings, filters and stats.
// Native messaging doesn't exist in WebView2 either, so the browser relays the family counts itself: every 5 minutes it opens a page
// of Shield's own (its family.js, in Shield's origin, never shown), which reads Shield's stats and builds the report with Shield's own
// familyReport() - counts only, never a site - and merges it into %APPDATA%\HallowShield\status.json in the bridge's format, keeping the
// other browsers' entries.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public static partial class ShieldHost
    {
        public const string Feedback = "declarativeNetRequestFeedback";
        public static string LoadedDir { get { return Path.Combine(BrowserSettings.LocalDir, "ShieldLoaded"); } }
        // the manifest with the optional permission made a required one (null when it can't be read)
        public static string PatchManifest(string json)
        {
            try
            {
                var js2 = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                var m = js2.Deserialize<Dictionary<string, object>>(json); if (m == null) return null;
                object p; var perms = m.TryGetValue("permissions", out p) && p is ArrayList ? ((ArrayList)p).Cast<object>().ToList() : new List<object>();
                if (!perms.Contains(Feedback)) perms.Add(Feedback);
                m["permissions"] = perms;
                object o; if (m.TryGetValue("optional_permissions", out o) && o is ArrayList) { var opt = ((ArrayList)o).Cast<object>().Where(x => (x as string) != Feedback).ToList(); if (opt.Count > 0) m["optional_permissions"] = opt; else m.Remove("optional_permissions"); }
                return js2.Serialize(m);
            }
            catch { return null; }
        }
        // the folder the browser loads: the patched copy (made once per Shield version / folder), or the original if that fails
        public static string Patched(string folder, Version v)
        {
            try
            {
                var mf = Path.Combine(folder, "manifest.json");
                var stamp = Path.GetFullPath(folder) + "|" + File.GetLastWriteTimeUtc(mf).Ticks + "|" + new FileInfo(mf).Length + "|hallow-1.4";
                var dest = Path.Combine(LoadedDir, v == null ? "x" : v.ToString());
                var marker = Path.Combine(dest, ".hallow-source");
                if (File.Exists(marker) && File.ReadAllText(marker) == stamp && File.Exists(Path.Combine(dest, "manifest.json"))) return dest;
                var patched = PatchManifest(File.ReadAllText(mf)); if (patched == null) return folder;
                var tmp = dest + ".tmp"; if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Copy(folder, tmp);
                File.WriteAllText(Path.Combine(tmp, "manifest.json"), patched, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(tmp, ".hallow-source"), stamp);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.Move(tmp, dest);
                foreach (var d in Directory.GetDirectories(LoadedDir)) if (!string.Equals(d, dest, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(d, true); } catch { }
                return dest;
            }
            catch { return folder; }
        }
        static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) { var n = Path.GetFileName(f); if (n.StartsWith(".hallow")) continue; File.Copy(f, Path.Combine(to, n), true); }
            foreach (var d in Directory.GetDirectories(from)) { var n = Path.GetFileName(d); if (n == "_metadata") continue; Copy(d, Path.Combine(to, n)); }
        }
    }

    // %APPDATA%\HallowShield\status.json in Hallow Shield's bridge format (see Shield's README, "Hallow family")
    public class ShieldEntry { public string Name, Profile, Id, Version, Level, Day; public long? BlockedToday, BlockedWeek, BannersToday; public DateTime Updated; }
    public static class ShieldStatus
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        public static readonly TimeSpan Stale = TimeSpan.FromDays(2), Forget = TimeSpan.FromDays(30);
        public static string Path14 { get { return Path.Combine(Family.DataDir("Shield"), "status.json"); } }
        static string Str(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static long? Count(Dictionary<string, object> d, string k)
        {
            object o; if (d == null || !d.TryGetValue(k, out o) || o == null) return null;
            try { var n = Convert.ToInt64(o, CultureInfo.InvariantCulture); return n < 0 ? (long?)null : Math.Min(n, 1000000000000L); } catch { return null; }
        }
        // the report Shield's familyReport() made (only its fields are kept - never anything else)
        public static ShieldEntry FromReport(string json, DateTime nowUtc)
        {
            try
            {
                var d = js.Deserialize<Dictionary<string, object>>(json); if (d == null) return null;
                var id = Str(d, "profile") ?? ""; if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]{1,32}$")) id = "";
                var lv = Str(d, "level"); if (lv != "basic" && lv != "optimal" && lv != "complete") lv = null;
                var day = Str(d, "day"); if (day != null && !System.Text.RegularExpressions.Regex.IsMatch(day, @"^\d{4}-\d{2}-\d{2}$")) day = null;
                var ver = Str(d, "version"); if (ver != null && ver.Length > 20) ver = ver.Substring(0, 20);
                return new ShieldEntry { Name = "Hallow Browser", Profile = "Default", Id = id, Version = ver, Level = lv, Day = day, BlockedToday = Count(d, "blockedToday"), BlockedWeek = Count(d, "blockedWeek"), BannersToday = Count(d, "bannersToday"), Updated = nowUtc };
            }
            catch { return null; }
        }
        public static List<ShieldEntry> Load(string text, out string version)
        {
            var list = new List<ShieldEntry>(); version = null;
            try
            {
                var d = text == null ? null : js.Deserialize<Dictionary<string, object>>(text); if (d == null) return list;
                version = Str(d, "version");
                object o; var arr = d.TryGetValue("browsers", out o) ? o as ArrayList : null; if (arr == null) return list;
                foreach (var x in arr.OfType<Dictionary<string, object>>())
                {
                    DateTime t; if (!DateTime.TryParse(Str(x, "updated") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) continue;
                    list.Add(new ShieldEntry { Name = Str(x, "name") ?? "Chrome", Profile = Str(x, "profile") ?? "Default", Id = Str(x, "id") ?? "", Version = Str(x, "version"), Level = Str(x, "level"), Day = Str(x, "day"),
                        BlockedToday = Count(x, "blockedToday"), BlockedWeek = Count(x, "blockedWeek"), BannersToday = Count(x, "bannersToday"), Updated = t });
                }
            }
            catch { }
            return list;
        }
        // our entry replaces the one with the same random profile id (whatever the name), the others stay; 30-day-old ones go
        public static List<ShieldEntry> Merge(List<ShieldEntry> list, ShieldEntry e, DateTime nowUtc)
        {
            var o = list.Where(x => nowUtc - x.Updated < Forget).ToList();
            var same = o.FirstOrDefault(x => x.Id == e.Id && (x.Name == e.Name || e.Id.Length > 0));
            if (same != null) { e.Profile = same.Name == e.Name ? same.Profile : e.Profile; o.Remove(same); }
            var taken = new HashSet<string>(o.Where(x => x.Name == e.Name).Select(x => x.Profile));
            if (same == null || taken.Contains(e.Profile)) { e.Profile = "Default"; for (int i = 2; taken.Contains(e.Profile); i++) e.Profile = "Profile " + i; }
            o.Add(e);
            return o;
        }
        public static bool Fresh(ShieldEntry x, DateTime nowUtc) { return nowUtc - x.Updated < Stale && x.Updated <= nowUtc.AddMinutes(5); }
        public static bool Today(ShieldEntry x, DateTime nowUtc)
        {
            if (!Fresh(x, nowUtc)) return false;
            var today = nowUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return x.Day != null ? x.Day == today : x.Updated.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == today;
        }
        static long? Sum(IEnumerable<ShieldEntry> xs, Func<ShieldEntry, long?> f) { long? s = null; foreach (var x in xs) { var n = f(x); if (n.HasValue) s = (s ?? 0) + n.Value; } return s; }
        static string Iso(DateTime utc) { return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture); }
        public static string Render(List<ShieldEntry> list, string version, DateTime nowUtc)
        {
            var browsers = list.Select(x => new Dictionary<string, object> { { "name", x.Name }, { "profile", x.Profile }, { "version", x.Version }, { "level", x.Level }, { "blockedToday", x.BlockedToday }, { "blockedWeek", x.BlockedWeek },
                { "bannersToday", x.BannersToday }, { "updated", Iso(x.Updated) }, { "day", x.Day }, { "id", x.Id } }).ToList();
            var fresh = list.Where(x => Fresh(x, nowUtc)).ToList(); var today = list.Where(x => Today(x, nowUtc)).ToList();
            return js.Serialize(new Dictionary<string, object> { { "version", version ?? "1.7" }, { "updated", Iso(nowUtc) }, { "browsers", browsers },
                { "blockedToday", Sum(today, x => x.BlockedToday) }, { "blockedWeek", Sum(fresh, x => x.BlockedWeek) }, { "bannersToday", Sum(today, x => x.BannersToday) } });
        }
        // under the bridge's own mutex, written atomically (a temp file swapped in) - so a reader never sees half a file
        public static bool Record(ShieldEntry e, DateTime nowUtc)
        {
            if (e == null) return false;
            using (var m = new Mutex(false, @"Local\HallowShieldBridge." + Environment.UserName))
            {
                bool own = false;
                try
                {
                    try { own = m.WaitOne(5000); } catch (AbandonedMutexException) { own = true; }
                    var path = Path14; string text = null;
                    try { if (File.Exists(path)) text = File.ReadAllText(path, Encoding.UTF8); } catch { }
                    string ver; var list = Load(text, out ver);
                    var json = Render(Merge(list, e, nowUtc), ver, nowUtc);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));
                    try { for (int i = 0; ; i++) { try { if (File.Exists(path)) File.Replace(tmp, path, null, true); else File.Move(tmp, path); break; } catch (IOException) { if (i >= 20) throw; Thread.Sleep(50); } catch (UnauthorizedAccessException) { if (i >= 20) throw; Thread.Sleep(50); } } }
                    finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                    return true;
                }
                catch { return false; }
                finally { if (own) m.ReleaseMutex(); }
            }
        }
    }

    public partial class BrowserWindow
    {
        public bool ShieldCounts;          // the patched copy is loaded: per-page counts and blocked stats work
        public static string RelayNote, LastReport;
        static DispatcherTimer relayTimer; static bool relayBusy;
        // Shield's own code builds the report (its family.js, imported in a page of Shield's origin)
        const string RelayScript = "(async function(){const f=await import('./family.js');try{await chrome.runtime.sendMessage({t:'stats-get'});}catch(e){}await new Promise(r=>setTimeout(r,800));" +
            "const o=await chrome.storage.local.get({stats:null,level:null,familyProfile:''});let p=o.familyProfile;if(!p){p=f.newProfileId();await chrome.storage.local.set({familyProfile:p});}" +
            "const counted=await chrome.permissions.contains({permissions:['declarativeNetRequestFeedback']});" +
            "return JSON.stringify(f.familyReport({stats:o.stats,counted:counted,level:o.level,version:chrome.runtime.getManifest().version,browser:'Hallow Browser',profile:p}));})()";
        void StartShieldRelay()
        {
            if (relayTimer != null || snap) return;
            relayTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
            relayTimer.Tick += (a, b) => { var m = Program.First; if (m != null) { var _ = m.RelayShield(); } };
            relayTimer.Start();
            var __ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(20000); var m = Program.First; if (m != null) await m.RelayShield(); }), DispatcherPriority.ApplicationIdle);
        }
        public async Task<string> RelayShield()
        {
            if (!S.ShareShieldCounts) { RelayNote = "off"; return null; }
            if (ShieldState != "extension" || ShieldId == null || relayBusy || snap || Private) return null;
            if (Game != null && !BrowserSettings.Offscreen) return null;   // not while a game runs (the counts catch up after it)
            relayBusy = true; WebView2 v = null;
            try
            {
                v = new WebView2 { Visibility = Visibility.Hidden, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                content.Children.Add(v);
                await Engine.Ensure(v);
                await Panels.NavigateAndWait(v.CoreWebView2, ShieldPage("family.js"));
                var r = await Panels.Eval(v.CoreWebView2, RelayScript);
                var json = new JavaScriptSerializer().Deserialize<string>(r);
                var e = ShieldStatus.FromReport(json, DateTime.UtcNow);
                var ok = await Task.Run(() => ShieldStatus.Record(e, DateTime.UtcNow));
                LastReport = json; RelayNote = ok ? "written " + DateTime.Now.ToString("HH:mm") : "couldn't write status.json";
                return json;
            }
            catch (Exception ex) { RelayNote = "failed: " + ex.Message; return null; }
            finally { relayBusy = false; if (v != null) { content.Children.Remove(v); try { v.Dispose(); } catch { } } }
        }
    }
}
