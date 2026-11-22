// hallow-twitch 3
// Hallow Browser's Twitch ad handling - the page part. Hallow Browser's own code (github.com/Dom428/hallow-browser), no third-party
// code. The browser runs it at document start on twitch.tv pages as (function(CFG){ ...this file... })(settings), in the page itself
// (not an extension, so it works in private windows too). Newer copies can come from the browser's GitHub release (asset
// hallow-twitch.js, checked against GitHub's sha256) so fixes ship without a whole browser release; the first line's number is the
// version.
//
// How it works: Twitch stitches ads into the stream's HLS playlists (server-side ad insertion), marked with #EXT-X-DATERANGE tags
// (CLASS="twitch-stitched-ad", "twitch-ad-quartile") and segments titled "Amazon|...". Twitch's player fetches those playlists in a
// Web Worker, so this script wraps Worker creation: each classic worker starts with a small prelude that wraps its fetch. When a media
// playlist carries ad markers, the prelude asks Twitch itself (gql.twitch.tv, PlaybackAccessToken with the public web client id the
// site uses; then usher.ttvnw.net) for the same channel's stream as another player type ("embed", "popout", ...) and, if that one has
// no ad, hands its playlist to the player while the break lasts. Only Twitch's own servers are contacted - never a proxy - and no
// cookies or sign-in go with those requests. If every alternative has the ad too, the ad plays muted under a Hallow "Ad break - back
// in 0:28" cover (the length comes from the DATERANGE tag), which lifts when the break is over. Channels on your allow list are left
// alone: their ads play and are counted, so the streamer is paid.
//
// Version 2 (checked against the real twitch.tv, Nov 2026): the player makes classic Workers from blob: addresses that only
// importScripts() Twitch's amazon-ivs worker, and fetches usher's master playlist at /api/v2/channel/hls/<channel>.m3u8 and the media
// playlists from <cluster>.playlist.ttvnw.net/v1/playlist/... (no ".m3u8" in that address). Version 1 only knew /api/channel/hls/,
// so it never learnt which channel a worker was playing: opening a stream from the front page or the directory (Twitch keeps the
// worker across its in-page navigation) let the ad play. The channel now always comes from the worker's own last master playlist -
// never from the page address, which can be stale after in-page navigation. Second signal: when the player shows its own ad label
// or countdown anyway, the video is covered and muted until the label goes away.
//
// Version 3 (Hallow Browser 1.12.1, real use: the player sometimes showed as a plain white box): no hide rule can hide a box that
// holds the video any more, and a once-a-second watchdog on channel pages shows the player again if something hid it, hides an empty
// ad frame laid over it, and nudges a video that is stuck while it should play (see "the player guard and watchdog" below).
'use strict';
if (!CFG || window.__hallowTwitch) return;
var HERE = location.href;
var ON_TWITCH = /^https:\/\/([a-z0-9-]+\.)?twitch\.tv\//i.test(HERE) || (CFG.test && HERE.indexOf(CFG.test + '/') === 0);
if (!ON_TWITCH) return;
window.__hallowTwitch = { v: CFG.version, events: [] };

