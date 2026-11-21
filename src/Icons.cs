// Hallow Browser - Font Awesome icons (path data in Icons.g.cs, made by tools\gen-icons.ps1).
using System.Collections.Generic;
using System.Windows.Media;

namespace HallowBrowser
{
    public static partial class Icons
    {
        static readonly Dictionary<string, Geometry> cache = new Dictionary<string, Geometry>();
        public static bool Has(string name) { return name != null && Data.ContainsKey(name); }   // 1.4
        public static Geometry Get(string name)
        {
            if (name == null) return null;
            Geometry g;
            if (!cache.TryGetValue(name, out g))
            {
                string d; if (!Data.TryGetValue(name, out d)) return null;
                g = Geometry.Parse(d); g.Freeze(); cache[name] = g;
            }
            return g;
        }
    }
}
