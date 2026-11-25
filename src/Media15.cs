// Hallow Browser 1.5 - global media controls: a toolbar button listing every tab (in every window) that plays audio or video, with
// play / pause and next; optionally the playing video pops out when you switch away from its tab (off by default).
// Windows' own media controls (the volume flyout, Hallow Mixer and Overlay through Windows' media sessions) already see what plays:
// measured, WebView2 registers each playing page with Windows' media session by itself - but under the engine's name
// ("msedgewebview2.exe"), not Hallow Browser's; the browser can't rename it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HallowBrowser
{
    public class MediaState { public int N; public bool Playing, Video, Next, Prev; public string Title; }

    public static class Media
    {
        // in the page (top frame): remembers the page's own media-session handlers (so "next" does what the site's next does), and
        // answers state / toggle / next / prev for the browser
        public const string Script = "(function(){try{if(window.top!==window||window.__hallowMedia)return;var H={};" +
            "try{var ms=navigator.mediaSession;if(ms&&ms.setActionHandler){var o=ms.setActionHandler.bind(ms);ms.setActionHandler=function(a,h){try{H[a]=h;}catch(e){}return o(a,h);};}}catch(e){}" +
            "var els=function(){return [].slice.call(document.querySelectorAll('video,audio'));};var J=JSON.stringify;" +
            "var M={state:function(){var e=els();var p=e.filter(function(x){return !x.paused&&!x.ended;});var t=null;try{var m=navigator.mediaSession&&navigator.mediaSession.metadata;if(m&&m.title)t=m.title+(m.artist?' - '+m.artist:'');}catch(x){}" +
            "return J({n:e.length,playing:p.length>0,video:p.some(function(x){return x.tagName==='VIDEO';}),next:!!H.nexttrack,prev:!!H.previoustrack,title:t});}," +
            "toggle:function(){var e=els();var p=e.filter(function(x){return !x.paused&&!x.ended;});if(p.length){if(H.pause){try{H.pause({action:'pause'});return 'paused';}catch(x){}}p.forEach(function(x){x.pause();});return 'paused';}" +
            "if(H.play){try{H.play({action:'play'});return 'playing';}catch(x){}}var t=e.filter(function(x){return x.currentTime>0;})[0]||e[0];if(t){var r=t.play();if(r&&r.catch)r.catch(function(){});return 'playing';}return 'none';}," +
            "next:function(){if(H.nexttrack){try{H.nexttrack({action:'nexttrack'});return 'ok';}catch(x){}}return 'none';}," +
            "prev:function(){if(H.previoustrack){try{H.previoustrack({action:'previoustrack'});return 'ok';}catch(x){}}return 'none';}};" +
            "Object.defineProperty(window,'__hallowMedia',{value:M,configurable:false,writable:false});" +
            "var said=0;document.addEventListener('play',function(){var n=Date.now();if(n-said<2000)return;said=n;try{window.chrome.webview.postMessage('hallow:media-play');}catch(e){}},true);" +   // also silent / muted media (Windows' "playing audio" flag misses those)
            "}catch(e){}})();";
        public static MediaState Parse(string json)
        {
            try
            {
                var s = new JavaScriptSerializer().Deserialize<string>(json); if (s == null) return null;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(s); if (d == null) return null;
                object o; Func<string, bool> b = k => d.TryGetValue(k, out o) && o is bool && (bool)o;
                return new MediaState { N = d.TryGetValue("n", out o) && o != null ? Convert.ToInt32(o) : 0, Playing = b("playing"), Video = b("video"), Next = b("next"), Prev = b("prev"), Title = d.TryGetValue("title", out o) ? o as string : null };
            }
            catch { return null; }
        }
        public static async Task<MediaState> State(Tab t)
        {
            var c = t == null ? null : t.Core; if (c == null) return null;
            try { return Parse(await Panels.Eval(c, "window.__hallowMedia?__hallowMedia.state():null")); } catch { return null; }   // DevTools: no user gesture handed to the page
        }
        public static async Task<string> Do(Tab t, string what)
        {
            var c = t == null ? null : t.Core; if (c == null) return "none";
            if (what != "toggle" && what != "next" && what != "prev") return "none";
            try { var r = await c.ExecuteScriptAsync("window.__hallowMedia?__hallowMedia." + what + "():'none'"); return new JavaScriptSerializer().Deserialize<string>(r) ?? "none"; } catch { return "none"; }
        }
    }

    public partial class BrowserWindow
    {
        Button mediaBtn;
        public bool MediaButtonShown { get { return mediaBtn != null && mediaBtn.Visibility == Visibility.Visible; } }
        Button BuildMediaButton()
        {
            mediaBtn = Ui.Tool("music", "Media playing in your tabs", () => { var _ = MediaFlyout(mediaBtn); }, 14);
            mediaBtn.Visibility = Visibility.Collapsed;
            return mediaBtn;
        }
        // tabs (all windows) that play or played media since their page loaded
        public static List<Tuple<BrowserWindow, Tab>> MediaTabs() { return Program.Windows.Where(w => !w.Gone).SelectMany(w => w.Tabs.Where(t => (t.Audible || t.MediaSeen) && t.Core != null).Select(t => Tuple.Create(w, t))).ToList(); }
        public static void UpdateMediaButtons()
        {
            bool any = MediaTabs().Count > 0;
            foreach (var w in Program.Windows) if (w.mediaBtn != null) w.mediaBtn.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }
        public Task MediaFlyoutAtButton() { return MediaFlyout(mediaBtn.Visibility == Visibility.Visible ? (UIElement)mediaBtn : menuBtn); }
        public async Task MediaFlyout(UIElement at)
        {
            var list = MediaTabs();
            var sp = new StackPanel(); var head = Ui.Label("Playing in your tabs"); head.Margin = new Thickness(12, 8, 12, 6); sp.Children.Add(head);
            var rows = new StackPanel(); sp.Children.Add(rows);
            if (list.Count == 0) { var m = Ui.Muted("Nothing is playing.", 12); m.Margin = new Thickness(12, 0, 12, 8); rows.Children.Add(m); }
            var pop = Panels.ShowFlyout(at, sp, 360);
            foreach (var x in list)
            {
                var w = x.Item1; var t = x.Item2; var st = await Media.State(t);
                if (st == null || st.N == 0) { if (!t.Audible) { t.MediaSeen = false; continue; } st = st ?? new MediaState { Playing = true }; }
                var g = new Grid { Margin = new Thickness(10, 3, 6, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(Ui.Icon(st.Video ? "video" : "music", 13, st.Playing ? "AccentBrush" : "Muted"));
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(txt, 1);
                txt.Children.Add(new TextBlock { Text = st.Title ?? t.Title, TextTrimming = TextTrimming.CharacterEllipsis });
                txt.Children.Add(Ui.Muted((Omni.HostOf(t.Url) ?? "") + (w != this ? "  -  another window" : "") + (t.Muted ? "  -  muted" : ""), 10.5));
                txt.Cursor = System.Windows.Input.Cursors.Hand; txt.MouseLeftButtonUp += (a, b) => { pop.IsOpen = false; w.Activate(t); w.Bring(); };
                g.Children.Add(txt);
                var acts = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(acts, 2);
                var playing = st.Playing;
                var pb = Ui.Tool(playing ? "pause" : "play", playing ? "Pause" : "Play", null, 12);
                pb.Click += async (a, b) => { var r = await Media.Do(t, "toggle"); var now = r == "playing"; pb.Content = Ui.Icon(now ? "pause" : "play", 12); pb.ToolTip = now ? "Pause" : "Play"; };
                acts.Children.Add(pb);
                if (st.Next) acts.Children.Add(Ui.Tool("forward-step", "Next", () => { var _ = Media.Do(t, "next"); }, 12));
                acts.Children.Add(Ui.Tool(t.Muted ? "volume-xmark" : "volume-high", t.Muted ? "Unmute" : "Mute", () => w.ToggleMute(t), 12));
                g.Children.Add(acts); rows.Children.Add(g);
            }
            var note = Ui.Muted("Windows' media controls show these too (as \"msedgewebview2\", the page engine).", 10.5); note.Margin = new Thickness(12, 6, 12, 6); note.TextWrapping = TextWrapping.Wrap; sp.Children.Add(note);
            UpdateMediaButtons();
        }

        // ---- the playing video pops out when you switch away (optional)
        public static int AutoPips;
        async void AutoPipCheck(Tab old, Tab now)
        {
            if (now != null && now.PoppedOut && now.AutoPipped) { now.AutoPipped = false; Pip.Return(this, now); }
            if (!S.AutoPip || Game != null || old == null || old == now || old.View == null || old.PoppedOut || !Tabs.Contains(old) || IsShown(old)) return;   // 1.19: never in game mode
            if (!(old.Audible || old.MediaSeen)) { LastAutoPip = "no media"; return; }
            var why = await AutoPipWhy119(old); LastAutoPip = why;   // 1.19: a playing video of a real size - not a muted autoplay (an ad, a background loop)
            if (why != "ok" || Game != null || old == Active || old.PoppedOut || !Tabs.Contains(old)) return;
            old.AutoPipped = true; AutoPips++;
            Pip.PopOut(this, old);
        }
    }
}
