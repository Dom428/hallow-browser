HALLOW BROWSER - a fast Chromium browser for gamers, with Hallow Shield built in

Hallow Browser shows pages with Microsoft Edge WebView2, the runtime that comes with
Windows 11 (Microsoft's, not ours). It never makes itself your default browser - you
choose that in Windows (Hallow Browser's Settings > General > Default browser takes you there).

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)

WHAT'S NEW IN 1.20 - THE EXTENSION BRIDGE
- Side panels: an extension with a side panel gets "Open side panel" (the extensions list, a pinned extension's right-click menu,
  hallow://extensions). Its panel opens in Hallow's sidebar with its extension features; chrome.sidePanel calls from its pages work
- "Open in Chrome" (a tab's menu, right-click on a page, Ctrl+K, More tools): the page's address goes to Chrome, Brave or Edge -
  Settings > General > Open in another browser. From a private window it asks first (the other browser's normal window isn't
  private) and offers that browser's own Incognito / InPrivate window
- Every extension gets a verdict - on the Chrome Web Store card before adding, and on hallow://extensions: "Works fully",
  "Works with Hallow's bridge" (only its side panel was missing) or "Use Open in Chrome" (it needs chrome.debugger, Google sign-in,
  native messaging or another feature WebView2 lacks), with the reasons
- Honestly: Claude in Chrome can't fully work in Hallow Browser - it needs chrome.debugger and native messaging, which WebView2
  doesn't have. Its card says "Use Open in Chrome" and points to Hallow's own Ask Claude sidebar for chatting about a page

WHAT'S NEW IN 1.19 - MODERN EXTRAS
- Pinned tabs: right-click a tab > "Pin tab". They stay at the left as small icons (a row of tiles with vertical tabs), have no
  close button and come back after a restart. Ctrl+W still closes one, like in Chrome
- Cookie banners declined for you (on by default): Hallow clicks "Reject all" / "Only necessary" on cookie banners (OneTrust,
  Cookiebot, Didomi, Quantcast, Usercentrics, TrustArc, Google and plain ones) - never Accept, never on sign-in pages. The padlock
  shows what it did and can leave a site alone; Settings > Privacy turns it off
- Copy clean link (share button, a tab's menu, right-click on a page, Ctrl+K): the address without tracking parameters (utm_...,
  fbclid, gclid...). Settings > Privacy > "Clean links when I copy the address" (off by default) does it for every copy
- Picture-in-picture when you switch tabs (off by default) skips muted autoplay videos and never runs during game mode
- Right-click the tab strip (or Ctrl+K) > "Group tabs by site", and "Ungroup all"
- Ask Claude (optional, off by default): a chat with Claude in the sidebar with your own Anthropic API key (stored encrypted, never
  synced). Nothing is sent until you ask; the page you're on only when you click "Use this page". Models: Claude Opus 5.5 (default),
  Sonnet 5.5, Haiku 5.5, Fable 5.2

WHAT'S NEW IN 1.18 - A BRAVE-STYLE TOP BAR
- Customize the toolbar: right-click it > "Customize toolbar..." (or Menu > View) - switch buttons on or off and move them
  (Home, Bookmarks, Downloads, Sidebar, Extensions, Shield, Game mode, Screenshot, Split view, Reader, Profile, Settings).
  "Reset to default" brings back Shield, Extensions, Downloads, Game mode, Settings and Profile
- The address bar shows a clean address (example.com/path) until you click in it; http pages say "Not secure"
- The padlock shows the connection, what Shield blocked, the site's permissions, cookies and site data, and Site settings
- Buttons inside the address bar only when they apply: zoom (click for 100%), reader, translate, install as app, share
  (copy the link or show a QR code - made on your PC) and pop out a playing video
- Shield's button shows how many things it blocked on the page
- The puzzle piece lists your extensions with a pin: pinned ones get their own toolbar button that opens their popup
- The downloads button shows a progress ring while downloads run; its pop-up shows the latest five
- A profile button with your profile's initial and colour; a tab-search button at the end of the tab strip
- Tabs shrink evenly as you open more; narrow tabs show the close button when you point at them, and arrows scroll the strip when
  the tabs don't fit

WHAT'S NEW IN 1.17 - ARROW KEYS IN THE ADDRESS BAR, MAGNET LINKS TO HALLOW TORRENTS
- Down / Up move through the address bar's suggestions one row at a time, Enter opens the row, Esc goes back to what you typed.
- A page's magnet link goes straight to Hallow Torrents when it's installed, even if Windows has another app for magnet links (like qBittorrent). Settings > Downloads can turn it off.

WHAT'S NEW IN 1.16.4 - NO DEAD TAB AFTER AN EXTENSION TRIES TO CLOSE THE BROWSER
- Fix: when an extension tries to close the window, the browser still stays open, and now the page that asked is
  closed too (1.16.2 could leave it behind as a blank tab that wouldn't load anything, on a busy PC)

WHAT'S NEW IN 1.16.3 - THE WHITE TWITCH PLAYER IS FIXED
- The white Twitch player is fixed: it was a broken channel extension overlay (a frame some channels lay over the video, drawn
  white - with "Make pages dark" on, or when it fails to load), now hidden
- New: Settings > Gaming > Twitch ads > "Show channel extensions over the video" (off by default). Panel extensions below the
  stream, chat and Twitch's controls always stay. On: a broken one (failed to load, or a blank sheet over the stream) is still hidden
- The white-player watchdog checks what is actually on screen over the player, not just the video's own picture
- "Make pages dark" leaves twitch.tv alone (Twitch has its own dark theme)
- Report a Twitch problem includes hidden extensions, frame load errors, the on-screen check and "Make pages dark"
- Twitch script version 6 (1.10+ installs pick it up from the release)

WHAT'S NEW IN 1.16.2 - "SIGN IN WITH GOOGLE" WORKS, EXTENSIONS CAN'T CLOSE THE BROWSER
- Google / Microsoft / Apple / GitHub sign-in pages (and the page they send you back to) are left alone: Shield strips no
  parameters and blocks nothing there, Hallow adds no headers, sign-in pop-ups are allowed and keep window.opener, and the tab
  that opened one stays awake (also while gaming)
- An extension can no longer close the whole browser: its last tab becomes a new tab page; only you close Hallow Browser
- Google's own "Dark theme" choice works with "Make pages dark" on: Google's pages draw their own light / dark theme
- Before adding a store extension that needs Chrome-only features (sidePanel, debugger, nativeMessaging, identity, tabGroups,
  offscreen...): "This extension needs Chrome features Hallow Browser doesn't have, so it probably won't work." (still allowed)

WHAT'S NEW IN 1.16.1 - TWITCH: THE STREAM KEEPS GOING DURING ADS
- When Twitch stitches an ad in, Hallow asks Twitch for seven backup streams of the channel at once
  (embed, popout, autoplay, site, picture-by-picture, thunderdome, frontpage) and plays the first
  one without the ad - at a lower quality (480p / 360p) for the break if that is what is ad-free.
- A small "ad-free backup (480p)" badge shows on the player for those seconds; after the break the
  player goes back to its own quality.
- The dark "Ad break" cover is now only for when every backup has the ad too.
- Report a Twitch problem (Ctrl+Shift+F12) lists every backup tried and why it failed.

WHAT'S NEW IN 1.16 - EXTENSIONS FROM THE CHROME WEB STORE
- hallow://extensions > Add from Chrome Web Store: paste a store link or ID, or click
  "Add to Hallow Browser" on the extension's store page.
- Fetched from Google's own update server only when you click; its signature and ID are
  checked; you see what it asks for before it's added. Updates: one click, or once a day.
- WebView2 can't connect extensions to desktop apps (native messaging), so extensions that
  need their desktop app won't fully work.

WHAT'S NEW IN 1.15.1 - A TIDIER NEW TAB PAGE
- The new tab page fits any window: the GUARD / SHIELD strip is a footer under everything (it covered
  the Overlay row in small windows); the games row scrolls sideways, titles get two lines.
- Your Steam games show their names (from Steam's files on this PC) instead of "App 1422450".
- SHIELD shows Hallow Browser's own blocked-today count, refreshed while a new tab is open.
- Duplicate speed-dial tiles (like two Gmail inbox tiles) are merged; pinned tiles are never removed.
- Google is the default search for new installs; a search engine you already picked stays.

WHAT'S NEW IN 1.15 - PASSKEYS THROUGH HALLOW KEYS
- When a site asks to save or use a passkey, Hallow Keys' prompt shows under the address bar: save it in
  your Hallow Keys vault, pick one of yours, or use your phone / a security key / Windows Hello instead.
  Needs Hallow Keys 2.13 or newer, running. Settings > Hallow Keys > Use Hallow Keys for passkeys (on).
- On sign-in pages that offer passkeys in the email box, the key in the address bar lists them too.
- Hallow Keys closed or the setting off: sites get Windows' own passkey window, as before.

WHAT'S NEW IN 1.14.3
- Percentages read 45% instead of 45 %.

WHAT'S NEW IN 1.14.2 - NO MORE WHITE TWITCH PLAYER AFTER AN AD
- An ad that gets through on Twitch is covered ("Ad break - back in 0:28") and muted,
  whichever way it shows itself (playlist markers, the player's ad label, Twitch's own
  ad events, a second video in an ad box).
- A white, blank or frozen player is found (Hallow looks at the video's picture) and
  recovered: nudge, reload the stream, the backup stream - and, right after an ad, the page.
- Report a Twitch problem (the Twitch button on a channel, More tools, or Ctrl+Shift+F12):
  saves "Hallow Twitch report <date>.zip" to your Downloads - what the player was doing
  and a small picture of it. No chat, user names or cookies; the channel is [channel].
  Send that file to the developer.
WHAT'S NEW IN 1.14 - SITE LOGOS EVERYWHERE
- Every bookmark shows its site's logo: on the bookmarks bar and in its folders, in the
  bookmarks manager, the History page, the new tab tiles, Ctrl+K and the address bar's
  suggestions. No logo yet? A brand icon for well-known sites, else a coloured letter.
- Logos are kept from the pages you open (never from private windows), in
  %LOCALAPPDATA%\HallowBrowser\Profile\favicons.
- At the next start Hallow takes the logos your old browser (Brave, Chrome, Edge...)
  already has for your imported bookmarks - from a copy of its icon file, nothing else.
  If that browser is open, it tries again at a later start.
- Settings > Privacy & security > Site logos: "Fetch missing site logos" (on) asks each
  remaining site once for its own icon, when the PC is idle and never during games -
  through Hallow itself (Shield applies), never a third-party icon service.
WHAT'S NEW IN 1.13 - A CLEANER MENU, A HISTORY PAGE, IMPORTS THAT LAND WHERE THEY BELONG
- A short main menu (14 entries instead of about 30): new tab / window / private window,
  History, Downloads, Bookmarks, Reading list & notes, Find in page, Zoom, then View,
  More tools and Profiles, and Settings, Help & about, Exit at the bottom. The rest is
  in submenus that open to the side. If the window is short the menu scrolls, so
  Settings is always reachable - and there is a Settings (gear) button on the toolbar.
- Shortcuts are unchanged and shown in the submenus; Ctrl+K still finds every action.
- History page (Ctrl+H, or Menu > History): every page you visited by day, with search,
  remove, and Clear history for the last hour / day / week / 4 weeks / all time.
  History imported from another browser shows with its own dates.
- Importing from another browser puts its bookmarks bar on your bookmarks bar (in a
  "From Brave" folder if yours already has bookmarks) and switches the bar on; the rest
  goes to Other bookmarks. The message has "Show bookmarks" and "Open history" buttons.
- An earlier import that left the bar's bookmarks deep in Other bookmarks is fixed once
  at start (a copy of your bookmarks is kept as bookmarks.before-1.13.json).
- The new tab page's "make Hallow your default browser" note goes away as soon as it is.
WHAT'S NEW IN 1.12.1 - A FIX FOR A WHITE TWITCH PLAYER
- Fixed: on a live Twitch channel the video area could show as a plain white box. Twitch
  draws ad layers on top of the video during a break (and the 1.10 hide rules could in
  theory hide the player itself). Now no hide rule can hide the player, an empty ad frame
  over the video is removed within about a second, and a video that stays stuck for more
  than ~3.5 s (while you didn't pause it) is nudged and, if needed, reloaded.
- Game mode no longer lowers the memory of the page you are watching - only of the pages
  you can't see.
- Twitch ads stay blocked as before. Installs of 1.10 - 1.12 get the Twitch part of this
  fix from the release on their next daily check.
WHAT'S NEW IN 1.12 - READY TO BE YOUR DEFAULT BROWSER
- Hallow Browser now registers with Windows like Chrome and Brave do (per user, no admin):
  for web links (http, https) and for .html, .htm, .shtml, .xht, .xhtml, .svg, .pdf and .webp
  files, so it shows up in Windows' Default apps and in "Open with". It still never makes
  itself the default - Windows keeps that choice for you to make.
- To make it your default browser: Settings > General > Default browser > "Make Hallow my
  default browser". Windows' Default apps page for Hallow Browser opens: click "Set default"
  at the top (or click HTTPS, then HTTP, then .html, and pick Hallow Browser each time).
- The card shows which browser is your default now ("Your default browser is Brave"), and
  "Import from Brave..." brings your bookmarks and history over.
- A small, one-time note on the new tab page while it isn't the default - close it once and
  it never comes back. Never during a game.
- Links and files open as a new tab in the browser you already have open (no second window).
  Local files with spaces, # or accents in their names open correctly; PDFs open in the
  built-in PDF viewer. Anything that isn't an address or a file is ignored - never searched.

WHAT'S NEW IN 1.11 - TWITCH AD HANDLING THAT WORKS ON THE REAL TWITCH
- Fixed: in 1.10 an ad could play when you opened a stream from Twitch's front page or
  directory. Twitch now asks for its playlists at a new address (usher's /api/v2/), which
  1.10 didn't recognise, so it never knew which channel the player was on. Checked against
  the real twitch.tv (signed out): ads are now swapped for Twitch's own ad-free stream.
- Second safety net: if Twitch's own "Ad" label shows up anyway, the video is covered and
  muted until it goes away.
- A tab that opens straight onto twitch.tv waits for the ad handling to be in place first.

WHAT'S NEW IN 1.10 - TWITCH AD HANDLING
- Twitch ads don't play (on by default): an ad Twitch stitches into the stream is swapped
  for Twitch's own ad-free stream of the same channel while the break lasts. When there is
  none, the ad plays muted under an "Ad break - back in 0:28" cover that lifts when it ends.
- Display ads and ad beacons are blocked. Chat, emotes and channel points keep working.
- Only Twitch's own servers are contacted - never a third-party proxy. Works in private
  windows too.
- Ads pay streamers: allow them on channels you support (the Twitch button on a channel:
  "Support <channel>: allow ads", or Settings > Gaming > Twitch ads).
- Honestly: Twitch changes this often, so it can stop working until a fix ships (fixes can
  also come between releases, sha256-checked); a swapped stream can be lower quality for the
  break; Twitch's terms discourage blocking its ads.

WHAT'S NEW IN 1.9 - SPEED AND ACCESSIBILITY
- Every button, tab, menu item, sidebar entry and setting has a name and help text for
  screen readers (Windows UI Automation - what Narrator reads), and works from the keyboard:
  F6 / Shift+F6 move between the address bar, the tabs, the page and the sidebar; Tab moves
  within; the arrow keys move between tabs; Esc closes a menu; Shift+F10 opens a tab's menu.
- A visible focus outline; the toolbar's buttons are keyboard stops now.
- Bigger text and controls: Settings > Appearance > Accessibility, 100-200%, separate from
  page zoom. By default it follows Windows' Text size.
- Windows' high-contrast themes switch the browser to their colours; Windows' "Animation
  effects" off means nothing in the browser moves.
- Starts a little sooner (window drawn ~15-35 ms earlier, the engine ready ~30 ms earlier);
  a benchmark for later comparisons. See README.md (Speed) for the numbers.
- Fixes from real use: the new tab's "Overlay: on/off" label shows Overlay's real state;
  the + button is centred on the tabs; the bookmarks bar turns on once you have bookmarks
  (unless you hid it with Ctrl+Shift+B); tab groups are easy to find (right-click a tab,
  Menu > Group tabs, Ctrl+K, Auto-group by site); the game-mode chip is a small controller
  icon, only while game mode is on (Settings can hide it), and game mode turns off properly.

WHAT'S NEW IN 1.8 - GAMING EXTRAS
- Skip sponsors and intros on YouTube (OFF by default - it contacts SponsorBlock, the
  community list at sponsor.ajay.app). Only a 4-character hash prefix of each video goes
  out, never the video's address, your account or cookies. Skip automatically (with Undo)
  or show a Skip button. Segment data: SponsorBlock contributors, CC BY-NC-SA 4.0.
- Twitch: the channel's chat in the sidebar (optional), also beside a full-screen stream;
  theatre mode (TV button or Alt+Shift+T, Esc leaves). Auto-claiming channel points is
  not offered - Twitch's terms forbid automation.
- Clip it: the clapperboard button on a stream asks Hallow Clips to save the last 30 s.
  Clips records your screen, so keep the stream on screen; by default Clips only records
  while a game runs (the browser tells you when it isn't recording).
- Playing a game from "Your games" on the new tab page turns game mode on at once; it goes
  off when the game closes.
WHAT'S NEW IN 1.7 - TRANSLATE PAGES, A CLEANER SETTINGS, TASKBAR EXTRAS, MORE SYNC
- Translate pages (OFF until you set it up - nothing preset): your own LibreTranslate
  server (e.g. on your NAS), or DeepL / Google with your own API key (kept protected by
  Windows on this PC). Then a translate button: the page's text goes to the service you
  picked only when you click Translate. Show original undoes it; "Always translate
  <language>" for the languages you choose.
- Casting: WebView2 can't (no Cast / Media Router in it - checked). "Open in Edge to
  cast" opens the tab in Microsoft Edge, which can.
- Settings in sections (General, Privacy & security, Shield, Gaming, Appearance, Sync,
  Profiles, Advanced) with a search box; Ctrl+K finds settings too.
- Taskbar: a jump list (New tab, New private window, Reading list, your profiles) and,
  if you switch it on, a preview per tab when you hover the taskbar button.
- Sync: Hallow Shield's My filters, and - only if you turn it on, only encrypted with the
  sync password - your history (the newest 1,000 pages by default).
WHAT'S NEW IN 1.6 - PROFILES, YOUR HALLOW APPS ON THE NEW TAB, SITES AS APPS
- Profiles ("Personal", "Gaming"...): each with its own sign-ins, history, bookmarks,
  sidebar panels, settings, sync and private windows. The profile button in the title bar
  switches; right-click a link > Open link in profile. Everything you had before is the
  "Default" profile - nothing is moved. Shield runs in every profile.
- New tab page: your games from Hallow Hub (Play), your newest clips from Hallow Clips and
  a Hallow Overlay on / off switch - each only when that app is installed, all local.
- Install this site (menu): its own window, its icon and name, a Start menu shortcut, and
  hallow://apps to open or uninstall it.
- Spell check with Windows' own spell checker, and "Add to dictionary" (from the next start).
- Memory saver Off / Balanced / Max (tabs sleep after 30 / 10 / 2 min) with a leaf and
  "about N MB saved" on sleeping tabs; battery saver; startup boost (OFF by default - it
  keeps a small hidden process while the browser is closed).
- Optional sounds and a calm ambience loop (OFF by default), made by the browser itself.
- Reading list (Ctrl+Shift+D) and short notes per page in the sidebar; notes sync too.

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
  on or off, remove it. 1.16: add from the Chrome Web Store too. Shield stays built in.
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
  address bar that searches Google (or DuckDuckGo, Bing, Brave Search), downloads, find
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
- Settings and open tabs: %APPDATA%\HallowBrowser (1.6: other profiles in Profiles\<id>;
  a copy of 1.5's settings files in backup-1.5)
- Installed sites: Start menu > Hallow Browser Apps, and hallow://apps

CREDITS
Microsoft Edge WebView2 SDK 1.0.4314.35 (c) Microsoft Corporation - WebView2-LICENSE.txt.
Hallow Shield's filter lists - shield\LISTS-LICENSE.txt. Icons: Font Awesome Free
(CC BY 4.0) - FontAwesome-LICENSE.txt.

https://github.com/Dom428/hallow-browser
