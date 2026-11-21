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

        // the tab's look in the strip
        public Border Header; public TextBlock TitleBlock; public ContentControl IconHost; public Button CloseBtn; public Path Sound;

        public bool Internal { get { return Url == Omni.SettingsUrl; } }
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
    }
}
