// Hallow Browser 1.18 - unit tests: the clean address, the toolbar's layout rules (default, show / hide, move, what is saved), the
// tab strip's width rule, the downloads ring's share, an extension's icon file, and the QR encoder (read back: format bits, unmasking,
// de-interleaving, Reed-Solomon syndromes, the bytes). No network.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace HallowBrowser.Tests
{
    public static class V118
    {
        static void Ok(bool c, string what) { Run.Ok(c, what); }
        static void Eq(object a, object b, string what) { Run.Eq(a, b, what); }

        public static void All()
        {
            // ---- the clean address
            Eq(AddressText.Clean("https://www.example.com/a/b?c=1#d"), "example.com/a/b?c=1#d", "1.18 address: https:// and www. go, the path stays");
            Eq(AddressText.Clean("https://example.com/"), "example.com", "1.18 address: a bare site is just its name");
            Eq(AddressText.Clean("http://127.0.0.1:8080/x"), "127.0.0.1:8080/x", "1.18 address: http:// goes too, the port stays");
            Eq(AddressText.Clean("https://www.co/"), "www.co", "1.18 address: \"www\" that is the name itself stays");
            Eq(AddressText.Clean("https://user:secret@example.com/p"), "example.com/p", "1.18 address: a user name / password in the address is never shown");
            Eq(AddressText.Clean("https://WWW.Example.com/"), "example.com", "1.18 address: host case");
            Eq(AddressText.Clean("http://[::1]:3000/"), "[::1]:3000", "1.18 address: IPv6");
            Eq(AddressText.Clean(Omni.NewTabUrl), "", "1.18 address: the new tab page stays empty");
            Eq(AddressText.Clean(Omni.SettingsUrl), Omni.Display(Omni.SettingsUrl), "1.18 address: the browser's own pages as they are");
            Eq(AddressText.Clean("chrome-extension://abc/popup.html"), "chrome-extension://abc/popup.html", "1.18 address: other schemes as they are");
            Ok(AddressText.NotSecure("http://example.com/") && !AddressText.NotSecure("https://example.com/") && !AddressText.NotSecure(Omni.NewTabUrl), "1.18 address: \"Not secure\" only for http pages");
            Eq(Omni.Display("https://x.com/"), "https://x.com/", "1.18 address: the full address (when you edit) is unchanged");

            // ---- the toolbar's layout
            var d = Toolbar118.Layout(null);
            Eq(string.Join(",", d), "shield,extensions,downloads,game,settings,profile", "1.18 toolbar: the default (Shield, Extensions, Downloads, Game mode, Settings, Profile; Home off)");
            Eq(string.Join(",", Toolbar118.Layout(new List<string>())), string.Join(",", d), "1.18 toolbar: an empty list is the default");
            Eq(string.Join(",", Toolbar118.Layout(new List<string> { "home", "bogus", "home", "shield" })), "home,shield", "1.18 toolbar: unknown and repeated IDs are dropped");
            Eq(Toolbar118.Layout(new List<string> { Toolbar118.None }).Count, 0, "1.18 toolbar: every button hidden is kept as hidden");
            Ok(Toolbar118.ToSave(Toolbar118.Default.ToList()) == null, "1.18 toolbar: the default is saved as null");
            Eq(string.Join(",", Toolbar118.ToSave(new List<string>())), Toolbar118.None, "1.18 toolbar: nothing shown is saved as [\"none\"]");
            var l = Toolbar118.Show(d, "home", true); l = Toolbar118.Move(l, "home", -1); l = Toolbar118.Show(l, "downloads", false);
            Eq(string.Join(",", l), "shield,extensions,game,settings,home,profile", "1.18 toolbar: show, move, hide");
            Eq(string.Join(",", Toolbar118.Move(d, "shield", -1)), string.Join(",", d), "1.18 toolbar: the first can't move further left");
            Eq(string.Join(",", Toolbar118.Hidden(d)), "home,bookmarks,sidebar,screenshot,split,reader", "1.18 toolbar: the hidden ones, in the usual order");
            Ok(Toolbar118.All.Length == 12 && Toolbar118.All.All(x => Toolbar118.Name(x) != x && Icons.Has(Toolbar118.Icon(x))), "1.18 toolbar: twelve buttons, each with a name and an icon");
            Ok(Icons.Get("share-nodes") != null && Icons.Get("picture-in-picture") != null, "1.18: the two glyphs drawn here (share, pop out)");
            var s = new BrowserSettings();
            Ok(s.Toolbar == null && s.PinnedExtensions == null, "1.18 settings: no toolbar layout and no pins by default");
            var ser = new DataContractJsonSerializer(typeof(BrowserSettings));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("{\"Search\":\"google\"}"))) { var old = (BrowserSettings)ser.ReadObject(ms); Ok(old.Toolbar == null && old.PinnedExtensions == null && Toolbar118.Layout(old.Toolbar).SequenceEqual(Toolbar118.Default), "1.18 settings: a 1.17 settings file gets the default toolbar"); }
            s.Toolbar = new List<string> { "home", "shield" }; s.PinnedExtensions = new List<string> { "abcdefghijklmnopabcdefghijklmnop" };
            using (var ms = new MemoryStream()) { ser.WriteObject(ms, s); ms.Position = 0; var back = (BrowserSettings)ser.ReadObject(ms); Ok(back.Toolbar.SequenceEqual(s.Toolbar) && back.PinnedExtensions.SequenceEqual(s.PinnedExtensions), "1.18 settings: the layout and the pins are kept"); }

            // ---- the tab strip's widths
            bool over;
            Eq(TabFit.Width(1000, 4, 0, out over), 230.0, "1.18 strip: few tabs stop at the maximum width"); Ok(!over, "1.18 strip: ... no overflow");
            var w = TabFit.Width(1000, 10, 0, out over); Ok(Math.Abs(w - 99) < 0.01 && !over, "1.18 strip: more tabs share the room evenly (" + w + ")");
            w = TabFit.Width(1000, 30, 0, out over); Ok(w == TabFit.Min && over, "1.18 strip: below the minimum the strip overflows (arrows)");
            w = TabFit.Width(1000, 10, 200, out over); Ok(Math.Abs(w - 79) < 0.01, "1.18 strip: group labels take their room first");

            // ---- the ring's share
            Eq(AddressText.Progress(new List<DownloadItem>()), -1.0, "1.18 ring: nothing running, no ring");
            Eq(AddressText.Progress(new List<DownloadItem> { new DownloadItem { State = "done", Got = 5, Total = 5 } }), -1.0, "1.18 ring: finished downloads don't count");
            Eq(AddressText.Progress(new List<DownloadItem> { new DownloadItem { State = "running", Got = 25, Total = 100 }, new DownloadItem { State = "paused", Got = 75, Total = 100 } }), 0.5, "1.18 ring: the overall share of what runs");
            Eq(AddressText.Progress(new List<DownloadItem> { new DownloadItem { State = "running", Got = 25, Total = 0 } }), 0.0, "1.18 ring: an unknown size shows an empty ring");

            // ---- an extension's icon file
            var dir = Path.Combine(BrowserSettings.TestDir, "ext-icon-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), "{\"manifest_version\":3,\"name\":\"X\",\"version\":\"1\",\"action\":{\"default_icon\":{\"16\":\"a16.png\",\"32\":\"a32.png\",\"128\":\"a128.png\"}},\"icons\":{\"48\":\"b48.png\"}}");
            foreach (var f in new[] { "a16.png", "a32.png", "a128.png", "b48.png" }) File.WriteAllBytes(Path.Combine(dir, f), new byte[] { 1 });
            Eq(Path.GetFileName(ExtStore.IconFile(dir)), "a32.png", "1.18 pins: the toolbar icon nearest 32 px");
            File.Delete(Path.Combine(dir, "a32.png")); File.Delete(Path.Combine(dir, "a128.png")); File.Delete(Path.Combine(dir, "a16.png"));
            Eq(Path.GetFileName(ExtStore.IconFile(dir)), "b48.png", "1.18 pins: else the extension's icons");
            File.WriteAllText(Path.Combine(dir, "manifest.json"), "{\"manifest_version\":3,\"name\":\"X\",\"version\":\"1\",\"icons\":{\"48\":\"../../outside.png\"}}");
            Ok(ExtStore.IconFile(dir) == null, "1.18 pins: never a file outside the extension's folder");

            // ---- the QR encoder
            Eq(Qr.FormatBits(0), 0x5412, "1.18 QR: format bits, level M mask 0 (101010000010010)");
            Eq(Qr.FormatBits(1), 0x5125, "1.18 QR: format bits, level M mask 1 (101000100100101)");
            Eq(Qr.DataCodewords(1), 16, "1.18 QR: version 1-M holds 16 data codewords"); Eq(Qr.DataCodewords(10), 216, "1.18 QR: version 10-M holds 216");
            Eq(string.Join(",", Qr.AlignmentPositions(7)), "6,22,38", "1.18 QR: alignment patterns of version 7");
            Eq(string.Join(",", Qr.AlignmentPositions(32)), "6,34,60,86,112,138", "1.18 QR: ... of version 32 (the odd one)");
            Eq(string.Join(",", Qr.AlignmentPositions(36)), "6,24,50,76,102,128,154", "1.18 QR: ... of version 36");
            foreach (var text in new[] { "https://example.com/", "https://news.example.com/patch-notes?season=7&lang=en#top", "http://127.0.0.1:51234/page-one?x=1#top", "https://example.com/" + new string('a', 300), "https://example.com/?q=" + new string('z', 1000), "https://ünicode.example/ä?ö=ü" })
            {
                var q = Qr.Encode(text); string why;
                var back = q == null ? null : Qr.Read(q, out why);
                Ok(q != null && back == text && q.Size == q.Version * 4 + 17, "1.18 QR: " + Encoding.UTF8.GetByteCount(text) + " bytes -> version " + (q == null ? 0 : q.Version) + " (mask " + (q == null ? -1 : q.Mask) + "), read back the same");
            }
            Ok(Qr.Encode("https://example.com/" + new string('x', 1300)) == null, "1.18 QR: too long for a phone screen = no code (copy the link instead)");
            var q1 = Qr.Encode("https://example.com/");
            Ok(q1.Version == 2 && q1.Get(0, 0) && q1.Get(6, 6) && !q1.Get(7, 7) && q1.Get(q1.Size - 1, 0) && q1.Get(0, q1.Size - 1) && q1.Get(8, q1.Size - 8), "1.18 QR: finder patterns in three corners and the dark module");
        }
    }
}
