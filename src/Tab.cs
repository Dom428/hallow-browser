// Hallow Browser - one tab: its WebView2 (or an internal page such as Settings) and what the window shows about it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public class Tab
    {
        static int next;
        public readonly int Id = ++next;
        public WebView2 View;                 // null while discarded, for internal pages and in --snap
        public FrameworkElement Page;         // internal pages (Settings)
        public string Url = Omni.NewTabUrl, Title = "New tab";
        public DateTime LastActive = DateTime.Now;
        public bool Audible, Muted, Suspended, Discarded, PoppedOut, Loading, SleptByGame, Crashed, Ready;
        public ImageSource Favicon;
        public List<KeysItem> KeysItems;      // Hallow Keys logins for this page (titles/usernames only)
        public bool KeysLocked;
        public Stopwatch OpenTimer;           // new-tab speed (perf)
        public string PendingUrl;             // where to go once the WebView is ready
        public string HttpsBlocked;           // 1.4: the http address the "not secure - continue?" page is about (while it shows)
        public bool MutedBySite;              // 1.4: muted by its site setting (unmuted again when it leaves that site)

        // the tab's look in the strip
        public Border Header; public TextBlock TitleBlock; public ContentControl IconHost; public Button CloseBtn; public Path Sound;
        // 1.2: the group stripe, the speaker button (mute), the split-view mark and the box holding them
        public Border GroupLine; public Button SoundBtn; public Path SplitMark; public StackPanel RightBox;
        public TabGroup Group;                // its tab group, or null
        public bool ReaderOn, Readable;       // reader mode shown / the page looks like an article
        public string ProbedUrl;              // the address the article check ran for
        public FrameworkElement ReaderView; public ReaderDoc ReaderDoc;
        // 1.5: a small picture for hover previews (taken while the tab is on screen), the "this tab crashed" page, media and dark pages
        public ImageSource Preview; public DateTime PreviewAt;
        public FrameworkElement CrashView;
        public bool MediaSeen, AutoPipped;    // it played media since its last page load / it was popped out because you switched away
        public bool? DarkApplied;             // Chromium's auto dark mode on this tab's WebView (null = never set)

        public bool Internal { get { return Omni.IsInternal(Url); } }
        public Microsoft.Web.WebView2.Core.CoreWebView2 Core { get { try { return View == null ? null : View.CoreWebView2; } catch { return null; } } }
        public TabState State(bool active)
        {
            return new TabState { Id = Id, Active = active, Audible = Audible, Suspended = Suspended, Discarded = Discarded, PoppedOut = PoppedOut, Loading = Loading, LastActive = LastActive };
        }
    }

    public class DownloadItem
    {
        public Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation Op;
        public string File, Name; public long Total, Got; public string State = "running";   // running | done | cancelled | failed | paused
        public DateTime Started = DateTime.Now;
        // 1.5: paused by you / by the gaming speed limit, Defender's answer, the record in the downloads list
        public bool UserPaused, LimitPaused; public string Scan; public string ScanNote; public bool Private; public DownloadRecord Record;
        public long LimitBase; public DateTime LimitFrom;
        public Microsoft.Web.WebView2.Core.CoreWebView2 Source;   // the tab's view it came from (a tab with a running download never sleeps: a sleeping page stalls it)
    }
}
