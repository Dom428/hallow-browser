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
    public static class Engine
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
            if (BrowserSettings.HostRules != null) a.Add("--host-resolver-rules=\"" + BrowserSettings.HostRules.Replace("\"", "") + "\"");
            if (BrowserSettings.Offscreen) { a.Add("--disable-features=CalculateNativeWinOcclusion"); a.Add("--disable-backgrounding-occluded-windows"); a.Add("--autoplay-policy=no-user-gesture-required"); }
            return string.Join(" ", a);
        }

        // Chromium keeps some choices only as profile preferences: third-party cookies (cookie_controls_mode) and Do Not Track.
        // They're written before the engine starts (the runtime reads them at start). A broken file is left alone.
        public static void WritePrefs(BrowserSettings s)
        {
            var f = Path.Combine(BrowserSettings.ProfileDir, "EBWebView", "Default", "Preferences");
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
            var o = new CoreWebView2EnvironmentOptions(ExtraArgs(s));
            o.AreBrowserExtensionsEnabled = true;          // Hallow Shield
            o.EnableTrackingPrevention = true;
            try { o.ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay; } catch { }
            Env = await CoreWebView2Environment.CreateAsync(null, BrowserSettings.ProfileDir, o);
            RuntimeVersion = Env.BrowserVersionString;
            Mark("environment");
            return Env;
        }

        // the profile-wide choices (dark pages, tracking prevention, no built-in password saving: Hallow Keys does that)
        public static void ApplyProfile(CoreWebView2Profile p, BrowserSettings s)
        {
            try { p.PreferredColorScheme = s.PageColors == "light" ? CoreWebView2PreferredColorScheme.Light : s.PageColors == "auto" ? CoreWebView2PreferredColorScheme.Auto : CoreWebView2PreferredColorScheme.Dark; } catch { }
            try { p.PreferredTrackingPreventionLevel = s.StrictTracking ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced; } catch { }
            try { p.IsPasswordAutosaveEnabled = false; p.IsGeneralAutofillEnabled = false; } catch { }
            try { p.DefaultDownloadFolderPath = Downloads(); } catch { }
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
                var html = NewTabHtml(s.Search, accent);
                var f = Path.Combine(NewTabDir, "index.html");
                if (!File.Exists(f) || File.ReadAllText(f) != html) File.WriteAllText(f, html, new UTF8Encoding(false));
            }
            catch { }
        }
        public static string NewTabHtml(string engine, string accent)
        {
            var name = Omni.EngineName(engine); var url = Omni.SearchUrl(engine, "");
            return "<!DOCTYPE html><html lang=en><head><meta charset=utf-8><meta name=color-scheme content=dark><title>New tab</title><style>" +
                "html,body{margin:0;height:100%;background:#14101B;color:#ECE8F4;font:15px 'Segoe UI',system-ui,sans-serif}" +
                "body{display:flex;flex-direction:column;align-items:center;justify-content:center;background:radial-gradient(ellipse at 50% 35%,#221A30 0,#14101B 60%)}" +
                "h1{font-weight:300;letter-spacing:.32em;font-size:34px;margin:0 0 6px}h1 b{font-weight:700;color:" + accent + "}" +
                "p{color:#9790A8;margin:0 0 28px;font:12px 'Cascadia Mono',Consolas,monospace;letter-spacing:.08em}" +
                "form{width:min(620px,86vw)}input{width:100%;box-sizing:border-box;padding:14px 18px;border-radius:10px;border:1px solid #3A3248;background:#110E17;color:#ECE8F4;font-size:16px;outline:none}" +
                "input:focus{border-color:" + accent + ";box-shadow:0 0 0 3px " + accent + "33}#t{position:fixed;bottom:18px;color:#5E5670;font:11px 'Cascadia Mono',Consolas,monospace}" +
                "</style></head><body><h1><b>HALLOW</b> BROWSER</h1><p>// SHIELD ON - NOTHING LEAVES THIS PC</p>" +
                "<form id=f><input id=q name=q autocomplete=off placeholder=\"Search " + name + " or type an address\" autofocus></form><div id=t></div>" +
                "<script>var u=" + new JavaScriptSerializer().Serialize(url) + ";document.getElementById('f').onsubmit=function(e){e.preventDefault();var v=document.getElementById('q').value.trim();if(v)location.href=u+encodeURIComponent(v);};" +
                "function c(){var d=new Date();document.getElementById('t').textContent=d.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});}c();setInterval(c,15000);</script></body></html>";
        }
    }
}
