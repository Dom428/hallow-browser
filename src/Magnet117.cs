// Hallow Browser 1.17 - magnet links go to Hallow Torrents. A page's magnet: link normally goes to whatever Windows has for
// magnet links (often qBittorrent, which registers itself for the whole PC). When Hallow Torrents is installed, Hallow Browser
// hands the link straight to it instead (its exe with the link - a running copy takes it over its pipe), without Windows'
// "open this app?" prompt. Settings > Downloads can leave magnet links to Windows. Only magnet: - every other app link is
// left to Windows as before. Tests never start anything: they record the link (and use a stand-in exe only when given one).
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        public static int MagnetsHanded; public static string LastMagnet;

        public static string TorrentsExe
        {
            get
            {
                var stand = BrowserSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_BROWSER_TORRENTS_EXE") : null;
                if (BrowserSettings.TestDir != null) return string.IsNullOrEmpty(stand) ? null : stand;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Hallow Torrents", "HallowTorrents.exe");
            }
        }
        public static bool TorrentsInstalled { get { try { var e = TorrentsExe; return BrowserSettings.TestDir != null || (e != null && File.Exists(e)); } catch { return false; } } }

        public static bool IsMagnet(string u) { return u != null && u.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) && u.Length < 8192 && u.IndexOf('"') < 0; }

        void HookMagnet(CoreWebView2 c)
        {
            c.LaunchingExternalUriScheme += (a, e) =>
            {
                try
                {
                    if (!IsMagnet(e.Uri) || S.MagnetsToWindows || !TorrentsInstalled) return;
                    e.Cancel = true;
                    HandMagnet(e.Uri);
                }
                catch { }
            };
        }

        public void HandMagnet(string uri)
        {
            MagnetsHanded++; LastMagnet = uri;
            var exe = TorrentsExe;
            if (exe != null && File.Exists(exe)) Process.Start(new ProcessStartInfo(exe, "\"" + uri + "\"") { UseShellExecute = false });
            Toast("Sent the magnet link to Hallow Torrents.", 3);
        }
    }
}
