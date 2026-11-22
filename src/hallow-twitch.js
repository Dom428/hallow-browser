// hallow-twitch 1
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
    function channelOf(url) {
        if (url.indexOf(USHER + '/api/channel/hls/') !== 0) return null;
        var m = /\/api\/channel\/hls\/([A-Za-z0-9_]+)\.m3u8/.exec(url);
        return m ? m[1].toLowerCase() : null;
    }
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
        var k = ch + '|' + type, a = alts[k], fresh = a && Date.now() - a.t < 8 * 60000;
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
                if (t && /#EXTINF/.test(t) && !hasAd(t)) {
                    sticky[ch] = type; ST.swaps++;
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
        if (!url || url.indexOf('.m3u8') < 0 && !variants[url]) return realFetch(input, init);
        var ch = channelOf(url);
        return realFetch(input, init).then(function (resp) {
            if (!resp.ok) return resp;
            return resp.clone().text().then(function (text) {
                if (text.indexOf('#EXT-X-STREAM-INF') >= 0) {   // a master playlist: remember which quality is which
                    if (ch) {
                        masters[ch] = url; lastCh = ch;
                        var list = parseMaster(text, url);
                        for (var i = 0; i < list.length; i++) variants[list[i].uri] = { ch: ch, res: list[i].res, bw: list[i].bw };
                    }
                    return resp;
                }
                if (text.indexOf('#EXTINF') < 0) return resp;
                var v = variants[url], c = v ? v.ch : lastCh || CFG.ch;
                if (!c) return resp;
                if (!hasAd(text)) {
                    if (inAd[c]) { inAd[c] = false; post({ ev: 'clean', ch: c }); }
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

// display ads and ad labels: hidden (never chat, emotes or channel points)
if (CFG.on) {
    var css = CFG.hide || '';
    if (css) {
        var st = document.createElement('style'); st.id = 'hallow-twitch-css';
        st.textContent = css + '{display:none!important}';
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
var cover = { end: 0, ids: {}, host: null, root: null, muted: [], timer: 0, ch: null };
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
function showCover(m) {
    var add = 0, fresh = !cover.end;
    for (var id in m.ids || {}) if (!(id in cover.ids)) { cover.ids[id] = 1; add += m.ids[id] || 0; }
    if (fresh) { cover.end = Date.now() + Math.max(m.secs || 0, add, 3) * 1000; cover.ch = m.ch; }
    else if (add > 0) cover.end += add * 1000;
    if (!cover.timer) cover.timer = setInterval(drawCover, 250);
    drawCover();
    if (fresh) toHost({ ev: 'cover', ch: m.ch, secs: Math.round((cover.end - Date.now()) / 1000) });
}
function liftCover(why) {
    if (!cover.end) return;
    clearInterval(cover.timer); cover.timer = 0; cover.end = 0;
    if (cover.host && cover.host.parentNode) cover.host.parentNode.removeChild(cover.host);
    for (var i = 0; i < cover.muted.length; i++) try { cover.muted[i].muted = false; } catch (e) { }
    cover.muted = [];
    log({ ev: 'lift' }); toHost({ ev: 'lift', ch: cover.ch, why: why });
}
window.__hallowTwitch.cover = function () { return cover.end ? { left: (cover.end - Date.now()) / 1000, shown: !!(cover.host && cover.host.isConnected), text: cover.root && cover.root.getElementById('t').textContent } : null; };

function onMsg(m) {
    if (!m || !m.ev) return;
    log(m);
    if (m.ev === 'cover') { showCover(m); return; }
    if (m.ev === 'swap' || m.ev === 'allowed' || m.ev === 'clean') toHost(m);
}
try { new BroadcastChannel(NONCE).onmessage = function (e) { onMsg(e.data); }; } catch (e) { }

if (CFG.on) {
    core(window, CFG, onMsg);
    // every classic worker starts with the same prelude (Twitch's player fetches its playlists in one)
    // (with the page's channel at that moment, in case the player fetched its master playlist outside the worker)
    var prelude = function () {
        var c = {}; for (var k in CFG) c[k] = CFG[k]; c.ch = CH_NOW();
        return '(' + core.toString() + ')(self,' + JSON.stringify(c) + ',(function(){var bc;try{bc=new BroadcastChannel(' + JSON.stringify(NONCE) + ')}catch(e){}return function(m){try{bc&&bc.postMessage(m)}catch(e){}}})());\n';
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
