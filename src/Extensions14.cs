// Hallow Browser 1.4 - your own extensions: hallow://extensions loads an unpacked extension (a folder with a manifest.json), switches
// it on or off, removes it. The folder is copied into the browser's data (LocalAppData\HallowBrowser\Extensions), so moving or
// deleting the original later doesn't break it, and loaded with WebView2's AddBrowserExtensionAsync. Hallow Shield stays built in
// (it can't be removed). Chrome Web Store installs aren't possible in WebView2 - the page says what to do instead.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    [DataContract]
    public class UserExtension
    {
        [DataMember] public string Id, Name, Version, Folder, Source, Popup, Options, Description;
        [DataMember] public bool Enabled = true;
        [OnDeserializing] void D(StreamingContext c) { Enabled = true; }
    }

    public static class ExtStore
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        public static string Dir { get { return Path.Combine(BrowserSettings.LocalDir, "Extensions"); } }
        static string Str(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as string : null; }
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as Dictionary<string, object> : null; }
        // "__MSG_name__" from _locales/<default_locale>/messages.json
        static string Msg(string folder, string def, string s)
        {
            var m = s == null ? null : Regex.Match(s, "^__MSG_(\\w+)__$"); if (m == null || !m.Success) return s;
            foreach (var loc in new[] { def, "en", "en_US" }.Where(x => !string.IsNullOrEmpty(x)))
                try
                {
                    var f = Path.Combine(folder, "_locales", loc, "messages.json"); if (!File.Exists(f)) continue;
                    var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    foreach (var kv in d) if (string.Equals(kv.Key, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) { var v = Str(kv.Value as Dictionary<string, object>, "message"); if (v != null) return v; }
                }
                catch { }
            return m.Groups[1].Value;
        }
        // the extension a folder holds, or null with the reason
        public static UserExtension Read(string folder, out string error)
        {
            error = null;
            try
            {
                var mf = Path.Combine(folder ?? "", "manifest.json");
                if (!File.Exists(mf)) { error = "There's no manifest.json in that folder. Pick the folder that has the extension's manifest.json in it (unzip a downloaded .zip or .crx first)."; return null; }
                var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(mf));
                object mv; int ver = d != null && d.TryGetValue("manifest_version", out mv) && mv is int ? (int)mv : 0;
                if (ver != 2 && ver != 3) { error = "That manifest.json isn't a Chromium extension manifest (manifest_version 2 or 3)."; return null; }
                var def = Str(d, "default_locale");
                var name = Msg(folder, def, Str(d, "name")); if (string.IsNullOrWhiteSpace(name)) { error = "The extension has no name."; return null; }
                var action = Obj(d, "action") ?? Obj(d, "browser_action") ?? Obj(d, "page_action");
                var opt = Str(Obj(d, "options_ui"), "page") ?? Str(d, "options_page");
                return new UserExtension { Name = name.Length > 80 ? name.Substring(0, 80) : name, Version = Str(d, "version") ?? "", Popup = Str(action, "default_popup"), Options = opt, Description = Msg(folder, def, Str(d, "description")) };
            }
            catch (Exception e) { error = "The manifest.json couldn't be read: " + e.Message; return null; }
        }
        public static string Slug(string name) { var s = Regex.Replace((name ?? "ext").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'); if (s.Length == 0) s = "ext"; return s.Length > 32 ? s.Substring(0, 32) : s; }
        public static string CopyIn(string src, string name)
        {
            var dest = Path.Combine(Dir, Slug(name) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            long total = 0; Copy(Path.GetFullPath(src), dest, ref total);
            return dest;
        }
        static void Copy(string from, string to, ref long total)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) { total += new FileInfo(f).Length; if (total > 300L * 1024 * 1024) throw new IOException("the folder is bigger than 300 MB"); File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); }
            foreach (var d in Directory.GetDirectories(from)) { var n = Path.GetFileName(d); if (n == ".git" || n == "node_modules") continue; Copy(d, Path.Combine(to, n), ref total); }
        }
        public static bool Ours(string folder) { try { return folder != null && Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Dir) + "\\", StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    }

    public partial class BrowserWindow
    {
        Button extBtn;
        public string ExtNote;
        FrameworkElement BuildExtButton()
        {
            extBtn = Ui.Tool("puzzle-piece", "Your extensions", () => ExtMenu(extBtn), 14);
            extBtn.Visibility = S.Extensions.Any(x => x.Enabled) ? Visibility.Visible : Visibility.Collapsed;
            return extBtn;
        }
        void ExtUiChanged()
        {
            foreach (var w in Program.Windows) if (w.extBtn != null) w.extBtn.Visibility = S.Extensions.Any(x => x.Enabled) && !w.Private ? Visibility.Visible : Visibility.Collapsed;
            var t = Tabs.FirstOrDefault(x => x.Url == Omni.ExtensionsUrl); if (t != null && extList != null) RefreshExtList();
        }
        async Task LoadUserExtensions(CoreWebView2Profile p)
        {
            if (S.Extensions.Count == 0) return;
            try
            {
                var have = await p.GetBrowserExtensionsAsync();
                foreach (var x in S.Extensions.ToList())
                {
                    var e = have.FirstOrDefault(h => h.Id == x.Id);
                    if (e == null && Directory.Exists(x.Folder)) try { e = await p.AddBrowserExtensionAsync(x.Folder); x.Id = e.Id; } catch (Exception ex) { ExtNote = x.Name + " didn't load: " + ex.Message; }
                    if (e != null && e.IsEnabled != x.Enabled) try { await e.EnableAsync(x.Enabled); } catch { }
                }
                S.Save();
            }
            catch (Exception e) { ExtNote = "Your extensions couldn't be listed: " + e.Message; }
            ExtUiChanged();
        }
        // "Load unpacked": the folder is checked, copied in and added. Returns null when it worked, else what went wrong.
        public async Task<string> LoadUnpacked(string src)
        {
            string err; var m = ExtStore.Read(src, out err); if (m == null) return err;
            var p = NormalProfile; if (p == null) return "The page engine isn't ready yet - try again in a moment.";
            if (ShieldState != "extension" && ShieldState != "fallback") { }
            string dest;
            try { dest = await Task.Run(() => ExtStore.CopyIn(src, m.Name)); } catch (Exception e) { return "The folder couldn't be copied: " + e.Message; }
            CoreWebView2BrowserExtension ext;
            try { ext = await p.AddBrowserExtensionAsync(dest); }
            catch (Exception e) { try { Directory.Delete(dest, true); } catch { } return "WebView2 didn't load it (" + e.Message + "). Some extensions use APIs WebView2 doesn't have."; }
            if (ext.Id == ShieldId) { try { Directory.Delete(dest, true); } catch { } return "That's Hallow Shield - it's built in already."; }
            foreach (var old in S.Extensions.Where(x => x.Id == ext.Id).ToList()) { S.Extensions.Remove(old); if (ExtStore.Ours(old.Folder) && old.Folder != dest) try { Directory.Delete(old.Folder, true); } catch { } }   // the same extension again: a newer copy
            m.Id = ext.Id; m.Folder = dest; m.Source = Path.GetFullPath(src); m.Enabled = true;
            S.Extensions.Add(m); S.Save(); ExtUiChanged();
            Toast(m.Name + " " + m.Version + " loaded.", 3);
            return null;
        }
        public async Task SetExtEnabled(UserExtension x, bool on)
        {
            var p = NormalProfile; x.Enabled = on; S.Save();
            if (p != null) try { var e = (await p.GetBrowserExtensionsAsync()).FirstOrDefault(h => h.Id == x.Id); if (e != null) await e.EnableAsync(on); } catch (Exception ex) { Toast("Couldn't switch it " + (on ? "on" : "off") + ": " + ex.Message, 5); }
            ExtUiChanged();
        }
        public async Task RemoveExt(UserExtension x)
        {
            var p = NormalProfile;
            if (p != null) try { var e = (await p.GetBrowserExtensionsAsync()).FirstOrDefault(h => h.Id == x.Id); if (e != null) await e.RemoveAsync(); } catch (Exception ex) { Toast("WebView2 didn't remove it: " + ex.Message, 5); return; }
            S.Extensions.Remove(x); S.Save();
            if (ExtStore.Ours(x.Folder)) try { Directory.Delete(x.Folder, true); } catch { }   // only the browser's own copy (never your original folder)
            ExtUiChanged(); Toast(x.Name + " removed.", 3);
        }
        void PickUnpacked()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Pick the extension's folder (the one with manifest.json in it)", ShowNewFolderButton = false })
            {
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var path = d.SelectedPath;
                var _ = LoadUnpacked(path).ContinueWith(r => Dispatcher.BeginInvoke(new Action(() => { if (r.Result != null) Toast(r.Result, 8); })));
            }
        }

        // ---- the overflow menu (the puzzle piece) and an extension's popup in a flyout like Shield's
        void ExtMenu(UIElement at)
        {
            var sp = new StackPanel(); Popup pop = null;
            foreach (var x in S.Extensions.Where(e => e.Enabled).ToList())
            {
                var xx = x; var r = Ui.MenuRow("puzzle-piece", xx.Name, xx.Popup != null ? null : xx.Options != null ? "options" : null, () =>
                {
                    pop.IsOpen = false;
                    if (xx.Popup != null) { var _ = ExtPopup(xx, at); } else if (xx.Options != null) NewTab("chrome-extension://" + xx.Id + "/" + xx.Options.TrimStart('/'), true); else Toast(xx.Name + " has no popup.", 3);
                });
                sp.Children.Add(r);
            }
            sp.Children.Add(Ui.MenuRow("gear", "Manage extensions", null, () => { pop.IsOpen = false; NewTab(Omni.ExtensionsUrl, true); }));
            pop = Panels.ShowFlyout(at, sp, 280);
        }
        static Window extWin; static WebView2 extView;
        public async Task<string> ExtPopup(UserExtension x, UIElement at)
        {
            if (extWin != null && extWin.Owner != this) { try { extWin.Close(); } catch { } extWin = null; }
            if (extWin == null)
            {
                extWin = new Window { Owner = this, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Width = 380, Height = 560 };
                extWin.SetResourceReference(Window.BackgroundProperty, "Panel");
                var border = new Border { BorderThickness = new Thickness(1) }; border.SetResourceReference(Border.BorderBrushProperty, "Line");
                extView = new WebView2(); border.Child = extView; extWin.Content = border;
                extWin.Deactivated += (a, b) => { if (!BrowserSettings.Offscreen && extWin != null) extWin.Hide(); };
                extWin.KeyDown += (a, b) => { if (b.Key == Key.Escape) extWin.Hide(); };
                var me = extWin; extWin.Closed += (a, b) => { if (extWin == me) { extWin = null; extView = null; } };
            }
            if (BrowserSettings.Offscreen) { extWin.Left = -32000; extWin.Top = -32000; extWin.ShowActivated = false; }
            else if (at != null)
            {
                var fe = (FrameworkElement)at; var pt = fe.PointToScreen(new Point(fe.ActualWidth, fe.ActualHeight + 4));
                var src = PresentationSource.FromVisual(this); var m = src != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
                var p = m.Transform(pt); extWin.Left = p.X - extWin.Width; extWin.Top = p.Y;
            }
            extWin.Show(); if (!BrowserSettings.Offscreen) extWin.Activate();
            await extView.EnsureCoreWebView2Async(Engine.Env);
            var url = "chrome-extension://" + x.Id + "/" + x.Popup.TrimStart('/');
            await Panels.NavigateAndWait(extView.CoreWebView2, url);
            return extView.CoreWebView2.Source;
        }
        public static CoreWebView2 ExtPopupCore { get { try { return extView == null ? null : extView.CoreWebView2; } catch { return null; } } }

        // ---- hallow://extensions
        StackPanel extList;
        FrameworkElement ExtensionsPage()
        {
            var col = new StackPanel { MaxWidth = 820, Margin = new Thickness(28, 26, 28, 40), HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(Ui.Row(Ui.Icon("puzzle-piece", 18, "AccentBrush"), Ui.T("  EXTENSIONS", 20, true, "Text")));
            col.Children.Add(Ui.Muted("Chromium extensions you load yourself run in every normal window (not in private windows). Their buttons are under the puzzle piece in the toolbar.", 12, 12));
            var tools = Ui.Row(Ui.Btn("Load unpacked...", PickUnpacked, true, "folder-open", "Pick an extension's folder (the one with manifest.json)"));
            tools.Margin = new Thickness(0, 0, 0, 14); col.Children.Add(tools);
            extList = new StackPanel(); col.Children.Add(extList);
            col.Children.Add(Ui.Card(new StackPanel { Children = {
                Ui.Label("Chrome Web Store?"),
                Ui.Muted("Installing from the Chrome Web Store isn't possible in WebView2 (Microsoft's engine has no store support), and Hallow Browser can't add it. What you can do instead:", 12, 6),
                Ui.Muted("1. Get the extension's source or a release zip from its developer (most open-source extensions publish one on GitHub), unzip it.\n2. Click \"Load unpacked...\" and pick the folder with manifest.json in it.\n3. Updates don't come by themselves: load the newer folder the same way (it replaces the old copy and keeps the extension's data).", 12, 6),
                Ui.Muted("Not everything works: WebView2 leaves out some extension APIs (for example native messaging, and permission prompts for optional permissions), and extensions don't run in private windows. Only load extensions you trust - they can read and change the pages you open.", 11.5, 0) } }));
            RefreshExtList();
            var sv = new ScrollViewer { Content = col, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            sv.SetResourceReference(Control.BackgroundProperty, "Bg");
            return sv;
        }
        void RefreshExtList()
        {
            if (extList == null) return;
            extList.Children.Clear();
            // Shield: built in, can't be removed
            var sh = new DockPanel();
            var shb = Ui.Btn("Shield settings", () => Panels.OpenShieldSettings(this), false, "shield-halved"); DockPanel.SetDock(shb, Dock.Right); sh.Children.Add(shb);
            var st = new StackPanel(); st.Children.Add(Ui.Row(Ui.Icon("shield-halved", 14, "AccentBrush"), Ui.T("  Hallow Shield " + (ShieldVersion ?? ""), 14, true, "Text"), Ui.T("   BUILT IN", 10.5, true, "Lilac")));
            st.Children.Add(Ui.Muted(ShieldState == "extension" ? "Always on - pause it per site in its panel. It comes with Hallow Browser and can't be removed." + (ShieldCounts ? " Counts what it blocks on each page." : "") : ShieldNote ?? "Starting...", 11.5, 0));
            sh.Children.Add(st); extList.Children.Add(Ui.Card(sh));
            if (ExtNote != null) extList.Children.Add(Ui.Muted(ExtNote, 11.5, 8));
            if (S.Extensions.Count == 0) extList.Children.Add(Ui.Muted("None of your own yet.", 12, 14));
            foreach (var x in S.Extensions.ToList())
            {
                var xx = x; var row = new DockPanel();
                var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top }; DockPanel.SetDock(tools, Dock.Right);
                if (xx.Popup != null && xx.Enabled) tools.Children.Add(Ui.Btn("Open", () => { var _ = ExtPopup(xx, extBtn); }, false, "up-right-from-square"));
                if (xx.Options != null && xx.Enabled) tools.Children.Add(Ui.Btn("Options", () => NewTab("chrome-extension://" + xx.Id + "/" + xx.Options.TrimStart('/'), true), false, "gear"));
                tools.Children.Add(Ui.Btn("Remove", () => { var _ = RemoveExt(xx); }, false, "trash-can"));
                foreach (FrameworkElement b in tools.Children) b.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(tools);
                var info = new StackPanel();
                info.Children.Add(Ui.Row(Ui.Icon("puzzle-piece", 13, "Lilac"), Ui.T("  " + xx.Name + "  " + xx.Version, 14, true, "Text")));
                if (!string.IsNullOrWhiteSpace(xx.Description)) info.Children.Add(Ui.Muted(xx.Description.Length > 160 ? xx.Description.Substring(0, 160) + "..." : xx.Description, 11.5, 2));
                info.Children.Add(Ui.Muted("ID " + xx.Id + "  -  loaded from " + (xx.Source ?? "?"), 10.5, 6));
                info.Children.Add(Ui.Switch("On", null, xx.Enabled, on => { var _ = SetExtEnabled(xx, on); }));
                row.Children.Add(info);
                extList.Children.Add(Ui.Card(row));
            }
        }
    }
}
