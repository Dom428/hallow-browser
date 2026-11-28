// Hallow Browser 1.21 - a new tab is ready to type in. Real use: "when I open a new tab I want to start typing in the search bar".
// The new tab page's view took the keyboard as it was shown (and again as it finished loading), so the address bar never had it.
// Now a new tab page opened in front (Ctrl+T, the + button, menus, Ctrl+K, the taskbar) puts the caret in the address bar, and for
// the next 2 seconds a page grabbing focus by itself is undone - unless you clicked or pressed a key in the page yourself.
using System;
using System.Windows.Input;
using System.Windows.Threading;

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        Tab typeTab; DateTime typeUntil;
        public static int AddressFocusRequests;   // tests: how often a new tab asked for the address bar
        public bool NewTabTypesInAddress { get { return !S.NewTabPageFocus; } }

        void WantAddressFor(Tab t)
        {
            if (t == null || !!S.NewTabPageFocus || !Omni.IsNewTab(t.Url)) return;
            typeTab = t; typeUntil = DateTime.UtcNow.AddSeconds(2); AddressFocusRequests++;
            FocusAddress();
            Dispatcher.BeginInvoke(new Action(() => { if (Active == typeTab && !Address.IsKeyboardFocusWithin) FocusAddress(); }), DispatcherPriority.ApplicationIdle);
        }
        // a page took focus: give it back to the address bar while the new tab is still "fresh" and the user didn't click the page
        bool KeepAddress(Tab x)
        {
            if (x == null || x != typeTab || x != Active || DateTime.UtcNow > typeUntil) return false;
            if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed) { typeTab = null; return false; }
            Dispatcher.BeginInvoke(new Action(() => { if (Active == x) FocusAddress(); }), DispatcherPriority.Input);
            return true;
        }
    }
}
