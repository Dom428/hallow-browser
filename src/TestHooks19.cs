// Hallow Browser 1.9 - developer test commands (only with HALLOW_BROWSER_TESTDIR, like the others): F6 regions, the UI scale, high
// contrast and reduced motion (forced in the browser - the Windows settings themselves are never changed), Tab order in Settings,
// flyouts opened and left with the keyboard, and the memory / game-mode counters.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static string A19(BrowserWindow w, string cmd, string arg)
        {
            Program.OnUi(w, () => { A11y.ApplyAll(); return "ok"; });   // what the idle walk does, now (deterministic for the checks)
            switch (cmd)
            {
                case "f6":   // F6 / Shift+F6 ("back"): the handler (the Shift state of an inactive off-screen window can't be trusted)
                    return Program.OnUi(w, () => { w.CycleRegion(arg == "back" ? -1 : 1); return "ok " + js.Serialize(w.RegionState()); });
                case "f6key":   // the real key path: the window's PreviewKeyDown takes F6
                    return Program.OnUi(w, () => { var src = PresentationSource.FromVisual(w); var e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, Key.F6) { RoutedEvent = Keyboard.PreviewKeyDownEvent }; w.RaiseEvent(e); return "ok " + js.Serialize(new Dictionary<string, object> { { "handled", e.Handled }, { "region", w.LastRegion } }); });
                case "region": return Program.OnUi(w, () => "ok " + js.Serialize(w.RegionState()));
                case "scale":   // <0|100..200> the setting
                    return Program.OnUi(w, () => { int v; if (int.TryParse(arg, out v)) { w.S.UiScale = v; w.S.Save(); Program.ApplyScale(w.S); } w.UpdateLayout(); return "ok " + js.Serialize(w.ScaleState()); });
                case "textscale":   // stands in for Windows' text size (100-225)
                    return Program.OnUi(w, () => { int v; A11y.ForceTextScale = int.TryParse(arg, out v) ? (int?)v : null; Program.ApplyScale(w.S); w.UpdateLayout(); return "ok " + js.Serialize(w.ScaleState()); });
                case "hc":   // on | off | system
                    return Program.OnUi(w, () => { A11y.ForceHighContrast = arg == "on" ? true : arg == "off" ? (bool?)false : null; Program.ReloadTheme(); w.UpdateLayout(); return "ok " + js.Serialize(w.ContrastState()); });
                case "motion":   // reduce | full | system
                    return Program.OnUi(w, () => { A11y.ForceReducedMotion = arg == "reduce" ? true : arg == "full" ? (bool?)false : null; A11y.MotionChanged(); return "ok " + js.Serialize(new Dictionary<string, object> { { "reduced", A11y.ReducedMotion }, { "system", !SystemParameters.ClientAreaAnimation } }); });
                case "taborder": return Program.OnUi(w, () => "ok " + js.Serialize(w.SettingsTabOrder()));
                case "open":   // menu | quick | tabmenu | railmenu | downloads | workspaces - a flyout, opened as if by the keyboard
                    return Program.OnUi(w, () => w.OpenFlyout(arg));
                case "flyout-esc":   // Esc inside the open flyout: closed, and the keyboard back on its button
                    return Program.OnUi(w, () => w.FlyoutEsc());
                case "toolbar": return Program.OnUi(w, () => "ok " + js.Serialize(w.ToolbarKeys()));
                case "names": return Program.OnUi(w, () => "ok " + js.Serialize(w.Unnamed()));
                // ---- real-use fixes
                case "plus": return Program.OnUi(w, () => { w.UpdateLayout(); return "ok " + js.Serialize(w.PlusState()); });
                case "chip": return Program.OnUi(w, () => { if (arg == "on" || arg == "off") { w.S.GameChip = arg == "on"; w.S.Save(); } w.Tick(true); return "ok " + js.Serialize(new Dictionary<string, object> { { "game", w.Game }, { "shown", w.GameChipShown }, { "setting", w.S.GameChip } }); });
                case "bar": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "on", w.S.BookmarksBar }, { "hidden", w.S.BookmarksBarHidden }, { "shown", w.BookmarksBarShown }, { "count", w.Bookmarks == null ? -1 : w.Bookmarks.Count } }));
                case "flyout-names": return Program.OnUi(w, () => { var p = A11y.LastFlyout; if (p == null || !p.IsOpen) return "error no flyout"; var c = p.Child as FrameworkElement; var names = A11y.TabStops(c).OfType<FrameworkElement>().Select(x => AutomationProperties.GetName(x)).ToList(); p.IsOpen = false; return "ok " + js.Serialize(names); });
                case "autogroup": return Program.OnUi(w, () => { var n = w.AutoGroupBySite(); return "ok " + js.Serialize(new Dictionary<string, object> { { "made", n }, { "groups", w.Groups.Select(g => g.Name + ":" + w.Tabs.Count(t => t.Group == g)).ToList() } }); });
                case "grouptip": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "shown", w.GroupTipWasShown }, { "toast", w.LastToast } }));
            }
            return "error unknown command";
        }
    }

    public partial class BrowserWindow
    {
        void DoEvents() { var fr = new System.Windows.Threading.DispatcherFrame(); Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => fr.Continue = false)); System.Windows.Threading.Dispatcher.PushFrame(fr); }
        static string Describe(object o)
        {
            var e = o as FrameworkElement; if (e == null) return o == null ? null : o.GetType().Name;
            var n = AutomationProperties.GetName(e);
            return e.GetType().Name + (string.IsNullOrEmpty(n) ? "" : ":" + n);
        }
        public Dictionary<string, object> RegionState()
        {
            var f = Keyboard.FocusedElement;
            return new Dictionary<string, object> { { "region", LastRegion }, { "current", CurrentRegion() }, { "target", Describe(LastRegionTarget) }, { "targetFocused", LastRegionTarget != null && LastRegionTarget.IsKeyboardFocusWithin },
                { "logical", Describe(FocusManager.GetFocusedElement(this)) }, { "keyboard", Describe(f) }, { "sidebarOn", S.SidebarOn } };
        }
        static double Rendered(FrameworkElement e, Visual root, bool height) { try { var r = e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)); return Math.Round(height ? r.Height : r.Width, 1); } catch { return -1; } }
        public Dictionary<string, object> ScaleState()
        {
            var wc = WindowChrome.GetWindowChrome(this);
            var v = Active != null ? Active.View : null; double zoom = -1; try { if (v != null) zoom = v.ZoomFactor; } catch { }
            return new Dictionary<string, object> { { "scale", A11y.Scale }, { "setting", S.UiScale }, { "windows", A11y.WindowsTextScale() }, { "caption", wc == null ? -1 : wc.CaptionHeight },
                { "topRow", Rendered(topRow, this, true) }, { "address", Rendered(addressBox, this, true) }, { "menuBtn", Rendered(menuBtn, this, true) },
                { "contentWidth", Rendered(content, this, false) }, { "pageWidth", v == null ? -1 : Rendered(v, this, false) }, { "zoom", zoom }, { "pageTransform", v == null ? null : v.LayoutTransform.ToString() } };
        }
        public Dictionary<string, object> ContrastState()
        {
            Func<string, string> col = k => { try { return ((SolidColorBrush)Application.Current.FindResource(k)).Color.ToString(); } catch { return null; } };
            var b = Ui.Tool("plus", "probe", null); var tmplBg = "";
            try { var st = (Style)Application.Current.FindResource("Btn"); foreach (var s in st.Setters.OfType<Setter>()) if (s.Property == Control.BackgroundProperty) tmplBg = s.Value.ToString(); } catch { }
            var on = Active != null && Active.Header != null ? Active.Header.BorderThickness.Top : -1;
            return new Dictionary<string, object> { { "hc", A11y.HighContrast }, { "theme", Program.ThemeIsHighContrast }, { "bg", col("Bg") }, { "text", col("Text") }, { "muted", col("Muted") }, { "accent", col("AccentBrush") }, { "line", col("Line") },
                { "btnBg", tmplBg }, { "activeTabOutline", on }, { "menuStyleIsCurrent", menuBtn.Style == Application.Current.FindResource("Tool") },
                { "window", SystemColors.WindowColor.ToString() }, { "windowText", SystemColors.WindowTextColor.ToString() }, { "highlight", SystemColors.HighlightColor.ToString() } };
        }
        public object SettingsTabOrder()
        {
            var t = Tabs.FirstOrDefault(x => x.Url == Omni.SettingsUrl);
            if (t == null) { t = NewTab(Omni.SettingsUrl, true); }
            Activate(t); UpdateLayout();
            var stops = A11y.TabStops(t.Page);
            var res = new List<Dictionary<string, object>>();
            foreach (var s in stops.OfType<FrameworkElement>())
            {
                Point p; try { p = s.TransformToAncestor(t.Page).Transform(new Point(0, 0)); } catch { continue; }
                res.Add(new Dictionary<string, object> { { "type", s.GetType().Name }, { "name", AutomationProperties.GetName(s) }, { "x", Math.Round(p.X) }, { "y", Math.Round(p.Y + s.ActualHeight / 2) } });   // y = the middle of the control (rows of mixed heights)
            }
            return res;
        }
        public string OpenFlyout(string which)
        {
            A11y.LastFlyout = null;
            switch (which)
            {
                case "menu": Panels.Menu(this, menuBtn); break;
                case "quick": Panels.Quick(this, quickBtn); break;
                case "downloads": Panels.DownloadsFlyout(this, dlBtn); break;
                case "tabmenu": if (Active != null) Panels.TabMenu(this, Active); break;
                case "workspaces": ShowWorkspaces(false); break;
                case "railmenu": { var p = Side.Panels.FirstOrDefault(x => x.RailBtn != null && !x.Transient); if (p == null) return "error no panel"; var rb = p.RailBtn; rb.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(rb), 0, Key.Apps) { RoutedEvent = Keyboard.KeyDownEvent }); break; }
                default: return "error unknown flyout";
            }
            DoEvents(); A11y.ApplyAll();
            var pop = A11y.LastFlyout; if (pop == null || !pop.IsOpen) return "error no flyout open";
            pop.UpdateLayout(); var child = pop.Child as FrameworkElement;
            bool focused = child != null && A11y.FocusFirst(child);
            var stops = child == null ? new List<UIElement>() : A11y.TabStops(child);
            return "ok " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "open", pop.IsOpen }, { "stops", stops.Count }, { "named", stops.OfType<FrameworkElement>().Count(s => !string.IsNullOrEmpty(AutomationProperties.GetName(s))) },
                { "first", Describe(stops.FirstOrDefault()) }, { "focused", focused && child.IsKeyboardFocusWithin }, { "cycle", child == null ? null : KeyboardNavigation.GetTabNavigation(child).ToString() }, { "scaled", child != null && child.LayoutTransform is ScaleTransform }, { "seen", A11y.Seen(pop) }, { "same", ReferenceEquals(A11y.LastFlyoutChild, child) }, { "lfType", A11y.LastFlyoutChild == null ? null : A11y.LastFlyoutChild.GetType().Name + "/" + KeyboardNavigation.GetTabNavigation(A11y.LastFlyoutChild) + "/" + A11y.LastFlyoutChild.LayoutTransform }, { "childType", child == null ? null : child.GetType().Name }, { "err", A11y.LastFlyoutError } });
        }
        public string FlyoutEsc()
        {
            var pop = A11y.LastFlyout; if (pop == null || !pop.IsOpen) return "error no flyout open";
            var child = pop.Child as FrameworkElement;
            var src = PresentationSource.FromVisual(child);
            var target = Keyboard.FocusedElement as UIElement; if (target == null || !(child.IsKeyboardFocusWithin)) target = child;
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, src, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(e); DoEvents();
            var back = pop.PlacementTarget as UIElement;
            return "ok " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "handled", e.Handled }, { "open", pop.IsOpen }, { "backOnButton", back != null && back.IsKeyboardFocused }, { "button", Describe(back) } });
        }
        public Dictionary<string, object> PlusState()
        {
            Func<FrameworkElement, double> mid = e => { try { var r = e.TransformToAncestor(this).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)); return Math.Round(r.Top + r.Height / 2, 1); } catch { return -1; } };
            var h = Active != null ? Active.Header : null;
            return new Dictionary<string, object> { { "plus", mid(newBtn) }, { "tab", h == null ? -1 : mid(h) }, { "scale", A11y.Scale }, { "vertical", S.VerticalTabs } };
        }
        public object ToolbarKeys()
        {
            var list = new[] { back, fwd, reload, newBtn, shieldBtn, dlBtn, quickBtn, menuBtn };
            return list.Select(b => new Dictionary<string, object> { { "name", AutomationProperties.GetName(b) }, { "help", AutomationProperties.GetHelpText(b) }, { "key", AutomationProperties.GetAcceleratorKey(b) }, { "focusable", b.Focusable }, { "tabStop", KeyboardNavigation.GetIsTabStop(b) } }).ToList();
        }
        // interactive controls of this window without a name (should be none)
        public List<string> Unnamed()
        {
            var r = new List<string>();
            Action<DependencyObject> walk = null;
            walk = d =>
            {
                var c = d as Control;
                if (c != null && c.IsVisible && (c is ButtonBase || c is TextBox || c is ComboBox) && string.IsNullOrEmpty(AutomationProperties.GetName(c)) && !(c is ContentControl && ((ContentControl)c).Content is string)) r.Add(c.GetType().Name + " " + (c.ToolTip ?? ""));
                int n = VisualTreeHelper.GetChildrenCount(d); for (int i = 0; i < n; i++) walk(VisualTreeHelper.GetChild(d, i));
            };
            walk(this);
            return r;
        }
    }
}
