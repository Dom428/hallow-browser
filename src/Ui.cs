// Hallow Browser - small helpers for building WPF in code (no XAML compiler): theme brushes, text, buttons, icons.
// Colours always come from the theme (Theme.xaml) by key, never written here, so another theme is just another dictionary.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HallowBrowser
{
    public static class Ui
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        public static Brush R(string key) { return (Brush)Application.Current.FindResource(key); }
        public static Style S(string key) { return (Style)Application.Current.FindResource(key); }
        public static void Bind(FrameworkElement e, DependencyProperty p, string key) { e.SetResourceReference(p, key); }
        public static void DarkTitle(Window w)
        {
            Look.Track(w);   // 1.1: dark or light title bar, like the browser (also when it changes live)
        }
        static ImageSource icon;
        public static ImageSource AppIcon()
        {
            if (icon != null) return icon;
            try
            {
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); icon.Freeze();
            }
            catch { }
            return icon;
        }
        public static TextBlock T(string text, double size = 12.5, bool bold = false, string fg = null, double bottom = 0)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            if (fg != null) t.SetResourceReference(TextBlock.ForegroundProperty, fg);
            return t;
        }
        public static TextBlock Muted(string text, double size = 12, double bottom = 0) { return T(text, size, false, "Muted", bottom); }
        public static TextBlock Label(string text) { var t = T(text.ToUpperInvariant(), 11, true, "Lilac", 8); t.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"); return t; }
        public static Path Icon(string name, double size, string fg = "Text")
        {
            var p = new Path { Data = Icons.Get(name), Stretch = Stretch.Uniform, Width = size, Height = size, SnapsToDevicePixels = true, VerticalAlignment = VerticalAlignment.Center };
            p.SetResourceReference(Shape.FillProperty, fg);
            return p;
        }
        public static FrameworkElement IconText(string icon, string text, double size = 12, string fg = "Text")
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) { var i = Icon(icon, size, fg); i.Margin = new Thickness(0, 0, 8, 0); sp.Children.Add(i); }
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
        public static Button Btn(string label, Action click, bool accent = false, string icon = null, string tip = null)
        {
            var b = new Button { Content = icon == null ? (object)label : IconText(icon, label, 12, accent ? "AccentBtnText" : "Text"), Style = S(accent ? "Accent" : "Btn"), Margin = new Thickness(0, 0, 8, 0) };
            if (tip != null) b.ToolTip = tip;
            if (click != null) b.Click += (s, e) => click();
            return b;
        }
        public static Button Tool(string icon, string tip, Action click, double size = 14)
        {
            var b = new Button { Content = Icon(icon, size), Style = S("Tool"), ToolTip = tip };
            if (click != null) b.Click += (s, e) => click();
            return b;
        }
        public static Button MenuRow(string icon, string text, string keys, Action click)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null) g.Children.Add(Icon(icon, 13, "Lilac"));
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1); g.Children.Add(t);
            if (keys != null) { var k = Muted(keys, 11); k.VerticalAlignment = VerticalAlignment.Center; k.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(k, 2); g.Children.Add(k); }
            var b = new Button { Content = g, Style = S("MenuRow") };
            if (click != null) b.Click += (s, e) => click();
            return b;
        }
        public static StackPanel Row(params UIElement[] items) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) p.Children.Add(i); return p; }
        public static Border Card(UIElement child, double pad = 18)
        {
            var b = new Border { Child = child, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(pad), Margin = new Thickness(0, 0, 0, 14) };
            b.SetResourceReference(Border.BackgroundProperty, "Panel"); b.SetResourceReference(Border.BorderBrushProperty, "Line");
            return b;
        }
        public static CheckBox Switch(string text, string sub, bool on, Action<bool> changed)
        {
            var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
            if (sub != null) sp.Children.Add(Muted(sub, 11.5));
            var c = new CheckBox { Content = sp, IsChecked = on, Style = S("Switch") };
            c.Click += (s, e) => changed(c.IsChecked == true);
            return c;
        }
        // a row of chips where one is picked
        public static WrapPanel Chips<T>(IList<KeyValuePair<T, string>> choices, T current, Action<T> picked)
        {
            var w = new WrapPanel(); var all = new List<ToggleButton>();
            foreach (var c in choices)
            {
                var val = c.Key;
                var tb = new ToggleButton { Content = c.Value, Style = S("Chip"), IsChecked = Equals(val, current) };
                tb.Click += (s, e) => { foreach (var o in all) o.IsChecked = o == tb; picked(val); };
                all.Add(tb); w.Children.Add(tb);
            }
            return w;
        }
        public static Border Flyout(UIElement child, double width)
        {
            var b = new Border { Child = child, Width = width, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6) };
            b.SetResourceReference(Border.BackgroundProperty, "Panel"); b.SetResourceReference(Border.BorderBrushProperty, "Line");
            return b;
        }
        public static Popup Pop(UIElement target, UIElement child, PlacementMode mode = PlacementMode.Bottom)
        {
            return new Popup { Child = child, PlacementTarget = target, Placement = mode, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, VerticalOffset = 4 };
        }
        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString("0.0") + " MB";
            return (bytes / 1073741824.0).ToString("0.00") + " GB";
        }
    }
}
