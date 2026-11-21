// Hallow Browser 1.7 - taskbar extras and "cast".
//  - Jump list (right-click the taskbar or Start icon): New tab, New private window, Reading list, and each profile - Windows'
//    ICustomDestinationList through COM interop (no libraries). The items start HallowBrowser.exe with --jump <what> / --profile <id>;
//    a running browser gets them through its pipe. Made once after the window is up (and again when profiles change), at idle.
//    Tests write it to a test AppUserModelID (HALLOW_BROWSER_JUMPLIST_APPID) and delete it after - never the real browser's list.
//  - Tab previews in the taskbar (OFF by default): each tab gets a hidden stand-in window registered with ITaskbarList3::RegisterTab,
//    so hovering the taskbar button shows one preview per tab (DWM iconic thumbnails from the tab's own picture); clicking one
//    switches to that tab, its X closes the tab. Windows draws these; the browser only hands it the pictures.
//  - Cast: WebView2 has Chromium's Presentation API (navigator.presentation) but no Media Router behind it - measured in 1.7:
//    getAvailability() says false and start() fails with "No screens found", and there's no Cast dialog. So instead of a DLNA stack of
//    our own, "Open in Edge to cast" opens the tab's address in Microsoft Edge, whose menu has "Cast media to device".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HallowBrowser
{
    // ================================================================ the jump list
    public class JumpItem { public string Title, Args, Icon; public int IconIndex; public bool Separator; public string Category; }

    public static class JumpList
    {
        // what the list holds (also what the tests check): tasks first, then the profiles
        public static List<JumpItem> Items(string exe, ProfileStore profiles)
        {
            var l = new List<JumpItem> {
                new JumpItem { Title = "New tab", Args = "--jump new-tab", Icon = exe },
                new JumpItem { Title = "New private window", Args = "--jump private-window", Icon = exe },
                new JumpItem { Title = "Reading list", Args = "--jump reading-list", Icon = exe } };
            if (profiles != null && profiles.Items.Count > 0)
                foreach (var p in profiles.Items) l.Add(new JumpItem { Title = p.Name, Args = "--profile " + p.Id, Icon = exe, Category = "Profiles" });
            return l;
        }
        public static string LastError, LastAppId; public static int LastCount; public static bool LastProfilesAsTasks;

        // writes the list; appId null = this app's own (Windows works it out from the exe), tests pass their own
        public static bool Commit(string exe, List<JumpItem> items, string appId)
        {
            LastError = null; LastAppId = appId; LastCount = 0; LastProfilesAsTasks = false;
            ICustomDestinationList list = null;
            try
            {
                list = (ICustomDestinationList)new CDestinationList();
                if (appId != null) list.SetAppID(appId);
                uint slots; object removed; var iid = typeof(IObjectArray).GUID;
                list.BeginList(out slots, ref iid, out removed);
                var profiles = items.Where(i => i.Category == "Profiles").ToList();
                var tasks = items.Where(i => i.Category == null).ToList();
                if (profiles.Count > 0)
                {
                    var pc = Collection(exe, profiles);
                    int hr = list.AppendCategory("Profiles", (IObjectArray)pc);
                    if (hr < 0) { LastProfilesAsTasks = true; tasks.Add(new JumpItem { Separator = true }); tasks.AddRange(profiles); }   // "recent items in jump lists" off: profiles go under the tasks
                }
                list.AddUserTasks((IObjectArray)Collection(exe, tasks));
                list.CommitList();
                LastCount = items.Count;
                return true;
            }
            catch (Exception e) { LastError = e.GetType().Name + ": " + e.Message; try { if (list != null) list.AbortList(); } catch { } return false; }
            finally { if (list != null) Marshal.ReleaseComObject(list); }
        }
        public static bool Delete(string appId)
        {
            ICustomDestinationList list = null;
            try { list = (ICustomDestinationList)new CDestinationList(); list.DeleteList(appId); return true; }
            catch (Exception e) { LastError = e.Message; return false; }
            finally { if (list != null) Marshal.ReleaseComObject(list); }
        }
        static IObjectCollection Collection(string exe, List<JumpItem> items)
        {
            var c = (IObjectCollection)new CEnumerableObjectCollection();
            foreach (var it in items) c.AddObject(Link(exe, it));
            return c;
        }
        static IShellLinkW Link(string exe, JumpItem it)
        {
            var link = (IShellLinkW)new CShellLink();
            var ps = (IPropertyStore)link;
            if (it.Separator)
            {
                var sepKey = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);
                var pv = PropVariant.FromBool(true); try { ps.SetValue(ref sepKey, ref pv); } finally { PropVariant.Clear(ref pv); }
            }
            else
            {
                link.SetPath(exe); link.SetArguments(it.Args ?? ""); link.SetDescription(it.Title);
                link.SetIconLocation(it.Icon ?? exe, it.IconIndex);
                try { link.SetWorkingDirectory(Path.GetDirectoryName(exe)); } catch { }
                var titleKey = new PROPERTYKEY(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
                var pv = PropVariant.FromString(it.Title); try { ps.SetValue(ref titleKey, ref pv); } finally { PropVariant.Clear(ref pv); }
            }
            ps.Commit();
            return link;
        }

        // ---- COM
        [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")] class CDestinationList { }
        [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")] class CEnumerableObjectCollection { }
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
        interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void BeginList(out uint pcMinSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
            void AppendKnownCategory(int category);
            void AddUserTasks(IObjectArray poa);
            void CommitList();
            void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void AbortList();
        }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
        interface IObjectArray { void GetCount(out uint pcObjects); void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv); }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
        interface IObjectCollection
        {
            void GetCount(out uint pcObjects); void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
            void AddObject([MarshalAs(UnmanagedType.Interface)] object punk); void AddFromArray(IObjectArray poaSource); void RemoveObjectAt(uint uiIndex); void Clear();
        }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags); void GetIDList(out IntPtr ppidl); void SetIDList(IntPtr pidl);
            void GetDescription(IntPtr pszName, int cch); void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory(IntPtr pszDir, int cch); void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments(IntPtr pszArgs, int cch); void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey); void SetHotkey(short wHotkey); void GetShowCmd(out int piShowCmd); void SetShowCmd(int iShowCmd);
            void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon); void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved); void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        interface IPropertyStore
        {
            void GetCount(out uint cProps); void GetAt(uint iProp, out PROPERTYKEY pkey); void GetValue(ref PROPERTYKEY key, out PropVariant pv);
            void SetValue(ref PROPERTYKEY key, ref PropVariant pv); void Commit();
        }
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PROPERTYKEY { public Guid fmtid; public uint pid; public PROPERTYKEY(Guid g, uint p) { fmtid = g; pid = p; } }
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PropVariant
        {
            [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; [FieldOffset(8)] public short b;
            public static PropVariant FromString(string s) { return new PropVariant { vt = 31, p = Marshal.StringToCoTaskMemUni(s) }; }   // VT_LPWSTR
            public static PropVariant FromBool(bool v) { return new PropVariant { vt = 11, b = (short)(v ? -1 : 0) }; }                 // VT_BOOL
            [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant pvar);
            public static void Clear(ref PropVariant v) { try { PropVariantClear(ref v); } catch { } }
        }
    }

    // ================================================================ a tab's stand-in window for its taskbar preview
    public class TabThumb : IDisposable
    {
        public HwndSource Src; public Tab Tab; public BrowserWindow Win; public int LastHr = 1; public int Requests; public string LastTitle;
        const int WM_ACTIVATE = 0x0006, WM_CLOSE = 0x0010, WM_SYSCOMMAND = 0x0112, SC_CLOSE = 0xF060, WM_DWMSENDICONICTHUMBNAIL = 0x0323, WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326, WM_SETICON = 0x0080;
        public IntPtr Handle { get { return Src == null ? IntPtr.Zero : Src.Handle; } }
        public TabThumb(BrowserWindow w, Tab t)
        {
            Win = w; Tab = t;
            var p = new HwndSourceParameters("Hallow Browser tab") { WindowStyle = unchecked((int)0x80000000) | 0x00C00000 | 0x00080000, ExtendedWindowStyle = 0, PositionX = -32000, PositionY = -32000, Width = 10, Height = 10, ParentWindow = IntPtr.Zero };
            Src = new HwndSource(p); Src.AddHook(Hook);
            int on = 1;
            DwmSetWindowAttribute(Handle, 7, ref on, 4);    // DWMWA_FORCE_ICONIC_REPRESENTATION
            DwmSetWindowAttribute(Handle, 10, ref on, 4);   // DWMWA_HAS_ICONIC_BITMAP
        }
        public void SetTitle(string title) { if (title != LastTitle) { LastTitle = title; SetWindowText(Handle, title ?? "Tab"); } }
        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_DWMSENDICONICTHUMBNAIL:
                    { int wd = (int)((long)lParam >> 16) & 0xFFFF, ht = (int)(long)lParam & 0xFFFF; Requests++; SendThumb(wd, ht); handled = true; return IntPtr.Zero; }
                case WM_DWMSENDICONICLIVEPREVIEWBITMAP:
                    handled = true; SendLive(); return IntPtr.Zero;
                case WM_ACTIVATE:
                    if (((int)(long)wParam & 0xFFFF) != 0) { handled = true; Win.Dispatcher.BeginInvoke(new Action(() => { Win.Bring(); Win.Activate(Tab); })); return IntPtr.Zero; }
                    break;
                case WM_SYSCOMMAND:
                    if (((int)(long)wParam & 0xFFF0) == SC_CLOSE) { handled = true; Win.Dispatcher.BeginInvoke(new Action(() => Win.CloseTab(Tab))); return IntPtr.Zero; }
                    break;
                case WM_CLOSE:
                    handled = true; Win.Dispatcher.BeginInvoke(new Action(() => Win.CloseTab(Tab))); return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }
        // the tab's picture (the hover preview it already has, else the favicon on the panel colour), fitted into w x h
        public void SendThumb(int w, int h)
        {
            if (w <= 0 || h <= 0) { w = 200; h = 120; }
            var src = Tab.Preview as BitmapSource;
            if (src == null && Tab.Core != null && Win.Showing(Tab)) { var _ = Win.CapturePreviewFor(Tab).ContinueWith(t => Win.Dispatcher.BeginInvoke(new Action(() => { if (Tab.Preview != null) SendThumb(w, h); }))); }
            var bmp = Render(src, w, h);
            LastHr = SetBitmap(bmp, false, w, h);
            if (LastHr == unchecked((int)0x80070057) && h > 90) { int h2 = 90, w2 = Math.Max(1, w * 90 / h); LastHr = SetBitmap(Render(src, w2, h2), false, w2, h2); }   // bigger than DWM takes (measured: about 100 px high): smaller
        }
        void SendLive()
        {
            var r = Win.ContentRectOnWindow(); if (r.Width < 2 || r.Height < 2) return;
            var src = Tab.Preview as BitmapSource;
            var bmp = Render(src, (int)r.Width, (int)r.Height);
            SetBitmap(bmp, true, (int)r.Width, (int)r.Height, (int)r.X, (int)r.Y);
        }
        BitmapSource Render(BitmapSource src, int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = (Application.Current.TryFindResource("Panel") as SolidColorBrush) ?? Brushes.Black;
                dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
                if (src != null) { double sc = Math.Min(w / src.Width, h / src.Height); double dw = src.Width * sc, dh = src.Height * sc; dc.DrawImage(src, new Rect((w - dw) / 2, (h - dh) / 2, dw, dh)); }
                else if (Tab.Favicon != null) { double s = Math.Min(32, Math.Min(w, h) / 2.0); dc.DrawImage(Tab.Favicon, new Rect((w - s) / 2, (h - s) / 2, s, s)); }
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv); return rtb;
        }
        int SetBitmap(BitmapSource b, bool live, int w, int h, int x = 0, int y = 0)
        {
            IntPtr bits; var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var hbm = CreateDIBSection(IntPtr.Zero, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (hbm == IntPtr.Zero) return -1;
            try
            {
                b.CopyPixels(new Int32Rect(0, 0, w, h), bits, w * h * 4, w * 4);
                if (live) { var pt = new POINT { x = x, y = y }; return DwmSetIconicLivePreviewBitmap(Handle, hbm, ref pt, 0); }
                return DwmSetIconicThumbnail(Handle, hbm, 0);
            }
            finally { DeleteObject(hbm); }
        }
        public void Invalidate() { try { DwmInvalidateIconicBitmaps(Handle); } catch { } }
        public void Dispose() { if (Src != null) { try { Src.RemoveHook(Hook); Src.Dispose(); } catch { } Src = null; } }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetIconicThumbnail(IntPtr h, IntPtr hbmp, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetIconicLivePreviewBitmap(IntPtr h, IntPtr hbmp, ref POINT client, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmInvalidateIconicBitmaps(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr h, string s);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    }

    public static class Taskbar
    {
        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")] class CTaskbarList { }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        interface ITaskbarList3
        {
            [PreserveSig] int HrInit(); [PreserveSig] int AddTab(IntPtr hwnd); [PreserveSig] int DeleteTab(IntPtr hwnd); [PreserveSig] int ActivateTab(IntPtr hwnd); [PreserveSig] int SetActiveAlt(IntPtr hwnd);
            [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, bool full);
            [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong done, ulong total); [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
            [PreserveSig] int RegisterTab(IntPtr hwndTab, IntPtr hwndMDI); [PreserveSig] int UnregisterTab(IntPtr hwndTab);
            [PreserveSig] int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore); [PreserveSig] int SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint reserved);
        }
        static ITaskbarList3 tb; static bool tried;
        static ITaskbarList3 Get() { if (tb == null && !tried) { tried = true; try { tb = (ITaskbarList3)new CTaskbarList(); if (tb.HrInit() < 0) tb = null; } catch { tb = null; } } return tb; }
        public static int LastHr;
        public static int Register(IntPtr tab, IntPtr main) { var t = Get(); if (t == null) return LastHr = -1; return LastHr = t.RegisterTab(tab, main); }
        public static int Unregister(IntPtr tab) { var t = Get(); return t == null ? -1 : t.UnregisterTab(tab); }
        public static int Order(IntPtr tab, IntPtr before) { var t = Get(); return t == null ? -1 : t.SetTabOrder(tab, before); }
        public static int SetActive(IntPtr tab, IntPtr main) { var t = Get(); return t == null ? -1 : t.SetTabActive(tab, main, 0); }
    }

    public static partial class Program
    {
        public static string PendingJump;
        // a jump-list argument -> the pipe's "invoke" item (anything else: nothing)
        public static string JumpInvoke(string a)
        {
            switch ((a ?? "").Trim().ToLowerInvariant()) { case "new-tab": return "new-tab"; case "private-window": return "private-window"; case "reading-list": return "reading-list"; }
            return null;
        }
        public static void RunPendingJump(BrowserWindow w)
        {
            var j = PendingJump; PendingJump = null; if (j == null) return;
            if (j == "new-tab") w.NewTab(Omni.NewTabUrl, true);
            else if (j == "private-window") OpenWindow(true, null, null);
            else if (j == "reading-list") w.Side.ShowNative("reading");
        }
    }

    public partial class BrowserWindow
    {
        // ---- the jump list: made at idle a few seconds after the window is up (first window of a normal browser only)
        public static bool JumpListDone; public static string JumpListNote;
        public void ScheduleJumpList(int delayMs)
        {
            if (snap || Private || AppMode) return;
            if (BrowserSettings.TestDir != null && BrowserSettings.JumpListTestId == null) return;   // tests never touch the real list
            var _ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(delayMs); UpdateJumpList(); }), DispatcherPriority.ApplicationIdle);
        }
        public bool UpdateJumpList()
        {
            if (BrowserSettings.TestDir != null && BrowserSettings.JumpListTestId == null) return false;
            var exe = Program.ExePath; var appId = BrowserSettings.JumpListTestId;
            bool ok;
            if (!S.JumpList) ok = JumpList.Commit(exe, new List<JumpItem>(), appId);   // off: an empty list
            else ok = JumpList.Commit(exe, JumpList.Items(exe, Program.Profiles), appId);
            JumpListDone = ok; JumpListNote = ok ? (S.JumpList ? JumpList.LastCount + " items" : "empty (off)") : JumpList.LastError;
            return ok;
        }

        // ---- tab previews in the taskbar
        DispatcherTimer thumbsTimer; bool thumbsOn;
        public int ThumbsRegistered;
        void ThumbsSoon()
        {
            if (thumbsTimer == null) { thumbsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) }; thumbsTimer.Tick += (a, b) => { thumbsTimer.Stop(); SyncThumbs(); }; }
            thumbsTimer.Stop(); thumbsTimer.Start();
        }
        public readonly Dictionary<Tab, TabThumb> Thumbs = new Dictionary<Tab, TabThumb>();
        public int ThumbRegisterHr;
        public void SyncThumbs()
        {
            var main = new WindowInteropHelper(this).Handle;
            bool want = S.TaskbarTabs && !snap && !AppMode && main != IntPtr.Zero && IsVisible;
            // tabs that closed or moved to another window lose their stand-in
            foreach (var kv in Thumbs.ToList()) if (!want || !Tabs.Contains(kv.Key)) { Taskbar.Unregister(kv.Value.Handle); kv.Value.Dispose(); Thumbs.Remove(kv.Key); }
            if (!want) { thumbsOn = false; ThumbsRegistered = 0; return; }
            thumbsOn = true;
            foreach (var t in Tabs)
            {
                TabThumb th; if (!Thumbs.TryGetValue(t, out th)) { th = new TabThumb(this, t); Thumbs[t] = th; ThumbRegisterHr = Taskbar.Register(th.Handle, main); }
                th.SetTitle(t.Title);
            }
            for (int i = Tabs.Count - 1; i >= 0; i--) Taskbar.Order(Thumbs[Tabs[i]].Handle, i + 1 < Tabs.Count ? Thumbs[Tabs[i + 1]].Handle : IntPtr.Zero);
            if (Active != null && Thumbs.ContainsKey(Active)) Taskbar.SetActive(Thumbs[Active].Handle, main);
            ThumbsRegistered = Thumbs.Count;
        }
        public bool Showing(Tab t) { return IsShown(t); }
        public Rect ContentRectOnWindow() { try { var p = content.TransformToAncestor(this).Transform(new Point(0, 0)); return new Rect(p.X, p.Y, content.ActualWidth, content.ActualHeight); } catch { return new Rect(); } }
        public async Task CapturePreviewFor(Tab t)
        {
            if (t == null || t.Core == null) return;
            try
            {
                var ms = new MemoryStream(); await t.Core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ms); ms.Position = 0;
                var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.DecodePixelWidth = 320; img.EndInit(); img.Freeze();
                t.Preview = Speed19.Detach(img); t.PreviewAt = DateTime.Now;
            }
            catch { }
        }

        // ---- Settings > Appearance > Taskbar
        public UIElement[] TaskbarSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Switch("Jump list on the taskbar and Start icon", "Right-click Hallow Browser's icon: New tab, New private window, Reading list and your profiles.", S.JumpList, on => { S.JumpList = on; S.Save(); UpdateJumpList(); }));
            items.Add(Ui.Switch("Show each tab in the taskbar preview", "Off by default. Hovering the taskbar button shows a preview per tab (from the tab's own picture); click one to switch to it. Windows draws them - with many tabs the row gets long.", S.TaskbarTabs, on => { S.TaskbarTabs = on; S.Save(); foreach (var x in Program.Windows) x.SyncThumbs(); }));
            return items.ToArray();
        }

        // ---- Cast (WebView2 can't): open the tab in Microsoft Edge, which can
        public static string LastCastLaunch;
        public void OpenInEdgeToCast(Tab t)
        {
            if (t == null || !Omni.IsWeb(t.Url)) { Toast("Open the page you want to cast first.", 4); return; }
            var u = t.Url; LastCastLaunch = u;
            if (NewTabFamily.LaunchLog != null) { try { File.AppendAllText(NewTabFamily.LaunchLog, "edge " + u + "\r\n"); } catch { } Toast("Opened in Edge - use Edge's menu > Cast media to device.", 6); return; }   // tests: written down, never opened
            try { Process.Start(new ProcessStartInfo("microsoft-edge:" + u) { UseShellExecute = true }); Toast("Opened in Microsoft Edge - use its menu (...) > More tools > Cast media to device.", 7); }
            catch { Toast("Microsoft Edge couldn't be started.", 5); }
        }
        public UIElement[] CastSettings()
        {
            var items = new List<UIElement>();
            items.Add(Ui.Muted("Casting isn't available in Hallow Browser: the page engine (Microsoft's WebView2) has Chromium's Presentation API but nothing behind it - it never finds a Chromecast or TV (measured: \"No screens found\") and has no Cast dialog.", 12, 8));
            items.Add(Ui.Muted("What works instead: \"Open in Edge to cast\" (the menu, Ctrl+K or the button below) opens the current tab in Microsoft Edge, which comes with Windows - then Edge's menu > More tools > Cast media to device. Your Hallow sign-ins aren't in Edge, so you may have to sign in there.", 11.5, 8));
            items.Add(Ui.Row(Ui.Btn("Open this tab in Edge to cast", () => OpenInEdgeToCast(Tabs.Where(x => Omni.IsWeb(x.Url)).OrderByDescending(x => x.LastActive).FirstOrDefault()), false, "tv")));
            return items.ToArray();
        }
    }
}