// ---------------------------------------------------------------- the part that runs in the page and in every worker
function core(G, CFG, post) {
    if (G.__hallowTwCore) return;
    var realFetch = G.fetch && G.fetch.bind(G);
    if (!realFetch) return;
    G.__hallowTwCore = { swaps: 0, covers: 0, alt: 0 };
    var ST = G.__hallowTwCore;
    var USHER = CFG.usher, GQL = CFG.gql;
    var TYPES = CFG.types || ['embed', 'popout', 'autoplay', 'picture-by-picture'];
    var Q = 'query PlaybackAccessToken_Template($login: String!, $isLive: Boolean!, $vodID: ID!, $isVod: Boolean!, $playerType: String!, $platform: String!) {' +
        '  streamPlaybackAccessToken(channelName: $login, params: {platform: $platform, playerBackend: "mediaplayer", playerType: $playerType}) @include(if: $isLive) {    value    signature   __typename  }' +
        '  videoPlaybackAccessToken(id: $vodID, params: {platform: $platform, playerBackend: "mediaplayer", playerType: $playerType}) @include(if: $isVod) {    value    signature   __typename  }}';
    var variants = {};   // media playlist URL -> { ch, res, bw }
    var masters = {};    // channel -> the player's own usher address
    var alts = {};       // channel|type -> { media, t }
    var sticky = {};     // channel -> the player type that was clean last time
    var inAd = {};       // channel -> true while its playlists carry an ad
    var noAlt = {};      // channel -> until when not to look again (every alternative had the ad a moment ago)
    var lastCh = null;
    var onAlt = {};      // channel -> the player type the player is on now (it stays there until its next master playlist)

    function allowed(ch) { return (CFG.allow || []).indexOf(ch) >= 0; }
    function hasAd(text) {
        return /CLASS="[^"]*(stitched|-ad"|-ad-|-ads")/i.test(text) || /X-TV-TWITCH-AD/.test(text) || /^#EXTINF:[^\n,]*,Amazon/m.test(text);
    }
    function attrs(s) {
        var o = {}, re = /([A-Z0-9-]+)=("([^"]*)"|[^,]*)/g, m;
        while ((m = re.exec(s))) o[m[1]] = m[3] !== undefined ? m[3] : m[2];
        return o;
    }
    // the break's length, from the DATERANGE tags: each ad's DURATION (and the pod's filled duration, when Twitch gives it)
    function adInfo(text) {
        var ids = {}, pods = {}, re = /^#EXT-X-DATERANGE:(.*)$/gm, m, total = 0, pod = 0, ext = 0;
        while ((m = re.exec(text))) {
            var a = attrs(m[1]);
            if (!/stitched/i.test(a.CLASS || '') && !(a['X-TV-TWITCH-AD-ROLL-TYPE'] && !/quartile/i.test(a.CLASS || ''))) continue;
            var d = parseFloat(a.DURATION || a['PLANNED-DURATION'] || '0') || 0;
            ids[a.ID || ('ad' + total)] = d; total += d;
            var pk = a['X-TV-TWITCH-AD-AD-SESSION-ID'] || a.ID, pf = parseFloat(a['X-TV-TWITCH-AD-POD-FILLED-DURATION'] || '0') || 0;
            if (pf > (pods[pk] || 0)) pods[pk] = pf;
        }
        for (var k in pods) pod += pods[k];
        var segs = text.match(/^#EXTINF:([0-9.]+),Amazon/gm) || [];
        for (var i = 0; i < segs.length; i++) ext += parseFloat(segs[i].slice(8)) || 0;
        var secs = Math.max(total, pod); if (!secs) secs = Math.max(ext, 15);
        return { ids: ids, secs: Math.min(Math.round(secs * 10) / 10, 180) };
    }
    function parseMaster(text, base) {
        var lines = text.split(/\r?\n/), out = [];
        for (var i = 0; i < lines.length; i++) {
            if (lines[i].indexOf('#EXT-X-STREAM-INF:') !== 0) continue;
            var a = attrs(lines[i].slice(18)), j = i + 1;
            while (j < lines.length && (lines[j] === '' || lines[j].charAt(0) === '#')) j++;
            if (j < lines.length) { try { out.push({ uri: new URL(lines[j], base).href, res: a.RESOLUTION || '', bw: +a.BANDWIDTH || 0, video: a.VIDEO || '' }); } catch (e) { } }
        }
        return out;
    }
    function pick(list, want) {
        if (!list.length) return null;
        if (want) {
            for (var i = 0; i < list.length; i++) if (want.res && list[i].res === want.res) return list[i];
            var best = null;
            for (var k = 0; k < list.length; k++) if (list[k].bw <= (want.bw || 0) && (!best || list[k].bw > best.bw)) best = list[k];
            if (best) return best;
        }
        return list.slice().sort(function (a, b) { return b.bw - a.bw; })[0];
    }
    // usher's master playlist address: /api/channel/hls/<channel>.m3u8 or (what twitch.tv uses now) /api/v2/channel/hls/<channel>.m3u8
    function channelOf(url) {
        if (url.indexOf(USHER + '/api/') !== 0) return null;
        var m = /^\/api\/(?:v\d+\/)?channel\/hls\/([A-Za-z0-9_]+)\.m3u8/.exec(url.slice(USHER.length));
        return m ? m[1].toLowerCase() : null;
    }
    // a media playlist address: one the master listed, a .m3u8, or Twitch's playlist hosts (<cluster>.playlist.ttvnw.net/v1/playlist/...)
    function playlistish(url) {
        return !!variants[url] || url.indexOf('.m3u8') >= 0 || /^https:\/\/[a-z0-9-]+\.playlist\.ttvnw\.net\/v\d+\/playlist\//i.test(url);
    }
    function dbg(m) { if (CFG.diag) post({ ev: 'dbg', m: m }); }
    function token(ch, type) {
        var body = JSON.stringify({ operationName: 'PlaybackAccessToken_Template', query: Q,
            variables: { isLive: true, login: ch, isVod: false, vodID: '', playerType: type, platform: 'web' } });
        return realFetch(GQL, { method: 'POST', body: body, credentials: 'omit', headers: { 'Client-ID': CFG.clientId, 'Content-Type': 'text/plain;charset=UTF-8' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (j) { var t = j && j.data && j.data.streamPlaybackAccessToken; return t && t.value && t.signature ? t : null; })
            .catch(function () { return null; });
    }
    // the same channel as another player type: token -> usher -> the matching quality's media playlist (its text)
    function alt(ch, type, want) {
        // (one per quality too: the player switches quality, and each needs its own media playlist from the alternative)
        var k = ch + '|' + type + '|' + (want ? want.res + '@' + want.bw : ''), a = alts[k], fresh = a && Date.now() - a.t < 8 * 60000;
        var media = fresh ? Promise.resolve(a.media) : token(ch, type).then(function (t) {
            if (!t) return null;
            var u = new URL(masters[ch] || (USHER + '/api/channel/hls/' + ch + '.m3u8?allow_source=true&fast_bread=true&player_backend=mediaplayer&playlist_include_framerate=true&reassignments_supported=true&supported_codecs=avc1'));
            u.searchParams.set('sig', t.signature); u.searchParams.set('token', t.value); u.searchParams.set('p', String(Math.floor(Math.random() * 9999999)));
            ST.alt++;
            return realFetch(u.href, { credentials: 'omit' }).then(function (r) { return r.ok ? r.text() : null; }).then(function (txt) {
                if (!txt) return null;
                var v = pick(parseMaster(txt, u.href), want); if (!v) return null;
                alts[k] = { media: v.uri, t: Date.now() };
                return v.uri;
            });
        });
        return media.then(function (m) {
            if (!m) return null;
            return realFetch(m, { credentials: 'omit' }).then(function (r) { if (!r.ok) { delete alts[k]; return null; } return r.text(); });
        }).catch(function () { delete alts[k]; return null; });
    }
    function asResponse(text, url) {
        var r = new Response(text, { status: 200, headers: { 'Content-Type': 'application/vnd.apple.mpegurl' } });
        try { Object.defineProperty(r, 'url', { value: url }); } catch (e) { }
        return r;
    }
    // an ad in this channel's playlist: the first clean alternative (the one that worked last time first), else the original + a cover
    function onAd(ch, url, text) {
        var want = variants[url], order = TYPES.slice();
        if (sticky[ch]) { order.splice(order.indexOf(sticky[ch]), 1); order.unshift(sticky[ch]); }
        var i = Date.now() < (noAlt[ch] || 0) ? order.length : 0;
        function next() {
            if (i >= order.length) {
                if (Date.now() >= (noAlt[ch] || 0)) noAlt[ch] = Date.now() + 8000;
                var info = adInfo(text); ST.covers++;
                post({ ev: 'cover', ch: ch, secs: info.secs, ids: info.ids });
                return asResponse(text, url);
            }
            var type = order[i++];
            return alt(ch, type, want).then(function (t) {
                dbg('alt ' + ch + ' ' + type + ': ' + (t ? (t.match(/#EXTINF/g) || []).length + ' segments, ad=' + hasAd(t) : 'nothing'));
                if (t && /#EXTINF/.test(t) && !hasAd(t)) {
                    sticky[ch] = type; onAlt[ch] = type; ST.swaps++;
                    post({ ev: 'swap', ch: ch, type: type });
                    return asResponse(t, url);
                }
                return next();
            });
        }
        return next();
    }

    G.fetch = function (input, init) {
        var url;
        try { url = typeof input === 'string' ? input : input instanceof URL ? input.href : input && input.url; } catch (e) { }
        if (!url || !playlistish(url)) return realFetch(input, init);
        var ch = channelOf(url);
        return realFetch(input, init).then(function (resp) {
            if (!resp.ok) return resp;
            return resp.clone().text().then(function (text) {
                if (text.indexOf('#EXT-X-STREAM-INF') >= 0) {   // a master playlist: remember which quality is which
                    // (the worker plays one stream at a time: its newest master says which channel - an unknown one forgets the old)
                    lastCh = ch;
                    dbg('master ' + (ch || '?') + ' ' + url.split('?')[0].slice(0, 90));
                    if (ch) {
                        masters[ch] = url; delete onAlt[ch];   // a new session of the player's own: it starts on the original
                        var list = parseMaster(text, url);
                        for (var i = 0; i < list.length; i++) variants[list[i].uri] = { ch: ch, res: list[i].res, bw: list[i].bw };
                    }
                    return resp;
                }
                if (text.indexOf('#EXTINF') < 0) return resp;
                var v = variants[url], c = v ? v.ch : lastCh, ad = hasAd(text);
                dbg('media ' + (c || '?') + (v ? ' ' + v.res : ' (not in master)') + ' ad=' + ad);
                if (!c) {   // an ad on a stream we can't name: no swap (never another channel's stream) - the page covers it
                    if (ad) { var inf = adInfo(text); ST.covers++; post({ ev: 'cover', ch: null, secs: inf.secs, ids: inf.ids }); }
                    return resp;
                }
                if (!ad) {
                    if (inAd[c]) { inAd[c] = false; post({ ev: 'clean', ch: c }); }
                    // After a swap the player STAYS on the alternative: on the real Twitch, going back to the original stream mid-session
                    // stalls the player for good (checked Nov 2026: paused, buffer empty, never recovers). The alternative is the same
                    // live stream at the same quality, so nothing is lost; the player's next master playlist starts over.
                    if (onAlt[c]) {
                        var type = onAlt[c];
                        return alt(c, type, v).then(function (t) {
                            if (t && /#EXTINF/.test(t) && !hasAd(t)) return asResponse(t, url);
                            if (t && hasAd(t)) { inAd[c] = true; return onAd(c, url, t); }   // the alternative has its own ad now
                            delete onAlt[c]; dbg('alternative gone - back to the original'); return resp;
                        });
                    }
                    return resp;
                }
                var first = !inAd[c]; inAd[c] = true;
                if (allowed(c)) { if (first) post({ ev: 'allowed', ch: c }); return resp; }
                return onAd(c, url, text);
            });
        });
    };
}

// ---------------------------------------------------------------- the page
var NONCE = 'hallow-tw-' + Math.random().toString(36).slice(2);
var CH_NOW = function () { var p = location.pathname; if (CFG.test && HERE.indexOf(CFG.test + '/') === 0) p = location.href.slice(CFG.test.length).split(/[?#]/)[0]; var m = /^\/([A-Za-z0-9_]{3,25})\/?$/.exec(p); return m ? m[1].toLowerCase() : null; };
function toHost(m) { try { window.chrome.webview.postMessage('hallow:twitch\t' + JSON.stringify(m)); } catch (e) { } }
function log(m) { var e = window.__hallowTwitch.events; e.push(m.ev + (m.type ? ':' + m.type : '') + (m.secs ? ':' + m.secs : '')); if (e.length > 100) e.shift(); }

// display ads and ad labels: hidden (never chat, emotes or channel points). Version 3: never a box that holds the video - each
// selector only hides an element without a <video> inside (:has), so a display-ad layout that wraps the player can't hide the stream.
function hideRule(list) {
    var hasOk = false; try { hasOk = CSS.supports('selector(:not(:has(video)))'); } catch (e) { }
    if (!hasOk) return list + '{display:none!important}';
    return list.split(',').map(function (s) { return ':is(' + s.trim() + '):not(:has(video))'; }).join(',') + '{display:none!important}';
}
if (CFG.on) {
    var css = CFG.hide || '';
    if (css) {
        var st = document.createElement('style'); st.id = 'hallow-twitch-css';
        st.textContent = hideRule(css);
        // Twitch moves between channels without loading a page: on a channel you allow ads on, its display ads show too.
        // (At document start there is no <html> element yet: the style goes in as soon as there is one.)
        var cssFor = function () {
            var r = document.head || document.documentElement; if (!r) return;
            if (!st.isConnected) r.appendChild(st);
            st.disabled = (CFG.allow || []).indexOf(CH_NOW()) >= 0;
        };
        cssFor();
        if (!st.isConnected) { var mo = new MutationObserver(function () { cssFor(); if (st.isConnected) mo.disconnect(); }); mo.observe(document, { childList: true, subtree: true }); }
        setInterval(cssFor, 1000);
    }
}

// GraphQL calls that only report ad events: answered here instead of sent (never a call that mixes in anything else)
var AD_OPS = CFG.adOps || [];
if (CFG.on && AD_OPS.length && window.fetch) {
    var pageFetch = window.fetch.bind(window);
    window.fetch = function (input, init) {
        try {
            var url = typeof input === 'string' ? input : input && input.url;
            if (url && url.indexOf(CFG.gql) === 0 && init && typeof init.body === 'string' && (CFG.allow || []).indexOf(CH_NOW()) < 0) {
                var b = JSON.parse(init.body), list = Array.isArray(b) ? b : [b];
                if (list.length && list.every(function (o) { return o && AD_OPS.indexOf(o.operationName) >= 0; })) {
                    window.__hallowTwitch.droppedOps = (window.__hallowTwitch.droppedOps || 0) + list.length;
                    var res = list.map(function () { return { data: null, extensions: {} }; });
                    return Promise.resolve(new Response(JSON.stringify(Array.isArray(b) ? res : res[0]), { status: 200, headers: { 'Content-Type': 'application/json' } }));
                }
            }
        } catch (e) { }
        return pageFetch(input, init);
    };
}

// the cover: muted video under a Hallow panel with the break's countdown
var cover = { end: 0, ids: {}, host: null, root: null, muted: [], timer: 0, ch: null, dom: false };
function bigVideo() {
    var vs = document.querySelectorAll('video'), best = null, area = 0;
    for (var i = 0; i < vs.length; i++) { var r = vs[i].getBoundingClientRect(), a = r.width * r.height; if (a > area) { area = a; best = vs[i]; } }
    return best;
}
function fmt(s) { s = Math.max(0, Math.ceil(s)); return Math.floor(s / 60) + ':' + ('0' + s % 60).slice(-2); }
function drawCover() {
    var left = (cover.end - Date.now()) / 1000;
    if (left <= 0) { liftCover('time'); return; }
    var v = bigVideo(), parent = document.fullscreenElement || document.documentElement;
    if (!cover.host) {
        cover.host = document.createElement('hallow-adbreak');
        cover.root = cover.host.attachShadow({ mode: 'closed' });
        cover.root.innerHTML = '<style>:host{all:initial;position:fixed;z-index:2147483647;display:block}div{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:6px;' +
            'background:#14101B;color:#ECE8F4;font:15px "Segoe UI",sans-serif;border:1px solid ' + CFG.accent + ';box-sizing:border-box}b{font:600 22px "Segoe UI",sans-serif;color:#fff}small{font-size:12px;opacity:.8}i{display:block;width:44px;height:3px;border-radius:2px;background:' + CFG.accent + '}</style>' +
            '<div><i></i><b id=t></b><small id=s></small></div>';
    }
    if (cover.host.parentNode !== parent) parent.appendChild(cover.host);
    var r = v ? v.getBoundingClientRect() : { left: 0, top: 0, width: innerWidth, height: innerHeight };
    var s = cover.host.style; s.left = r.left + 'px'; s.top = r.top + 'px'; s.width = Math.max(r.width, 200) + 'px'; s.height = Math.max(r.height, 120) + 'px';
    cover.root.getElementById('t').textContent = 'Ad break — back in ' + fmt(left);
    cover.root.getElementById('s').textContent = 'Hallow Browser is covering a Twitch ad. The stream plays muted underneath.';
    var vs = document.querySelectorAll('video');
    for (var i = 0; i < vs.length; i++) if (!vs[i].muted) { vs[i].muted = true; cover.muted.push(vs[i]); }
}
function allowedHere(ch) { return (CFG.allow || []).indexOf(ch || CH_NOW()) >= 0; }
function showCover(m) {
    if (allowedHere(m.ch)) return;
    var add = 0, fresh = !cover.end;
    for (var id in m.ids || {}) if (!(id in cover.ids)) { cover.ids[id] = 1; add += m.ids[id] || 0; }
    if (fresh) { cover.end = Date.now() + Math.max(m.secs || 0, add, 3) * 1000; cover.ch = m.ch; }
    else if (add > 0) cover.end += add * 1000;
    if (!cover.timer) cover.timer = setInterval(drawCover, 250);
    drawCover();
    if (fresh) toHost({ ev: 'cover', ch: m.ch || CH_NOW(), secs: Math.round((cover.end - Date.now()) / 1000), why: m.why || 'playlist' });
}
function liftCover(why) {
    if (!cover.end) return;
    clearInterval(cover.timer); cover.timer = 0; cover.end = 0; cover.ids = {}; cover.dom = false;
    if (cover.host && cover.host.parentNode) cover.host.parentNode.removeChild(cover.host);
    for (var i = 0; i < cover.muted.length; i++) try { cover.muted[i].muted = false; } catch (e) { }
    cover.muted = [];
    log({ ev: 'lift' }); toHost({ ev: 'lift', ch: cover.ch, why: why });
}
window.__hallowTwitch.cover = function () { return cover.end ? { left: (cover.end - Date.now()) / 1000, shown: !!(cover.host && cover.host.isConnected), text: cover.root && cover.root.getElementById('t').textContent } : null; };

// after a swapped break the player goes back to its own stream; on the real Twitch it can stop there (paused, nothing more played).
// For 20 s after the break, a video that stops moving is nudged: Twitch's own player is paused and played again, and if that
// doesn't help its source is loaded again (as the player's own error recovery would). Found through React's tree on #root.
var swappedSince = 0, watch = 0;
function reactRoot() {
    var el = document.getElementById('root'); if (!el) return null;
    for (var k in el) if (k.indexOf('__reactContainer') === 0 && el[k]) return el[k];
    var rc = el._reactRootContainer; return rc && rc._internalRoot && rc._internalRoot.current || null;
}
function findState(test) {
    var root = reactRoot(); if (!root) return null;
    var stack = [root], n = 0;
    while (stack.length && n++ < 300000) {
        var f = stack.pop();
        try { if (f.stateNode && test(f.stateNode)) return f.stateNode; } catch (e) { }
        if (f.sibling) stack.push(f.sibling); if (f.child) stack.push(f.child);
    }
    return null;
}
function nudge(hard) {
    var how = 'video';
    try {
        if (!hard) {
            var host = findState(function (s) { return s.props && s.props.mediaPlayerInstance && typeof s.props.mediaPlayerInstance.play === 'function'; });
            var p = host && host.props.mediaPlayerInstance;
            if (p) { p.pause(); p.play(); how = 'player'; }
            else { var v = bigVideo(); if (v) { v.play(); } }
        } else {
            var st = findState(function (s) { return typeof s.setSrc === 'function' && typeof s.setInitialPlaybackSettings === 'function'; });
            if (st) { st.setSrc({ isNewMediaPlayerInstance: true, refreshAccessToken: true }); how = 'reload'; }
            else { var v2 = bigVideo(); if (v2) { v2.pause(); v2.play(); } }
        }
    } catch (e) { how += '-failed'; }
    log({ ev: 'resync', type: how }); window.__hallowTwitch.resyncs = (window.__hallowTwitch.resyncs || 0) + 1;
}
var afterUntil = 0;
function afterBreak() {
    clearInterval(watch); afterUntil = Date.now() + 21000;
    var start = Date.now(), last = -1, stuck = 0, tries = 0;
    watch = setInterval(function () {
        var v = bigVideo();
        if (!v || Date.now() - start > 20000 || tries > 2) { clearInterval(watch); return; }
        var moved = last >= 0 && v.currentTime - last > 0.25; last = v.currentTime;
        if (moved && !v.paused) { stuck = 0; return; }
        if (++stuck >= 3) { stuck = 0; tries++; nudge(tries > 1); }
    }, 1000);
}

// ---------------------------------------------------------------- version 3 (Hallow Browser 1.12.1): the player guard and watchdog
// Real use showed the player as a plain white box now and then. Three things are watched, once a second, on a channel page:
//  1. The player is never hidden: an element around the main <video> that an ad rule (ours, or an ad filter's) hid is shown again.
//  2. Nothing empty sits on top of it: an ad frame over the video - an iframe from an ad host or with no address, an element named
//     like an ad box (display ad, squeezeback, picture-by-picture, outstream...), or an empty opaque white box - is hidden. Twitch's own
//     overlays (controls, extensions from ext-twitch.tv, the mature-content gate, error screens) have text or a twitch address and stay.
//  3. A video that stays stuck for more than ~3.5 s while it should play (not paused by you) is nudged like after an ad break: Twitch's
//     player is paused and played, then its source is loaded again. At most 3 tries until it moves again.
var AD_NAME = /(^|[\s_-])ads?([\s_-]|$)|display-?ad|displayad|(^|[\s_-])sda([\s_-]|$)|squeezeback|pbyp|picture-by-picture|outstream|commercial-break|advertis|amazon/i;
var AD_SRC = /^https?:\/\/([a-z0-9-]+\.)*(amazon-adsystem\.com|doubleclick\.net|googlesyndication\.com|googletagservices\.com|imasdk\.googleapis\.com|ads\.twitch\.tv|ads\.ttvnw\.net|adnxs\.com|adsrvr\.org)\//i;
var WD = { guards: 0, unblocks: 0, stalls: 0, last: null, stuckSince: 0, lastT: -1, tries: 0, lastTry: 0 };
window.__hallowTwitch.watch = WD;
function nameOf(el) {
    var s = '';
    try { s = (typeof el.className === 'string' ? el.className : (el.getAttribute('class') || '')) + ' ' + (el.id || '') + ' ' + (el.getAttribute('data-a-target') || '') + ' ' + (el.getAttribute('data-test-selector') || '') + ' ' + (el.getAttribute('aria-label') || ''); } catch (e) { }
    return s;
}
function adish(el) { return AD_NAME.test(nameOf(el)); }
function hiddenByRule(el) { try { return !!CFG.hide && el.matches(CFG.hide); } catch (e) { return false; } }
function playerVideo() {
    var v = bigVideo();
    if (v && v.getBoundingClientRect().width > 0) return v;
    return document.querySelector('[data-a-target="video-ref"] video, .video-player video') || v;
}
function wdEvent(what) { WD.last = what + ' ' + new Date().toISOString().slice(11, 19); log({ ev: what }); }
// 1. an ancestor of the video (or the video) hidden by an ad rule is shown again (inline !important beats any page rule)
function guardPlayer(v) {
    var fixed = 0;
    for (var p = v; p && p !== document.body && p !== document.documentElement; p = p.parentElement) {
        var cs = getComputedStyle(p);
        if (cs.display !== 'none' && cs.visibility !== 'hidden') continue;
        if (p !== v && !hiddenByRule(p) && !adish(p)) continue;   // hidden by Twitch itself (a layout it isn't using): left alone
        if (cs.display === 'none') p.style.setProperty('display', 'block', 'important');
        if (cs.visibility === 'hidden') p.style.setProperty('visibility', 'visible', 'important');
        p.setAttribute('data-hallow-keep', '');
        fixed++;
    }
    if (fixed) { WD.guards += fixed; wdEvent('guard'); }
    return fixed;
}
function overlap(a, b) {
    var w = Math.min(a.right, b.right) - Math.max(a.left, b.left), h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
    return w > 0 && h > 0 ? (w * h) / Math.max(1, b.width * b.height) : 0;
}
function emptyWhite(el, cs) {
    var m = /rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/.exec(cs.backgroundColor || '');
    if (!m || +m[1] < 235 || +m[2] < 235 || +m[3] < 235 || (m[4] !== undefined && +m[4] < 0.9)) return false;
    return !(el.innerText || '').trim() && !el.querySelector('img,video,canvas,button,svg,input');
}
function isBlocker(el, v, vr) {
    if (el === v || el.contains(v) || el.tagName === 'HALLOW-ADBREAK' || overlap(el.getBoundingClientRect(), vr) < 0.5) return false;
    if (el.tagName === 'IFRAME') { var src = el.getAttribute('src') || ''; return !src || src === 'about:blank' || AD_SRC.test(el.src); }
    if (el.closest && el.closest('[data-a-target="player-controls"],.player-controls,.extension-view__iframe')) return false;
    var cs = getComputedStyle(el);
    if (emptyWhite(el, cs)) return true;
    for (var p = el, i = 0; p && i < 4 && !p.contains(v); p = p.parentElement, i++) if (adish(p)) return true;
    return false;
}
// 2. what covers the middle of the video, if it is an empty ad frame: hidden (the frame itself, or its ad-named box)
function unblock(v) {
    var vr = v.getBoundingClientRect(); if (vr.width < 160 || vr.height < 90) return 0;
    var pts = [[0.5, 0.5], [0.3, 0.35], [0.7, 0.65]], n = 0;
    for (var i = 0; i < pts.length; i++) {
        var stack = document.elementsFromPoint(vr.left + vr.width * pts[i][0], vr.top + vr.height * pts[i][1]);
        for (var j = 0; j < stack.length && stack[j] !== v; j++) {
            var el = stack[j];
            if (!isBlocker(el, v, vr)) continue;
            var box = el; for (var p = el.parentElement, k = 0; p && k < 4 && !p.contains(v); p = p.parentElement, k++) if (adish(p)) box = p;
            box.style.setProperty('display', 'none', 'important'); box.setAttribute('data-hallow-unblocked', '');
            n++; break;
        }
    }
    if (n) { WD.unblocks += n; wdEvent('unblock'); }
    return n;
}
// 3. a stuck video that should be playing
function stuck(v) {
    var now = Date.now();
    if (v.paused || v.ended || document.hidden || cover.end || now < afterUntil) { WD.stuckSince = 0; WD.lastT = v.currentTime; return; }
    if (v.currentTime - WD.lastT > 0.1 || v.currentTime < WD.lastT) { WD.stuckSince = 0; WD.tries = 0; WD.lastT = v.currentTime; return; }
    if (!WD.stuckSince) { WD.stuckSince = now; return; }
    if (now - WD.stuckSince >= 3500 && now - WD.lastTry >= 4000 && WD.tries < 3) {
        WD.tries++; WD.lastTry = now; WD.stalls++; wdEvent('stall');
        nudge(WD.tries > 1);
    }
}
function watchdog() {
    if (!document.body || !CH_NOW()) return;
    var v = playerVideo(); if (!v) return;
    try { guardPlayer(v); } catch (e) { }
    try { if (CFG.on && !allowedHere(null)) unblock(v); } catch (e) { }
    try { stuck(v); } catch (e) { }
}
if (CFG.on) setInterval(watchdog, 1000);

function onMsg(m) {
    if (!m || !m.ev) return;
    if (m.ev === 'swap') swappedSince = swappedSince || Date.now();
    if (m.ev === 'clean' && swappedSince) { swappedSince = 0; afterBreak(); }
    if (m.ev === 'dbg') { var d = window.__hallowTwitch.diag; if (d) { d.push(Math.round(performance.now()) + ' ' + m.m); if (d.length > 400) d.shift(); } return; }
    log(m);
    if (m.ev === 'cover') { showCover(m); return; }
    if (m.ev === 'clean' && cover.end && !cover.dom) liftCover('clean');   // the playlist has no ad any more
    if (m.ev === 'swap' || m.ev === 'allowed' || m.ev === 'clean') toHost(m);
}
if (CFG.diag) window.__hallowTwitch.diag = [];

// second signal: the player's own ad label / countdown (it shows them whenever an ad really plays - hidden by the CSS above, but
// still there). While one is on the page the video stays covered and muted, whatever the playlists said.
var AD_UI = '[data-a-target="video-ad-label"],[data-a-target="video-ad-countdown"],[data-a-target="video-ad-countdown-container"]';
function domAd() {
    if (!CFG.on || !document.documentElement) return;
    var el = document.querySelector(AD_UI);
    if (!el || allowedHere(null)) { if (cover.dom) { cover.dom = false; cover.end = Math.min(cover.end, Date.now() + 1000); } return; }
    var cd = document.querySelector('[data-a-target="video-ad-countdown"]'), m = cd && /(\d+):(\d\d)/.exec(cd.textContent || ''), secs = m ? +m[1] * 60 + +m[2] : 0;
    if (!cover.dom) { cover.dom = true; log({ ev: 'dom-ad' }); }
    if (!cover.end) showCover({ ev: 'cover', ch: CH_NOW(), secs: Math.max(secs, 2), why: 'dom' });
    var until = Date.now() + Math.max(secs * 1000, 1500);
    if (until > cover.end) cover.end = until;
}
if (CFG.on) setInterval(domAd, 500);
try { new BroadcastChannel(NONCE).onmessage = function (e) { onMsg(e.data); }; } catch (e) { }

if (CFG.on) {
    core(window, CFG, onMsg);
    // every classic worker starts with the same prelude (Twitch's player fetches its playlists in one: a blob: worker whose code is
    // importScripts(<amazon-ivs worker>) - read here and run after the prelude)
    var prelude = function () {
        return '(' + core.toString() + ')(self,' + JSON.stringify(CFG) + ',(function(){var bc;try{bc=new BroadcastChannel(' + JSON.stringify(NONCE) + ')}catch(e){}return function(m){try{bc&&bc.postMessage(m)}catch(e){}}})());\n';
    };
    var OW = window.Worker;
    var source = function (u) {
        var abs = new URL(String(u), location.href).href;
        if (/^(blob|data):/i.test(abs)) {   // read now: the page may revoke a blob address right after creating the worker
            var x = new XMLHttpRequest(); x.open('GET', abs, false); x.send();
            return x.status === 200 || x.status === 0 ? prelude() + x.responseText : null;
        }
        return prelude() + 'importScripts(' + JSON.stringify(abs) + ');';
    };
    if (OW) {
        var HW = function (u, o) {
            var nu = u;
            try { if (!(o && o.type === 'module')) { var s = source(u); if (s != null) { nu = URL.createObjectURL(new Blob([s], { type: 'text/javascript' })); window.__hallowTwitch.workers = (window.__hallowTwitch.workers || 0) + 1; } } } catch (e) { nu = u; }
            return Reflect.construct(OW, [nu, o], new.target || HW);
        };
        HW.prototype = OW.prototype;
        Object.setPrototypeOf(HW, OW);
        try { Object.defineProperty(HW, 'name', { value: 'Worker' }); } catch (e) { }
        window.Worker = HW;
    }
}
toHost({ ev: 'ready', ch: CH_NOW(), v: CFG.version });
