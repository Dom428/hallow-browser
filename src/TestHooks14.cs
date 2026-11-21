// Hallow Browser 1.4 - developer test commands for windows, private windows, site permissions, HTTPS-only, extensions, Shield's
// counts and relay, and the game corner (only with HALLOW_BROWSER_TESTDIR, like the others): they call the same code the buttons call.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static BrowserWindow WinAt(string s) { int i; return int.TryParse(s, out i) && i >= 0 && i < Program.Windows.Count ? Program.Windows[i] : null; }
        static object TabsOf(BrowserWindow w) { return w.Tabs.Select((t, i) => new Dictionary<string, object> { { "i", i }, { "url", t.Url }, { "title", t.Title }, { "active", t == w.Active }, { "ready", t.Ready }, { "view", t.View != null }, { "muted", t.Muted } }).ToList(); }
        static string V14(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                // ---- windows
                case "wlist":
                    return Program.OnUi(w, () => "ok " + js.Serialize(Program.Windows.Select((x, i) => new Dictionary<string, object> { { "i", i }, { "private", x.Private }, { "space", x.SpaceId }, { "spaceName", x.CurrentSpace == null ? null : x.CurrentSpace.Name }, { "primary", x.IsPrimary }, { "gone", x.Gone }, { "shield", x.ShieldState }, { "tabs", TabsOf(x) } }).ToList()));
                case "wnew": return Program.OnUi(w, () => { Program.OpenWindow(false, arg.Length > 0 ? arg : null, null); return "ok " + (Program.Windows.Count - 1); });
                case "wpriv": return Program.OnUi(w, () => { Program.OpenWindow(true, arg.Length > 0 ? arg : null, null); return "ok " + (Program.Windows.Count - 1); });
                case "wmove":
                    return Program.OnUi(w, () =>
                    {
                        var from = WinAt(p[0]); if (from == null) return "error no window"; int ti; if (!int.TryParse(p[1], out ti) || ti < 0 || ti >= from.Tabs.Count) return "error no tab";
                        var t = from.Tabs[ti];
                        if (p[2] == "new") { var nw = Program.TearOff(t, from, null); return nw == null ? "error" : "ok " + Program.Windows.IndexOf(nw); }
                        var to = WinAt(p[2]); int at = p.Length > 3 ? int.Parse(p[3]) : -1;
                        return Program.MoveTab(t, from, to, at) ? "ok " + Program.Windows.IndexOf(to) : "error refused";
                    });
                case "wclose": return Program.OnUi(w, () => { var x = WinAt(arg); if (x == null) return "error"; x.Close(); return "ok"; });
                case "weval":
                {
                    var x = WinAt(p[0]); int ti; if (x == null || !int.TryParse(p[1], out ti)) return "error"; var expr = arg.Substring(arg.IndexOf(' ', arg.IndexOf(' ') + 1) + 1);
                    return Async(w, async () => { if (ti >= x.Tabs.Count || x.Tabs[ti].Core == null) return "error no view"; return "ok " + await Panels.Eval(x.Tabs[ti].Core, expr); });
                }
                case "wnav":
                {
                    var x = WinAt(p[0]); int ti; if (x == null || !int.TryParse(p[1], out ti)) return "error";
                    return Async(w, async () => { if (ti >= x.Tabs.Count || x.Tabs[ti].Core == null) return "error no view"; await Panels.NavigateAndWait(x.Tabs[ti].Core, p[2]); return "ok " + x.Tabs[ti].Url; });
                }
                case "hist": return Program.OnUi(w, () => { var h = Program.SharedHistory; if (h == null) return "ok -1"; return "ok " + h.All().Count(e => e.Url != null && e.Url.Contains(arg)); });
                case "typed": return Program.OnUi(w, () => { lock (w.S.Typed) return "ok " + w.S.Typed.Count(u => u.Contains(arg)); });
                case "savenow": return Program.OnUi(w, () => { Program.SaveAll(); var h = Program.SharedHistory; if (h != null) h.Save(); return "ok"; });
                // ---- site permissions and settings
                case "ask": return Program.OnUi(w, () => { var x = Program.Windows.FirstOrDefault(y => y.PendingAsk != null); var a = x == null ? null : x.PendingAsk; return "ok " + (a == null ? "null" : js.Serialize(new Dictionary<string, object> { { "host", a.Host }, { "origin", a.Origin }, { "kind", a.Kind.ToString() }, { "window", Program.Windows.IndexOf(x) } })); });
                case "answer": return Program.OnUi(w, () => { var x = Program.Windows.FirstOrDefault(y => y.PendingAsk != null); if (x == null) return "error nothing asked"; x.Answer(arg); return "ok"; });
                case "perms": return Async(w, async () => "ok " + js.Serialize((await w.PermissionsFor(arg == "*" ? null : arg)).Select(x => x.PermissionKind + " " + x.PermissionOrigin + " " + x.PermissionState).ToList()));
                case "rule":
                    return Program.OnUi(w, () =>
                    {
                        var r = SiteRules.Get(w.S, p[0], true); var v = p.Length > 2 && p[2] != "null" ? p[2] : null;
                        if (p[1] == "popups") r.Popups = v; else if (p[1] == "sound") r.Sound = v; else if (p[1] == "script") r.Script = v; else if (p[1] == "http") r.Http = v == "true"; else return "error";
                        SiteRules.Tidy(w.S); w.S.Save(); return "ok";
                    });
                case "sites": return Async(w, async () => "ok " + js.Serialize(await w.SiteHosts()));
                case "reset": return Async(w, async () => { await w.ResetSite(arg); return "ok"; });
                case "cookies": return Async(w, async () => "ok " + await w.CookieCount(arg));
                case "delcookies": return Async(w, async () => "ok " + await w.DeleteCookies(arg));
                case "popups": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "blocked", w.PopupsBlocked }, { "last", w.LastPopup }, { "tabs", w.Tabs.Count } }));
                case "sitespage": return Async(w, async () => { var t = w.NewTab(Omni.SitesUrl, true); await Task.Delay(300); await w.RefreshSites(); return "ok " + (t.Page != null); });
                // ---- HTTPS-only
                case "https": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "upgrades", w.HttpsUpgrades }, { "asked", w.HttpsAsked }, { "on", w.S.HttpsOnly }, { "blocked", w.Tabs.Select(t => t.HttpsBlocked).ToList() }, { "urls", w.Tabs.Select(t => t.Url).ToList() }, { "trace", BrowserWindow.HttpsTrace.ToList() } }));
                case "httpsgo": return Program.OnUi(w, () => { var t = TabAt(w, p[0]); if (t == null) return "error no tab"; if (t.HttpsBlocked == null) return "error not asking"; w.ContinueHttp(t, p.Length > 1 && p[1] == "always"); return "ok"; });
                case "httpsclick": { var t = TabAt(w, p[0]); if (t == null) return "error"; var id = p[1]; return Async(w, async () => t.Core == null ? "error" : "ok " + await Panels.Eval(t.Core, "(function(){var b=document.getElementById('" + id.Replace("'", "") + "');if(!b)return 'none';b.click();return 'clicked';})()")); }
                // ---- extensions
                case "xload": return Async(w, async () => { var e = await w.LoadUnpacked(arg); return e == null ? "ok " + js.Serialize(w.S.Extensions.Select(x => x.Id).ToList()) : "error " + e; });
                case "xlist":
                    return Async(w, async () =>
                    {
                        var prof = w.NormalProfile; var have = prof == null ? null : await prof.GetBrowserExtensionsAsync();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "mine", w.S.Extensions.Select(x => new Dictionary<string, object> { { "id", x.Id }, { "name", x.Name }, { "enabled", x.Enabled }, { "folder", x.Folder }, { "exists", Directory.Exists(x.Folder) } }).ToList() },
                            { "engine", have == null ? null : have.Select(x => x.Name + "|" + x.Id + "|" + x.IsEnabled).ToList() } });
                    });
                case "xenable": return Async(w, async () => { var x = w.S.Extensions.FirstOrDefault(e => e.Id == p[0]); if (x == null) return "error"; await w.SetExtEnabled(x, p[1] == "on"); return "ok"; });
                case "xremove": return Async(w, async () => { var x = w.S.Extensions.FirstOrDefault(e => e.Id == arg); if (x == null) return "error"; await w.RemoveExt(x); return "ok"; });
                case "xpopup": return Async(w, async () => { var x = w.S.Extensions.FirstOrDefault(e => e.Id == arg); if (x == null) return "error"; var src = await w.ExtPopup(x, null); await Task.Delay(400); var c = BrowserWindow.ExtPopupCore; return "ok " + js.Serialize(new Dictionary<string, object> { { "src", src }, { "text", c == null ? null : await Panels.Eval(c, "document.body.innerText") } }); });
                // ---- Shield
                case "shstate": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "state", w.ShieldState }, { "id", w.ShieldId }, { "counts", w.ShieldCounts }, { "folder", w.ShieldFolder }, { "loaded", w.S.ShieldLoaded }, { "relay", BrowserWindow.RelayNote } }));
                case "shcount":
                    return Async(w, async () =>
                    {
                        var t = TabAt(w, arg); if (t != null) w.Activate(t);
                        var v = await Panels.Shield(w, null); if (v == null) return "error no popup";
                        string n = null; for (int i = 0; i < 30; i++) { await Task.Delay(250); n = await Panels.Eval(v.CoreWebView2, "document.getElementById('count')?document.getElementById('count').textContent:''"); if (n != "\"\"" && n != "\"-\"" && n != "\"0\"") break; }
                        var txt = await Panels.Eval(v.CoreWebView2, "document.getElementById('countText')?document.getElementById('countText').textContent:''");
                        Panels.HideShield();
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "count", js.Deserialize<string>(n) }, { "text", js.Deserialize<string>(txt) }, { "note", Panels.ShieldTabNote } });
                    });
                case "relay": return Async(w, async () => { var r = await w.RelayShield(); string f = null; try { f = File.ReadAllText(ShieldStatus.Path14); } catch { } return "ok " + js.Serialize(new Dictionary<string, object> { { "report", r }, { "note", BrowserWindow.RelayNote }, { "file", f } }); });
                // ---- the game corner and settings
                case "corner":
                    return Async(w, async () =>
                    {
                        int before = BrowserWindow.CornerFetches; w.GameCornerTick(true);
                        for (int i = 0; i < 120 && BrowserWindow.CornerFetches == before; i++) await Task.Delay(250);
                        await Task.Delay(500);
                        string f = null; try { f = File.ReadAllText(GameCorner.CachePath); } catch { }
                        return "ok " + (f ?? "null");
                    });
                case "ntdata": return Program.OnUi(w, () => "ok " + w.NewTabJson());
                case "settings": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "httpsOnly", w.S.HttpsOnly }, { "secureDns", w.S.SecureDns }, { "dnsApplied", Engine.SecureDnsApplied }, { "share", w.S.ShareShieldCounts }, { "corner", w.S.GameCorner }, { "sites", w.S.Sites.Count }, { "exts", w.S.Extensions.Count } }));
                case "set":
                    return Program.OnUi(w, () =>
                    {
                        var v = p.Length > 1 ? p[1] : "";
                        if (p[0] == "httpsOnly") w.S.HttpsOnly = v == "true"; else if (p[0] == "secureDns") w.S.SecureDns = v; else if (p[0] == "share") w.S.ShareShieldCounts = v == "true"; else if (p[0] == "corner") w.S.GameCorner = v == "true"; else return "error";
                        w.S.Save(); return "ok";
                    });
            }
            return "error unknown 1.4 test command";
        }
    }
}
