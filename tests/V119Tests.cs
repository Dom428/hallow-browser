// Hallow Browser 1.19 - unit tests: the clean-link rules (thoroughly), pinned-tab order and the session field, the cookie-banner
// rules and script, the new settings' defaults, and Ask Claude's request body and stream reading (no network, no key, no files).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;

namespace HallowBrowser.Tests
{
    public static class V119
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All()
        {
            CleanLinks(); Pins(); Cookies(); Settings(); Ai();
        }

        static void CleanLinks()
        {
            Func<string, string> C = LinkClean.Clean;
            Eq(C("https://example.com/a?utm_source=x&utm_medium=y&utm_campaign=z"), "https://example.com/a", "1.19 clean: utm_* all go (and the ? with them)");
            Eq(C("https://example.com/a?id=5&utm_source=x&page=2"), "https://example.com/a?id=5&page=2", "1.19 clean: functional parameters stay, in their order");
            Eq(C("https://example.com/?fbclid=IwAR123"), "https://example.com/", "1.19 clean: fbclid");
            Eq(C("https://shop.example.com/p?gclid=abc&dclid=d&msclkid=m&gbraid=g&wbraid=w&gclsrc=aw.ds"), "https://shop.example.com/p", "1.19 clean: gclid, dclid, msclkid, gbraid, wbraid, gclsrc");
            Eq(C("https://example.com/x?mc_eid=1&mc_cid=2&igshid=3&yclid=4&_hsenc=5&_hsmi=6&mkt_tok=7&ref_src=twsrc"), "https://example.com/x", "1.19 clean: mc_eid, mc_cid, igshid, yclid, _hsenc, _hsmi, mkt_tok, ref_src");
            Eq(C("https://example.com/x?oly_anon_id=1&oly_enc_id=2&vero_id=3&vero_conv=4&q=games"), "https://example.com/x?q=games", "1.19 clean: oly_* and vero_*");
            Eq(C("https://learn.microsoft.com/a?WT.mc_id=ABC-123&view=net-8"), "https://learn.microsoft.com/a?view=net-8", "1.19 clean: WT.mc_id (any case)");
            Eq(C("https://example.com/a?UTM_Source=x&Id=1"), "https://example.com/a?Id=1", "1.19 clean: names in any case");
            Eq(C("https://example.com/a?utm_source=x#section-2"), "https://example.com/a#section-2", "1.19 clean: the fragment stays");
            Eq(C("https://example.com/a?q=1#x?utm_source=y"), "https://example.com/a?q=1#x?utm_source=y", "1.19 clean: a ? in the fragment isn't the query");
            Eq(C("https://www.youtube.com/watch?v=dQw4w9WgXcQ&si=AbCdEf123"), "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "1.19 clean: si on youtube.com (v stays)");
            Eq(C("https://youtu.be/dQw4w9WgXcQ?si=xyz&t=42"), "https://youtu.be/dQw4w9WgXcQ?t=42", "1.19 clean: si on youtu.be (the time stays)");
            Eq(C("https://open.spotify.com/track/abc?si=123"), "https://open.spotify.com/track/abc", "1.19 clean: si on Spotify");
            Eq(C("https://example.com/search?si=1&q=a"), "https://example.com/search?si=1&q=a", "1.19 clean: si elsewhere is left alone");
            Eq(C("https://notyoutube.com/w?si=1"), "https://notyoutube.com/w?si=1", "1.19 clean: a look-alike host isn't YouTube");
            Eq(C("https://example.com/a?ref=homepage&source=rss"), "https://example.com/a?ref=homepage&source=rss", "1.19 clean: ref and source (often functional) stay");
            Eq(C("https://example.com/a?q=caf%C3%A9&utm_source=x&b=a%26b"), "https://example.com/a?q=caf%C3%A9&b=a%26b", "1.19 clean: the encoding of kept parameters is untouched");
            Eq(C("https://example.com/a?utm%5Fsource=x&k=1"), "https://example.com/a?k=1", "1.19 clean: an encoded name is recognised");
            Eq(C("https://example.com/a?flag&utm_term=t"), "https://example.com/a?flag", "1.19 clean: a parameter without a value stays");
            Eq(C("https://example.com/a?a=1&&b=2"), "https://example.com/a?a=1&&b=2", "1.19 clean: nothing to remove = exactly as it was");
            Eq(C("https://example.com/a?a=1&&utm_source=s&b=2"), "https://example.com/a?a=1&b=2", "1.19 clean: empty pieces tidied once something is removed");
            Eq(C("https://example.com/"), "https://example.com/", "1.19 clean: no query");
            Eq(C("https://example.com/?"), "https://example.com/?", "1.19 clean: an empty query as it was");
            Eq(C("hallow://settings?utm_source=x"), "hallow://settings?utm_source=x", "1.19 clean: only http / https");
            Eq(C("not a url"), "not a url", "1.19 clean: not an address");
            Eq(C(null), null, "1.19 clean: null");
            Eq(C("http://127.0.0.1:8080/p?utm_source=a&x=1&_gl=1*abc&srsltid=Af"), "http://127.0.0.1:8080/p?x=1", "1.19 clean: _gl and srsltid; ports stay");
            Eq(C("https://example.com/p?ttclid=1&twclid=2&li_fat_id=3&epik=4&s_kwcid=5&ef_id=6&rb_clickid=7"), "https://example.com/p", "1.19 clean: TikTok, X, LinkedIn, Pinterest, Adobe click ids");
            Eq(C("https://example.com/p?pk_campaign=a&mtm_source=b&page=3"), "https://example.com/p?page=3", "1.19 clean: Matomo campaign parameters");
            Eq(C("https://example.com/p?__hstc=1&__hssc=2&__hsfp=3&hsCtaTracking=4"), "https://example.com/p", "1.19 clean: HubSpot cookies-in-the-address");
            int n; LinkClean.Clean("https://example.com/?a=1&utm_source=x&fbclid=y", out n); Eq(n, 2, "1.19 clean: counts what it removed");
            Ok(LinkClean.IsTracking("utm_anything", "x.com") && !LinkClean.IsTracking("id", "x.com") && !LinkClean.IsTracking("", "x.com") && !LinkClean.IsTracking("q", "youtube.com"), "1.19 clean: the rule per name");
            Eq(C("https://user:pw@example.com/a?utm_source=x"), "https://user:pw@example.com/a", "1.19 clean: the rest of the address isn't rewritten");
        }

        static void Pins()
        {
            var tabs = new List<string> { "a", "B", "c", "D", "e" };
            Eq(string.Join(",", PinRules.Order(tabs, x => char.IsUpper(x[0]))), "B,D,a,c,e", "1.19 pins: pinned ones first, both kept in their order");
            Eq(string.Join(",", PinRules.Order(new List<string>(), x => true)), "", "1.19 pins: no tabs");
            Ok(PinRules.Width >= 36 && PinRules.Width < TabFit.Min, "1.19 pins: a pinned tab is narrower than the smallest normal tab (" + PinRules.Width + " px)");
            var s = new Session(); s.Tabs.Add(new SessionTab { Url = "https://a.example/", Pinned = true }); s.Tabs.Add(new SessionTab { Url = "https://b.example/" });
            var ser = new DataContractJsonSerializer(typeof(Session));
            using (var ms = new MemoryStream()) { ser.WriteObject(ms, s); ms.Position = 0; var back = (Session)ser.ReadObject(ms); Ok(back.Tabs[0].Pinned && !back.Tabs[1].Pinned, "1.19 pins: kept in the session"); }
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"Tabs\":[{\"Url\":\"https://x.example/\"}],\"Active\":0}"))) { var old = (Session)ser.ReadObject(ms); Ok(!old.Tabs[0].Pinned, "1.19 pins: a 1.18 session has no pinned tabs"); }
            // the strip: pinned tabs take their small room first
            bool over; var w = TabFit.Width(1000 - 2 * (PinRules.Width + 1), 8, 0, out over);
            Ok(!over && w > TabFit.Min && w <= TabFit.Max, "1.19 pins: the others share what's left (" + Math.Round(w, 1) + " px)");
        }

        static void Cookies()
        {
            Ok(CookieBanner.Reject.Length >= 100, "1.19 cookies: reject / necessary-only texts in seven languages (" + CookieBanner.Reject.Length + ")");
            Ok(CookieBanner.Reject.All(x => x == CookieBanner.Normalize(x)), "1.19 cookies: the list is written as normalized");
            Ok(CookieBanner.Reject.Distinct().Count() == CookieBanner.Reject.Length, "1.19 cookies: no duplicates");
            var acceptish = new[] { "accept", "akzept", "acepto", "aceito", "accetto", "accepteer", "agree", "allow", "zustimm", "ok", "got it" };
            Ok(CookieBanner.Reject.All(x => !acceptish.Any(a => x.Split(' ').Any(word => word.StartsWith(a))) || x.Contains("without") || x.Contains("sans") || x.Contains("sin") || x.Contains("sem") || x.Contains("senza") || x == "disagree" || x == "i disagree"),
                "1.19 cookies: no reject text accepts (only \"continue without accepting\" and \"disagree\" kinds)");
            foreach (var t in new[] { "Reject all", "  REJECT   ALL ", "Reject All.", "Alle ablehnen", "Tout refuser", "Rechazar todo", "Rejeitar tudo", "Rifiuta tutto", "Alles weigeren", "Only necessary", "Nur notwendige Cookies", "Solo necesarias", "Continuer sans accepter", "Weiger alles" })
                Ok(CookieBanner.IsReject(t), "1.19 cookies: \"" + t.Trim() + "\" is a reject button");
            foreach (var t in new[] { "Accept all", "Accept", "Allow all cookies", "I agree", "Alle akzeptieren", "Tout accepter", "Aceptar", "Aceitar", "Accetta tutto", "Alles accepteren", "OK", "Got it", "Manage options", "Settings", "Learn more", "Reject all and subscribe" })
                Ok(!CookieBanner.IsReject(t), "1.19 cookies: \"" + t + "\" is never clicked");
            Ok(CookieBanner.LooksAccept("Accept all cookies") && CookieBanner.LooksAccept("Alle akzeptieren") && !CookieBanner.LooksAccept("Reject all") && !CookieBanner.LooksAccept("Disagree") && !CookieBanner.LooksAccept("Continuer sans accepter") && !CookieBanner.LooksAccept("Nicht einverstanden"), "1.19 cookies: the accept check (for consent managers' own ids)");
            var js = CookieBanner.Script(SignIn.Hosts.Concat(new[] { "Off.Example.com." }));
            Ok(SignIn.Hosts.All(h => js.Contains("\"" + h + "\"")) && js.Contains("\"off.example.com\""), "1.19 cookies: the script skips the sign-in hosts and the sites you turned it off for");
            Ok(js.Contains("onetrust-reject-all-handler") && js.Contains("CybotCookiebotDialogBodyButtonDecline") && js.Contains("didomi-notice-disagree-button") && js.Contains("qc-cmp2") && js.Contains("uc-deny-all-button") && js.Contains("truste-consent-required") && js.Contains("fc-cta-do-not-consent"),
                "1.19 cookies: OneTrust, Cookiebot, Didomi, Quantcast, Usercentrics, TrustArc, Google Funding Choices");
            Ok(js.Contains("LIMIT=" + CookieBanner.GiveUpMs) && CookieBanner.GiveUpMs <= 10000 && !js.Contains("__EXEMPT__") && !js.Contains("__REJECT__"), "1.19 cookies: gives up after " + CookieBanner.GiveUpMs / 1000 + " s; every placeholder filled");
            Ok(js.Contains("input[type=password]") && js.Contains("(login|session)"), "1.19 cookies: never in a box with a password field; GitHub's sign-in paths");
            Ok(!SiteRules.Empty(new SiteRule { Host = "x.com", Consent = "off" }) && SiteRules.Empty(new SiteRule { Host = "x.com" }), "1.19 cookies: a site turned off is a site rule of its own");
        }

        static void Settings()
        {
            var s = new BrowserSettings();
            Ok(s.CookieDecline && !s.CleanCopy && !s.AiOn && s.AiModel == null, "1.19 settings: cookie banners declined by default; clean copy and Ask Claude off");
            var ser = new DataContractJsonSerializer(typeof(BrowserSettings));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"Search\":\"google\"}"))) { var old = (BrowserSettings)ser.ReadObject(ms); old.Fix(); Ok(old.CookieDecline && !old.CleanCopy && !old.AiOn, "1.19 settings: a 1.18 settings file gets the defaults"); }
            s.CookieDecline = false; s.CleanCopy = true; s.AiOn = true; s.AiModel = "claude-sonnet-5-5";
            using (var ms = new MemoryStream()) { ser.WriteObject(ms, s); ms.Position = 0; var back = (BrowserSettings)ser.ReadObject(ms); back.Fix(); Ok(!back.CookieDecline && back.CleanCopy && back.AiOn && back.AiModel == "claude-sonnet-5-5", "1.19 settings: kept"); }
            s.AiModel = "gpt-something"; s.Fix(); Ok(s.AiModel == null, "1.19 settings: an unknown model goes back to the default");
            using (var ms = new MemoryStream()) { ser.WriteObject(ms, new BrowserSettings()); var txt = Encoding.UTF8.GetString(ms.ToArray()); Ok(!txt.ToLowerInvariant().Contains("sk-ant") && !txt.Contains("ApiKey"), "1.19 settings: no API key field in settings.json"); }
            Ok(!SyncData.SettingKeys.Any(k => k.StartsWith("Ai")), "1.19 settings: Ask Claude's settings don't sync");
        }

        static void Ai()
        {
            Eq(AiApi.DefaultModel, "claude-opus-5-5", "1.19 ai: Claude Opus 5.5 by default");
            Eq(AiApi.ApiVersion, "2026-10-21", "1.19 ai: the API version header");
            Ok(AiApi.Models.Select(m => m[0]).SequenceEqual(new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-2" }), "1.19 ai: the model picker");
            Ok(AiApi.Endpoint.StartsWith("https://api.anthropic.com/v1/messages") || BrowserSettings.TestDir != null, "1.19 ai: the real endpoint outside tests");
            Ok(AiKey.LooksValid("sk-ant-api03-" + new string('x', 40)) && !AiKey.LooksValid("sk-proj-abc") && !AiKey.LooksValid("sk-ant- has space here") && !AiKey.LooksValid(null), "1.19 ai: what a key looks like");
            var turns = new List<AiTurn> {
                new AiTurn { Role = "user", Text = "first" },
                new AiTurn { Role = "assistant", Text = "answer", Blocks = new List<Dictionary<string, object>> { new Dictionary<string, object> { { "type", "thinking" }, { "thinking", "" }, { "signature", "SIG1" } }, new Dictionary<string, object> { { "type", "text" }, { "text", "answer" } } } },
                new AiTurn { Role = "user", Text = "broken", Failed = true }, new AiTurn { Role = "assistant", Text = "", Failed = true, Note = "error" },
                new AiTurn { Role = "user", Text = "about this page", PageTitle = "T", PageUrl = "https://x.example/", PageText = "PAGE-TEXT-123" } };
            var body = AiApi.Body("claude-opus-5-5", turns);
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
            var msgs = (System.Collections.ArrayList)d["messages"];
            Ok((string)d["model"] == "claude-opus-5-5" && (bool)d["stream"] && Convert.ToInt32(d["max_tokens"]) == AiApi.MaxTokens && (string)d["fallbacks"] == "default" && !d.ContainsKey("temperature") && !d.ContainsKey("thinking"), "1.19 ai: body - model, streaming, max_tokens, server-side fallbacks; no temperature, thinking left to the model");
            Ok(msgs.Count == 3, "1.19 ai: failed turns aren't sent again (" + msgs.Count + " messages)");
            Ok(body.Contains("SIG1") && body.Contains("\"thinking\""), "1.19 ai: an answer's thinking block goes back unchanged (with its signature)");
            Ok(body.Contains("PAGE-TEXT-123") && body.Contains("page title=") && body.IndexOf("PAGE-TEXT-123") < body.IndexOf("about this page"), "1.19 ai: the page (when added) goes before the question it came with");
            var body2 = AiApi.Body("claude-haiku-5-5", new List<AiTurn> { new AiTurn { Role = "user", Text = "hi" } });
            Ok(!body2.Contains("fallbacks") && !body2.Contains("page title=") && !AiApi.UsesFallbacks("claude-haiku-5-5") && AiApi.UsesFallbacks("claude-sonnet-5-5"), "1.19 ai: Haiku has no server-side fallback; no page unless one was added");
            Ok(AiApi.PageBlock("A \"quoted\" title", "https://x/", "t", true).Contains("cut to the first 50,000") && !AiApi.PageBlock("A \"quoted\" title", "https://x/", "t", false).Contains("\"quoted\""), "1.19 ai: the page block (title quoted safely, says when it was cut)");
            // the stream
            var r = new AiReply(); var said = new StringBuilder();
            Action<string, string> ev = (e, data) => { var t = AiApi.Process(e, data, r); if (t != null) said.Append(t); };
            ev("message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[]}}");
            ev("content_block_start", "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"\"}}");
            ev("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"signature_delta\",\"signature\":\"abc\"}}");
            ev("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":0}");
            ev("content_block_start", "{\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}");
            ev("ping", "{\"type\":\"ping\"}");
            ev("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}");
            ev("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"text_delta\",\"text\":\" there\"}}");
            ev("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":5}}");
            ev("message_stop", "{\"type\":\"message_stop\"}");
            Ok(said.ToString() == "Hello there" && r.Text == "Hello there" && r.StopReason == "end_turn" && r.Deltas == 2, "1.19 ai: text deltas stream in order; the stop reason");
            var clean = r.Clean();
            Ok(clean.Count == 2 && (string)clean[0]["type"] == "thinking" && (string)clean[0]["signature"] == "abc" && (string)clean[1]["text"] == "Hello there", "1.19 ai: the answer's blocks as they go back");
            var re = new AiReply(); AiApi.Process("error", "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}", re);
            Ok(re.Error != null && re.Error.Contains("busy"), "1.19 ai: a stream error is said plainly (" + re.Error + ")");
            Ok(AiApi.Friendly(401, null, null).Contains("API key") && AiApi.Friendly(429, null, null).Contains("Too many") && AiApi.Friendly(500, "api_error", "boom").Contains("500"), "1.19 ai: HTTP errors in words");
            var rr = new AiReply(); AiApi.Process("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"refusal\"}}", rr); Eq(rr.StopReason, "refusal", "1.19 ai: a refusal is seen");
            Eq(AiApi.Process("x", "not json", new AiReply()), null, "1.19 ai: a broken event is skipped");
        }
    }
}
