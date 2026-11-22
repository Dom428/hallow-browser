// Hallow Browser 1.12 - "be your default browser". Hallow Browser NEVER sets itself as the default: Windows 11 protects the
// choice (UserChoice / UserChoiceLatest carry a hash) and only you can make it, in Windows Settings. What 1.12 does:
//   * registers with Windows completely, per user and without admin, the way Chrome and Brave do: a URL ProgID (HallowBrowserURL)
//     and a file ProgID (HallowBrowserHTML) with an Application subkey, Capabilities with URL and file associations and StartMenu,
//     OpenWithProgids for the file types, InstallInfo, RegisteredApplications - then SHChangeNotify(SHCNE_ASSOCCHANGED). The
//     installer runs "HallowBrowser.exe --register"; the browser re-registers itself at start (cheaply, in the background) only
//     when something is missing or the registered exe is gone / it is the installed copy; uninstall runs --unregister.
//   * opens what Windows hands it: an http/https address, a local file (turned into a correct file:/// address - spaces, #, %,
//     unicode) or a file: address; anything else is refused (never searched: a command line isn't something you typed). With
//     "--single-argument" (what the ProgIDs use, like Chrome) the rest of the command line is one address, so a crafted link
//     can't add switches. A running browser gets it through the family pipe as a new tab (no second window).
//   * shows the current default (read-only, from UserChoiceLatest / UserChoice) in Settings > General > Default browser, with a
//     button to Windows' own Default apps page for Hallow Browser and a one-time, dismissable hint on the new tab page.
// Tests write the registration to a throw-away test root (a fake, or a sandbox key named by HALLOW_BROWSER_REG_ROOT), never to the
// real HKCU classes; the browser never touches the registry at all while HALLOW_BROWSER_TESTDIR is set.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace HallowBrowser
{
    // ---- a registry the registration can be written to: the real HKCU (or a sandbox key under it) or an in-memory fake (tests)
    public interface IRegistry
    {
        object Get(string path, string name);          // name "" = the (default) value; null when missing
        void Set(string path, string name, object value);   // string = REG_SZ, int = REG_DWORD; creates the key
        bool Exists(string path);
        void DeleteTree(string path);
        void DeleteValue(string path, string name);
        string[] ValueNames(string path);
        string[] SubKeys(string path);
    }
    public class RealRegistry : IRegistry
    {
        readonly RegistryKey root; public readonly string RootName;
        public RealRegistry(RegistryKey root, string name) { this.root = root; RootName = name; }
        // HKCU itself, or (tests) a sandbox key under HKCU\Software\HallowBrowserTests
        public static RealRegistry CurrentUser() { return new RealRegistry(Registry.CurrentUser, "HKCU"); }
        public static RealRegistry Sandbox(string sub)
        {
            if (string.IsNullOrEmpty(sub) || !sub.StartsWith(@"Software\HallowBrowserTests\", StringComparison.OrdinalIgnoreCase) || sub.Contains("..")) throw new ArgumentException("a test registry root must be under HKCU\\Software\\HallowBrowserTests");
            return new RealRegistry(Registry.CurrentUser.CreateSubKey(sub), @"HKCU\" + sub);
        }
        public object Get(string path, string name) { using (var k = root.OpenSubKey(path)) return k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames); }
        public void Set(string path, string name, object value)
        {
            using (var k = root.CreateSubKey(path))
                if (value is int) k.SetValue(name, value, RegistryValueKind.DWord); else k.SetValue(name, (string)value ?? "", RegistryValueKind.String);
        }
        public bool Exists(string path) { using (var k = root.OpenSubKey(path)) return k != null; }
        public void DeleteTree(string path) { try { root.DeleteSubKeyTree(path, false); } catch { } }
        public void DeleteValue(string path, string name) { try { using (var k = root.OpenSubKey(path, true)) if (k != null) k.DeleteValue(name, false); } catch { } }
        public string[] ValueNames(string path) { using (var k = root.OpenSubKey(path)) return k == null ? new string[0] : k.GetValueNames(); }
        public string[] SubKeys(string path) { using (var k = root.OpenSubKey(path)) return k == null ? new string[0] : k.GetSubKeyNames(); }
    }
    public class FakeRegistry : IRegistry
    {
        // key path (lower case) -> values (name lower case -> value); the key's own spelling is kept for listing
        public readonly Dictionary<string, Dictionary<string, object>> Keys = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        public int Writes;
        static string N(string p) { return (p ?? "").Trim('\\'); }
        void Ensure(string p) { p = N(p); var parts = p.Split('\\'); for (int i = 1; i <= parts.Length; i++) { var k = string.Join("\\", parts.Take(i)); if (!Keys.ContainsKey(k)) Keys[k] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); } }
        public object Get(string path, string name) { Dictionary<string, object> v; object o; return Keys.TryGetValue(N(path), out v) && v.TryGetValue(name ?? "", out o) ? o : null; }
        public void Set(string path, string name, object value) { Ensure(path); Keys[N(path)][name ?? ""] = value; Writes++; }
        public bool Exists(string path) { return Keys.ContainsKey(N(path)); }
        public void DeleteTree(string path) { var p = N(path); foreach (var k in Keys.Keys.ToList()) if (k.Equals(p, StringComparison.OrdinalIgnoreCase) || k.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)) Keys.Remove(k); }
        public void DeleteValue(string path, string name) { Dictionary<string, object> v; if (Keys.TryGetValue(N(path), out v)) v.Remove(name ?? ""); }
        public string[] ValueNames(string path) { Dictionary<string, object> v; return Keys.TryGetValue(N(path), out v) ? v.Keys.ToArray() : new string[0]; }
        public string[] SubKeys(string path) { var p = N(path) + "\\"; return Keys.Keys.Where(k => k.StartsWith(p, StringComparison.OrdinalIgnoreCase) && k.IndexOf('\\', p.Length) < 0).Select(k => k.Substring(p.Length)).ToArray(); }
    }

    public class RegEntry
    {
        public string Path, Name; public object Value;
        public RegEntry(string p, string n, object v) { Path = p; Name = n; Value = v; }
        public override string ToString() { return Path + " [" + (Name.Length == 0 ? "(default)" : Name) + "] = " + Value; }
    }

    public class DefaultStatus
    {
        public string HttpsProgId, HttpProgId, Name;   // Name: a friendly name ("Brave"), null when unknown / none
        public bool IsHallow, HttpDiffers;
        public string Text;                            // what Settings says
    }

    public static class DefaultBrowser
    {
        public const string AppName = "Hallow Browser";
        public const string UrlProgId = "HallowBrowserURL", FileProgId = "HallowBrowserHTML";
        public const string ClientKey = @"Software\Clients\StartMenuInternet\HallowBrowser";
        public const string Description = "A fast browser for gamers with Hallow Shield built in";
        public static readonly string[] Protocols = { "http", "https" };
        // .pdf: WebView2 has Edge's PDF viewer built in; .svg / .webp: shown like any image page
        public static readonly string[] FileTypes = { ".htm", ".html", ".shtml", ".xht", ".xhtml", ".svg", ".pdf", ".webp" };
        public const string SettingsUri = "ms-settings:defaultapps?registeredAppUser=Hallow%20Browser";   // documented (Windows 11, 2023-04 update or later)
        public const string SettingsUriFallback = "ms-settings:defaultapps";

        // ================================================================ registration
        public static string Quoted(string exe) { return "\"" + exe + "\""; }
        public static string OpenCommand(string exe) { return Quoted(exe) + " --single-argument %1"; }
        public static string IconValue(string exe) { return Quoted(exe) + ",0"; }

        // everything 1.12 writes (relative to HKCU). InstallInfo\IconsVisible is written only when missing (--hide-icons keeps its 0).
        public static List<RegEntry> Entries(string exe)
        {
            var e = new List<RegEntry>();
            Action<string, string, object> add = (p, n, v) => e.Add(new RegEntry(p, n, v));
            foreach (var pid in new[] { UrlProgId, FileProgId })
            {
                var c = @"Software\Classes\" + pid;
                add(c, "", pid == UrlProgId ? "Hallow Browser web address" : "Hallow Browser HTML Document");
                if (pid == UrlProgId) add(c, "URL Protocol", "");
                add(c + @"\DefaultIcon", "", IconValue(exe));
                add(c + @"\shell\open\command", "", OpenCommand(exe));
                add(c + @"\Application", "ApplicationName", AppName);
                add(c + @"\Application", "ApplicationIcon", IconValue(exe));
                add(c + @"\Application", "ApplicationDescription", Description);
                add(c + @"\Application", "ApplicationCompany", "Hallow");
                // no AppUserModelId: Hallow Browser doesn't set an explicit one (Windows derives it from the exe)
            }
            foreach (var ext in FileTypes) add(@"Software\Classes\" + ext + @"\OpenWithProgids", FileProgId, "");
            var k = ClientKey;
            add(k, "", AppName);
            add(k + @"\DefaultIcon", "", IconValue(exe));
            add(k + @"\shell\open\command", "", Quoted(exe));
            add(k + @"\Capabilities", "ApplicationName", AppName);
            add(k + @"\Capabilities", "ApplicationDescription", Description);
            add(k + @"\Capabilities", "ApplicationIcon", IconValue(exe));
            foreach (var p in Protocols) add(k + @"\Capabilities\URLAssociations", p, UrlProgId);
            foreach (var ext in FileTypes) add(k + @"\Capabilities\FileAssociations", ext, FileProgId);
            add(k + @"\Capabilities\StartMenu", "StartMenuInternet", "HallowBrowser");
            add(k + @"\InstallInfo", "ReinstallCommand", Quoted(exe) + " --register");
            add(k + @"\InstallInfo", "HideIconsCommand", Quoted(exe) + " --hide-icons");
            add(k + @"\InstallInfo", "ShowIconsCommand", Quoted(exe) + " --show-icons");
            add(@"Software\RegisteredApplications", AppName, k + @"\Capabilities");
            return e;
        }
        public static void Register(IRegistry r, string exe)
        {
            foreach (var x in Entries(exe)) if (!Equals(r.Get(x.Path, x.Name), x.Value)) r.Set(x.Path, x.Name, x.Value);
            if (!(r.Get(ClientKey + @"\InstallInfo", "IconsVisible") is int)) r.Set(ClientKey + @"\InstallInfo", "IconsVisible", 1);
        }
        // what's missing or different (empty = registered as this exe)
        public static List<RegEntry> Missing(IRegistry r, string exe)
        {
            var res = new List<RegEntry>();
            foreach (var x in Entries(exe)) { object v; try { v = r.Get(x.Path, x.Name); } catch { v = null; } if (!Equals(v, x.Value)) res.Add(x); }
            return res;
        }
        // everything Hallow Browser added, and nothing else: other apps' OpenWithProgids and file types stay
        public static void Unregister(IRegistry r)
        {
            r.DeleteTree(@"Software\Classes\" + UrlProgId);
            r.DeleteTree(@"Software\Classes\" + FileProgId);
            r.DeleteTree(ClientKey);
            r.DeleteValue(@"Software\RegisteredApplications", AppName);
            foreach (var ext in FileTypes)
            {
                var owp = @"Software\Classes\" + ext + @"\OpenWithProgids";
                r.DeleteValue(owp, FileProgId);
                if (r.Exists(owp) && r.ValueNames(owp).Length == 0 && r.SubKeys(owp).Length == 0) r.DeleteTree(owp);
                var ek = @"Software\Classes\" + ext;
                if (r.Exists(ek) && r.ValueNames(ek).Length == 0 && r.SubKeys(ek).Length == 0) r.DeleteTree(ek);   // only a key we left empty
            }
        }
        // the exe the URL ProgID opens now (null = not registered)
        public static string RegisteredExe(IRegistry r)
        {
            var c = r.Get(@"Software\Classes\" + UrlProgId + @"\shell\open\command", "") as string;
            if (string.IsNullOrEmpty(c)) return null;
            var m = Regex.Match(c, "^\\s*\"([^\"]+)\""); if (m.Success) return m.Groups[1].Value;
            var sp = c.IndexOf(' '); return sp > 0 ? c.Substring(0, sp) : c;
        }
        static bool Same(string a, string b) { try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); } catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); } }
        public static string InstalledExe { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Hallow Browser\HallowBrowser.exe"); } }
        // at start: should this copy (re-)register? Never steals a working registration from the installed copy (a build run from
        // somewhere else leaves it alone); takes over when nothing is registered, the registered exe is gone (a moved install), or
        // this IS the installed copy (an update / a repair). Same exe -> only when something is missing.
        public static string StartDecision(string registeredExe, bool complete, string exe, string installedExe, Func<string, bool> exists)
        {
            bool isInstalled = Same(exe, installedExe);
            if (registeredExe == null) return isInstalled || !exists(installedExe) ? "register" : "skip: not registered, and this isn't the installed copy";
            if (Same(registeredExe, exe)) return complete ? "ok" : "register";
            if (!exists(registeredExe)) return "register";   // moved / reinstalled elsewhere
            return isInstalled ? "register" : "skip: registered to another copy that still exists";
        }
        public static string LastStart;   // what the start-up check did (for status / tests)
        // at start, on a worker: a few registry reads; writes only when the decision says so
        public static void EnsureAtStart()
        {
            if (BrowserSettings.TestDir != null || Environment.GetEnvironmentVariable("HALLOW_BROWSER_NOREGISTER") == "1") { LastStart = "skip: test"; return; }
            Task.Run(() =>
            {
                try
                {
                    var r = RealRegistry.CurrentUser(); var exe = Program.ExePath;
                    var d = StartDecision(RegisteredExe(r), Missing(r, exe).Count == 0, exe, InstalledExe, File.Exists);
                    if (d == "register") { Register(r, exe); NotifyShell(); }
                    LastStart = d;
                }
                catch (Exception e) { LastStart = "error " + e.Message; }
            });
        }

        [DllImport("shell32.dll")] static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        const int SHCNE_ASSOCCHANGED = 0x08000000; const uint SHCNF_IDLIST = 0;
        public static void NotifyShell() { try { SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero); } catch { } }

        // --register / --unregister / --hide-icons / --show-icons (the installer, uninstaller and Windows' InstallInfo commands).
        // In a test folder only a sandbox root (HALLOW_BROWSER_REG_ROOT) is ever written, never HKCU's real keys.
        public static int RunCli(string what)
        {
            IRegistry r;
            if (BrowserSettings.TestDir != null)
            {
                var sub = Environment.GetEnvironmentVariable("HALLOW_BROWSER_REG_ROOT");
                if (string.IsNullOrEmpty(sub)) return 3;
                try { r = RealRegistry.Sandbox(sub); } catch { return 3; }
            }
            else r = RealRegistry.CurrentUser();
            try
            {
                var exe = Program.ExePath;
                switch (what)
                {
                    case "--register": Register(r, exe); break;
                    case "--unregister": Unregister(r); break;
                    case "--hide-icons": Shortcuts(false, exe); if (r.Exists(ClientKey)) r.Set(ClientKey + @"\InstallInfo", "IconsVisible", 0); break;
                    case "--show-icons": Shortcuts(true, exe); if (r.Exists(ClientKey)) r.Set(ClientKey + @"\InstallInfo", "IconsVisible", 1); break;
                    default: return 2;
                }
                if (BrowserSettings.TestDir == null) NotifyShell();
                return 0;
            }
            catch { return 1; }
        }
        // the Start menu and Desktop shortcuts the installer makes (a test folder's stand-ins in tests)
        public static string[] ShortcutDirs()
        {
            if (BrowserSettings.TestDir != null) return new[] { Path.Combine(BrowserSettings.TestDir, @"Shortcuts\Programs"), Path.Combine(BrowserSettings.TestDir, @"Shortcuts\Desktop") };
            return new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        }
        static void Shortcuts(bool show, string exe)
        {
            foreach (var dir in ShortcutDirs())
            {
                var lnk = Path.Combine(dir, "Hallow Browser.lnk");
                if (!show) { try { if (File.Exists(lnk)) File.Delete(lnk); } catch { } continue; }   // only our own shortcut
                try
                {
                    Directory.CreateDirectory(dir);
                    SiteApps.CreateShortcut(lnk, exe, "", null, Description);
                }
                catch { }
            }
        }

        // ================================================================ status (read-only)
        public static string ProgIdOverride;   // --snap and unit tests: a made-up answer (never written anywhere)
        static string TestProgId() { return BrowserSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_BROWSER_DEFAULT_PROGID") : null; }
        // Windows 11 keeps the choice in UserChoiceLatest\ProgId (newer builds) and UserChoice; the newer one wins
        public static string ReadProgId(IRegistry r, string protocol)
        {
            var b = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\" + protocol;
            string v = null;
            try { v = r.Get(b + @"\UserChoiceLatest\ProgId", "ProgId") as string; } catch { }
            if (string.IsNullOrEmpty(v)) try { v = r.Get(b + @"\UserChoice", "ProgId") as string; } catch { }
            return string.IsNullOrEmpty(v) ? null : v;
        }
        static readonly string[][] names = {
            new[] { "HallowBrowser", AppName }, new[] { "BraveHTML", "Brave" }, new[] { "BraveBHTML", "Brave Beta" }, new[] { "BraveDHTML", "Brave Dev" }, new[] { "BraveSSHTM", "Brave Nightly" }, new[] { "Brave", "Brave" },
            new[] { "ChromeHTML", "Google Chrome" }, new[] { "ChromeBHTML", "Google Chrome Beta" }, new[] { "ChromeDHTML", "Google Chrome Dev" }, new[] { "ChromeSSHTM", "Google Chrome Canary" }, new[] { "Chrome", "Google Chrome" },
            new[] { "MSEdgeHTM", "Microsoft Edge" }, new[] { "MSEdgeBHTML", "Microsoft Edge Beta" }, new[] { "MSEdgeDHTML", "Microsoft Edge Dev" }, new[] { "MSEdgeSSHTM", "Microsoft Edge Canary" }, new[] { "MSEdge", "Microsoft Edge" },
            new[] { "FirefoxURL", "Firefox" }, new[] { "FirefoxHTML", "Firefox" }, new[] { "OperaGXStable", "Opera GX" }, new[] { "OperaStable", "Opera" }, new[] { "Opera", "Opera" },
            new[] { "VivaldiHTM", "Vivaldi" }, new[] { "LibreWolfHTM", "LibreWolf" }, new[] { "WaterfoxURL", "Waterfox" }, new[] { "IE.HTTP", "Internet Explorer" } };
        // a ProgID -> the browser's name (prefix match: Firefox / Vivaldi add an install hash); null = unknown
        public static string FriendlyName(string progId)
        {
            if (string.IsNullOrWhiteSpace(progId)) return null;
            foreach (var n in names) if (progId.Equals(n[0], StringComparison.OrdinalIgnoreCase)) return n[1];
            foreach (var n in names) if (progId.StartsWith(n[0], StringComparison.OrdinalIgnoreCase)) return n[1];
            return null;
        }
        public static bool IsHallow(string progId) { return progId != null && progId.StartsWith("HallowBrowser", StringComparison.OrdinalIgnoreCase); }
        public static DefaultStatus StatusFrom(string https, string http)
        {
            var s = new DefaultStatus { HttpsProgId = https, HttpProgId = http, IsHallow = IsHallow(https), Name = FriendlyName(https) };
            s.HttpDiffers = http != null && https != null && !string.Equals(http, https, StringComparison.OrdinalIgnoreCase) && IsHallow(http) != IsHallow(https);
            if (s.IsHallow) s.Text = "Hallow Browser is your default browser.";
            else if (https == null) s.Text = "No default browser is set for web links.";
            else if (s.Name != null) s.Text = "Your default browser is " + s.Name + ".";
            else if (https.StartsWith("AppX", StringComparison.OrdinalIgnoreCase)) s.Text = "Your default browser is a Microsoft Store app.";
            else s.Text = "Your default browser is another app (" + https + ").";
            if (s.HttpDiffers) s.Text += " (HTTP links open in " + (FriendlyName(http) ?? http) + ".)";
            return s;
        }
        public static DefaultStatus Status()
        {
            var o = ProgIdOverride ?? TestProgId();
            if (o != null) return StatusFrom(o, o);
            try { var r = RealRegistry.CurrentUser(); return StatusFrom(ReadProgId(r, "https"), ReadProgId(r, "http")); }
            catch { return StatusFrom(null, null); }
        }

        // the button: Windows' Default apps page for Hallow Browser (it only opens Settings - the choice is yours there)
        public static string LastOpened;   // tests: what would have opened (nothing is opened in a test folder)
        public static bool OpenWindowsSettings()
        {
            if (BrowserSettings.TestDir != null || Snap.On) { LastOpened = SettingsUri; return true; }
            try { Process.Start(new ProcessStartInfo(SettingsUri) { UseShellExecute = true }); LastOpened = SettingsUri; return true; }
            catch
            {
                try { Process.Start(new ProcessStartInfo(SettingsUriFallback) { UseShellExecute = true }); LastOpened = SettingsUriFallback; return true; } catch { return false; }
            }
        }
        public const string HowTo = "Windows asks you to choose - Hallow Browser never changes it by itself. On the page that opens, click \"Set default\" at the top (or click HTTPS, then HTTP, then .html, and pick Hallow Browser each time).";

        // ================================================================ command-line arguments -> addresses
        // "--single-argument": everything after it on the raw command line is ONE address (what Windows' ProgIDs pass: %1)
        public static string SingleArgument(string commandLine)
        {
            if (commandLine == null) return null;
            var m = Regex.Match(commandLine, @"(^|\s)--single-argument(\s|$)");
            if (!m.Success) return null;
            var rest = commandLine.Substring(m.Index + m.Length).Trim();
            if (rest.Length >= 2 && rest[0] == '"' && rest[rest.Length - 1] == '"') rest = rest.Substring(1, rest.Length - 2);
            return rest.Length == 0 ? null : rest;
        }
        static bool IsDrivePath(string s) { return Regex.IsMatch(s, @"^[a-zA-Z]:[\\/]"); }
        static bool IsUncPath(string s) { return s.StartsWith(@"\\") && !s.StartsWith(@"\\?\") && !s.StartsWith(@"\\.\"); }
        // a local path -> file:///C:/a%20b/%231.html (each part UTF-8 and percent-escaped like Chromium does: %, #, ?, ;, spaces,
        // non-ASCII); \\server\share\x -> file://server/share/x. null if it isn't a usable path.
        public static string FileUri(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string full;
            try { full = Path.GetFullPath(path.Trim()); } catch { return null; }
            if (IsUncPath(full))
            {
                var parts = full.Substring(2).Split(new[] { '\\', '/' }, StringSplitOptions.None);
                if (parts.Length < 2 || parts[0].Length == 0) return null;
                return "file://" + parts[0].ToLowerInvariant() + "/" + string.Join("/", parts.Skip(1).Select(Esc));
            }
            if (!IsDrivePath(full)) return null;
            var segs = full.Substring(3).Split(new[] { '\\', '/' }, StringSplitOptions.None);
            return "file:///" + char.ToUpperInvariant(full[0]) + ":/" + string.Join("/", segs.Select(Esc));
        }
        static string Esc(string seg)
        {
            var sb = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(seg))
            {
                char c = (char)b;
                bool keep = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || "-._~!$&'()*+,=:@".IndexOf(c) >= 0;
                if (keep && b < 0x80) sb.Append(c); else sb.Append('%').Append(b.ToString("X2"));
            }
            return sb.ToString();
        }
        // one argument -> an address to open, or null (refused: never searched)
        public static string ArgToUrl(string arg, string cwd, Func<string, bool> fileExists)
        {
            if (arg == null) return null;
            var a = arg.Trim();
            if (a.Length >= 2 && a[0] == '"' && a[a.Length - 1] == '"') a = a.Substring(1, a.Length - 2).Trim();
            if (a.Length == 0 || a.Length > 32 * 1024 || a.IndexOf('\0') >= 0) return null;
            if (IsDrivePath(a) || IsUncPath(a)) return FileUri(a);
            var m = Regex.Match(a, @"^([a-zA-Z][a-zA-Z0-9+.\-]*):");
            if (m.Success && m.Groups[1].Value.Length > 1)
            {
                var scheme = m.Groups[1].Value.ToLowerInvariant(); Uri u;
                if ((scheme == "http" || scheme == "https") && Uri.TryCreate(a, UriKind.Absolute, out u) && u.Host.Length > 0 && Omni.IsWeb(u.AbsoluteUri)) return u.AbsoluteUri;
                if (scheme == "file" && Uri.TryCreate(a, UriKind.Absolute, out u) && u.IsFile) return u.AbsoluteUri;
                if (!(Regex.IsMatch(a, @"^[a-zA-Z0-9.\-]+:\d{1,5}(/|$|\?|#)"))) return null;   // javascript:, data:, mailto:, hallow: ... refused
            }
            // a relative path to a file that's there (a console in that folder)
            if (cwd != null && fileExists != null && a.IndexOfAny(Path.GetInvalidPathChars()) < 0)
            {
                try { var p = Path.Combine(cwd, a); if (fileExists(p)) return FileUri(p); } catch { }
            }
            // an address without its scheme ("example.com/x", "localhost:8080"); words and anything else -> refused, never searched
            var x = Omni.Interpret(a, "duckduckgo");
            if (x != null && Omni.IsWeb(x) && x != Omni.SearchUrl("duckduckgo", a) && !x.StartsWith("https://duckduckgo.com/?q=", StringComparison.Ordinal)) return x;
            return null;
        }
        // all address arguments; a path split at its spaces (an unquoted command line) is joined back when the joined path exists
        public static List<string> UrlsFromArgs(IList<string> rest, string cwd, Func<string, bool> fileExists)
        {
            var res = new List<string>(); int n = rest == null ? 0 : rest.Count;
            for (int i = 0; i < n; )
            {
                int took = 0;
                if (fileExists != null && (IsDrivePath(rest[i]) || IsUncPath(rest[i])))
                    for (int j = n; j > i + 1 && took == 0; j--)
                    {
                        var cand = string.Join(" ", rest.Skip(i).Take(j - i));
                        bool ok = false; try { ok = fileExists(cand); } catch { }
                        if (ok) { var u = FileUri(cand); if (u != null) res.Add(u); took = j - i; }
                    }
                if (took > 0) { i += took; continue; }
                var one = ArgToUrl(rest[i], cwd, fileExists); if (one != null) res.Add(one);
                i++;
            }
            return res;
        }
        // what the family pipe's "open" accepts: http, https and file: addresses (local files Windows handed to a second start)
        public static bool Openable(string url, out string normal)
        {
            normal = null; Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme == "http" || u.Scheme == "https") { normal = u.AbsoluteUri; return true; }
            if (u.Scheme == "file" && u.IsFile) { normal = u.AbsoluteUri; return true; }
            return false;
        }
    }

    public partial class BrowserSettings
    {
        [DataMember] public bool DefaultHintDismissed;   // 1.12: the new tab page's "make Hallow your default" hint was closed (never shown again)
    }

    public partial class BrowserWindow
    {
        // ---- Settings > General > Default browser
        public UIElement[] DefaultBrowserSettings()
        {
            var st = DefaultBrowser.Status();
            var line = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var icon = Ui.Icon("circle-info", 15, "Muted"); icon.Margin = new Thickness(0, 0, 10, 0);
            Action show = () =>
            {
                line.Text = st.Text;
                line.SetResourceReference(TextBlock.ForegroundProperty, st.IsHallow ? "GreenText" : "Text");
                icon.Data = Icons.Get(st.IsHallow ? "circle-check" : "circle-info");
                icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, st.IsHallow ? "GreenText" : "AmberText");
            };
            show();
            var status = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(icon, Dock.Left); status.Children.Add(icon); status.Children.Add(line);
            var make = Ui.Btn(st.IsHallow ? "Open Windows' Default apps" : "Make Hallow my default browser", () => { if (!DefaultBrowser.OpenWindowsSettings()) Toast("Windows Settings didn't open. Open Settings > Apps > Default apps and pick Hallow Browser.", 8); }, true, "up-right-from-square");
            var again = Ui.Btn("Check again", () => { st = DefaultBrowser.Status(); show(); DefaultChanged(); }, false, "arrows-rotate");
            Button imp = null; imp = Ui.Btn("Import from Brave...", () => ShowImport(imp), false, "file-import");
            var buttons = Ui.Row(make, again); buttons.Margin = new Thickness(0, 0, 0, 8);
            return new UIElement[] {
                status,
                buttons,
                Ui.Muted(DefaultBrowser.HowTo, 11.5, 12),
                Ui.Switch("Remind me on the new tab page", "A small note on the new tab page while Hallow Browser isn't your default - close it once and it never comes back. Never during a game.", !S.DefaultHintDismissed, on => { S.DefaultHintDismissed = !on; S.Save(); NewTabDefaultChanged(); }),
                Ui.Muted("Moving from Brave? Bring your bookmarks and history over (passwords and cookies stay in Brave; Hallow Keys has its own importer).", 12, 6),
                Ui.Row(imp) };
        }
        // the status may have changed (Windows Settings closed): the new tab page's hint follows
        public void DefaultChanged() { NewTabDefaultChanged(); }
        void NewTabDefaultChanged() { NewTabChanged(); }
        // the new tab page's buttons: "Choose in Windows Settings" and the X (never shown again)
        bool DefaultHintMessage(string m)
        {
            if (m == "hallow:nt-default") { DefaultBrowser.OpenWindowsSettings(); return true; }
            if (m == "hallow:nt-default-off") { S.DefaultHintDismissed = true; S.Save(); NewTabChanged(); return true; }
            return false;
        }
    }

    public static partial class NewTabDefault
    {
        // the hint's text for data.json: null = no hint (dismissed, already the default, or a game is running - nothing during games)
        public static Dictionary<string, object> Hint(BrowserSettings s, bool inGame, DefaultStatus st)
        {
            if (s == null || s.DefaultHintDismissed || inGame || st == null || st.IsHallow) return null;
            return new Dictionary<string, object> { { "name", st.Name ?? (st.HttpsProgId == null ? null : "another app") } };
        }
    }
}
