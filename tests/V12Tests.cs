// Hallow Browser 1.2 - unit tests: tab groups (order, drops, colours and their contrast in both looks), fuzzy search, the history
// store, bookmarks, workspace names and splits, sessions with groups (and 1.1 sessions still loading), the new settings, the reader
// document and screenshot crops / names.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HallowBrowser.Tests
{
    public static class V12
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All() { GroupTests(); FuzzyTests(); HistoryTests(); BookmarkTests(); WorkspaceTests(); SessionTests(); SettingsTests(); ReaderTests(); ShotTests(); ColourTests(); }

        static void GroupTests()
        {
            Eq(GroupRules.Normalize("Gray"), "grey", "group colour: gray = grey");
            Eq(GroupRules.Normalize("teal"), "blue", "group colour: unknown -> blue");
            Eq(GroupRules.Key("purple"), "GroupPurple", "group colour resource key");
            Eq(GroupRules.NextColor(new[] { "blue", "red" }), "yellow", "next group gets the next unused colour");
            Eq(GroupRules.Name("  ", 3), "Group 3", "empty group name -> Group n");
            Eq(GroupRules.Name(new string('x', 60), 1).Length, 40, "group names are cut at 40");
            // contiguous: a group sits where its first tab is
            var a = new object(); var b = new object();
            var items = new[] { "1", "a1", "2", "a2", "b1", "3", "b2" };
            Func<string, object> grp = s => s.StartsWith("a") ? a : s.StartsWith("b") ? b : null;
            Eq(string.Join(",", GroupRules.Contiguous(items, grp)), "1,a1,a2,2,b1,b2,3", "group tabs stay next to each other");
            Eq(string.Join(",", GroupRules.Contiguous(new[] { "1", "2" }, grp)), "1,2", "no groups: order unchanged");
            // drops
            string G = "G", H = "H";
            Eq(GroupRules.Drop<string>(G, G, null, null, false), "G", "dropped between two tabs of a group = into it");
            Eq(GroupRules.Drop<string>(G, null, null, null, false), null, "dropped after a group's last tab = not in it");
            Eq(GroupRules.Drop<string>(null, null, null, H, false), "H", "dropped on a group's label = into it");
            Eq(GroupRules.Drop<string>(null, null, G, null, true), null, "dragged out of its group = no group");
            Eq(GroupRules.Drop<string>(G, null, G, null, true), "G", "moved inside its own group = stays");
            Eq(GroupRules.Drop<string>(null, null, G, null, false), "G", "a group's only tab takes its group along");
            Eq(GroupRules.Drop<string>(G, G, H, null, true), "G", "dropped inside another group = that group");
        }

        static void FuzzyTests()
        {
            Ok(Fuzzy.Score("", "anything") == 0, "fuzzy: empty query matches all");
            Ok(Fuzzy.Score("zzz", "Hallow Browser") < 0, "fuzzy: no match");
            Ok(Fuzzy.Score("hbr", "Hallow Browser") > 0, "fuzzy: letters in order (hbr -> Hallow Browser)");
            Ok(Fuzzy.Score("raid", "Delta Force - season guide") < 0, "fuzzy: letters spread far apart don't count");
            Ok(Fuzzy.Score("brow", "Hallow Browser") > Fuzzy.Score("hbr", "Hallow Browser"), "fuzzy: a piece of a word beats scattered letters");
            Ok(Fuzzy.Score("set", "Settings") > Fuzzy.Score("set", "Reset zoom"), "fuzzy: the start of the text beats the middle");
            Ok(Fuzzy.Score("vert tabs", "Vertical tabs") > 0 && Fuzzy.Score("vert zebra", "Vertical tabs") < 0, "fuzzy: every word must match");
            Ok(Fuzzy.Score("TABS", "vertical tabs") > 0, "fuzzy: case doesn't matter");
        }

        static string Tmp(string name) { var d = Path.Combine(Path.GetTempPath(), "HallowBrowserUnit12-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(d); return Path.Combine(d, name); }

        static void HistoryTests()
        {
            var f = Tmp("hist.json");
            var h = new HistoryStore(f); h.Load();
            var t0 = new DateTime(2026, 11, 1, 12, 0, 0);
            h.Add("https://a.example/", "Alpha", t0); h.Add("https://b.example/", "Bravo", t0.AddMinutes(1)); h.Add("https://a.example/", null, t0.AddMinutes(2));
            Eq(h.Count, 2, "history: one entry per address");
            var all = h.All(); Eq(all[0].Url, "https://a.example/", "history: newest first"); Eq(all[0].Visits, 2, "history: visits counted"); Eq(all[0].Title, "Alpha", "history: an empty title keeps the old one");
            h.Add("javascript:alert(1)", "x", t0); h.Add("chrome-extension://x/y", "x", t0);
            Eq(h.Count, 2, "history: only http/https pages");
            h.SetTitle("https://b.example/", "Bravo page"); Eq(h.All()[1].Title, "Bravo page", "history: the title follows the page");
            var s = h.Search("brav", 5); Ok(s.Count == 1 && s[0].Key.Url == "https://b.example/", "history: search by title");
            s = h.Search("a.example", 5); Ok(s.Count >= 1 && s[0].Key.Url == "https://a.example/", "history: search by address");
            h.Save(); Ok(File.Exists(f), "history: saved to its file");
            var h2 = new HistoryStore(f);
            h2.Add("https://c.example/", "Charlie", t0.AddMinutes(5));   // before the file is read: stays in front
            h2.Load(); var l2 = h2.All();
            Ok(l2.Count == 3 && l2[0].Url == "https://c.example/" && l2[1].Url == "https://a.example/", "history: a page added before loading stays newest; the file's come after");
            for (int i = 0; i < HistoryStore.Max + 10; i++) h2.Add("https://n.example/" + i, "n" + i, t0);
            Eq(h2.Count, HistoryStore.Max, "history: at most 5,000 pages");
            h2.Clear(); Ok(h2.Count == 0 && !File.Exists(f), "history: Clear empties it and deletes the file");
            var h3 = new HistoryStore(f); h3.Add("https://x.example/", "x", t0); h3.Save(); Ok(!File.Exists(f), "history: never written before its file was read (nothing lost)");
            try { Directory.Delete(Path.GetDirectoryName(f), true); } catch { }
        }

        static void BookmarkTests()
        {
            var f = Tmp("bm.json");
            var b = new BookmarkStore(f); b.Load();
            Ok(b.Toggle("https://a.example/", "Alpha") && b.Has("https://a.example/"), "bookmark: Ctrl+D adds");
            Ok(!b.Toggle("ftp://x", "x") && !b.Has("ftp://x"), "bookmark: only web pages");
            var b2 = new BookmarkStore(f); b2.Load(); Ok(b2.Has("https://a.example/") && b2.All()[0].Title == "Alpha", "bookmark: saved");
            Ok(!b2.Toggle("https://a.example/", "Alpha") && !b2.Has("https://a.example/"), "bookmark: Ctrl+D again removes");
            try { Directory.Delete(Path.GetDirectoryName(f), true); } catch { }
        }

        static void WorkspaceTests()
        {
            Eq(WorkspaceRules.UniqueName("Gaming", new[] { "Default" }), "Gaming", "workspace name");
            Eq(WorkspaceRules.UniqueName("gaming", new[] { "Gaming", "Gaming 2" }), "gaming 3", "workspace names stay different");
            Eq(WorkspaceRules.UniqueName("  ", new string[0]), "Workspace", "empty workspace name");
            int l, r;
            Ok(WorkspaceRules.ParseSplit("1,3", 4, out l, out r) && l == 1 && r == 3, "split: parsed");
            Ok(!WorkspaceRules.ParseSplit("1,4", 4, out l, out r) && !WorkspaceRules.ParseSplit("2,2", 4, out l, out r) && !WorkspaceRules.ParseSplit("x", 4, out l, out r) && !WorkspaceRules.ParseSplit(null, 4, out l, out r), "split: bad values ignored");
            Eq(WorkspaceRules.TabCount(null), 0, "tab count of an empty workspace");
        }

        static T RoundTrip<T>(string json) { using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms); }
        static string ToJson<T>(T o) { using (var ms = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(ms, o); return Encoding.UTF8.GetString(ms.ToArray()); } }

        static void SessionTests()
        {
            var old = RoundTrip<Session>("{\"Active\":1,\"Tabs\":[{\"Title\":\"A\",\"Url\":\"https://a.example/\"},{\"Title\":\"B\",\"Url\":\"https://b.example/\"}]}");
            Ok(old.Tabs.Count == 2 && old.Active == 1 && old.Groups == null && old.Split == null && old.Tabs[0].Group == null, "a 1.1 session.json still loads (no groups, no split)");
            var s = new Session { Active = 0, Split = "0,1", Groups = new List<SessionGroup> { new SessionGroup { Id = "g1", Name = "Raid", Color = "purple", Collapsed = true } } };
            s.Tabs.Add(new SessionTab { Url = "https://a.example/", Group = "g1" }); s.Tabs.Add(new SessionTab { Url = "https://b.example/" });
            var back = RoundTrip<Session>(ToJson(s));
            Ok(back.Groups.Count == 1 && back.Groups[0].Collapsed && back.Groups[0].Color == "purple" && back.Tabs[0].Group == "g1" && back.Split == "0,1", "a session keeps its groups (name, colour, collapsed) and split");
            var ws = RoundTrip<WorkspaceStore>(ToJson(new WorkspaceStore { Current = "w2", Items = new List<Workspace> { new Workspace { Id = "default", Name = "Default", Session = s }, new Workspace { Id = "w2", Name = "Work" } } }));
            Ok(ws.Current == "w2" && ws.Items.Count == 2 && ws.Items[0].Session.Tabs.Count == 2 && ws.Items[1].Session == null, "workspaces.json round trip");
            var sv = RoundTrip<SavedList>(ToJson(new SavedList { Items = new List<SavedSession> { new SavedSession { Name = "Raid night", Saved = "2026-11-21T20:00:00", Session = s } } }));
            Ok(sv.Items.Count == 1 && sv.Items[0].Session.Groups.Count == 1, "saved sessions round trip");
        }

        static void SettingsTests()
        {
            var s = RoundTrip<BrowserSettings>("{\"SettingsVersion\":2,\"Search\":\"google\"}"); s.Fix();
            Ok(!s.VerticalTabs && !s.VTabsCollapsed && s.SidebarSide == "auto" && s.ReaderFontSize == 18 && s.ReaderWidth == "medium" && s.KeepHistory, "1.1 settings -> 1.2 defaults (horizontal tabs, history on, reader 18 px medium)");
            var b = RoundTrip<BrowserSettings>("{\"SidebarSide\":\"top\",\"ReaderFontSize\":3,\"ReaderWidth\":\"huge\"}"); b.Fix();
            Ok(b.SidebarSide == "auto" && b.ReaderFontSize == 18 && b.ReaderWidth == "medium", "bad 1.2 values are put right");
            Ok(Reader.PageWidth("narrow") < Reader.PageWidth("medium") && Reader.PageWidth("medium") < Reader.PageWidth("wide"), "reader widths");
        }

        static void ReaderTests()
        {
            var inner = "{\"ok\":true,\"title\":\"T\",\"byline\":\"By A\",\"site\":\"ex.com\",\"words\":460,\"blocks\":[{\"t\":\"p\",\"x\":\"One\"},{\"t\":\"h2\",\"x\":\"Two\"},{\"t\":\"script\",\"x\":\"bad\"},{\"t\":\"img\",\"x\":\"\",\"src\":\"https://ex.com/a.png\"}]}";
            var d = ReaderDoc.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(inner));
            Ok(d.Ok && d.Title == "T" && d.Site == "ex.com" && d.Blocks.Count == 3 && d.Minutes == 2, "reader: the extractor's result is read (unknown kinds dropped, 2 min read)");
            Ok(!ReaderDoc.Parse("\"{\\\"ok\\\":true,\\\"blocks\\\":[]}\"").Ok, "reader: nothing to show = not ok");
            Ok(!ReaderDoc.Parse("null").Ok && !ReaderDoc.Parse("garbage").Ok, "reader: broken results = not ok");
            Ok(Reader.Extract.Contains("JSON.stringify") && !Reader.Extract.Contains("innerHTML"), "reader: the extractor returns text blocks, never page HTML");
        }

        static byte[] Png(int w, int h)
        {
            var px = new byte[w * h * 4]; for (int i = 0; i < px.Length; i++) px[i] = (byte)(i % 251);
            var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(b));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }
        static void ShotTests()
        {
            var png = Png(200, 100);
            var c = Shots.Crop(png, new Rect(0.25, 0.5, 0.5, 0.5)); var sz = Shots.PixelSize(c);
            Ok(sz.Width == 100 && sz.Height == 50, "screenshot: a region crop has the right size (" + sz.Width + "x" + sz.Height + ")");
            var c2 = Shots.Crop(png, new Rect(0.9, 0.9, 0.5, 0.5)); var s2 = Shots.PixelSize(c2);
            Ok(s2.Width == 20 && s2.Height == 10, "screenshot: a region past the edge is cut to the picture");
            var t = new DateTime(2026, 11, 21, 20, 5, 9);
            Eq(Shots.FileName(t, n => false), "Hallow Browser 2026-11-21 20.05.09.png", "screenshot: file name");
            Eq(Shots.FileName(t, n => n == "Hallow Browser 2026-11-21 20.05.09.png"), "Hallow Browser 2026-11-21 20.05.09 (2).png", "screenshot: never overwrites");
            Ok(Shots.Folder.Contains("Hallow Browser") && Shots.Folder.StartsWith(BrowserSettings.TestDir ?? "?"), "screenshot: tests save into the test folder");
        }

        static void ColourTests()
        {
            var x = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("HallowBrowser.Theme.xaml")).ReadToEnd();
            var dark = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Accent.ApplyTo(x, "#F07820"));
            var light = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Accent.ApplyTo(Look.LightXaml(x, "#F07820"), "#F07820"));
            Func<ResourceDictionary, string, string> col = (rd, k) => { var o = rd[k]; var c = o is SolidColorBrush ? ((SolidColorBrush)o).Color : (Color)o; return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); };
            foreach (var g in GroupRules.Colors)
            {
                var k = GroupRules.Key(g);
                // group names are text on the strip / column, the panels and the hover colour
                var dmin = new[] { "Strip", "Bar", "PanelHi", "TabOn" }.Min(bg => Look.Contrast(col(dark, k), col(dark, bg)));
                var lmin = new[] { "Strip", "Bar", "PanelHi", "TabOn", "Bg" }.Min(bg => Look.Contrast(col(light, k), col(light, bg)));
                Ok(dmin >= 4.5, "group colour " + g + " (dark) as text: " + dmin.ToString("0.00") + ":1 at worst");
                Ok(lmin >= 4.5, "group colour " + g + " (light) as text: " + lmin.ToString("0.00") + ":1 at worst");
                Ok(col(dark, k) != col(light, k), "group colour " + g + " has its own light value");
            }
        }
    }
}
