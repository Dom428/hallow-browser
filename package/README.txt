HALLOW BROWSER - a fast Chromium browser for gamers, with Hallow Shield built in

Hallow Browser shows pages with Microsoft Edge WebView2, the runtime that comes with
Windows 11 (Microsoft's, not ours). It never makes itself your default browser - pick
it yourself in Windows Settings > Apps > Default apps if you want it for links.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)

WHAT'S NEW IN 1.2 - POWER FEATURES
- Vertical tabs: tabs in a column on the left with their icons, folding to icons only
  (Settings, or right-click a tab). The sidebar moves to the right beside them.
- Tab groups: right-click a tab > Add to a new group. Named and coloured; click the name
  to fold a group away, right-click it to rename, recolour or put the whole group to
  sleep; drag a tab onto it to add it. Saved with your tabs.
- Workspaces (top left): separate sets of tabs and groups, like Gaming and Work, each with
  its own tabs next time - all on the same sign-ins. Save a session with a name and
  restore it later.
- Split view: Ctrl+click a tab to show it beside the one you're on; drag the divider,
  swap the sides.
- Ctrl+K: one box that finds your tabs, history, bookmarks (new: Ctrl+D), every command
  and setting, and sidebar panels. History stays on this PC; Settings > Clear history.
- Screenshots (Ctrl+Shift+X): the visible area, the whole page or a region you drag -
  copy it or save it to Pictures\Hallow Browser.
- Reader mode (F9): a clean, text-first view of articles in light or dark, with text size
  and width.
- Mute: click the speaker on a tab; right-click > Mute all other tabs.

WHAT'S NEW IN 1.1 - THE GAMER SIDEBAR
- Sidebar (Ctrl+Shift+S): Discord, Twitch, YouTube, Spotify and WhatsApp beside the page,
  or any site you add. Each panel stays signed in like a tab, has a phone/desktop version
  and a width you drag. Hidden panels sleep after a while (at once in game mode) - except
  one playing sound, so Spotify keeps playing. Badges show counts like "(3) Discord".
- Live notifications (off by default): Twitch and YouTube channels you list, checked on
  their own pages every 5 minutes. Through Hallow Hub when it runs, otherwise Windows.
  Honestly: this reads the sites' pages, so it can break when they change them.
- Hot tabs (Shift+Esc): every tab and panel by CPU and memory, one click to sleep or close.
- Bandwidth limiter: a download cap per tab and panel (while a game runs, or always).
  WebView2 can't throttle the whole browser, so it's per tab - see the README.
- Light and dark mode: follows Hallow's mode (Hallow Toolkit), or pick Light / Dark in
  Settings > Look. Pages follow the browser unless you choose otherwise.
- Hallow Shield 1.7 built in.

WHAT'S NEW IN 1.0
- Tabs in the title bar (drag to reorder, Ctrl+Shift+T brings closed ones back), an
  address bar that searches DuckDuckGo (or Google, Bing, Brave Search), downloads, find
  in page, zoom, fullscreen video, Chrome's keyboard shortcuts and your tabs back next time.
- Pages are dark where the site allows it. The Hallow console look with your Hallow accent.
- Hallow Shield is built in and always on: the shield button opens its panel (this site
  Strict / Normal / Off, levels, cookie banners, annoyances, the zapper, My filters,
  stats); Menu > Hallow Shield settings has the lists and the rest.
- Hallow Keys: a key shows in the address bar when Keys has a login for the page. Click
  it and pick a login to fill - never by itself, only on that exact site.
- Gaming-light mode: while you play, background tabs sleep and the browser runs at a
  lower priority (tabs playing sound stay awake). Tab sleeping after 15 minutes, a RAM
  limiter, a CPU limiter, pop-out video that stays on top, and a quick panel with your
  other Hallow apps (Guard score, Mixer, Save a clip, Hub).
- Privacy: third-party cookies blocked, Global Privacy Control and Do Not Track sent,
  optional clearing of browsing data when you close it. No Hallow server; Microsoft's
  WebView2 runtime has its own diagnostic data settings in Windows Settings.

WHERE THINGS ARE
- The browser: %LOCALAPPDATA%\Programs\Hallow Browser
- Browsing data: %LOCALAPPDATA%\HallowBrowser\Profile
- Settings and open tabs: %APPDATA%\HallowBrowser

CREDITS
Microsoft Edge WebView2 SDK 1.0.4314.35 (c) Microsoft Corporation - WebView2-LICENSE.txt.
Hallow Shield's filter lists - shield\LISTS-LICENSE.txt. Icons: Font Awesome Free
(CC BY 4.0) - FontAwesome-LICENSE.txt.

https://github.com/Dom428/hallow-browser
