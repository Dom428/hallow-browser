// hallow-twitch 5
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
// importScripts() Twitch's amazon-ivs worker, and fetches usher's master playlist at /api/v2/channel/hls/[channel].m3u8 and the media
// playlists from <cluster>.playlist.ttvnw.net/v1/playlist/... (no ".m3u8" in that address). Version 1 only knew /api/channel/hls/,
// so it never learnt which channel a worker was playing: opening a stream from the front page or the directory (Twitch keeps the
// worker across its in-page navigation) let the ad play. The channel now always comes from the worker's own last master playlist -
// never from the page address, which can be stale after in-page navigation. Second signal: when the player shows its own ad label
// or countdown anyway, the video is covered and muted until the label goes away.
//
// Version 3 (Hallow Browser 1.12.1, real use: the player sometimes showed as a plain white box): no hide rule can hide a box that
// holds the video any more, and a once-a-second watchdog on channel pages shows the player again if something hid it, hides an empty
// ad frame laid over it, and nudges a video that is stuck while it should play (see "the player guard and watchdog" below).
//
// Version 4 (Hallow Browser 1.14.2, real use: "heard the ad, then a white screen" - an ad that was neither swapped nor covered, then a
// white player under Twitch's controls): an ad that gets through is covered and muted whichever way it shows itself - ad markers in a
// playlist (more kinds of marker, also playlists fetched with XMLHttpRequest and in module workers), the player's ad labels, the ad
// events Twitch's page reports, or a second video playing in an ad box. A white, blank or frozen player is found by sampling the
// video's picture (a 32 x 18 canvas) and its state, and recovered step by step (nudge, reload the source, the backup stream, the page).
// Every decision is kept (window.__hallowTwitch.decisions) for Hallow Browser's "Report a Twitch problem".
//
// Version 5 (Hallow Browser 1.16.1, real use: the dark cover showed - every backup v4 tried had failed): keep the stream going. Written
// fresh after reading how the well-known open-source approaches describe it (no code taken from them):
//  - more backups, all asked at once: embed, popout, autoplay (as Twitch's Android app asks for it), site, picture-by-picture,
//    thunderdome, frontpage - the first one in that order with no ad marker is used (the one that worked last time is asked first);
//  - a lower quality is fine for the break: when no backup is clean at the player's quality, their 480p / 360p streams are tried;
//  - the backup token is asked like Twitch's own page asks (its device id and integrity headers, never the sign-in);
//  - the player's own token is asked as "popout" (fewer ads are stitched into that one), never on a channel you allow ads on;
//  - a small "ad-free backup (480p)" badge shows on the player while a backup plays the break; a lower-quality backup goes back to the
//    player's own quality after the break (the player loads its stream again);
//  - every backup asked for is in the decisions with why it failed (token / usher / playlist error, ad marker), so "Report a Twitch
//    problem" shows it. The dark cover stays only for when every backup has the ad too.
'use strict';
if (!CFG || window.__hallowTwitch) return;
var HERE = location.href;
var ON_TWITCH = /^https:\/\/([a-z0-9-]+\.)?twitch\.tv\//i.test(HERE) || (CFG.test && HERE.indexOf(CFG.test + '/') === 0);
if (!ON_TWITCH) return;
window.__hallowTwitch = { v: CFG.version, events: [] };

