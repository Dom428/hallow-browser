// Hallow Browser 1.6 - install a site as an app: its own window (no tabs, no address bar), its icon and name, a Start menu
// shortcut ("HallowBrowser.exe --app <url> --profile <id>") and a list at hallow://apps with Uninstall. The name, start address,
// scope and icon come from the page's web app manifest when it has one (read by the page itself, same as the site would); without
// one: the page's title and the favicon the engine already has (GetFaviconAsync - no extra fetch). Links that leave the site open
// in the browser.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    [DataContract]
    public class InstalledApp
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Url;        // where it starts
        [DataMember] public string Scope;      // addresses that stay in its window (origin + path prefix)
        [DataMember] public string Icon;       // png (LocalAppData\HallowBrowser[\Profiles\<id>]\apps)
        [DataMember] public string Ico;        // the shortcut's icon
        [DataMember] public string Shortcut;   // the Start menu .lnk
        [DataMember] public string IconFrom;   // manifest | favicon | none
        [DataMember] public string Installed;
    }
    [DataContract] public class AppList { [DataMember] public List<InstalledApp> Items = new List<InstalledApp>(); }

    public class ManifestInfo { public string Name, Start, Scope, IconUrl; }

    public static class SiteApps
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public static string Dir { get { return Path.Combine(BrowserSettings.LocalDir, "apps"); } }
        static string FilePath { get { return Path.Combine(Dir, "apps.json"); } }
        // tests never touch your Start menu
        public static string StartMenuDir { get { return BrowserSettings.TestDir != null ? Path.Combine(BrowserSettings.TestDir, "StartMenu", "Hallow Browser Apps") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hallow Browser Apps"); } }
        public static List<InstalledApp> SnapItems;   // --snap: made-up apps
        public static List<InstalledApp> Load() { if (SnapItems != null) return SnapItems.ToList(); var l = BrowserSettings.Read<AppList>(FilePath); return l == null || l.Items == null ? new List<InstalledApp>() : l.Items.Where(x => x != null && Omni.IsWeb(x.Url)).ToList(); }
        public static void Save(List<InstalledApp> items) { Directory.CreateDirectory(Dir); BrowserSettings.WriteFile(FilePath, new AppList { Items = items }, typeof(AppList)); }
        public static InstalledApp ForUrl(string url)
        {
            var all = Load();
            return all.FirstOrDefault(a => string.Equals(a.Url, url, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault(a => InScope(a, url));
        }
        // an address that isn't installed (an old shortcut): its own window anyway, named after the site
        public static InstalledApp Adhoc(string url) { return new InstalledApp { Id = "adhoc", Name = Omni.HostOf(url) ?? "App", Url = url, Scope = ScopeOf(url, null) }; }
        public static string ScopeOf(string start, string scope)
        {
            Uri s; if (!Uri.TryCreate(start ?? "", UriKind.Absolute, out s)) return null;
            Uri sc; if (scope != null && Uri.TryCreate(s, scope, out sc) && sc.Scheme == s.Scheme && sc.Authority == s.Authority) return sc.GetLeftPart(UriPartial.Path);
            return s.GetLeftPart(UriPartial.Authority) + "/";
        }
        public static bool InScope(InstalledApp a, string url)
        {
            if (a == null || a.Scope == null || !Omni.IsWeb(url)) return false;
            Uri u, s; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || !Uri.TryCreate(a.Scope, UriKind.Absolute, out s)) return false;
            return u.Scheme == s.Scheme && string.Equals(u.Authority, s.Authority, StringComparison.OrdinalIgnoreCase) && u.AbsolutePath.StartsWith(s.AbsolutePath, StringComparison.Ordinal);
        }
        // the manifest's name, start_url, scope and best icon (relative addresses against the manifest's own address)
        public static ManifestInfo ParseManifest(string json, string manifestUrl, string pageUrl)
        {
            var m = new ManifestInfo();
            Dictionary<string, object> d; try { d = js.Deserialize<Dictionary<string, object>>(json); } catch { return null; }
            if (d == null) return null;
            Uri baseU; if (!Uri.TryCreate(manifestUrl ?? pageUrl ?? "", UriKind.Absolute, out baseU)) return null;
            Uri page; Uri.TryCreate(pageUrl ?? "", UriKind.Absolute, out page);
            Func<string, string> S = k => { object o; return d.TryGetValue(k, out o) ? o as string : null; };
            m.Name = ProfileStore.CleanName(S("name")) ?? ProfileStore.CleanName(S("short_name"));
            Uri st; var su = S("start_url");
            if (su != null && Uri.TryCreate(baseU, su, out st) && (st.Scheme == "https" || st.Scheme == "http") && (page == null || st.Authority == page.Authority)) m.Start = st.AbsoluteUri;
            Uri sc; var sco = S("scope"); if (sco != null && Uri.TryCreate(baseU, sco, out sc) && (page == null || sc.Authority == page.Authority)) m.Scope = sc.AbsoluteUri;
            object ic; int best = -1;
            if (d.TryGetValue("icons", out ic) && ic is System.Collections.IEnumerable && !(ic is string))
                foreach (var o in (System.Collections.IEnumerable)ic)
                {
                    var x = o as Dictionary<string, object>; if (x == null) continue;
                    object src, sizes, type, purpose; x.TryGetValue("src", out src); x.TryGetValue("sizes", out sizes); x.TryGetValue("type", out type); x.TryGetValue("purpose", out purpose);
                    var s = src as string; if (s == null) continue;
                    if (purpose is string && ((string)purpose).Split(' ').All(p => p == "monochrome")) continue;
                    if (type is string && ((string)type).Contains("svg") || s.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;   // WPF can't draw SVG icons
                    int size = 0; foreach (var part in ((sizes as string) ?? "").Split(' ')) { var wh = part.Split('x', 'X'); int v; if (wh.Length == 2 && int.TryParse(wh[0], out v)) size = Math.Max(size, v); }
                    int score = size == 0 ? 1 : size > 512 ? 512 - (size - 512) / 4 : size;   // the biggest up to 512
                    Uri iu; if (score > best && Uri.TryCreate(baseU, s, out iu) && (iu.Scheme == "https" || iu.Scheme == "http" || iu.Scheme == "data")) { best = score; m.IconUrl = iu.AbsoluteUri; }
                }
            return m;
        }
        // a picture (png, jpg, ico, gif, bmp) as a square png of at most `size` px; null when it can't be read
        public static byte[] SquarePng(byte[] img, int size)
        {
            try
            {
                var dec = BitmapDecoder.Create(new MemoryStream(img), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource f = dec.Frames.OrderByDescending(x => x.PixelWidth).First();
                int side = Math.Min(size, Math.Max(f.PixelWidth, f.PixelHeight)); if (side < 1) return null;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    double sc = (double)side / Math.Max(f.PixelWidth, f.PixelHeight), w = f.PixelWidth * sc, h = f.PixelHeight * sc;
                    RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
                    dc.DrawImage(f, new Rect((side - w) / 2, (side - h) / 2, w, h));
                }
                var rt = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32); rt.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rt));
                var ms = new MemoryStream(); enc.Save(ms); return ms.ToArray();
            }
            catch { return null; }
        }
        // an .ico with PNG images inside (Windows Vista and later read those): 256, 48, 32 and 16 px
        // exactly size x size (a small favicon is scaled up), as 32-bit BGRA pixels
        static BitmapSource Square(byte[] img, int size)
        {
            try
            {
                var dec = BitmapDecoder.Create(new MemoryStream(img), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapSource f = dec.Frames.OrderByDescending(x => x.PixelWidth).First();
                var dv = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
                using (var dc = dv.RenderOpen()) { double sc = (double)size / Math.Max(f.PixelWidth, f.PixelHeight), w = f.PixelWidth * sc, h = f.PixelHeight * sc; dc.DrawImage(f, new Rect((size - w) / 2, (size - h) / 2, w, h)); }
                var rt = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32); rt.Render(dv);
                return new FormatConvertedBitmap(rt, PixelFormats.Bgra32, null, 0);
            }
            catch { return null; }
        }
        // an .ico: 256 px as PNG (Windows Vista and later), 48 / 32 / 16 px as classic 32-bit bitmaps (what every part of Windows reads)
        public static byte[] Ico(byte[] png)
        {
            var sizes = new[] { 256, 48, 32, 16 }; var data = new List<byte[]>();
            foreach (var s in sizes)
            {
                var b = Square(png, s); if (b == null) return null;
                if (s == 256) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(b)); var m = new MemoryStream(); enc.Save(m); data.Add(m.ToArray()); continue; }
                var px = new byte[s * s * 4]; b.CopyPixels(px, s * 4, 0);
                var m2 = new MemoryStream(); var bw = new BinaryWriter(m2);
                int maskRow = ((s + 31) / 32) * 4;
                bw.Write(40); bw.Write(s); bw.Write(s * 2); bw.Write((short)1); bw.Write((short)32); bw.Write(0); bw.Write(s * s * 4 + maskRow * s); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = s - 1; y >= 0; y--) bw.Write(px, y * s * 4, s * 4);   // bottom-up rows
                bw.Write(new byte[maskRow * s]);                                    // the AND mask (alpha does the work)
                bw.Flush(); data.Add(m2.ToArray());
            }
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((short)0); w.Write((short)1); w.Write((short)data.Count);
            int off = 6 + 16 * data.Count;
            for (int i = 0; i < data.Count; i++)
            {
                int px = sizes[i];
                w.Write((byte)(px >= 256 ? 0 : px)); w.Write((byte)(px >= 256 ? 0 : px)); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32); w.Write(data[i].Length); w.Write(off); off += data[i].Length;
            }
            foreach (var b in data) w.Write(b);
            w.Flush(); return ms.ToArray();
        }
        public static string SafeName(string name)
        {
            var s = new string((name ?? "App").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim().TrimEnd('.');
            return s.Length == 0 ? "App" : s.Length > 60 ? s.Substring(0, 60).Trim() : s;
        }
        public static string ShortcutArgs(string url, string profile) { return "--app \"" + url.Replace("\"", "%22") + "\" --profile " + profile; }
        // a Start menu shortcut through Windows' own shell object (late bound - no extra assembly)
        public static bool CreateShortcut(string lnk, string target, string args, string icon, string description)
        {
            object sh = null, sc = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell"); if (t == null) return false;
                sh = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                if (icon != null) st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { icon + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description ?? "" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                return File.Exists(lnk);
            }
            catch { return false; }
            finally { try { if (sc != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc); if (sh != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sh); } catch { } }
        }
        public static string ReadShortcut(string lnk, string what)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell"); var sh = Activator.CreateInstance(t);
                var sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                return sc.GetType().InvokeMember(what, BindingFlags.GetProperty, null, sc, null) as string;
            }
            catch { return null; }
        }
        public static string NewId(string name, IEnumerable<InstalledApp> have) { return ProfileStore.NewId(name, have.Select(a => a.Id)); }
    }

    public partial class BrowserWindow
    {
        public InstalledApp App;                         // set: this window is an installed site's own window
        public bool AppMode { get { return App != null; } }
        public static int AppsInstalled, AppsOpened;
        public string LastInstallNote;

        // ---- the page's manifest (and its icon), read by the page itself
        const string ManifestScript = "(async()=>{var r={title:document.title||'',href:location.href,mu:null,m:null};var l=document.querySelector('link[rel~=\"manifest\"]');if(l&&l.href){r.mu=l.href;try{var x=await fetch(l.href,{credentials:'same-origin'});if(x.ok)r.m=await x.text();}catch(e){}}return JSON.stringify(r);})()";
        static string IconScript(string url) { return "(async()=>{try{var x=await fetch(" + new JavaScriptSerializer().Serialize(url) + ",{credentials:'same-origin'});if(!x.ok)return '';var b=await x.blob();if(b.size>2097152)return '';return await new Promise(function(res){var f=new FileReader();f.onload=function(){res(f.result)};f.onerror=function(){res('')};f.readAsDataURL(b)});}catch(e){return ''}})()"; }
        public async Task<InstalledApp> InstallSite(Tab t)
        {
            if (t == null || t.Core == null || Private || AppMode || !Omni.IsWeb(t.Url)) { Toast("Open a site in a normal window to install it.", 4); return null; }
            var c = t.Core; var jsz = new JavaScriptSerializer();
            string title = t.Title, href = t.Url, mu = null, mjson = null;
            try
            {
                var raw = jsz.Deserialize<string>(await Panels.Eval(c, ManifestScript));
                var d = jsz.Deserialize<Dictionary<string, object>>(raw);
                title = d["title"] as string ?? title; href = d["href"] as string ?? href; mu = d["mu"] as string; mjson = d["m"] as string;
            }
            catch { }
            var man = mjson == null ? null : SiteApps.ParseManifest(mjson, mu, href);
            var all = SiteApps.Load();
            var name = (man != null ? man.Name : null) ?? ProfileStore.CleanName(title) ?? Omni.HostOf(href) ?? "App";
            var start = man != null && man.Start != null ? man.Start : href;
            var app = all.FirstOrDefault(a => a.Url == start) ?? new InstalledApp { Id = SiteApps.NewId(name, all) };
            app.Name = name; app.Url = start; app.Scope = SiteApps.ScopeOf(start, man != null ? man.Scope : null); app.Installed = DateTime.Now.ToString("s");
            // the icon: the manifest's, else the favicon the engine already has (no extra fetch)
            byte[] png = null; app.IconFrom = "none";
            if (man != null && man.IconUrl != null)
            {
                try
                {
                    var du = jsz.Deserialize<string>(await Panels.Eval(c, IconScript(man.IconUrl)));
                    var comma = du == null ? -1 : du.IndexOf(','); if (comma > 0 && du.Substring(0, comma).Contains("base64")) png = SiteApps.SquarePng(Convert.FromBase64String(du.Substring(comma + 1)), 256);
                    if (png != null) app.IconFrom = "manifest";
                }
                catch { }
            }
            if (png == null)
            {
                try { using (var s = await c.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png)) if (s != null && s.Length > 0) { var ms = new MemoryStream(); s.CopyTo(ms); png = SiteApps.SquarePng(ms.ToArray(), 256); if (png != null) app.IconFrom = "favicon"; } } catch { }
            }
            try
            {
                Directory.CreateDirectory(SiteApps.Dir);
                if (png != null)
                {
                    app.Icon = Path.Combine(SiteApps.Dir, app.Id + ".png"); File.WriteAllBytes(app.Icon, png);
                    var ico = SiteApps.Ico(png); if (ico != null) { app.Ico = Path.Combine(SiteApps.Dir, app.Id + ".ico"); File.WriteAllBytes(app.Ico, ico); }
                }
                Directory.CreateDirectory(SiteApps.StartMenuDir);
                var lnk = Path.Combine(SiteApps.StartMenuDir, SiteApps.SafeName(app.Name) + (BrowserSettings.IsDefaultProfile ? "" : " (" + SiteApps.SafeName(Program.CurrentProfile.Name) + ")") + ".lnk");
                if (app.Shortcut != null && app.Shortcut != lnk) try { File.Delete(app.Shortcut); } catch { }
                app.Shortcut = SiteApps.CreateShortcut(lnk, Program.ExePath, SiteApps.ShortcutArgs(app.Url, BrowserSettings.ProfileId), app.Ico ?? Program.ExePath, app.Name + " - a site installed from Hallow Browser") ? lnk : null;
            }
            catch { }
            all.RemoveAll(a => a.Id == app.Id); all.Add(app); SiteApps.Save(all); AppsInstalled++;
            LastInstallNote = app.Name + " | " + app.IconFrom + " | " + (app.Shortcut != null ? "shortcut" : "no shortcut");
            Toast(app.Shortcut != null ? "Installed \"" + app.Name + "\" - it's in your Start menu (Hallow Browser Apps) and on hallow://apps." : "Installed \"" + app.Name + "\" (the Start menu shortcut couldn't be made) - it's on hallow://apps.", 6);
            Program.OpenApp(app.Url);
            if (Tabs.Count > 1 && SiteApps.InScope(app, t.Url)) CloseTab(t);   // it moved to its own window, like other browsers do
            return app;
        }
        public static bool UninstallApp(string id)
        {
            var all = SiteApps.Load(); var a = all.FirstOrDefault(x => x.Id == id); if (a == null) return false;
            foreach (var f in new[] { a.Shortcut, a.Icon, a.Ico }) if (f != null) try { File.Delete(f); } catch { }   // only the files the browser made for it
            all.Remove(a); SiteApps.Save(all);
            foreach (var w in Program.Windows.Where(x => x.AppMode && x.App.Id == id).ToList()) w.Close();
            return true;
        }

        // ---- an app window: the site's name and icon in the title bar, its page, back and reload - nothing else
        TextBlock appName;
        public void MakeAppWindow(InstalledApp a)
        {
            App = a; Title = a.Name;
            ImageSource icon = null;
            try { if (a.Icon != null && File.Exists(a.Icon)) { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(a.Icon); bi.EndInit(); bi.Freeze(); icon = bi; Icon = bi; } } catch { }
            var top = (Grid)root.Children[0];
            var logoRow = top.Children.OfType<StackPanel>().FirstOrDefault(); if (logoRow != null) logoRow.Visibility = Visibility.Collapsed;
            stripHost.Visibility = Visibility.Collapsed;
            foreach (UIElement e in root.Children) if (Grid.GetRow(e) == 1) e.Visibility = Visibility.Collapsed;   // the toolbar
            var img = new Image { Source = icon ?? Ui.AppIcon(), Width = 16, Height = 16, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            appName = new TextBlock { Text = a.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            appName.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Button ab = Ui.Tool("arrow-left", "Back (Alt+Left)", GoBack, 12), ar = Ui.Tool("arrow-rotate-right", "Reload (F5)", () => { if (Active != null && Active.Core != null) Active.Core.Reload(); }, 12),
                ao = Ui.Tool("up-right-from-square", "Open in Hallow Browser", () => { if (Active != null) OutOfApp(Active.Url); }, 12);
            foreach (var b in new[] { ab, ar, ao }) { b.Width = 30; b.Height = 28; Chrome(b); }
            var row = Ui.Row(img, appName, ab, ar, ao); row.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(row, 1); top.Children.Add(row);
            ApplyTabLayout(); Side.ApplyOn();
            if (!BrowserSettings.Offscreen) { Width = Math.Min(Width, 1100); Height = Math.Min(Height, 760); }
        }
        public async Task StartApp()
        {
            if (snap || App == null) return;
            try { await (Engine.Starting ?? Engine.Create(S)); }
            catch (Exception e) { Toast("The page engine (Microsoft Edge WebView2) didn't start: " + e.Message, 15); return; }
            Engine.WriteNewTab(S, Program.AccentHex);
            NewTab(App.Url, true); AppsOpened++;
            tick.Start(); Tick(true); WriteStatus();
            var _ = Dispatcher.BeginInvoke(new Action(async () => { await Task.Delay(1500); EnsureStores(); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        // anything outside the app: a tab in the browser (this profile's normal window, or a new one)
        Tab OutOfApp(string url)
        {
            var m = Program.First;
            if (m == null) { Program.OpenWindow(false, url, null); return null; }
            var t = m.NewTab(url, true); m.Bring(); return t;
        }
        async Task AppPopup(CoreWebView2NewWindowRequestedEventArgs b)
        {
            var m = Program.First ?? Program.OpenWindow(false, null, null);
            await m.AdoptNewWindow(b, null); m.Bring();
        }
        // links you click that leave the app's scope open in the browser (redirects, e.g. a sign-in, stay)
        void AppScope(CoreWebView2 c)
        {
            c.NavigationStarting += (a, e) =>
            {
                if (!AppMode || !e.IsUserInitiated || !Omni.IsWeb(e.Uri) || SiteApps.InScope(App, e.Uri)) return;
                e.Cancel = true; var u = e.Uri; Dispatcher.BeginInvoke(new Action(() => OutOfApp(u)));
            };
        }

        // ---- hallow://apps
        StackPanel appsList;
        public FrameworkElement AppsPage()
        {
            var col = new StackPanel { MaxWidth = 760, Margin = new Thickness(28, 26, 28, 40), HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(Ui.Row(Ui.Icon("table-cells-large", 22, "AccentBrush"), new TextBlock { Width = 10 }, Ui.T("Apps", 22, true, "Text")));
            col.Children.Add(Ui.Muted("Sites you installed from the menu (Install this site...). Each opens in its own window - no tabs, no address bar - from the Start menu (Hallow Browser Apps) or here. They use this profile's sign-ins" + (BrowserSettings.IsDefaultProfile ? "" : " (" + Program.CurrentProfile.Name + ")") + ".", 12, 14));
            appsList = new StackPanel(); col.Children.Add(appsList); FillApps();
            var sv = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            sv.SetResourceReference(Control.BackgroundProperty, "Bg");
            return sv;
        }
        public int AppsPageRows { get { return appsList == null ? -1 : appsList.Children.OfType<Border>().Count(); } }
        public void FillApps()
        {
            if (appsList == null) return;
            appsList.Children.Clear();
            var all = SiteApps.Load();
            foreach (var a in all)
            {
                var aa = a; var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                FrameworkElement ic = Ui.Icon("globe", 22, "Muted");
                try { if (a.Icon != null && File.Exists(a.Icon)) { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(a.Icon); bi.DecodePixelWidth = 64; bi.EndInit(); bi.Freeze(); ic = new Image { Source = bi, Width = 28, Height = 28 }; } } catch { }
                ic.VerticalAlignment = VerticalAlignment.Center; ic.HorizontalAlignment = HorizontalAlignment.Left; g.Children.Add(ic);
                var tx = new StackPanel(); tx.Children.Add(Ui.T(a.Name, 13, true)); tx.Children.Add(Ui.Muted(a.Url, 11)); tx.Children.Add(Ui.Muted((a.Shortcut != null ? "In the Start menu" : "No Start menu shortcut") + " - icon from " + (a.IconFrom == "manifest" ? "the site's manifest" : a.IconFrom == "favicon" ? "its favicon" : "nowhere (none found)"), 10.5));
                Grid.SetColumn(tx, 1); g.Children.Add(tx);
                var bs = Ui.Row(Ui.Btn("Open", () => Program.OpenApp(aa.Url), true, "up-right-from-square"), Ui.Btn("Uninstall", () => { UninstallApp(aa.Id); FillApps(); Toast("Uninstalled \"" + aa.Name + "\" (its window, Start menu shortcut and icon are gone; your sign-in to the site stays in this profile).", 6); }, false, "trash-can"));
                bs.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(bs, 2); g.Children.Add(bs);
                appsList.Children.Add(Ui.Card(g, 12));
            }
            if (all.Count == 0) appsList.Children.Add(Ui.Muted("No apps yet. Open a site, then the menu > Install this site...", 12.5, 0));
        }
    }

    public static partial class Program
    {
        // an installed site's window (or the one already open for it)
        public static BrowserWindow OpenApp(string url)
        {
            var a = SiteApps.ForUrl(url) ?? SiteApps.Adhoc(url);
            var open = Windows.FirstOrDefault(x => x.AppMode && !x.Gone && x.App.Id == a.Id && a.Id != "adhoc");
            if (open != null) { open.Bring(); return open; }
            var s = Win != null ? Win.S : BrowserSettings.Load();
            var w = new BrowserWindow(s, false); w.MakeAppWindow(a);
            if (!BrowserSettings.Offscreen) w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Register(w);
            w.Loaded += async (x, y) => await w.StartApp();
            w.Show(); if (!BrowserSettings.Offscreen) w.Activate();
            return w;
        }
    }
}
