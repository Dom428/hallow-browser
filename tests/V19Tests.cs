// Hallow Browser 1.9 - unit tests: names and keys made from tool tips, the UI scale (Windows' text size by default, 100-200%), the
// high-contrast theme (every colour a system colour, nothing of the Hallow palette left, still a valid theme), the new setting's
// defaults, the memory-target rule and pictures that drop their source data, the settings card, and the tab's UI Automation peer.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowBrowser.Tests
{
    public static class V19
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All() { NameTests(); ScaleTests(); ContrastTests(); SettingsTests(); SpeedTests(); PeerTests(); RealUseTests(); }

        static void RealUseTests()
        {
            Ok(Rules19.AutoShowBar(false, false, 1, false), "bookmarks bar: on by itself with one bookmark");
            Ok(!Rules19.AutoShowBar(false, false, 0, false), "bookmarks bar: not without bookmarks");
            Ok(!Rules19.AutoShowBar(false, true, 50, false), "bookmarks bar: hidden on purpose stays hidden");
            Ok(!Rules19.AutoShowBar(false, false, 5, true), "bookmarks bar: never from a private window");
            Ok(Rules19.ChipVisible("Delta Force", true) && !Rules19.ChipVisible(null, true) && !Rules19.ChipVisible("Delta Force", false), "game icon: only while game mode is on, and only if the setting allows it");
            Func<string, bool> none = n => false, df = n => n == "Delta Force";
            Eq(Rules19.FromHub(new[] { "Delta Force" }, true, df), "Delta Force", "game mode from Hub: Hub runs and the game's process is there");
            Eq(Rules19.FromHub(new[] { "Delta Force" }, true, none), null, "game mode from Hub: Hub still says Delta Force but its process is gone -> off (the stuck chip)");
            Eq(Rules19.FromHub(new[] { "THE FINALS" }, true, none), "THE FINALS", "game mode from Hub: a game the browser can't see by process - Hub's word");
            Eq(Rules19.FromHub(new[] { "THE FINALS" }, false, none), null, "game mode from Hub: Hub closed (its status.json left behind) -> off");
            Eq(Rules19.FromHub(new string[0], true, none), null, "game mode from Hub: nothing playing");
            var plan = Rules19.BySite(new[] { "https://www.youtube.com/a", "https://example.com/", "https://youtube.com/b", Omni.NewTabUrl, "https://example.com/x", "https://solo.net/" }, new[] { false, false, false, false, true, false });
            Ok(plan.Count == 1 && plan[0].Key == "youtube.com" && plan[0].Value.SequenceEqual(new[] { 0, 2 }), "auto-group: by site (www. ignored), only sites with 2+ tabs outside a group");
            Eq(Rules19.GroupName("youtube.com"), "Youtube", "auto-group: the group is named after the site");
            Eq(Rules19.GroupName("localhost"), "Localhost", "auto-group: a one-word host");
            var s = new BrowserSettings(); s.Fix();
            Ok(s.GameChip && !s.BookmarksBarHidden && !s.GroupTipShown && !s.BookmarksBar, "settings: game icon on, bar not hidden on purpose, tip not shown yet, bar off until a bookmark");
        }

        static void NameTests()
        {
            Eq(A11y.NamePart("New tab (Ctrl+T)"), "New tab", "a11y: name from a tool tip without its shortcut");
            Eq(A11y.KeyPart("New tab (Ctrl+T)"), "Ctrl+T", "a11y: the shortcut becomes the accelerator key");
            Eq(A11y.NamePart("Hallow Shield - this site, levels, filters"), "Hallow Shield", "a11y: name = the part before \" - \" (the rest is help text)");
            Eq(A11y.NamePart("Discord (3 new) - asleep\nRight-click (or Shift+F10): phone/desktop, reload, remove"), "Discord (3 new)", "a11y: a sidebar entry: name with its unread count, first line only");
            Eq(A11y.KeyPart("Discord (3 new)"), null, "a11y: \"(3 new)\" isn't a shortcut");
            Eq(A11y.NamePart("Reader mode (F9)"), "Reader mode", "a11y: function-key shortcut taken off");
            Eq(A11y.NamePart("Close (Esc)"), "Close", "a11y: Esc taken off");
            Eq(A11y.NamePart(null), null, "a11y: no tool tip, no name from it");
            var tb = new TextBlock { Text = "Put to sleep" }; var g = new Grid(); g.Children.Add(new TextBlock { Text = "" }); g.Children.Add(tb); g.Children.Add(new TextBlock { Text = "Ctrl+W" });
            Ok(A11y.Texts(g).SequenceEqual(new[] { "Put to sleep", "Ctrl+W" }), "a11y: the words of a menu row, in order (empty ones skipped)");
        }

        static void ScaleTests()
        {
            Eq(A11y.ScaleFor(0, 100), 1.0, "scale: like Windows at 100% text = 1.0");
            Eq(A11y.ScaleFor(0, 150), 1.5, "scale: follows Windows' text size (150%)");
            Eq(A11y.ScaleFor(0, 225), 2.0, "scale: Windows' 225% is held at 200%");
            Eq(A11y.ScaleFor(125, 225), 1.25, "scale: a chosen size wins over Windows'");
            Eq(A11y.ScaleFor(90, 100), 1.0, "scale: never below 100%");
            Ok(A11y.ScaleChoices.SequenceEqual(new[] { 0, 100, 125, 150, 175, 200 }), "scale: the choices (0 = like Windows) - 100 to 200%");
            A11y.ForceTextScale = 175; Eq(A11y.WindowsTextScale(), 175, "scale: tests can stand in for Windows' text size (the real setting is only read)"); A11y.ForceTextScale = null;
            var w = A11y.WindowsTextScale(); Ok(w >= 100 && w <= 225, "scale: Windows' text size read (" + w + "%)");
        }

        static string Theme() { using (var s = typeof(Program).Assembly.GetManifestResourceStream("HallowBrowser.Theme.xaml")) using (var r = new System.IO.StreamReader(s)) return r.ReadToEnd(); }
        static void ContrastTests()
        {
            var x = Theme();
            var hc = A11y.HighContrastXaml(x);
            var sys = A11y.HcColors().Values.Select(v => v.ToUpperInvariant()).ToList();
            var colours = Regex.Matches(hc, "#([0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\\b").Cast<Match>().Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
            var odd = colours.Where(c => !sys.Contains(c) && !c.StartsWith("#00")).ToList();
            Ok(odd.Count == 0, "high contrast: every colour in the theme is a system colour (" + colours.Count + " distinct; not: " + string.Join(" ", odd) + ")");
            Ok(!hc.ToUpperInvariant().Contains("F07820") && !hc.ToUpperInvariant().Contains("14101B") && !hc.ToUpperInvariant().Contains("A98BE0"), "high contrast: nothing of the Hallow palette (accent, background, lilac) is left");
            ResourceDictionary rd = null; try { rd = (ResourceDictionary)XamlReader.Parse(Accent.ApplyTo(hc, "#8B5CF6")); } catch (Exception e) { Ok(false, "high contrast: parses: " + e.Message); }
            if (rd != null)
            {
                Func<string, Color> col = k => ((SolidColorBrush)rd[k]).Color;
                Eq(col("Bg"), SystemColors.WindowColor, "high contrast: background = Window");
                Eq(col("Text"), SystemColors.WindowTextColor, "high contrast: text = WindowText");
                Eq(col("Muted"), SystemColors.WindowTextColor, "high contrast: muted text = WindowText (no grey text)");
                Eq(col("Line"), SystemColors.WindowTextColor, "high contrast: lines are drawn in WindowText (visible)");
                Eq(col("AccentBrush"), SystemColors.HighlightColor, "high contrast: the accent = Highlight");
                Eq(col("OnAccent"), SystemColors.HighlightTextColor, "high contrast: text on the accent = HighlightText");
                Eq((Color)rd["PageBg"], SystemColors.WindowColor, "high contrast: behind pages = Window");
                Ok(rd.Contains("Tool") && rd.Contains("Btn") && rd.Contains("Switch"), "high contrast: the styles are all there");
                Ok(hc.Contains("StrokeThickness=\"3\""), "high contrast: a thicker focus outline");
            }
            Ok(Regex.Match(x, "x:Key=\"Tool\"[\\s\\S]*?</Style>").Value.Contains("Property=\"Focusable\" Value=\"True\""), "keyboard: the toolbar's icon buttons are keyboard stops now (Tool style)");
            Ok(x.Contains("UiScaleTransform"), "scale: tool tips follow the UI scale");
        }

        static void SettingsTests()
        {
            var s = new BrowserSettings(); s.Fix();
            Eq(s.UiScale, 0, "settings: the UI scale follows Windows by default");
            s.UiScale = 133; s.Fix(); Eq(s.UiScale, 0, "settings: an odd UI scale falls back to Windows'");
            s.UiScale = 175; s.Fix(); Eq(s.UiScale, 175, "settings: a listed UI scale stays");
            Eq(SettingsLayout.SectionOf("Accessibility"), "Appearance", "settings: the Accessibility card is in Appearance");
        }

        static void SpeedTests()
        {
            var src = new byte[64 * 64 * 4]; var bmp = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, src, 64 * 4);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); var ms = new System.IO.MemoryStream(); enc.Save(ms); ms.Position = 0;
            var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
            var d = Speed19.Detach(img);
            Ok(d != null && d.IsFrozen && !(d is BitmapImage) && d.PixelWidth == 64, "previews: the kept picture doesn't hold its encoded source (a frozen copy of the pixels)");
            var args = Engine.ExtraArgs(new BrowserSettings());
            Ok(args.Contains("--renderer-process-limit=8") && !args.Contains("site-per-process") && !args.Contains("disable-site-isolation") && !args.Contains("disable-gpu") && !args.Contains("disable-features=CalculateNativeWinOcclusion"), "speed: site isolation untouched, hardware acceleration and occlusion on (outside tests)");
        }

        static void PeerTests()
        {
            var t = new Tab { Title = "Example page" };
            var h = new TabHeader(t, null);
            Ok(h.Focusable && System.Windows.Input.KeyboardNavigation.GetIsTabStop(h), "tabs: a tab is a keyboard stop");
            var p = UIElementAutomationPeer.CreatePeerForElement(h);
            Ok(p != null && p.GetAutomationControlType() == AutomationControlType.TabItem && p.GetName() == "Example page", "tabs: UI Automation sees a TabItem named after the page");
            t.Audible = true; t.Muted = true; t.Suspended = true;
            Eq(p.GetName(), "Example page (muted) (asleep)", "tabs: muted and asleep are in its name");
            Ok(p.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider, "tabs: selectable (SelectionItem pattern)");
        }
    }
}
