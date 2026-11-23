// Hallow Browser 1.15 - passkeys through Hallow Keys. When a website asks to make or use a passkey (WebAuthn: navigator.credentials
// .create / .get with "publicKey"), Hallow Browser hands it to Hallow Keys (2.13+) when Keys runs and "Use Hallow Keys for passkeys" is
// on; otherwise the site gets WebView2's own passkey window (Windows Hello, your phone, a security key) exactly as before.
// Why not Keys' browser extension (WebView2 can load extensions - Shield is one): WebView2 leaves out native messaging, and the Keys
// extension's only way to reach the app is its native host, so its passkey prompt could never get an answer. Instead the browser does
// what the extension + its native host do, with the parts a page can't touch kept on the browser's side:
//  - a small script of the browser's own (added to every https document before the page's scripts, also in frames) wraps
//    navigator.credentials.create / get and the PublicKeyCredential capability checks, and posts the request to the browser
//    ("hallow:pk"). Everything else (passwords, federated logins, non-publicKey calls) goes to the original untouched;
//  - the browser takes the page's address from the engine (WebMessageReceived's Source for the tab or the frame), never from the
//    message, and only for https; the rpId, challenge and names are passed as plain strings, length-capped;
//  - frames: a frame of the tab's own origin, or of another origin only when the page gave it the publickey-credentials-get / -create
//    permission (the iframe's "allow", read from the parent page by the browser), as browsers do; else NotAllowedError. Frames inside
//    frames are left to WebView2's own handling;
//  - Hallow Keys checks everything again (the same rules as for the extension: https, rpId = the page's domain or listed in the rpId's
//    /.well-known/webauthn, nothing while locked, Windows Hello first when it's on) and checks that the pipe's other end really is
//    Hallow Browser;
//  - Hallow Keys' prompt is drawn by the browser under the address bar (WPF - the page can't see, cover or click it): save / pick a
//    passkey / "Use another device or the browser's own" (the original WebView2 call) / Cancel; locked: "Unlock Hallow Keys" and it waits.
//    A click counts only after the prompt has been up half a second;
//  - the signed answer goes back only to the same tab or frame, and only while it's still on the origin that asked;
//  - passkey autofill (mediation "conditional"): the request waits, the key in the address bar lists the site's passkeys, picking one
//    signs in. Private windows: never (WebView2's own window there).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserSettings
    {
        [DataMember] public bool KeysPasskeys = true;   // 1.15: passkey requests go to Hallow Keys while it runs (else WebView2's own window)
        void Defaults115() { KeysPasskeys = true; }
    }

    public partial class Tab
    {
        public PasskeyRequest PasskeyCond;   // 1.15: a waiting passkey-autofill request (mediation "conditional") and its passkeys
    }

    public class PasskeyRequest
    {
        public string Rid, Op, Url, Origin, TopUrl, RpId; public bool Conditional, Stale;
        public Dictionary<string, object> Req; public Tab Tab; public CoreWebView2 Core; public CoreWebView2Frame Frame;
        public List<Dictionary<string, object>> Items = new List<Dictionary<string, object>>(); public bool Locked;
    }
    public class PasskeyRow { public string Hk, Icon, Title, Sub, Msg; public bool Main, Sep; public Action Go; }
    public class PasskeyPrompt
    {
        public PasskeyRequest R; public string Title, Sub; public List<PasskeyRow> Rows = new List<PasskeyRow>();
        public Popup Pop; public bool Busy, Closed; public DateTime ShownAt; public DispatcherTimer Timer;
    }

    public static partial class KeysLink
    {
        // ---- 1.15: tests point the browser at a stand-in that runs Hallow Keys' own code on its test pipe (never the real Keys)
        static string TestPipe { get { return BrowserSettings.TestDir != null ? Environment.GetEnvironmentVariable("HALLOW_BROWSER_KEYS_PIPE") : null; } }
        public static bool Installed
        {
            get
            {
                if (BrowserSettings.TestDir != null) return Environment.GetEnvironmentVariable("HALLOW_BROWSER_KEYS_INSTALLED") == "1";
                try { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Hallow Keys", "HallowKeys.exe")); } catch { return false; }
            }
        }
        public const string Client = "hallow-browser";   // Keys 2.13 checks that the pipe's other end really is HallowBrowser.exe

        // ---- the page part (every https document, frames too, before the page's own scripts). __NATIVE__ is false only in tests: there
        // "the browser's own" never opens Windows' passkey window - it fails with a marker instead.
        public const string PasskeyScriptTemplate = @"(function(){'use strict';
if(location.protocol!=='https:'||!window.isSecureContext||typeof navigator.credentials==='undefined'||typeof CredentialsContainer==='undefined')return;
if(window.__hallowPk)return;try{Object.defineProperty(window,'__hallowPk',{value:1});}catch(e){return;}
var wv=window.chrome&&window.chrome.webview;if(!wv||typeof wv.postMessage!=='function'||typeof wv.addEventListener!=='function')return;
var post=wv.postMessage.bind(wv),listen=wv.addEventListener.bind(wv),JS=JSON.stringify;
var proto=CredentialsContainer.prototype,origCreate=proto.create,origGet=proto.get,PKC=window.PublicKeyCredential;
var NATIVE=__NATIVE__;
function own(kind,self,args){if(!NATIVE)return Promise.reject(new DOMException('test: the browser own passkey window','NotAllowedError'));return (kind==='create'?origCreate:origGet).apply(self,args);}
var enc=function(buf){var b=new Uint8Array(buf),s='';for(var i=0;i<b.length;i++)s+=String.fromCharCode(b[i]);return btoa(s).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');};
var dec=function(s){s=String(s||'').replace(/-/g,'+').replace(/_/g,'/');while(s.length%4)s+='=';var b=atob(s),u=new Uint8Array(b.length);for(var i=0;i<b.length;i++)u[i]=b.charCodeAt(i);return u.buffer;};
var bytes=function(x){if(x instanceof ArrayBuffer)return x;if(ArrayBuffer.isView(x))return x.buffer.slice(x.byteOffset,x.byteOffset+x.byteLength);throw new TypeError('Expected a BufferSource');};
var copy=function(ab){return ab.slice(0);};
var pending={},seq=0;
listen('message',function(e){var d=e.data;if(!d||d.__hpk!=='res'||typeof d.rid!=='string')return;var p=pending[d.rid];if(!p)return;if(d.ack){p.acked=true;return;}delete pending[d.rid];p.done(d);});
function newRid(){return 'hp'+(++seq)+'-'+Math.random().toString(36).slice(2);}
function call(op,req,rid){return new Promise(function(done){rid=rid||newRid();var p={done:done,acked:false};pending[rid]=p;
try{post('hallow:pk\t'+JS({rid:rid,op:op,req:req||{}}));}catch(e){delete pending[rid];done({action:'fallback'});return;}
setTimeout(function(){if(!p.acked&&pending[rid]){delete pending[rid];done({action:'fallback'});}},1500);});}
function abortErr(signal){return signal&&signal.reason!==undefined?signal.reason:new DOMException('The operation was aborted.','AbortError');}
function withAbort(signal,rid,promise){if(!signal)return promise;return new Promise(function(res,rej){
var onAbort=function(){try{post('hallow:pk\t'+JS({rid:newRid(),op:'abort',req:{rid:rid}}));}catch(e){}delete pending[rid];rej(abortErr(signal));};
signal.addEventListener('abort',onAbort,{once:true});
promise.then(function(v){signal.removeEventListener('abort',onAbort);res(v);},function(er){signal.removeEventListener('abort',onAbort);rej(er);});});}
function define(o,props){for(var k in props)Object.defineProperty(o,k,{value:props[k],enumerable:true,configurable:true});return o;}
function credential(c,kind,ext){var rawId=dec(c.id);var R=kind==='create'?window.AuthenticatorAttestationResponse:window.AuthenticatorAssertionResponse;
var resp=Object.create(R?R.prototype:Object.prototype);var cd=dec(c.clientDataJSON),ad=dec(c.authenticatorData);
if(kind==='create'){var att=dec(c.attestationObject),pub=dec(c.publicKey),tr=(c.transports||[]).slice();
define(resp,{clientDataJSON:cd,attestationObject:att,getAuthenticatorData:function(){return copy(ad);},getPublicKey:function(){return copy(pub);},
getPublicKeyAlgorithm:function(){return c.publicKeyAlgorithm;},getTransports:function(){return tr.slice();},
toJSON:function(){return {clientDataJSON:c.clientDataJSON,attestationObject:c.attestationObject,authenticatorData:c.authenticatorData,publicKey:c.publicKey,publicKeyAlgorithm:c.publicKeyAlgorithm,transports:tr.slice()};}});}
else{define(resp,{clientDataJSON:cd,authenticatorData:ad,signature:dec(c.signature),userHandle:c.userHandle?dec(c.userHandle):null,
toJSON:function(){return {clientDataJSON:c.clientDataJSON,authenticatorData:c.authenticatorData,signature:c.signature,userHandle:c.userHandle||undefined};}});}
var cred=Object.create(PKC?PKC.prototype:Object.prototype);var x=ext||{};
define(cred,{id:c.id,rawId:rawId,type:'public-key',response:resp,authenticatorAttachment:'platform',getClientExtensionResults:function(){return JSON.parse(JS(x));},
toJSON:function(){return {id:c.id,rawId:c.id,type:'public-key',response:resp.toJSON(),authenticatorAttachment:'platform',clientExtensionResults:JSON.parse(JS(x))};}});
return cred;}
function fail(r){if(r.name==='TypeError')return new TypeError(r.msg||'Invalid request');return new DOMException(r.msg||'The operation either timed out or was not allowed.',r.name||'NotAllowedError');}
function hintsOf(pk){return Array.isArray(pk.hints)?pk.hints.filter(function(h){return typeof h==='string';}).slice(0,5).map(function(h){return h.slice(0,20);}):[];}
function rpIdOf(pk,kind){var v=kind==='create'?(pk.rp&&pk.rp.id):pk.rpId;return v===undefined||v===null?location.hostname:String(v);}
proto.create=function create(options){var self=this,args=arguments;
if(!options||!options.publicKey)return origCreate.apply(self,args);
var signal=options.signal;if(signal&&signal.aborted)return Promise.reject(abortErr(signal));
var req,pk=options.publicKey;try{req={rpId:rpIdOf(pk,'create'),rpName:pk.rp&&pk.rp.name?String(pk.rp.name):'',challenge:enc(bytes(pk.challenge)),
userId:enc(bytes(pk.user.id)),userName:String(pk.user.name||''),displayName:String(pk.user.displayName||''),
algs:(pk.pubKeyCredParams||[]).filter(function(p){return p&&p.type==='public-key';}).map(function(p){return String(p.alg);}),
exclude:(pk.excludeCredentials||[]).filter(function(c){return c&&c.type==='public-key';}).map(function(c){return enc(bytes(c.id));}),
attachment:pk.authenticatorSelection&&typeof pk.authenticatorSelection.authenticatorAttachment==='string'?pk.authenticatorSelection.authenticatorAttachment.slice(0,20):'',
hints:hintsOf(pk)};}catch(e){return origCreate.apply(self,args);}
var credProps=!!(pk.extensions&&pk.extensions.credProps);var rid=newRid();
return withAbort(signal,rid,call('create',req,rid).then(function(r){if(r.action==='fallback')return own('create',self,args);
if(r.action==='cred')return credential(r.cred,'create',credProps?{credProps:{rk:true}}:{});throw fail(r);}));};
proto.get=function get(options){var self=this,args=arguments;
if(!options||!options.publicKey)return origGet.apply(self,args);
var signal=options.signal;if(signal&&signal.aborted)return Promise.reject(abortErr(signal));
var req,pk=options.publicKey;try{req={rpId:rpIdOf(pk,'get'),challenge:enc(bytes(pk.challenge)),
allow:(pk.allowCredentials||[]).filter(function(c){return c&&c.type==='public-key';}).map(function(c){return enc(bytes(c.id));}),
conditional:options.mediation==='conditional',hints:hintsOf(pk)};}catch(e){return origGet.apply(self,args);}
var rid=newRid();
return withAbort(signal,rid,call('get',req,rid).then(function(r){if(r.action==='fallback')return own('get',self,args);
if(r.action==='cred')return credential(r.cred,'get',{});throw fail(r);}));};
if(PKC){var ask=function(){return call('status',{}).then(function(r){return r&&r.running?true:null;});};
var oc=PKC.isConditionalMediationAvailable,ou=PKC.isUserVerifyingPlatformAuthenticatorAvailable,ocap=PKC.getClientCapabilities;
try{PKC.isConditionalMediationAvailable=function isConditionalMediationAvailable(){return ask().then(function(v){return v||(oc?oc.call(PKC):false);});};}catch(e){}
try{PKC.isUserVerifyingPlatformAuthenticatorAvailable=function isUserVerifyingPlatformAuthenticatorAvailable(){return ask().then(function(v){return v||(ou?ou.call(PKC):false);});};}catch(e){}
if(typeof ocap==='function')try{PKC.getClientCapabilities=function getClientCapabilities(){return Promise.all([ocap.call(PKC).catch(function(){return {};}),ask()]).then(function(a){var c=Object.assign({},a[0]);
if(a[1]){c.passkeyPlatformAuthenticator=true;c.userVerifyingPlatformAuthenticator=true;c.conditionalGet=true;c.relatedOrigins=true;}return c;});};}catch(e){}}
})();";
        public static string PasskeyScript(bool native) { return PasskeyScriptTemplate.Replace("__NATIVE__", native ? "true" : "false"); }

        // ---- what a page's message may carry (unit-tested): plain strings, capped like the extension's background script does
        public static string Str(Dictionary<string, object> d, string k, int max)
        {
            object o; return d != null && d.TryGetValue(k, out o) && o is string && ((string)o).Length <= max ? (string)o : "";
        }
        public static List<string> Strs(Dictionary<string, object> d, string k, int max)
        {
            object o; if (d == null || !d.TryGetValue(k, out o) || o == null || o is string) return new List<string>();
            var e = o as IEnumerable; if (e == null) return new List<string>();
            return e.Cast<object>().OfType<string>().Where(x => x.Length <= 1400).Take(max).ToList();
        }
        public static bool Flag(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        // the request Keys gets: the address is the one the engine reported for the document that asked (never the page's words)
        public static Dictionary<string, object> PasskeyMessage(string type, PasskeyRequest r, string id = null)
        {
            var q = r.Req ?? new Dictionary<string, object>();
            var m = new Dictionary<string, object> { { "type", type }, { "url", r.Url }, { "rpId", r.RpId ?? "" }, { "client", Client } };
            if (r.TopUrl != null) m["topUrl"] = r.TopUrl;
            if (type == "passkey-list") m["allow"] = r.Op == "get" ? Strs(q, "allow", 64) : new List<string>();
            if (type == "passkey-get") { m["id"] = id ?? ""; m["challenge"] = Str(q, "challenge", 1400); m["allow"] = Strs(q, "allow", 64); }
            if (type == "passkey-create")
            {
                m["rpName"] = Str(q, "rpName", 256); m["challenge"] = Str(q, "challenge", 1400); m["userId"] = Str(q, "userId", 100);
                m["userName"] = Str(q, "userName", 256); m["displayName"] = Str(q, "displayName", 256); m["algs"] = Strs(q, "algs", 20); m["exclude"] = Strs(q, "exclude", 64);
            }
            return m;
        }
        // a passkey request may wait for Windows Hello in Keys (2 minutes, like Keys' own host)
        public static Dictionary<string, object> AskPasskey(Dictionary<string, object> msg) { return Ask(msg, ((string)msg["type"]) == "passkey-list" ? 5000 : 130000); }

        // the permission a cross-origin frame needs (unit-tested): the parent page's own answer for each <iframe> of that origin
        public static string FramePolicyScript(string origin, string feature)
        {
            return "(function(o,f){try{var l=[].slice.call(document.querySelectorAll('iframe'));" +
                "var m=l.filter(function(x){try{return new URL(x.getAttribute('src')||'about:blank',document.baseURI).origin===o;}catch(e){return false;}});" +
                "if(!m.length)return 'none';for(var i=0;i<m.length;i++){var x=m[i],ok=false;" +
                "if(x.featurePolicy&&typeof x.featurePolicy.allowsFeature==='function')ok=x.featurePolicy.allowsFeature(f);" +
                "else{var dp=document.featurePolicy;var po=!dp||dp.allowsFeature(f,o);var a=(x.getAttribute('allow')||'').split(';').map(function(s){return s.trim().split(/\\s+/);}).filter(function(p){return p[0]===f;});" +
                "ok=po&&a.length>0&&(a[0].length===1||a[0].slice(1).some(function(v){return v==='*'||v===\"'src'\"||v===o;}));}" +
                "if(!ok)return 'no';}return 'yes';}catch(e){return 'error';}})(" + new JavaScriptSerializer().Serialize(origin) + "," + new JavaScriptSerializer().Serialize(feature) + ")";
        }
        // the site asked for a security key or a phone rather than a passkey on this PC (the extension's rule)
        public static bool WantsOther(Dictionary<string, object> q)
        {
            if (Str(q, "attachment", 20) == "cross-platform") return true;
            var h = Strs(q, "hints", 5); return h.Count > 0 && (h[0] == "security-key" || h[0] == "hybrid");
        }
    }

    public partial class BrowserWindow
    {
        public PasskeyPrompt PkPrompt; public string LastPasskey;
        public static int PasskeyMessages, PasskeyRefused, PasskeyFallbacks, PasskeyCreds, FramesHooked;   // tests
        static readonly JavaScriptSerializer pkJs = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };

        // every tab of a normal window: the page script, and each frame's messages
        void Hook115(CoreWebView2 c)
        {
            if (Private) return;   // private windows keep WebView2's own passkey window
            var _ = c.AddScriptToExecuteOnDocumentCreatedAsync(KeysLink.PasskeyScript(BrowserSettings.TestDir == null));
            c.FrameCreated += (a, e) => HookPasskeyFrame(c, e.Frame);
        }
        public readonly Dictionary<CoreWebView2, List<CoreWebView2Frame>> PasskeyFrames = new Dictionary<CoreWebView2, List<CoreWebView2Frame>>();   // the tab's frames (tests reach into them)
        void HookPasskeyFrame(CoreWebView2 c, CoreWebView2Frame f)
        {
            FramesHooked++;
            List<CoreWebView2Frame> fl; if (!PasskeyFrames.TryGetValue(c, out fl)) PasskeyFrames[c] = fl = new List<CoreWebView2Frame>(); fl.Add(f);
            f.Destroyed += (a, e) => fl.Remove(f);
            f.WebMessageReceived += (a, e) =>
            {
                string m = null; try { m = e.TryGetWebMessageAsString(); } catch { }
                if (m == null || !m.StartsWith("hallow:pk\t")) return;
                Tab t; if (!owner.TryGetValue(c, out t)) return;
                OnPasskeyMessage(t, c, f, e.Source, m.Substring(10));
            };
            f.NavigationStarting += (a, e) => PasskeyGone(null, f);
            f.Destroyed += (a, e) => PasskeyGone(null, f);
        }
        // the tab or a frame went to another page: its waiting requests are void (nothing is ever posted to the next page)
        void PasskeyGone(Tab t, CoreWebView2Frame f)
        {
            var p = PkPrompt;
            if (p != null && ((t != null && p.R.Tab == t) || (f != null && p.R.Frame == f))) { p.R.Stale = true; ClosePasskeyPrompt(); }
            foreach (var x in Tabs) if (x.PasskeyCond != null && ((t != null && x == t) || (f != null && x.PasskeyCond.Frame == f))) { x.PasskeyCond.Stale = true; x.PasskeyCond = null; if (x == Active) UpdateBar(); }
        }

        void PasskeyReply(PasskeyRequest r, Dictionary<string, object> o)
        {
            o["__hpk"] = "res"; o["rid"] = r.Rid;
            var json = pkJs.Serialize(o);
            try
            {
                if (r.Frame != null) { if (r.Frame.IsDestroyed() == 0) r.Frame.PostWebMessageAsJson(json); }
                else if (r.Core != null) r.Core.PostWebMessageAsJson(json);
            }
            catch { }
        }
        static Dictionary<string, object> PkErr(string name, string msg) { return new Dictionary<string, object> { { "action", "error" }, { "name", name }, { "msg", msg } }; }
        // still the same page that asked? (the tab's origin, and for a frame: not navigated or gone)
        bool PasskeyLive(PasskeyRequest r)
        {
            if (r.Stale || !Tabs.Contains(r.Tab) || r.Tab.Core != r.Core) return false;
            string top; try { top = r.Core.Source; } catch { return false; }
            if (r.Frame == null) return Omni.Origin(top) == r.Origin;
            try { if (r.Frame.IsDestroyed() != 0) return false; } catch { return false; }
            return Omni.Origin(top) == Omni.Origin(r.TopUrl);
        }
        void PasskeyFallback(PasskeyRequest r, string why) { PasskeyFallbacks++; LastPasskey = "fallback: " + why; PasskeyReply(r, new Dictionary<string, object> { { "action", "fallback" } }); }
        void PasskeyFail(PasskeyRequest r, string name, string msg) { PasskeyRefused++; LastPasskey = "refused: " + name + " " + msg; PasskeyReply(r, PkErr(name, msg)); }
        void PasskeyCred(PasskeyRequest r, Dictionary<string, object> c)
        {
            if (!PasskeyLive(r)) { LastPasskey = "dropped: the page changed"; return; }
            c.Remove("ok"); PasskeyCreds++; LastPasskey = "signed: " + r.Op;
            PasskeyReply(r, new Dictionary<string, object> { { "action", "cred" }, { "cred", c } });
        }

        // a "hallow:pk" message: source = the address the engine reports for the document that sent it (the tab or one frame)
        async void OnPasskeyMessage(Tab t, CoreWebView2 c, CoreWebView2Frame f, string source, string json)
        {
            if (t == null || c == null) return;
            Dictionary<string, object> d; try { d = pkJs.Deserialize<Dictionary<string, object>>(json); } catch { return; }
            if (d == null) return;
            var rid = KeysLink.Str(d, "rid", 80); if (rid.Length == 0) return;
            object rq; d.TryGetValue("req", out rq);
            var r = new PasskeyRequest { Rid = rid, Op = KeysLink.Str(d, "op", 20), Tab = t, Core = c, Frame = f, Url = source, Origin = Omni.Origin(source), Req = rq as Dictionary<string, object> ?? new Dictionary<string, object>() };
            PasskeyReply(r, new Dictionary<string, object> { { "ack", true } });
            PasskeyMessages++;
            if (r.Op == "abort")
            {
                var target = KeysLink.Str(r.Req, "rid", 80);
                if (PkPrompt != null && PkPrompt.R.Rid == target && PkPrompt.R.Tab == t) { PkPrompt.R.Stale = true; ClosePasskeyPrompt(); }
                if (t.PasskeyCond != null && t.PasskeyCond.Rid == target) { t.PasskeyCond = null; if (t == Active) UpdateBar(); }
                return;
            }
            bool on = S.KeysPasskeys && !Private;
            if (r.Op == "status") { var run = on && await Task.Run(() => KeysLink.Running()); PasskeyReply(r, new Dictionary<string, object> { { "action", "status" }, { "running", run } }); return; }
            if (r.Op != "create" && r.Op != "get") { PasskeyFail(r, "NotSupportedError", "Unknown request"); return; }
            if (r.Origin == null || !r.Origin.StartsWith("https://")) { PasskeyFail(r, "SecurityError", "Passkeys only work on secure (https) pages"); return; }
            if (!on) { PasskeyFallback(r, "the setting is off"); return; }
            if (f != null)
            {
                string top; try { top = c.Source; } catch { return; }
                r.TopUrl = top;
                var feature = r.Op == "create" ? "publickey-credentials-create" : "publickey-credentials-get";
                string pol; try { pol = await c.ExecuteScriptAsync(KeysLink.FramePolicyScript(r.Origin, feature)); } catch { pol = null; }
                if (pol != "\"yes\"") { PasskeyFail(r, "NotAllowedError", "The '" + feature + "' feature is not enabled in this document. Permissions Policy may be used to delegate Web Authentication capabilities to cross-origin child frames."); return; }
            }
            r.RpId = KeysLink.Str(r.Req, "rpId", 253);
            r.Conditional = r.Op == "get" && KeysLink.Flag(r.Req, "conditional");
            if (r.Op == "create") { var algs = KeysLink.Strs(r.Req, "algs", 20); if (algs.Count > 0 && !algs.Contains("-7")) { PasskeyFallback(r, "Keys makes ES256 passkeys only"); return; } }
            if (!KeysLink.Running()) { PasskeyFallback(r, "Hallow Keys isn't running"); return; }
            var list = await Task.Run(() => KeysLink.AskPasskey(KeysLink.PasskeyMessage("passkey-list", r)));
            if (list == null) { PasskeyFallback(r, "Hallow Keys didn't answer"); return; }
            object running; if (list.TryGetValue("running", out running) && running is bool && !(bool)running) { PasskeyFallback(r, "Hallow Keys isn't running"); return; }
            if (list.ContainsKey("error")) { PasskeyFail(r, KeysLink.Str(list, "error", 40), KeysLink.Str(list, "msg", 300)); return; }
            if (!PasskeyLive(r)) return;
            if (!KeysLink.Flag(list, "ok") && !KeysLink.Flag(list, "locked")) { PasskeyFail(r, "NotAllowedError", KeysLink.Str(list, "msg", 300)); return; }
            r.Locked = KeysLink.Flag(list, "locked"); SetItems(r, list);
            if (r.Conditional)
            {
                if (t.PasskeyCond != null && t.PasskeyCond != r) PasskeyFail(t.PasskeyCond, "AbortError", "A new request replaced this one");
                t.PasskeyCond = r; LastPasskey = "waiting for a pick (autofill): " + r.Items.Count + " passkey(s)";
                if (t == Active) UpdateBar();
                return;
            }
            ShowPasskeyPrompt(r);
        }
        static void SetItems(PasskeyRequest r, Dictionary<string, object> list)
        {
            r.Items = new List<Dictionary<string, object>>(); object it;
            if (list.TryGetValue("items", out it) && it is IEnumerable) foreach (var o in (IEnumerable)it) { var x = o as Dictionary<string, object>; if (x != null) r.Items.Add(x); }
            var rp = KeysLink.Str(list, "rpId", 253); if (rp.Length > 0) r.RpId = rp;
        }

        // ---------------------------------------------------------------- Hallow Keys' prompt, drawn by the browser
        void ShowPasskeyPrompt(PasskeyRequest r)
        {
            if (PkPrompt != null && !PkPrompt.Closed) { var old = PkPrompt; ClosePasskeyPrompt(); if (!old.R.Stale) PasskeyFail(old.R, "NotAllowedError", "Another passkey request replaced this one"); }
            var p = new PasskeyPrompt { R = r }; PkPrompt = p;
            if (r.Locked) PasskeyLocked(p); else PasskeyReady(p);
        }
        string PasskeyWhere(PasskeyRequest r)
        {
            var here = Omni.HostOf(r.Url) ?? r.RpId;
            return r.Frame != null && !Omni.SameOrigin(r.Url, r.TopUrl) ? here + " (in a frame on " + Omni.HostOf(r.TopUrl) + ")" : here;
        }
        PasskeyRow FallbackRow(PasskeyPrompt p, bool main)
        {
            return new PasskeyRow { Hk = "fallback", Main = main, Icon = "mobile-screen", Title = "Use another device or the browser's own", Sub = "Your phone (scan the QR code Windows shows), a security key or Windows Hello", Go = () => { ClosePasskeyPrompt(); PasskeyFallback(p.R, "you chose the browser's own"); } };
        }
        PasskeyRow CancelRow(PasskeyPrompt p) { return new PasskeyRow { Hk = "cancel", Icon = "xmark", Title = "Cancel", Go = () => { ClosePasskeyPrompt(); PasskeyFail(p.R, "NotAllowedError", "The operation either timed out or was not allowed."); } }; }
        void PasskeyReady(PasskeyPrompt p)
        {
            var r = p.R; var other = KeysLink.WantsOther(r.Req); var rows = new List<PasskeyRow>();
            if (other) { rows.Add(new PasskeyRow { Msg = "This site asked for a security key or your phone.", Hk = "asked-other" }); rows.Add(FallbackRow(p, true)); rows.Add(new PasskeyRow { Sep = true }); }
            if (r.Op == "create")
            {
                var q = r.Req; var who = KeysLink.Str(q, "userName", 256); if (who.Length == 0) who = KeysLink.Str(q, "displayName", 256); if (who.Length == 0) who = "this account";
                rows.Add(new PasskeyRow { Hk = "save", Main = !other, Icon = "key", Title = "Save passkey in Hallow Keys", Sub = who, Go = () => PasskeyUse(p, null) });
                rows.Add(new PasskeyRow { Sep = true }); if (!other) rows.Add(FallbackRow(p, false)); rows.Add(CancelRow(p));
                RenderPasskey(p, "Save a passkey in Hallow Keys?", "For " + who + " on " + r.RpId + " - asked by " + PasskeyWhere(r) + ". It stays in your vault and syncs with it.", rows);
                return;
            }
            for (int i = 0; i < r.Items.Count; i++)
            {
                var it = r.Items[i]; var id = KeysLink.Str(it, "id", 64); var user = KeysLink.Str(it, "userName", 256); var disp = KeysLink.Str(it, "displayName", 256); var title = KeysLink.Str(it, "title", 256);
                rows.Add(new PasskeyRow { Hk = "pick-" + i, Main = i == 0 && !other, Icon = "key", Title = user.Length > 0 ? user : disp.Length > 0 ? disp : title, Sub = title + (disp.Length > 0 && disp != user ? " - " + disp : ""), Go = () => PasskeyUse(p, id) });
            }
            if (r.Items.Count == 0) rows.Add(new PasskeyRow { Msg = "No passkeys for " + r.RpId + " in Hallow Keys.", Hk = "none" });
            rows.Add(new PasskeyRow { Sep = true }); if (!other) rows.Add(FallbackRow(p, false)); rows.Add(CancelRow(p));
            RenderPasskey(p, "Sign in with a passkey", "Hallow Keys - " + r.RpId + (Omni.HostOf(r.Url) != r.RpId ? ", asked by " + PasskeyWhere(r) : r.Frame != null ? " (in a frame on " + Omni.HostOf(r.TopUrl) + ")" : ""), rows);
        }
        // locked: say so, offer to open Hallow Keys, look again every 1.5 s for 3 minutes
        void PasskeyLocked(PasskeyPrompt p)
        {
            RenderPasskey(p, "Hallow Keys is locked", "Unlock it to use your passkeys.", new List<PasskeyRow> {
                new PasskeyRow { Hk = "unlock", Main = true, Icon = "lock-open", Title = "Unlock Hallow Keys", Sub = "Opens Hallow Keys on this PC", Go = () => { Task.Run(() => KeysLink.Ask(new Dictionary<string, object> { { "type", "open" } }, 3000)); } },
                new PasskeyRow { Msg = "Waiting for Hallow Keys to be unlocked...", Hk = "waiting" }, new PasskeyRow { Sep = true }, FallbackRow(p, false), CancelRow(p) });
            int n = 0; bool asking = false;
            p.Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            p.Timer.Tick += async (a, b) =>
            {
                if (p.Closed || ++n > 120) { p.Timer.Stop(); return; }
                if (asking) return; asking = true;
                var r = await Task.Run(() => KeysLink.AskPasskey(KeysLink.PasskeyMessage("passkey-list", p.R))); asking = false;
                if (p.Closed || r == null) return;
                if (r.ContainsKey("error")) { p.Timer.Stop(); ClosePasskeyPrompt(); PasskeyFail(p.R, KeysLink.Str(r, "error", 40), KeysLink.Str(r, "msg", 300)); return; }
                if (KeysLink.Flag(r, "ok")) { p.Timer.Stop(); SetItems(p.R, r); p.R.Locked = false; PasskeyReady(p); }
            };
            p.Timer.Start();
        }
        // a click on save / a passkey: Keys makes or signs (it may ask Windows Hello first)
        async void PasskeyUse(PasskeyPrompt p, string id)
        {
            if (p.Busy || p.Closed) return; p.Busy = true;
            var r = p.R; var create = r.Op == "create";
            RenderPasskey(p, create ? "Saving the passkey..." : "Signing in...", "Hallow Keys is " + (create ? "making a new key for " : "signing for ") + r.RpId + ".", new List<PasskeyRow>());
            var c = await Task.Run(() => KeysLink.AskPasskey(KeysLink.PasskeyMessage(create ? "passkey-create" : "passkey-get", r, id)));
            ClosePasskeyPrompt();
            if (c != null && KeysLink.Flag(c, "ok")) { PasskeyCred(r, c); Toast(create ? "Passkey saved in Hallow Keys for " + r.RpId + "." : "Signed in with a passkey from Hallow Keys.", 4); }
            else PasskeyFail(r, c == null ? "NotAllowedError" : KeysLink.Str(c, "error", 40).Length > 0 ? KeysLink.Str(c, "error", 40) : "NotAllowedError", c == null ? "Hallow Keys didn't answer" : KeysLink.Str(c, "msg", 300));
        }
        void RenderPasskey(PasskeyPrompt p, string title, string sub, List<PasskeyRow> rows)
        {
            p.Title = title; p.Sub = sub; p.Rows = rows; p.ShownAt = DateTime.Now;
            var sp = new StackPanel { Margin = new Thickness(4) };
            var ic = Ui.Icon("key", 16, "AccentBrush"); ic.Margin = new Thickness(0, 2, 10, 0); ic.VerticalAlignment = VerticalAlignment.Top;
            var head = new StackPanel(); head.Children.Add(Ui.T(title, 13.5, true, "Text", 2)); if (!string.IsNullOrEmpty(sub)) { var s = Ui.Muted(sub, 11.5, 6); s.TextWrapping = TextWrapping.Wrap; s.MaxWidth = 330; head.Children.Add(s); }
            sp.Children.Add(Ui.Row(ic, head));
            foreach (var row in rows)
            {
                if (row.Sep) { var line = new Border { Height = 1, Margin = new Thickness(4, 5, 4, 5) }; line.SetResourceReference(Border.BackgroundProperty, "Line"); sp.Children.Add(line); continue; }
                if (row.Msg != null) { var m = Ui.T(row.Msg, 12, false, "AmberText", 0); m.TextWrapping = TextWrapping.Wrap; m.Margin = new Thickness(8, 4, 8, 4); sp.Children.Add(m); continue; }
                var x = row;
                var g = new StackPanel { Orientation = Orientation.Horizontal };
                var i2 = Ui.Icon(x.Icon ?? "key", 13, x.Main ? "AccentBtnText" : "Lilac"); i2.Margin = new Thickness(0, 2, 10, 0); i2.VerticalAlignment = VerticalAlignment.Top; g.Children.Add(i2);
                var tx = new StackPanel(); tx.Children.Add(Ui.T(x.Title, 12.5, true, x.Main ? "AccentBtnText" : "Text", 0)); if (!string.IsNullOrEmpty(x.Sub)) { var sm = Ui.T(x.Sub, 11, false, x.Main ? "AccentBtnText" : "Muted", 0); sm.TextWrapping = TextWrapping.Wrap; sm.MaxWidth = 300; tx.Children.Add(sm); }
                g.Children.Add(tx);
                var b = new Button { Content = g, Style = (Style)FindResource(x.Main ? "Accent" : "MenuRow"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(10, 7, 10, 7) };
                b.Click += (a, e) => PasskeyClick(p, x.Hk);
                sp.Children.Add(b);
            }
            var note = Ui.Muted("Hallow Browser's own window - the page can't see or click it. The key stays in Hallow Keys.", 10.5, 0); note.Margin = new Thickness(8, 6, 8, 2); note.TextWrapping = TextWrapping.Wrap; sp.Children.Add(note);
            var fl = Ui.Flyout(sp, 380); fl.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            if (p.Pop == null) p.Pop = new Popup { PlacementTarget = addressBox, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = true, VerticalOffset = 6 };
            p.Pop.Child = fl; p.Pop.HorizontalOffset = Math.Max(0, addressBox.ActualWidth - 390);
            UpdatePasskeyPrompt();
        }
        // a click on a row (also the tests' way in): only after the prompt has been up half a second (no click-through from the page)
        public string PasskeyClick(PasskeyPrompt p, string hk)
        {
            if (p == null || p.Closed || p.Busy) return "closed";
            if ((DateTime.Now - p.ShownAt).TotalMilliseconds < 500) return "too soon";
            var row = p.Rows.FirstOrDefault(x => x.Hk == hk && x.Go != null); if (row == null) return "no such row";
            row.Go(); return "ok";
        }
        void ClosePasskeyPrompt()
        {
            var p = PkPrompt; if (p == null) return;
            p.Closed = true; if (p.Timer != null) p.Timer.Stop(); if (p.Pop != null) p.Pop.IsOpen = false; PkPrompt = null;
        }
        // shown only over the tab that asked, while this window shows (tests: never on screen)
        void UpdatePasskeyPrompt()
        {
            var p = PkPrompt; if (p == null || p.Pop == null) return;
            if (!Tabs.Contains(p.R.Tab)) { var r = p.R; ClosePasskeyPrompt(); r.Stale = true; return; }
            p.Pop.IsOpen = !BrowserSettings.Offscreen && !snap && IsVisible && p.R.Tab == Active && !p.Closed;
        }

        // ---------------------------------------------------------------- passkey autofill: the key in the address bar
        public bool PasskeyKeyShows(Tab t) { return S.KeysPasskeys && t != null && t.PasskeyCond != null && (t.PasskeyCond.Items.Count > 0 || t.PasskeyCond.Locked); }
        public List<UIElement> PasskeyFlyoutRows(Tab t, Func<Popup> pop)
        {
            var res = new List<UIElement>(); var r = t == null ? null : t.PasskeyCond;
            if (r == null || !S.KeysPasskeys) return res;
            var h = Ui.Muted("Passkeys for " + r.RpId, 11, 0); h.Margin = new Thickness(12, 4, 12, 2); res.Add(h);
            if (r.Locked) { var m = Ui.Muted("Hallow Keys is locked. Unlock it, then reload the page.", 12); m.Margin = new Thickness(12, 0, 12, 6); res.Add(m); }
            for (int i = 0; i < r.Items.Count; i++)
            {
                var it = r.Items[i]; var id = KeysLink.Str(it, "id", 64); var user = KeysLink.Str(it, "userName", 256);
                var row = Ui.MenuRow("key", (user.Length > 0 ? user : KeysLink.Str(it, "displayName", 256)) + "  ·  " + KeysLink.Str(it, "title", 256) + " (passkey)", null, null);
                row.Click += (a, b) => { var p = pop(); if (p != null) p.IsOpen = false; var _ = PasskeyCondPick(t, id); };
                res.Add(row);
            }
            return res;
        }
        public async Task<string> PasskeyCondPick(Tab t, string id)
        {
            var r = t == null ? null : t.PasskeyCond; if (r == null) return "nothing waiting";
            var c = await Task.Run(() => KeysLink.AskPasskey(KeysLink.PasskeyMessage("passkey-get", r, id)));
            if (t.PasskeyCond != r) return "replaced";
            if (c != null && KeysLink.Flag(c, "ok")) { t.PasskeyCond = null; if (t == Active) UpdateBar(); PasskeyCred(r, c); Toast("Signed in with a passkey from Hallow Keys.", 4); return "signed"; }
            var msg = c == null ? "Hallow Keys didn't answer" : KeysLink.Str(c, "msg", 300);
            Toast("Not signed in: " + msg + ".", 5); return "no: " + msg;
        }

        // ---------------------------------------------------------------- Settings > Hallow Keys
        public IEnumerable<UIElement> PasskeySettings()
        {
            var installed = KeysLink.Installed; var running = KeysLink.Running();
            yield return Ui.Switch("Use Hallow Keys for passkeys", "When a site asks to save or use a passkey, Hallow Keys' prompt shows under the address bar: save it in your vault, pick one of yours, or use your phone / a security key / Windows Hello instead. Needs Hallow Keys 2.13 or newer, running. Private windows always use Windows' own window.", S.KeysPasskeys, on => { S.KeysPasskeys = on; S.Save(); });
            yield return Ui.Muted(running ? "Hallow Keys is running - passkey requests go to it." : installed ? "Hallow Keys is installed but not running - sites get Windows' own passkey window until you start it." : "Hallow Keys isn't installed on this PC - sites get Windows' own passkey window.", 11.5, 4);
        }
    }
}
