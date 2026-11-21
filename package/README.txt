HALLOW BROWSER - a fast Chromium browser for gamers, with Hallow Shield built in

Hallow Browser shows pages with Microsoft Edge WebView2, the runtime that comes with
Windows 11 (Microsoft's, not ours). It never makes itself your default browser - pick
it yourself in Windows Settings > Apps > Default apps if you want it for links.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)

WHAT'S NEW IN 1.5 - FASTER TO USE, SAFER DOWNLOADS, LOGINS INTO KEYS
- Save to Hallow Keys: after you sign in, Save / Never for this site / Not now. Keys
  keeps it (encrypted); the browser never stores passwords. Not in private windows.
- Address bar: sums and conversions (200*1.2, 5 km in miles), keywords (yt cats,
  w term, gh repo - add your own), tab search (% name). Search suggestions from your
  engine are OFF by default (they send what you type to the engine).
- Make pages dark: for every site or per site (menu, padlock, Settings > Look).
- Downloads: pause, resume, cancel; a speed limit while you play; Microsoft Defender
  checks each finished file (clean / threat found); hallow://downloads lists them all
  with Show in folder.
- Tabs: hover previews, a picture while you drag a tab out, mouse gestures (off by
  default), Reopen closed window. Private windows' menus are purple too.
- Media: a music button for every tab playing audio or video (play / pause / next);
  optional pop-out when you switch away from a playing video.
- Crash recovery: "This tab crashed - Reload", and "Restore your tabs" if the browser
  didn't close properly.

WHAT'S NEW IN 1.4 - WINDOWS, PRIVACY AND YOUR OWN EXTENSIONS
- More windows: Ctrl+N opens a new window. Drag a tab out of the strip to give it its own
  window, or drop it into another window's strip (the page moves as it is). Each window
  keeps its own tabs next time; closing the last window quits.
- Private windows (Ctrl+Shift+N): dark purple with a mask. No history, no session, no
  bookmark or sync changes, cookies gone when you close it - downloads are still kept.
  Shield blocks there too (its network-only fallback: WebView2 doesn't run extensions in
  private windows).
- Site permissions: camera, microphone, location, notifications, clipboard, MIDI and more
  ask first - Allow, Allow this time or Block. Site settings (hallow://settings/sites and
  the padlock in the address bar): permissions, pop-ups, sound, JavaScript, cookies.
- HTTPS-only mode (on): addresses are tried with https first; a site without it asks
  before going on. Secure DNS (Settings > Security): Cloudflare, Quad9 or Google - needs a
  browser restart.
- Your own extensions (hallow://extensions): load an unpacked extension folder, switch it
  on or off, remove it. The Chrome Web Store can't work in WebView2 - the page says what
  to do instead. Shield stays built in.
- Shield's "blocked on this page" number works, and its counts reach Hallow Hub and Remote
  (%APPDATA%\HallowShield\status.json - counts only).
- Game corner on the new tab page (OFF by default - it contacts Steam and Epic): news for
  the games in your Hallow Hub library and Epic's free games, at most every 6 hours.

WHAT'S NEW IN 1.3 - MOVING IN AND SYNCING
- Import from Chrome, Edge, Brave, Opera, Opera GX, Vivaldi and Firefox on this PC:
  bookmarks (into an "Imported from ..." folder, nothing twice) and history. Read from
  copies - the other browser is never changed. Passwords and cookies are never imported
  (Hallow Keys has its own password importer).
- Bookmarks manager (Ctrl+Shift+O): folders, rename, drag to reorder, HTML export and
  import. Bookmarks bar (Ctrl+Shift+B).
- Sync through a folder you pick (OneDrive, Dropbox, Syncthing, a NAS share) - off by
  default: bookmarks, saved sessions, workspaces, settings and sidebar panels, optionally
  encrypted with a sync password. History isn't synced.
- New tab page: speed dial (your top sites and pinned ones), a clock and the Hallow strip
  (Guard score, Shield blocked today, now playing). Also fixed: on current WebView2
  versions 1.2's new tab page showed an error page.
- Light mode reads better (filled accent buttons, darker accent and status text).

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