// ---------------------------------------------------------------- the part that runs in the page and in every worker
function core(G, CFG, post, sub) {
    if (G.__hallowTwCore) return;
    var realFetch = G.fetch && G.fetch.bind(G);
    if (!realFetch) return;
    G.__hallowTwCore = { swaps: 0, covers: 0, alt: 0, xhr: 0 };
    var ST = G.__hallowTwCore;
    var USHER = CFG.usher, GQL = CFG.gql;
    // v5: the backups, in the order they are preferred. An older browser passes its own shorter CFG.types: this script's list is used.
    var TYPES = CFG.types5 || ['embed', 'popout', 'autoplay', 'site', 'picture-by-picture', 'thunderdome', 'frontpage'];
    var PLATFORM = { autoplay: 'android' };   // v5: autoplay is asked for as Twitch's Android app asks for it (a 360p stream)
    var HDR = {};                             // v5: Twitch's own device id / integrity headers, as the page sends them (never the sign-in)
    (function () { var h = CFG.hdr || {}; for (var k in h) HDR[k] = h[k]; })();
    var Q = 'query PlaybackAccessToken_Template($login: String!, $isLive: Boolean!, $vodID: ID!, $isVod: Boolean!, $playerType: String!, $platform: String!) {' +
        '  streamPlaybackAccessToken(channelName: $login, params: {platform: $platform, playerBackend: "mediaplayer", playerType: $playerType}) @include(if: $isLive) {    value    signature   __typename  }' +
        '  videoPlaybackAccessToken(id: $vodID, params: {platform: $platform, playerBackend: "mediaplayer", playerType: $playerType}) @include(if: $isVod) {    value    signature   __typename  }}';
    var variants = {};   // media playlist URL -> { ch, res, bw }
    var masters = {};    // channel -> the player's own usher address
    var altMasters = {}; // v5: channel|type -> { list (its qualities), t }
    var sticky = {};     // channel -> { type, low } that was clean last time
    var inAd = {};       // channel -> true while its playlists carry an ad
    var noAlt = {};      // channel -> until when not to look again (every alternative had the ad a moment ago)
    var lastCh = null;
    var onAlt = {};      // channel -> { type, low, res, lower } the player is on now (it stays there until its next master playlist)
    var preferAlt = {};  // v4: channel -> { type, until } - the page asked for the backup stream (the player's own one stayed white / stuck)
    var adWhyLast = {};  // v5: channel -> the marker of its current break
    var sawLive = false; // v4: this worker has seen Twitch's "live" segment titles (so another title means something else: an ad)

    function allowed(ch) { return (CFG.allow || []).indexOf(ch) >= 0; }
    // v4: which marker says "ad" (null: none). Twitch's live segments are titled "live"; ads "Amazon|..." or other titles, with
    // DATERANGE tags (CLASS twitch-stitched-ad / twitch-ad-quartile / X-TV-TWITCH-AD-* attributes) or cue tags.
    function adWhy(text) {
        if (/CLASS="[^"]*(stitched|-ad"|-ad-|-ads"|midroll|preroll)/i.test(text)) return 'class';
        if (/X-TV-TWITCH-AD/.test(text)) return 'x-tv-twitch-ad';
        if (/^#EXTINF:[^\n,]*,\s*Amazon/mi.test(text)) return 'amazon';
        if (/^#EXT-X-(CUE-OUT|SCTE35)|SCTE35-OUT|#EXT-OATCLS-SCTE35/m.test(text)) return 'cue';
        if (/^#EXTINF:[0-9.]+,[ \t]*live[ \t\r]*$/m.test(text)) sawLive = true;
        if (sawLive && /^#EXTINF:[0-9.]+,[ \t]*(?!live[ \t\r]*$)[^\s][^\n]*$/m.test(text)) return 'title';   // a non-"live" title where there were "live" ones
        return null;
    }
    function hasAd(text) { return !!adWhy(text); }
    // v4: decisions are kept for "Report a Twitch problem" (always, only the transitions - not every playlist)
    function note(m) { post({ ev: 'dec', m: m }); }
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
    // usher's master playlist address: /api/channel/hls/[channel].m3u8 or (what twitch.tv uses now) /api/v2/channel/hls/[channel].m3u8
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
    // v5: a backup's token, asked like Twitch's own page asks (its device id and integrity headers, no cookies, no sign-in).
    // Fails with a short reason: token-http-<status>, token-<GraphQL error>, token-network.
    function token(ch, type) {
        var body = JSON.stringify({ operationName: 'PlaybackAccessToken_Template', query: Q,
            variables: { isLive: true, login: ch, isVod: false, vodID: '', playerType: type, platform: PLATFORM[type] || 'web' } });
        var h = { 'Client-ID': CFG.clientId, 'Content-Type': 'text/plain;charset=UTF-8' };
        for (var k in HDR) if (HDR[k]) h[k] = HDR[k];
        return realFetch(GQL, { method: 'POST', body: body, credentials: 'omit', headers: h, __hallow: 1 })
            .then(function (r) { if (!r.ok) throw 'token-http-' + r.status; return r.json(); }, function () { throw 'token-network'; })
            .then(function (j) {
                var t = j && j.data && j.data.streamPlaybackAccessToken;
                if (!(t && t.value && t.signature)) throw 'token-' + (j && j.errors && j.errors[0] && String(j.errors[0].message || 'error').slice(0, 40).replace(/[^\w -]/g, '') || 'none');
                return t;
            });
    }
    // a backup's qualities: token -> usher (the player's own usher address with the backup's token); kept 5 minutes
    function masterFor(ch, type) {
        var k = ch + '|' + type, m = altMasters[k];
        if (m && Date.now() - m.t < 5 * 60000) return Promise.resolve(m.list);
        return token(ch, type).then(function (t) {
            var u = new URL(masters[ch] || (USHER + '/api/channel/hls/' + ch + '.m3u8?allow_source=true&fast_bread=true&player_backend=mediaplayer&playlist_include_framerate=true&reassignments_supported=true&supported_codecs=avc1'));
            u.searchParams.set('sig', t.signature); u.searchParams.set('token', t.value); u.searchParams.set('p', String(Math.floor(Math.random() * 9999999)));
            u.searchParams.delete('parent_domains');
            ST.alt++;
            return realFetch(u.href, { credentials: 'omit' })
                .then(function (r) { if (!r.ok) throw 'usher-' + r.status; return r.text(); }, function (e) { throw typeof e === 'string' ? e : 'usher-network'; })
                .then(function (txt) { var list = parseMaster(txt, u.href); if (!list.length) throw 'usher-no-variants'; altMasters[k] = { list: list, t: Date.now() }; return list; });
        });
    }
    function height(res) { var m = /x(\d+)$/.exec(res || ''); return m ? +m[1] : 0; }
    function label(v) { var h = v && height(v.res); return h ? h + 'p' : (v && v.res) || '?'; }
    // v5: the "low quality for the break" stream: the best one at 480p or below (else the smallest there is)
    function lowOf(list) {
        var best = null, min = null;
        for (var i = 0; i < list.length; i++) {
            var h = height(list[i].res);
            if (h && h <= 480 && (!best || list[i].bw > best.bw)) best = list[i];
            if (!min || list[i].bw < min.bw) min = list[i];
        }
        return best || min;
    }
    // v5: one backup: its media playlist at the player's quality (or its low one) -> { type, low, ok, why, text, v }. 6 s at most.
    function attempt(ch, type, want, low) {
        var r = { type: type, low: !!low }, k = ch + '|' + type;
        var p = masterFor(ch, type).then(function (list) {
            var v = low ? lowOf(list) : pick(list, want); r.v = v;
            return realFetch(v.uri, { credentials: 'omit' }).then(function (x) { if (!x.ok) throw 'playlist-' + x.status; return x.text(); }, function (e) { throw typeof e === 'string' ? e : 'playlist-network'; });
        }).then(function (text) {
            if (r.done) return r;
            r.text = text;
            if (!/#EXTINF/.test(text)) { r.why = 'empty'; return r; }
            var w = adWhy(text); if (w) { r.why = 'ad/' + w; return r; }
            r.ok = true; return r;
        }, function (e) {
            if (!r.done) r.why = typeof e === 'string' ? e : 'error';
            if (/^playlist-/.test(r.why || '')) delete altMasters[k];
            return r;
        });
        var limit = new Promise(function (res) { setTimeout(function () { if (!r.ok && !r.why) r.why = 'timeout'; res(r); }, 6000); });
        return Promise.race([p, limit]).then(function (x) { x.done = true; return x; });
    }
    function asResponse(text, url) {
        var r = new Response(text, { status: 200, headers: { 'Content-Type': 'application/vnd.apple.mpegurl' } });
        try { Object.defineProperty(r, 'url', { value: url }); } catch (e) { }
        return r;
    }
    // v5: every backup asked for, for the decisions: "embed:ad/class, popout:token-http-403, autoplay/android:clean 360p, ..."
    function summary(rs) {
        return rs.map(function (r) { return r.type + (PLATFORM[r.type] ? '/' + PLATFORM[r.type] : '') + (r.low ? '@low' : '') + ':' + (r.ok ? 'clean ' + label(r.v) : (r.why || '?') + (r.v && r.low ? ' ' + label(r.v) : '')); }).join(', ');
    }
    // an ad in this channel's playlist: the backup that was clean last time first, else every backup at once (the first clean one in
    // TYPES order wins), else their lower qualities, else the original under the cover (and no new look for 8 s)
    function onAd(ch, url, text) {
        var want = variants[url], st = sticky[ch], tried = [];
        if (Date.now() < (noAlt[ch] || 0)) return coverIt(ch, url, text, null);
        var first = st ? attempt(ch, st.type, want, st.low) : Promise.resolve(null);
        return first.then(function (r0) {
            if (r0) { tried.push(r0); if (r0.ok) return use(ch, url, r0, tried, want); }
            var rest = TYPES.filter(function (t) { return !(st && !st.low && t === st.type); });
            return Promise.all(rest.map(function (t) { return attempt(ch, t, want, false); })).then(function (rs) {
                tried = tried.concat(rs);
                for (var i = 0; i < rs.length; i++) if (rs[i].ok) return use(ch, url, rs[i], tried, want);
                // lower quality for the break: the 480p / 360p stream of each backup that answered (when it is another stream)
                var again = TYPES.filter(function (t) {
                    var m = altMasters[ch + '|' + t], at = null;
                    for (var j = 0; j < tried.length; j++) if (tried[j].type === t && !tried[j].low) at = tried[j];
                    if (!m) return false;
                    if (st && st.low && st.type === t) return false;           // asked first already
                    return !(at && at.v && lowOf(m.list).uri === at.v.uri);   // that one was already the low one
                });
                return Promise.all(again.map(function (t) { return attempt(ch, t, want, true); })).then(function (ls) {
                    tried = tried.concat(ls);
                    for (var j = 0; j < ls.length; j++) if (ls[j].ok) return use(ch, url, ls[j], tried, want);
                    return coverIt(ch, url, text, tried);
                });
            });
        });
    }
    function use(ch, url, r, tried, want) {
        var prev = onAlt[ch], h = height(r.v && r.v.res), hw = want ? height(want.res) : 0, lower = !!(h && hw && h < hw);
        sticky[ch] = { type: r.type, low: r.low };
        onAlt[ch] = { type: r.type, low: r.low, res: label(r.v), lower: lower };
        ST.swaps++;
        if (!prev || prev.type !== r.type || prev.low !== r.low || tried.length > 1)
            note('swap ' + ch + ' -> ' + r.type + ' ' + label(r.v) + (lower ? ' (lower quality for the break, the player had ' + label(want) + ')' : '') + ' [' + summary(tried) + ']');
        post({ ev: 'swap', ch: ch, type: r.type, res: label(r.v), lower: lower });
        return asResponse(r.text, url);
    }
    function coverIt(ch, url, text, tried) {
        if (tried) noAlt[ch] = Date.now() + 8000;
        var info = adInfo(text); ST.covers++;
        if (tried) note('no clean backup for ' + ch + ' [' + summary(tried) + '] - cover ' + info.secs + ' s');
        post({ ev: 'cover', ch: ch, secs: info.secs, ids: info.ids });
        return asResponse(text, url);
    }

    G.fetch = function (input, init) {
        var url;
        try { url = typeof input === 'string' ? input : input instanceof URL ? input.href : input && input.url; } catch (e) { }
        // v4: Twitch's playlists now carry a DATERANGE CLASS="twitch-trigger" with an X-TV-TWITCH-TRIGGER-URL on its playlist hosts
        // (<cluster>.playlist.ttvnw.net/trigger/...; seen on the real twitch.tv, Nov 2026). What the player gets from it is looked at:
        // a playlist or an answer about ads there is kept in the decisions and covered (never changed or blocked)
        if (url && /^https:\/\/[a-z0-9-]+\.playlist\.ttvnw\.net\/trigger\//i.test(url)) {
            return realFetch(input, init).then(function (resp) {
                try {
                    resp.clone().text().then(function (body) {
                        var why = /#EXTINF/.test(body) ? adWhy(body) : (/"[a-z_]*(ad|ads|pod|commercial)[a-z_]*"\s*:/i.test(body) ? 'trigger-answer' : null);
                        note('trigger ' + resp.status + ', ' + body.length + ' bytes' + (why ? ', ad (' + why + ')' : ''));
                        dbg('trigger body ' + body.slice(0, 400));
                        if (why && /#EXTINF/.test(body)) { var inf = adInfo(body); post({ ev: 'cover', ch: lastCh, secs: inf.secs, ids: inf.ids }); }
                    }).catch(function () { });
                } catch (e) { }
                return resp;
            });
        }
        if (url && CFG.diag && /ttvnw\.net|twitch\.tv/.test(url) && !/\.ts(\?|$)|\.mp4(\?|$)|segment/.test(url) && !playlistish(url)) dbg('fetch ' + url.split('?')[0].slice(0, 100));
        if (!url || !playlistish(url)) return realFetch(input, init);
        var ch = channelOf(url);
        // v5: usher is told the player is embedded on another site by parent_domains - that stream gets more ads: left out
        if (ch && /[?&]parent_domains=/.test(url)) { try { var pu = new URL(url); pu.searchParams.delete('parent_domains'); url = pu.href; input = url; note('parent_domains left out of the master playlist address'); } catch (e) { } }
        return realFetch(input, init).then(function (resp) {
            if (!resp.ok) return resp;
            return resp.clone().text().then(function (text) {
                return handle(url, ch, text, resp);
            });
        });
    };

    // a playlist the player fetched: the master tells which quality is which; a media playlist with an ad is swapped (fetch) or covered
    function handle(url, ch, text, resp) {
        if (text.indexOf('#EXT-X-STREAM-INF') >= 0) {   // a master playlist: remember which quality is which
            // (the worker plays one stream at a time: its newest master says which channel - an unknown one forgets the old)
            if (ch !== lastCh) note('master ' + (ch || '?') + (resp ? '' : ' (xhr)'));
            lastCh = ch;
            dbg('master ' + (ch || '?') + ' ' + url.split('?')[0].slice(0, 90));
            if (ch) {
                masters[ch] = url; delete onAlt[ch];   // a new session of the player's own: it starts on the original
                var pa = preferAlt[ch];                 // v4: ...unless the page asked for the backup stream (the original stayed white)
                if (pa && Date.now() < pa.until) { onAlt[ch] = { type: pa.type, low: false, res: '?', lower: false }; note('backup stream for ' + ch + ': ' + pa.type); }
                var list = parseMaster(text, url);
                for (var i = 0; i < list.length; i++) variants[list[i].uri] = { ch: ch, res: list[i].res, bw: list[i].bw };
            }
            return resp;
        }
        if (text.indexOf('#EXTINF') < 0) return resp;
        var v = variants[url], c = v ? v.ch : lastCh, why = adWhy(text), ad = !!why;
        dbg('media ' + (c || '?') + (v ? ' ' + v.res : ' (not in master)') + ' ad=' + ad);
        if (!c || !resp) {   // an ad on a stream we can't name, or one fetched with XMLHttpRequest: no swap - covered
            var key = c || '?';
            if (ad && !inAd[key]) { inAd[key] = true; var inf = adInfo(text); ST.covers++; note('ad (' + why + ') on ' + key + (resp ? ' - no channel, cover' : ' via xhr - cover') + ' ' + inf.secs + ' s'); post({ ev: 'cover', ch: c || null, secs: inf.secs, ids: inf.ids }); }
            else if (ad) { var inf2 = adInfo(text); post({ ev: 'cover', ch: c || null, secs: inf2.secs, ids: inf2.ids }); }
            else if (inAd[key]) { inAd[key] = false; note('clean ' + key); post({ ev: 'clean', ch: c || null }); }
            return resp;
        }
        if (!ad) {
            var oa = onAlt[c];
            if (inAd[c]) {
                inAd[c] = false;
                note('clean ' + c + (oa ? (oa.lower ? ' - back to the player\'s own quality (the player loads its stream again)' : ' (staying on ' + oa.type + ' ' + oa.res + ', the same quality)') : ''));
                post({ ev: 'clean', ch: c, main: !!(oa && oa.lower) });
                if (oa && oa.lower) oa.backBy = Date.now() + 5000;
            }
            // After a swap the player STAYS on the alternative: on the real Twitch, going back to the original stream mid-session
            // stalls the player for good (checked Nov 2026: paused, buffer empty, never recovers). The alternative is the same
            // live stream at the same quality, so nothing is lost; the player's next master playlist starts over.
            // v5: a LOWER quality backup is left after the break: the page loads the player's stream again (a new master playlist starts
            // on the original); if that hasn't happened within 5 s, the worker goes back to the original stream itself.
            if (oa && oa.backBy && Date.now() > oa.backBy) { delete onAlt[c]; note('back to the original stream of ' + c + ' (no new master playlist came)'); return resp; }
            var pa2 = preferAlt[c];
            if (!oa && pa2 && Date.now() < pa2.until) { oa = onAlt[c] = { type: pa2.type, low: false, res: '?', lower: false }; note('backup stream for ' + c + ': ' + pa2.type); }
            if (oa) {
                return attempt(c, oa.type, v, oa.low).then(function (r) {
                    if (r.ok) return asResponse(r.text, url);
                    if (/^ad\//.test(r.why || '') && !oa.backBy) { inAd[c] = true; note('ad (' + r.why.slice(3) + ') on the backup ' + oa.type); return onAd(c, url, r.text); }   // the backup has its own ad now
                    if (onAlt[c] === oa) delete onAlt[c];
                    note('backup ' + oa.type + ' gone (' + (r.why || '?') + ') - back to the original'); return resp;
                });
            }
            return resp;
        }
        var first = !inAd[c]; inAd[c] = true;
        if (first) note('ad (' + why + ') on ' + c + (v ? ' ' + v.res : '') + ' - ' + adInfo(text).secs + ' s');
        else if (adWhyLast[c] !== why) note('the ad on ' + c + ' is now marked by ' + why);   // v5: the break goes on with another marker
        adWhyLast[c] = why;
        if (allowed(c)) { if (first) post({ ev: 'allowed', ch: c }); return resp; }
        return onAd(c, url, text);
    }

    // v4: the page can ask for the backup stream (the player's own stream stayed white or stuck after a break)
    if (sub) sub(function (m) {
        if (m.cmd === 'backup' && m.ch) { preferAlt[m.ch] = { type: sticky[m.ch] && !sticky[m.ch].low ? sticky[m.ch].type : TYPES[0], until: Date.now() + 10 * 60000 }; note('page asked for the backup stream of ' + m.ch); }
        if (m.cmd === 'hdr' && m.h) for (var k in m.h) if (typeof m.h[k] === 'string') HDR[k] = m.h[k];   // v5: the page's newest Twitch headers
    });

    // v4: playlists fetched with XMLHttpRequest are looked at too (Twitch's player uses fetch today; if that changes, an ad that comes
    // that way is at least covered and muted - it can't be swapped, the response is read-only)
    var XO = G.XMLHttpRequest;
    if (XO && XO.prototype && !XO.prototype.__hallowTw) {
        try {
            var xOpen = XO.prototype.open, xSend = XO.prototype.send;
            XO.prototype.open = function (m, u) { try { this.__hallowUrl = new URL(String(u), G.location.href).href; } catch (e) { this.__hallowUrl = null; } return xOpen.apply(this, arguments); };
            XO.prototype.send = function () {
                var x = this, u = x.__hallowUrl;
                if (u && /^https?:/i.test(u) && playlistish(u)) {
                    x.addEventListener('load', function () {
                        try { if ((x.responseType === '' || x.responseType === 'text') && x.status === 200) { ST.xhr++; handle(u, channelOf(u), x.responseText, null); } } catch (e) { }
                    });
                }
                return xSend.apply(this, arguments);
            };
            XO.prototype.__hallowTw = 1;
        } catch (e) { }
    }
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
// v5: the headers Twitch's page sends with its own GraphQL calls (its device id, integrity token, client version and session), so the
// backup token is asked for the same way. Never Authorization (the sign-in) or cookies. Until the page sends one: a random device id.
var PAGE_HDR = { 'X-Device-Id': (function () { var s = '', a = 'abcdefghijklmnopqrstuvwxyz0123456789'; for (var i = 0; i < 32; i++) s += a.charAt(Math.floor(Math.random() * a.length)); return s; })() };
var HDR_KEYS = { 'x-device-id': 'X-Device-Id', 'device-id': 'X-Device-Id', 'client-integrity': 'Client-Integrity', 'client-version': 'Client-Version', 'client-session-id': 'Client-Session-Id' };
function grabHeaders(input, init) {
    var h = init && init.headers || (input && typeof input === 'object' && input.headers), got = 0;
    if (!h) return;
    var take = function (k, v) { var n = HDR_KEYS[String(k).toLowerCase()]; if (n && typeof v === 'string' && v && PAGE_HDR[n] !== v) { PAGE_HDR[n] = v; got++; } };
    try {
        if (typeof h.forEach === 'function' && !Array.isArray(h)) h.forEach(function (v, k) { take(k, v); });
        else if (Array.isArray(h)) h.forEach(function (p) { if (p && p.length === 2) take(p[0], p[1]); });
        else for (var k in h) take(k, h[k]);
    } catch (e) { }
    if (got) {
        var m = { cmd: 'hdr', h: PAGE_HDR };
        try { pageCmd(m); } catch (e) { }
        try { if (bcOut) bcOut.postMessage(m); } catch (e) { }
        if (!window.__hallowTwitch.hdr) { window.__hallowTwitch.hdr = 1; decide('Twitch\'s own request headers are used for the backup tokens (' + Object.keys(PAGE_HDR).join(', ') + ')'); }
    }
}
// v5: the player's own token is asked as "popout" instead of "site" (as the well-known approaches do: fewer ads are stitched into it).
// Only live streams, never on a channel you allow ads on.
function forcePopout(list) {
    if (CFG.forcePopout === false) return 0;
    var n = 0;
    for (var i = 0; i < list.length; i++) {
        var o = list[i], va = o && o.variables;
        if (o && /^PlaybackAccessToken/.test(o.operationName || '') && va && va.isLive && va.playerType === 'site') { va.playerType = 'popout'; n++; }
    }
    if (n) { window.__hallowTwitch.forced = (window.__hallowTwitch.forced || 0) + n; if (window.__hallowTwitch.forced === n) decide('the player\'s own stream token is asked as popout (not site)'); }
    return n;
}
if (CFG.on && AD_OPS.length && window.fetch) {
    var pageFetch = window.fetch.bind(window);
    window.fetch = function (input, init) {
        if (init && init.__hallow) return pageFetch(input, init);   // v5: Hallow's own backup token request (from the page's core)
        try {
            var url = typeof input === 'string' ? input : input && input.url;
            if (url && url.indexOf(CFG.gql) === 0) try { grabHeaders(input, init); } catch (e) { }
            if (url && url.indexOf(CFG.gql) === 0 && init && typeof init.body === 'string' && (CFG.allow || []).indexOf(CH_NOW()) < 0) {
                var b = JSON.parse(init.body), list = Array.isArray(b) ? b : [b];
                try { if (forcePopout(list)) { var nb = {}; for (var ik in init) nb[ik] = init[ik]; nb.body = JSON.stringify(Array.isArray(b) ? list : list[0]); init = nb; } } catch (e) { }
                try { gqlAdEvents(list); } catch (e) { }   // v4: Twitch's page reports a playing ad: a signal to cover it
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
    if (fresh) { cover.end = Date.now() + Math.max(m.secs || 0, add, 3) * 1000; cover.ch = m.ch; cover.why = m.why || 'playlist'; }
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
// v4: an opaque white sheet over (nearly) all of the video with at most a button or a short line on it - real use showed Twitch's
// controls and one button over a plain white player. Twitch's own screens over the player (the mature-content gate, errors, the
// extension overlay) are dark, transparent or have real text, and stay.
function whiteSheet(el, cs, vr) {
    var m = /rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/.exec(cs.backgroundColor || '');
    if (!m || +m[1] < 235 || +m[2] < 235 || +m[3] < 235 || (m[4] !== undefined && +m[4] < 0.9)) return false;
    if (overlap(el.getBoundingClientRect(), vr) < 0.8) return false;
    return (el.innerText || '').trim().length <= 40 && !el.querySelector('video,canvas,input,textarea,iframe[src*="ext-twitch.tv"]');
}
function isBlocker(el, v, vr) {
    if (el === v || el.contains(v) || el.tagName === 'HALLOW-ADBREAK' || overlap(el.getBoundingClientRect(), vr) < 0.5) return false;
    if (el.tagName === 'IFRAME') { var src = el.getAttribute('src') || ''; return !src || src === 'about:blank' || AD_SRC.test(el.src); }
    if (el.closest && el.closest('[data-a-target="player-controls"],.player-controls,.extension-view__iframe')) return false;
    var cs = getComputedStyle(el);
    if (emptyWhite(el, cs)) return true;
    if (whiteSheet(el, cs, vr)) return true;   // v4
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
        if (WD.tries === 3) backup('stuck');   // v4: the third try is on the backup stream
        nudge(WD.tries > 1);
    } else if (WD.tries >= 3 && now - WD.lastTry >= 12000 && now - SIG.lastAd < 5 * 60000) {
        WD.lastTry = now; pageReload('stuck after an ad');   // v4: still stuck after an ad break: the page, at most once in 10 minutes
    }
}

// ---------------------------------------------------------------- version 4 (Hallow Browser 1.14.2): an ad that gets through, a white player
// Real use: "heard the ad, then a white screen" - the ad was neither swapped nor covered, then the player stayed white under Twitch's
// controls. Now:
//  4. Any sign that an ad plays covers and mutes it (the cover above): ad markers in a playlist (the worker, also XMLHttpRequest), the
//     player's ad labels / countdown (more of their names, and a short "Ad" / "Ad 1 of 2" label in the player), the ad events Twitch's
//     page reports to gql.twitch.tv (an impression or quartile), or a second video playing with sound in an ad box.
//  5. A white, blank or frozen player is recovered: the video's picture is sampled (32 x 18 pixels on a canvas, every 2 s) together
//     with its state. Nearly all white and flat for 6 s, no picture at all (readyState / videoWidth) for 6 s while it should play, or a
//     picture that stopped changing while time runs on for 8 s within a minute after an ad: nudge, reload the source, the backup stream
//     (Twitch's own stream as another player type), and - only within 5 minutes after an ad, at most once in 10 minutes - the page.
var SIG = { gql: 0, gqlLast: null, video: 0, label: 0, lastAd: 0, gqlUntil: 0, domSince: 0, domMuteUntil: 0 };
window.__hallowTwitch.signals = SIG;
var DEC = window.__hallowTwitch.decisions = [], SEEN = {};
function decide(m) { DEC.push(new Date().toISOString().slice(11, 19) + ' ' + m); if (DEC.length > 150) DEC.shift(); }
function adSeen() { SIG.lastAd = Date.now(); }
function backup(why) {
    var ch = CH_NOW(); if (!ch) return;
    decide('backup stream asked (' + why + ')');
    try { if (bcOut) bcOut.postMessage({ cmd: 'backup', ch: ch }); } catch (e) { }
    pageCmd({ cmd: 'backup', ch: ch });
}
var pageSubs = [];
function pageSub(f) { pageSubs.push(f); }
function pageCmd(m) { for (var i = 0; i < pageSubs.length; i++) try { pageSubs[i](m); } catch (e) { } }

// 4b. the ad events Twitch's page reports (they are answered here, not sent - see above): an impression / quartile = an ad plays now
function gqlAdEvents(list) {
    for (var i = 0; i < list.length; i++) {
        var o = list[i], n = o && o.operationName || '';
        if (!/Ad/.test(n) || AD_OPS.indexOf(n) < 0 && !/AdEvent|VideoAd/.test(n)) continue;
        var inp = o.variables && (o.variables.input || o.variables) || {}, ev = String(inp.eventName || inp.event_name || inp.event || '');
        var pl = {}; try { pl = typeof inp.eventPayload === 'string' ? JSON.parse(inp.eventPayload) : (inp.eventPayload || {}); } catch (e) { }
        SIG.gqlLast = (n + ' ' + ev).trim().slice(0, 80);
        if (/pod_complete|pod_end|break_end|ad_end|_error$/i.test(ev)) { if (SIG.gqlUntil) { SIG.gqlUntil = Date.now() + 1000; decide('gql ' + ev); } continue; }
        if (!/impression|quartile|ad_start|break_start|pod_start/i.test(ev)) continue;
        if (/quartile/i.test(ev) && +(pl.quartile || pl.ad_quartile || 0) >= 4) { SIG.gqlUntil = Math.min(SIG.gqlUntil || 0, Date.now() + 3000); continue; }
        var d = +(pl.ad_duration || pl.duration || pl.roll_duration || pl.pod_duration || 0) || 15;   // no length given: 15 s, longer with each quartile
        if (!SIG.gqlUntil || Date.now() > SIG.gqlUntil) decide('gql ad event ' + ev + ' (' + Math.round(d) + ' s)');
        SIG.gql++; adSeen();
        SIG.gqlUntil = Math.max(SIG.gqlUntil || 0, Date.now() + Math.min(d, 120) * 1000 + 2000);
        if (!allowedHere(null)) showCover({ ev: 'cover', ch: CH_NOW(), secs: Math.min(d, 120), why: 'gql' });
    }
}
function gqlTick() {
    if (!SIG.gqlUntil) return;
    if (Date.now() < SIG.gqlUntil) { if (cover.end && cover.end < SIG.gqlUntil) cover.end = SIG.gqlUntil; return; }
    SIG.gqlUntil = 0;
    if (cover.end && !cover.dom && cover.why === 'gql') cover.end = Math.min(cover.end, Date.now() + 500);   // the pod is over
}

// 4c. a second video playing with sound in an ad box (a picture-by-picture / client-side ad player): muted, and the player covered
function adVideo(main) {
    if (allowedHere(null)) return;
    var vs = document.querySelectorAll('video'), found = false;
    for (var i = 0; i < vs.length; i++) {
        var x = vs[i]; if (x === main || x.paused || x.readyState < 2) continue;
        var boxed = false; for (var p = x.parentElement, k = 0; p && k < 8; p = p.parentElement, k++) if (adish(p) || (p.classList && p.classList.contains('pbyp-player-instance'))) { boxed = true; break; }
        if (!boxed) continue;
        found = true;
        if (!x.muted) { x.muted = true; cover.muted.push(x); }
    }
    if (found) {
        adSeen();
        if (!SIG.video) { SIG.video = 1; decide('a second video plays in an ad box - covered'); log({ ev: 'ad-video' }); }
        if (!allowedHere(null)) { if (!cover.end) showCover({ ev: 'cover', ch: CH_NOW(), secs: 3, why: 'video' }); var until = Date.now() + 2500; if (until > cover.end) cover.end = until; }
    } else SIG.video = 0;
}

// 5. a white / blank / frozen player
var BL = { why: null, since: 0, okSince: 0, tries: 0, lastTry: 0, lastSample: 0, s: null, hash: null, sameSince: 0, lastCt: -1, canvas: 'unknown', recovered: 0, reloads: 0 };
window.__hallowTwitch.blank = BL;
var cvs = null, cctx = null;
function sample(v) {
    if (!v.videoWidth || v.readyState < 2) return null;
    try {
        if (!cvs) { cvs = document.createElement('canvas'); cvs.width = 32; cvs.height = 18; cctx = cvs.getContext('2d', { willReadFrequently: true }); }
        cctx.drawImage(v, 0, 0, 32, 18);
        var d = cctx.getImageData(0, 0, 32, 18).data, white = 0, black = 0, lo = 255, hi = 0, sum = 0, h = 0;
        for (var i = 0; i < d.length; i += 4) {
            var l = (d[i] + d[i + 1] + d[i + 2]) / 3; sum += l; if (l < lo) lo = l; if (l > hi) hi = l;
            if (d[i] >= 235 && d[i + 1] >= 235 && d[i + 2] >= 235) white++; if (l <= 16) black++;
            h = (h * 31 + (l >> 3)) | 0;
        }
        BL.canvas = 'ok';
        return { white: white / 576, black: black / 576, spread: hi - lo, mean: Math.round(sum / 576), hash: h };
    } catch (e) { BL.canvas = 'blocked'; return null; }
}
function shouldPlay(v) {
    if (v.paused || v.ended || document.hidden || cover.end) return false;
    var pl = v.closest && v.closest('[data-a-player-state]');
    return !(pl && /paused|ended|idle/i.test(pl.getAttribute('data-a-player-state') || ''));
}
function pageReload(why) {
    var now = Date.now(), last = 0; try { last = +sessionStorage.getItem('hallowTwReload') || 0; } catch (e) { }
    if (now - last < 10 * 60000) return false;
    try { sessionStorage.setItem('hallowTwReload', String(now)); } catch (e) { }
    BL.reloads++; decide('page reloaded (' + why + ')'); log({ ev: 'resync', type: 'page' });
    setTimeout(function () { location.reload(); }, 200);
    return true;
}
function blankCheck(v) {
    var now = Date.now();
    if (!shouldPlay(v)) { BL.since = 0; BL.why = null; BL.sameSince = 0; BL.lastCt = v.currentTime; return; }
    var why = BL.why;
    if (now - BL.lastSample >= 2000) {
        BL.lastSample = now; why = null;
        var s = sample(v); BL.s = s;
        var moving = BL.lastCt >= 0 && v.currentTime - BL.lastCt > 0.5; BL.lastCt = v.currentTime;
        if (s) {
            if (s.white >= 0.95 && s.spread <= 24) why = 'white';
            else if (s.hash === BL.hash && moving && now - SIG.lastAd < 60000) { if (!BL.sameSince) BL.sameSince = now; if (now - BL.sameSince >= 8000) why = 'frozen'; }
            else BL.sameSince = 0;
            BL.hash = s.hash;
        }
    }
    if (!why) { BL.since = 0; BL.why = null; if (!BL.okSince) BL.okSince = now; if (now - BL.okSince > 15000) BL.tries = 0; return; }
    BL.okSince = 0;
    if (BL.why !== why) { BL.why = why; BL.since = now; }
    if (now - BL.since < (why === 'white' ? 6000 : 0) || now - BL.lastTry < 6000) return;
    // a white picture: up to the backup stream, and the page only within 5 minutes after an ad; a frozen one (it could be a still
    // "be right back" screen): only the two gentle steps
    var maxTries = why === 'frozen' ? 2 : now - SIG.lastAd < 5 * 60000 ? 4 : 3;
    if (BL.tries >= maxTries) return;
    BL.tries++; BL.lastTry = now; BL.recovered++;
    wdEvent('blank-' + why);
    decide('player ' + why + ' - recovery step ' + BL.tries);
    if (BL.tries === 1) nudge(false);
    else if (BL.tries === 2) nudge(true);
    else if (BL.tries === 3) { backup('blank ' + why); nudge(true); }
    else pageReload('white player after an ad');
}function watchdog() {
    if (!document.body || !CH_NOW()) return;
    var v = playerVideo(); if (!v) return;
    try { guardPlayer(v); } catch (e) { }
    try { if (CFG.on && !allowedHere(null)) unblock(v); } catch (e) { }
    try { stuck(v); } catch (e) { }
    try { if (CFG.on) adVideo(v); } catch (e) { }
    try { gqlTick(); } catch (e) { }
    try { blankCheck(v); } catch (e) { }
}
if (CFG.on) setInterval(watchdog, 1000);

// v5: while a backup plays the break, a small badge on the player says so ("ad-free backup (480p)") - never over the middle of the
// picture, never in the way of a click; gone when the break is over (or after 3 minutes at most)
var badge = { host: null, root: null, on: false, text: '', timer: 0, since: 0 };
function drawBadge() {
    if (!badge.on) return;
    if (Date.now() - badge.since > 180000) { hideBadge(); return; }
    var v = bigVideo(), parent = document.fullscreenElement || document.documentElement; if (!parent) return;
    if (!badge.host) {
        badge.host = document.createElement('hallow-backup');
        badge.root = badge.host.attachShadow({ mode: 'closed' });
        badge.root.innerHTML = '<style>:host{all:initial;position:fixed;z-index:2147483646;display:block;pointer-events:none}span{display:block;font:600 11px "Segoe UI",sans-serif;color:#ECE8F4;background:rgba(20,16,27,.72);' +
            'border-left:3px solid ' + CFG.accent + ';border-radius:4px;padding:2px 7px;white-space:nowrap}</style><span id=t></span>';
    }
    if (badge.host.parentNode !== parent) parent.appendChild(badge.host);
    var r = v ? v.getBoundingClientRect() : { left: 0, top: 0 };
    badge.host.style.left = (r.left + 8) + 'px'; badge.host.style.top = (r.top + 8) + 'px';
    badge.root.getElementById('t').textContent = badge.text;
}
function showBadge(res) {
    badge.text = 'ad-free backup' + (res && res !== '?' ? ' (' + res + ')' : '');
    if (!badge.on) badge.since = Date.now();
    badge.on = true; drawBadge();
    if (!badge.timer) badge.timer = setInterval(drawBadge, 500);
}
function hideBadge() {
    badge.on = false; clearInterval(badge.timer); badge.timer = 0;
    if (badge.host && badge.host.parentNode) badge.host.parentNode.removeChild(badge.host);
}
window.__hallowTwitch.badge = function () { return badge.on && badge.host && badge.host.isConnected ? badge.text : null; };

function onMsg(m) {
    if (!m || !m.ev) return;
    if (m.ev === 'swap') {
        swappedSince = swappedSince || Date.now();
        if (!allowedHere(m.ch)) showBadge(m.res);
        // v5: a backup was found after the cover went up (the next look, 8 s later): the stream is back - the cover goes
        if (cover.end && !cover.dom && cover.why === 'playlist' && !(SIG.gqlUntil && Date.now() < SIG.gqlUntil)) liftCover('swap');
    }
    if (m.ev === 'cover' || m.ev === 'clean') hideBadge();
    if (m.ev === 'clean' && m.main) { decide('back to the player\'s own quality: the player loads its stream again'); nudge(true); }
    if (m.ev === 'clean' && swappedSince) { swappedSince = 0; afterBreak(); }
    if (m.ev === 'dbg') { var d = window.__hallowTwitch.diag; if (d) { d.push(Math.round(performance.now()) + ' ' + m.m); if (d.length > 400) d.shift(); } return; }
    if (m.ev === 'dec') { decide(String(m.m).slice(0, 700)); return; }
    if (m.ch) SEEN[m.ch] = 1;
    if (m.ev === 'cover' || m.ev === 'swap' || m.ev === 'allowed') adSeen();
    log(m);
    if (m.ev === 'cover') { showCover(m); return; }
    if (m.ev === 'clean' && cover.end && !cover.dom && !(SIG.gqlUntil && Date.now() < SIG.gqlUntil - 2500)) liftCover('clean');   // the playlist has no ad any more
    if (m.ev === 'swap' || m.ev === 'allowed' || m.ev === 'clean') toHost(m);
}
if (CFG.diag) window.__hallowTwitch.diag = [];

// second signal: the player's own ad label / countdown (it shows them whenever an ad really plays - hidden by the CSS above, but
// still there). While one is on the page the video stays covered and muted, whatever the playlists said. v4: more of their names,
// and a short ad label in the player found by its text; one that stays for more than 3 minutes is ignored for 10 (never a cover for good).
var AD_UI = '[data-a-target="video-ad-label"],[data-a-target="video-ad-countdown"],[data-a-target="video-ad-countdown-container"],[data-a-target*="video-ad"],' +
    '[data-test-selector*="ad-banner"],[data-test-selector="commercial-break-in-progress"],[data-a-target="ad-countdown"],[class*="video-ad__"],[class*="ad-countdown"]';
var AD_TEXT = /^(Ad|Ads|Ad \d+ of \d+|Ad \d+\/\d+|Ad · \d+:\d\d|Advertisement|Commercial break( in progress)?)$/i;
function labelAd() {
    var pl = document.querySelector('[data-a-target="video-player"], .video-player'); if (!pl) return null;
    var w = document.createTreeWalker(pl, NodeFilter.SHOW_TEXT), n, k = 0;
    while ((n = w.nextNode()) && k++ < 400) {
        var s = (n.nodeValue || '').trim(); if (!s || s.length > 32 || !AD_TEXT.test(s)) continue;
        var el = n.parentElement; if (!el || el.closest('[data-a-target="player-controls"] button[aria-label], .chat-shell')) continue;
        return el;
    }
    return null;
}
function domAd() {
    if (!CFG.on || !document.documentElement) return;
    var now = Date.now(), el = now < SIG.domMuteUntil ? null : (document.querySelector(AD_UI) || labelAd());
    if (el && !SIG.domSince) SIG.domSince = now;
    if (el && now - SIG.domSince > 180000) { SIG.domMuteUntil = now + 600000; decide('an ad label stayed 3 minutes - ignored for 10'); el = null; }
    if (!el || allowedHere(null)) {
        SIG.domSince = 0;
        if (cover.dom) { cover.dom = false; cover.end = Math.min(cover.end, Date.now() + 1000); }
        return;
    }
    adSeen();
    var cd = document.querySelector('[data-a-target="video-ad-countdown"],[data-a-target="ad-countdown"],[class*="ad-countdown"]'), m = cd && /(\d+):(\d\d)/.exec(cd.textContent || ''), secs = m ? +m[1] * 60 + +m[2] : 0;
    if (!cover.dom) { cover.dom = true; log({ ev: 'dom-ad' }); SIG.label++; decide('the player shows an ad label (' + ((el.getAttribute('data-a-target') || el.getAttribute('data-test-selector') || (typeof el.className === 'string' ? el.className.split(' ')[0] : '') || 'text').slice(0, 40)) + ') - covered'); }
    if (!cover.end) showCover({ ev: 'cover', ch: CH_NOW(), secs: Math.max(secs, 2), why: 'dom' });
    var until = Date.now() + Math.max(secs * 1000, 1500);
    if (until > cover.end) cover.end = until;
}
if (CFG.on) setInterval(domAd, 500);
var bcOut = null;
try { bcOut = new BroadcastChannel(NONCE); bcOut.onmessage = function (e) { onMsg(e.data); }; } catch (e) { }

// v4: what "Report a Twitch problem" reads (Hallow Browser adds the page's state and a small picture of the player itself). The
// channel names this script saw are replaced by [channel]; no chat, user names or cookies are in here.
window.__hallowTwitch.report = function () {
    var H = window.__hallowTwitch, names = Object.keys(SEEN), ch = CH_NOW(); if (ch) names.push(ch);
    var o = {
        script: CFG.version, on: !!CFG.on, allowedHere: allowedHere(null), events: H.events.slice(-60), decisions: DEC.slice(-100), watch: WD, blank: BL,
        signals: SIG, cover: H.cover(), workers: H.workers || 0, droppedOps: H.droppedOps || 0, resyncs: H.resyncs || 0,
        core: window.__hallowTwCore || null, swappedSince: swappedSince ? Date.now() - swappedSince : 0, sinceAd: SIG.lastAd ? Date.now() - SIG.lastAd : -1
    };
    var s = JSON.stringify(o);
    for (var i = 0; i < names.length; i++) if (names[i]) s = s.split(names[i]).join('[channel]');
    return s;
};

// v5: the settings each core gets, with the Twitch headers known so far
function withHdr() { var o = {}; for (var k in CFG) o[k] = CFG[k]; o.hdr = PAGE_HDR; return o; }
if (CFG.on) {
    core(window, withHdr(), onMsg, pageSub);
    // every classic worker starts with the same prelude (Twitch's player fetches its playlists in one: a blob: worker whose code is
    // importScripts(<amazon-ivs worker>) - read here and run after the prelude). v4: module workers get it too (before a dynamic
    // import of their code), and the page can send the worker a command (the backup stream).
    var prelude = function () {
        return '(function(){var bc;try{bc=new BroadcastChannel(' + JSON.stringify(NONCE) + ')}catch(e){}' +
            'var P=function(m){try{bc&&bc.postMessage(m)}catch(e){}};' +
            'var S=function(f){try{bc&&bc.addEventListener("message",function(e){if(e.data&&e.data.cmd)f(e.data)})}catch(e){}};' +
            '(' + core.toString() + ')(self,' + JSON.stringify(withHdr()) + ',P,S);})();\n';
    };
    var OW = window.Worker;
    var source = function (u, module) {
        var abs = new URL(String(u), location.href).href;
        if (/^(blob|data):/i.test(abs)) {   // read now: the page may revoke a blob address right after creating the worker
            var x = new XMLHttpRequest(); x.open('GET', abs, false); x.send();
            return x.status === 200 || x.status === 0 ? prelude() + x.responseText : null;
        }
        return prelude() + (module ? 'import(' + JSON.stringify(abs) + ');' : 'importScripts(' + JSON.stringify(abs) + ');');
    };
    if (OW) {
        var HW = function (u, o) {
            var nu = u;
            try {
                var s = source(u, !!(o && o.type === 'module'));
                if (s != null) { nu = URL.createObjectURL(new Blob([s], { type: 'text/javascript' })); window.__hallowTwitch.workers = (window.__hallowTwitch.workers || 0) + 1; if (o && o.type === 'module') decide('a module worker was wrapped'); }
            } catch (e) { nu = u; }
            return Reflect.construct(OW, [nu, o], new.target || HW);
        };
        HW.prototype = OW.prototype;
        Object.setPrototypeOf(HW, OW);
        try { Object.defineProperty(HW, 'name', { value: 'Worker' }); } catch (e) { }
        window.Worker = HW;
    }
}
toHost({ ev: 'ready', ch: CH_NOW(), v: CFG.version });