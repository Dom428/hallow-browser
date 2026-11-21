HALLOW BROWSER - a fast Chromium browser for gamers, with Hallow Shield built in

Hallow Browser shows pages with Microsoft Edge WebView2, the runtime that comes with
Windows 11 (Microsoft's, not ours). It never makes itself your default browser - pick
it yourself in Windows Settings > Apps > Default apps if you want it for links.

HOW TO INSTALL
1. Right-click the zip > Properties > tick "Unblock" > OK
2. Right-click the zip > Extract All
3. Double-click INSTALL.bat (no admin needed)

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
