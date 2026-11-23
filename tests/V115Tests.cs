// Hallow Browser 1.15 - unit tests: passkeys through Hallow Keys. The page script (https only, frames too, the original kept for "the
// browser's own", never Windows' window in tests), what a page's message may carry, the request Keys gets (the engine's address,
// never the page's; the frame's tab as topUrl), the frame permission check, the setting. No network, no Keys.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class V115
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        public static void All() { ScriptTests(); MessageTests(); SettingTests(); }

        static void ScriptTests()
        {
            var live = KeysLink.PasskeyScript(true); var test = KeysLink.PasskeyScript(false);
            Ok(live.Contains("var NATIVE=true;") && test.Contains("var NATIVE=false;") && !live.Contains("__NATIVE__"), "passkeys 1.15: the page script calls the original WebView2 function for \"the browser's own\" (tests: a marker, never Windows' window)");
            Ok(live.Contains("location.protocol!=='https:'") && live.Contains("window.isSecureContext"), "passkeys 1.15: the page script does nothing on http pages");
            Ok(live.Contains("origCreate=proto.create,origGet=proto.get") && live.Contains("if(!options||!options.publicKey)return origGet.apply(self,args);") && live.Contains("if(!options||!options.publicKey)return origCreate.apply(self,args);"), "passkeys 1.15: only publicKey requests are taken; passwords / federated / other calls go to the original untouched");
            Ok(live.Contains("post('hallow:pk\\t'") && live.Contains("var post=wv.postMessage.bind(wv)"), "passkeys 1.15: requests go to the browser (its own message channel, kept before the page's scripts run)");
            Ok(!live.Contains("location.href") && !live.Contains("location.origin") && !live.Contains("document.referrer"), "passkeys 1.15: the page script never sends an address (the browser takes it from the engine)");
            Ok(live.Contains("setTimeout(function(){if(!p.acked&&pending[rid])") && live.Contains("done({action:'fallback'})"), "passkeys 1.15: no answer from the browser in 1.5 s (frames inside frames) -> the browser's own");
            Ok(live.Contains("isUserVerifyingPlatformAuthenticatorAvailable") && live.Contains("isConditionalMediationAvailable") && live.Contains("if(typeof ocap==='function')"), "passkeys 1.15: the capability checks say Keys is there only while it runs; getClientCapabilities only where WebView2 has it");
            Ok(live.Contains("signal.addEventListener('abort'") && live.Contains("op:'abort'"), "passkeys 1.15: an AbortSignal closes the prompt");
            Ok(!live.Contains("\"") , "passkeys 1.15: the page script has no double quotes (safe as a C# verbatim string)");
        }

        static Dictionary<string, object> D(string json) { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
        static void MessageTests()
        {
            var q = D("{\"rpId\":\"rp.test\",\"challenge\":\"" + new string('A', 43) + "\",\"allow\":[\"x\",5,null,\"" + new string('B', 1401) + "\"],\"url\":\"https://bank.test/\",\"origin\":\"https://bank.test\",\"topUrl\":\"https://bank.test/\",\"client\":\"evil\",\"userName\":\"" + new string('u', 300) + "\"}");
            var r = new PasskeyRequest { Op = "get", Url = "https://evil.test/login", Origin = "https://evil.test", RpId = KeysLink.Str(q, "rpId", 253), Req = q };
            var m = KeysLink.PasskeyMessage("passkey-get", r, "id1");
            Ok((string)m["url"] == "https://evil.test/login" && !m.ContainsKey("origin") && !m.ContainsKey("topUrl"), "passkeys 1.15: Keys gets the engine's address - a page's own url / origin / topUrl in the message are ignored");
            Ok((string)m["client"] == "hallow-browser", "passkeys 1.15: the request says it's from Hallow Browser (Keys 2.13 checks the pipe's process really is)");
            Ok(((List<string>)m["allow"]).SequenceEqual(new[] { "x" }), "passkeys 1.15: lists keep strings only, each at most 1400 characters");
            var c = KeysLink.PasskeyMessage("passkey-create", new PasskeyRequest { Op = "create", Url = "https://rp.test/", RpId = "rp.test", Req = q });
            Ok((string)c["userName"] == "" && (string)c["challenge"] == new string('A', 43), "passkeys 1.15: a value over its cap is dropped, not cut (a 300-character user name -> empty)");
            var fr = KeysLink.PasskeyMessage("passkey-list", new PasskeyRequest { Op = "get", Url = "https://frame.test/x", RpId = "frame.test", TopUrl = "https://top.test/page", Req = q });
            Ok((string)fr["topUrl"] == "https://top.test/page" && (string)fr["url"] == "https://frame.test/x", "passkeys 1.15: a frame's request carries its own address and the tab's (topUrl - from the browser too)");
            Ok(KeysLink.Str(D("{\"a\":5}"), "a", 10) == "" && KeysLink.Str(D("{\"a\":\"xy\"}"), "a", 1) == "" && KeysLink.Strs(D("{\"a\":\"xy\"}"), "a", 5).Count == 0, "passkeys 1.15: numbers / long strings / a string where a list belongs -> nothing");
            Ok(KeysLink.WantsOther(D("{\"attachment\":\"cross-platform\"}")) && KeysLink.WantsOther(D("{\"hints\":[\"security-key\"]}")) && KeysLink.WantsOther(D("{\"hints\":[\"hybrid\",\"client-device\"]}")) && !KeysLink.WantsOther(D("{\"hints\":[\"client-device\"]}")) && !KeysLink.WantsOther(D("{}")), "passkeys 1.15: a site asking for a security key or a phone gets \"Use another device\" first (like the extension)");
            var pol = KeysLink.FramePolicyScript("https://frame.test", "publickey-credentials-get");
            Ok(pol.Contains("featurePolicy.allowsFeature(f)") && pol.Contains("\"https://frame.test\",\"publickey-credentials-get\"") && pol.Contains("return 'none'") && pol.Contains("if(!ok)return 'no'"), "passkeys 1.15: a cross-origin frame needs the permission from its parent page - every iframe of that origin must have it, none = refused");
            Ok(KeysLink.PasskeyMessage("passkey-list", new PasskeyRequest { Op = "create", Url = "https://a.test/", RpId = "a.test", Req = q }).ContainsKey("allow") && ((List<string>)KeysLink.PasskeyMessage("passkey-list", new PasskeyRequest { Op = "create", Url = "https://a.test/", RpId = "a.test", Req = q })["allow"]).Count == 0, "passkeys 1.15: create's list check sends no allow list");
        }

        static void SettingTests()
        {
            Ok(new BrowserSettings().KeysPasskeys, "passkeys 1.15: \"Use Hallow Keys for passkeys\" is on by default");
            var s = (BrowserSettings)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(BrowserSettings)).ReadObject(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")));
            Ok(s.KeysPasskeys, "passkeys 1.15: an older settings file gets it on too");
            Ok(!SyncData.SettingKeys.Contains("KeysPasskeys"), "passkeys 1.15: the setting stays on this PC (Keys is per PC)");
        }
    }
}
