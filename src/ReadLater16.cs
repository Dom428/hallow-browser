// Hallow Browser 1.6 - the reading list ("Save for later", Ctrl+Shift+D: read / unread) and short notes per page, both in the
// sidebar (two panels of the browser's own at the top of the rail). Kept on this PC in the profile's folder (reading-list.json,
// notes.json); the notes also go through the 1.3 folder sync when sync is on (encrypted when the sync password is set).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HallowBrowser
{
    [DataContract] public class ReadItem { [DataMember] public string Url; [DataMember] public string Title; [DataMember] public string Added; [DataMember] public bool Read; [DataMember] public string ReadAt; }
    [DataContract] public class ReadFile { [DataMember] public List<ReadItem> Items = new List<ReadItem>(); }
    [DataContract] public class PageNote { [DataMember] public string Key; [DataMember] public string Url; [DataMember] public string Title; [DataMember] public string Text; [DataMember] public string Updated; }
    [DataContract] public class NoteFile { [DataMember] public List<PageNote> Items = new List<PageNote>(); }

    public static class ReadingList
    {
        public const int Max = 500;
        static List<ReadItem> items; static readonly object gate = new object();
        static string FilePath { get { return System.IO.Path.Combine(BrowserSettings.Dir, "reading-list.json"); } }
        public static List<ReadItem> Items { get { lock (gate) { Load(); return items.ToList(); } } }
        static void Load() { if (items != null) return; var f = BrowserSettings.Read<ReadFile>(FilePath); items = f == null || f.Items == null ? new List<ReadItem>() : f.Items.Where(x => x != null && Omni.IsWeb(x.Url)).ToList(); }
        static void Save() { BrowserSettings.WriteFile(FilePath, new ReadFile { Items = items }, typeof(ReadFile)); }
        public static void Reset() { lock (gate) items = null; }
        public static void UseForSnap(List<ReadItem> list) { lock (gate) items = list; }   // --snap: made-up items (never saved: --snap saves nothing)
        public static int Count(bool unreadOnly) { lock (gate) { Load(); return items.Count(x => !unreadOnly || !x.Read); } }
        // newest first; saving a page again moves it to the top as unread
        public static bool Add(string url, string title)
        {
            if (!Omni.IsWeb(url)) return false;
            lock (gate)
            {
                Load(); items.RemoveAll(x => x.Url == url);
                items.Insert(0, new ReadItem { Url = url, Title = string.IsNullOrWhiteSpace(title) ? Omni.HostOf(url) : title.Trim(), Added = DateTime.Now.ToString("s") });
                if (items.Count > Max) items.RemoveRange(Max, items.Count - Max);
                Save(); return true;
            }
        }
        public static void SetRead(string url, bool read) { lock (gate) { Load(); foreach (var x in items.Where(x => x.Url == url)) { x.Read = read; x.ReadAt = read ? DateTime.Now.ToString("s") : null; } Save(); } }
        public static void Remove(string url) { lock (gate) { Load(); items.RemoveAll(x => x.Url == url); Save(); } }
        public static bool Has(string url) { lock (gate) { Load(); return items.Any(x => x.Url == url); } }
    }

    public static class PageNotes
    {
        public const int MaxText = 4000, Max = 2000;
        static List<PageNote> items; static readonly object gate = new object();
        static string FilePath { get { return System.IO.Path.Combine(BrowserSettings.Dir, "notes.json"); } }
        // one note per page: scheme, host and path (the query and #part left out, so a page's note follows it)
        public static string Key(string url)
        {
            Uri u; if (!Omni.IsWeb(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return null;
            var p = u.AbsolutePath; if (p.Length > 1 && p.EndsWith("/")) p = p.TrimEnd('/');
            return u.Scheme + "://" + u.Authority.ToLowerInvariant() + p;
        }
        public static string IdOf(string key) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? ""))).Replace("-", "").Substring(0, 20).ToLowerInvariant(); }
        static void Load() { if (items != null) return; var f = BrowserSettings.Read<NoteFile>(FilePath); items = f == null || f.Items == null ? new List<PageNote>() : f.Items.Where(x => x != null && x.Key != null && !string.IsNullOrEmpty(x.Text)).ToList(); }
        static void Save() { BrowserSettings.WriteFile(FilePath, new NoteFile { Items = items }, typeof(NoteFile)); }
        public static void Reset() { lock (gate) items = null; }
        public static void UseForSnap(List<PageNote> list) { lock (gate) items = list; }
        public static List<PageNote> All { get { lock (gate) { Load(); return items.OrderByDescending(x => x.Updated, StringComparer.Ordinal).ToList(); } } }
        public static PageNote Get(string url) { var k = Key(url); if (k == null) return null; lock (gate) { Load(); return items.FirstOrDefault(x => x.Key == k); } }
        // an empty text removes the note
        public static bool Set(string url, string title, string text)
        {
            var k = Key(url); if (k == null) return false;
            text = (text ?? "").Replace("\r\n", "\n"); if (text.Length > MaxText) text = text.Substring(0, MaxText);
            lock (gate)
            {
                Load(); var n = items.FirstOrDefault(x => x.Key == k);
                if (text.Trim().Length == 0) { if (n == null) return false; items.Remove(n); Save(); return true; }
                if (n != null && n.Text == text) return false;
                if (n == null) { n = new PageNote { Key = k }; items.Add(n); }
                n.Url = url; n.Title = string.IsNullOrWhiteSpace(title) ? (n.Title ?? Omni.HostOf(url)) : title.Trim(); n.Text = text; n.Updated = DateTime.Now.ToString("s");
                if (items.Count > Max) items = items.OrderByDescending(x => x.Updated, StringComparer.Ordinal).Take(Max).ToList();
                Save(); return true;
            }
        }
        public static void Replace(IEnumerable<PageNote> list) { lock (gate) { items = list.Where(x => x != null && x.Key != null && !string.IsNullOrEmpty(x.Text)).GroupBy(x => x.Key).Select(g => g.First()).ToList(); Save(); } }
    }

    public partial class BrowserWindow
    {
        public void SaveForLater(Tab t)
        {
            if (t == null || !Omni.IsWeb(t.Url)) { Toast("Open a page to save it for later.", 3); return; }
            if (Private) { Toast("Private windows keep nothing - the reading list isn't here.", 4); return; }
            ReadingList.Add(t.Url, t.Title);
            Toast("Saved for later - it's in the reading list (the sidebar).", 4);
            Side.RefreshNative();
        }
        // the active page changed: the notes panel follows it
        public void PageChangedForNotes() { if (Side != null && Side.NativeOpen == "notes") Side.RefreshNative(); }
    }

    public partial class Sidebar
    {
        public string NativeOpen;              // "reading" | "notes" | null
        FrameworkElement nativeView; Button readBtn, notesBtn;
        string notesKey; TextBox noteBox; DispatcherTimer noteTimer;
        public const int NativeWidth = 340;
        void AddNativeButtons()
        {
            readBtn = NativeButton("book-bookmark", "Reading list - pages you saved for later (Ctrl+Shift+D)", "reading");
            notesBtn = NativeButton("note-sticky", "Notes - a short note for the page you're on", "notes");
            railList.Children.Add(readBtn); railList.Children.Add(notesBtn);
            var sep = new Border { Height = 1, Margin = new Thickness(10, 4, 10, 4) }; sep.SetResourceReference(Border.BackgroundProperty, "LineDim"); railList.Children.Add(sep);
            RefreshNativeButtons();
        }
        Button NativeButton(string icon, string tip, string kind)
        {
            var b = new Button { Style = Ui.S("Tool"), Width = 40, Height = 40, Margin = new Thickness(0, 1, 0, 1), Content = Ui.Icon(icon, 16, "Text"), HorizontalAlignment = HorizontalAlignment.Center, ToolTip = tip };
            b.Click += (a, e) => { if (NativeOpen == kind) HideNative(true); else ShowNative(kind); };
            return b;
        }
        void RefreshNativeButtons()
        {
            foreach (var x in new[] { Tuple.Create(readBtn, "reading", "book-bookmark"), Tuple.Create(notesBtn, "notes", "note-sticky") })
            {
                if (x.Item1 == null) continue;
                x.Item1.Content = Ui.Icon(x.Item3, 16, NativeOpen == x.Item2 ? "AccentBrush" : "Text");
                if (NativeOpen == x.Item2) x.Item1.SetResourceReference(Control.BackgroundProperty, "AccentSoft"); else x.Item1.ClearValue(Control.BackgroundProperty);
            }
            if (readBtn != null) { var n = ReadingList.Count(true); readBtn.ToolTip = "Reading list" + (n > 0 ? " (" + n + " unread)" : "") + " - pages you saved for later (Ctrl+Shift+D)"; }
        }
        public void ShowNative(string kind)
        {
            if (w.Private || w.AppMode) return;
            if (!w.S.SidebarOn) { w.S.SidebarOn = true; ApplyOn(); }
            if (Open != null) { HideOnly(Open); Open = null; }
            RemoveNative();
            NativeOpen = kind;
            host.Visibility = Visibility.Visible; splitter.Visibility = Visibility.Visible;
            hostCol.Width = new GridLength(NativeWidth); splitCol.Width = GridLength.Auto;
            uaBtn.Visibility = Visibility.Collapsed;
            foreach (var x in Panels) if (x.View != null) x.View.Visibility = Visibility.Hidden;
            RefreshNative();
            w.PanelsChanged();
        }
        void RemoveNative()
        {
            SaveNoteNow();
            if (nativeView != null) { viewBox.Children.Remove(nativeView); nativeView = null; }
            NativeOpen = null; noteBox = null; notesKey = null; uaBtn.Visibility = Visibility.Visible;
            RefreshNativeButtons();
        }
        public void HideNative(bool collapse)
        {
            RemoveNative();
            if (collapse && Open == null) { host.Visibility = Visibility.Collapsed; splitter.Visibility = Visibility.Collapsed; hostCol.Width = new GridLength(0); splitCol.Width = new GridLength(0); w.PanelsChanged(); }
        }
        public void RefreshNative()
        {
            RefreshNativeButtons();
            if (NativeOpen == null) return;
            if (NativeOpen == "notes" && noteBox != null && notesKey == PageNotes.Key(w.Active == null ? null : w.Active.Url)) return;   // still the same page: keep the cursor where it is
            SaveNoteNow();
            if (nativeView != null) viewBox.Children.Remove(nativeView);
            nativeView = NativeOpen == "reading" ? ReadingView() : NotesView();
            viewBox.Children.Add(nativeView);
            headTitle.Text = NativeOpen == "reading" ? "Reading list" : "Notes";
        }
        bool showAllRead;
        FrameworkElement ReadingView()
        {
            var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
            var all = ReadingList.Items; int unread = all.Count(x => !x.Read);
            sp.Children.Add(Ui.Chips(new List<KeyValuePair<bool, string>> { new KeyValuePair<bool, string>(false, "Unread (" + unread + ")"), new KeyValuePair<bool, string>(true, "All (" + all.Count + ")") }, showAllRead, v => { showAllRead = v; RefreshNative(); }));
            var save = Ui.Btn("Save this page", () => w.SaveForLater(w.Active), true, "plus", "Ctrl+Shift+D"); save.Margin = new Thickness(0, 6, 0, 10); save.HorizontalAlignment = HorizontalAlignment.Left; sp.Children.Add(save);
            var list = all.Where(x => showAllRead || !x.Read).ToList();
            foreach (var it in list)
            {
                var x = it; var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tx = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = it.Url };
                var title = Ui.T(it.Title ?? it.Url, 12.5, !it.Read); title.TextTrimming = TextTrimming.CharacterEllipsis; tx.Children.Add(title);
                tx.Children.Add(Ui.Muted((Omni.HostOf(it.Url) ?? "") + " - " + NewTabFamily.Ago(it.Added, DateTime.Now) + (it.Read ? " - read" : ""), 10.5));
                tx.MouseLeftButtonUp += (a, e) => { ReadingList.SetRead(x.Url, true); w.NewTab(x.Url, true); RefreshNative(); };
                g.Children.Add(tx);
                var bs = Ui.Row(Ui.Tool(it.Read ? "eye-slash" : "check", it.Read ? "Mark as unread" : "Mark as read", () => { ReadingList.SetRead(x.Url, !x.Read); RefreshNative(); }, 11), Ui.Tool("xmark", "Remove from the list", () => { ReadingList.Remove(x.Url); RefreshNative(); }, 11));
                foreach (Button b in bs.Children) { b.Width = 26; b.Height = 24; }
                Grid.SetColumn(bs, 1); g.Children.Add(bs);
                var card = new Border { Child = g, Padding = new Thickness(8, 6, 4, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 6) };
                card.SetResourceReference(Border.BackgroundProperty, "Panel"); card.SetResourceReference(Border.BorderBrushProperty, "LineDim");
                sp.Children.Add(card);
            }
            if (list.Count == 0) sp.Children.Add(Ui.Muted(all.Count == 0 ? "Nothing saved yet. Ctrl+Shift+D (or the button above) saves the page you're on." : "All read.", 12));
            sp.Children.Add(Ui.Muted("Kept on this PC only.", 10.5));
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        }
        FrameworkElement NotesView()
        {
            var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
            var t = w.Active; var url = t == null ? null : t.Url; var key = PageNotes.Key(url);
            if (key == null) sp.Children.Add(Ui.Muted("Notes are kept per page - open a page to write one.", 12, 10));
            else
            {
                var head = Ui.T(t.Title ?? url, 12.5, true, null, 2); head.TextTrimming = TextTrimming.CharacterEllipsis; sp.Children.Add(head);
                sp.Children.Add(Ui.Muted(key, 10.5, 6));
                var n = PageNotes.Get(url);
                noteBox = new TextBox { Style = Ui.S("Field"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8), MaxLength = PageNotes.MaxText, Text = n == null ? "" : n.Text.Replace("\n", "\r\n") };
                notesKey = key; var box = noteBox; var u = url; var ti = t.Title;
                if (noteTimer == null) { noteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; noteTimer.Tick += (a, b) => { noteTimer.Stop(); SaveNoteNow(); }; }
                box.TextChanged += (a, b) => { box.Tag = u + "\t" + ti; noteTimer.Stop(); noteTimer.Start(); };
                sp.Children.Add(box);
                sp.Children.Add(Ui.Muted("Saved as you type. " + (w.S.SyncOn ? "Notes go through your sync folder" + (w.S.SyncEncrypt ? " (encrypted)." : ".") : "Kept on this PC (they sync too when sync is on)."), 10.5, 12));
            }
            var all = PageNotes.All;
            if (all.Count > 0)
            {
                sp.Children.Add(Ui.Label("All notes (" + all.Count + ")"));
                foreach (var it in all.Take(100))
                {
                    var x = it; var row = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 8), ToolTip = it.Url };
                    var tt = Ui.T(it.Title ?? it.Key, 12, x.Key == key); tt.TextTrimming = TextTrimming.CharacterEllipsis; row.Children.Add(tt);
                    var first = (it.Text ?? "").Split('\n')[0]; var m = Ui.Muted(first.Length > 80 ? first.Substring(0, 80) + "..." : first, 11); m.TextTrimming = TextTrimming.CharacterEllipsis; row.Children.Add(m);
                    row.MouseLeftButtonUp += (a, e) => { if (x.Key != key) w.NewTab(x.Url, true); };
                    sp.Children.Add(row);
                }
            }
            return new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        }
        public void SaveNoteNow()
        {
            if (noteTimer != null) noteTimer.Stop();
            var box = noteBox; if (box == null || box.Tag == null) return;
            var p = ((string)box.Tag).Split(new[] { '\t' }, 2); box.Tag = null;
            if (PageNotes.Set(p[0], p.Length > 1 ? p[1] : null, box.Text)) w.NotesChanged();
        }
        // tests: type into the note box as if typed
        public bool TypeNote(string text) { if (noteBox == null) return false; noteBox.Text = text; SaveNoteNow(); return true; }
    }
}
