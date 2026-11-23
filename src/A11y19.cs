// Hallow Browser 1.9 - accessibility: every button, tab, menu row, sidebar entry and setting has a UI Automation name, help text and
// keyboard access; F6 cycles address bar / tabs / page / sidebar; flyouts take the keyboard when opened with it and give it back on
// Esc (no traps); a UI scale of 100-200% for the browser's own UI (following Windows' "Text size" by default, separate from page zoom);
// Windows' high-contrast themes switch the browser to the system colours; Windows' "Show animations" off = no motion anywhere.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public static class A11y
    {
        // ================================================================ what Windows says (tests may force each one - never by
        // changing the Windows setting itself)
        public static bool? ForceHighContrast, ForceReducedMotion; public static int? ForceTextScale = TestTextScale();
        // tests: HALLOW_BROWSER_TEXTSCALE stands in for Windows' text size (only with a test folder)
        static int? TestTextScale() { int v; return BrowserSettings.TestDir != null && int.TryParse(Environment.GetEnvironmentVariable("HALLOW_BROWSER_TEXTSCALE"), out v) ? (int?)v : null; }
        public static bool HighContrast { get { return ForceHighContrast ?? SystemParameters.HighContrast; } }
        public static bool ReducedMotion { get { return ForceReducedMotion ?? !SystemParameters.ClientAreaAnimation; } }
        // Windows' Settings > Accessibility > Text size (100-225%), read only
        public static int WindowsTextScale()
        {
            if (ForceTextScale.HasValue) return ForceTextScale.Value;
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility")) { var v = k == null ? null : k.GetValue("TextScaleFactor"); if (v != null) return Math.Max(100, Math.Min(225, Convert.ToInt32(v))); } } catch { }
            return 100;
        }
        public static readonly int[] ScaleChoices = { 0, 100, 125, 150, 175, 200 };   // 0 = follow Windows' text size
        public static double ScaleFor(int setting, int windowsPercent)
        {
            int p = setting == 0 ? windowsPercent : setting;
            return Math.Max(100, Math.Min(200, p)) / 100.0;
        }

        // ================================================================ UI scale: one frozen ScaleTransform in the application's
        // resources; the browser's own UI (title bar, toolbar, info / find bar, tab column, sidebar rail and panels, internal pages,
        // flyouts, tool tips, dialogs) uses it as a LayoutTransform. Pages (WebView2) are never inside it: page zoom stays its own.
        public const string ScaleKey = "UiScaleTransform";
        public static double Scale = 1;
        public static void SetScale(double s)
        {
            Scale = s; var t = new ScaleTransform(s, s); t.Freeze();
            if (Application.Current != null) Application.Current.Resources[ScaleKey] = t;
        }
        public static void Scaled(FrameworkElement e) { if (e != null) e.SetResourceReference(FrameworkElement.LayoutTransformProperty, ScaleKey); }
        public static readonly DependencyProperty NoScaleProperty = DependencyProperty.RegisterAttached("NoScale", typeof(bool), typeof(A11y), new PropertyMetadata(false));
        public static void SetNoScale(DependencyObject d, bool v) { d.SetValue(NoScaleProperty, v); }
        // panels whose children (other than pages) are scaled as they are added (the pages area, the sidebar's panel)
        public static readonly DependencyProperty ScaleHostProperty = DependencyProperty.RegisterAttached("ScaleHost", typeof(bool), typeof(A11y), new PropertyMetadata(false));
        public static void SetScaleHost(DependencyObject d, bool v) { d.SetValue(ScaleHostProperty, v); }

        // ================================================================ high contrast: the theme's colours become system colours
        static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static Dictionary<string, string> HcColors()
        {
            return new Dictionary<string, string> { { "window", Hex(SystemColors.WindowColor) }, { "text", Hex(SystemColors.WindowTextColor) }, { "hi", Hex(SystemColors.HighlightColor) }, { "hitext", Hex(SystemColors.HighlightTextColor) }, { "hot", Hex(SystemColors.HotTrackColor) } };
        }
        // by resource key (what the brush is for); keys not listed go by property / luminance below
        static string HcForKey(string key)
        {
            switch (key)
            {
                case "Bg": case "Strip": case "Bar": case "Panel": case "PanelHi": case "TabOn": case "TabHover": case "FieldBg": case "FieldFocus": case "Rail":
                case "AccentSoft": case "AccentGlow": case "AccentBtnBg": case "PageBg": return "window";
                case "AccentBrush": case "AccentLight": case "AccentBtnLine": case "CloseHover": case "BadgeBg": return "hi";
                case "OnAccent": return "hitext";
            }
            return key != null && (key.StartsWith("Group") || key == "Line" || key == "LineDim") ? "text" : null;
        }
        static double Lum(string hex)
        {
            var h = hex.TrimStart('#'); if (h.Length == 8) h = h.Substring(2);
            int r = Convert.ToInt32(h.Substring(0, 2), 16), g = Convert.ToInt32(h.Substring(2, 2), 16), b = Convert.ToInt32(h.Substring(4, 2), 16);
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        }
        static bool IsAccent(string hex) { var h = hex.TrimStart('#').ToUpperInvariant(); if (h.Length == 8) h = h.Substring(2); return h == "F07820" || h == "F59A50"; }
        static bool HasAlpha(string hex) { var h = hex.TrimStart('#'); return h.Length == 8 && !h.StartsWith("FF", StringComparison.OrdinalIgnoreCase); }
        // a colour by what it paints: fills (Background / Fill) vs lines and text (Foreground / BorderBrush / Stroke / Caret / Selection)
        static string HcForProperty(string prop, string hex)
        {
            var h = hex.TrimStart('#'); if (h.Length == 8 && h.StartsWith("00")) return null;   // transparent stays
            bool fill = prop == null || prop.EndsWith("Background") || prop.EndsWith("Fill") || prop == "Color";
            if (IsAccent(hex)) return HasAlpha(hex) ? (fill ? "window" : "hi") : "hi";
            if (fill) return Lum(hex) < 0.45 ? "window" : "text";
            return "text";
        }
        // Theme.xaml (before the accent is swapped in) with every colour replaced by a system colour
        public static string HighContrastXaml(string xaml)
        {
            var map = HcColors();
            var doc = new XmlDocument(); doc.LoadXml(xaml);
            var re = new Regex("^#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})$");
            Action<XmlNode> walk = null;
            walk = n =>
            {
                var el = n as XmlElement;
                if (el != null)
                {
                    string key = el.GetAttribute("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
                    if (el.LocalName == "Color" && re.IsMatch(el.InnerText.Trim())) { var k = HcForKey(key) ?? HcForProperty("Color", el.InnerText.Trim()); if (k != null) el.InnerText = map[k]; }
                    foreach (XmlAttribute a in el.Attributes.Cast<XmlAttribute>().ToList())
                    {
                        if (!re.IsMatch(a.Value)) continue;
                        string k = null;
                        if (el.LocalName == "SolidColorBrush" && a.LocalName == "Color") k = HcForKey(key) ?? HcForProperty(key != null && (key == "Text" || key == "Muted" || key == "Lilac" || key.EndsWith("Text") || key == "Green" || key == "Amber" || key == "Red") ? "Foreground" : "Background", a.Value);
                        else if (el.LocalName == "Setter" && a.LocalName == "Value") { var p = el.GetAttribute("Property"); k = HcForProperty(p.Contains(".") ? p.Substring(p.LastIndexOf('.') + 1) : p, a.Value); }
                        else k = HcForProperty(a.LocalName, a.Value);
                        if (k != null) a.Value = map[k];
                    }
                }
                foreach (XmlNode c in n.ChildNodes) walk(c);
            };
            walk(doc.DocumentElement);
            foreach (var cm in doc.SelectNodes("//comment()").Cast<XmlNode>().ToList()) cm.ParentNode.RemoveChild(cm);   // (the notes name the palette)
            // the focus outline: thicker in high contrast
            return doc.OuterXml.Replace("Margin=\"-2\" Stroke=", "Margin=\"-3\" Stroke=").Replace("StrokeThickness=\"2\" RadiusX=\"3\"", "StrokeThickness=\"3\" RadiusX=\"3\"");
        }

        // ================================================================ names, help text and keyboard for every control (class handlers)
        static bool installed;
        public static void Install()
        {
            if (installed) return; installed = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(NoFocusClick), true);
            EventManager.RegisterClassHandler(typeof(ButtonBase), UIElement.KeyDownEvent, new KeyEventHandler(MenuKey));
            SystemParameters.StaticPropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "HighContrast" && ForceHighContrast == null && Application.Current != null) Application.Current.Dispatcher.BeginInvoke(new Action(Program.ReloadTheme));
                if (e.PropertyName == "ClientAreaAnimation" && Application.Current != null) Application.Current.Dispatcher.BeginInvoke(new Action(MotionChanged));
            };
        }
        // 1.9: after the theme dictionary is replaced (light / dark, high contrast), controls already made get the new dictionary's
        // style of the same name (their templates hold colours of their own)
        public static int Restyle(ResourceDictionary old, ResourceDictionary nw, IEnumerable<DependencyObject> roots)
        {
            if (old == null || nw == null) return 0;
            var map = new Dictionary<Style, Style>();
            foreach (var k in old.Keys) { var a = old[k] as Style; var b = nw.Contains(k) ? nw[k] as Style : null; if (a != null && b != null && a != b) map[a] = b; }
            int n = 0; var seen = new HashSet<DependencyObject>();
            Action<DependencyObject> walk = null;
            walk = d =>
            {
                if (d == null || !seen.Add(d)) return;
                var fe = d as FrameworkElement; Style s;
                if (fe != null && fe.Style != null && map.TryGetValue(fe.Style, out s)) { fe.Style = s; n++; }
                if (d is Visual) { int c = VisualTreeHelper.GetChildrenCount(d); for (int i = 0; i < c; i++) walk(VisualTreeHelper.GetChild(d, i)); }
                foreach (var l in LogicalTreeHelper.GetChildren(d)) { var ld = l as DependencyObject; if (ld != null) walk(ld); }
            };
            foreach (var r in roots) walk(r);
            return n;
        }
        public static event Action MotionChangedEvent;
        public static void MotionChanged() { if (MotionChangedEvent != null) MotionChangedEvent(); }

        // marks a name we made (so a later Loaded may refresh it; a name set by the code itself is never touched)
        static readonly DependencyProperty AutoProperty = DependencyProperty.RegisterAttached("Auto", typeof(bool), typeof(A11y), new PropertyMetadata(false));
        static readonly DependencyProperty NoFocusProperty = DependencyProperty.RegisterAttached("NoFocus", typeof(bool), typeof(A11y), new PropertyMetadata(false));
        static readonly DependencyProperty PopHookedProperty = DependencyProperty.RegisterAttached("PopHooked", typeof(bool), typeof(A11y), new PropertyMetadata(false));

        // WPF raises Loaded only on elements with a handler of their own, so a class handler never sees most controls: instead every
        // window and open flyout (PresentationSource.CurrentSources) is walked after layout settles, at idle - only when the UI changed
        static bool scheduled; static readonly DependencyProperty PopSeenProperty = DependencyProperty.RegisterAttached("PopSeen", typeof(bool), typeof(A11y), new PropertyMetadata(false));
        public static int Walks;
        public static void Schedule(Dispatcher d)
        {
            if (scheduled || d == null) return; scheduled = true;
            d.BeginInvoke(new Action(() => { scheduled = false; ApplyAll(); }), DispatcherPriority.ApplicationIdle);
        }
        public static void ApplyAll()
        {
            Walks++;
            foreach (PresentationSource ps in PresentationSource.CurrentSources.OfType<PresentationSource>().ToList())
            {
                try { if (ps.IsDisposed || ps.RootVisual == null) continue; WalkTree(ps.RootVisual, 0); } catch { }
            }
        }
        static void WalkTree(DependencyObject d, int depth)
        {
            if (depth > 200) return;
            var fe = d as FrameworkElement;
            if (fe != null) { Visit(fe); if (fe is WebView2 || fe is HwndHost) return; }
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) WalkTree(VisualTreeHelper.GetChild(d, i), depth + 1);
        }
        static void Visit(FrameworkElement fe)
        {
            try
            {
                var parent = VisualTreeHelper.GetParent(fe) as FrameworkElement;
                if (parent != null && (bool)parent.GetValue(ScaleHostProperty) && !(fe is WebView2) && !(fe is HwndHost) && !(bool)fe.GetValue(NoScaleProperty) && fe.ReadLocalValue(FrameworkElement.LayoutTransformProperty) == DependencyProperty.UnsetValue) Scaled(fe);
                var pop = fe.Parent as Popup;
                if (pop != null && pop.Child == fe && pop.IsOpen && !(bool)pop.GetValue(PopSeenProperty))   /* (its PopupRoot names the Popup as parent too) */ { pop.SetValue(PopSeenProperty, true); Flyout(pop, fe); }
                var rb = fe as RepeatButton;   // a scroll bar's page areas
                if (rb != null && fe.TemplatedParent is Slider && string.IsNullOrEmpty(AutomationProperties.GetName(rb)))
                {
                    bool dec = (rb.Name ?? "").Contains("Decrease") || rb.Command == Slider.DecreaseLarge; var lbl = AutomationProperties.GetName((Slider)fe.TemplatedParent);
                    AutomationProperties.SetName(rb, (dec ? "Lower" : "Higher") + (string.IsNullOrEmpty(lbl) ? "" : " - " + lbl)); AutomationProperties.SetHelpText(rb, "Moves the slider a step"); return;
                }
                if (rb != null && fe.TemplatedParent is ScrollBar && string.IsNullOrEmpty(AutomationProperties.GetName(rb)))
                {
                    var sb = (ScrollBar)fe.TemplatedParent; bool dec = (rb.Command == ScrollBar.PageUpCommand || rb.Command == ScrollBar.PageLeftCommand || (rb.Name ?? "").Contains("Decrease"));
                    AutomationProperties.SetName(rb, sb.Orientation == Orientation.Vertical ? (dec ? "Page up" : "Page down") : (dec ? "Page left" : "Page right"));
                    AutomationProperties.SetHelpText(rb, "Scrolls a page"); return;
                }
                if (fe is ButtonBase) { Name((ButtonBase)fe); return; }
                if (fe is TextBox || fe is PasswordBox || fe is ComboBox || fe is Slider) { NameField((Control)fe); return; }
                var w = fe as Window; if (w != null && !(w is BrowserWindow) && w.Content is FrameworkElement && w.Content != null && !(w.Content is WebView2)) { var c = (FrameworkElement)w.Content; if (c.ReadLocalValue(FrameworkElement.LayoutTransformProperty) == DependencyProperty.UnsetValue && !ContainsPage(c)) Scaled(c); }
            }
            catch (Exception ex) { LastFlyoutError = fe.GetType().Name + ": " + ex.Message; }
        }
        static bool ContainsPage(DependencyObject d)
        {
            if (d is WebView2 || d is HwndHost) return true;
            int n = VisualTreeHelper.GetChildrenCount(d); for (int i = 0; i < n; i++) if (ContainsPage(VisualTreeHelper.GetChild(d, i))) return true;
            return false;
        }

        // ---- the words of an element's content (TextBlocks and strings, in order)
        public static List<string> Texts(object o)
        {
            var r = new List<string>(); Collect(o, r, 0); return r;
        }
        static void Collect(object o, List<string> r, int depth)
        {
            if (o == null || depth > 10) return;
            var s = o as string; if (s != null) { if (s.Trim().Length > 0) r.Add(s.Trim()); return; }
            var tb = o as TextBlock; if (tb != null) { var t = tb.Text; if (string.IsNullOrWhiteSpace(t)) t = string.Concat(tb.Inlines.OfType<System.Windows.Documents.Run>().Select(x => x.Text)); if (!string.IsNullOrWhiteSpace(t) && tb.Visibility != Visibility.Collapsed) r.Add(t.Trim()); return; }
            var cc = o as ContentControl; if (cc != null) { Collect(cc.Content, r, depth + 1); return; }
            var dec = o as Decorator; if (dec != null) { Collect(dec.Child, r, depth + 1); return; }
            var p = o as Panel; if (p != null) { foreach (var c in p.Children) Collect(c, r, depth + 1); return; }
        }
        // "New tab (Ctrl+T)" -> name "New tab", key "Ctrl+T"; "Hallow Shield - this site, levels" -> "Hallow Shield"; first line only
        public static string NamePart(string tip)
        {
            if (string.IsNullOrWhiteSpace(tip)) return null;
            var s = tip.Split('\n')[0].Trim();
            var m = Regex.Match(s, @"^(.*?)\s*\((?:Ctrl|Alt|Shift|F\d+|Esc|Enter)[^)]*\)\s*$"); if (m.Success && m.Groups[1].Value.Length > 0) s = m.Groups[1].Value;
            var dash = s.IndexOf(" - "); if (dash >= 3) s = s.Substring(0, dash);
            return s.Trim();
        }
        public static string KeyPart(string tip)
        {
            if (string.IsNullOrWhiteSpace(tip)) return null;
            var m = Regex.Match(tip.Split('\n')[0], @"\(((?:Ctrl|Alt|Shift|F\d+|Esc|Enter)[^)]*)\)"); return m.Success ? m.Groups[1].Value : null;
        }
        class TipConv : IValueConverter
        {
            readonly int part; public TipConv(int p) { part = p; }
            public object Convert(object v, Type t, object p, CultureInfo c) { var s = v as string; if (s == null) return ""; return part == 0 ? (NamePart(s) ?? "") : part == 1 ? s.Replace("\n", ". ") : (KeyPart(s) ?? ""); }
            public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
        }
        static readonly TipConv NameConv = new TipConv(0), HelpConv = new TipConv(1), KeyConv = new TipConv(2);
        static bool Settable(DependencyObject d, DependencyProperty p) { return (bool)d.GetValue(AutoProperty) || string.IsNullOrEmpty((string)d.GetValue(p)); }

        static void Name(ButtonBase b)
        {
            var app = Application.Current;
            if (app != null && b.Style != null && (b.Style == app.TryFindResource("Tool") || b.Style == app.TryFindResource("Caption"))) b.SetValue(NoFocusProperty, true);
            if ((bool)b.GetValue(AutoProperty) && BindingOperations.IsDataBound(b, AutomationProperties.HelpTextProperty)) return;   // done (bound to its tool tip)
            bool explicitName = !(bool)b.GetValue(AutoProperty) && !string.IsNullOrEmpty(AutomationProperties.GetName(b));
            b.SetValue(AutoProperty, true);
            var texts = Texts(b.Content);
            if (b.ToolTip is string)
            {
                // the tool tip may change (Mute / Unmute, Maximise / Restore): bound, so the name follows it
                if (!explicitName && (texts.Count == 0 || b.Style == (app == null ? null : app.TryFindResource("Tool")))) b.SetBinding(AutomationProperties.NameProperty, new Binding("ToolTip") { Source = b, Converter = NameConv });
                else if (!explicitName) AutomationProperties.SetName(b, texts[0]);
                b.SetBinding(AutomationProperties.HelpTextProperty, new Binding("ToolTip") { Source = b, Converter = HelpConv });
                b.SetBinding(AutomationProperties.AcceleratorKeyProperty, new Binding("ToolTip") { Source = b, Converter = KeyConv });
                return;
            }
            var cb = b as CheckBox;
            string name = texts.Count > 0 ? texts[0] : null, help = texts.Count > 1 ? string.Join(". ", texts.Skip(1)) : null;
            if (b.Style != null && app != null && b.Style == app.TryFindResource("MenuRow") && texts.Count > 1) { AutomationProperties.SetAcceleratorKey(b, texts[texts.Count - 1]); help = null; }
            // a chip in a row of choices: the text over the row says what it is for
            if (b is ToggleButton && cb == null) { var label = LabelBefore(b.Parent as FrameworkElement); if (label != null) help = label + ": " + (name ?? ""); }
            if (name == null) name = b.Tag as string;
            if (!explicitName && name != null) AutomationProperties.SetName(b, name);
            var nm = AutomationProperties.GetName(b);
            AutomationProperties.SetHelpText(b, help ?? (cb != null ? (nm + (cb.IsChecked == true ? " - on" : " - off")) : nm) ?? "");
        }
        // the short text just before a control (or its row) in the same panel
        static string LabelBefore(FrameworkElement e)
        {
            for (int up = 0; e != null && up < 2; up++, e = e.Parent as FrameworkElement)
            {
                var p = e.Parent as Panel; if (p == null) continue;
                int i = p.Children.IndexOf(e);
                for (int j = i - 1; j >= 0 && j >= i - 2; j--) { var tb = p.Children[j] as TextBlock; if (tb != null && !string.IsNullOrWhiteSpace(tb.Text) && tb.Text.Length <= 140) return tb.Text.Trim(); }
            }
            return null;
        }
        static void NameField(Control c)
        {
            if (!Settable(c, AutomationProperties.NameProperty)) { if (string.IsNullOrEmpty(AutomationProperties.GetHelpText(c))) AutomationProperties.SetHelpText(c, AutomationProperties.GetName(c)); return; }
            c.SetValue(AutoProperty, true);
            var tip = c.ToolTip as string;
            var name = (c is Slider ? LabelBefore(c) ?? NamePart(tip) : NamePart(tip) ?? LabelBefore(c)) ?? (c is ComboBox ? "Choice" : c is Slider ? "Slider" : "Text field");   // a slider's tool tip is its value
            AutomationProperties.SetName(c, name);
            AutomationProperties.SetHelpText(c, tip ?? name);
        }

        // ---- the toolbar's icon buttons take the keyboard (Tab, Enter / Space) but a mouse click doesn't move the focus (as before)
        static void NoFocusClick(object sender, MouseButtonEventArgs e)
        {
            var b = sender as ButtonBase; if (b == null || !(bool)b.GetValue(NoFocusProperty) || b.IsKeyboardFocused || !b.Focusable) return;
            b.Focusable = false; b.Dispatcher.BeginInvoke(new Action(() => b.Focusable = true), DispatcherPriority.Input);
        }
        // the menu key or Shift+F10 on a button = its right-click menu (sidebar entries, tab-strip buttons)
        static void MenuKey(object sender, KeyEventArgs e)
        {
            var b = sender as ButtonBase; if (b == null || e.Handled) return;
            if (e.Key == Key.Apps || (e.Key == Key.System && e.SystemKey == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) || (e.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonUpEvent, Source = b };
                b.RaiseEvent(args); e.Handled = true;
            }
        }

        // ---- flyouts: opened with the keyboard = the keyboard goes in; Tab / arrows cycle inside; Esc closes and goes back to the
        // button that opened it (nothing traps the keyboard). Prompts that stay open (save a login, a site's permission) aren't entered
        // by themselves - F6 reaches them.
        public static Popup LastFlyout; public static string LastFlyoutError;
        public static bool Seen(Popup p) { return (bool)p.GetValue(PopSeenProperty); }
        public static FrameworkElement LastFlyoutChild;
        static void Flyout(Popup p, FrameworkElement child)
        {
            LastFlyoutChild = child;
            if (child.ReadLocalValue(FrameworkElement.LayoutTransformProperty) == DependencyProperty.UnsetValue) Scaled(child);   // the UI scale
            KeyboardNavigation.SetTabNavigation(child, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(child, KeyboardNavigationMode.Cycle);
            if (ReducedMotion) p.PopupAnimation = PopupAnimation.None;
            if (!(bool)p.GetValue(PopHookedProperty))
            {
                p.SetValue(PopHookedProperty, true);
                child.PreviewKeyDown += (a, b) =>
                {
                    if (b.Key != Key.Escape || b.Handled) return;
                    b.Handled = true; bool had = child.IsKeyboardFocusWithin; p.IsOpen = false;
                    var t = p.PlacementTarget as UIElement; if (had && t != null && t.Focusable && t.IsVisible) t.Focus();
                };
                p.Closed += (a, b) => { p.SetValue(PopSeenProperty, false); var t = p.PlacementTarget as UIElement; if (child.IsKeyboardFocusWithin && t != null && t.Focusable && t.IsVisible) t.Focus(); };
            }
            if (!p.StaysOpen) { LastFlyout = p; if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) child.Dispatcher.BeginInvoke(new Action(() => { if (p.IsOpen && !child.IsKeyboardFocusWithin) FocusFirst(child); }), DispatcherPriority.Input); }
        }
        public static bool FocusFirst(DependencyObject root)
        {
            var f = FirstFocusable(root); if (f == null) return false;
            f.Focus(); return true;
        }
        public static UIElement FirstFocusable(DependencyObject root)
        {
            var ue = root as UIElement;
            if (ue != null && (ue.Visibility != Visibility.Visible || !ue.IsEnabled)) return null;
            if (ue != null && ue.Focusable && KeyboardNavigation.GetIsTabStop(ue) && !(ue is ScrollViewer)) return ue;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) { var r = FirstFocusable(VisualTreeHelper.GetChild(root, i)); if (r != null) return r; }
            return null;
        }
        // every keyboard stop under an element, in the order Tab visits them (visual tree order; no TabIndex is used anywhere)
        public static List<UIElement> TabStops(DependencyObject root, List<UIElement> into = null)
        {
            into = into ?? new List<UIElement>();
            var ue = root as UIElement;
            if (ue != null && (ue.Visibility != Visibility.Visible || !ue.IsEnabled)) return into;
            if (ue != null && ue.Focusable && KeyboardNavigation.GetIsTabStop(ue) && !(ue is ScrollViewer) && !(ue is Popup)) into.Add(ue);
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) TabStops(VisualTreeHelper.GetChild(root, i), into);
            return into;
        }
    }

    // ================================================================ a tab in the strip / column: a keyboard stop and a UIA TabItem
    public class TabHeader : Border
    {
        public readonly Tab T; readonly BrowserWindow W;
        public TabHeader(Tab t, BrowserWindow w) { T = t; W = w; Focusable = true; KeyboardNavigation.SetIsTabStop(this, true); }
        protected override AutomationPeer OnCreateAutomationPeer() { return new TabHeaderPeer(this); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e); if (e.Handled) return;
            var mods = Keyboard.Modifiers; var k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k == Key.Enter || k == Key.Space) { W.Activate(T); e.Handled = true; }
            else if (k == Key.Delete) { W.KeyboardClose(T); e.Handled = true; }
            else if (k == Key.Apps || (k == Key.F10 && (mods & ModifierKeys.Shift) != 0)) { Panels.TabMenu(W, T); e.Handled = true; }
            else if (k == Key.Left || k == Key.Up) { W.FocusTabHeader(T, -1); e.Handled = true; }
            else if (k == Key.Right || k == Key.Down) { W.FocusTabHeader(T, 1); e.Handled = true; }
            else if (k == Key.Home) { W.FocusTabHeader(null, int.MinValue); e.Handled = true; }
            else if (k == Key.End) { W.FocusTabHeader(null, int.MaxValue); e.Handled = true; }
        }
        public BrowserWindow Window { get { return W; } }
    }
    public class TabHeaderPeer : FrameworkElementAutomationPeer, ISelectionItemProvider
    {
        readonly TabHeader h;
        public TabHeaderPeer(TabHeader owner) : base(owner) { h = owner; }
        protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.TabItem; }
        protected override string GetClassNameCore() { return "HallowTab"; }
        protected override string GetNameCore()
        {
            var t = h.T; var n = string.IsNullOrEmpty(t.Title) ? "Tab" : t.Title;
            if (t.Audible) n += t.Muted ? " (muted)" : " (playing)"; else if (t.Muted) n += " (muted)";
            if (t.Suspended || t.Discarded) n += " (asleep)";
            return n;
        }
        protected override string GetHelpTextCore()
        {
            var w = h.Window; int i = w.Tabs.IndexOf(h.T);
            return "Tab " + (i + 1) + " of " + w.Tabs.Count + ". Enter shows it, Delete closes it, the arrow keys move between tabs, Shift+F10 opens its menu.";
        }
        protected override bool IsKeyboardFocusableCore() { return true; }
        protected override bool IsControlElementCore() { return true; }
        protected override bool IsContentElementCore() { return true; }
        public override object GetPattern(PatternInterface p) { return p == PatternInterface.SelectionItem ? this : base.GetPattern(p); }
        public void Select() { h.Window.Activate(h.T); }
        public void AddToSelection() { Select(); }
        public void RemoveFromSelection() { throw new InvalidOperationException("one tab is always shown"); }
        public bool IsSelected { get { return h.Window.Active == h.T; } }
        public IRawElementProviderSimple SelectionContainer { get { return null; } }
    }

    public partial class BrowserSettings
    {
        [DataMember] public int UiScale;   // 1.9: the browser's own UI, 100-200%; 0 = like Windows' text size (the default)
        [DataMember] public bool GameChip = true;          // 1.9: the small controller icon in the title bar while game mode is on
        [DataMember] public bool BookmarksBarHidden;       // 1.9: the bookmarks bar was hidden on purpose (then it never comes back by itself)
        [DataMember] public bool GroupTipShown;            // 1.9: the one-time tip about tab groups
        void Defaults19() { UiScale = 0; GameChip = true; BookmarksBarHidden = false; GroupTipShown = false; }
        void Fix19() { if (Array.IndexOf(A11y.ScaleChoices, UiScale) < 0) UiScale = 0; }
    }

    public partial class BrowserWindow
    {
        // ================================================================ F6 / Shift+F6: address bar -> tabs -> page -> sidebar -> ...
        public static readonly string[] Regions = { "address", "tabs", "page", "sidebar" };
        public string LastRegion; public UIElement LastRegionTarget;
        static bool Under(DependencyObject d, DependencyObject root)
        {
            for (int i = 0; d != null && i < 200; i++) { if (d == root) return true; var p = (d is Visual || d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : null; d = p ?? LogicalTreeHelper.GetParent(d); }
            return false;
        }
        public string RegionOf(DependencyObject f)
        {
            if (f == null) return null;
            if (Under(f, toolbarRow)) return "address";
            if (Under(f, topRow) || (vtabs != null && Under(f, vtabs))) return "tabs";
            if (Under(f, content)) return "page";
            if (Side != null && Side.Contains(f)) return "sidebar";
            return null;
        }
        public string CurrentRegion()
        {
            var f = Keyboard.FocusedElement as DependencyObject;
            if (f != null && Under(f, this)) return RegionOf(f) ?? LastRegion;
            // the keyboard isn't in this window (not active - e.g. the tests' off-screen window): where F6 last went
            return LastRegion ?? RegionOf(FocusManager.GetFocusedElement(this) as DependencyObject);
        }
        public void CycleRegion(int dir)
        {
            var cur = CurrentRegion(); int i = Array.IndexOf(Regions, cur);
            for (int n = 0; n < Regions.Length; n++)
            {
                i = ((i < 0 ? (dir > 0 ? -1 : 0) : i) + dir + Regions.Length) % Regions.Length;
                var target = RegionTarget(Regions[i]);
                if (target == null) continue;
                LastRegion = Regions[i]; LastRegionTarget = target;
                if (target == Address) FocusAddress(); else target.Focus();
                return;
            }
        }
        UIElement RegionTarget(string r)
        {
            switch (r)
            {
                case "address": return toolbarRow.IsVisible || BrowserSettings.Offscreen ? Address : null;
                case "tabs": return Active != null && Active.Header != null && Active.Header.Visibility == Visibility.Visible ? Active.Header : null;
                case "page":
                    if (Active == null) return null;
                    if (Active.View != null && !Active.PoppedOut && Active.View.Visibility == Visibility.Visible) return Active.View;
                    if (Active.Page != null) return A11y.FirstFocusable(Active.Page) ?? Active.Page;
                    return null;
                case "sidebar": return Side != null ? Side.KeyboardTarget() : null;
            }
            return null;
        }
        public void FocusTabHeader(Tab from, int d)
        {
            var list = Tabs.Where(t => t.Header != null && t.Header.Visibility == Visibility.Visible).ToList(); if (list.Count == 0) return;
            int i = from == null ? 0 : list.IndexOf(from);
            int j = d == int.MinValue ? 0 : d == int.MaxValue ? list.Count - 1 : Math.Max(0, Math.Min(list.Count - 1, i + d));
            list[j].Header.Focus(); LastRegion = "tabs"; LastRegionTarget = list[j].Header;
        }
        public void KeyboardClose(Tab t)
        {
            int i = Tabs.IndexOf(t); CloseTab(t);
            if (Tabs.Count > 0) { var n = Tabs[Math.Min(Math.Max(0, i), Tabs.Count - 1)]; if (n.Header != null) n.Header.Focus(); }
        }

        // ================================================================ Settings > Appearance > Accessibility
        public UIElement[] AccessibilitySettings()
        {
            var wts = A11y.WindowsTextScale();
            return new UIElement[] {
                Ui.Muted("Size of the browser's own text and controls (tabs, toolbar, menus, the sidebar, Settings). Pages keep their own zoom (Ctrl + / Ctrl -).", 12, 6),
                Ui.Chips(A11y.ScaleChoices.Select(v => new KeyValuePair<int, string>(v, v == 0 ? "Like Windows (" + wts + "%)" : v + "%")).ToList(), S.UiScale, v => { S.UiScale = v; S.Save(); Program.ApplyScale(S); }),
                Ui.Muted("Like Windows = Windows Settings > Accessibility > Text size.", 11.5, 10),
                Ui.Muted("High contrast: " + (A11y.HighContrast ? "on - the browser uses your contrast theme's colours." : "follows Windows (Settings > Accessibility > Contrast themes) - the browser switches to its colours by itself."), 12, 4),
                Ui.Muted("Animations: follow Windows' \"Animation effects\" (Settings > Accessibility > Visual effects) - " + (A11y.ReducedMotion ? "off now, so nothing in the browser moves." : "on now.") + " Pages are told too (prefers-reduced-motion).", 12, 4),
                Ui.Muted("Keyboard: F6 / Shift+F6 move between the address bar, the tabs, the page and the sidebar; Tab moves within; the arrow keys move between tabs; Enter or Space presses; Esc closes a menu; Shift+F10 opens a tab's or a sidebar entry's menu.", 11.5, 0) };
        }

        // ================================================================ UI scale
        public void ApplyUiScale()
        {
            var wc = WindowChrome.GetWindowChrome(this);
            if (wc != null) wc.CaptionHeight = 38 * A11y.Scale;
            FitTabs();
        }
        // ================================================================ high contrast: the active tab is outlined (its fill is the
        // window colour like the others)
        void HighContrastTab(Tab x, bool on)
        {
            if (x.Header == null) return;
            if (A11y.HighContrast) { x.Header.BorderThickness = on ? new Thickness(2, 2, 2, 0) : new Thickness(0); x.Header.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); }
            else if (x.Header.BorderThickness != new Thickness(0)) x.Header.BorderThickness = new Thickness(0);
        }
    }
}
