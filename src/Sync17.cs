// Hallow Browser 1.7 - sync carries two more things:
//  - Hallow Shield's My filters. They live inside Shield (the extension's own storage, chrome.storage.local "myFilters"); since 1.4
//    the browser loads its own copy of Shield, and the browser reads and writes My filters through a hidden page of Shield's own
//    origin (the same way as the family relay) - Shield's background notices the change and recompiles them, exactly as when you
//    edit them in Shield's settings. One record ("shf"), the newer change wins. If Shield can't be reached, this PC sends nothing
//    for it (never an empty list over yours).
//  - History, opt-in and ENCRYPTED ONLY: it needs the sync password (the folder's copy is then AES-256-GCM). The newest N pages
//    (250 / 1,000 / 2,500 / 5,000 - the size cap), one record per page (address, title, last visit, visits). Pages from other PCs
//    are merged in (most visits, newest visit); clearing history on one PC doesn't clear the others. Without a password, history
//    records are never written to the folder - also not other PCs' ones.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace HallowBrowser
{
    public static partial class SyncHistory
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        public const int MaxTitle = 200, MaxUrl = 2048;
        public static string IdOf(string url) { using (var sha = SHA256.Create()) return "hist:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url ?? "")), 0, 12).Replace("-", "").ToLowerInvariant(); }
        // the newest max pages as records
        public static List<SyncRecord> Records(IEnumerable<HistoryEntry> items, int max)
        {
            var res = new List<SyncRecord>();
            foreach (var e in items.Where(x => x != null && Omni.IsWeb(x.Url) && x.Url.Length <= MaxUrl).OrderByDescending(x => x.Last ?? "", StringComparer.Ordinal).Take(max))
            {
                var t = e.Title == null ? null : e.Title.Length > MaxTitle ? e.Title.Substring(0, MaxTitle) : e.Title;
                res.Add(new SyncRecord { Id = IdOf(e.Url), Kind = "hist", Data = js.Serialize(new SortedDictionary<string, object>(StringComparer.Ordinal) { { "l", e.Last }, { "t", t }, { "u", e.Url }, { "v", Math.Max(1, e.Visits) } }) });
            }
            return res;
        }
        // two versions of one page -> one (the same rule as HistoryStore.MergeSynced, so both PCs end with this exact record)
        public static string Combine(SyncRecord x, SyncRecord y)
        {
            var a = From(x); var b = From(y); if (a == null || b == null || a.Url != b.Url) return null;
            int c = string.CompareOrdinal(a.Last ?? "", b.Last ?? ""); var newer = c >= 0 ? a : b; var older = c >= 0 ? b : a;
            string t;
            if (c == 0) t = string.IsNullOrWhiteSpace(a.Title) ? b.Title : string.IsNullOrWhiteSpace(b.Title) ? a.Title : string.CompareOrdinal(a.Title, b.Title) >= 0 ? a.Title : b.Title;
            else t = !string.IsNullOrWhiteSpace(newer.Title) ? newer.Title : older.Title;
            return Records(new[] { new HistoryEntry { Url = a.Url, Title = t, Last = newer.Last, Visits = Math.Max(a.Visits, b.Visits) } }, 1)[0].Data;
        }
        public static HistoryEntry From(SyncRecord r)
        {
            try
            {
                var d = js.Deserialize<Dictionary<string, object>>(r.Data); object u, t, l, v;
                d.TryGetValue("u", out u); d.TryGetValue("t", out t); d.TryGetValue("l", out l); d.TryGetValue("v", out v);
                var url = u as string; if (!Omni.IsWeb(url)) return null;
                return new HistoryEntry { Url = url, Title = t as string, Last = l as string, Visits = v == null ? 1 : Convert.ToInt32(v) };
            }
            catch { return null; }
        }
    }

    public partial class SyncInput
    {
        public string ShieldFilters;             // 1.7: Shield's My filters as read just now (null = couldn't read: nothing sent for it)
        public HistoryStore History; public int HistoryMax;   // 1.7: the history store, when history sync is on (encrypted) - else null
        public string ShieldFiltersIn;           // 1.7: My filters that came in (to be written into Shield)
    }

    public partial class BrowserWindow
    {
        // ---- Shield's My filters, through a hidden page of Shield's own origin (never shown)
        public string ShieldFiltersCache;   // the last My filters read from Shield (for sync on close)
        public static string ShieldFiltersNote;
        const string GetFiltersScript = "chrome.storage.local.get({myFilters:''}).then(function(o){return typeof o.myFilters==='string'?o.myFilters:'';})";
        static string SetFiltersScript(string text) { return "chrome.storage.local.set({myFilters:" + new JavaScriptSerializer().Serialize(text ?? "") + "}).then(function(){return chrome.storage.local.get({myFilters:''});}).then(function(o){return o.myFilters;})"; }
        bool shieldPageBusy;
        async Task<string> ShieldPageEval(string script)
        {
            if (ShieldState != "extension" || ShieldId == null || snap || Private) return null;
            for (int i = 0; i < 40 && shieldPageBusy; i++) await Task.Delay(100);
            if (shieldPageBusy) return null;
            shieldPageBusy = true; WebView2 v = null;
            try
            {
                v = new WebView2 { Visibility = Visibility.Hidden, Width = 1, Height = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                content.Children.Add(v);
                await Engine.Ensure(v);
                await Panels.NavigateAndWait(v.CoreWebView2, ShieldPage("family.js"));
                return await Panels.Eval(v.CoreWebView2, script);
            }
            finally { shieldPageBusy = false; if (v != null) { content.Children.Remove(v); try { v.Dispose(); } catch { } } }
        }
        public Task<string> ShieldPageEvalPublic(string script) { return ShieldPageEval(script); }
        public async Task<string> ReadShieldFilters()
        {
            try
            {
                var r = await ShieldPageEval(GetFiltersScript); if (r == null) { ShieldFiltersNote = "Shield isn't loaded"; return null; }
                var text = new JavaScriptSerializer().Deserialize<string>(r) ?? "";
                ShieldFiltersCache = text; ShieldFiltersNote = "read " + DateTime.Now.ToString("HH:mm"); return text;
            }
            catch (Exception e) { ShieldFiltersNote = "couldn't read: " + e.Message; return null; }
        }
        public async Task<bool> WriteShieldFilters(string text)
        {
            try
            {
                var r = await ShieldPageEval(SetFiltersScript(text)); if (r == null) { ShieldFiltersNote = "Shield isn't loaded"; return false; }
                var back = new JavaScriptSerializer().Deserialize<string>(r) ?? "";
                var ok = back == (text ?? ""); if (ok) ShieldFiltersCache = back;
                ShieldFiltersNote = ok ? "written " + DateTime.Now.ToString("HH:mm") : "Shield kept something else"; return ok;
            }
            catch (Exception e) { ShieldFiltersNote = "couldn't write: " + e.Message; return false; }
        }
        bool HistorySyncing { get { return S.SyncHistory && S.SyncEncrypt && !Private && SyncCrypto.LoadPassword() != null; } }

        // Settings > Sync: the 1.7 part
        public UIElement[] SyncSettings17()
        {
            var items = new List<UIElement>();
            items.Add(new Border { Height = 10 });
            items.Add(Ui.Switch("Hallow Shield's My filters", "Your own block / hide rules from Shield's settings, on every PC. The browser reads and writes them through Shield itself (its built-in copy).", S.SyncShieldFilters, v => { S.SyncShieldFilters = v; S.Save(); SyncSoon(2); }));
            var histNote = Ui.T("", 11.5, false, "AmberText", 4);
            Action refresh = () =>
            {
                var pw = SyncCrypto.LoadPassword() != null;
                histNote.Text = !S.SyncHistory ? "" : !S.SyncEncrypt || !pw ? "History waits for encryption: turn on \"Encrypt the sync file\" and set the sync password - it's never synced unencrypted." : "History syncs encrypted (the newest " + S.SyncHistoryMax.ToString("N0") + " pages).";
                histNote.SetResourceReference(TextBlock.ForegroundProperty, S.SyncHistory && S.SyncEncrypt && pw ? "GreenText" : "AmberText");
                histNote.Visibility = histNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            items.Add(Ui.Switch("History (encrypted only)", "Off by default. Only with the sync password: the pages you visited go to the folder AES-encrypted, never readable there. Clearing history here doesn't clear your other PCs.", S.SyncHistory, v => { S.SyncHistory = v; S.Save(); refresh(); SyncSoon(2); }));
            items.Add(Ui.Muted("History sync: at most the newest", 12, 6));
            items.Add(Ui.Chips(BrowserSettings.SyncHistoryChoices.Select(n => new KeyValuePair<int, string>(n, n.ToString("N0") + " pages")).ToList(), S.SyncHistoryMax, v => { S.SyncHistoryMax = v; S.Save(); refresh(); SyncSoon(2); }));
            items.Add(histNote); refresh();
            return items.ToArray();
        }
    }
}
