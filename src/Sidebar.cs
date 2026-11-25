// Hallow Browser 1.1 - the gamer sidebar: pinned web panels (Discord, Twitch, YouTube, Spotify, WhatsApp, or any site you add) beside
// the page. Each panel is its own WebView2 on the browser's one environment and profile, so it stays signed in like a tab and gets
// Hallow Shield too. Panels load only when you first open them; a hidden panel sleeps (TrySuspendAsync) after N minutes, at once in
// game mode - never while it plays sound (Spotify keeps playing). A count in the page title ("(3) Discord") shows as a badge.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    [DataContract]
    public class SidebarSite
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Url;
        [DataMember] public bool Mobile;        // the phone version of the site (an Android Chrome user agent)
        [DataMember] public int Width = 420;    // the panel's width (drag its edge)
    }

    // the sidebar's rules as plain functions (unit-tested)
    public static class SidebarRules
    {
        public const int MinWidth = 280, MaxWidth = 900;
        public static List<SidebarSite> Defaults()
        {
            return new List<SidebarSite> {
                new SidebarSite { Id = "discord", Name = "Discord", Url = "https://discord.com/app", Width = 440 },
                new SidebarSite { Id = "twitch", Name = "Twitch", Url = "https://www.twitch.tv/", Mobile = true, Width = 400 },
                new SidebarSite { Id = "youtube", Name = "YouTube", Url = "https://m.youtube.com/", Mobile = true, Width = 400 },
                new SidebarSite { Id = "spotify", Name = "Spotify", Url = "https://open.spotify.com/", Width = 440 },
                new SidebarSite { Id = "whatsapp", Name = "WhatsApp", Url = "https://web.whatsapp.com/", Width = 480 } };
        }
        public static int ClampWidth(double w) { return double.IsNaN(w) || w <= 0 ? 420 : (int)Math.Max(MinWidth, Math.Min(MaxWidth, w)); }
        public static string NewId(string name, IEnumerable<SidebarSite> existing)
        {
            var b = Regex.Replace((name ?? "panel").ToLowerInvariant(), "[^a-z0-9]+", ""); if (b.Length == 0) b = "panel"; if (b.Length > 20) b = b.Substring(0, 20);
            var ids = new HashSet<string>(existing.Where(x => x != null && x.Id != null).Select(x => x.Id));
            var id = b; for (int i = 2; ids.Contains(id); i++) id = b + i;
            return id;
        }
        // what you type in "Add a site": an address (http/https only) -> a site named after its host
        public static SidebarSite FromAddress(string typed, IEnumerable<SidebarSite> existing)
        {
            if (string.IsNullOrWhiteSpace(typed)) return null;
            var u = Omni.Interpret(typed.Trim(), "duckduckgo");
            if (u == null || !Omni.IsWeb(u) || u.StartsWith(Omni.SearchUrl("duckduckgo", ""), StringComparison.OrdinalIgnoreCase)) return null;
            var host = Omni.HostOf(u) ?? "Panel"; if (host.StartsWith("www.")) host = host.Substring(4);
            var name = host.Split('.')[0]; if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name.Substring(1);
            var list = existing.ToList();
            return new SidebarSite { Id = NewId(name, list), Name = name, Url = u, Width = 420 };
        }
        // the brand icon for well-known sites (Font Awesome brands), else null (the rail shows the site's favicon or its first letter)
        public static string Brand(string url)
        {
            var h = (Omni.HostOf(url) ?? "").ToLowerInvariant();
            Func<string, bool> on = d => h == d || h.EndsWith("." + d);
            if (on("discord.com")) return "discord"; if (on("twitch.tv")) return "twitch"; if (on("youtube.com")) return "youtube";
            if (on("spotify.com")) return "spotify"; if (on("whatsapp.com")) return "whatsapp";
            return null;
        }
        // "(3) Discord" -> "3", "(99+) Inbox" -> "99+", "[2] Chat" -> "2", "* (1) x" -> "1"; no count / "(0)" -> null
        static readonly Regex unread = new Regex(@"^\W{0,3}[\(\[](\d{1,4}\+?)[\)\]]", RegexOptions.Compiled);
        public static string Unread(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            var m = unread.Match(title.TrimStart()); if (!m.Success) return null;
            var v = m.Groups[1].Value; if (v.TrimEnd('+').TrimStart('0').Length == 0) return null;
            int n; if (int.TryParse(v.TrimEnd('+'), out n) && n > 99) return "99+";
            return v;
        }
        // a hidden panel sleeps after `minutes` (0 = never), at once in game mode; never while it plays sound or while shown
        public static bool ShouldSleep(bool shown, bool asleep, bool audible, bool game, DateTime hiddenAt, DateTime now, int minutes)
        {
            if (shown || asleep || audible) return false;
            return game || (minutes > 0 && now - hiddenAt >= TimeSpan.FromMinutes(minutes));
        }
        // the phone user agent (Chrome on Android, the reduced form Chrome sends), with the engine's own major version
        public static string MobileAgent(string runtimeVersion)
        {
            var major = (runtimeVersion ?? "").Split('.')[0]; int n; if (!int.TryParse(major, out n)) major = "130";
            return "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" + major + ".0.0.0 Mobile Safari/537.36";
        }
    }

    // one panel while the browser runs
    public partial class SidePanel
    {
        public SidebarSite Site;
        public WebView2 View;                 // null until first opened (and after "Unload")
        public bool Shown, Asleep, Audible, Ready;
        public DateTime HiddenAt = DateTime.Now;
        public string Title, Unread, DesktopAgent;
        public Button RailBtn; public Border Badge; public TextBlock BadgeText; public Border AudioDot; public ContentControl IconHost;
        public CoreWebView2 Core { get { try { return View == null ? null : View.CoreWebView2; } catch { return null; } } }
    }

    public partial class Sidebar
    {
        readonly BrowserWindow w;
        public readonly List<SidePanel> Panels = new List<SidePanel>();
        public SidePanel Open;
        readonly Border rail; readonly StackPanel railList; readonly Grid host, viewBox; readonly GridSplitter splitter;
        ColumnDefinition railCol, hostCol, splitCol;
        readonly ColumnDefinition[] leftCols, rightCols;   // 1.2: rail | panel | splitter on the left, or splitter | panel | rail on the right
        public bool Right;
        readonly TextBlock headTitle; readonly Button uaBtn;
        static string IconDir { get { return Path.Combine(BrowserSettings.LocalDir, "sidebar"); } }

        // builds into the body grid: columns 1-3 (left: rail | panel | splitter) or 5-7 (right: splitter | panel | rail); the pages are in 4
        public Sidebar(BrowserWindow win, Grid body)
        {
            w = win;
            leftCols = new[] { body.ColumnDefinitions[1], body.ColumnDefinitions[2], body.ColumnDefinitions[3] };
            rightCols = new[] { body.ColumnDefinitions[7], body.ColumnDefinitions[6], body.ColumnDefinitions[5] };
            railCol = leftCols[0]; hostCol = leftCols[1]; splitCol = leftCols[2];
            foreach (var c in leftCols.Concat(rightCols)) c.Width = new GridLength(0);
            leftCols[1].MaxWidth = rightCols[1].MaxWidth = SidebarRules.MaxWidth;
            rail = new Border { Width = 46, BorderThickness = new Thickness(0, 0, 1, 0) };
            rail.SetResourceReference(Border.BackgroundProperty, "Rail"); rail.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            var rg = new Grid(); rg.RowDefinitions.Add(new RowDefinition()); rg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            railList = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var sv = new ScrollViewer { Content = railList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            rg.Children.Add(sv);
            var add = Ui.Tool("plus", "Add a site to the sidebar", null, 12); add.Width = 34; add.Height = 34; add.Margin = new Thickness(0, 4, 0, 8); add.HorizontalAlignment = HorizontalAlignment.Center;
            add.Click += (a, b) => AddFlyout(add);
            Grid.SetRow(add, 1); rg.Children.Add(add);
            rail.Child = rg; Grid.SetColumn(rail, 1); body.Children.Add(rail); A11y.Scaled(rail);   // 1.9: the UI scale

            host = new Grid { Visibility = Visibility.Collapsed };
            host.SetResourceReference(Panel.BackgroundProperty, "Bg");
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); host.RowDefinitions.Add(new RowDefinition());   // 1.20: a note row (an extension panel's gap)
            var head = new Grid(); head.SetResourceReference(Panel.BackgroundProperty, "Bar");
            head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headTitle = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
            head.Children.Add(headTitle);
            uaBtn = Ui.Tool("mobile-screen", "Phone / desktop version", () => { if (Open != null) SetMobile(Open, !Open.Site.Mobile); }, 12); uaBtn.Width = 28; uaBtn.Height = 26;
            var bs = Ui.Row(uaBtn,
                Small("arrow-rotate-right", "Reload", () => { if (Open != null && Open.Core != null) Open.Core.Reload(); }),
                Small("up-right-from-square", "Open in a tab", () => { if (Open != null) w.NewTab(Open.Core != null ? Open.Core.Source : Open.Site.Url, true); }),
                Small("xmark", "Hide the panel (it sleeps after a while)", Hide));
            bs.Margin = new Thickness(0, 0, 4, 0); bs.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bs, 1); head.Children.Add(bs);
            var hl = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom }; hl.SetResourceReference(Border.BackgroundProperty, "LineDim"); Grid.SetColumnSpan(hl, 2); head.Children.Add(hl);
            host.Children.Add(head);
            viewBox = new Grid(); Grid.SetRow(viewBox, 2); host.Children.Add(viewBox);
            BuildNoteHost120(host);   // 1.20
            A11y.SetScaleHost(host, true); A11y.SetNoScale(viewBox, true); A11y.SetScaleHost(viewBox, true);   // 1.9: the header and the browser's own panels scale, web panels never
            Grid.SetColumn(host, 2); body.Children.Add(host);

            splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Columns, Visibility = Visibility.Collapsed, ToolTip = "Drag to resize the panel" };
            splitter.SetResourceReference(Control.BackgroundProperty, "LineDim");
            splitter.DragCompleted += (a, b) => { if (Open != null) { Open.Site.Width = SidebarRules.ClampWidth(hostCol.ActualWidth); hostCol.Width = new GridLength(Open.Site.Width); w.S.Save(); } };
            Grid.SetColumn(splitter, 3); body.Children.Add(splitter);
            Rebuild();
            ApplyOn();
        }
        // 1.2: the sidebar on the left or the right (next to vertical tabs it moves to the right)
        public void Dock(bool right)
        {
            if (right == Right) return;
            Right = right;
            var cols = right ? rightCols : leftCols;
            foreach (var c in leftCols.Concat(rightCols)) c.Width = new GridLength(0);
            railCol = cols[0]; hostCol = cols[1]; splitCol = cols[2];
            Grid.SetColumn(rail, right ? 7 : 1); Grid.SetColumn(host, right ? 6 : 2); Grid.SetColumn(splitter, right ? 5 : 3);
            rail.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
            ApplyOn();
            if (Open != null) { hostCol.Width = new GridLength(SidebarRules.ClampWidth(Open.Site.Width)); splitCol.Width = GridLength.Auto; }
        }
        PlacementMode Side { get { return Right ? PlacementMode.Left : PlacementMode.Right; } }
        // 1.9: F6 - is the keyboard in the sidebar, and where it goes (the open panel, else the first entry of the rail)
        public bool Contains(DependencyObject f)
        {
            for (int i = 0; f != null && i < 200; i++) { if (f == rail || f == host) return true; f = (f is Visual ? VisualTreeHelper.GetParent(f) : null) ?? LogicalTreeHelper.GetParent(f); }
            return false;
        }
        public UIElement KeyboardTarget()
        {
            if (rail.Visibility != Visibility.Visible && host.Visibility != Visibility.Visible) return null;
            if (host.Visibility == Visibility.Visible)
            {
                if (Open != null && Open.View != null) return Open.View;
                var f = A11y.FirstFocusable(viewBox); if (f != null) return f;
            }
            return rail.Visibility == Visibility.Visible ? A11y.FirstFocusable(rail) : null;
        }
        static Button Small(string icon, string tip, Action click) { var b = Ui.Tool(icon, tip, click, 11); b.Width = 28; b.Height = 26; return b; }

        // ---- the rail
        public void Rebuild()
        {
            // keep the panels that are still in the list, drop the others (their WebViews go away)
            foreach (var p in Panels.Where(p => !p.Transient && !w.S.SidebarSites.Contains(p.Site)).ToList()) Remove(p, false);
            railList.Children.Clear();
            AddNativeButtons();   // 1.6: the reading list and notes (the browser's own panels) first
            foreach (var site in w.S.SidebarSites)
            {
                var p = Panels.FirstOrDefault(x => x.Site == site);
                if (p == null) { p = new SidePanel { Site = site }; Panels.Add(p); }
                MakeButton(p); railList.Children.Add(p.RailBtn);
            }
            Panels.Sort((a, b) => w.S.SidebarSites.IndexOf(a.Site).CompareTo(w.S.SidebarSites.IndexOf(b.Site)));
        }
        void MakeButton(SidePanel p)
        {
            var g = new Grid { Width = 34, Height = 34 };
            p.IconHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Focusable = false, IsTabStop = false };
            g.Children.Add(p.IconHost);
            p.BadgeText = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            p.Badge = new Border { CornerRadius = new CornerRadius(7), MinWidth = 14, Height = 14, Padding = new Thickness(3, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, -3, 0), Child = p.BadgeText, Visibility = Visibility.Collapsed };
            p.Badge.SetResourceReference(Border.BackgroundProperty, "BadgeBg");
            g.Children.Add(p.Badge);
            p.AudioDot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -1, 1), Visibility = Visibility.Collapsed };
            p.AudioDot.SetResourceReference(Border.BackgroundProperty, "Green");
            g.Children.Add(p.AudioDot);
            var b = new Button { Style = Ui.S("Tool"), Width = 40, Height = 40, Margin = new Thickness(0, 1, 0, 1), Content = g, HorizontalAlignment = HorizontalAlignment.Center };
            b.Click += (a, e) => Toggle(p);
            b.MouseRightButtonUp += (a, e) => { Menu(p); e.Handled = true; };
            p.RailBtn = b;
            Refresh(p);
        }
        public void Refresh(SidePanel p)
        {
            if (p.RailBtn == null) return;
            var brand = SidebarRules.Brand(p.Site.Url);
            var ico = Path.Combine(IconDir, p.Site.Id + ".png");
            if (brand != null) p.IconHost.Content = Ui.Icon(brand, 18, p.Shown ? "AccentBrush" : "Text");
            else if (File.Exists(ico)) { try { var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(ico); img.DecodePixelWidth = 32; img.EndInit(); img.Freeze(); p.IconHost.Content = new Image { Source = img, Width = 18, Height = 18 }; } catch { p.IconHost.Content = Letter(p); } }
            else p.IconHost.Content = Letter(p);
            p.Badge.Visibility = p.Unread != null ? Visibility.Visible : Visibility.Collapsed; p.BadgeText.Text = p.Unread ?? "";
            p.AudioDot.Visibility = p.Audible && !p.Shown ? Visibility.Visible : Visibility.Collapsed;
            p.RailBtn.Opacity = p.Asleep ? 0.6 : 1;
            if (p.Shown) p.RailBtn.SetResourceReference(Control.BackgroundProperty, "AccentSoft"); else p.RailBtn.ClearValue(Control.BackgroundProperty);
            p.RailBtn.ToolTip = p.Site.Name + (p.Unread != null ? " (" + p.Unread + " new)" : "") + (p.Asleep ? " - asleep" : p.Audible ? " - playing" : "") + "\nRight-click (or Shift+F10): phone/desktop, reload, remove";
        }
        static FrameworkElement Letter(SidePanel p)
        {
            var t = new TextBlock { Text = (p.Site.Name ?? "?").Substring(0, 1).ToUpperInvariant(), FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var b = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = t };
            b.SetResourceReference(Border.BorderBrushProperty, "Line"); b.SetResourceReference(Border.BackgroundProperty, "PanelHi");
            return b;
        }

        // ---- on / off (Ctrl+Shift+S)
        public void Toggle() { if (w.Private) { w.Toast("The sidebar's panels use your signed-in profile, so they aren't in private windows.", 4); return; } w.S.SidebarOn = !w.S.SidebarOn; w.S.Save(); ApplyOn(); }
        public void ApplyOn()
        {
            bool on = w.S.SidebarOn && !w.Private && !w.AppMode;   // 1.4: not in private windows (the panels are signed in on your normal profile); 1.6: nor app windows
            rail.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            railCol.Width = on ? GridLength.Auto : new GridLength(0);
            if (!on && Open != null && !Open.Transient) Hide();   // 1.8: the Twitch chat stays beside its stream
        }

        // ---- show / hide a panel
        public void Toggle(SidePanel p) { if (Open == p) Hide(); else Show(p); }
        public void Show(SidePanel p)
        {
            if (w.Private) return;
            if (!w.S.SidebarOn && !p.Transient) { w.S.SidebarOn = true; ApplyOn(); }
            if (NativeOpen != null) RemoveNative();   // 1.6
            if (Open != null && Open != p) HideOnly(Open);
            Open = p; p.Shown = true;
            if (!p.Transient) { w.S.SidebarOpenId = p.Site.Id; w.S.Save(); }   // 1.8: the Twitch chat isn't reopened at start
            host.Visibility = Visibility.Visible; splitter.Visibility = Visibility.Visible;
            hostCol.Width = new GridLength(SidebarRules.ClampWidth(p.Site.Width)); splitCol.Width = GridLength.Auto;
            headTitle.Text = p.Title ?? p.Site.Name;
            UaButton(p);
            uaBtn.Visibility = p.ExtId != null ? Visibility.Collapsed : Visibility.Visible; ShowNote120(p);   // 1.20: an extension's side panel
            if (p.View == null) { var _ = Create(p); }
            else { p.View.Visibility = Visibility.Visible; if (p.Asleep && p.Core != null) { try { p.Core.Resume(); } catch { } } p.Asleep = false; }
            foreach (var x in Panels) if (x.View != null && x != p) x.View.Visibility = Visibility.Hidden;
            Refresh(p);
            w.PanelsChanged();
        }
        public void Hide()
        {
            if (Open == null) { if (NativeOpen != null) HideNative(true); return; }   // 1.6: the reading list / notes
            var was = Open; HideOnly(Open); Open = null; ShowNote120(null);
            if (!was.Transient) { w.S.SidebarOpenId = null; w.S.Save(); } else ChatClosedByYou(was);
            host.Visibility = Visibility.Collapsed; splitter.Visibility = Visibility.Collapsed;
            hostCol.Width = new GridLength(0); splitCol.Width = new GridLength(0);
            w.PanelsChanged();
        }
        void HideOnly(SidePanel p)
        {
            p.Shown = false; p.HiddenAt = DateTime.Now;
            if (p.View != null) p.View.Visibility = Visibility.Hidden;
            Refresh(p);
        }
        void UaButton(SidePanel p)
        {
            uaBtn.Content = Ui.Icon(p.Site.Mobile ? "mobile-screen" : "desktop", 12);
            uaBtn.ToolTip = p.Site.Mobile ? "Phone version - click for the desktop version" : "Desktop version - click for the phone version";
        }

        async Task Create(SidePanel p)
        {
            var v = new WebView2 { Visibility = p.Shown ? Visibility.Visible : Visibility.Hidden };
            try { var c = (Color)w.FindResource("PageBg"); v.DefaultBackgroundColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B); } catch { }
            p.View = v; p.Ready = false; p.Asleep = false;
            viewBox.Children.Add(v);
            try { await Engine.Ensure(v); }
            catch (Exception e)
            {
                // gone again, so the next click tries anew
                if (p.View == v) { viewBox.Children.Remove(v); try { v.Dispose(); } catch { } p.View = null; }
                w.Toast("The " + p.Site.Name + " panel couldn't start: " + e.Message, 8); return;
            }
            if (p.View != v) return;
            var core = v.CoreWebView2;
            p.DesktopAgent = core.Settings.UserAgent;
            if (p.Site.Mobile) core.Settings.UserAgent = SidebarRules.MobileAgent(Engine.RuntimeVersion);
            w.HookPanel(core);
            if (p.ExtId != null) { var ext = p.ExtId; await w.HookShim120(core, w.SidePanelPathOf(w.S.Extensions.FirstOrDefault(e => e.Id == ext)), () => ext, p); if (p.View != v) return; }   // 1.20: chrome.sidePanel's shim
            core.DocumentTitleChanged += (a, b) =>
            {
                p.Title = core.DocumentTitle; var u = SidebarRules.Unread(p.Title);
                if (u != p.Unread) { p.Unread = u; Refresh(p); }
                if (p == Open) headTitle.Text = string.IsNullOrWhiteSpace(p.Title) ? p.Site.Name : p.Site.Name + "  -  " + p.Title;
            };
            core.IsDocumentPlayingAudioChanged += (a, b) => { p.Audible = core.IsDocumentPlayingAudio; Refresh(p); };
            core.NavigationCompleted += (a, b) => { p.Ready = true; };
            core.FaviconChanged += async (a, b) =>
            {
                if (string.IsNullOrEmpty(core.FaviconUri)) return;
                bool brand = SidebarRules.Brand(p.Site.Url) != null;
                try
                {
                    using (var s = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                    {
                        if (s == null || s.Length == 0) return;
                        var ms = new MemoryStream(); s.CopyTo(ms); var bytes = ms.ToArray();
                        var src = core.Source; var _s = System.Threading.Tasks.Task.Run(() => SiteIcons.Save(src, bytes, false));   // 1.14: the site logo cache (panels are never private)
                        if (brand) return;
                        Directory.CreateDirectory(IconDir);
                        File.WriteAllBytes(Path.Combine(IconDir, p.Site.Id + ".png"), bytes);
                    }
                    Refresh(p);
                }
                catch { }
            };
            core.NewWindowRequested += async (a, b) => { var d = b.GetDeferral(); try { await w.AdoptNewWindow(b, null); } finally { d.Complete(); } };
            core.ProcessFailed += (a, b) => { if (b.ProcessFailedKind != CoreWebView2ProcessFailedKind.BrowserProcessExited && p.Shown) try { core.Reload(); } catch { } };
            try { core.Navigate(p.Site.Url); } catch { }
        }

        // phone / desktop: a new user agent and a fresh load of the site's address (sites pick their version when the page loads)
        public void SetMobile(SidePanel p, bool mobile)
        {
            p.Site.Mobile = mobile; w.S.Save();
            var c = p.Core;
            if (c != null)
            {
                try { c.Settings.UserAgent = mobile ? SidebarRules.MobileAgent(Engine.RuntimeVersion) : p.DesktopAgent; } catch { }
                try { c.Navigate(p.Site.Url); } catch { }
            }
            if (p == Open) UaButton(p);
        }
        // "Unload": the panel's page is closed (it loads again when you open it); it stays in the sidebar
        public void Unload(SidePanel p)
        {
            if (p == Open) Hide();
            if (p.ExtId != null) Panels.Remove(p);   // 1.20: an extension's panel goes away when unloaded
            if (p.View != null) { viewBox.Children.Remove(p.View); try { p.View.Dispose(); } catch { } p.View = null; }
            p.Asleep = false; p.Audible = false; p.Ready = false; Refresh(p);
        }
        public void Remove(SidePanel p, bool fromSettings = true)
        {
            Unload(p); Panels.Remove(p);
            if (fromSettings) { w.S.SidebarSites.Remove(p.Site); w.S.Save(); railList.Children.Remove(p.RailBtn); }
            try { File.Delete(Path.Combine(IconDir, p.Site.Id + ".png")); } catch { }
        }
        public SidePanel Add(SidebarSite site)
        {
            w.S.SidebarSites.Add(site); w.S.Save();
            Rebuild();
            return Panels.FirstOrDefault(x => x.Site == site);
        }

        // ---- every 5 s (the window's tick): hidden panels go to sleep
        public void Tick(bool game)
        {
            var now = DateTime.Now;
            foreach (var p in Panels)
            {
                var c = p.Core; if (c == null) continue;
                bool asleep; try { asleep = c.IsSuspended; } catch { asleep = p.Asleep; }
                if (p.Asleep != asleep) { p.Asleep = asleep; Refresh(p); }
                if (SidebarRules.ShouldSleep(p.Shown, p.Asleep, p.Audible, game, p.HiddenAt, now, w.S.SidebarSleepMinutes)) { var _ = Sleep(p); }
            }
        }
        public async Task Sleep(SidePanel p)
        {
            var c = p.Core; if (c == null || p.Shown || p.Asleep) return;
            try { if (await c.TrySuspendAsync()) { p.Asleep = true; Refresh(p); } } catch { }
        }
        public void ReopenLast()
        {
            if (!w.S.SidebarOn || w.S.SidebarOpenId == null) return;
            var p = Panels.FirstOrDefault(x => x.Site.Id == w.S.SidebarOpenId); if (p != null) Show(p);
        }
        public void FullScreen(bool on)
        {
            if (on && Open != null && Open == Chat) { rail.Visibility = Visibility.Collapsed; railCol.Width = new GridLength(0); return; }   // 1.8: the chat beside a full-screen stream
            if (on) { rail.Visibility = host.Visibility = splitter.Visibility = Visibility.Collapsed; railCol.Width = hostCol.Width = splitCol.Width = new GridLength(0); return; }
            ApplyOn();
            if (Open != null) { host.Visibility = splitter.Visibility = Visibility.Visible; hostCol.Width = new GridLength(SidebarRules.ClampWidth(Open.Site.Width)); splitCol.Width = GridLength.Auto; }
        }
        // --snap: a made-up panel (WPF-drawn, never a real site) with made-up badges
        public void SnapShow(string id, FrameworkElement mock, Dictionary<string, string> badges)
        {
            foreach (var p in Panels) { string b; p.Unread = badges != null && badges.TryGetValue(p.Site.Id, out b) ? b : null; if (p.Site.Id == "spotify") p.Audible = true; Refresh(p); }
            var sp = Panels.FirstOrDefault(x => x.Site.Id == id); if (sp == null) return;
            Open = sp; sp.Shown = true; Refresh(sp);
            host.Visibility = splitter.Visibility = Visibility.Visible; hostCol.Width = new GridLength(sp.Site.Width); splitCol.Width = GridLength.Auto;
            headTitle.Text = sp.Site.Name; UaButton(sp);
            viewBox.Children.Clear(); viewBox.Children.Add(mock);
        }
        public void SetBackground(Color c) { foreach (var p in Panels) if (p.View != null) try { p.View.DefaultBackgroundColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B); } catch { } }

        // ---- right-click on a rail icon
        void Menu(SidePanel p)
        {
            var sp = new StackPanel(); Popup pop = null;
            Action<Action> run = a => { pop.IsOpen = false; a(); };
            sp.Children.Add(Ui.MenuRow(p.Shown ? "xmark" : "table-columns", p.Shown ? "Hide " + p.Site.Name : "Open " + p.Site.Name, null, () => run(() => Toggle(p))));
            sp.Children.Add(Ui.MenuRow(p.Site.Mobile ? "desktop" : "mobile-screen", p.Site.Mobile ? "Desktop version" : "Phone version", null, () => run(() => SetMobile(p, !p.Site.Mobile))));
            if (p.Core != null) sp.Children.Add(Ui.MenuRow("arrow-rotate-right", "Reload", null, () => run(() => { try { p.Core.Reload(); } catch { } })));
            if (p.Core != null) sp.Children.Add(Ui.MenuRow("moon", "Unload (frees its memory)", null, () => run(() => Unload(p))));
            sp.Children.Add(Ui.MenuRow("up-right-from-square", "Open in a tab", null, () => run(() => w.NewTab(p.Core != null ? p.Core.Source : p.Site.Url, true))));
            sp.Children.Add(Ui.MenuRow("trash-can", "Remove from the sidebar", null, () => run(() => Remove(p))));
            Panels_CloseOpen();
            pop = new Popup { Child = Ui.Flyout(sp, 240), PlacementTarget = p.RailBtn, Placement = Side, StaysOpen = false, AllowsTransparency = true };
            pop.IsOpen = true;
        }
        static void Panels_CloseOpen() { HallowBrowser.Panels.CloseFlyouts(); }
        void AddFlyout(Button at)
        {
            var sp = new StackPanel { Margin = new Thickness(8) };
            sp.Children.Add(Ui.Label("Add a site"));
            var box = new TextBox { Style = Ui.S("Field"), Height = 28, Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(box);
            var mobile = new CheckBox { Content = new TextBlock { Text = "Phone version" }, Style = Ui.S("Switch"), Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(mobile);
            var msg = Ui.Muted("An address, e.g. discord.com or reddit.com. It stays signed in like a tab.", 11, 8); sp.Children.Add(msg);
            Popup pop = null;
            Action go = () =>
            {
                var site = SidebarRules.FromAddress(box.Text, w.S.SidebarSites);
                if (site == null) { msg.Text = "That isn't a web address (http or https)."; return; }
                site.Mobile = mobile.IsChecked == true; pop.IsOpen = false;
                var p = Add(site); if (p != null) Show(p);
            };
            box.KeyDown += (a, b) => { if (b.Key == Key.Enter) { go(); b.Handled = true; } };
            sp.Children.Add(Ui.Row(Ui.Btn("Add", go, true, "plus")));
            Panels_CloseOpen();
            pop = new Popup { Child = Ui.Flyout(sp, 300), PlacementTarget = at, Placement = Side, StaysOpen = false, AllowsTransparency = true };
            pop.Opened += (a, b) => box.Focus();
            pop.IsOpen = true;
        }
    }
}
