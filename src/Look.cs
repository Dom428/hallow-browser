// Hallow Browser - light and dark mode (Hallow family spec). The family mode is "mode" in %APPDATA%\Hallow\theme.json
// ("dark" | "light" | "system", written by Hallow Toolkit; missing = dark). This app's own setting (Look: follow / light / dark) wins.
// "system" follows Windows (HKCU ...\Themes\Personalize AppsUseLightTheme, 1 = light), polled every 3 s - read only, never written.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HallowBrowser
{
    public static class Look
    {
        public const string Follow = "follow", Light = "light", Dark = "dark";
        // the light palette (spec); dark = each app's own Theme.xaml values, unchanged
        public const string LBg = "#F6F4FA", LPanel = "#FFFFFF", LPanel2 = "#EEEAF5", LBorder = "#DAD3E6", LText = "#1E1A26", LMuted = "#6B6478", LLilac = "#7758BF",   // 1.3: was #7A5CC0 (4.30:1 on #EEEAF5)
            LGreenText = "#1B7A44", LAmberText = "#8F5B00",   // 1.3: green / amber as small text (the icon shades are just under 4.5:1)
            LGreen = "#1E8E4F", LAmber = "#A66A00", LRed = "#C2333F";

        public static bool IsLight;   // what the windows use now

        // "dark" | "light" | "system" from theme.json; anything else, a missing file or a broken one = "dark"
        public static string ReadFamilyMode(string themeFile)
        {
            try
            {
                if (string.IsNullOrEmpty(themeFile) || !File.Exists(themeFile)) return Dark;
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(themeFile));
                object m; if (d == null || !d.TryGetValue("mode", out m)) return Dark;
                var s = ((m as string) ?? "").Trim().ToLowerInvariant();
                return s == Light || s == "system" ? s : Dark;
            }
            catch { return Dark; }
        }

        // Windows' "apps use light theme". HALLOW_SYSTEM_LIGHT=1/0 replaces it for tests and snaps (the real setting is only read).
        public static Func<bool> SystemLight = ReadSystemLight;
        public static bool ReadSystemLight()
        {
            var e = Environment.GetEnvironmentVariable("HALLOW_SYSTEM_LIGHT");
            if (e == "1") return true; if (e == "0") return false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v != null && Convert.ToInt32(v) == 1;   // not set = dark (family spec)
                }
            }
            catch { return false; }
        }

        // the app's own choice wins; "follow" (or anything unknown) uses the family mode
        public static bool Resolve(string appLook, string familyMode, Func<bool> systemLight)
        {
            var a = (appLook ?? "").Trim().ToLowerInvariant();
            if (a == Light) return true;
            if (a == Dark) return false;
            if (familyMode == Light) return true;
            if (familyMode == "system") return systemLight != null && systemLight();
            return false;
        }
        public static bool Resolve(string appLook, string themeFile) { return Resolve(appLook, ReadFamilyMode(themeFile), SystemLight); }
        public static string Normalize(string appLook) { var a = (appLook ?? "").Trim().ToLowerInvariant(); return a == Light || a == Dark ? a : Follow; }

        // ---- colours
        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        public static string H(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static double Lum(Color c)
        {
            Func<byte, double> lin = v => { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B);
        }
        public static double Contrast(string a, string b) { double x = Lum(C(a)), y = Lum(C(b)); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }

        // the accent for TEXT and thin lines on light backgrounds: darkened in HSL until it reaches 4.5:1 on white, the light background
        // and (1.3) the darkest light surface, #EEEAF5 (strip, hover, rail) - default orange -> about #A94E0B. Unchanged when it already does.
        public static string TextAccent(string hex)
        {
            var c = C(hex); double h, s, l; ToHsl(c, out h, out s, out l);
            var cur = hex.ToUpperInvariant();
            while ((Contrast(cur, LPanel) < 4.5 || Contrast(cur, LBg) < 4.5 || Contrast(cur, LPanel2) < 4.5) && l > 0) { l = Math.Max(0, l - 0.005); cur = H(FromHsl(h, s, l)); }
            return cur;
        }
        // 1.3 (as Hallow Toolkit 5.16): a filled accent in light mode - near-black or white text, whichever reads better; if that text
        // doesn't reach 4.5:1 on the real accent, the fill is the accent darkened to 4.5:1 (4.7, for hover) on white, with white text
        public static string TextOnFill(string fill) { return Contrast(LText, fill) >= Contrast("#FFFFFF", fill) ? LText : "#FFFFFF"; }
        public static string FillOf(string accent)
        {
            var a = accent.ToUpperInvariant(); if (Contrast(a, TextOnFill(a)) >= 4.5) return a;
            var c = C(a); double h, s, l; ToHsl(c, out h, out s, out l); var cur = a;
            while (Contrast(cur, "#FFFFFF") < 4.7 && l > 0) { l = Math.Max(0, l - 0.005); cur = H(FromHsl(h, s, l)); }   // a little margin: the hover veil (10% accent) still leaves 4.5:1
            return cur;
        }
        static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            l = (mx + mn) / 2; h = s = 0; if (mx == mn) return;
            var d = mx - mn; s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
            if (mx == r) h = (g - b) / d + (g < b ? 6 : 0); else if (mx == g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
            h /= 6;
        }
        static Color FromHsl(double h, double s, double l)
        {
            Func<double, double, double, double> f = (p, q, t) => { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1 / 6.0) return p + (q - p) * 6 * t; if (t < 0.5) return q; if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6; return p; };
            double r, g, b; if (s == 0) r = g = b = l; else { var q = l < 0.5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q; r = f(p, q, h + 1 / 3.0); g = f(p, q, h); b = f(p, q, h - 1 / 3.0); }
            Func<double, byte> k = v => (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
            return Color.FromRgb(k(r), k(g), k(b));
        }

        // Theme.xaml text with each dark colour swapped for its light one (keys stay the same). Whole "#RRGGBB" / "#AARRGGBB" tokens only.
        public static string Swap(string xaml, IDictionary<string, string> darkToLight)
        {
            return Regex.Replace(xaml, "#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\\b", m =>
            {
                string v; var key = m.Value.ToUpperInvariant();
                if (darkToLight.TryGetValue(key, out v)) return v;
                if (key.Length == 9 && darkToLight.TryGetValue("#" + key.Substring(3), out v)) return "#" + key.Substring(1, 2) + v.TrimStart('#');   // keep the alpha
                return m.Value;
            });
        }
        // one hard-coded colour from code, swapped the same way when light
        public static string Pick(string darkHex, IDictionary<string, string> darkToLight)
        {
            if (!IsLight) return darkHex; string v;
            return darkToLight.TryGetValue(darkHex.ToUpperInvariant(), out v) ? v : darkHex;
        }

        // ---- Hallow Browser's Theme.xaml in light: the named brushes by key, then the colours written inside the templates.
        // The solid accent (#F07820, the lighter #F59A50) becomes the text accent (darkened until 4.5:1); the see-through accent
        // forms (#33F07820 fills) keep the real accent and are swapped by Accent.ApplyTo afterwards. No glows in light.
        public static readonly Dictionary<string, string> KeyLight = new Dictionary<string, string> {
            { "Bg", LBg }, { "Strip", LPanel2 }, { "Bar", LPanel }, { "Panel", LPanel }, { "PanelHi", LPanel2 }, { "TabOn", LPanel }, { "TabHover", LBg },
            { "FieldBg", LBg }, { "FieldFocus", LPanel }, { "Line", LBorder }, { "LineDim", LBorder }, { "Text", LText }, { "Muted", LMuted }, { "Lilac", LLilac },
            { "AccentGlow", "#00F07820" }, { "OnAccent", LText }, { "Green", LGreen }, { "Amber", LAmber }, { "GreenText", LGreenText }, { "AmberText", LAmberText }, { "Red", LRed }, { "PageBg", LBg }, { "Rail", LPanel2 }, { "BadgeBg", LRed },
            // 1.2: tab group colours, dark enough for text on the light strip (4.5:1)
            { "GroupBlue", "#2858A8" }, { "GroupRed", "#B02A38" }, { "GroupYellow", "#7A5600" }, { "GroupGreen", "#1A7442" }, { "GroupPink", "#A42D6C" },
            { "GroupPurple", "#6741B5" }, { "GroupCyan", "#0E6A76" }, { "GroupOrange", "#9C4A0A" }, { "GroupGrey", "#5C5768" } };
        public static readonly Dictionary<string, string> TemplateLight = new Dictionary<string, string> {
            { "#1E1829", LPanel }, { "#463C58", "#CFC6DE" }, { "#221C2E", LPanel }, { "#2A2336", LPanel2 }, { "#9790A8", LMuted }, { "#3A3248", "#CFC6DE" },
            { "#ECE8F4", LText }, { "#14101B", LBg }, { "#110E17", LPanel2 }, { "#1C1726", LPanel } };
        public static string LightXaml(string xaml, string accent)
        {
            var ta = TextAccent(accent);
            // 1.3: accent buttons and the game chip are a filled accent (8-digit, so the token pass below leaves them alone)
            var fill = FillOf(accent); var keys = new Dictionary<string, string>(KeyLight);
            keys["AccentBtnBg"] = "#FF" + fill.TrimStart('#'); keys["AccentBtnLine"] = "#FF" + fill.TrimStart('#'); keys["AccentBtnText"] = "#FF" + TextOnFill(fill).TrimStart('#');
            MatchEvaluator byKey = m => { string v; return keys.TryGetValue(m.Groups[2].Value, out v) ? m.Groups[1].Value + v + m.Groups[4].Value : m.Value; };
            // named brushes / colours: <SolidColorBrush x:Key="Bg" Color="#..."/> and <Color x:Key="PageBg">#...</Color>
            xaml = Regex.Replace(xaml, "(x:Key=\"(\\w+)\" Color=\")(#[0-9A-Fa-f]{6,8})(\")", byKey);
            xaml = Regex.Replace(xaml, "(<Color x:Key=\"(\\w+)\">)(#[0-9A-Fa-f]{6,8})(</Color>)", byKey);
            // the rest: whole 6-digit tokens (8-digit see-through ones keep their colour; the accent ones are done by Accent.ApplyTo)
            var map = new Dictionary<string, string>(TemplateLight); map["#F07820"] = ta; map["#F59A50"] = ta; map["#1A0E04"] = LText;
            return Regex.Replace(xaml, "#[0-9A-Fa-f]{6}(?![0-9A-Fa-f])", m => { string v; return map.TryGetValue(m.Value.ToUpperInvariant(), out v) ? v : m.Value; });
        }

        // ---- Windows title bar (DWMWA_USE_IMMERSIVE_DARK_MODE = 20)
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        public static void TitleBar(Window w)
        {
            try { var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return; int dark = IsLight ? 0 : 1; DwmSetWindowAttribute(h, 20, ref dark, 4); } catch { }
        }
        // sets it now (if the window has a handle) and whenever the window gets one
        public static void Track(Window w) { w.SourceInitialized += (s, e) => TitleBar(w); TitleBar(w); }
        public static void TitleBars() { try { foreach (Window w in Application.Current.Windows) TitleBar(w); } catch { } }

        // calls changed on the UI thread when Windows' light/dark setting flips (only matters while something uses "system")
        public static DispatcherTimer WatchSystem(Dispatcher ui, Action changed)
        {
            bool last = SystemLight();
            var t = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) => { bool now; try { now = SystemLight(); } catch { return; } if (now == last) return; last = now; try { changed(); } catch { } };
            t.Start(); return t;
        }
    }
}
