// Hallow Browser - the window: tab strip in the title bar, toolbar with the address bar, find bar, and the pages.
// The browser's own UI is plain WPF (no WebView for the chrome), so it draws at once; the pages are WebView2 controls on one
// shared environment, with one hidden, pre-warmed WebView waiting to become the next new tab.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public partial class BrowserWindow : Window
    {
        public BrowserSettings S;
        public readonly List<Tab> Tabs = new List<Tab>();
        public Tab Active;
        readonly List<SessionTab> closed = new List<SessionTab>();
        public readonly List<DownloadItem> Downloads = new List<DownloadItem>();
        public IGameSource Games;
        public string Game;                       // the running game (game mode on), or null
        // 1.4: Shield's state lives in a box - shared by every normal window (one profile), private windows have their own (InPrivate profile)
        public ShieldBox SB = Program.MainShield;
        public string ShieldState { get { return SB.State; } set { SB.State = value; } }   // extension | fallback | missing | starting
        public string ShieldId { get { return SB.Id; } set { SB.Id = value; } }
        public string ShieldVersion { get { return SB.Version; } set { SB.Version = value; } }
        public string ShieldFolder { get { return SB.Folder; } set { SB.Folder = value; } }
        public string ShieldNote { get { return SB.Note; } set { SB.Note = value; } }
        public ShieldHost.Rules Fallback { get { return SB.Fallback; } set { SB.Fallback = value; } }
        public int FallbackBlocked { get { return SB.Blocked; } set { SB.Blocked = value; } }
        public readonly bool Private;              // 1.4: a private window (InPrivate profile: no history, session, bookmark or sync writes)
        public string UpdateNote; public GhRelease Update;
        public Sidebar Side; public LiveWatcher Live;   // 1.1
        bool snap;

        // UI
        Grid root, content; StackPanel strip; Grid stripHost; Border addressBox; public TextBox Address;
        Button back, fwd, reload, keyBtn, zoomChip, shieldBtn, dlBtn, quickBtn, menuBtn, maxBtn, newBtn;
        Border gameChip; TextBlock gameText; Grid findBar; TextBox findBox; TextBlock findCount; Border popPlaceholder;
        Popup toast, suggest; TextBlock toastText; StackPanel suggestList; Border dlBadge; System.Windows.Shapes.Path lockIcon;
        WebView2 prewarm; bool prewarmReady;
        readonly DispatcherTimer tick, saveTimer, statusTimer;
        bool fullscreen; WindowState preFullState; Thickness preFullMargin;

        public BrowserWindow(BrowserSettings s, bool snapMode, bool priv = false)
        {
            S = s; snap = snapMode; Private = priv;
            if (priv) { SB = Program.PrivateShield; PrivateLook(); }
            Title = priv ? "Private - Hallow Browser" : "Hallow Browser"; Icon = Ui.AppIcon();
            SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Text");
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12.5;
            Width = S.Width; Height = S.Height; MinWidth = 480; MinHeight = 360;
            if (!double.IsNaN(S.Left) && !double.IsNaN(S.Top) && OnScreen(S.Left, S.Top)) { WindowStartupLocation = WindowStartupLocation.Manual; Left = S.Left; Top = S.Top; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (S.Maximized && !BrowserSettings.Offscreen) WindowState = WindowState.Maximized;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 38, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            Ui.DarkTitle(this);
            Games = BrowserSettings.FakeGameFile != null ? (IGameSource)new FileGames(BrowserSettings.FakeGameFile) : new WindowsGames();
            Build();
            if (priv) PrivateChrome();
            ProfileChrome();   // 1.6: the profile's colour and name (when there's more than one profile)
            StateChanged += (a, b) => FitMaximized();
            PreviewKeyDown += OnKey;
            ContentRendered += (a, b) => Engine.Mark("chrome-first-paint");
            Closing += OnClosing;
            tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) }; tick.Tick += (a, b) => Tick(false);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; saveTimer.Tick += (a, b) => { saveTimer.Stop(); SaveSession(); };
            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) }; statusTimer.Tick += (a, b) => { statusTimer.Stop(); WriteStatus(); };
            if (BrowserSettings.Offscreen) { WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; ShowInTaskbar = false; ShowActivated = false; SourceInitialized += (a, b) => NoActivate(); }
        }
        static bool OnScreen(double l, double t) { return l > SystemParameters.VirtualScreenLeft - 50 && t > SystemParameters.VirtualScreenTop - 20 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60; }
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        void NoActivate() { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); }   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW

        // ================================================================ layout
        void Build()
        {
            root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            Content = root;

            // ---- row 0: the tab strip (it's the title bar too)
            var top = new Grid(); top.SetResourceReference(Panel.BackgroundProperty, "Strip");
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // logo / quick panel
            top.ColumnDefinitions.Add(new ColumnDefinition());                             // tabs + new tab + drag space
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // game chip
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // caption buttons
            var logo = new Button { Style = Ui.S("Tool"), Width = 38, Height = 38, ToolTip = "Hallow quick panel", Content = new Image { Source = Ui.AppIcon(), Width = 20, Height = 20 } };
            logo.Click += (a, b) => Panels.Quick(this, logo); Chrome(logo);
            top.Children.Add(Ui.Row(logo, BuildWorkspaceButton()));   // 1.2: the workspace switcher, top left
            stripHost = new Grid { ClipToBounds = true }; Grid.SetColumn(stripHost, 1);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            strip = new StackPanel { Orientation = Orientation.Horizontal };
            newBtn = Ui.Tool("plus", "New tab (Ctrl+T)", () => NewTab(Omni.NewTabUrl, true), 12); newBtn.Margin = new Thickness(4, 0, 0, 3); Chrome(newBtn);
            sp.Children.Add(strip); sp.Children.Add(newBtn);
            stripHost.Children.Add(sp); stripHost.SizeChanged += (a, b) => FitTabs();
            top.Children.Add(stripHost);
            gameChip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 10, 2), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed };
            gameChip.SetResourceReference(Border.BackgroundProperty, "AccentBtnBg"); gameChip.SetResourceReference(Border.BorderBrushProperty, "AccentBtnLine");   // 1.3: filled in light mode
            gameText = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)FindResource("MonoFont") };
            gameText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBtnText");
            var gi = Ui.Icon("gamepad", 12, "AccentBtnText"); gi.Margin = new Thickness(0, 0, 6, 0);
            gameChip.Child = Ui.Row(gi, gameText);
            gameChip.ToolTip = "Gaming-light mode: background tabs sleep and the browser runs at a lower priority while you play. Tabs playing sound stay awake.";
            Grid.SetColumn(gameChip, 2); top.Children.Add(gameChip);
            var caps = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(caps, 3);
            var minB = Caption("window-minimize", "Minimise", () => WindowState = WindowState.Minimized);
            maxBtn = Caption("window-maximize", "Maximise", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            var closeB = Caption("xmark", "Close", Close); closeB.Tag = "close";
            caps.Children.Add(minB); caps.Children.Add(maxBtn); caps.Children.Add(closeB);
            top.Children.Add(caps);
            root.Children.Add(top);

            // ---- row 1: the toolbar
            var bar = new Grid { Height = 42 }; bar.SetResourceReference(Panel.BackgroundProperty, "Bar"); Grid.SetRow(bar, 1);
            for (int i = 0; i < 3; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            back = Ui.Tool("arrow-left", "Back (Alt+Left)", GoBack); fwd = Ui.Tool("arrow-right", "Forward (Alt+Right)", GoForward); reload = Ui.Tool("arrow-rotate-right", "Reload (F5)", ReloadOrStop);
            left.Children.Add(back); left.Children.Add(fwd); left.Children.Add(reload); bar.Children.Add(left);
            addressBox = new Border { CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1), Height = 30, Margin = new Thickness(2, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            addressBox.SetResourceReference(Border.BackgroundProperty, "FieldBg"); addressBox.SetResourceReference(Border.BorderBrushProperty, "LineDim");
            Grid.SetColumn(addressBox, 1);
            var ag = new Grid(); ag.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); ag.ColumnDefinitions.Add(new ColumnDefinition()); ag.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            lockIcon = Ui.Icon("magnifying-glass", 12, "Muted"); ag.Children.Add(LockButton(lockIcon));   // 1.4: the padlock is a button (site info and permissions)
            Address = new TextBox { Style = Ui.S("Address") }; Grid.SetColumn(Address, 1); ag.Children.Add(Address);
            Address.GotKeyboardFocus += (a, b) => { addressBox.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); addressBox.SetResourceReference(Border.BackgroundProperty, "FieldFocus"); Dispatcher.BeginInvoke(new Action(Address.SelectAll), DispatcherPriority.Input); };
            Address.LostKeyboardFocus += (a, b) => { addressBox.SetResourceReference(Border.BorderBrushProperty, "LineDim"); addressBox.SetResourceReference(Border.BackgroundProperty, "FieldBg"); if (!suggestList.IsKeyboardFocusWithin) suggest.IsOpen = false; };
            Address.PreviewMouseLeftButtonDown += (a, b) => { if (!Address.IsKeyboardFocusWithin) { Address.Focus(); b.Handled = true; } };
            Address.KeyDown += AddressKey; Address.TextChanged += (a, b) => { if (Address.IsKeyboardFocusWithin) Suggest(); };
            var inBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) }; Grid.SetColumn(inBar, 2);
            zoomChip = new Button { Style = Ui.S("Tool"), Width = double.NaN, Height = 24, Padding = new Thickness(6, 0, 6, 0), Visibility = Visibility.Collapsed, ToolTip = "Zoom - click for 100%" };
            zoomChip.Click += (a, b) => SetZoom(1.0);
            keyBtn = Ui.Tool("key", "Fill a login from Hallow Keys", () => Panels.Keys(this, keyBtn), 13); keyBtn.Height = 24; keyBtn.Width = 28; keyBtn.Visibility = Visibility.Collapsed;
            inBar.Children.Add(zoomChip); inBar.Children.Add(keyBtn);
            BuildAddressExtras(inBar);   // 1.2: reader mode, bookmark star
            ag.Children.Add(inBar);
            addressBox.Child = ag; bar.Children.Add(addressBox);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(right, 2);
            shieldBtn = Ui.Tool("shield-halved", "Hallow Shield - this site, levels, filters", () => { var _ = Panels.Shield(this, shieldBtn); }, 14);
            right.Children.Add(BuildExtButton());   // 1.4: your own extensions (an overflow menu)
            shieldBtn.Content = Ui.Icon("shield-halved", 14, "AccentBrush");
            var dlGrid = new Grid(); dlGrid.Children.Add(Ui.Icon("download", 14));
            dlBadge = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -4, 0), Visibility = Visibility.Collapsed };
            dlBadge.SetResourceReference(Border.BackgroundProperty, "AccentBrush"); dlGrid.Children.Add(dlBadge);
            dlBtn = new Button { Style = Ui.S("Tool"), Content = dlGrid, ToolTip = "Downloads (Ctrl+J)" }; dlBtn.Click += (a, b) => Panels.DownloadsFlyout(this, dlBtn);
            quickBtn = Ui.Tool("gamepad", "Game and Hallow panel", () => Panels.Quick(this, quickBtn), 15);
            menuBtn = Ui.Tool("bars", "Menu", () => Panels.Menu(this, menuBtn), 14);
            right.Children.Add(BuildMediaButton());   // 1.5: media playing in any tab
            right.Children.Add(shieldBtn); right.Children.Add(dlBtn); right.Children.Add(quickBtn); right.Children.Add(menuBtn);
            bar.Children.Add(right);
            // 1.3: the toolbar and, under it, the bookmarks bar (Ctrl+Shift+B)
            var row1 = new StackPanel(); Grid.SetRow(row1, 1); row1.Children.Add(bar); row1.Children.Add(BuildBookmarksBar());
            root.Children.Add(row1);

            // ---- row 2: find in page (hidden until Ctrl+F)
            findBar = new Grid { Height = 36, Visibility = Visibility.Collapsed }; findBar.SetResourceReference(Panel.BackgroundProperty, "Bar");
            var fl = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top }; fl.SetResourceReference(Border.BackgroundProperty, "LineDim"); findBar.Children.Add(fl);
            findBox = new TextBox { Style = Ui.S("Field"), Width = 260, Height = 26, Padding = new Thickness(6, 2, 6, 2) };
            findBox.TextChanged += (a, b) => FindStart(); findBox.KeyDown += (a, b) => { if (b.Key == Key.Enter) { FindNext((Keyboard.Modifiers & ModifierKeys.Shift) == 0); b.Handled = true; } else if (b.Key == Key.Escape) { HideFind(); b.Handled = true; } };
            findCount = Ui.Muted("", 11.5); findCount.VerticalAlignment = VerticalAlignment.Center; findCount.Margin = new Thickness(10, 0, 6, 0); findCount.MinWidth = 50;
            var fr = Ui.Row(Ui.Icon("magnifying-glass", 12, "Muted"), new Border { Width = 8 }, findBox, findCount, Ui.Tool("chevron-up", "Previous (Shift+Enter)", () => FindNext(false), 11), Ui.Tool("chevron-down", "Next (Enter)", () => FindNext(true), 11), Ui.Tool("xmark", "Close (Esc)", HideFind, 11));
            fr.HorizontalAlignment = HorizontalAlignment.Right; fr.Margin = new Thickness(0, 0, 12, 0); fr.VerticalAlignment = VerticalAlignment.Center;
            findBar.Children.Add(fr);
            var row2 = new StackPanel(); Grid.SetRow(row2, 2); BuildInfoBar(row2); row2.Children.Add(findBar); root.Children.Add(row2);   // 1.5: an info bar (e.g. "Restore your tabs") above the find bar

            // ---- row 3: pages
            // 1.1: the body is the sidebar (rail | panel | splitter) and the pages
            // 1.2: vertical tabs | sidebar on the left (rail | panel | splitter) | pages (split view inside) | sidebar on the right
            var body = new Grid(); Grid.SetRow(body, 3);
            for (int i = 0; i < 8; i++) body.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            content = new Grid(); content.SetResourceReference(Panel.BackgroundProperty, "Bg"); Grid.SetColumn(content, 4); body.Children.Add(content);
            BuildSplit(); BuildVerticalTabs(body);
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top }; line.SetResourceReference(Border.BackgroundProperty, "LineDim"); Grid.SetRow(line, 3);
            popPlaceholder = new Border { Visibility = Visibility.Collapsed };
            var ppSp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ppI = Ui.Icon("up-right-from-square", 34, "Lilac"); ppI.Margin = new Thickness(0, 0, 0, 14); ppSp.Children.Add(ppI);
            var ppT = Ui.T("This tab is playing in the pop-out window", 16, true, "Text", 6); ppT.HorizontalAlignment = HorizontalAlignment.Center; ppSp.Children.Add(ppT);
            var ppM = Ui.Muted("It stays on top of your other windows - and of games in borderless or windowed mode.", 12, 16); ppM.HorizontalAlignment = HorizontalAlignment.Center; ppSp.Children.Add(ppM);
            var ppB = Ui.Btn("Bring it back here", () => { if (Active != null) Pip.Return(this, Active); }, true, "rotate-left"); ppB.HorizontalAlignment = HorizontalAlignment.Center; ppSp.Children.Add(ppB);
            popPlaceholder.Child = ppSp; content.Children.Add(popPlaceholder);
            Side = new Sidebar(this, body);
            root.Children.Add(body); root.Children.Add(line);
            ApplyTabLayout();

            // ---- toast and suggestions
            toastText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
            var tb = Ui.Flyout(toastText, double.NaN); tb.Padding = new Thickness(14, 9, 14, 9); tb.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            toast = new Popup { Child = tb, PlacementTarget = content, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = true };
            toast.Opened += (a, b) => { toast.HorizontalOffset = 16; toast.VerticalOffset = -((FrameworkElement)toast.Child).ActualHeight - 16; };
            suggestList = new StackPanel();
            suggest = new Popup { Child = Ui.Flyout(suggestList, double.NaN), PlacementTarget = addressBox, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = true, VerticalOffset = 4 };
            Deactivated += (a, b) => { suggest.IsOpen = false; };
        }
        static void Chrome(UIElement e) { WindowChrome.SetIsHitTestVisibleInChrome(e, true); }
        Button Caption(string icon, string tip, Action click)
        {
            var b = new Button { Style = Ui.S("Caption"), ToolTip = tip, Content = Ui.Icon(icon, 10, "Muted") };
            ((System.Windows.Shapes.Path)b.Content).SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = b });
            b.Click += (a, e) => click(); Chrome(b); return b;
        }
        void FitMaximized()
        {
            if (fullscreen) return;
            // a maximised WindowChrome window hangs over the screen edge by the resize border
            root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            maxBtn.Content = Ui.Icon(WindowState == WindowState.Maximized ? "window-restore" : "window-maximize", 10, "Muted");
            ((System.Windows.Shapes.Path)maxBtn.Content).SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = maxBtn });
            maxBtn.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximise";
        }

        // ================================================================ start
        public async Task Start(List<string> urls)
        {
            Engine.Mark("window");
            if (snap) return;
            try { await (Engine.Starting ?? Engine.Create(S)); }
            catch (Exception e) { Toast("The page engine (Microsoft Edge WebView2) didn't start: " + e.Message, 15); return; }
            Engine.WriteNewTab(S, Program.AccentHex);
            RefreshNewTabData();   // 1.3: tiles and the Hallow strip (in the background)
            // 1.2: the current workspace's session (session.json is always the current workspace's); its groups and split come back too
            LoadSpaces();
            Session session = null;
            if (S.RestoreSession) { session = BrowserSettings.LoadSession(); if (session == null && CurrentSpace != null) session = CurrentSpace.Session; }
            // the tab you'll see first is made first; the others wait (as discarded tabs) until you click them
            await OpenSession(session, urls, true);
            tick.Start(); Tick(true);
            if (session != null && S.RestoreSession) Program.RestoreWindows(session.Windows);   // 1.4: the other windows from last time
            CrashRecovery();   // 1.5: didn't close properly last time -> say so / "Restore your tabs"
            // background work after the first page is on its way
            var _ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(1200); MakePrewarm(); await Task.Delay(800); Side.ReopenLast(); await Task.Delay(500); EnsureStores(); await Task.Delay(500); Vibe.Ambience(this); }), DispatcherPriority.ApplicationIdle);   // 1.6: the ambience (when it's on)
            Live = new LiveWatcher(this); Live.Start();
            BackgroundChecks();
            WriteStatus();
        }

        // ================================================================ tabs
        public Tab NewTab(string url, bool activate, int index = -1)
        {
            if (AppMode && Tabs.Count > 0) return OutOfApp(url);   // 1.6: an app window has its one page; anything else opens in the browser
            if (Private && (url == null || Omni.IsNewTab(url))) url = Omni.PrivateTabUrl;   // 1.4: a private window's own new tab page
            if (Private && Omni.IsInternal(url) && url != Omni.SettingsUrl) { var m = Program.First; if (m != null) { var mt = m.NewTab(url, true); m.Bring(); return mt; } }
            if (Omni.IsInternal(url)) { var ex = Tabs.FirstOrDefault(x => x.Url == url); if (ex != null) { Activate(ex); return ex; } }
            var t = new Tab { Url = url ?? Omni.NewTabUrl, Title = url == null || Omni.IsNewTab(url) ? "New tab" : Omni.HostOf(url) ?? "Tab", OpenTimer = Stopwatch.StartNew() };
            int at = index >= 0 ? index : Active != null ? Tabs.IndexOf(Active) + 1 : Tabs.Count;
            at = Math.Max(0, Math.Min(at, Tabs.Count));
            AddHeader(t, at); Tabs.Insert(at, t);
            // 1.2: opened between two tabs of a group = in that group
            t.Group = GroupRules.Drop(at > 0 ? Tabs[at - 1].Group : null, at + 1 < Tabs.Count ? Tabs[at + 1].Group : null, null, null, false);
            RenderStrip();
            if (t.OpenTimer != null) lock (Engine.Perf) Engine.Perf["newtab-header"] = t.OpenTimer.Elapsed.TotalMilliseconds;
            if (Omni.IsInternal(t.Url)) ShowSettingsIn(t);
            else if (!snap)
            {
                if (prewarmReady && prewarm != null && Omni.IsNewTab(t.Url)) { TakePrewarm(t); }
                else { t.PendingUrl = t.Url; var _ = CreateView(t, null); }
            }
            if (activate) Activate(t);
            Changed();
            Vibe.Play("open", this);   // 1.6 (when sounds are on)
            return t;
        }
        async Task CreateView(Tab t, CoreWebView2NewWindowRequestedEventArgs nw)
        {
            var v = new WebView2 { Visibility = IsShown(t) && !t.ReaderOn ? Visibility.Visible : Visibility.Hidden };
            try { var c = (Color)FindResource("PageBg"); v.DefaultBackgroundColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B); } catch { }
            t.View = v; t.Discarded = false; t.Ready = false;
            content.Children.Add(v); LayoutSplit();
            v.GotFocus += (a, b) => ViewFocused(Tabs.FirstOrDefault(x => x.View == v));
            try { await EnsureCore(v); }
            catch (Exception e) { Toast("A page couldn't start: " + e.Message, 8); return; }
            if (t.View != v) return;
            Hook(t, v.CoreWebView2);
            if (nw == null && t.PendingUrl != null) { var u = t.PendingUrl; t.PendingUrl = null; Go(t, u); }
        }
        void Go(Tab t, string url)
        {
            url = Omni.FixNewTab(url);   // 1.3: a 1.2 session's "https://newtab.hallow/"
            var c = t.Core; if (c == null) { t.PendingUrl = url; return; }
            try { c.Navigate(url); } catch (Exception e) { Toast("Couldn't open that address: " + e.Message, 6); }
        }

        // the next new tab is ready before you ask for it
        void MakePrewarm()
        {
            if (snap || prewarm != null || Engine.Env == null) return;
            var v = new WebView2 { Visibility = Visibility.Hidden };
            try { var c = (Color)FindResource("PageBg"); v.DefaultBackgroundColor = System.Drawing.Color.FromArgb(c.R, c.G, c.B); } catch { }
            prewarm = v; prewarmReady = false; lock (Engine.Perf) Engine.Perf["prewarm-made"] = Engine.SinceStart();
            content.Children.Add(v);
            v.CoreWebView2InitializationCompleted += (a, b) =>
            {
                if (!b.IsSuccess || prewarm != v) return;
                lock (Engine.Perf) Engine.Perf["prewarm-init"] = Engine.SinceStart();

                Hook(null, v.CoreWebView2, true);
                EventHandler<CoreWebView2NavigationCompletedEventArgs> done = null;
                done = (x, y) => { v.CoreWebView2.NavigationCompleted -= done; if (prewarm == v) prewarmReady = true; lock (Engine.Perf) Engine.Perf["prewarm-ready"] = Engine.SinceStart(); };
                v.CoreWebView2.NavigationCompleted += done;
                v.CoreWebView2.Navigate(Private ? Omni.PrivateTabUrl : Omni.NewTabUrl);
            };
            var _ = EnsureCore(v);
        }
        void TakePrewarm(Tab t)
        {
            var v = prewarm; prewarm = null; prewarmReady = false;
            t.View = v; t.Ready = true; t.Url = Private ? Omni.PrivateTabUrl : Omni.NewTabUrl; t.Discarded = false;
            v.GotFocus += (a, b) => ViewFocused(Tabs.FirstOrDefault(x => x.View == v));
            Rehook(t, v.CoreWebView2);
            try { var _r = v.CoreWebView2.ExecuteScriptAsync("window.hallowRefresh&&hallowRefresh()"); } catch { }   // 1.3: the tiles as they are now
            if (t.OpenTimer != null) { lock (Engine.Perf) Engine.Perf["newtab-swap"] = t.OpenTimer.Elapsed.TotalMilliseconds; }
            var timer = t.OpenTimer; t.OpenTimer = null;
            // shown = after the next layout and render pass
            if (timer != null) Dispatcher.BeginInvoke(new Action(() => { lock (Engine.Perf) Engine.Perf["newtab-last"] = timer.Elapsed.TotalMilliseconds; }), DispatcherPriority.Loaded);
            // 1.3: the next one starts at once - now that the new tab page really loads (1.2's showed an error page on newer runtimes, see
            // Omni.NewTabUrl), a fresh view takes ~2 s to its first document, so waiting 600 ms more left quick second tabs cold
            var _ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(60); MakePrewarm(); }), DispatcherPriority.Background);
        }
        // warms the connection to an address (the top suggestion) through the hidden WebView - same network stack as the tabs
        void Preconnect(string url)
        {
            if (!S.Preconnect || prewarm == null || !prewarmReady) return;
            var o = Omni.Origin(url); if (o == null) return;
            try { var _ = prewarm.CoreWebView2.ExecuteScriptAsync("(function(){var l=document.createElement('link');l.rel='preconnect';l.href=" + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(o) + ";document.head.appendChild(l);})()"); } catch { }
        }

        // the tab's header (1.2: placed by RenderStrip, in the title bar or the vertical column; callers render after adding the tab)
        void AddHeader(Tab t, int index)
        {
            var g = new Grid { Height = 32 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            t.GroupLine = new Border { Visibility = Visibility.Collapsed, IsHitTestVisible = false, CornerRadius = new CornerRadius(1) }; Grid.SetColumnSpan(t.GroupLine, 4); g.Children.Add(t.GroupLine);
            t.IconHost = new ContentControl { Width = 16, Height = 16, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Focusable = false, IsTabStop = false };
            g.Children.Add(t.IconHost);
            t.TitleBlock = new TextBlock { Text = t.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            Grid.SetColumn(t.TitleBlock, 1); g.Children.Add(t.TitleBlock);
            // 1.2: the speaker is a button (mute / unmute this tab); a mark shows the tabs of the split view
            t.SplitMark = Ui.Icon("table-columns", 10, "Lilac"); t.SplitMark.Margin = new Thickness(4, 0, 2, 0); t.SplitMark.Visibility = Visibility.Collapsed; t.SplitMark.ToolTip = "In split view";
            t.Sound = Ui.Icon("volume-high", 11, "Lilac");
            t.SoundBtn = new Button { Style = Ui.S("Tool"), Width = 22, Height = 20, Margin = new Thickness(2, 0, 0, 0), Content = t.Sound, Visibility = Visibility.Collapsed, ToolTip = "Mute this tab" };
            t.SoundBtn.Click += (a, b) => ToggleMute(t);
            t.RightBox = Ui.Row(t.SplitMark, t.SoundBtn); t.RightBox.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(t.RightBox, 2); g.Children.Add(t.RightBox);
            t.CloseBtn = new Button { Style = Ui.S("Tool"), Width = 20, Height = 20, Margin = new Thickness(2, 0, 6, 0), Content = Ui.Icon("xmark", 9, "Muted"), ToolTip = "Close tab (Ctrl+W)" };
            t.CloseBtn.Click += (a, b) => CloseTab(t); Grid.SetColumn(t.CloseBtn, 3); g.Children.Add(t.CloseBtn);
            t.Header = new Border { Child = g, CornerRadius = new CornerRadius(8, 8, 0, 0), Margin = new Thickness(0, 6, 1, 0), Width = 200, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = S.TabPreviews ? null : t.Title, HorizontalAlignment = HorizontalAlignment.Left };   // 1.5: the hover preview replaces the tooltip
            Chrome(t.Header);
            Point down = new Point(); bool dragging = false, outside = false;
            t.Header.MouseLeftButtonDown += (a, b) =>
            {
                if (b.ClickCount != 1) return;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { AddToSplit(t); b.Handled = true; return; }   // 1.2: Ctrl+click = split view
                Activate(t); down = b.GetPosition(TabPanel); dragging = false; t.Header.CaptureMouse(); b.Handled = true;
            };
            t.Header.MouseMove += (a, b) =>
            {
                if (!t.Header.IsMouseCaptured || b.LeftButton != MouseButtonState.Pressed) return;
                var p = b.GetPosition(TabPanel);
                if (!dragging && Math.Abs(S.VerticalTabs ? p.Y - down.Y : p.X - down.X) < 6) return;
                if (!dragging) { dragging = true; dragActive = true; }
                outside = OutsideStrip(b);                 // 1.4: out of the strip = a new window or another window's strip
                t.Header.Opacity = outside ? 0.55 : 1;
                if (outside) { t.Header.Cursor = Cursors.SizeAll; if (Tabs.Count > 1 || Program.Windows.Count(x => x != this && x.Private == Private) > 0) GhostMove(t, b); return; }   // 1.5: the tab's picture follows the mouse
                GhostHide();
                t.Header.Cursor = Cursors.Hand;
                DragTo(t, p);   // reorder; over a group's label = into that group
            };
            t.Header.MouseLeftButtonUp += (a, b) => { GhostHide(); var d = dragging; dragging = false; t.Header.ReleaseMouseCapture(); t.Header.Opacity = t.Suspended || t.Discarded ? 0.62 : 1; t.Header.Cursor = Cursors.Hand; if (d) { if (!(outside && DropOutside(t, b))) DropTab(t); } outside = false; };
            t.Header.LostMouseCapture += (a, b) => { GhostHide(); if (dragging) { dragging = false; DropTab(t); } };
            t.Header.MouseDown += (a, b) => { if (b.ChangedButton == MouseButton.Middle) { CloseTab(t); b.Handled = true; } };
            t.Header.MouseRightButtonUp += (a, b) => { Panels.TabMenu(this, t); b.Handled = true; };
            t.Header.MouseEnter += (a, b) => { if (t != Active) t.Header.SetResourceReference(Border.BackgroundProperty, "TabHover"); HoverEnter(t); };
            t.Header.MouseLeave += (a, b) => { HoverLeave(); if (t != Active) { if (IsShown(t)) t.Header.SetResourceReference(Border.BackgroundProperty, "TabHover"); else t.Header.Background = Brushes.Transparent; } };
            SetTabIcon(t);
        }
        public void MoveTab(Tab t, int to)
        {
            int cur = Tabs.IndexOf(t); if (cur < 0) return;
            to = Math.Max(0, Math.Min(to, Tabs.Count - 1)); if (to == cur) return;
            Tabs.RemoveAt(cur); Tabs.Insert(to, t);
            RenderStrip(); Changed();
        }
        void FitTabs()
        {
            if (Tabs.Count == 0) return;
            if (S.VerticalTabs) { bool ic = S.VTabsCollapsed; foreach (var t in Tabs) { t.Header.Width = double.NaN; t.CloseBtn.Visibility = ic ? Visibility.Collapsed : Visibility.Visible; } return; }
            int vis = Tabs.Count(t => t.Header.Visibility == Visibility.Visible);
            double chips = 0;
            foreach (var g in Groups) if (g.Chip != null && g.Chip.Parent == strip) { g.Chip.Measure(new Size(double.PositiveInfinity, 38)); chips += g.Chip.DesiredSize.Width; }
            double avail = Math.Max(100, stripHost.ActualWidth - 44 - chips);
            double w = vis == 0 ? 200 : Math.Max(48, Math.Min(230, avail / vis - 1));
            foreach (var t in Tabs) { t.Header.Width = w; t.CloseBtn.Visibility = w < 80 && t != Active ? Visibility.Collapsed : Visibility.Visible; }
        }
        void SetTabIcon(Tab t)
        {
            if (t.Favicon != null) t.IconHost.Content = new Image { Source = t.Favicon, Width = 16, Height = 16 };
            else if (t.Loading) t.IconHost.Content = Ui.Icon("arrows-rotate", 12, "AccentBrush");
            else if (t.Internal) t.IconHost.Content = Ui.Icon(t.Url == Omni.BookmarksUrl ? "bookmark" : t.Url == Omni.ExtensionsUrl ? "puzzle-piece" : t.Url == Omni.SitesUrl ? "sliders" : t.Url == Omni.DownloadsUrl ? "download" : t.Url == Omni.AppsUrl ? "table-cells-large" : "gear", 13, "Lilac");
            else if (Private && Omni.IsNewTab(t.Url)) t.IconHost.Content = Ui.Icon("mask", 13, "Lilac");
            else if (Omni.IsNewTab(t.Url)) t.IconHost.Content = new Image { Source = Ui.AppIcon(), Width = 16, Height = 16 };
            else t.IconHost.Content = Ui.Icon("globe", 13, "Muted");
            t.Header.Opacity = t.Suspended || t.Discarded ? 0.62 : 1;
            SleepMark(t);   // 1.6: a leaf and "about N MB saved" on a sleeping tab
            t.SoundBtn.Visibility = t.Audible || t.Muted ? Visibility.Visible : Visibility.Collapsed;
            t.Sound.Data = Icons.Get(t.Muted ? "volume-xmark" : "volume-high");
            t.SoundBtn.ToolTip = t.Muted ? "Unmute this tab" : "Mute this tab";
            if (S.VerticalTabs && S.VTabsCollapsed) t.Header.ToolTip = t.Title + (t.Audible ? " (playing)" : "") + (t.Muted ? " (muted)" : "");
        }

        public void Activate(Tab t)
        {
            if (t == null || !Tabs.Contains(t)) return;
            var old = Active; Active = t; t.LastActive = DateTime.Now;
            if (t.Group != null && t.Group.Collapsed) { t.Group.Collapsed = false; RenderStrip(); }
            bool split = SplitShown;
            foreach (var x in Tabs)
            {
                bool on = x == t, shown = on || (split && (x == SplitL || x == SplitR));
                if (on) { x.Header.SetResourceReference(Border.BackgroundProperty, "TabOn"); x.TitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "Text"); }
                else if (shown) { x.Header.SetResourceReference(Border.BackgroundProperty, "TabHover"); x.TitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "Text"); }
                else { x.Header.Background = Brushes.Transparent; x.TitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
                if (x.View != null && !x.PoppedOut) x.View.Visibility = shown && !x.ReaderOn && x.CrashView == null ? Visibility.Visible : Visibility.Hidden;
                if (x.CrashView != null) x.CrashView.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;   // 1.5
                if (x.Page != null) x.Page.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                if (x.ReaderView != null) x.ReaderView.Visibility = shown && x.ReaderOn ? Visibility.Visible : Visibility.Collapsed;
            }
            popPlaceholder.Visibility = t.PoppedOut ? Visibility.Visible : Visibility.Collapsed;
            foreach (var x in split ? new[] { SplitL, SplitR } : new[] { t })
            {
                if (x.Discarded && x.Internal && x.Page == null) { ShowSettingsIn(x); x.Page.Visibility = Visibility.Visible; }   // 1.3: a restored settings / bookmarks tab
                if (x.Discarded && !x.Internal && !snap) { x.PendingUrl = x.Url; var _ = CreateView(x, null); }
                if (x.Suspended && x.Core != null) { try { x.Core.Resume(); } catch { } x.Suspended = false; x.SleptByGame = false; }
                if (x != t) SetTabIcon(x);
            }
            LayoutSplit();
            SetTabIcon(t); FitTabs();
            UpdateBar();
            if (findBar.Visibility == Visibility.Visible) FindStart();
            if (old != t) { Changed(); HoverLeave(); SchedulePreview(t, 700); AutoPipCheck(old, t); PageChangedForNotes(); }   // 1.5: hover previews, auto pop-out; 1.6: the notes panel follows
            if (t.Ready && t.ProbedUrl != t.Url && !snap) ProbeReader(t);
        }
        public void CloseTab(Tab t)
        {
            int i = Tabs.IndexOf(t); if (i < 0) return;
            if (t.PoppedOut) Pip.Close(this, t, false);
            if (Omni.IsWeb(t.Url) || t.Internal) { closed.Add(new SessionTab { Url = t.Url, Title = t.Title }); if (closed.Count > 25) closed.RemoveAt(0); }
            Vibe.Play("close", this);   // 1.6 (when sounds are on)
            if (t == SplitL || t == SplitR) SplitL = SplitR = null;
            Tabs.RemoveAt(i); TabPanel.Children.Remove(t.Header);
            if (t.View != null) { content.Children.Remove(t.View); try { t.View.Dispose(); } catch { } t.View = null; }
            if (t.Page != null) { content.Children.Remove(t.Page); t.Page = null; }
            if (t.ReaderView != null) { content.Children.Remove(t.ReaderView); t.ReaderView = null; }
            DropCrashView(t); if (PendingLogin != null && PendingLogin.Tab == t) ClearLogin(); if (t.MediaSeen || t.Audible) Dispatcher.BeginInvoke(new Action(UpdateMediaButtons));   // 1.5
            if (Tabs.Count == 0) { if (BrowserSettings.TestDir != null && BrowserSettings.Offscreen) { NewTab(Omni.NewTabUrl, true); } else { Close(); return; } }
            else if (Active == t) Activate(Tabs[Math.Min(i, Tabs.Count - 1)]);
            RenderStrip(); LayoutSplit(); Changed();
        }
        public void ReopenClosed()
        {
            if (closed.Count == 0) { Toast("No closed tabs to bring back.", 3); return; }
            var c = closed[closed.Count - 1]; closed.RemoveAt(closed.Count - 1);
            NewTab(c.Url, true);
        }
        public int ClosedCount { get { return closed.Count; } }
        public void Duplicate(Tab t) { if (t != null) NewTab(t.Url, true, Tabs.IndexOf(t) + 1); }
        public void CloseOthers(Tab t) { foreach (var x in Tabs.Where(x => x != t).ToList()) CloseTab(x); }
        public void ToggleMute(Tab t) { if (t == null || t.Core == null) return; t.Core.IsMuted = !t.Core.IsMuted; }

        // ================================================================ the WebView's events
        void Hook(Tab t, CoreWebView2 c, bool isPrewarm = false)
        {
            var st = c.Settings;
            st.AreDefaultContextMenusEnabled = true; st.IsStatusBarEnabled = true; st.AreDevToolsEnabled = true; st.IsZoomControlEnabled = true;
            st.AreBrowserAcceleratorKeysEnabled = true; st.IsBuiltInErrorPageEnabled = true;
            try { st.IsPasswordAutosaveEnabled = false; st.IsGeneralAutofillEnabled = false; } catch { }
            try { c.SetVirtualHostNameToFolderMapping(Omni.NewTabHost, Engine.NewTabDir, CoreWebView2HostResourceAccessKind.Deny); } catch { }
            if (Private) { if (!privApplied) { privApplied = true; Engine.ApplyProfile(c.Profile, S); } }
            else if (!SB.ProfileDone) { SB.ProfileDone = true; Engine.ApplyProfile(c.Profile, S); var _ = LoadShield(c.Profile); }
            if (S.DoNotTrack)
            {
                try { c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document); } catch { }
                var __ = c.AddScriptToExecuteOnDocumentCreatedAsync("try{Object.defineProperty(Navigator.prototype,'globalPrivacyControl',{get:function(){return true},configurable:true});}catch(e){}");
            }
            if (Fallback != null) try { c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All); } catch { }
            c.WebResourceRequested += OnRequest;
            if (capApplied > 0) { capped.Add(c); var ___ = Throttle.Apply(c, capApplied); }
            Hook14(c);   // 1.4: site permissions, HTTPS-only, per-site scripting / sound / pop-ups
            Hook15(c);   // 1.5: login capture for Hallow Keys, media controls, gestures
            Hook16(c);   // 1.6: spell check off / Add to dictionary, Open link in profile
            // 1.2: in split view, clicking into a side makes it the active tab
            var ____ = c.AddScriptToExecuteOnDocumentCreatedAsync("if(window.top===window)window.addEventListener('pointerdown',function(){try{window.chrome.webview.postMessage('hallow:focus')}catch(e){}},true);");
            if (isPrewarm) return;
            Rehook(t, c);
        }
        // the per-tab events (again when the pre-warmed WebView becomes a real tab)
        readonly Dictionary<CoreWebView2, Tab> owner = new Dictionary<CoreWebView2, Tab>();
        readonly HashSet<CoreWebView2> hooked = new HashSet<CoreWebView2>();
        void Rehook(Tab t, CoreWebView2 c)
        {
            owner[c] = t;
            if (!hooked.Add(c)) { SyncFromCore(t, c); return; }
            Func<Tab> T = () => { Tab x; return owner.TryGetValue(c, out x) ? x : null; };
            c.SourceChanged += (a, b) => { var x = T(); if (x == null || x.HttpsBlocked != null) return; if (c.Source == "about:blank" && !x.Ready && x.Url != null && x.Url != "about:blank") return; x.Url = c.Source; if (x == Active) UpdateBar(); Changed(); };
            c.DocumentTitleChanged += (a, b) => { var x = T(); if (x == null) return; x.Title = c.DocumentTitle; if (string.IsNullOrWhiteSpace(x.Title)) x.Title = Omni.HostOf(x.Url) ?? "Tab"; if (Omni.IsNewTab(x.Url)) x.Title = "New tab"; x.TitleBlock.Text = x.Title; if (!S.TabPreviews) x.Header.ToolTip = x.Title; if (x == Active) UpdateTitle(); if (!Private && History != null && Omni.IsWeb(x.Url)) History.SetTitle(x.Url, x.Title); Changed(); };
            c.NavigationStarting += (a, b) => { var x = T(); if (x == null) return; x.MediaSeen = x.Audible; ApplyDark(x, c, b.Uri); x.Loading = true; x.KeysItems = null; x.KeysLocked = false; if (!Omni.IsNewTab(b.Uri)) x.Favicon = null; x.Readable = false; x.ProbedUrl = null; if (x.ReaderOn) ReaderOff(x); SetTabIcon(x); if (x == Active) UpdateBar(); };
            c.WebMessageReceived += (a, b) => { if (T() == null) return; string m = null; try { m = b.TryGetWebMessageAsString(); } catch { } if (m == "hallow:focus") ViewFocused(T()); else if (m != null && m.StartsWith("hallow:nt-")) NewTabMessage(m, b.Source); else if (m != null && m.StartsWith("hallow:https-")) HttpsMessage(T(), m); else if (m != null && m.StartsWith("hallow:login\t")) OnLoginMessage(T(), c, b.Source, m.Substring(13)); else if (m != null && m.StartsWith("hallow:gesture\t")) OnGesture(T(), m.Substring(15)); else if (m == "hallow:media-play") { var x = T(); if (x != null && !x.MediaSeen) { x.MediaSeen = true; UpdateMediaButtons(); } } };
            c.NavigationCompleted += (a, b) =>
            {
                var x = T(); if (x == null) return; x.Loading = false; x.Ready = true; SetTabIcon(x);
                if (x.OpenTimer != null) { lock (Engine.Perf) Engine.Perf["newtab-last"] = x.OpenTimer.Elapsed.TotalMilliseconds; x.OpenTimer = null; }
                if (Tabs.Count > 0 && x == Tabs[0]) Engine.Mark("first-page-loaded");
                Engine.Mark("first-navigation-completed");
                if (x == Active) UpdateBar();
                if (b.IsSuccess && Omni.IsWeb(x.Url)) { var _ = CheckKeys(x, c, 0); RecordHistory(x); if (x == Active) ProbeReader(x); if (IsShown(x)) SchedulePreview(x, 900); EnsurePageScripts(c); }
                if (x == Active) PageChangedForNotes();   // 1.6
            };
            c.HistoryChanged += (a, b) => { var x = T(); if (x == Active) UpdateBar(); };
            c.FaviconChanged += async (a, b) =>
            {
                var x = T(); if (x == null) return;
                if (string.IsNullOrEmpty(c.FaviconUri)) { x.Favicon = null; SetTabIcon(x); return; }
                try
                {
                    using (var s = await c.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                    {
                        if (s == null || s.Length == 0) return;
                        var ms = new MemoryStream(); s.CopyTo(ms); ms.Position = 0;
                        var img = new System.Windows.Media.Imaging.BitmapImage(); img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.StreamSource = ms; img.DecodePixelWidth = 32; img.EndInit(); img.Freeze();
                        x.Favicon = img; SetTabIcon(x);
                    }
                }
                catch { }
            };
            c.NewWindowRequested += async (a, b) =>
            {
                var x = T(); if (x == null) return;   // 1.4: a tab moved to another window - that window answers
                if (!PopupAllowed(x, b)) { b.Handled = true; return; }
                var d = b.GetDeferral();
                try { if (AppMode) await AppPopup(b); else await AdoptNewWindow(b, x); }   // 1.6: pop-ups from an app window open in the browser
                finally { d.Complete(); }
            };
            c.WindowCloseRequested += (a, b) => { var x = T(); if (x != null) CloseTab(x); };
            c.DownloadStarting += OnDownload;
            c.ContainsFullScreenElementChanged += (a, b) => { var x = T(); if (x == Active && !x.PoppedOut) SetFullscreen(c.ContainsFullScreenElement); };
            c.IsDocumentPlayingAudioChanged += (a, b) => { var x = T(); if (x == null) return; x.Audible = c.IsDocumentPlayingAudio; if (x.Audible) x.MediaSeen = true; SetTabIcon(x); UpdateMediaButtons(); };
            c.IsMutedChanged += (a, b) => { var x = T(); if (x == null) return; x.Muted = c.IsMuted; SetTabIcon(x); };
            c.ProcessFailed += (a, b) =>
            {
                var x = T(); if (x == null) return;
                if (b.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) { Toast("The page engine stopped. Restart Hallow Browser to go on.", 10); return; }
                // 1.5: a friendly "This tab crashed - Reload" page instead of reloading by itself (or a dead page)
                if (b.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited) ShowCrashed(x, "crashed");
                else if (b.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) ShowCrashed(x, "hung");
            };
            var view = t.View;
            if (view != null) view.ZoomFactorChanged += (a, b) => { var x = T(); if (x == Active) UpdateZoom(); };
            c.Find.MatchCountChanged += (a, b) => { if (T() == Active) FindShow(); };
            c.Find.ActiveMatchIndexChanged += (a, b) => { if (T() == Active) FindShow(); };
            SyncFromCore(t, c);
        }
        // links that open a new tab or window open a new tab next to this one (keeping window.opener, so sign-in pop-ups work) -
        // also from the sidebar's panels (then at the end of the strip)
        public async Task AdoptNewWindow(CoreWebView2NewWindowRequestedEventArgs b, Tab x)
        {
            try
            {
                var nt = new Tab { Url = b.Uri, Title = Omni.HostOf(b.Uri) ?? "Tab" };
                int at = x != null ? Tabs.IndexOf(x) + 1 : Tabs.Count;
                AddHeader(nt, at); Tabs.Insert(at, nt);
                if (x != null && x.Group != null) nt.Group = x.Group;   // 1.2: a link from a grouped tab opens in its group
                RenderStrip();
                await CreateView(nt, b);
                if (nt.Core != null) { b.NewWindow = nt.Core; b.Handled = true; }
                if (!(b.WindowFeatures != null && b.WindowFeatures.HasSize == false && b.IsUserInitiated && (Keyboard.Modifiers & ModifierKeys.Control) != 0)) Activate(nt);
                Changed();
            }
            catch { b.Handled = false; }
        }
        // a sidebar panel's WebView: the same privacy settings and Shield fallback as a tab (no Hallow Keys in panels)
        public void HookPanel(CoreWebView2 c)
        {
            var st = c.Settings;
            st.AreDefaultContextMenusEnabled = true; st.IsStatusBarEnabled = false; st.AreDevToolsEnabled = true; st.IsZoomControlEnabled = true;
            st.AreBrowserAcceleratorKeysEnabled = true; st.IsBuiltInErrorPageEnabled = true;
            try { st.IsPasswordAutosaveEnabled = false; st.IsGeneralAutofillEnabled = false; } catch { }
            if (!SB.ProfileDone) { SB.ProfileDone = true; Engine.ApplyProfile(c.Profile, S); var _ = LoadShield(c.Profile); }
            if (S.DoNotTrack)
            {
                try { c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document); } catch { }
                var __ = c.AddScriptToExecuteOnDocumentCreatedAsync("try{Object.defineProperty(Navigator.prototype,'globalPrivacyControl',{get:function(){return true},configurable:true});}catch(e){}");
            }
            if (Fallback != null) try { c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All); } catch { }
            c.WebResourceRequested += OnRequest;
            c.DownloadStarting += OnDownload;
            Hook14(c);
            if (capApplied > 0) { capped.Add(c); var ___ = Throttle.Apply(c, capApplied); }
        }
        void SyncFromCore(Tab t, CoreWebView2 c)
        {
            try { var src = c.Source; if (!(src == "about:blank" && !t.Ready && t.Url != null)) t.Url = src; t.Audible = c.IsDocumentPlayingAudio; t.Muted = c.IsMuted; } catch { }   // 1.2: a view that is still starting keeps the address it is going to
            if (Omni.IsNewTab(t.Url)) t.Title = "New tab"; t.TitleBlock.Text = t.Title; SetTabIcon(t);
            if (t == Active) UpdateBar();
            if (t.View != null) { t.View.ZoomFactorChanged -= ZoomChanged; t.View.ZoomFactorChanged += ZoomChanged; }
        }
        void ZoomChanged(object s, EventArgs e) { if (Active != null && Active.View == s) UpdateZoom(); }

        // Do Not Track / Global Privacy Control on page requests, and Shield's network fallback
        void OnRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                if (e.ResourceContext == CoreWebView2WebResourceContext.Document && S.DoNotTrack) { e.Request.Headers.SetHeader("Sec-GPC", "1"); e.Request.Headers.SetHeader("DNT", "1"); }
                if (Fallback != null && e.ResourceContext != CoreWebView2WebResourceContext.Document && Fallback.Blocks(e.Request.Uri))
                {
                    e.Response = Engine.Env.CreateWebResourceResponse(null, 403, "Blocked by Hallow Shield", "");
                    FallbackBlocked++;
                }
            }
            catch { }
        }

        // ================================================================ Hallow Keys: show the key when Keys has logins for this page
        async Task CheckKeys(Tab t, CoreWebView2 c, int attempt)
        {
            if (!S.KeysOn || !KeysLink.Running()) return;
            var url = t.Url;
            string has; try { has = await Panels.Eval(c, KeysLink.HasPasswordScript); } catch { return; }
            if (has != "true")
            {
                if (attempt == 0) { await Task.Delay(1500); if (t.Url == url && t.Core == c) await CheckKeys(t, c, 1); }
                return;
            }
            var items = await Task.Run(() => KeysLink.Match(url));
            if (t.Url != url || t.Core != c) return;
            if (items.Count == 0) { var state = await Task.Run(() => KeysLink.State()); t.KeysLocked = state == "locked"; }
            t.KeysItems = items;
            if (t == Active) UpdateBar();
        }

        // ================================================================ toolbar state
        void UpdateBar()
        {
            var t = Active; if (t == null) return;
            var c = t.Core;
            back.IsEnabled = c != null && c.CanGoBack; fwd.IsEnabled = c != null && c.CanGoForward;
            reload.Content = Ui.Icon(t.Loading ? "xmark" : "arrow-rotate-right", 14); reload.ToolTip = t.Loading ? "Stop (Esc)" : "Reload (F5)";
            if (!Address.IsKeyboardFocusWithin) Address.Text = Omni.Display(t.Url);
            Uri u; var secure = Uri.TryCreate(t.Url ?? "", UriKind.Absolute, out u) && u.Scheme == "https" && !Omni.IsNewTab(t.Url);
            lockIcon.Data = Icons.Get(t.Internal ? "gear" : secure ? "lock" : Omni.IsWeb(t.Url) ? "lock-open" : "magnifying-glass");
            lockIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, secure ? "Green" : Omni.IsWeb(t.Url) && u != null && u.Scheme == "http" ? "Amber" : "Muted");
            var keyShow = S.KeysOn && ((t.KeysItems != null && t.KeysItems.Count > 0) || t.KeysLocked);
            keyBtn.Visibility = keyShow ? Visibility.Visible : Visibility.Collapsed;
            keyBtn.Opacity = t.KeysItems != null && t.KeysItems.Count > 0 ? 1 : 0.5;
            keyBtn.ToolTip = t.KeysLocked ? "Hallow Keys is locked" : "Fill a login from Hallow Keys (" + (t.KeysItems == null ? 0 : t.KeysItems.Count) + ")";
            ((System.Windows.Shapes.Path)keyBtn.Content).SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
            UpdateBarPower(t); UpdateZoom(); UpdateTitle();
        }
        void UpdateTitle()
        {
            if (AppMode) { Title = App.Name; return; }   // 1.6: an installed site's window is named after the site
            var app = (Private ? "Private - Hallow Browser" : "Hallow Browser") + TitleSuffix;   // 1.6: and the profile, when there are several
            Title = Active == null || Omni.IsNewTab(Active.Url) ? app : Active.Title + " - " + app;
        }
        void UpdateZoom()
        {
            var z = Active != null && Active.View != null ? Active.View.ZoomFactor : 1.0;
            zoomChip.Visibility = Math.Abs(z - 1.0) > 0.01 ? Visibility.Visible : Visibility.Collapsed;
            zoomChip.Content = new TextBlock { Text = Math.Round(z * 100) + "%", FontSize = 11 };
        }
        public void SetZoom(double z) { if (Active != null && Active.View != null) { Active.View.ZoomFactor = Math.Max(0.25, Math.Min(5, z)); UpdateZoom(); } }
        public void Zoom(int dir)
        {
            if (Active == null || Active.View == null) return;
            double[] steps = { 0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5 };
            var z = Active.View.ZoomFactor;
            var next = dir > 0 ? steps.FirstOrDefault(s => s > z + 0.001) : steps.LastOrDefault(s => s < z - 0.001);
            if (next > 0) SetZoom(next);
        }
        public double CurrentZoom { get { return Active != null && Active.View != null ? Active.View.ZoomFactor : 1.0; } }

        void GoBack() { var c = Active == null ? null : Active.Core; if (c != null && c.CanGoBack) c.GoBack(); }
        void GoForward() { var c = Active == null ? null : Active.Core; if (c != null && c.CanGoForward) c.GoForward(); }
        void ReloadOrStop() { var t = Active; if (t == null || t.Core == null) return; if (t.Loading) t.Core.Stop(); else t.Core.Reload(); }
        public void Navigate(string typed, bool newTab)
        {
            bool done; var special = Special(typed, out done); if (done) return;   // 1.5: "% name" switches tabs, "yt cats" searches YouTube
            var url = special ?? Omni.Interpret(typed, S.Search); if (url == null) return;
            if (!Private && Omni.IsWeb(url) && !url.StartsWith(Omni.SearchUrl(S.Search, ""), StringComparison.OrdinalIgnoreCase)) S.Remember(url);   // 1.4: not from a private window
            if (Omni.IsInternal(url)) { NewTab(url, true); return; }
            if (newTab || Active == null || Active.Internal) NewTab(url, true);
            else Go(Active, url);
            suggest.IsOpen = false;
            if (Active != null && Active.View != null && !BrowserSettings.Offscreen) Active.View.Focus();
        }

        // ================================================================ address bar
        // 1.5: AddressKey, Suggest and MoveSuggest are in AddressBar15.cs (tab search, calculator, keywords, engine suggestions)

        // ================================================================ find in page
        public void ShowFind() { findBar.Visibility = Visibility.Visible; findBox.Focus(); findBox.SelectAll(); if (findBox.Text.Length > 0) FindStart(); }
        public string FindFor(string text) { findBar.Visibility = Visibility.Visible; findBox.Text = text; FindStart(); return findCount.Text; }
        public string FindCount { get { return findCount.Text; } }
        void HideFind() { findBar.Visibility = Visibility.Collapsed; try { if (Active != null && Active.Core != null) Active.Core.Find.Stop(); } catch { } findCount.Text = ""; if (Active != null && Active.View != null) Active.View.Focus(); }
        async void FindStart()
        {
            var c = Active == null ? null : Active.Core; if (c == null) return;
            var text = findBox.Text;
            try
            {
                if (text.Length == 0) { c.Find.Stop(); findCount.Text = ""; return; }
                var o = Engine.Env.CreateFindOptions(); o.FindTerm = text; o.IsCaseSensitive = false; o.SuppressDefaultFindDialog = true; o.ShouldHighlightAllMatches = true;
                await c.Find.StartAsync(o); FindShow();
            }
            catch { findCount.Text = "-"; }
        }
        void FindNext(bool forward) { var c = Active == null ? null : Active.Core; if (c == null) return; try { if (forward) c.Find.FindNext(); else c.Find.FindPrevious(); } catch { } }
        void FindShow()
        {
            var c = Active == null ? null : Active.Core; if (c == null) return;
            try { var n = c.Find.MatchCount; findCount.Text = findBox.Text.Length == 0 ? "" : n == 0 ? "0/0" : Math.Max(1, c.Find.ActiveMatchIndex) + "/" + n; } catch { }
        }

        // ================================================================ fullscreen (videos, F11)
        public void SetFullscreen(bool on)
        {
            if (on == fullscreen) return;
            fullscreen = on;
            var wc = WindowChrome.GetWindowChrome(this);
            if (on)
            {
                preFullState = WindowState; preFullMargin = root.Margin;
                root.RowDefinitions[0].Height = new GridLength(0); root.RowDefinitions[1].Height = new GridLength(0); root.RowDefinitions[2].Height = new GridLength(0);
                root.Children.OfType<FrameworkElement>().Where(x => Grid.GetRow(x) < 3).ToList().ForEach(x => x.Visibility = Visibility.Collapsed);
                wc.CaptionHeight = 0; root.Margin = new Thickness(0); Side.FullScreen(true); ApplyTabLayout();
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
            }
            else
            {
                root.RowDefinitions[0].Height = new GridLength(38); root.RowDefinitions[1].Height = GridLength.Auto; root.RowDefinitions[2].Height = GridLength.Auto;
                root.Children.OfType<FrameworkElement>().Where(x => Grid.GetRow(x) < 3 && x != findBar).ToList().ForEach(x => x.Visibility = Visibility.Visible);
                wc.CaptionHeight = 38; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
                WindowState = preFullState; FitMaximized(); Side.FullScreen(false); ApplyTabLayout();
            }
        }

        // ================================================================ keyboard (like Chrome)
        void OnKey(object sender, KeyEventArgs e)
        {
            var k = e.Key == Key.System ? e.SystemKey : e.Key; var m = Keyboard.Modifiers;
            bool ctrl = (m & ModifierKeys.Control) != 0, shift = (m & ModifierKeys.Shift) != 0, alt = (m & ModifierKeys.Alt) != 0;
            Action act = null;
            if (ctrl && !alt)
            {
                switch (k)
                {
                    case Key.T: act = shift ? (Action)ReopenClosed : () => NewTab(Omni.NewTabUrl, true); break;
                    case Key.N: act = () => Program.OpenWindow(shift, null, null); break;              // 1.4: Ctrl+N a new window, Ctrl+Shift+N a private one
                    case Key.W: case Key.F4: act = () => { if (Active != null) CloseTab(Active); }; break;
                    case Key.Tab: act = () => Cycle(shift ? -1 : 1); break;
                    case Key.PageDown: act = () => Cycle(1); break;
                    case Key.PageUp: act = () => Cycle(-1); break;
                    case Key.L: act = FocusAddress; break;
                    case Key.E: act = () => { FocusAddress(); }; break;
                    case Key.K: act = () => Palette.Show(this); break;                                   // 1.2: the command palette
                    case Key.D: act = shift ? (Action)(() => SaveForLater(Active)) : () => ToggleBookmark(Active); break;   // 1.2: bookmark; 1.6: Ctrl+Shift+D saves it for later
                    case Key.B: if (shift) act = ToggleBookmarksBar; break;                                // 1.3: the bookmarks bar
                    case Key.O: if (shift) act = () => NewTab(Omni.BookmarksUrl, true); break;             // 1.3: the bookmarks manager
                    case Key.X: if (shift) act = ShowScreenshot; break;                                    // 1.2: screenshot
                    case Key.R: act = () => { if (Active != null && Active.Core != null) { if (shift) Active.Core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}"); else Active.Core.Reload(); } }; break;
                    case Key.F: act = ShowFind; break;
                    case Key.G: act = () => FindNext(!shift); break;
                    case Key.J: act = () => Panels.DownloadsFlyout(this, dlBtn); break;
                    case Key.P: act = () => { if (Active != null && Active.Core != null) Active.Core.ShowPrintUI(); }; break;
                    case Key.D0: case Key.NumPad0: act = () => SetZoom(1.0); break;
                    case Key.OemPlus: case Key.Add: act = () => Zoom(1); break;
                    case Key.OemMinus: case Key.Subtract: act = () => Zoom(-1); break;
                    case Key.OemComma: act = () => NewTab(Omni.SettingsUrl, true); break;
                    case Key.M: if (shift) act = () => ToggleMute(Active); break;
                    case Key.S: if (shift) act = () => Side.Toggle(); break;
                    case Key.Delete: if (shift) act = () => Panels.ClearData(this); break;
                    default:
                        if (k >= Key.D1 && k <= Key.D8) { int n = k - Key.D1; act = () => { if (n < Tabs.Count) Activate(Tabs[n]); }; }
                        else if (k == Key.D9) act = () => { if (Tabs.Count > 0) Activate(Tabs[Tabs.Count - 1]); };
                        break;
                }
            }
            else if (alt && !ctrl)
            {
                if (k == Key.Left) act = GoBack; else if (k == Key.Right) act = GoForward; else if (k == Key.D) act = FocusAddress; else if (k == Key.Home) act = () => Go(Active, Omni.NewTabUrl);
            }
            else if (!ctrl && !alt)
            {
                if (k == Key.F5) act = () => { if (Active != null && Active.Core != null) Active.Core.Reload(); };
                else if (k == Key.F6) act = FocusAddress;
                else if (k == Key.F3) act = () => { if (findBar.Visibility != Visibility.Visible) ShowFind(); else FindNext(!shift); };
                else if (k == Key.F11) act = () => SetFullscreen(!fullscreen);
                else if (k == Key.F9) act = () => ToggleReader(Active);                                    // 1.2: reader mode
                else if (k == Key.F12) act = () => { if (Active != null && Active.Core != null) Active.Core.OpenDevToolsWindow(); };
                else if (k == Key.Escape && shift) act = () => HotTabs.Flyout(this, menuBtn);
                else if (k == Key.Escape && fullscreen && !(Active != null && Active.Core != null && Active.Core.ContainsFullScreenElement)) act = () => SetFullscreen(false);
                else if (k == Key.Escape && Active != null && Active.Loading && !Address.IsKeyboardFocusWithin) act = () => Active.Core.Stop();
            }
            if (act != null) { e.Handled = true; act(); }
        }
        void Cycle(int d) { if (Tabs.Count < 2 || Active == null) return; int i = Tabs.IndexOf(Active); Activate(Tabs[(i + d + Tabs.Count) % Tabs.Count]); }
        void FocusAddress() { Address.Focus(); Address.SelectAll(); }

        // ================================================================ downloads
        // 1.5: OnDownload is in Downloads15.cs (pause / resume / cancel, the limit while gaming, Defender)
        public void SeenDownloads() { dlBadge.Visibility = Visibility.Collapsed; }

        // ================================================================ Shield
        async Task LoadShield(CoreWebView2Profile profile)
        {
            Version v; var folder = ShieldHost.Pick(out v);
            ShieldFolder = folder; ShieldVersion = v == null ? null : v.ToString(2);
            if (folder == null) { ShieldState = "missing"; ShieldNote = "Hallow Shield's files are missing - reinstall Hallow Browser."; WriteStatus(); return; }
            if (BrowserSettings.ForceFallback) { await StartFallback(folder, "test: the network-only fallback was asked for"); return; }
            string failed = null;
            try
            {
                // 1.4: Shield's own copy with the "blocked on this page" permission granted (see ShieldBridge14.cs)
                var load = await Task.Run(() => ShieldHost.Patched(folder, v));
                var want = ShieldVersion + "|" + load;
                var exts = await profile.GetBrowserExtensionsAsync();
                var mine = exts.Where(x => x.Name == "Hallow Shield").ToList();
                CoreWebView2BrowserExtension ext;
                if (S.ShieldLoaded == want && mine.Count == 1) { ext = mine[0]; if (!ext.IsEnabled) await ext.EnableAsync(true); }
                else
                {
                    // added over the installed one (same id): an update in place, so Shield keeps its settings, filters and stats
                    // (1.3 removed it first, which lost them); only if that fails is the old one removed
                    ext = null;
                    if (mine.Count <= 1) try { ext = await profile.AddBrowserExtensionAsync(load); } catch { ext = null; }
                    if (ext == null) { foreach (var x in mine) try { await x.RemoveAsync(); } catch { } ext = await profile.AddBrowserExtensionAsync(load); }
                    S.ShieldLoaded = want; S.Save();
                }
                ShieldCounts = !string.Equals(load, folder, StringComparison.OrdinalIgnoreCase);
                ShieldId = ext.Id; ShieldState = "extension"; ShieldNote = null;
                var _x = LoadUserExtensions(profile);   // 1.4: your own extensions
                StartShieldRelay();
                ShieldHost.Tidy(folder.StartsWith(ShieldHost.UpdatesDir, StringComparison.OrdinalIgnoreCase) ? folder : null);
            }
            catch (Exception e) { failed = e.Message; }
            if (failed != null) await StartFallback(folder, failed);
            WriteStatus();
        }
        async Task StartFallback(string folder, string why)
        {
            var rules = await Task.Run(() => ShieldHost.LoadRules(folder));
            Fallback = rules; ShieldState = "fallback";
            ShieldNote = "Shield runs in its network-only fallback (" + rules.Count + " block rules; no cosmetic filtering or per-site settings): " + why;
            foreach (var w in Program.Windows.Where(x => x.SB == SB).Concat(new[] { this }).Distinct().ToList()) w.FallbackFilters();   // 1.4: every window on this profile
            WriteStatus();
        }
        public void FallbackFilters()
        {
            foreach (var t in Tabs) { var c = t.Core; if (c != null) try { c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All); } catch { } }
            if (prewarm != null && prewarm.CoreWebView2 != null) try { prewarm.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All); } catch { }
        }
        public string ShieldPage(string page) { return ShieldId == null ? null : "chrome-extension://" + ShieldId + "/" + page; }

        // ================================================================ every 5 s: game mode, sleeping tabs, RAM, priority
        readonly HashSet<int> sleptByGame = new HashSet<int>();
        DateTime lastRam = DateTime.MinValue; ProcessPriorityClass? appliedPriority; public long LastMemMb;
        public void Tick(bool force)
        {
            var g = S.GameLight ? Games.Game() : null;
            bool changed = g != Game;
            Game = g;
            gameChip.Visibility = Game != null ? Visibility.Visible : Visibility.Collapsed;
            gameText.Text = Game != null ? "GAME MODE" : "";
            if (Game != null) gameChip.ToolTip = "Game mode (" + Game + "): background tabs sleep and the browser runs at a lower priority. Tabs playing sound stay awake.";
            var now = DateTime.Now;
            var states = Tabs.Select(t => t.State(t == Active)).ToList();
            var sleepMin = SleepMinutesNow();   // 1.6: the memory saver level (and sooner on battery)
            foreach (var id in Planner.ToSuspend(states, Game != null, now, sleepMin)) { var t = Tabs.First(x => x.Id == id); var _ = Suspend(t, Game != null); }
            if (changed && Game != null) Vibe.Play("game", this);   // 1.6: the game-mode sound (when sounds are on)
            if (changed) Vibe.GameChanged(this);
            if (changed && Game == null)
                foreach (var id in Planner.ToWakeAfterGame(states, now, sleepMin, sleptByGame)) { var t = Tabs.First(x => x.Id == id); if (t.Core != null) try { t.Core.Resume(); t.Suspended = false; SetTabIcon(t); } catch { } }
            if (Game == null) sleptByGame.Clear();
            // RAM limiter (every 15 s)
            if (S.RamLimitMb > 0 && (force || now - lastRam > TimeSpan.FromSeconds(15)))
            {
                lastRam = now; var used = MemoryMb(); LastMemMb = used;
                List<int> sus, dis; Planner.OverCap(Tabs.Select(t => t.State(t == Active)).ToList(), used, S.RamLimitMb, out sus, out dis);
                foreach (var id in sus) { var t = Tabs.First(x => x.Id == id); var _ = Suspend(t, false); }
                foreach (var id in dis) { var t = Tabs.First(x => x.Id == id); Discard(t); }
            }
            ApplyPriority(Planner.Priority(Game != null, S.CpuLimiter));
            // 1.1: hidden sidebar panels sleep (at once in game mode, never while playing sound); the per-tab bandwidth cap
            Side.Tick(Game != null);
            ApplyCap(Throttle.Want(S.BandwidthKBps, S.BandwidthGameOnly, Game != null));
            BatteryTick();   // 1.6: on battery, background tabs and panels are slowed down
            if (changed || force) WriteStatus();
        }
        double capApplied = -1; readonly HashSet<CoreWebView2> capped = new HashSet<CoreWebView2>();
        public double CapApplied { get { return capApplied; } }
        void ApplyCap(double want)
        {
            var cores = Tabs.Select(t => t.Core).Concat(Side.Panels.Select(p => p.Core)).Where(c => c != null).ToList();
            if (prewarm != null && prewarm.CoreWebView2 != null) cores.Add(prewarm.CoreWebView2);
            if (want == capApplied)
            {
                // new pages since the last change (Hook / HookPanel cap them too; this catches any that were made in between)
                if (want > 0) foreach (var c in cores.Where(c => !capped.Contains(c))) { capped.Add(c); var _ = Throttle.Apply(c, want); }
                return;
            }
            capApplied = want; capped.Clear();
            foreach (var c in cores) { if (want > 0) capped.Add(c); var _ = Throttle.Apply(c, want); }
        }
        public void PanelsChanged() { Changed(); }
        public void Bring() { if (BrowserSettings.Offscreen) return; if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Show(); Activate(); }
        async Task Suspend(Tab t, bool byGame)
        {
            var c = t.Core; if (c == null || t.Suspended) return;
            if (Downloads.Any(d => d.Source == c && (d.State == "running" || d.State == "paused"))) return;   // 1.5: measured - a sleeping tab stalls the downloads it started
            long before = await TabMemoryMb(t);   // 1.6: "memory saved" on the sleeping tab
            try { if (await c.TrySuspendAsync()) { t.Suspended = true; if (byGame) sleptByGame.Add(t.Id); SetTabIcon(t); var _ = MeasureSaved(t, before); } } catch { }
        }
        public void Discard(Tab t)
        {
            if (t == Active || t.View == null || t.Audible || t.PoppedOut) return;
            var dc = t.Core; if (dc != null && Downloads.Any(d => d.Source == dc && (d.State == "running" || d.State == "paused"))) return;   // 1.5: never while its download runs
            var mb = t.LastMemMb; content.Children.Remove(t.View); try { t.View.Dispose(); } catch { } t.View = null; t.Discarded = true; t.Suspended = false; if (mb > 0) t.SavedMb = mb; SetTabIcon(t);   // 1.6: an unloaded tab frees all of it
        }
        public long MemoryMb()
        {
            long sum = 0;
            try { foreach (var p in Engine.Env.GetProcessInfos()) try { using (var pr = Process.GetProcessById(p.ProcessId)) sum += pr.PrivateMemorySize64; } catch { } } catch { }
            return sum / (1024 * 1024);
        }
        void ApplyPriority(ProcessPriorityClass want)
        {
            if (appliedPriority == want) return;
            appliedPriority = want;
            try { Process.GetCurrentProcess().PriorityClass = want; } catch { }
            try { foreach (var p in Engine.Env.GetProcessInfos()) try { using (var pr = Process.GetProcessById(p.ProcessId)) pr.PriorityClass = want; } catch { } } catch { }
            // background tabs use less memory while a game runs or the CPU limiter is on
            foreach (var t in Tabs) { var c = t.Core; if (c != null) try { c.MemoryUsageTargetLevel = want == ProcessPriorityClass.Normal ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low; } catch { } }
        }
        public string PriorityName { get { return appliedPriority.HasValue ? appliedPriority.Value.ToString() : "Normal"; } }

        // ================================================================ session, status, toast
        void Changed() { saveTimer.Stop(); saveTimer.Start(); statusTimer.Stop(); statusTimer.Start(); }
        public void SaveSession()
        {
            if (snap || Private || Tabs.Count == 0) return;
            // 1.2: with groups and the split; the current workspace keeps its copy in workspaces.json too
            // 1.4: every normal window (session.json: the first window's tabs as before, the others in "Windows")
            Program.SaveAll();
        }
        public void WriteStatus()
        {
            if (snap) return;
            Family.WriteStatus(Program.StatusJson(Program.Win ?? this));
        }
        DispatcherTimer toastTimer;
        public void Toast(string text, int seconds)
        {
            toastText.Text = text; toast.IsOpen = false; toast.IsOpen = true;
            if (toastTimer == null) { toastTimer = new DispatcherTimer(); toastTimer.Tick += (a, b) => { toastTimer.Stop(); toast.IsOpen = false; }; }
            toastTimer.Stop(); toastTimer.Interval = TimeSpan.FromSeconds(seconds); toastTimer.Start();
        }
        public string LastToast { get { return toastText.Text; } }

        void BackgroundChecks()
        {
            if (BrowserSettings.TestDir != null && Environment.GetEnvironmentVariable("HALLOW_BROWSER_API") == null) return;
            Task.Run(() =>
            {
                Thread(5000);
                var now = DateTime.Now;
                DateTime last;
                if (!DateTime.TryParse(S.LastUpdateCheck, out last) || now - last > TimeSpan.FromHours(20))
                {
                    var r = Updater.Latest();
                    Dispatcher.BeginInvoke(new Action(() => { S.LastUpdateCheck = now.ToString("s"); S.Save(); if (Updater.IsNewer(r)) { Update = r; UpdateNote = "Hallow Browser " + r.Version.ToString(2) + " is out."; Toast(UpdateNote + " Open the menu to update.", 8); } }));
                }
                if (S.ShieldAutoUpdate && (!DateTime.TryParse(S.LastShieldCheck, out last) || now - last > TimeSpan.FromHours(20)))
                {
                    var msg = ShieldHost.CheckForUpdate();
                    Dispatcher.BeginInvoke(new Action(() => { S.LastShieldCheck = now.ToString("s"); S.Save(); if (msg != null) Toast(msg, 8); }));
                }
            });
        }
        static void Thread(int ms) { System.Threading.Thread.Sleep(ms); }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            tick.Stop();
            foreach (var t in Tabs.Where(x => x.PoppedOut).ToList()) Pip.Close(this, t, false);
            if (snap) return;
            // 1.4: a private window keeps nothing; a window that isn't the last one goes away with its tabs (its own "Window n"
            // workspace too) - the browser goes on in the other windows
            if (Private || AppMode) { Gone = true; return; }   // 1.6: an app window keeps nothing either (it opens its site again)
            if (!Program.Quitting && Program.Windows.Any(x => x != this && !x.Private && !x.Gone)) { Gone = true; if (Live != null) Live.Stop(); Program.Forget(this); return; }
            if (Live != null) Live.Stop();
            SaveSession();
            if (History != null) History.Save();
            SyncOnClose();   // 1.3: this PC's last changes to the sync folder (a few seconds at most)
            if (!fullscreen && Program.First == this) { S.Maximized = WindowState == WindowState.Maximized; if (WindowState == WindowState.Normal && !BrowserSettings.Offscreen) { S.Left = Left; S.Top = Top; S.Width = Width; S.Height = Height; } }
            S.Save();
            if (S.ClearOnExit && !clearedOnExit && Active != null && Active.Core != null && Program.Windows.Count(x => !x.Gone && x != this) == 0)
            {
                // clear before the engine goes away: wait for it (a few seconds at most)
                e.Cancel = true; var core = Active.Core;
                if (clearing) return; clearing = true;
                Program.ClearAll(this, core.Profile).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => { clearedOnExit = true; Close(); })));
                return;
            }
        }
        bool clearing, clearedOnExit;
        public new void Close() { if (clearing && !clearedOnExit) return; base.Close(); }
        public void ForceClose() { clearedOnExit = true; base.Close(); }
        public CoreWebView2Profile Profile { get { var t = Tabs.FirstOrDefault(x => x.Core != null); return t == null ? null : t.Core.Profile; } }
        public Grid ContentGrid { get { return content; } }
        public void ShowPlaceholder(bool on) { popPlaceholder.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }
        public void Refresh(Tab t) { SetTabIcon(t); LayoutSplit(); if (t == Active) UpdateBar(); }

        void ShowSettingsIn(Tab t)
        {
            bool bm = t.Url == Omni.BookmarksUrl;   // 1.3: the bookmarks manager is a page of the browser too
            bool ex = t.Url == Omni.ExtensionsUrl, si = t.Url == Omni.SitesUrl;   // 1.4: extensions, site settings
            bool dl = t.Url == Omni.DownloadsUrl;                                  // 1.5: downloads
            bool ap = t.Url == Omni.AppsUrl;                                       // 1.6: installed sites
            t.Url = bm ? Omni.BookmarksUrl : ex ? Omni.ExtensionsUrl : si ? Omni.SitesUrl : dl ? Omni.DownloadsUrl : ap ? Omni.AppsUrl : Omni.SettingsUrl; t.Title = bm ? "Bookmarks" : ex ? "Extensions" : si ? "Site settings" : dl ? "Downloads" : ap ? "Apps" : "Settings"; t.TitleBlock.Text = t.Title; t.Discarded = false;
            t.Page = bm ? BookmarksPage() : ex ? ExtensionsPage() : si ? SitesPage() : dl ? DownloadsPage() : ap ? AppsPage() : Panels.SettingsPage(this); t.Page.Visibility = Visibility.Collapsed;
            content.Children.Add(t.Page); SetTabIcon(t); LayoutSplit();
        }
        public void ReloadSettings() { var t = Tabs.FirstOrDefault(x => x.Url == Omni.SettingsUrl); if (t == null) return; content.Children.Remove(t.Page); t.Page = Panels.SettingsPage(this); t.Page.Visibility = IsShown(t) ? Visibility.Visible : Visibility.Collapsed; content.Children.Add(t.Page); LayoutSplit(); }
        public void ApplyPageColors() { var p = Profile; if (p != null) Engine.ApplyProfile(p, S); Engine.WriteNewTab(S, Program.AccentHex); }
        // 1.1: light / dark changed (theme.json, Windows' setting or the Look choice): title bars, page backgrounds, pages that follow
        public void ThemeChanged()
        {
            Look.TitleBars();
            try
            {
                var c = (Color)FindResource("PageBg"); var dc = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                foreach (var t in Tabs) if (t.View != null) try { t.View.DefaultBackgroundColor = dc; } catch { }
                if (prewarm != null) try { prewarm.DefaultBackgroundColor = dc; } catch { }
                Side.SetBackground(c);
            }
            catch { }
            ApplyPageColors();
            // the pre-warmed new tab was drawn in the old look: reload it
            if (prewarm != null && prewarm.CoreWebView2 != null) try { prewarm.CoreWebView2.Reload(); } catch { }
            foreach (var t in Tabs) if (Omni.IsNewTab(t.Url) && t.Core != null) try { t.Core.Reload(); } catch { }
        }

        // --snap: made-up tabs and a made-up page in the content area (never real pages)
        public void SnapFill(string page, bool gameOn, bool keys)
        {
            Tabs.Clear(); Groups.Clear(); strip.Children.Clear(); vlist.Children.Clear();
            var names = new[] { "Hallow News - patch notes", "Delta Force - season guide", "Lo-fi beats to game to", "New tab" };
            foreach (var n in names) { var t = new Tab { Url = n == "New tab" ? Omni.NewTabUrl : "https://example.com/", Title = n }; AddHeader(t, Tabs.Count); Tabs.Add(t); if (n.StartsWith("Lo-fi")) { t.Audible = true; SetTabIcon(t); } }
            RenderStrip();
            Tabs[2].Suspended = false; Tabs[1].Suspended = gameOn; SetTabIcon(Tabs[1]);
            var act = Tabs[0]; act.Url = "https://news.example.com/patch-notes"; act.KeysItems = keys ? new List<KeysItem> { new KeysItem { Id = "x", Title = "Hallow News", Username = "player@example.com" } } : null;
            Activate(act);
            ShieldState = "extension"; ShieldVersion = "1.7"; ShieldId = "snap"; if (Engine.RuntimeVersion == null) Engine.RuntimeVersion = Engine.FindRuntime();
            if (gameOn) { Game = "Delta Force"; gameChip.Visibility = Visibility.Visible; gameText.Text = "GAME MODE"; }
            var mock = Panels.MockPage(page); mock.Tag = "mock"; content.Children.Add(mock);
            dlBadge.Visibility = Visibility.Visible;
        }
    }
}
