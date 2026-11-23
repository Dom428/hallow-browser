// Hallow Browser 1.14.2 - "Report a Twitch problem" (the Twitch button's panel, More tools, Ctrl+K, Ctrl+Shift+F12 on twitch.tv).
// Saves one anonymised file to your Downloads folder - "Hallow Twitch report <date time>.zip" with report.json and player.png - for the
// developer to see what the player was doing: the browser and Twitch script versions, the script's events and playlist decisions,
// the player's and video's state, the elements over the player (tag, class names and Twitch's data-a-target / data-test-selector
// names only - no text, no chat, no user names, no cookies or sign-in), and a small picture of the player area only. The channel's
// name is replaced by [channel] everywhere. Nothing is sent anywhere: you choose whether to pass the file on.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class TwitchReport
    {
        public const string Keys = "Ctrl+Shift+F12";
        // the page's state, read by the browser itself (works even if the Twitch script never ran on the page)
        public const string Probe = @"(function(){
function cls(el){var c=(typeof el.className==='string'?el.className:(el.getAttribute&&el.getAttribute('class'))||'');return c.split(/\s+/).filter(function(x){return /^[A-Za-z][\w-]{0,60}$/.test(x)}).slice(0,8).join(' ');}
function d(el){if(!el||el.nodeType!==1)return null;var cs=getComputedStyle(el),r=el.getBoundingClientRect(),o={tag:el.tagName.toLowerCase()};var c=cls(el);if(c)o.cls=c;
['data-a-target','data-test-selector','data-a-player-state','data-a-player-type','role'].forEach(function(n){var v=el.getAttribute(n);if(v)o[n]=v.slice(0,60);});
o.bg=cs.backgroundColor;o.disp=cs.display;o.vis=cs.visibility;o.op=cs.opacity;o.pos=cs.position;o.z=cs.zIndex;o.rect=[Math.round(r.left),Math.round(r.top),Math.round(r.width),Math.round(r.height)];
o.textLen=(el.innerText||'').trim().length;if(el.tagName==='IFRAME'){try{o.host=el.src?new URL(el.src).host:'(none)';}catch(e){o.host='?';}}if(el.tagName==='HALLOW-ADBREAK')o.hallowCover=true;return o;}
var vs=[].slice.call(document.querySelectorAll('video')),main=null,area=0;vs.forEach(function(v){var r=v.getBoundingClientRect();if(r.width*r.height>area){area=r.width*r.height;main=v;}});
var pl=document.querySelector('[data-a-target=""video-player""], .video-player');var pr=pl?pl.getBoundingClientRect():(main?main.getBoundingClientRect():null);
var out={page:/^\/[A-Za-z0-9_]{3,25}\/?$/.test(location.pathname)?'channel':'other',vis:document.visibilityState,dpr:devicePixelRatio,vw:innerWidth,vh:innerHeight,fullscreen:!!document.fullscreenElement,
theme:document.documentElement.className.indexOf('theme-light')>=0?'light':'dark',player:pr?[Math.round(pr.left),Math.round(pr.top),Math.round(pr.width),Math.round(pr.height)]:null};
out.videos=vs.map(function(v){var r=v.getBoundingClientRect(),cs=getComputedStyle(v),b=0;try{if(v.buffered.length)b=+(v.buffered.end(v.buffered.length-1)-v.currentTime).toFixed(1);}catch(e){}
return {main:v===main,rs:v.readyState,ns:v.networkState,paused:v.paused,ended:v.ended,ct:+v.currentTime.toFixed(1),muted:v.muted,vol:+v.volume.toFixed(2),w:v.videoWidth,h:v.videoHeight,ahead:b,err:v.error?v.error.code:0,
src:(v.currentSrc||'').split(':')[0],rect:[Math.round(r.left),Math.round(r.top),Math.round(r.width),Math.round(r.height)],disp:cs.display,vis:cs.visibility,op:cs.opacity};});
if(main){try{var c=document.createElement('canvas');c.width=32;c.height=18;var x=c.getContext('2d');x.drawImage(main,0,0,32,18);var p=x.getImageData(0,0,32,18).data,wh=0,bl=0,s=0,lo=255,hi=0;
for(var i=0;i<p.length;i+=4){var l=(p[i]+p[i+1]+p[i+2])/3;s+=l;if(l<lo)lo=l;if(l>hi)hi=l;if(p[i]>=235&&p[i+1]>=235&&p[i+2]>=235)wh++;if(l<=16)bl++;}
out.picture={white:+(wh/576).toFixed(2),black:+(bl/576).toFixed(2),mean:Math.round(s/576),spread:Math.round(hi-lo)};}catch(e){out.picture=String(e&&e.name||e);}
var hid=[];for(var q=main.parentElement;q;q=q.parentElement){var qs=getComputedStyle(q);if(qs.display==='none'||qs.visibility==='hidden'||+qs.opacity===0)hid.push(d(q));}out.hiddenAround=hid;}
if(pr&&pr.width>0){out.over={};[[0.5,0.5],[0.2,0.2],[0.8,0.2],[0.2,0.8],[0.8,0.8]].forEach(function(pt){var st=document.elementsFromPoint(pr.left+pr.width*pt[0],pr.top+pr.height*pt[1]),a=[];
for(var i=0;i<st.length&&i<10;i++){a.push(d(st[i]));if(st[i]===main)break;}out.over[pt.join(',')]=a;});}
var ads=document.querySelectorAll('[data-a-target*=""ad""],[data-test-selector*=""ad""],[class*=""isplay-ad""],[class*=""DisplayAd""],[class*=""squeezeback""],[class*=""pbyp""],[class*=""picture-by-picture""],[class*=""outstream""],[class*=""commercial""],[class*=""video-ad""],[aria-label*=""dvert""],[data-hallow-unblocked],[data-hallow-keep]');
out.adMarkers=[];for(var j=0;j<ads.length&&out.adMarkers.length<40;j++){var e=ads[j],t=e.getAttribute('data-a-target')||'';if(/^(chat|channel|follow|subscribe|user|login|signup|nav|side-nav|home|browse|player-(theatre|fullscreen|settings|mute|play))/i.test(t))continue;var o=d(e);o.hasVideo=!!(main&&e.contains(main));out.adMarkers.push(o);}
var H=window.__hallowTwitch;out.hallow=H?(H.report?JSON.parse(H.report()):{script:H.v,events:(H.events||[]).slice(-60),watch:H.watch||null,resyncs:H.resyncs||0,workers:H.workers||0,note:'an older Twitch script (no report)'}):null;
return JSON.stringify(out);})()";

        // the channel's name (and any name the page script reported) out of the text
        public static string Scrub(string json, string channel)
        {
            if (json == null) return null;
            if (!string.IsNullOrEmpty(channel)) json = Regex.Replace(json, Regex.Escape(channel), "[channel]", RegexOptions.IgnoreCase);
            return json;
        }
        public static string FileName(DateTime now) { return "Hallow Twitch report " + now.ToString("yyyy-MM-dd HH-mm-ss") + ".zip"; }

        // the player area out of a picture of the page, at most 480 px wide
        public static byte[] Crop(byte[] png, int[] rect, double viewW)
        {
            if (png == null || png.Length == 0) return null;
            using (var ms = new MemoryStream(png)) using (var bmp = new Bitmap(ms))
            {
                var k = viewW > 0 ? bmp.Width / viewW : 1.0;
                var r = rect != null && rect.Length == 4 && rect[2] > 20 && rect[3] > 20 ? new Rectangle((int)(rect[0] * k), (int)(rect[1] * k), (int)(rect[2] * k), (int)(rect[3] * k)) : new Rectangle(0, 0, bmp.Width, bmp.Height);
                r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
                if (r.Width < 4 || r.Height < 4) r = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var w = Math.Min(480, r.Width); var h = Math.Max(1, (int)Math.Round(r.Height * (w / (double)r.Width)));
                using (var o = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(o)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(bmp, new Rectangle(0, 0, w, h), r, GraphicsUnit.Pixel); }
                    using (var outMs = new MemoryStream()) { o.Save(outMs, ImageFormat.Png); return outMs.ToArray(); }
                }
            }
        }

        public static string WriteZip(string folder, DateTime now, string json, byte[] png)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName(now)); int n = 2;
            while (File.Exists(path)) path = Path.Combine(folder, Path.GetFileNameWithoutExtension(FileName(now)) + " (" + n++ + ").zip");
            using (var fs = new FileStream(path, FileMode.CreateNew)) using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                using (var s = new StreamWriter(z.CreateEntry("report.json").Open(), new UTF8Encoding(false))) s.Write(json);
                if (png != null) using (var s = z.CreateEntry("player.png", CompressionLevel.NoCompression).Open()) s.Write(png, 0, png.Length);
            }
            return path;
        }
    }

    public partial class BrowserWindow
    {
        public string LastTwitchReport;
        public bool CanReportTwitch(Tab t) { return t != null && t.Core != null && TwitchAdRules.IsTwitch(t.Url); }

        // the menu entries and Ctrl+Shift+F12
        public void ReportTwitchProblem(Tab t)
        {
            if (!CanReportTwitch(t)) { Toast("Open the Twitch tab with the problem first, then Report a Twitch problem (" + TwitchReport.Keys + ").", 6); return; }
            Toast("Saving a Twitch report...", 3);
            var _ = SaveTwitchReport(t, null).ContinueWith(r => Dispatcher.BeginInvoke(new Action(() =>
            {
                var path = r.IsFaulted ? null : r.Result;
                if (path == null) { Toast("The Twitch report couldn't be saved" + (r.Exception != null ? ": " + r.Exception.GetBaseException().Message : "."), 8); return; }
                ToastWith("Twitch report saved: " + path + "\nSend this file to the developer - it has no chat, user names or cookies.", 15,
                    Tuple.Create<string, string, Action>("Show in folder", "folder-open", () => ShowInFolder(path)));
            })));
        }

        // reads the page, takes a small picture of the player and writes the zip; returns its path (tests pass their own folder)
        public async Task<string> SaveTwitchReport(Tab t, string folder)
        {
            var c = t.Core; var now = DateTime.Now; var js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var ch = Twitch.Channel(t.Url);
            string pageJson = null, err = null;
            try { var raw = await c.ExecuteScriptAsync(TwitchReport.Probe); pageJson = js.Deserialize<string>(raw); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
            Dictionary<string, object> page = null;
            try { if (pageJson != null) page = js.Deserialize<Dictionary<string, object>>(pageJson); } catch { }
            byte[] png = null;
            try
            {
                using (var ms = new MemoryStream())
                {
                    await c.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                    int[] rect = null; double vw = 0;
                    if (page != null)
                    {
                        object pr, w; if (page.TryGetValue("player", out pr) && pr is System.Collections.ArrayList) rect = ((System.Collections.ArrayList)pr).Cast<object>().Select(Convert.ToInt32).ToArray();
                        if (page.TryGetValue("vw", out w) && w != null) vw = Convert.ToDouble(w);
                    }
                    png = TwitchReport.Crop(ms.ToArray(), rect, vw);
                }
            }
            catch { png = null; }
            bool muted = false, audio = false; try { muted = c.IsMuted; audio = c.IsDocumentPlayingAudio; } catch { }
            string dl = null; try { if (File.Exists(TwitchAdRules.Downloaded)) dl = "version " + TwitchAdRules.VersionOf(File.ReadAllText(TwitchAdRules.Downloaded, Encoding.UTF8)); } catch { }
            var host = new Dictionary<string, object> {
                { "browser", Program.Version }, { "webview2", Engine.RuntimeVersion }, { "twitchScript", TwitchAdRules.CurrentVersion }, { "twitchScriptSource", TwitchAdRules.Source },
                { "downloadedScript", dl }, { "scriptSaidReady", t.TwScriptV }, { "blockTwitchAds", S.TwitchAds }, { "fixesBetweenUpdates", S.TwitchFixes },
                { "allowListCount", (S.TwitchAllow ?? new List<string>()).Count }, { "thisChannelAllowed", ch != null && S.TwitchAllow != null && S.TwitchAllow.Contains(ch) },
                { "tabSwaps", t.TwSwaps }, { "tabCovers", t.TwCovers }, { "tabCovered", t.TwCovered }, { "mutedByHallow", t.TwMutedByUs }, { "tabMuted", muted }, { "playingAudio", audio },
                { "lastNote", t.TwNote }, { "adRequestsBlocked", TwBlocked }, { "gameMode", Game != null }, { "memoryLow", MemoryLow(t) }, { "bandwidthCap", CapApplied > 0 },
                { "private", Private }, { "shield", ShieldState }, { "active", t == Active }, { "windowMinimized", WindowState == System.Windows.WindowState.Minimized }, { "theatre", Theatre },
                { "asleep", t.Suspended }, { "time", now.ToString("yyyy-MM-dd HH:mm:ss") } };
            var report = new Dictionary<string, object> { { "what", "Hallow Browser - Twitch problem report (anonymised: no chat, user names, cookies or sign-in; the channel is [channel])" },
                { "browser", host }, { "page", page }, { "pageError", err }, { "picture", png != null ? "player.png (" + png.Length / 1024 + " KB)" : "none" } };
            var json = TwitchReport.Scrub(js.Serialize(report), ch);
            var path = TwitchReport.WriteZip(folder ?? Engine.Downloads(), now, json, png);
            LastTwitchReport = path;
            return path;
        }
    }

    public static partial class TestHooks
    {
        static string V1142(BrowserWindow w, string cmd, string arg)
        {
            var p = arg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            switch (cmd)
            {
                case "report":   // <tab> [folder] - what the button saves (tests use their own folder)
                    {
                        var t = TabAt(w, p.Length > 0 ? p[0] : ""); if (t == null) return "error no tab";
                        return Async(w, async () => "ok " + js.Serialize(await w.SaveTwitchReport(t, p.Length > 1 ? p[1] : null)));
                    }
                case "button":   // the menu entry / shortcut as clicked: the toast it shows
                    return Program.OnUi(w, () => { w.ReportTwitchProblem(w.Active); return "ok " + js.Serialize(w.LastToast); });
                case "menu":     // is the entry in More tools on this tab, and its shortcut
                    return Program.OnUi(w, () => { var it = Menu113.Leaves(Menu113.Model(w)).FirstOrDefault(x => x.Text == "Report a Twitch problem"); return "ok " + js.Serialize(it == null ? null : new Dictionary<string, object> { { "keys", it.Keys }, { "enabled", it.Enabled } }); });
                case "click":    // <tab> <js> - runs the script as if the user clicked (a user gesture: a link may open a new tab)
                    {
                        var t = TabAt(w, p.Length > 0 ? p[0] : ""); if (t == null || p.Length < 2) return "error no tab";
                        return Async(w, async () => "ok " + await t.Core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", js.Serialize(new Dictionary<string, object> { { "expression", p[1] }, { "userGesture", true } })));
                    }
                case "last": return Program.OnUi(w, () => "ok " + js.Serialize(w.LastTwitchReport));
                case "toast": return Program.OnUi(w, () => "ok " + js.Serialize(w.LastToast + " | " + string.Join(",", w.LastToastActions)));
            }
            return "error unknown 1.14.2 test command";
        }
    }
}
