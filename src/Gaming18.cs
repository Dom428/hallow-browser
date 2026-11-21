// Hallow Browser 1.8 - gaming extras.
//  - YouTube sponsor / intro skip (OFF by default): the community SponsorBlock list (sponsor.ajay.app). For each video the browser
//    itself asks GET /api/skipSegments/<first 4 hex characters of SHA-256(video id)> - the privacy-preserving hash-prefix lookup, so
//    SponsorBlock never learns which video you watch (it answers for every video whose hash starts the same way and the browser picks
//    yours out). No cookies, no address, no account. The segments go into the page with a small browser-owned script (not Shield):
//    skip at once, or show a Skip button. SponsorBlock's data is CC BY-NC-SA 4.0 - credited in Settings, used as-is, non-commercially,
//    kept in memory only.
//  - Twitch: the channel's chat (twitch.tv/popout/<channel>/chat) in the sidebar beside the stream (optional, also in full screen and
//    theatre mode); theatre mode hides the browser around the page (Alt+Shift+T, Esc). Auto-claiming channel points is NOT built:
//    Twitch's Terms of Service (Prohibited Conduct) forbid accessing its services "by means of any robot, spider, scraper, crawler, or
//    other automated means for any purpose" - a script clicking the bonus for you is exactly that.
//  - Clip it: asks Hallow Clips (its family pipe, "save") to save its replay buffer while the stream is on screen. Clips records the
//    screen; the browser records nothing itself.
//  - Game launch handoff: Play on the new tab's "Your games" turns game mode on at once; it goes off when the game closes (Steam's
//    own running flag, Hallow Hub's playing state or the game's process), or after 2 minutes if the game never showed up.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        // ---- 1.8 settings (per profile)
        [DataMember] public bool SponsorSkip;                    // OFF by default: contacts SponsorBlock
        [DataMember] public List<string> SponsorCats;            // which kinds of segments
        [DataMember] public string SponsorMode = "auto";         // auto | button
        [DataMember] public bool TwitchChat;                     // OFF by default: the channel's chat in the sidebar

        // 1.8 tests: local stand-ins only (never the real SponsorBlock / YouTube / Twitch / Steam). The test folder is read here directly:
        // this file's static fields may be set up before TestDir's (partial class, another file)
        public static readonly string SponsorBlockTest = Env("HALLOW_BROWSER_TESTDIR") != null ? Env("HALLOW_BROWSER_SPONSORBLOCK_URL") : null;
        public static readonly string YouTubeTest = Env("HALLOW_BROWSER_TESTDIR") != null ? Env("HALLOW_BROWSER_YOUTUBE_BASE") : null;
        public static readonly string TwitchTest = Env("HALLOW_BROWSER_TESTDIR") != null ? Env("HALLOW_BROWSER_TWITCH_BASE") : null;
        public static readonly string FakeSteamFile = Env("HALLOW_BROWSER_TESTDIR") != null ? Env("HALLOW_BROWSER_FAKE_STEAM") : null;

        void Defaults18() { SponsorSkip = false; SponsorCats = new List<string>(SponsorBlock.DefaultCats); SponsorMode = "auto"; TwitchChat = false; }
        void Fix18()
        {
            if (SponsorCats == null) SponsorCats = new List<string>(SponsorBlock.DefaultCats);
            SponsorCats = SponsorCats.Where(c => SponsorBlock.Cats.Any(x => x[0] == c)).Distinct().ToList();
            if (SponsorMode != "auto" && SponsorMode != "button") SponsorMode = "auto";
        }
    }

    public class SbSegment { public string Cat; public double Start, End; }

    public static class SponsorBlock
    {
        public const string Api = "https://sponsor.ajay.app";
        public const string Credit = "Segment data from SponsorBlock (sponsor.ajay.app) by its contributors, licensed CC BY-NC-SA 4.0 - used as it comes, non-commercially, kept in memory only.";
        // the API's category names and what we call them
        public static readonly string[][] Cats = {
            new[] { "sponsor", "Sponsors", "sponsor" }, new[] { "intro", "Intros and intermissions", "intro" }, new[] { "outro", "Outros and end cards", "outro" },
            new[] { "selfpromo", "Self-promotion", "self-promotion" }, new[] { "interaction", "Interaction reminders (like / subscribe)", "reminder" } };
        public static readonly string[] DefaultCats = { "sponsor", "selfpromo" };
        public static string Label(string cat) { var c = Cats.FirstOrDefault(x => x[0] == cat); return c == null ? cat : c[2]; }
        static readonly Regex idRe = new Regex("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        // the video a YouTube address is about: youtube.com/watch?v=<id>, m. / music. / www., /shorts/<id>, /live/<id>, youtu.be/<id>
        public static string VideoId(string url)
        {
            Uri u; if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            string path = u.AbsolutePath, query = u.Query;
            if (BrowserSettings.YouTubeTest != null && url.StartsWith(BrowserSettings.YouTubeTest + "/", StringComparison.OrdinalIgnoreCase))
                path = url.Substring(BrowserSettings.YouTubeTest.Length).Split('?', '#')[0];
            else
            {
                if (u.Scheme != "https" && u.Scheme != "http") return null;
                var h = u.Host.ToLowerInvariant();
                if (h == "youtu.be") { var s = path.Trim('/'); return idRe.IsMatch(s) ? s : null; }
                if (h != "youtube.com" && h != "www.youtube.com" && h != "m.youtube.com" && h != "music.youtube.com") return null;
            }
            if (path == "/watch")
            {
                foreach (var kv in query.TrimStart('?').Split('&')) { var i = kv.IndexOf('='); if (i > 0 && kv.Substring(0, i) == "v") { var v = Uri.UnescapeDataString(kv.Substring(i + 1)); return idRe.IsMatch(v) ? v : null; } }
                return null;
            }
            foreach (var pre in new[] { "/shorts/", "/live/" }) if (path.StartsWith(pre)) { var v = path.Substring(pre.Length).Trim('/'); return idRe.IsMatch(v) ? v : null; }
            return null;
        }
        // the only thing about the video that leaves the PC: the first 4 hex characters of SHA-256(video id)
        public static string Prefix(string videoId)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(videoId))).Replace("-", "").Substring(0, 4).ToLowerInvariant();
        }
        public static string Url(string prefix, IEnumerable<string> cats)
        {
            var list = cats.Where(c => Cats.Any(x => x[0] == c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
            return (BrowserSettings.SponsorBlockTest ?? Api) + "/api/skipSegments/" + prefix + "?categories=" + Uri.EscapeDataString(js.Serialize(list)) + "&actionType=skip";
        }
        // the answer for every video under that prefix -> the segments of OUR video (skip ones, of the categories asked for, in order)
        public static List<SbSegment> Parse(string json, string videoId, ICollection<string> cats)
        {
            var res = new List<SbSegment>();
            if (string.IsNullOrEmpty(json)) return res;
            try
            {
                var arr = js.DeserializeObject(json) as object[]; if (arr == null) return res;
                foreach (var o in arr)
                {
                    var d = o as Dictionary<string, object>; object v, segs;
                    if (d == null || !d.TryGetValue("videoID", out v) || (v as string) != videoId || !d.TryGetValue("segments", out segs)) continue;
                    foreach (var so in (segs as object[]) ?? new object[0])
                    {
                        var s = so as Dictionary<string, object>; if (s == null) continue;
                        object cat, at, seg; s.TryGetValue("category", out cat); s.TryGetValue("actionType", out at); s.TryGetValue("segment", out seg);
                        if (!(cat is string) || !cats.Contains((string)cat) || (at != null && (at as string) != "skip")) continue;
                        var se = seg as object[]; if (se == null || se.Length < 2) continue;
                        double a, b; try { a = Convert.ToDouble(se[0], CultureInfo.InvariantCulture); b = Convert.ToDouble(se[1], CultureInfo.InvariantCulture); } catch { continue; }
                        if (double.IsNaN(a) || double.IsNaN(b) || a < 0 || b - a < 0.5 || b > 24 * 3600) continue;
                        res.Add(new SbSegment { Cat = (string)cat, Start = a, End = b });
                    }
                }
            }
            catch { }
            return res.OrderBy(x => x.Start).ToList();
        }

        // ---- fetching (a background thread; answers kept 30 minutes in memory, per prefix and categories)
        public static readonly List<string> Requests = new List<string>();   // what was asked (tests and Settings' "last looked up")
        static readonly Dictionary<string, Tuple<DateTime, string>> cache = new Dictionary<string, Tuple<DateTime, string>>();
        public static readonly TimeSpan Keep = TimeSpan.FromMinutes(30);
        public static string LastError;
        // null = SponsorBlock couldn't be reached (nothing is skipped); empty = no segments for this video
        public static List<SbSegment> Segments(string videoId, ICollection<string> cats)
        {
            if (videoId == null || cats == null || cats.Count == 0) return new List<SbSegment>();
            var url = Url(Prefix(videoId), cats);
            string body = null; Tuple<DateTime, string> hit;
            lock (cache) if (cache.TryGetValue(url, out hit) && DateTime.Now - hit.Item1 < Keep) body = hit.Item2 ?? "";
            if (body == null)
            {
                try { body = Get(url); LastError = null; }
                catch (Exception e) { LastError = e.Message; return null; }
                lock (cache) { cache[url] = Tuple.Create(DateTime.Now, body); if (cache.Count > 300) cache.Remove(cache.OrderBy(x => x.Value.Item1).First().Key); }
            }
            return Parse(body, videoId, cats);
        }
        public static void ClearCache() { lock (cache) cache.Clear(); }
        static string Get(string url)
        {
            lock (Requests) { Requests.Add(url); if (Requests.Count > 200) Requests.RemoveAt(0); }
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "HallowBrowser/" + Program.Version; r.Timeout = 8000; r.ReadWriteTimeout = 8000; r.Accept = "application/json";
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; r.CookieContainer = null; r.AllowAutoRedirect = false;
            try
            {
                using (var resp = (HttpWebResponse)r.GetResponse()) using (var s = resp.GetResponseStream()) using (var rd = new StreamReader(s, Encoding.UTF8))
                {
                    var sb = new StringBuilder(); var buf = new char[16384]; int n;
                    while ((n = rd.Read(buf, 0, buf.Length)) > 0) { sb.Append(buf, 0, n); if (sb.Length > 8 * 1024 * 1024) throw new IOException("too big"); }
                    return sb.ToString();
                }
            }
            catch (WebException e)
            {
                var hr = e.Response as HttpWebResponse;
                if (hr != null && hr.StatusCode == HttpStatusCode.NotFound) { hr.Dispose(); return ""; }   // 404 = no segments under this prefix
                if (hr != null) hr.Dispose();
                throw;
            }
        }

        // ---- the page's part: segments in, a skip (or a Skip button) when the video gets there. Shadow DOM, so the page's
        // styles can't touch it; inside the full-screen element when there is one. Updated by running it again.
        public const string BtnBg = "#14101B", BtnText = "#FFFFFF", NoteText = "#ECE8F4";
        public static string Script(string videoId, IList<SbSegment> segs, string mode, string accent)
        {
            var data = js.Serialize(new Dictionary<string, object> { { "vid", videoId }, { "mode", mode },
                { "segs", (segs ?? new List<SbSegment>()).Select(x => new Dictionary<string, object> { { "s", x.Start }, { "e", x.End }, { "c", x.Cat }, { "l", Label(x.Cat) } }).ToList() } });
            return "(function(D){var S=window.__hallowSB;" +
                "if(!S){S=window.__hallowSB={skips:0,log:[]};document.addEventListener('timeupdate',function(e){try{window.__hallowSB.tick(e.target)}catch(x){}},true);document.addEventListener('seeked',function(e){try{window.__hallowSB.tick(e.target)}catch(x){}},true);document.addEventListener('fullscreenchange',function(){try{window.__hallowSB.hide(true)}catch(x){}},true);}" +
                "S.vid=D.vid;S.mode=D.mode;S.segs=D.segs||[];S.done={};S.cur=null;" +
                "S.ui=function(){if(S.host&&S.host.isConnected&&S.host.parentNode===(document.fullscreenElement||document.documentElement))return;if(!S.host){S.host=document.createElement('hallow-skip');S.root=S.host.attachShadow({mode:'closed'});" +
                "S.root.innerHTML='<style>:host{all:initial;position:fixed;z-index:2147483647;display:none}div{display:flex;gap:8px;align-items:center;background:" + BtnBg + ";color:" + NoteText + ";border:1px solid " + accent + ";border-radius:18px;padding:5px 6px 5px 14px;font:13px \"Segoe UI\",sans-serif;box-shadow:0 2px 10px rgba(0,0,0,.5)}button{all:unset;cursor:pointer;background:" + BtnBg + ";color:" + BtnText + ";border:1px solid " + accent + ";border-radius:14px;padding:4px 12px;font:600 13px \"Segoe UI\",sans-serif}button:focus-visible{outline:2px solid " + accent + "}small{font-size:11px;opacity:.85}</style><div><span id=t></span><button id=b></button></div>';}" +
                "(document.fullscreenElement||document.documentElement).appendChild(S.host);};" +
                "S.place=function(v){var r=v.getBoundingClientRect();S.host.style.right=Math.max(8,innerWidth-r.right+16)+'px';S.host.style.bottom=Math.max(8,innerHeight-r.bottom+64)+'px';};" +
                "S.hide=function(force){if(S.host&&(force||!S.noteUntil||Date.now()>S.noteUntil)){S.host.style.display='none';S.shown=null;}};" +
                "S.show=function(v,g,skipped){S.ui();S.place(v);var t=S.root.getElementById('t'),b=S.root.getElementById('b');" +
                "if(skipped){t.innerHTML='';t.appendChild(document.createTextNode('Skipped '+g.l+' '));var sm=document.createElement('small');sm.textContent='SponsorBlock';t.appendChild(sm);b.textContent='Undo';b.onclick=function(){v.currentTime=g.s;S.hide(true);S.noteUntil=0;};S.noteUntil=Date.now()+4000;S.shown='note';}" +
                "else{if(S.shown==='note'&&Date.now()<S.noteUntil)return;t.textContent='';b.textContent='Skip '+g.l+' \\u25B8';b.onclick=function(){S.done[g.s+'-'+g.e]=1;v.currentTime=g.e;S.skips++;S.log.push('button:'+g.c);S.hide(true);};S.shown='button';}" +
                "S.host.style.display='block';};" +
                "S.tick=function(v){if(!(v instanceof HTMLMediaElement)||v.tagName!=='VIDEO')return;if(!S.vid||location.href.indexOf(S.vid)<0||!S.segs.length){S.hide();return;}" +
                "var t=v.currentTime,g=null;for(var i=0;i<S.segs.length;i++){var x=S.segs[i];if(t>=x.s&&t<x.e-0.3){g=x;break;}}if(!g){S.hide();return;}" +
                "var k=g.s+'-'+g.e;if(S.mode==='auto'&&!S.done[k]){S.done[k]=1;v.currentTime=g.e;S.skips++;S.log.push('auto:'+g.c);S.show(v,g,true);return;}S.show(v,g,false);};" +
                "S.hide(true);return S.segs.length;})(" + data + ")";
        }
        public const string ClearScript = "(function(){var S=window.__hallowSB;if(S){S.segs=[];S.vid=null;if(S.host)S.host.style.display='none';}return 0;})()";
    }

    public static class Twitch
    {
        public const string Base = "https://www.twitch.tv";
        static readonly Regex nameRe = new Regex("^[A-Za-z0-9_]{3,25}$", RegexOptions.Compiled);
        // first path parts that aren't channels
        static readonly HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "directory", "videos", "settings", "subscriptions", "inventory", "wallet", "drops", "popout", "search", "downloads", "jobs", "turbo", "prime",
            "store", "friends", "messages", "login", "signup", "logout", "payments", "broadcast", "moderator", "team", "collections", "embed", "following",
            "dashboard", "products", "bits", "redeem", "user", "privacy", "legal", "p", "u", "clip", "clips", "event", "events", "creatorcamp", "partner", "chat",
            "help", "security", "annual-recap", "recap", "activate", "safety", "guidelines", "_deck", "browse", "esports", "sitemap", "subs" };
        // the channel a Twitch address is watching (twitch.tv/<channel>, www. / m.), else null
        public static string Channel(string url)
        {
            Uri u; if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            string path;
            if (BrowserSettings.TwitchTest != null && url.StartsWith(BrowserSettings.TwitchTest + "/", StringComparison.OrdinalIgnoreCase)) path = url.Substring(BrowserSettings.TwitchTest.Length).Split('?', '#')[0];
            else
            {
                var h = u.Host.ToLowerInvariant();
                if (u.Scheme != "https" || (h != "twitch.tv" && h != "www.twitch.tv" && h != "m.twitch.tv")) return null;
                path = u.AbsolutePath;
            }
            var parts = path.Trim('/').Split('/');
            if (parts.Length != 1 || !nameRe.IsMatch(parts[0]) || reserved.Contains(parts[0])) return null;
            return parts[0].ToLowerInvariant();
        }
        public static string ChatUrl(string channel) { return (BrowserSettings.TwitchTest ?? Base) + "/popout/" + Uri.EscapeDataString(channel) + "/chat"; }
        // Twitch's own theatre mode (its player button), on or off - only when you turned theatre mode on yourself
        public static string TheatreScript(bool on)
        {
            return "(function(){var b=document.querySelector('button[data-a-target=\"player-theatre-mode-button\"]');if(!b)return 'none';var inT=/^exit/i.test(b.getAttribute('aria-label')||'');if(inT!==" + (on ? "true" : "false") + ")b.click();return inT?'was-on':'was-off';})()";
        }
    }

    // ---- game launch handoff (plain logic, unit-tested)
    public class Handoff
    {
        public string Id, Name; public DateTime Started; public bool Seen, Active;
        public static TimeSpan Grace = TimeSpan.FromMinutes(2);   // tests make it shorter
        public void Start(string id, string name, DateTime now) { Id = id; Name = name; Started = now; Seen = false; Active = true; }
        // detected: the game the usual detection sees (process or Hub's playing state); steam: Steam's running flag for this game
        // (null = can't tell). Returns the game for game mode.
        public string Step(string detected, bool? steam, DateTime now)
        {
            if (!Active) return detected;
            if (detected != null || steam == true) { Seen = true; return detected ?? Name; }
            if (Seen || now - Started > Grace) { Active = false; return null; }   // it ran and closed / it never showed up
            return Name;
        }
    }
    public static class SteamState
    {
        // Steam's own flag (HKCU\Software\Valve\Steam\Apps\<id>\Running = 1 while the game runs); tests: a file with the running id
        public static bool? Running(string appId)
        {
            if (appId == null || !appId.All(char.IsDigit)) return null;
            if (BrowserSettings.FakeSteamFile != null) { try { return File.Exists(BrowserSettings.FakeSteamFile) && File.ReadAllText(BrowserSettings.FakeSteamFile).Trim() == appId; } catch { return null; } }
            if (BrowserSettings.TestDir != null) return null;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\Apps\" + appId))
                {
                    if (k == null) return null;
                    var v = k.GetValue("Running"); return v is int ? (int)v == 1 : (bool?)null;
                }
            }
            catch { return null; }
        }
    }

    public partial class Tab { public string SbVid, SbNote; public int SbRun, SbCount = -1; }

    public partial class SidePanel { public bool Transient; }

    // ---- the Twitch chat panel (a sidebar panel of its own, not in your list of sites)
    public partial class Sidebar
    {
        public SidePanel Chat; public string ChatChannel; SidePanel chatPrev; string chatDismissed; bool chatHiding;
        public const string ChatId = "hallow-twitch-chat";
        public void ShowChat(string channel)
        {
            if (w.Private || w.AppMode || channel == null) return;
            if (channel == chatDismissed) return;   // you closed it for this channel
            var url = Twitch.ChatUrl(channel);
            if (Chat == null) { Chat = new SidePanel { Site = new SidebarSite { Id = ChatId, Name = "Twitch chat", Url = url, Width = 340 }, Transient = true }; Panels.Add(Chat); }
            if (Chat.Site.Url != url) { Chat.Site.Url = url; Chat.Site.Name = "Twitch chat - " + channel; if (Chat.Core != null) try { Chat.Core.Navigate(url); } catch { } }
            Chat.Site.Name = "Twitch chat - " + channel; ChatChannel = channel;
            if (Open != Chat) { if (Open != null && !Open.Transient) chatPrev = Open; Show(Chat); }
        }
        public void HideChat()
        {
            if (Chat == null || Open != Chat) { chatDismissed = null; return; }
            chatHiding = true; try { Hide(); } finally { chatHiding = false; }
            ChatChannel = null; chatDismissed = null;
            var prev = chatPrev; chatPrev = null;
            if (prev != null && Panels.Contains(prev) && w.S.SidebarOn) Show(prev);
        }
        void ChatClosedByYou(SidePanel p) { if (p == Chat && !chatHiding) { chatDismissed = ChatChannel; chatPrev = null; } }
        public bool HostShown { get { return host.Visibility == Visibility.Visible; } }
        public bool RailShown { get { return rail.Visibility == Visibility.Visible; } }
        // theatre mode: the rail goes, an open panel (the chat) stays
        public void Theatre(bool on)
        {
            if (on) { rail.Visibility = Visibility.Collapsed; railCol.Width = new GridLength(0); return; }
            ApplyOn();
        }
    }

    public partial class BrowserWindow
    {
        // ================================================================ YouTube: SponsorBlock
        void Hook18(CoreWebView2 c)
        {
            c.SourceChanged += (a, b) => { Tab x; if (b.IsNewDocument || !owner.TryGetValue(c, out x)) return; var _ = SponsorCheck(x, c, false); };
            c.NavigationCompleted += (a, b) => { Tab x; if (!owner.TryGetValue(c, out x)) return; if (b.IsSuccess) { var _ = SponsorCheck(x, c, true); } };
        }
        public async Task<int> SponsorCheck(Tab t, CoreWebView2 c, bool newDocument)
        {
            if (t == null || c == null || t.Core != c) return -1;
            string src = null; try { src = c.Source; } catch { }   // the address now (this runs before the tab's own Url is updated)
            var vid = S.SponsorSkip ? SponsorBlock.VideoId(src ?? t.Url) : null;
            if (vid == null)
            {
                if (t.SbVid != null) { t.SbVid = null; t.SbCount = -1; t.SbNote = null; t.SbRun++; try { await c.ExecuteScriptAsync(SponsorBlock.ClearScript); } catch { } }
                return -1;
            }
            if (vid == t.SbVid && !newDocument) return t.SbCount;
            t.SbVid = vid; t.SbCount = -1; t.SbNote = null; var run = ++t.SbRun;   // nothing stale while it's looked up
            var cats = S.SponsorCats.ToList(); var mode = S.SponsorMode;
            var segs = await Task.Run(() => SponsorBlock.Segments(vid, cats));
            if (t.SbRun != run || t.Core != c) return -1;
            t.SbCount = segs == null ? -1 : segs.Count;
            t.SbNote = segs == null ? "SponsorBlock couldn't be reached (" + SponsorBlock.LastError + ") - nothing is skipped" : segs.Count + " segment" + (segs.Count == 1 ? "" : "s") + " from SponsorBlock";
            try { await c.ExecuteScriptAsync(SponsorBlock.Script(vid, segs, mode, Program.AccentHex)); } catch { }
            return t.SbCount;
        }
        public void SponsorSettingsChanged()
        {
            SponsorBlock.ClearCache();
            foreach (var w in Program.Windows) foreach (var t in w.Tabs) if (t.Core != null) { var _ = w.SponsorCheck(t, t.Core, true); }
        }

        // ================================================================ stream pages: Clip it, theatre mode, Twitch chat
        Button clipBtn, theatreBtn;
        void BuildStreamButtons(Panel inBar)
        {
            clipBtn = Ui.Tool("clapperboard", "Clip it - Hallow Clips saves the last 30 s of your screen", () => { var _ = ClipIt(Active); }, 13); clipBtn.Height = 24; clipBtn.Width = 28; clipBtn.Visibility = Visibility.Collapsed;
            theatreBtn = Ui.Tool("tv", "Theatre mode - hide the browser around the stream (Alt+Shift+T)", ToggleTheatre, 13); theatreBtn.Height = 24; theatreBtn.Width = 28; theatreBtn.Visibility = Visibility.Collapsed;
            inBar.Children.Insert(0, theatreBtn); inBar.Children.Insert(0, clipBtn);
        }
        public static bool IsStream(string url) { return Twitch.Channel(url) != null || SponsorBlock.VideoId(url) != null; }
        static DateTime clipsLook = DateTime.MinValue; static bool clipsThere;
        static bool ClipsInstalled { get { if (DateTime.Now - clipsLook > TimeSpan.FromSeconds(30)) { clipsLook = DateTime.Now; clipsThere = NewTabFamily.Installed("Clips"); } return clipsThere; } }
        public bool ClipButtonVisible { get { return clipBtn != null && clipBtn.Visibility == Visibility.Visible; } }
        public bool TheatreButtonVisible { get { return theatreBtn != null && theatreBtn.Visibility == Visibility.Visible; } }
        public void UpdateBarPublic18(Tab t) { UpdateBar18(t); }
        void UpdateBar18(Tab t)
        {
            if (clipBtn == null || t == null) return;
            bool stream = !AppMode && !t.Internal && IsStream(t.Url);
            clipBtn.Visibility = stream && ClipsInstalled ? Visibility.Visible : Visibility.Collapsed;
            theatreBtn.Visibility = stream || Theatre ? Visibility.Visible : Visibility.Collapsed;
            if (Theatre) theatreBtn.SetResourceReference(Control.BackgroundProperty, "AccentSoft"); else theatreBtn.ClearValue(Control.BackgroundProperty);
            TwitchChatFor(t);
        }
        // the chat follows the tab you're looking at
        void TwitchChatFor(Tab t)
        {
            if (Side == null || Private || AppMode || snap) return;
            var ch = S.TwitchChat && t == Active ? Twitch.Channel(t.Url) : null;
            if (ch != null) Side.ShowChat(ch);
            else if (Side.Chat != null && Side.Open == Side.Chat) Side.HideChat();
        }

        // ---- theatre mode: the tab strip, toolbar, bookmarks bar, vertical tabs and the sidebar's rail go; the page (and the chat) stay
        public bool Theatre; Tab theatreTab;
        public void ToggleTheatre() { SetTheatre(!Theatre); }
        public void SetTheatre(bool on)
        {
            if (on == Theatre || (on && (fullscreen || AppMode))) return;
            Theatre = on;
            ChromeRows(!on);
            Side.Theatre(on); ApplyTabLayout();
            var t = Active;
            if (on && t != null && t.Core != null && Twitch.Channel(t.Url) != null) { theatreTab = t; try { var _ = t.Core.ExecuteScriptAsync(Twitch.TheatreScript(true)); } catch { } }
            if (!on && theatreTab != null) { var x = theatreTab; theatreTab = null; if (x.Core != null && Twitch.Channel(x.Url) != null) try { var _ = x.Core.ExecuteScriptAsync(Twitch.TheatreScript(false)); } catch { } }
            if (on) Toast("Theatre mode - Esc or Alt+Shift+T to leave.", 3);
            if (Active != null) UpdateBar18(Active);
        }
        void ChromeRows(bool show)
        {
            var wc = WindowChrome.GetWindowChrome(this);
            if (!show)
            {
                root.RowDefinitions[0].Height = new GridLength(0); root.RowDefinitions[1].Height = new GridLength(0); root.RowDefinitions[2].Height = new GridLength(0);
                root.Children.OfType<FrameworkElement>().Where(x => Grid.GetRow(x) < 3).ToList().ForEach(x => x.Visibility = Visibility.Collapsed);
                wc.CaptionHeight = 0;
            }
            else
            {
                root.RowDefinitions[0].Height = new GridLength(38); root.RowDefinitions[1].Height = GridLength.Auto; root.RowDefinitions[2].Height = GridLength.Auto;
                root.Children.OfType<FrameworkElement>().Where(x => Grid.GetRow(x) < 3 && x != findBar).ToList().ForEach(x => x.Visibility = Visibility.Visible);
                wc.CaptionHeight = 38;
            }
        }
        void AfterFullscreen18(bool on)
        {
            if (!on && Theatre) { ChromeRows(false); Side.Theatre(true); ApplyTabLayout(); }
        }
        public bool ChromeShown { get { return root.RowDefinitions[1].Height.Value != 0 || root.RowDefinitions[1].Height.IsAuto; } }
        public double CaptionNow { get { return WindowChrome.GetWindowChrome(this).CaptionHeight; } }
        public bool IsFullscreen { get { return fullscreen; } }

        // ---- Clip it: Hallow Clips saves its replay buffer (the screen) - only while the stream is on screen
        public static string LastClipNote;
        public async Task<string> ClipIt(Tab t)
        {
            Func<string, string> say = m => { LastClipNote = m; Toast(m, m.StartsWith("Clip saved") ? 6 : 9); return m; };
            if (t == null) return say("Open the stream first.");
            if ((t != Active && !t.PoppedOut) || WindowState == WindowState.Minimized || !IsVisible) return say("Bring the stream on screen first - Hallow Clips records what's on your screen.");
            if (!ClipsInstalled && !NewTabFamily.Installed("Clips")) return say("Hallow Clips isn't installed - it's the Hallow app that records clips of your screen.");
            var r = await Task.Run(() =>
            {
                var st = Family.Status("Clips");
                if (st == null) return "Hallow Clips isn't running. Start it, keep the stream on screen and try again - Clips records your screen, the browser doesn't record anything itself.";
                object rec, paused, secs; st.TryGetValue("recording", out rec); st.TryGetValue("pausedFor", out paused); st.TryGetValue("seconds", out secs);
                if (paused is string && ((string)paused).Length > 0) return "Hallow Clips is paused while " + paused + " records - nothing to save from Clips right now.";
                if (!(rec is bool) || !(bool)rec) return "Hallow Clips isn't recording right now. It records your screen and, by default, only while a game runs - to clip streams, turn off \"Only while a game is running\" in Clips' settings and keep the stream on screen.";
                var reply = Family.Send("Clips", "save", 30000);
                if (reply == null) return "Hallow Clips didn't answer in time.";
                if (reply.StartsWith("error")) return "Hallow Clips couldn't save: " + reply.Substring(5).Trim();
                string file = null;
                try { var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reply.Substring(2).Trim()); object f; if (d != null && d.TryGetValue("file", out f)) file = f as string; } catch { }
                int n = 0; try { n = Convert.ToInt32(secs ?? 0, CultureInfo.InvariantCulture); } catch { }
                return "Clip saved" + (n > 0 ? " (the last " + n + " s)" : "") + (file != null ? ": " + file : ".");
            });
            if (r.StartsWith("Clip saved")) RefreshFamily(1500);   // the new tab's "Recent clips"
            return say(r);
        }

        // ================================================================ game launch handoff
        public static readonly Handoff Launched = new Handoff();
        string GameNow()
        {
            if (!S.GameLight) return null;
            var d = Games.Game();
            if (!Launched.Active) return d;
            return Launched.Step(d, SteamState.Running(Launched.Id), DateTime.Now);
        }
        public static void GameStarted(FamGame g)
        {
            if (g == null) return;
            Launched.Start(g.Id, g.Name, DateTime.Now);
            foreach (var w in Program.Windows.ToList()) w.Tick(true);   // at once, not at the next 5 s look
        }
        public void PlayFromNewTab(string id, Action<string> done)
        {
            var g = NewTabFamily.LastGames.FirstOrDefault(x => x.Id == id);
            Task.Run(() => NewTabFamily.Play(id)).ContinueWith(r => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (g != null && (r.Result == "hub" || r.Result == "link")) GameStarted(g);
                if (g != null) Toast(r.Result == "hub" ? "Hallow Hub is starting " + g.Name + " - game mode is on." : r.Result == "link" ? "Starting " + g.Name + " through Steam - game mode is on." : "That game isn't in the list.", 4);
                if (done != null) done(r.Result);
            })));
        }

        // ================================================================ Settings > Gaming > YouTube and Twitch
        public UIElement[] StreamSettings()
        {
            var items = new List<UIElement>();
            var catRows = new StackPanel { Margin = new Thickness(18, 0, 0, 4) };
            var mode = Ui.Chips(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("auto", "Skip automatically"), new KeyValuePair<string, string>("button", "Show a Skip button") }, S.SponsorMode, v => { S.SponsorMode = v; S.Save(); SponsorSettingsChanged(); });
            mode.Margin = new Thickness(18, 0, 0, 6);
            Action shown = () => { catRows.Visibility = mode.Visibility = S.SponsorSkip ? Visibility.Visible : Visibility.Collapsed; };
            items.Add(Ui.Switch("Skip sponsors and intros on YouTube - this contacts SponsorBlock", "SponsorBlock (sponsor.ajay.app) is a free community list of sponsor and intro segments. For each video only a 4-character hash prefix is sent - the first 4 characters of the SHA-256 of the video's id, shared by many videos - plus the kinds you picked. Never the video's address, your account or cookies. Off by default.", S.SponsorSkip, on => { S.SponsorSkip = on; S.Save(); shown(); SponsorSettingsChanged(); }));
            items.Add(mode);
            foreach (var c in SponsorBlock.Cats)
            {
                var id = c[0];
                catRows.Children.Add(Ui.Switch(c[1], null, S.SponsorCats.Contains(id), on => { S.SponsorCats.Remove(id); if (on) S.SponsorCats.Add(id); S.Save(); SponsorSettingsChanged(); }));
            }
            items.Add(catRows);
            items.Add(Ui.Muted(SponsorBlock.Credit, 11.5, 12));
            items.Add(Ui.Switch("Twitch chat in the sidebar when I watch a channel", "Opens the channel's chat (twitch.tv/popout/<channel>/chat) beside the stream, also in full screen and theatre mode, signed in like your tabs. Close it and it stays closed for that channel.", S.TwitchChat, on => { S.TwitchChat = on; S.Save(); foreach (var w in Program.Windows) if (w.Active != null) w.UpdateBar18(w.Active); }));
            items.Add(Ui.Muted("Theatre mode: the TV button in the address bar on a stream (or Alt+Shift+T) hides the browser around the page; on Twitch it also turns on Twitch's own theatre mode. Esc leaves.", 11.5, 6));
            items.Add(Ui.Muted("Clip it: the clapperboard button on a stream asks Hallow Clips to save its replay buffer (the last 30 s unless you changed Clips' clip length). Clips records your screen, so the stream has to be on screen - and by default Clips only records while a game runs.", 11.5, 6));
            items.Add(Ui.Muted("Playing a game from \"Your games\" on the new tab page turns game mode on at once; it goes off when the game closes (Steam, Hallow Hub or the game itself say so), or after 2 minutes if the game never started.", 11.5, 6));
            items.Add(Ui.Muted("Not offered: auto-claiming Twitch channel points. Twitch's terms forbid using automated means to access its services, so the browser won't click the bonus for you.", 11.5, 0));
            shown();
            return items.ToArray();
        }
    }
}
