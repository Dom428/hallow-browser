// Hallow Browser - the engine: Microsoft Edge WebView2 (the Evergreen runtime that comes with Windows 11 - Microsoft's, not ours).
// One environment for the whole browser (created once, at start), one profile (%LOCALAPPDATA%\HallowBrowser\Profile).
// The runtime is never downloaded by Hallow Browser: if it's missing, the browser says where to get it from Microsoft.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace HallowBrowser
{
    public static partial class Engine
    {
        public static CoreWebView2Environment Env;
        public static Task<CoreWebView2Environment> Starting;
        public static string RuntimeVersion;
        public static readonly Stopwatch Clock = Stopwatch.StartNew();   // since the process started (about)
        public static readonly Dictionary<string, double> Perf = new Dictionary<string, double>();
        public static void Mark(string what) { lock (Perf) if (!Perf.ContainsKey(what)) Perf[what] = SinceStart(); }
        public static double SinceStart() { try { return (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; } catch { return Clock.Elapsed.TotalMilliseconds; } }

        // the runtime's version, or null when it isn't installed (registry first - EdgeUpdate's client key - then the loader)
        public const string ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        public static string FindRuntime()
        {
            foreach (var root in new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\", @"HKEY_LOCAL_MACHINE\SOFTWARE\", @"HKEY_CURRENT_USER\Software\" })
            {
                try { var v = Registry.GetValue(root + ClientKey, "pv", null) as string; if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return v; } catch { }
            }
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { return null; }
        }
        public const string RuntimePage = "https://developer.microsoft.com/microsoft-edge/webview2/";

        public static string ExtraArgs(BrowserSettings s)
        {
            var a = new List<string>();
            // speed: a cap on renderer processes (Chromium keeps sites apart anyway - the cap is soft where isolation needs it)
            a.Add("--renderer-process-limit=8");
            // 1.3: the new tab page's made-up host never goes to DNS - answered "not found" at once. Without this every fresh view waited
            // for the network's DNS to give up on "newtab.hallow" (~2 s here) before showing the page, and the name went out to DNS
            var rules = "MAP " + Omni.NewTabHost + " ~NOTFOUND";
            if (BrowserSettings.HostRules != null) rules += ", " + BrowserSettings.HostRules.Replace("\"", "");
            a.Add("--host-resolver-rules=\"" + rules + "\"");
            if (BrowserSettings.Offscreen) { a.Add("--disable-features=CalculateNativeWinOcclusion"); a.Add("--disable-backgrounding-occluded-windows"); a.Add("--autoplay-policy=no-user-gesture-required"); }
            if (BrowserSettings.IgnoreCerts) a.Add("--ignore-certificate-errors");   // 1.4 tests only: the local stand-ins' self-signed certificates
            return string.Join(" ", a);
        }

        // Chromium keeps some choices only as profile preferences: third-party cookies (cookie_controls_mode) and Do Not Track.
        // They're written before the engine starts (the runtime reads them at start). A broken file is left alone.
        public static void WritePrefs(BrowserSettings s)
        {
            // 1.6: this window's browser profile (Default or a profile of its own) and its private profile
            WritePrefs(s, Path.Combine(BrowserSettings.EngineProfileFolder(BrowserSettings.WebViewProfile), "Preferences"));
            WritePrefs(s, Path.Combine(BrowserSettings.EngineProfileFolder(BrowserSettings.PrivateWebViewProfile), "Preferences"));   // 1.4: private windows' profile
            WriteSpellWords(s);   // 1.6: words you added to the dictionary
            WriteLocalState(BrowserSettings.IsDefaultProfile ? s : DefaultProfileSettings());   // secure DNS is engine-wide: the Default profile's choice
        }
        static void WritePrefs(BrowserSettings s, string f)
        {
            try
            {
                var js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
                Dictionary<string, object> d;
                if (File.Exists(f)) { d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f)); if (d == null) return; }
                else { Directory.CreateDirectory(Path.GetDirectoryName(f)); d = new Dictionary<string, object>(); }
                object p; var profile = d.TryGetValue("profile", out p) ? p as Dictionary<string, object> : null;
                if (profile == null) d["profile"] = profile = new Dictionary<string, object>();
                profile["cookie_controls_mode"] = s.BlockThirdPartyCookies ? 1 : 0;
                profile["block_third_party_cookies"] = s.BlockThirdPartyCookies;
                d["enable_do_not_track"] = s.DoNotTrack;
                var tmp = f + ".hallow.tmp";
                File.WriteAllText(tmp, js.Serialize(d), new UTF8Encoding(false));
                if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
            }
            catch { }
        }

        public static async Task<CoreWebView2Environment> Create(BrowserSettings s)
        {
            if (Env != null) return Env;
            Directory.CreateDirectory(BrowserSettings.ProfileDir);
            WritePrefs(s);
            SecureDnsApplied = s.SecureDns;
            var o = new CoreWebView2EnvironmentOptions(ExtraArgs(s));
            o.AreBrowserExtensionsEnabled = true;          // Hallow Shield
            o.EnableTrackingPrevention = true;
            try { o.ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay; } catch { }
            var lang = SpellLanguage(); if (lang != null) o.Language = lang;   // 1.6: the spell check language (engine-wide, the same for every profile)
            Env = await CoreWebView2Environment.CreateAsync(null, BrowserSettings.ProfileDir, o);
            RuntimeVersion = Env.BrowserVersionString;
            var made = Env; made.BrowserProcessExited += (a, b) => { if (Env == made && (Program.Boosted || Program.Windows.Count == 0)) { Env = null; Starting = null; } };   // 1.6: startup boost starts it again
            Mark("environment");
            return Env;
        }

        // 1.4: secure DNS (DNS over HTTPS). Chromium keeps it in "Local State" (dns_over_https.mode / .templates), which the runtime
        // reads when it starts - tested with a local DoH stand-in: every lookup then goes to the provider (and nowhere else). A feature
        // flag in AdditionalBrowserArguments alone did nothing. "off" leaves the runtime's own default (the key is removed).
        public static string SecureDnsApplied;
        public static string DohTemplate(string provider)
        {
            if (provider == null || provider == "off") return null;
            if (BrowserSettings.DohTemplateTest != null) return BrowserSettings.DohTemplateTest;
            switch (provider)
            {
                case "cloudflare": return "https://chrome.cloudflare-dns.com/dns-query";
                case "quad9": return "https://dns.quad9.net/dns-query";
                case "google": return "https://dns.google/dns-query{?dns}";
            }
            return null;
        }
        public static string DnsName(string p) { return p == "cloudflare" ? "Cloudflare" : p == "quad9" ? "Quad9" : p == "google" ? "Google" : "Off"; }
        public static void WriteLocalState(BrowserSettings s)
        {
            var f = Path.Combine(BrowserSettings.ProfileDir, "EBWebView", "Local State");
            try
            {
                var json = LocalStateJson(File.Exists(f) ? File.ReadAllText(f) : null, DohTemplate(s.SecureDns));
                if (json == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                var tmp = f + ".hallow.tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
            }
            catch { }
        }
        // the new Local State text, or null when nothing changes (a broken file is left alone)
        public static string LocalStateJson(string existing, string template)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
            Dictionary<string, object> d;
            if (existing != null) { try { d = js.Deserialize<Dictionary<string, object>>(existing); } catch { return null; } if (d == null) return null; }
            else d = new Dictionary<string, object>();
            if (template == null) { if (!d.ContainsKey("dns_over_https")) return null; d.Remove("dns_over_https"); }
            else
            {
                object cur; var c = d.TryGetValue("dns_over_https", out cur) ? cur as Dictionary<string, object> : null; object m, t;
                if (c != null && c.TryGetValue("mode", out m) && (m as string) == "secure" && c.TryGetValue("templates", out t) && (t as string) == template) return null;
                d["dns_over_https"] = new Dictionary<string, object> { { "mode", "secure" }, { "templates", template } };
            }
            return js.Serialize(d);
        }

        // the profile-wide choices (dark pages, tracking prevention, no built-in password saving: Hallow Keys does that)
        public static void ApplyProfile(CoreWebView2Profile p, BrowserSettings s)
        {
            try { p.PreferredColorScheme = PageScheme(s.PageColors, Look.IsLight); } catch { }
            try { p.PreferredTrackingPreventionLevel = s.StrictTracking ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced; } catch { }
            try { p.IsPasswordAutosaveEnabled = false; p.IsGeneralAutofillEnabled = false; } catch { }
            try { p.DefaultDownloadFolderPath = Downloads(); } catch { }
        }
        // pages: "follow" = the browser's own look (1.1, the default), dark, light or "auto" (Windows' app mode, the engine decides)
        public static CoreWebView2PreferredColorScheme PageScheme(string pageColors, bool browserLight)
        {
            if (pageColors == "light") return CoreWebView2PreferredColorScheme.Light;
            if (pageColors == "auto") return CoreWebView2PreferredColorScheme.Auto;
            if (pageColors == "dark") return CoreWebView2PreferredColorScheme.Dark;
            return browserLight ? CoreWebView2PreferredColorScheme.Light : CoreWebView2PreferredColorScheme.Dark;
        }
        public static string Downloads()
        {
            if (BrowserSettings.TestDir != null) return Path.Combine(BrowserSettings.TestDir, "Downloads");   // tests never write to your Downloads
            try { var k = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", "{374DE290-123F-4565-9164-39C4925E467B}", null) as string; if (!string.IsNullOrEmpty(k)) return Environment.ExpandEnvironmentVariables(k); } catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        // ---- the new tab page: a local file (no network at all), shown through a virtual host
        public static string NewTabDir { get { return Path.Combine(BrowserSettings.LocalDir, "newtab"); } }
        public static void WriteNewTab(BrowserSettings s, string accent)
        {
            try
            {
                Directory.CreateDirectory(NewTabDir);
                var html = NewTabHtml(s.Search, accent, Look.IsLight);
                var f = Path.Combine(NewTabDir, "index.html");
                if (!File.Exists(f) || File.ReadAllText(f) != html) File.WriteAllText(f, html, new UTF8Encoding(false));
                var ph = PrivateTabHtml(s.Search);   // 1.4: a private window's new tab page
                var pf = Path.Combine(NewTabDir, "private.html");
                if (!File.Exists(pf) || File.ReadAllText(pf) != ph) File.WriteAllText(pf, ph, new UTF8Encoding(false));
            }
            catch { }
        }
        // 1.3: speed dial (top sites + pinned), an optional clock and the Hallow strip. The page itself is static; what goes in the
        // tiles and the strip comes from data.json next to it (written by NewTabData, local only) - read with one same-origin fetch,
        // so the page is drawn at once and the tiles follow a few milliseconds later. Nothing is loaded from the network.
        public static string NewTabHtml(string engine, string accent, bool light = false)
        {
            var name = Omni.EngineName(engine); var url = Omni.SearchUrl(engine, "");
            // 1.1: the light look too (family palette; the accent darkened for text)
            string bg = light ? "#F6F4FA" : "#14101B", glow = light ? "#ECE5F8" : "#221A30", fg = light ? "#1E1A26" : "#ECE8F4", muted = light ? "#6B6478" : "#9790A8",
                line = light ? "#DAD3E6" : "#3A3248", field = light ? "#FFFFFF" : "#110E17", dim = light ? "#6B6478" : "#5E5670", head = light ? Look.TextAccent(accent) : accent,
                tile = light ? "#FFFFFF" : "#1C1626", tileHover = light ? "#F0EAFA" : "#261E33";
            return "<!DOCTYPE html><html lang=en><head><meta charset=utf-8><meta name=color-scheme content=" + (light ? "light" : "dark") + "><title>New tab</title><style>" +
                "html,body{margin:0;height:100%;background:" + bg + ";color:" + fg + ";font:15px 'Segoe UI',system-ui,sans-serif}" +
                "body{display:flex;flex-direction:column;align-items:center;justify-content:center;background:radial-gradient(ellipse at 50% 35%," + glow + " 0," + bg + " 60%)}" +
                "h1{font-weight:300;letter-spacing:.32em;font-size:34px;margin:0 0 6px}h1 b{font-weight:700;color:" + head + "}" +
                "p{color:" + muted + ";margin:0 0 28px;font:12px 'Cascadia Mono',Consolas,monospace;letter-spacing:.08em}" +
                "#clk{font:200 54px 'Segoe UI',system-ui,sans-serif;letter-spacing:.04em;margin:0 0 4px;color:" + fg + "}#clk:empty{display:none}" +
                "form{width:min(620px,86vw)}input{width:100%;box-sizing:border-box;padding:14px 18px;border-radius:10px;border:1px solid " + line + ";background:" + field + ";color:" + fg + ";font-size:16px;outline:none}" +
                "input:focus{border-color:" + head + ";box-shadow:0 0 0 3px " + accent + "33}" +
                "#dial{display:grid;grid-template-columns:repeat(auto-fill,104px);gap:12px;justify-content:center;width:min(720px,90vw);margin-top:26px}#dial:empty{display:none}" +
                ".tl{position:relative;display:flex;flex-direction:column;align-items:center;gap:8px;padding:14px 6px 10px;border-radius:12px;background:" + tile + ";border:1px solid " + line + ";color:" + fg + ";text-decoration:none;font-size:12px}" +
                ".tl:hover,.tl:focus{background:" + tileHover + ";border-color:" + head + ";outline:none}" +
                ".tl i{width:40px;height:40px;border-radius:10px;display:flex;align-items:center;justify-content:center;font:700 18px 'Segoe UI',sans-serif;font-style:normal;color:#fff}" +
                ".tl span{max-width:92px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}" +
                ".tl button{position:absolute;top:3px;width:20px;height:20px;border:0;border-radius:6px;background:transparent;color:" + muted + ";font-size:12px;line-height:20px;padding:0;cursor:pointer;opacity:0}" +
                ".tl:hover button,.tl:focus-within button{opacity:1}.tl button:hover{background:" + line + ";color:" + fg + "}.tl .x{right:3px}.tl .pn{left:3px}.tl.pinned .pn{opacity:1;color:" + head + "}" +
                "#fam{position:fixed;bottom:40px;display:flex;gap:10px;flex-wrap:wrap;justify-content:center}#fam:empty{display:none}" +
                "#fam div{padding:5px 12px;border-radius:14px;border:1px solid " + line + ";background:" + tile + ";font:11.5px 'Cascadia Mono',Consolas,monospace;color:" + muted + "}#fam b{color:" + head + ";font-weight:600}" +
                "#t{position:fixed;bottom:18px;color:" + dim + ";font:11px 'Cascadia Mono',Consolas,monospace}" +
                "#gc{width:min(720px,90vw);margin-top:22px;display:grid;grid-template-columns:1fr 1fr;gap:12px}#gc:empty{display:none}#gc section{background:" + tile + ";border:1px solid " + line + ";border-radius:12px;padding:10px 14px}" +
                "#gc h2{font:600 11px 'Cascadia Mono',Consolas,monospace;letter-spacing:.12em;color:" + head + ";margin:2px 0 8px}#gc a{display:block;color:" + fg + ";text-decoration:none;font-size:12.5px;padding:4px 0;border-top:1px solid " + line + ";overflow:hidden;text-overflow:ellipsis;white-space:nowrap}" +
                "#gc a:first-of-type{border-top:0}#gc a:hover{color:" + head + "}#gc a small{color:" + muted + ";margin-right:6px}#gc .note{color:" + muted + ";font-size:11px;margin-top:6px}" +
                NewTabFamily.Css(fg, muted, line, tile, tileHover, head) +   // 1.6: the Hallow family rows
                "</style></head><body><div id=clk></div><h1><b>HALLOW</b> BROWSER</h1><p>// SHIELD ON - NOTHING LEAVES THIS PC</p>" +
                "<form id=f><input id=q name=q autocomplete=off placeholder=\"Search " + name + " or type an address\" autofocus></form><div id=dial></div><div id=hf></div><div id=gc></div><div id=fam></div><div id=t></div>" +
                "<script>var u=" + new JavaScriptSerializer().Serialize(url) + ";document.getElementById('f').onsubmit=function(e){e.preventDefault();var v=document.getElementById('q').value.trim();if(v)location.href=u+encodeURIComponent(v);};" +
                "var D={};function c(){var d=new Date(),s=('0'+d.getHours()).slice(-2)+':'+('0'+d.getMinutes()).slice(-2);document.getElementById('t').textContent=D.clock?'':s;document.getElementById('clk').textContent=D.clock?s:'';}c();setInterval(c,15000);" +
                "function post(m){try{chrome.webview.postMessage(m);}catch(e){}}" +
                "function el(t,k,x){var e=document.createElement(t);if(k)e.className=k;if(x!=null)e.textContent=x;return e;}" +
                "function draw(){var g=document.getElementById('dial');g.textContent='';(D.tiles||[]).forEach(function(s){var a=el('a','tl'+(s.pin?' pinned':''));a.href=s.u;a.title=s.t+'\\n'+s.u;" +
                "var i=el('i',null,s.l);i.style.background=s.c;a.appendChild(i);a.appendChild(el('span',null,s.t));" +
                "var p=el('button','pn','\u2605');p.title=s.pin?'Unpin':'Pin';p.onclick=function(e){e.preventDefault();e.stopPropagation();post((s.pin?'hallow:nt-unpin\\t':'hallow:nt-pin\\t')+s.u+'\\t'+s.t);};a.appendChild(p);" +
                "var x=el('button','x','\u00d7');x.title=s.pin?'Unpin':'Remove from this page';x.onclick=function(e){e.preventDefault();e.stopPropagation();post((s.pin?'hallow:nt-unpin\\t':'hallow:nt-hide\\t')+s.u);};a.appendChild(x);g.appendChild(a);});" +
                "var f=document.getElementById('fam');f.textContent='';(D.family||[]).forEach(function(s){var d=el('div');var b=el('b',null,s.k+' ');d.appendChild(b);d.appendChild(document.createTextNode(s.v));f.appendChild(d);});" +
                "var gc=document.getElementById('gc');gc.textContent='';var G=D.games;if(G&&((G.news||[]).length||(G.free||[]).length)){" +
                "var sn=el('section');sn.appendChild(el('h2',null,'GAME NEWS'));(G.news||[]).forEach(function(n){var a=el('a');a.href=n.u;a.title=n.t;a.appendChild(el('small',null,n.g));a.appendChild(document.createTextNode(n.t));sn.appendChild(a);});if(!(G.news||[]).length)sn.appendChild(el('div','note','No news for the games in your Hallow Hub library.'));gc.appendChild(sn);" +
                "var sf=el('section');sf.appendChild(el('h2',null,'FREE GAMES'));(G.free||[]).forEach(function(n){var a=el('a');a.href=n.u;a.title=n.t;a.appendChild(el('small',null,n.w));a.appendChild(document.createTextNode(n.t));sf.appendChild(a);});if(!(G.free||[]).length)sf.appendChild(el('div','note','No free games right now.'));" +
                "sf.appendChild(el('div','note',G.note||''));gc.appendChild(sf);}c();}" +
                "window.hallowRefresh=function(){return fetch('data.json?'+Date.now(),{cache:'no-store'}).then(function(r){return r.json();}).then(function(j){D=j||{};draw();return (D.tiles||[]).length;}).catch(function(){return -1;});};" +
                NewTabFamily.Script +
                "hallowRefresh();document.addEventListener('visibilitychange',function(){if(!document.hidden){hallowRefresh();if(window.hallowFamily)hallowFamily();}});</script></body></html>";
        }
    }
}
