// Hallow Browser 1.5 - address bar upgrades: an inline calculator and unit conversions ("200*1.2", "5 km in miles"), search
// keywords ("yt cats", "w term", "gh repo" - your own list in Settings), tab search ("% name") and, only if you switch it on,
// search suggestions from the search engine you picked (that sends what you type to the engine - off by default, never in
// private windows). Everything here is worked out on this PC; only the suggestions (when on) go to the engine.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HallowBrowser
{
    [DataContract] public class SearchKeyword { [DataMember] public string Key; [DataMember] public string Url; [DataMember] public string Name; }   // Url has %s for the words

    public static class Calc
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // "200*1.2" -> 240; null when it isn't a sum (a lone number, words, or anything we can't work out)
        public static double? Eval(string input)
        {
            if (input == null) return null;
            var s = input.Trim().TrimStart('=').Trim().Replace("×", "*").Replace("÷", "/").Replace(",", "");
            if (s.Length == 0 || s.Length > 200) return null;
            if (!Regex.IsMatch(s, @"^[0-9+\-*/^%().\s eEpiqrtsabcolgnx]+$")) return null;
            if (!Regex.IsMatch(s, @"[+\-*/^%(]|sqrt|pi|\d\s*x\s*[\d(]")) return null;           // a lone number is not a sum
            if (Regex.IsMatch(s, @"^[+-]?\d+(\.\d+)?$")) return null;
            try { int p = 0; var v = Expr(s, ref p); Skip(s, ref p); if (p != s.Length || double.IsNaN(v) || double.IsInfinity(v)) return null; return v; }
            catch { return null; }
        }
        static void Skip(string s, ref int p) { while (p < s.Length && s[p] == ' ') p++; }
        static double Expr(string s, ref int p)
        {
            var v = Term(s, ref p);
            while (true) { Skip(s, ref p); if (p < s.Length && (s[p] == '+' || s[p] == '-')) { var op = s[p++]; var r = Term(s, ref p); v = op == '+' ? v + r : v - r; } else return v; }
        }
        static double Term(string s, ref int p)
        {
            var v = Power(s, ref p);
            while (true)
            {
                Skip(s, ref p);
                if (p < s.Length && (s[p] == '*' || s[p] == '/' || (s[p] == 'x' && !s.Substring(p).StartsWith("x("))))
                { var op = s[p++]; var r = Power(s, ref p); if (op == '/' && r == 0) throw new DivideByZeroException(); v = op == '/' ? v / r : v * r; }
                else if (p < s.Length && s[p] == '%' && NextIsOperand(s, p + 1)) { p++; var r = Power(s, ref p); v = v % r; }
                else return v;
            }
        }
        static bool NextIsOperand(string s, int p) { while (p < s.Length && s[p] == ' ') p++; return p < s.Length && (char.IsDigit(s[p]) || s[p] == '(' || s[p] == '.'); }
        static double Power(string s, ref int p)
        {
            var b = Unary(s, ref p); Skip(s, ref p);
            if (p < s.Length && s[p] == '^') { p++; var e = Power(s, ref p); return Math.Pow(b, e); }
            return b;
        }
        static double Unary(string s, ref int p)
        {
            Skip(s, ref p);
            if (p < s.Length && s[p] == '-') { p++; return -Unary(s, ref p); }
            if (p < s.Length && s[p] == '+') { p++; return Unary(s, ref p); }
            var v = Atom(s, ref p); Skip(s, ref p);
            if (p < s.Length && s[p] == '%' && !NextIsOperand(s, p + 1)) { p++; v /= 100; }   // "20%" = 0.2
            return v;
        }
        static double Atom(string s, ref int p)
        {
            Skip(s, ref p);
            if (p >= s.Length) throw new FormatException();
            if (s[p] == '(') { p++; var v = Expr(s, ref p); Skip(s, ref p); if (p >= s.Length || s[p] != ')') throw new FormatException(); p++; return v; }
            foreach (var f in new[] { "sqrt", "abs", "log", "ln" })
                if (string.CompareOrdinal(s, p, f, 0, f.Length) == 0) { p += f.Length; var a = Atom(s, ref p); return f == "sqrt" ? Math.Sqrt(a) : f == "abs" ? Math.Abs(a) : f == "log" ? Math.Log10(a) : Math.Log(a); }
            if (string.CompareOrdinal(s, p, "pi", 0, 2) == 0) { p += 2; return Math.PI; }
            var m = Regex.Match(s.Substring(p), @"^(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
            if (!m.Success) throw new FormatException();
            p += m.Length; return double.Parse(m.Value, Inv);
        }
        public static string Format(double v)
        {
            if (Math.Abs(v) >= 1e15 || (Math.Abs(v) < 1e-6 && v != 0)) return v.ToString("0.######E+0", Inv);
            var r = Math.Round(v, 10);
            return r.ToString("#,0.##########", Inv);
        }

        // ---- unit conversions: "<number> <unit> in|to <unit>"
        class U { public string Kind; public double F; public double Off; public string Name; }
        static readonly Dictionary<string, U> Units = BuildUnits();
        static Dictionary<string, U> BuildUnits()
        {
            var d = new Dictionary<string, U>(StringComparer.OrdinalIgnoreCase);
            Action<string, double, string, string[]> add = (kind, f, name, names) => { foreach (var n in names) d[n] = new U { Kind = kind, F = f, Name = name }; };
            add("len", 1000, "km", new[] { "km", "kilometer", "kilometers", "kilometre", "kilometres" });
            add("len", 1, "m", new[] { "m", "meter", "meters", "metre", "metres" });
            add("len", 0.01, "cm", new[] { "cm", "centimeter", "centimeters", "centimetre", "centimetres" });
            add("len", 0.001, "mm", new[] { "mm", "millimeter", "millimeters", "millimetre", "millimetres" });
            add("len", 1609.344, "miles", new[] { "mi", "mile", "miles" });
            add("len", 0.9144, "yd", new[] { "yd", "yard", "yards" });
            add("len", 0.3048, "ft", new[] { "ft", "foot", "feet" });
            add("len", 0.0254, "in", new[] { "inch", "inches", "\"" });
            add("mass", 1000, "kg", new[] { "kg", "kilo", "kilos", "kilogram", "kilograms" });
            add("mass", 1, "g", new[] { "g", "gram", "grams" });
            add("mass", 453.59237, "lb", new[] { "lb", "lbs", "pound", "pounds" });
            add("mass", 28.349523125, "oz", new[] { "oz", "ounce", "ounces" });
            add("mass", 6350.29318, "st", new[] { "st", "stone", "stones" });
            add("vol", 1, "l", new[] { "l", "liter", "liters", "litre", "litres" });
            add("vol", 0.001, "ml", new[] { "ml", "milliliter", "milliliters", "millilitre", "millilitres" });
            add("vol", 3.785411784, "gal", new[] { "gal", "gallon", "gallons" });
            add("vol", 0.2365882365, "cups", new[] { "cup", "cups" });
            add("vol", 0.0295735295625, "fl oz", new[] { "floz", "fl oz" });
            add("speed", 1 / 3.6, "km/h", new[] { "km/h", "kmh", "kph" });
            add("speed", 0.44704, "mph", new[] { "mph" });
            add("speed", 1, "m/s", new[] { "m/s" });
            add("speed", 0.514444, "knots", new[] { "kn", "knot", "knots" });
            add("data", 1, "B", new[] { "b", "byte", "bytes" });
            add("data", 1e3, "KB", new[] { "kb", "kilobyte", "kilobytes" });
            add("data", 1e6, "MB", new[] { "mb", "megabyte", "megabytes" });
            add("data", 1e9, "GB", new[] { "gb", "gigabyte", "gigabytes" });
            add("data", 1e12, "TB", new[] { "tb", "terabyte", "terabytes" });
            add("data", 1024, "KiB", new[] { "kib" }); add("data", 1048576, "MiB", new[] { "mib" }); add("data", 1073741824, "GiB", new[] { "gib" }); add("data", 1099511627776, "TiB", new[] { "tib" });
            add("time", 1, "s", new[] { "s", "sec", "secs", "second", "seconds" });
            add("time", 60, "min", new[] { "min", "mins", "minute", "minutes" });
            add("time", 3600, "h", new[] { "h", "hr", "hrs", "hour", "hours" });
            add("time", 86400, "days", new[] { "day", "days" });
            add("time", 604800, "weeks", new[] { "week", "weeks" });
            d["c"] = d["celsius"] = d["°c"] = new U { Kind = "temp", Name = "°C" };
            d["f"] = d["fahrenheit"] = d["°f"] = new U { Kind = "temp", Name = "°F" };
            d["k"] = d["kelvin"] = new U { Kind = "temp", Name = "K" };
            return d;
        }
        static double ToKelvin(double v, string n) { return n == "°C" ? v + 273.15 : n == "°F" ? (v - 32) * 5 / 9 + 273.15 : v; }
        static double FromKelvin(double k, string n) { return n == "°C" ? k - 273.15 : n == "°F" ? (k - 273.15) * 9 / 5 + 32 : k; }
        // "5 km in miles" -> "3.10686 miles"; null when it isn't a conversion we know
        public static string Convert(string input, out double value)
        {
            value = 0; if (input == null) return null;
            var m = Regex.Match(input.Trim(), @"^(-?[\d.,]+(?:[eE][+-]?\d+)?)\s*([a-zA-Z°/""]+(?:\s?oz)?)\s+(?:in|to|as|=)\s+([a-zA-Z°/""]+(?:\s?oz)?)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            double v; if (!double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, Inv, out v)) return null;
            U a, b; if (!Units.TryGetValue(m.Groups[2].Value.Trim(), out a) || !Units.TryGetValue(m.Groups[3].Value.Trim(), out b) || a.Kind != b.Kind) return null;
            value = a.Kind == "temp" ? FromKelvin(ToKelvin(v, a.Name), b.Name) : v * a.F / b.F;
            var shown = Math.Abs(value) >= 1e15 || (Math.Abs(value) < 1e-4 && value != 0) ? value.ToString("0.####E+0", Inv) : Math.Round(value, Math.Abs(value) >= 100 ? 2 : 5).ToString("#,0.#####", Inv);
            return shown + " " + b.Name;
        }
        // what the address bar shows for the text: "= 240" / "= 3.10686 miles", or null
        public static string Answer(string input)
        {
            double v; var c = Convert(input, out v); if (c != null) return c;
            var e = Eval(input); return e == null ? null : Format(e.Value);
        }
    }

    public static class Keywords
    {
        public static List<SearchKeyword> Defaults()
        {
            return new List<SearchKeyword> {
                new SearchKeyword { Key = "yt", Name = "YouTube", Url = "https://www.youtube.com/results?search_query=%s" },
                new SearchKeyword { Key = "w", Name = "Wikipedia", Url = "https://en.wikipedia.org/wiki/Special:Search?search=%s" },
                new SearchKeyword { Key = "gh", Name = "GitHub", Url = "https://github.com/search?q=%s" } };
        }
        public static string Clean(string key) { var k = (key ?? "").Trim().ToLowerInvariant(); return Regex.IsMatch(k, @"^[a-z0-9._\-!@]{1,16}$") ? k : null; }
        public static bool ValidUrl(string url) { return url != null && url.Contains("%s") && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) && Omni.IsWeb(url.Replace("%s", "x")); }
        // "yt cats" -> the YouTube search for cats; null when the first word isn't a keyword (or nothing follows it)
        public static string Expand(IEnumerable<SearchKeyword> list, string typed, out SearchKeyword used)
        {
            used = null; if (list == null || typed == null) return null;
            var s = typed.Trim(); var sp = s.IndexOf(' '); if (sp <= 0) return null;
            var key = s.Substring(0, sp).ToLowerInvariant(); var rest = s.Substring(sp + 1).Trim(); if (rest.Length == 0) return null;
            used = list.FirstOrDefault(k => k != null && k.Key == key && ValidUrl(k.Url)); if (used == null) return null;
            return used.Url.Replace("%s", Uri.EscapeDataString(rest));
        }
    }

    public static class TabSearch
    {
        // "% name" (or "%name") -> "name"; null when it isn't a tab search
        public static string Query(string typed) { if (typed == null) return null; var s = typed.TrimStart(); if (!s.StartsWith("%")) return null; return s.Substring(1).Trim(); }
        public static bool Matches(string q, string title, string url)
        {
            if (string.IsNullOrEmpty(q)) return true;
            foreach (var w in q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if ((title ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0 && (url ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }
    }

    public static class Suggestions
    {
        // the engine's own suggestion address (OpenSearch JSON: ["typed", ["one", "two", ...]]). Tests: a local stand-in only.
        public static string Url(string engine, string q)
        {
            var e = Uri.EscapeDataString(q ?? "");
            if (BrowserSettings.SuggestTest != null) return BrowserSettings.SuggestTest + (BrowserSettings.SuggestTest.Contains("?") ? "&" : "?") + "q=" + e;
            switch (engine)
            {
                case "google": return "https://suggestqueries.google.com/complete/search?client=firefox&q=" + e;
                case "bing": return "https://api.bing.com/osjson.aspx?query=" + e;
                case "brave": return "https://search.brave.com/api/suggest?q=" + e;
                default: return "https://duckduckgo.com/ac/?type=list&q=" + e;
            }
        }
        public static List<string> Parse(string json, int max)
        {
            var list = new List<string>();
            try
            {
                var o = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as object[];
                if (o == null || o.Length < 2) return list;
                var arr = o[1] as object[]; if (arr == null) return list;
                foreach (var x in arr) { var s = x as string; if (!string.IsNullOrWhiteSpace(s) && s.Length <= 200 && !list.Contains(s)) list.Add(s.Trim()); if (list.Count >= max) break; }
            }
            catch { }
            return list;
        }
        // one request, no cookies, a short timeout; null on any failure
        public static List<string> Fetch(string engine, string q, int max)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(Url(engine, q));
                req.Timeout = 2500; req.ReadWriteTimeout = 2500; req.UserAgent = "Mozilla/5.0 HallowBrowser"; req.Accept = "application/json";
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (var r = (HttpWebResponse)req.GetResponse()) using (var st = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                {
                    var buf = new char[65536]; int n = st.ReadBlock(buf, 0, buf.Length);
                    return Parse(new string(buf, 0, n), max);
                }
            }
            catch { return null; }
        }
    }
}
