// Hallow Browser 1.6 - --snap: made-up pictures of 1.6's parts (never your profiles, reading list, notes or apps)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        public static ProfileStore SnapProfiles()
        {
            var s = new ProfileStore(); s.Fix();
            s.Items.Add(new ProfileInfo { Id = "gaming", Name = "Gaming", Color = "#22C55E" });
            s.Items.Add(new ProfileInfo { Id = "work", Name = "Work", Color = "#3B82F6" });
            return s;
        }
        // the profile chip in the title bar and the switcher (drawn in the page area, as it shows under the chip)
        public void SnapProfileSwitcher()
        {
            ProfileChrome();
            var sp = new StackPanel(); foreach (var x in ProfileMenuRows(a => { })) sp.Children.Add(x);
            var fl = Ui.Flyout(sp, 280); fl.HorizontalAlignment = HorizontalAlignment.Left; fl.VerticalAlignment = VerticalAlignment.Top; fl.Margin = new Thickness(60, 4, 0, 0); fl.Tag = "mock";
            content.Children.Add(fl);
        }
        public void SnapSide(string kind)
        {
            ReadingList.UseForSnap(new List<ReadItem> {
                new ReadItem { Url = "https://news.example.com/season-7", Title = "Season 7: everything new in Delta Force", Added = DateTime.Now.AddHours(-3).ToString("s") },
                new ReadItem { Url = "https://guides.example.com/maps", Title = "The best routes on every map", Added = DateTime.Now.AddDays(-1).ToString("s") },
                new ReadItem { Url = "https://patch.example.com/1-6", Title = "Patch notes 1.6", Added = DateTime.Now.AddDays(-2).ToString("s"), Read = true } });
            PageNotes.UseForSnap(new List<PageNote> {
                new PageNote { Key = "https://news.example.com/patch-notes", Url = "https://news.example.com/patch-notes", Title = "Hallow News - patch notes", Text = "Raid night Friday 9 pm\nBring smokes and the keycard (left door)", Updated = DateTime.Now.ToString("s") },
                new PageNote { Key = "https://guides.example.com/maps", Url = "https://guides.example.com/maps", Title = "The best routes on every map", Text = "Route B is faster after the patch", Updated = DateTime.Now.AddDays(-1).ToString("s") } });
            Side.ShowNative(kind);
        }
        public void SnapApps()
        {
            SiteApps.SnapItems = new List<InstalledApp> {
                new InstalledApp { Id = "raids", Name = "Hallow Raids", Url = "https://raids.example.com/app/", Scope = "https://raids.example.com/app/", IconFrom = "manifest", Shortcut = "x" },
                new InstalledApp { Id = "chat", Name = "Squad chat", Url = "https://chat.example.com/", Scope = "https://chat.example.com/", IconFrom = "favicon", Shortcut = "x" } };
            foreach (var m in content.Children.OfType<FrameworkElement>().Where(x => (x.Tag as string) == "mock").ToList()) content.Children.Remove(m);
            var page = AppsPage(); page.Tag = "mock"; content.Children.Add(page);
            var t = Tabs[0]; t.Url = Omni.AppsUrl; t.Title = "Apps"; t.TitleBlock.Text = t.Title; SetTabIcon(t); Activate(t); Address.Text = Omni.AppsUrl;
            SiteApps.SnapItems = new List<InstalledApp>();
        }
        // an installed site's own window (a made-up page)
        public void SnapAppWindow()
        {
            MakeAppWindow(new InstalledApp { Id = "raids", Name = "Hallow Raids", Url = "https://raids.example.com/app/", Scope = "https://raids.example.com/app/" });
        }
        // sleeping tabs with the leaf (made-up numbers)
        public void SnapSleeping()
        {
            Tabs[1].Suspended = true; Tabs[1].SavedMb = 182; SetTabIcon(Tabs[1]);
            Tabs[3].Discarded = true; Tabs[3].SavedMb = 240; SetTabIcon(Tabs[3]);
        }
    }
}
