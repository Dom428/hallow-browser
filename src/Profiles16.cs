// Hallow Browser 1.6 - browser profiles ("Personal", "Gaming"...): each with its own sign-ins, history, bookmarks, sidebar panels,
// settings, sync and private windows. A profile is a WebView2 profile (CoreWebView2ControllerOptions.ProfileName) in the one
// engine folder, and its own Hallow Browser process with its own files (Profiles\<id>). The data you had before 1.6 IS the
// "Default" profile: it keeps its folders and its WebView2 profile, nothing is moved. Shield is loaded into every profile.
// Also: spell check (Windows' spell checker inside the engine) and "Add to dictionary".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        // ---- which profile this process is (set once, at the start, before anything reads a path)
        public static string ProfileId = ProfileStore.DefaultId;
        public static bool IsDefaultProfile { get { return ProfileId == ProfileStore.DefaultId; } }
        // the WebView2 profile: null = the engine's own default profile (the one 1.0-1.5 used, so "Default" keeps everything)
        public static string WebViewProfile { get { return IsDefaultProfile ? null : "Hallow_" + ProfileId; } }
        public static string PrivateWebViewProfile { get { return IsDefaultProfile ? BrowserWindow.PrivateProfile : BrowserWindow.PrivateProfile + "_" + ProfileId; } }
        public static string EngineProfileFolder(string name) { return Path.Combine(ProfileDir, "EBWebView", name == null ? "Default" : "WV2Profile_" + name.ToLowerInvariant()); }
        // the local history store: the Default profile keeps it where 1.2-1.5 did (in the engine folder), others in their own folder
        public static string HistoryDir { get { return IsDefaultProfile ? ProfileDir : LocalDir; } }

        // ---- 1.6 settings (per profile)
        [DataMember] public string MemorySaver = "balanced";   // off (30 min) | balanced (10 min) | max (2 min) | custom (SleepMinutes, as 1.5)
        [DataMember] public bool BatterySaver = true;          // on battery: background tabs and panels are slowed down and sleep sooner
        [DataMember] public bool StartupBoost;                 // OFF by default: a small hidden process stays after you close the browser
        [DataMember] public bool Sounds;                       // OFF by default: tab open / close, download done, game mode on
        [DataMember] public int SoundVolume = 40;              // 0-100
        [DataMember] public bool Ambience;                     // OFF by default: a calm generated loop while the browser is open
        [DataMember] public int AmbienceVolume = 20;
        [DataMember] public bool SpellCheck = true;            // the engine checks spelling as you type (Windows' spell checker)
        [DataMember] public List<string> SpellWords;           // words you added to the dictionary
        [DataMember] public bool NewTabHallow = true;          // your games, recent clips and the Overlay switch on the new tab page

        public static readonly string[] MemoryLevels = { "off", "balanced", "max", "custom" };
        void Defaults16()
        {
            MemorySaver = "balanced"; BatterySaver = true; SoundVolume = 40; AmbienceVolume = 20; SpellCheck = true; NewTabHallow = true;
        }
        void Fix16()
        {
            // 1.5's "sleep after" stays yours: the usual 15 minutes become Balanced, anything else is kept as Custom
            if (SettingsVersion < 3) { MemorySaver = SleepMinutes == 15 ? "balanced" : "custom"; SettingsVersion = 3; }
            if (Array.IndexOf(MemoryLevels, MemorySaver) < 0) MemorySaver = "balanced";
            SoundVolume = Math.Max(0, Math.Min(100, SoundVolume)); AmbienceVolume = Math.Max(0, Math.Min(100, AmbienceVolume));
            if (SpellWords == null) SpellWords = new List<string>();
            SpellWords = SpellWords.Select(SpellRules.Word).Where(x => x != null).Distinct(StringComparer.Ordinal).Take(5000).ToList();
        }
        public static BrowserSettings LoadFrom(string file)
        {
            try { if (File.Exists(file)) using (var f = File.OpenRead(file)) { var s = (BrowserSettings)new DataContractJsonSerializer(typeof(BrowserSettings)).ReadObject(f); if (s != null) { s.Fix(); return s; } } } catch { }
            var d = new BrowserSettings(); d.Fix(); return d;
        }
    }

    [DataContract]
    public class ProfileInfo
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string Color;     // the window's colour chip (#RRGGBB)
        [DataMember] public string Created;
    }

    // %APPDATA%\HallowBrowser\profiles.json: the list of profiles, which one a plain start opens, and the engine-wide spell check language
    [DataContract]
    public class ProfileStore
    {
        public const string DefaultId = "default";
        public static readonly string[] Colors = { "#8B5CF6", "#22C55E", "#3B82F6", "#F59E0B", "#EF4444", "#EC4899", "#14B8A6", "#94A3B8" };
        public static readonly string[] ColorNames = { "Purple", "Green", "Blue", "Amber", "Red", "Pink", "Teal", "Slate" };
        [DataMember] public List<ProfileInfo> Items = new List<ProfileInfo>();
        [DataMember] public string StartWith = DefaultId;
        [DataMember] public string SpellLanguage;      // null = the engine's own (Windows' display language)
        [DataMember] public string Migrated;           // when 1.6 first saw the 1.5 data (made it the Default profile)
        [DataMember] public string Backup;             // where the copy of 1.5's settings files went

        public static string FilePath { get { return Path.Combine(BrowserSettings.RootDir, "profiles.json"); } }
        public static ProfileStore Load()
        {
            var s = BrowserSettings.Read<ProfileStore>(FilePath) ?? new ProfileStore();
            s.Fix(); return s;
        }
        public void Save() { BrowserSettings.WriteFile(FilePath, this, typeof(ProfileStore)); }
        public void Fix()
        {
            if (Items == null) Items = new List<ProfileInfo>();
            Items.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Id) || !ValidId(x.Id));
            Items = Items.GroupBy(x => x.Id).Select(g => g.First()).ToList();
            var d = Items.FirstOrDefault(x => x.Id == DefaultId);
            if (d == null) { d = new ProfileInfo { Id = DefaultId, Name = "Default", Color = Colors[0] }; Items.Insert(0, d); }
            else if (Items.IndexOf(d) != 0) { Items.Remove(d); Items.Insert(0, d); }
            foreach (var x in Items) { x.Name = CleanName(x.Name) ?? (x.Id == DefaultId ? "Default" : x.Id); if (!ValidColor(x.Color)) x.Color = Colors[0]; }
            if (StartWith == null || !Items.Any(x => x.Id == StartWith)) StartWith = DefaultId;
            if (SpellLanguage != null && !SpellRules.ValidTag(SpellLanguage)) SpellLanguage = null;
        }
        public ProfileInfo Find(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return null;
            return Items.FirstOrDefault(x => x.Id == idOrName.ToLowerInvariant()) ?? Items.FirstOrDefault(x => string.Equals(x.Name, idOrName.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        public static bool ValidId(string id) { return id != null && id.Length >= 1 && id.Length <= 24 && id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')); }
        public static bool ValidColor(string c) { return c != null && c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit); }
        public static string CleanName(string n)
        {
            if (n == null) return null; n = new string(n.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
            if (n.Length > 32) n = n.Substring(0, 32).Trim();
            return n.Length == 0 ? null : n;
        }
        // an id from the name: lower-case letters and digits ("Gaming PC" -> "gamingpc"), unique
        public static string NewId(string name, IEnumerable<string> taken)
        {
            var t = new HashSet<string>(taken);
            var b = new string((name ?? "").ToLowerInvariant().Where(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')).Take(18).ToArray());
            if (b.Length == 0 || b == DefaultId) b = "profile";
            var id = b; for (int i = 2; t.Contains(id); i++) id = b + i;
            return id;
        }
        public ProfileInfo Add(string name, string color)
        {
            name = CleanName(name); if (name == null) return null;
            if (Items.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
            var p = new ProfileInfo { Id = NewId(name, Items.Select(x => x.Id)), Name = name, Color = ValidColor(color) ? color : Colors[Items.Count % Colors.Length], Created = DateTime.Now.ToString("s") };
            Items.Add(p); return p;
        }

        // ---- 1.6 migration: the browser's data from before profiles becomes the "Default" profile. Nothing is moved or rewritten:
        // the Default profile keeps 1.5's folders and the engine's default WebView2 profile. A copy of the small settings files is kept
        // in backup-1.5 first (the engine's own folder is far bigger; the installer of 1.6 backs that up).
        public static ProfileStore EnsureMigrated()
        {
            if (File.Exists(FilePath)) return Load();
            var s = new ProfileStore(); s.Fix();
            var root = BrowserSettings.RootDir;
            try
            {
                var olds = Directory.Exists(root) ? Directory.GetFiles(root, "*.json").Where(f => !Path.GetFileName(f).Equals("status.json", StringComparison.OrdinalIgnoreCase)).ToList() : new List<string>();
                if (olds.Count > 0)
                {
                    var bk = Path.Combine(root, "backup-1.5"); Directory.CreateDirectory(bk);
                    foreach (var f in olds) { var to = Path.Combine(bk, Path.GetFileName(f)); if (!File.Exists(to)) File.Copy(f, to); }
                    s.Backup = bk;
                }
                s.Migrated = DateTime.Now.ToString("s");
            }
            catch { }
            if (!BrowserSettings.Frozen) s.Save();
            return s;
        }
    }

    // ---- spell check: words for "Add to dictionary", the languages Windows can check
    public static class SpellRules
    {
        // one word: letters (any script), digits, ' and - inside; 1-64 characters
        public static string Word(string s)
        {
            if (s == null) return null; s = s.Trim();
            if (s.Length == 0 || s.Length > 64 || !char.IsLetter(s[0])) return null;
            foreach (var c in s) if (!char.IsLetterOrDigit(c) && c != '\'' && c != '-' && c != '’') return null;
            return s;
        }
        public static bool ValidTag(string t) { return t != null && t.Length >= 2 && t.Length <= 16 && t.All(c => char.IsLetterOrDigit(c) || c == '-') && char.IsLetter(t[0]); }
        // Chromium's "Custom Dictionary.txt": one word per line, then "checksum_v1 = " and the MD5 of the lines above
        public static string DictionaryFile(IEnumerable<string> words)
        {
            var body = string.Concat(words.Select(w => w + "\n"));
            string sum; using (var md = MD5.Create()) sum = BitConverter.ToString(md.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLowerInvariant();
            return body + "checksum_v1 = " + sum;   // nothing after it: Chromium compares the rest of the file with the MD5 (measured: a newline there made it drop the words)
        }
        public static List<string> WordsIn(string file)
        {
            var res = new List<string>(); if (file == null) return res;
            foreach (var l in file.Replace("\r", "").Split('\n')) { if (l.StartsWith("checksum_v1")) break; var w = Word(l); if (w != null) res.Add(w); }
            return res;
        }

        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISpellCheckerFactory
        {
            [PreserveSig] int get_SupportedLanguages(out IEnumString value);
            [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
        }
        [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")] class SpellCheckerFactory { }
        static List<string> langs;
        // the languages Windows' spell checker has on this PC (Windows Settings > Time & language > Language adds more)
        public static List<string> WindowsLanguages()
        {
            if (langs != null) return langs;
            var res = new List<string>();
            try
            {
                var f = (ISpellCheckerFactory)new SpellCheckerFactory(); IEnumString e;
                if (f.get_SupportedLanguages(out e) == 0 && e != null)
                {
                    var one = new string[1];
                    while (e.Next(1, one, IntPtr.Zero) == 0 && res.Count < 200) if (ValidTag(one[0])) res.Add(one[0]);
                }
            }
            catch { }
            return langs = res;
        }
        public static string LanguageName(string tag)
        {
            try { return new System.Globalization.CultureInfo(tag).DisplayName; } catch { return tag; }
        }
    }

    public static partial class Engine
    {
        // every WebView of this process is on this profile (private windows: the profile's private, InPrivate one)
        public static Task Ensure(WebView2 v, bool priv = false)
        {
            var name = priv ? BrowserSettings.PrivateWebViewProfile : BrowserSettings.WebViewProfile;
            if (name == null) return v.EnsureCoreWebView2Async(Env);
            var o = Env.CreateCoreWebView2ControllerOptions(); o.ProfileName = name; if (priv) o.IsInPrivateModeEnabled = true;
            return v.EnsureCoreWebView2Async(Env, o);
        }
        // the engine-wide spell check language: the environment's language, so it must be the same in every profile's process
        // (profiles.json, not per profile). Measured: the engine ignores the profile preference "spellcheck.dictionaries".
        public static string SpellLanguage() { try { return ProfileStore.Load().SpellLanguage; } catch { return null; } }
        public static BrowserSettings DefaultProfileSettings() { return BrowserSettings.LoadFrom(Path.Combine(BrowserSettings.RootDir, "settings.json")); }
        // "Add to dictionary": the profile's Custom Dictionary.txt (Chromium's own file), written before the engine loads the profile
        // (measured: the words are honoured from the next start; the engine has no way to add a word while it runs)
        public static void WriteSpellWords(BrowserSettings s)
        {
            try
            {
                var f = Path.Combine(BrowserSettings.EngineProfileFolder(BrowserSettings.WebViewProfile), "Custom Dictionary.txt");
                if ((s.SpellWords == null || s.SpellWords.Count == 0) && !File.Exists(f)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, SpellRules.DictionaryFile(s.SpellWords ?? new List<string>()), new UTF8Encoding(false));
            }
            catch { }
        }
        // spell check off: editable fields don't ask for it (the attribute is inherited from the page's root)
        public const string SpellOffScript = "try{document.documentElement.setAttribute('spellcheck','false');}catch(e){}";
    }

    public static partial class Program
    {
        public static ProfileStore Profiles;
        public static ProfileInfo CurrentProfile { get { var s = Profiles; return s == null ? null : s.Find(BrowserSettings.ProfileId); } }
        // the family pipe: the Default profile is "Browser" (what Hallow apps know); another profile answers on "Browser-<id>"
        // (the hyphen keeps it out of Hub's list of Hallow apps)
        public static string PipeApp(string id) { return id == null || id == ProfileStore.DefaultId ? "Browser" : "Browser-" + id; }
        public static string MutexName(string id, string leaf) { return @"Local\HallowBrowser" + leaf + (id == null || id == ProfileStore.DefaultId ? "" : ".p." + id); }
        // --profile <id or name>, --app <url>; the rest are addresses
        public static void ParseArgs(string[] args, out string profile, out string app, out List<string> rest)
        {
            profile = null; app = null; rest = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--profile" && i + 1 < args.Length) { profile = args[++i]; continue; }
                if (args[i] == "--app" && i + 1 < args.Length) { app = args[++i]; continue; }
                if (args[i].StartsWith("--")) continue;
                rest.Add(args[i]);
            }
        }
        public static string ExePath { get { try { return Process.GetCurrentProcess().MainModule.FileName; } catch { return System.Reflection.Assembly.GetExecutingAssembly().Location; } } }
        // opens a profile's window (its own process: started, or the running one is asked through its pipe), optionally with an address
        public static bool LaunchProfile(string id, string url, string app = null)
        {
            var p = Profiles == null ? null : Profiles.Find(id); if (p == null) return false;
            if (url != null && !Omni.IsWeb(url)) url = null;
            try
            {
                if (Family.Running(PipeApp(p.Id)))
                {
                    var r = Family.Send(PipeApp(p.Id), app != null ? "app " + app : url == null ? "open" : "open " + url, 4000);
                    if (r != null && r.StartsWith("ok")) return true;
                }
                var a = "--profile " + p.Id + (app != null ? " --app \"" + app + "\"" : "") + (url != null ? " \"" + url + "\"" : "");
                var psi = new ProcessStartInfo(ExePath, a) { UseShellExecute = false };
                Process.Start(psi);
                LaunchedProfiles++;
                return true;
            }
            catch { return false; }
        }
        public static int LaunchedProfiles;
    }

    public partial class BrowserWindow
    {
        // ---- the profile's colour and name in the title bar (when there's more than one profile)
        Border profChip, profLine;
        public static Color ProfileColor(ProfileInfo p) { try { return (Color)ColorConverter.ConvertFromString(p == null ? ProfileStore.Colors[0] : p.Color); } catch { return Color.FromRgb(0x8B, 0x5C, 0xF6); } }
        public void ProfileChrome()
        {
            RefreshProfileButton();   // 1.18: the profile button on the toolbar
            if (snap && Program.Profiles == null) return;
            if (!snap && JumpListDone && Program.First == this) ScheduleJumpList(300);   // 1.7: the jump list lists the profiles
            var top = (Grid)root.Children[0];
            var logoRow = top.Children.OfType<StackPanel>().FirstOrDefault(); if (logoRow == null) return;
            if (profChip != null) { logoRow.Children.Remove(profChip); top.Children.Remove(profLine); profChip = null; profLine = null; }
            var store = Program.Profiles; var cur = Program.CurrentProfile;
            if (store == null || cur == null || (store.Items.Count < 2 && BrowserSettings.IsDefaultProfile)) return;
            var col = ProfileColor(cur);
            var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(col), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var tx = new TextBlock { Text = cur.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis };
            tx.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var bd = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 10, 2), BorderThickness = new Thickness(1), Child = Ui.Row(dot, tx), Background = new SolidColorBrush(Color.FromArgb(0x26, col.R, col.G, col.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, col.R, col.G, col.B)) };
            var b = new Button { Style = Ui.S("Tool"), Width = double.NaN, Height = 30, Padding = new Thickness(0), Margin = new Thickness(6, 0, 4, 0), Content = bd, ToolTip = "Profile: " + cur.Name + " - click to switch or add a profile", VerticalAlignment = VerticalAlignment.Center };
            b.Click += (a, e) => ProfileFlyout(b);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            profChip = new Border { Child = b, VerticalAlignment = VerticalAlignment.Center };
            logoRow.Children.Add(profChip);
            profLine = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(col), IsHitTestVisible = false };
            Grid.SetColumnSpan(profLine, 4); top.Children.Add(profLine);
            UpdateTitle();
        }
        public string ProfileChipText { get { return profChip == null ? null : Program.CurrentProfile.Name; } }
        public static string TitleSuffix { get { var p = Program.CurrentProfile; return p == null || (BrowserSettings.IsDefaultProfile && (Program.Profiles == null || Program.Profiles.Items.Count < 2)) ? "" : " (" + p.Name + ")"; } }

        // ---- the switcher: every profile (this one marked), "Add a profile", "Manage profiles"
        public void ProfileFlyout(UIElement at)
        {
            var sp = new StackPanel(); Popup p = null;
            Action<Action> run = a => { if (p != null) p.IsOpen = false; a(); };
            foreach (var x in ProfileMenuRows(run)) sp.Children.Add(x);
            p = Panels.ShowFlyout(at, sp, 280);
        }
        public List<UIElement> ProfileMenuRows(Action<Action> run)
        {
            var res = new List<UIElement>(); var store = Program.Profiles; if (store == null) return res;
            var head = Ui.Label("Profiles"); head.Margin = new Thickness(12, 8, 12, 4); res.Add(head);
            foreach (var pr in store.Items)
            {
                var pp = pr; bool here = pr.Id == BrowserSettings.ProfileId;
                var row = Ui.MenuRow(null, pr.Name + (here ? "  (this window)" : ""), Family.Running(Program.PipeApp(pr.Id)) && !here ? "open" : null, () => run(() => { if (here) Bring(); else if (!Program.LaunchProfile(pp.Id, null)) Toast("Couldn't open the " + pp.Name + " profile.", 5); }));
                var g = row.Content as Grid;
                if (g != null) g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(ProfileColor(pr)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
                res.Add(row);
            }
            res.Add(Ui.MenuRow("user-plus", "Add a profile...", null, () => run(() => { ShowSettingsSection("profiles"); })));
            res.Add(Ui.MenuRow("users", "Manage profiles", null, () => run(() => ShowSettingsSection("profiles"))));
            return res;
        }
        public void ShowSettingsSection(string which) { NewTab(Omni.SettingsUrl, true); }

        // ---- the page's own menu: "Add to dictionary" on a misspelled word, "Open link in profile" on a link
        public static List<string> LastMenuItems = new List<string>();
        public static string LastMenuWord;
        public void Hook16(CoreWebView2 c)
        {
            if (!S.SpellCheck) { var _ = c.AddScriptToExecuteOnDocumentCreatedAsync(Engine.SpellOffScript); }
            if (AppMode) AppScope(c);
            c.ContextMenuRequested += (a, e) =>
            {
                try
                {
                    var items = e.MenuItems; var t = e.ContextMenuTarget; LastMenuWord = null;
                    int lastSpell = -1; for (int i = 0; i < items.Count; i++) if (items[i].Name == "spellcheck") lastSpell = i;
                    if (lastSpell >= 0 && t.IsEditable && t.HasSelection)
                    {
                        var word = SpellRules.Word(t.SelectionText);
                        if (word != null && !Private)
                        {
                            LastMenuWord = word;
                            var it = Engine.Env.CreateContextMenuItem("Add “" + word + "” to dictionary", null, CoreWebView2ContextMenuItemKind.Command);
                            it.CustomItemSelected += (x, y) => Dispatcher.BeginInvoke(new Action(() => AddWord(word)));
                            items.Insert(lastSpell + 1, it);
                        }
                    }
                    var store = Program.Profiles;
                    if (t.HasLinkUri && Omni.IsWeb(t.LinkUri) && store != null && store.Items.Count > 1)
                    {
                        var url = t.LinkUri;
                        var sub = Engine.Env.CreateContextMenuItem("Open link in profile", null, CoreWebView2ContextMenuItemKind.Submenu);
                        foreach (var pr in store.Items.Where(x => x.Id != BrowserSettings.ProfileId))
                        {
                            var pp = pr; var ci = Engine.Env.CreateContextMenuItem(pr.Name, null, CoreWebView2ContextMenuItemKind.Command);
                            ci.CustomItemSelected += (x, y) => Dispatcher.BeginInvoke(new Action(() => OpenInProfile(pp.Id, url)));
                            sub.Children.Add(ci);
                        }
                        int at = -1; for (int i = 0; i < items.Count; i++) if (items[i].Name == "openLinkInNewWindow" || items[i].Name == "openLinkInNewTab") at = i;
                        items.Insert(at >= 0 ? at + 1 : Math.Min(items.Count, 2), sub);
                    }
                    LastMenuItems = items.Select(x => x.Kind == CoreWebView2ContextMenuItemKind.Submenu ? x.Label + ":[" + string.Join("|", x.Children.Select(y => y.Label)) + "]" : x.Name == "custom" ? x.Label : x.Name).ToList();
                }
                catch { }
            };
        }
        public bool OpenInProfile(string id, string url)
        {
            var p = Program.Profiles == null ? null : Program.Profiles.Find(id);
            if (p == null || !Omni.IsWeb(url)) return false;
            if (p.Id == BrowserSettings.ProfileId) { NewTab(url, true); return true; }
            var ok = Program.LaunchProfile(p.Id, url);
            Toast(ok ? "Opening it in the " + p.Name + " profile." : "Couldn't open the " + p.Name + " profile.", 3);
            return ok;
        }
        public void AddWord(string word)
        {
            word = SpellRules.Word(word); if (word == null) return;
            if (!S.SpellWords.Contains(word)) { S.SpellWords.Add(word); S.Save(); }
            Engine.WriteSpellWords(S);
            Toast("“" + word + "” is in your dictionary from the next start of Hallow Browser (WebView2 can't add a word while it runs).", 6);
        }

        // ---- Settings > Profiles
        public UIElement[] ProfileSettings()
        {
            var items = new List<UIElement>(); var store = Program.Profiles ?? ProfileStore.Load();
            items.Add(Ui.Muted("Each profile has its own sign-ins and cookies, history, bookmarks, sidebar panels, settings, sync and private windows - like separate browsers (e.g. \"Personal\" and \"Gaming\"). Hallow Shield runs in every profile. This window is the " + (Program.CurrentProfile != null ? Program.CurrentProfile.Name : "Default") + " profile.", 12, 8));
            var list = new StackPanel(); items.Add(list);
            Action fill = null;
            fill = () =>
            {
                list.Children.Clear();
                foreach (var pr in store.Items.ToList())
                {
                    var pp = pr; var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(ProfileColor(pr)), VerticalAlignment = VerticalAlignment.Center });
                    var nm = new StackPanel(); nm.Children.Add(new TextBlock { Text = pr.Name + (pr.Id == BrowserSettings.ProfileId ? "  (this window)" : "") + (store.StartWith == pr.Id ? "  - opens at start" : "") });
                    nm.Children.Add(Ui.Muted(pr.Id == ProfileStore.DefaultId ? "Your data from before profiles - nothing was moved." : "Files: Profiles\\" + pr.Id, 11)); Grid.SetColumn(nm, 1); g.Children.Add(nm);
                    var btns = Ui.Row();
                    if (pr.Id != BrowserSettings.ProfileId) btns.Children.Add(Ui.Btn("Open", () => Program.LaunchProfile(pp.Id, null), false, "up-right-from-square"));
                    btns.Children.Add(Ui.Btn("Open at start", () => { store.StartWith = pp.Id; store.Save(); fill(); }));
                    var colorBtn = Ui.Btn("Colour", null, false, "circle"); colorBtn.Click += (a, b) => { var i = Array.IndexOf(ProfileStore.Colors, pp.Color); pp.Color = ProfileStore.Colors[(i + 1) % ProfileStore.Colors.Length]; store.Save(); fill(); if (pp.Id == BrowserSettings.ProfileId) foreach (var w in Program.Windows) w.ProfileChrome(); };
                    btns.Children.Add(colorBtn);
                    if (pr.Id != ProfileStore.DefaultId)
                        btns.Children.Add(Ui.Btn("Remove from the list", () =>
                        {
                            if (pp.Id == BrowserSettings.ProfileId || Family.Running(Program.PipeApp(pp.Id))) { Toast("Close the " + pp.Name + " profile's windows first.", 4); return; }
                            store.Items.Remove(pp); store.Fix(); store.Save(); fill(); foreach (var w in Program.Windows) w.ProfileChrome();
                            Toast("Removed from the list. Its files stay in Profiles\\" + pp.Id + " (delete that folder yourself if you want them gone).", 8);
                        }, false, "trash-can"));
                    Grid.SetColumn(btns, 2); g.Children.Add(btns);
                    list.Children.Add(g);
                }
            };
            fill();
            var nb = new TextBox { Style = Ui.S("Field"), Width = 200, Height = 28, Margin = new Thickness(0, 0, 8, 0), ToolTip = "A name, e.g. Gaming" };
            string pick = ProfileStore.Colors[store.Items.Count % ProfileStore.Colors.Length];
            var chips = Ui.Chips(ProfileStore.Colors.Select((c, i) => new KeyValuePair<string, string>(c, ProfileStore.ColorNames[i])).ToList(), pick, v => pick = v);
            var msg = Ui.Muted("", 11.5);
            Action add = () =>
            {
                var p = store.Add(nb.Text, pick);
                if (p == null) { msg.Text = "Pick a name that isn't used yet."; return; }
                store.Save(); nb.Text = ""; msg.Text = "Added \"" + p.Name + "\" - open it from the profile button in the title bar."; fill();
                foreach (var w in Program.Windows) w.ProfileChrome();
            };
            nb.KeyDown += (a, b) => { if (b.Key == System.Windows.Input.Key.Enter) { add(); b.Handled = true; } };
            items.Add(new Border { Height = 6 });
            items.Add(Ui.Row(nb, Ui.Btn("Add a profile", add, true, "user-plus")));
            items.Add(chips); items.Add(msg);
            items.Add(Ui.Muted("Right-click a link > Open link in profile. Each profile runs as its own Hallow Browser process on the same engine. Sync: each profile syncs its own things - a profile other than Default uses a sub-folder of the sync folder you pick (profile-<id>). Secure DNS and the spell check language are engine-wide (set them in the Default profile).", 11.5, 0));
            return items.ToArray();
        }

        // ---- Settings > Spell check
        public UIElement[] SpellSettings()
        {
            var items = new List<UIElement>(); var store = Program.Profiles ?? ProfileStore.Load();
            items.Add(Ui.Switch("Check spelling as I type", "Misspelled words get a red line; right-click one for suggestions and \"Add to dictionary\". Applies to pages opened after you change it.", S.SpellCheck, on => { S.SpellCheck = on; S.Save(); }));
            var langs = SpellRules.WindowsLanguages();
            items.Add(Ui.Muted("Language (for every profile; applies after a restart)", 12, 6));
            var choices = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", "Windows' language") };
            foreach (var l in langs) choices.Add(new KeyValuePair<string, string>(l, SpellRules.LanguageName(l)));
            if (store.SpellLanguage != null && !langs.Contains(store.SpellLanguage)) choices.Add(new KeyValuePair<string, string>(store.SpellLanguage, SpellRules.LanguageName(store.SpellLanguage)));
            items.Add(Ui.Chips(choices, store.SpellLanguage ?? "", v => { var s2 = ProfileStore.Load(); s2.SpellLanguage = v == "" ? null : v; s2.Save(); Program.Profiles = s2; Toast("The spell check language changes when Hallow Browser next starts (all its windows closed).", 5); }));
            items.Add(Ui.Muted("Honestly: WebView2 checks spelling with Windows' own spell checker - nothing is downloaded by the browser. It ignores Chromium's dictionary list, so the language is the engine's language: picking one also changes the language pages are asked for (Accept-Language) and of the right-click menu. Only languages Windows has spelling for are offered (" + (langs.Count == 0 ? "none found" : string.Join(", ", langs)) + "); add more in Windows Settings > Time & language > Language - Windows gets them from Microsoft.", 11.5, 8));
            var wl = new WrapPanel();
            Action fill = null;
            fill = () =>
            {
                wl.Children.Clear();
                foreach (var w in S.SpellWords.ToList())
                {
                    var ww = w; var x = Ui.Tool("xmark", "Remove this word", () => { S.SpellWords.Remove(ww); S.Save(); Engine.WriteSpellWords(S); fill(); }, 9); x.Width = 20; x.Height = 20;
                    var chip = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 3, 4, 3), Margin = new Thickness(0, 0, 6, 6), Child = Ui.Row(new TextBlock { Text = w, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, x) };
                    chip.SetResourceReference(Border.BorderBrushProperty, "Line"); chip.SetResourceReference(Border.BackgroundProperty, "PanelHi"); wl.Children.Add(chip);
                }
                if (S.SpellWords.Count == 0) wl.Children.Add(Ui.Muted("Your dictionary: no words yet (right-click a misspelled word > Add to dictionary).", 11.5));
            };
            fill(); items.Add(wl);
            return items.ToArray();
        }
    }
}
