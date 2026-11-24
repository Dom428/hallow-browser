// Hallow Browser 1.15.1 - developer test commands for the new tab page (only with HALLOW_BROWSER_TESTDIR, like the others):
// made-up history, data.json / family.json written now, an off-screen render of the page at a given size / zoom / look (a window at
// -32000, never shown or focused, its page muted) with a check that no element boxes overlap, and the Shield relay. Test profiles only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        // every box that must not overlap another (one that holds the other doesn't count); a game / clip card counts only where its
        // row shows it. Also: sideways scrolling of the page, the footer under the content, the cards' two-line titles.
        public const string NtChecker = @"(function(){
var sel=['#dh','#clk','main>h1','main>p','form','.tl','#hf section>h2','#hf .hr','#hf .hc','.ov','#gc section','#fam>div','#t'];
var els=[];sel.forEach(function(s){document.querySelectorAll(s).forEach(function(e){if(els.indexOf(e)<0)els.push(e);});});
function box(e){var r=e.getBoundingClientRect();var b={l:r.left,t:r.top,r:r.right,b:r.bottom};if(e.classList.contains('hc')){var p=e.closest('.hr').getBoundingClientRect();b={l:Math.max(b.l,p.left),t:Math.max(b.t,p.top),r:Math.min(b.r,p.right),b:Math.min(b.b,p.bottom)};}return b;}
function name(e){return e.tagName.toLowerCase()+(e.id?'#'+e.id:'')+(e.className&&typeof e.className==='string'?'.'+e.className.split(' ').join('.'):'')+' '+JSON.stringify((e.textContent||'').trim().slice(0,24));}
var vis=els.filter(function(e){var cs=getComputedStyle(e);var b=box(e);return cs.display!=='none'&&cs.visibility!=='hidden'&&b.r-b.l>1&&b.b-b.t>1;});
var over=[];for(var i=0;i<vis.length;i++)for(var j=i+1;j<vis.length;j++){var a=vis[i],c=vis[j];if(a.contains(c)||c.contains(a))continue;var x=box(a),y=box(c);var w=Math.min(x.r,y.r)-Math.max(x.l,y.l),h=Math.min(x.b,y.b)-Math.max(x.t,y.t);if(w>1&&h>1)over.push(name(a)+' x '+name(c)+' ('+Math.round(w)+'x'+Math.round(h)+')');}
var main=document.querySelector('main').getBoundingClientRect(),ft=document.querySelector('footer').getBoundingClientRect();
var titles=[].slice.call(document.querySelectorAll('#hf .hc .t')).map(function(t){return {n:t.textContent,h:t.clientHeight,lines:Math.round(t.scrollHeight/16),cut:t.scrollWidth>t.clientWidth+1};});
var rows=[].slice.call(document.querySelectorAll('#hf .hr')).map(function(r){return {sw:r.scrollWidth,cw:r.clientWidth,ox:getComputedStyle(r).overflowX};});
return JSON.stringify({vw:innerWidth,vh:innerHeight,n:vis.length,overlaps:over,hscroll:document.documentElement.scrollWidth>innerWidth+1,scrollH:document.documentElement.scrollHeight,
footerBelow:ft.top>=main.bottom-0.5,chips:[].slice.call(document.querySelectorAll('#fam>div')).map(function(d){return d.textContent;}),
tiles:[].slice.call(document.querySelectorAll('.tl span')).map(function(d){return d.textContent;}),games:titles,rows:rows,
sections:[].slice.call(document.querySelectorAll('#hf section>h2')).map(function(h){return h.firstChild?h.firstChild.textContent:'';}),
placeholder:document.getElementById('q').placeholder,bg:getComputedStyle(document.body).backgroundColor,fg:getComputedStyle(document.body).color});})()";

        static string V1151(BrowserWindow w, string cmd, string arg)
        {
            switch (cmd)
            {
                case "hist":   // <json list of {Url,Title,Visits,Last}>: made-up history in this (test) profile
                    return Program.OnUi(w, () =>
                    {
                        var list = js.Deserialize<List<HistoryEntry>>(arg);
                        w.EnsureStoresNow(); var added = w.History.Merge(list);
                        return "ok " + added + " " + w.History.Count;
                    });
                case "write":   // data.json and family.json now; both returned
                    return Program.OnUi(w, () =>
                    {
                        var d = w.NewTabJson(); NewTabData.Write(d);
                        var f = NewTabFamily.Build(w.S, DateTime.Now); NewTabFamily.Write(f);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "data", d }, { "family", f } });
                    });
                case "render":   // <w> <h> <zoom> <light 0|1> [png]: the page off-screen at that size, checked
                    return Async(w, async () =>
                    {
                        var a = arg.Split(new[] { ' ' }, 5); int W = int.Parse(a[0]), H = int.Parse(a[1]); double zoom = double.Parse(a[2], CultureInfo.InvariantCulture); bool light = a[3] == "1";
                        var png = a.Length > 4 ? a[4] : null;
                        var page = "test-" + (light ? "light" : "dark") + ".html"; var pf = Path.Combine(Engine.NewTabDir, page);
                        File.WriteAllText(pf, Engine.NewTabHtml(w.S.Search, Program.AccentHex, light), new UTF8Encoding(false));
                        var win = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, Width = W, Height = H, Title = "Hallow test render" };
                        var v = new WebView2(); win.Content = v;
                        try
                        {
                            win.Show();
                            await Engine.Ensure(v); var c = v.CoreWebView2; c.IsMuted = true;   // muted before it loads anything
                            try { c.SetVirtualHostNameToFolderMapping(Omni.NewTabHost, Engine.NewTabDir, CoreWebView2HostResourceAccessKind.Deny); } catch { }
                            v.ZoomFactor = zoom;
                            await Panels.NavigateAndWait(c, "https://" + Omni.NewTabHost + "/" + page);
                            await Panels.Eval(c, "hallowRefresh()"); await Panels.Eval(c, "window.hallowFamily?hallowFamily():0");
                            await Task.Delay(700);
                            var r = await Panels.Eval(c, NtChecker);
                            if (png != null) { Directory.CreateDirectory(Path.GetDirectoryName(png)); using (var fs = File.Create(png)) await c.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs); }
                            return "ok " + js.Deserialize<string>(r);
                        }
                        finally { try { win.Close(); } catch { } try { v.Dispose(); } catch { } try { File.Delete(pf); } catch { } }
                    });
                case "relay":   // Shield's report now; what the new tab chip says
                    return Async(w, async () =>
                    {
                        var r = await w.RelayShield();
                        var chip = NewTabData.Strip(null, File.Exists(ShieldStatus.Path14) ? File.ReadAllText(ShieldStatus.Path14) : null, null, DateTime.Now, BrowserWindow.LastReport);
                        return "ok " + js.Serialize(new Dictionary<string, object> { { "report", r }, { "note", BrowserWindow.RelayNote }, { "chip", chip.Where(x => x["k"] == "SHIELD").Select(x => x["v"]).FirstOrDefault() }, { "ntRelays", BrowserWindow.NewTabRelays } });
                    });
                case "relay-count": return "ok " + js.Serialize(new Dictionary<string, object> { { "ntRelays", BrowserWindow.NewTabRelays } });
                case "shieldstats":   // Shield's own stored stats for today (read in Shield's page, as its options page does)
                    return Async(w, async () =>
                    {
                        var v = new WebView2 { Visibility = Visibility.Hidden, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                        w.AddHidden(v);
                        try
                        {
                            await Engine.Ensure(v); v.CoreWebView2.IsMuted = true;
                            await Panels.NavigateAndWait(v.CoreWebView2, w.ShieldPage("family.js"));
                            var r = await Panels.Eval(v.CoreWebView2, ("(async function(){const s=(await import('./stats.js'));const o=await chrome.storage.local.get({stats:null});const d=o.stats&&o.stats.days[s.dayOf(Date.now())]||{};return JSON.stringify({relays:RELAYS,today:(d.ads||0)+(d.trackers||0)+(d.other||0),last:o.stats?o.stats.last:0,feedback:await chrome.permissions.contains({permissions:['declarativeNetRequestFeedback']})});})()").Replace("RELAYS", BrowserWindow.RelaysStarted.ToString()));
                            return "ok " + js.Deserialize<string>(r);
                        }
                        finally { w.RemoveHidden(v); try { v.Dispose(); } catch { } }
                    });
            }
            return "error unknown test command";
        }
    }

    public partial class BrowserWindow
    {
        public void AddHidden(UIElement v) { content.Children.Add(v); }
        public void RemoveHidden(UIElement v) { content.Children.Remove(v); }
    }
}
