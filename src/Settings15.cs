// Hallow Browser 1.5 - Settings: the address bar (keywords, engine suggestions), downloads, tabs and media, dark pages, saving logins.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        public UIElement[] AddressSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Muted("Type a sum (200*1.2, 15% of 80 as 80*15%) or a conversion (5 km in miles, 72 f to c, 3 gb in mib) and the answer shows under the address bar - click it to copy. \"% name\" finds an open tab.", 12, 8));
            items.Add(Ui.Muted("Search keywords: type the keyword, a space and what to look for (\"yt cats\").", 12, 4));
            var list = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            Action fill = null;
            fill = () =>
            {
                list.Children.Clear();
                foreach (var k in S.Keywords.ToList())
                {
                    var kk = k; var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var key = Ui.T(k.Key, 12.5, true, "Lilac"); key.FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"); key.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(key);
                    var tx = new StackPanel(); tx.Children.Add(new TextBlock { Text = k.Name ?? Omni.HostOf(k.Url) }); tx.Children.Add(Ui.Muted(k.Url, 11)); Grid.SetColumn(tx, 1); g.Children.Add(tx);
                    var del = Ui.Tool("trash-can", "Remove this keyword", () => { S.Keywords.Remove(kk); S.Save(); fill(); }, 12); Grid.SetColumn(del, 2); g.Children.Add(del);
                    list.Children.Add(g);
                }
                if (S.Keywords.Count == 0) list.Children.Add(Ui.Muted("No keywords.", 12));
            };
            fill(); items.Add(list);
            var kb = new TextBox { Style = Ui.S("Field"), Width = 70, Height = 28, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Keyword, e.g. r" };
            var nb = new TextBox { Style = Ui.S("Field"), Width = 120, Height = 28, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Name, e.g. Reddit" };
            var ub = new TextBox { Style = Ui.S("Field"), Width = 300, Height = 28, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Search address with %s where the words go, e.g. https://www.reddit.com/search/?q=%s" };
            var msg = Ui.Muted("Keyword, name and the search address with %s for the words.", 11.5);
            Action add = () =>
            {
                var k = Keywords.Clean(kb.Text); var u = (ub.Text ?? "").Trim();
                if (k == null) { msg.Text = "A keyword is one short word (letters, digits, . _ - ! @)."; return; }
                if (!Keywords.ValidUrl(u)) { msg.Text = "The address must start with https:// (or http://) and have %s where the words go."; return; }
                S.Keywords.RemoveAll(x => x.Key == k); S.Keywords.Add(new SearchKeyword { Key = k, Url = u, Name = string.IsNullOrWhiteSpace(nb.Text) ? Omni.HostOf(u.Replace("%s", "x")) : nb.Text.Trim() });
                S.Save(); kb.Text = nb.Text = ub.Text = ""; msg.Text = "Added \"" + k + "\"."; fill();
            };
            ub.KeyDown += (a, b) => { if (b.Key == Key.Enter) { add(); b.Handled = true; } };
            items.Add(Ui.Row(kb, nb, ub, Ui.Btn("Add", add, true, "plus")));
            items.Add(msg);
            items.Add(Ui.Row(Ui.Btn("Put the usual ones back", () => { foreach (var d in Keywords.Defaults()) if (!S.Keywords.Any(x => x.Key == d.Key)) S.Keywords.Add(d); S.Save(); fill(); }, false, "rotate-left")));
            items.Add(new Border { Height = 10 });
            items.Add(Ui.Switch("Search suggestions from " + Omni.EngineName(S.Search), "Off by default. When on, what you type in the address bar is sent to " + Omni.EngineName(S.Search) + " as you type, to get its suggestions. Addresses, sums and tab searches are never sent, and private windows never send anything.", S.SearchSuggest, on => { S.SearchSuggest = on; S.Save(); }));
            return items.ToArray();
        }

        public UIElement[] DownloadSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Switch("Check downloads with Microsoft Defender", "When a download finishes, Defender scans that one file (its own command-line scanner, at the lowest priority) and the downloads list shows Clean or Threat found. Defender's real-time protection keeps working either way.", S.DownloadScan, on => { S.DownloadScan = on; S.Save(); }));
            items.Add(Ui.Switch("Open magnet links in Hallow Torrents", "On by default when Hallow Torrents is installed: a magnet link on a page goes straight to Hallow Torrents, whatever app Windows has for magnet links (like qBittorrent). Off: Windows decides, as before.", !S.MagnetsToWindows, on => { S.MagnetsToWindows = !on; S.Save(); }));
            items.Add(Ui.Muted("While a game runs, downloads together use at most", 12, 6));
            items.Add(Ui.Chips(BrowserSettings.DlLimitChoices.Select(k => new KeyValuePair<int, string>(k, DownloadLimit.Name(k))).ToList(), S.DownloadGameKBps, v => { S.DownloadGameKBps = v; S.Save(); }));
            items.Add(Ui.Muted("Honestly: WebView2 has no speed setting for downloads (and its network emulation only slows some of them), so the browser pauses and resumes each download many times a second to hold the speed. It works for downloads in Hallow Browser only, not other apps or the game's own updates.", 11.5, 8));
            items.Add(Ui.Row(Ui.Btn("All downloads", () => NewTab(Omni.DownloadsUrl, true), false, "download"), Ui.Btn("Open the Downloads folder", () => { try { System.Diagnostics.Process.Start(Engine.Downloads()); } catch { } }, false, "folder-open")));
            return items.ToArray();
        }

        public UIElement[] TabsMediaSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Switch("Tab previews", "Hover a tab to see a small picture of it (taken while it's on screen, kept in memory only).", S.TabPreviews, on => { S.TabPreviews = on; S.Save(); foreach (var t in Tabs) t.Header.ToolTip = on ? null : t.Title; }));
            items.Add(Ui.Switch("Mouse gestures", "Hold the right mouse button on a page and drag: left = back, right = forward, down then right = close the tab. Off by default.", S.Gestures, on => { S.Gestures = on; S.Save(); GesturesChanged(); }));
            items.Add(Ui.Switch("Pop out a playing video when I switch tabs", "The video keeps playing in a small window on top; it comes back when you return to its tab. Off by default.", S.AutoPip, on => { S.AutoPip = on; S.Save(); }));
            items.Add(Ui.Muted("The music button in the toolbar lists every tab playing audio or video, in every window, with play / pause and next. Windows' media controls (and Hallow Mixer and Overlay) see them too - WebView2 tells Windows itself, under the engine's name \"msedgewebview2\".", 11.5, 0));
            return items.ToArray();
        }

        public UIElement[] KeysSaveSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Switch("Offer to save logins in Hallow Keys", "After you sign in, \"Save to Hallow Keys?\" - Keys keeps it, encrypted (the same request Keys' own browser extension makes). The browser never stores passwords. Not in private windows.", S.SaveLogins, on => { S.SaveLogins = on; S.Save(); }));
            var nl = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            Action fill = null;
            fill = () =>
            {
                nl.Children.Clear();
                foreach (var h in S.NeverSave.ToList())
                {
                    var hh = h; var chip = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 3, 4, 3), Margin = new Thickness(0, 0, 6, 6) };
                    chip.SetResourceReference(Border.BorderBrushProperty, "Line"); chip.SetResourceReference(Border.BackgroundProperty, "PanelHi");
                    var x = Ui.Tool("xmark", "Offer again for this site", () => { S.NeverSave.Remove(hh); S.Save(); fill(); }, 9); x.Width = 20; x.Height = 20;
                    chip.Child = Ui.Row(new TextBlock { Text = h, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, x); nl.Children.Add(chip);
                }
                if (S.NeverSave.Count == 0) nl.Children.Add(Ui.Muted("Never offered for: no sites.", 11.5));
            };
            fill(); items.Add(nl);
            return items.ToArray();
        }
    }
}
