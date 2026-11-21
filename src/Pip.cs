// Hallow Browser - "Pop out video": the tab moves into a small borderless window that stays on top (over games in borderless or
// windowed mode - nothing can draw over an exclusive full-screen game). The page's biggest video fills the little window;
// "Back to tab" (or closing it) puts the tab back where it was. The tab never sleeps while it's popped out.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HallowBrowser
{
    public static class Pip
    {
        static readonly Dictionary<Tab, Window> wins = new Dictionary<Tab, Window>();
        const string Css = "(function(){var v=[].slice.call(document.querySelectorAll('video')).sort(function(a,b){return b.clientWidth*b.clientHeight-a.clientWidth*a.clientHeight;})[0];" +
            "if(!v)return 'novideo';v.setAttribute('data-hallow-pip','1');var s=document.getElementById('hallow-pip-style');if(!s){s=document.createElement('style');s.id='hallow-pip-style';document.documentElement.appendChild(s);}" +
            "s.textContent='video[data-hallow-pip]{position:fixed!important;inset:0!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;z-index:2147483647!important;background:#000!important;object-fit:contain!important;}html,body{overflow:hidden!important}';return 'ok';})()";
        const string Uncss = "(function(){var s=document.getElementById('hallow-pip-style');if(s)s.remove();var v=document.querySelector('video[data-hallow-pip]');if(v)v.removeAttribute('data-hallow-pip');return 'ok';})()";

        public static Window Window(Tab t) { Window w; return wins.TryGetValue(t, out w) ? w : null; }

        public static async void PopOut(BrowserWindow bw, Tab t)
        {
            if (t == null || t.PoppedOut || t.View == null || t.Core == null) { if (t != null && t.Core == null) bw.Toast("Open the tab first, then pop out its video.", 4); return; }
            string has; try { has = await t.Core.ExecuteScriptAsync("document.querySelectorAll('video').length"); } catch { has = "0"; }
            if (has == "0") { bw.Toast("There's no video on this page to pop out.", 4); return; }
            if (t == bw.SplitL || t == bw.SplitR) bw.CloseSplit();   // 1.2: a popped-out tab leaves the split view
            var view = t.View;
            bw.ContentGrid.Children.Remove(view);
            var w = new Window { Title = t.Title + " - Hallow Browser", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResizeWithGrip, Topmost = true, ShowInTaskbar = true, Width = 480, Height = 300, Icon = Ui.AppIcon() };
            w.SetResourceReference(System.Windows.Window.BackgroundProperty, "Strip");
            var wa = SystemParameters.WorkArea; w.Left = wa.Right - w.Width - 24; w.Top = wa.Bottom - w.Height - 24;
            if (BrowserSettings.Offscreen) { w.Left = -32000; w.Top = -32000; w.ShowActivated = false; w.ShowInTaskbar = false; }
            var g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) }); g.RowDefinitions.Add(new RowDefinition());
            var bar = new DockPanel(); bar.SetResourceReference(Panel.BackgroundProperty, "Strip");
            bar.MouseLeftButtonDown += (a, b) => { if (b.ButtonState == MouseButtonState.Pressed) try { w.DragMove(); } catch { } };
            var close = Ui.Tool("xmark", "Close the pop-out (the tab goes back)", () => Return(bw, t), 10); close.Width = 26; close.Height = 24; DockPanel.SetDock(close, Dock.Right);
            var back = Ui.Tool("rotate-left", "Back to the tab", () => Return(bw, t), 10); back.Width = 26; back.Height = 24; DockPanel.SetDock(back, Dock.Right);
            bar.Children.Add(close); bar.Children.Add(back);
            var title = Ui.Muted(t.Title, 11); title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; title.Margin = new Thickness(10, 0, 6, 0); title.VerticalAlignment = VerticalAlignment.Center;
            bar.Children.Add(title);
            g.Children.Add(bar);
            Grid.SetRow(view, 1); view.Visibility = Visibility.Visible; g.Children.Add(view);
            w.Content = g;
            w.Closed += (a, b) => { if (t.PoppedOut) Return(bw, t, true); };
            wins[t] = w; t.PoppedOut = true;
            if (t.Suspended && t.Core != null) { try { t.Core.Resume(); } catch { } t.Suspended = false; }
            w.Show();
            try { await t.Core.ExecuteScriptAsync(Css); } catch { }
            if (t == bw.Active) bw.ShowPlaceholder(true);
            bw.Refresh(t);
        }
        public static void Return(BrowserWindow bw, Tab t, bool windowClosed = false) { Close(bw, t, !windowClosed); }
        public static void Close(BrowserWindow bw, Tab t, bool closeWindow)
        {
            Window w; if (!wins.TryGetValue(t, out w)) return;
            wins.Remove(t); t.PoppedOut = false;
            var view = t.View;
            if (view != null)
            {
                var g = w.Content as Grid; if (g != null) g.Children.Remove(view);
                Grid.SetRow(view, 0);
                bw.ContentGrid.Children.Add(view);
                view.Visibility = t == bw.Active ? Visibility.Visible : Visibility.Hidden;
                try { var _ = t.Core.ExecuteScriptAsync(Uncss); } catch { }
            }
            if (closeWindow) try { w.Close(); } catch { }
            if (t == bw.Active) bw.ShowPlaceholder(false);
            bw.Refresh(t);
        }
    }
}
