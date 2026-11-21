// Hallow Browser 1.2 - screenshots (Ctrl+Shift+X): the visible area (CapturePreviewAsync), the full page (DevTools
// Page.captureScreenshot with captureBeyondViewport) or a region you drag over a still of the page; copied to the clipboard or
// saved as PNG to Pictures\Hallow Browser. Nothing leaves the PC.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public static class Shots
    {
        public const int MaxFullHeight = 16000, MaxFullWidth = 8000;   // CSS px (huge pages are cut there)
        public static string Folder { get { return BrowserSettings.TestDir != null ? System.IO.Path.Combine(BrowserSettings.TestDir, "Pictures", "Hallow Browser") : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Hallow Browser"); } }
        public static int LastCopiedBytes;   // tests: the clipboard isn't touched in a test folder

        public static async Task<byte[]> Visible(CoreWebView2 c)
        {
            using (var ms = new MemoryStream()) { await c.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms); return ms.ToArray(); }
        }
        // the page's whole height (and width) in one picture
        public static async Task<byte[]> FullPage(CoreWebView2 c)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 * 1024 };
            var m = js.Deserialize<Dictionary<string, object>>(await c.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
            double cw, ch, vw, vh; Size(m, "cssContentSize", "contentSize", "width", "height", out cw, out ch); Size(m, "cssLayoutViewport", "layoutViewport", "clientWidth", "clientHeight", out vw, out vh);
            var w = Math.Ceiling(Math.Max(vw, Math.Min(cw, MaxFullWidth))); var h = Math.Ceiling(Math.Max(vh, Math.Min(ch, MaxFullHeight)));
            if (w <= 0 || h <= 0) return await Visible(c);
            var args = "{\"format\":\"png\",\"captureBeyondViewport\":true,\"fromSurface\":true,\"clip\":{\"x\":0,\"y\":0,\"width\":" + w.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"height\":" + h.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"scale\":1}}";
            var r = js.Deserialize<Dictionary<string, object>>(await c.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", args));
            object data; if (r == null || !r.TryGetValue("data", out data) || !(data is string)) throw new InvalidOperationException("the engine gave no picture");
            return Convert.FromBase64String((string)data);
        }
        static void Size(Dictionary<string, object> m, string key, string fallback, string wk, string hk, out double w, out double h)
        {
            w = h = 0; object o; if (m == null || !(m.TryGetValue(key, out o) || m.TryGetValue(fallback, out o))) return;
            var d = o as Dictionary<string, object>; if (d == null) return;
            object a, b; if (d.TryGetValue(wk, out a)) w = Convert.ToDouble(a); if (d.TryGetValue(hk, out b)) h = Convert.ToDouble(b);
        }
        public static BitmapSource Decode(byte[] png) { using (var ms = new MemoryStream(png)) { var f = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); f.Freeze(); return f; } }
        public static Size PixelSize(byte[] png) { var b = Decode(png); return new Size(b.PixelWidth, b.PixelHeight); }
        // a part of the picture: r in fractions of its width / height (0..1)
        public static byte[] Crop(byte[] png, Rect r)
        {
            var b = Decode(png);
            int x = (int)Math.Round(Clamp(r.X) * b.PixelWidth), y = (int)Math.Round(Clamp(r.Y) * b.PixelHeight);
            int w = Math.Max(1, Math.Min(b.PixelWidth - x, (int)Math.Round(Clamp(r.Width) * b.PixelWidth))), h = Math.Max(1, Math.Min(b.PixelHeight - y, (int)Math.Round(Clamp(r.Height) * b.PixelHeight)));
            var c = new CroppedBitmap(b, new Int32Rect(Math.Min(x, b.PixelWidth - 1), Math.Min(y, b.PixelHeight - 1), w, h));
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(c));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }
        static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }
        public static string FileName(DateTime t, Func<string, bool> exists)
        {
            var b = "Hallow Browser " + t.ToString("yyyy-MM-dd HH.mm.ss"); var n = b + ".png";
            for (int i = 2; exists(n); i++) n = b + " (" + i + ").png";
            return n;
        }
        public static string Save(byte[] png)
        {
            Directory.CreateDirectory(Folder);
            var path = System.IO.Path.Combine(Folder, FileName(DateTime.Now, n => File.Exists(System.IO.Path.Combine(Folder, n))));
            File.WriteAllBytes(path, png); return path;
        }
        public static void Copy(byte[] png)
        {
            if (BrowserSettings.TestDir != null) { LastCopiedBytes = png.Length; return; }
            Clipboard.SetImage(Decode(png));
        }

        // ---- the flyout: three kinds x copy / save
        public static void Flyout(BrowserWindow w, UIElement at)
        {
            var sp = new StackPanel(); Popup pop = null;
            var head = Ui.Label("Screenshot"); head.Margin = new Thickness(12, 8, 12, 6); sp.Children.Add(head);
            Action<string, string, string> row = (icon, text, mode) =>
            {
                var g = new Grid { Margin = new Thickness(8, 2, 6, 2) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(Ui.Icon(icon, 13, "Lilac"));
                var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1); g.Children.Add(t);
                var copy = Ui.Btn("Copy", () => { pop.IsOpen = false; var _ = Take(w, mode, false, null); }, false, "copy");
                var save = Ui.Btn("Save", () => { pop.IsOpen = false; var _ = Take(w, mode, true, null); }, true, "floppy-disk"); save.Margin = new Thickness(0);
                var r = Ui.Row(copy, save); Grid.SetColumn(r, 2); g.Children.Add(r);
                sp.Children.Add(g);
            };
            row("desktop", "Visible area", "visible");
            row("expand", "Full page", "full");
            row("crop-simple", "Drag a region", "region");
            var m = Ui.Muted("Saved to Pictures\\Hallow Browser. Ctrl+Shift+X", 11); m.Margin = new Thickness(12, 6, 12, 6); sp.Children.Add(m);
            pop = Panels.ShowFlyout(at, sp, 380);
        }

        // takes one; region: r = fractions of the visible area, or null = let the user drag it. Returns the result for tests.
        public static async Task<string> Take(BrowserWindow w, string mode, bool save, Rect? region)
        {
            var t = w.Active; var c = t == null ? null : t.Core;
            if (c == null || t.ReaderOn) { w.Toast(t != null && t.ReaderOn ? "Leave reader mode first, then take the screenshot." : "There's no page to take a picture of.", 4); return "error no page"; }
            byte[] png;
            try
            {
                if (mode == "full") png = await FullPage(c);
                else
                {
                    png = await Visible(c);
                    if (mode == "region")
                    {
                        var r = region ?? await Drag(w, t, png);
                        if (r == null || r.Value.Width <= 0.002 || r.Value.Height <= 0.002) return "cancelled";
                        png = Crop(png, r.Value);
                    }
                }
            }
            catch (Exception e) { w.Toast("The screenshot didn't work: " + e.Message, 6); return "error " + e.Message; }
            var px = PixelSize(png);
            if (save)
            {
                string path; try { path = Save(png); } catch (Exception e) { w.Toast("Couldn't save the screenshot: " + e.Message, 6); return "error " + e.Message; }
                w.Toast("Screenshot saved: Pictures\\Hallow Browser\\" + System.IO.Path.GetFileName(path), 5);
                return "saved|" + path + "|" + px.Width + "|" + px.Height;
            }
            try { Copy(png); } catch (Exception e) { w.Toast("Couldn't copy the screenshot: " + e.Message, 6); return "error " + e.Message; }
            w.Toast("Screenshot copied (" + px.Width + " x " + px.Height + ").", 3);
            return "copied|" + png.Length + "|" + px.Width + "|" + px.Height;
        }

        // a still of the page over it (a popup: WPF can't draw over the page itself); drag a box, Esc cancels
        static Task<Rect?> Drag(BrowserWindow w, Tab t, byte[] png)
        {
            var tcs = new TaskCompletionSource<Rect?>();
            var target = (FrameworkElement)t.View;
            double W = target.ActualWidth, H = target.ActualHeight;
            var g = new Grid { Width = W, Height = H, Cursor = Cursors.Cross, Focusable = true, Background = Brushes.Transparent };
            g.Children.Add(new Image { Source = Decode(png), Stretch = Stretch.Fill });
            var shade = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)), IsHitTestVisible = false };
            var box = new Rectangle { StrokeThickness = 2, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            box.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            var hint = Ui.Flyout(Ui.T("Drag over the part you want  -  Esc cancels", 12, true, "Text"), double.NaN);
            hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Top; hint.Margin = new Thickness(0, 14, 0, 0); hint.IsHitTestVisible = false;
            g.Children.Add(shade); g.Children.Add(box); g.Children.Add(hint);
            Action<Rect> draw = r =>
            {
                var all = new RectangleGeometry(new Rect(0, 0, W, H));
                shade.Data = r.Width > 0 ? (Geometry)new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(r)) : all;
                box.Visibility = r.Width > 0 ? Visibility.Visible : Visibility.Collapsed; box.Margin = new Thickness(r.X, r.Y, 0, 0); box.Width = r.Width; box.Height = r.Height;
            };
            draw(new Rect(0, 0, 0, 0));
            var pop = new Popup { Child = g, PlacementTarget = target, Placement = PlacementMode.Relative, AllowsTransparency = true, StaysOpen = true, PopupAnimation = PopupAnimation.None };
            Point start = new Point(); bool down = false; Rect sel = new Rect();
            Action<Rect?> finish = r => { if (tcs.Task.IsCompleted) return; pop.IsOpen = false; tcs.TrySetResult(r); };
            g.MouseLeftButtonDown += (a, b) => { start = b.GetPosition(g); down = true; g.CaptureMouse(); hint.Visibility = Visibility.Collapsed; };
            g.MouseMove += (a, b) => { if (!down) return; var p = b.GetPosition(g); sel = new Rect(new Point(Math.Max(0, Math.Min(W, start.X)), Math.Max(0, Math.Min(H, start.Y))), new Point(Math.Max(0, Math.Min(W, p.X)), Math.Max(0, Math.Min(H, p.Y)))); draw(sel); };
            g.MouseLeftButtonUp += (a, b) => { if (!down) return; down = false; g.ReleaseMouseCapture(); finish(sel.Width < 4 || sel.Height < 4 ? (Rect?)null : new Rect(sel.X / W, sel.Y / H, sel.Width / W, sel.Height / H)); };
            g.MouseRightButtonUp += (a, b) => finish(null);
            g.KeyDown += (a, b) => { if (b.Key == Key.Escape) finish(null); };
            pop.Closed += (a, b) => finish(null);
            pop.Opened += (a, b) => g.Focus();
            pop.IsOpen = true;
            return tcs.Task;
        }
    }
}
