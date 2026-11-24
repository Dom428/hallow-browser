// Hallow Browser 1.16 - the browser's side of "Add from Chrome Web Store": the button on store pages, the add flow (fetch only on
// your click, check, show what it asks for, then add), updates and the hallow://extensions card.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace HallowBrowser
{
    public partial class BrowserWindow
    {
        public static PendingExt Pending; public static string StoreNote; public static int StoreFetches;   // tests read these
        // on chromewebstore.google.com extension pages: one "Add to Hallow Browser" button (the page's own "Add to Chrome" can't work here)
        public const string StoreScript = "(function(){if(window.top!==window||location.hostname!=='chromewebstore.google.com')return;" +
            "function id(){var m=location.pathname.match(/\\/detail\\/(?:[^\\/]+\\/)?([a-p]{32})(?:\\/|$)/);return m?m[1]:null;}" +
            "function put(){var i=id(),b=document.getElementById('hallow-cws-add');if(!i){if(b)b.remove();return;}if(b){b.dataset.id=i;return;}" +
            "b=document.createElement('button');b.id='hallow-cws-add';b.dataset.id=i;b.textContent='Add to Hallow Browser';" +
            "b.style.cssText='position:fixed;right:24px;bottom:24px;z-index:2147483647;padding:12px 20px;border:0;border-radius:22px;background:#7C3AED;color:#fff;font:600 14px Segoe UI,sans-serif;cursor:pointer;box-shadow:0 4px 18px rgba(0,0,0,.35)';" +
            "b.onclick=function(){try{chrome.webview.postMessage('hallow:cws\\t'+b.dataset.id);}catch(x){}};(document.body||document.documentElement).appendChild(b);}" +
            "document.addEventListener('DOMContentLoaded',put);setInterval(put,1000);})();";
        void Hook116(CoreWebView2 c)
        {
            if (Private) return;
            var _ = c.AddScriptToExecuteOnDocumentCreatedAsync(StoreScript);
        }
        // a click on that button: only from the store's own pages (the engine's address of the page, never the page's word)
        bool StoreMessage(string m, string source)
        {
            if (!m.StartsWith("hallow:cws\t")) return false;
            Uri u; if (Private || source == null || !Uri.TryCreate(source, UriKind.Absolute, out u) || u.Host != WebStore.StoreHost) return true;
            var id = m.Substring(11); if (!Crx.IdRx.IsMatch(id)) return true;
            var _ = AddFromStore(id, false);
            return true;
        }

        // fetch -> check -> unpack (not loaded yet) -> the card on hallow://extensions with what it asks for
        public async Task<string> AddFromStore(string id, bool update)
        {
            if (!Crx.IdRx.IsMatch(id ?? "")) return "That isn't a Chrome Web Store link or extension ID.";
            if (id == ShieldId) return "That's Hallow Shield - it's built in already.";
            if (Pending != null && Pending.Crx != null) CancelPending();
            Pending = new PendingExt { Id = id, Update = update }; StoreNote = "Getting it from the Chrome Web Store..."; ShowExtensionsPage();
            StoreFetches++;
            string err = null; Crx.Result c = null; string folder = null; UserExtension info = null; Dictionary<string, object> man = null;
            await Task.Run(() =>
            {
                var bytes = WebStore.Fetch(id, out err); if (bytes == null) return;
                c = Crx.Parse(bytes, id); if (c.Error != null) { err = "Not added: " + c.Error + "."; c = null; return; }
                try { man = WebStore.Manifest(c.Zip); var raw = WebStore.Str(man, "name") ?? ""; folder = WebStore.Unpack(c, raw.StartsWith("__MSG_") || raw.Length == 0 ? "store" : raw); } catch (Exception e) { err = "Not added: " + e.Message + "."; return; }
                string e2; info = ExtStore.Read(folder, out e2); if (info == null) { err = "Not added: " + e2; try { Directory.Delete(folder, true); } catch { } folder = null; }
            });
            if (err != null || c == null || info == null) { Pending = new PendingExt { Id = id, Error = err ?? "Not added." }; StoreNote = null; RefreshExtList(); return Pending.Error; }
            Pending = new PendingExt { Id = id, Name = info.Name, Version = info.Version, Crx = c, Asks = WebStore.Asks(man), Gaps = WebStore.Gaps(man), Update = update };
            Pending.Crx.Name = folder;   // the unpacked folder (removed if you cancel)
            StoreNote = null; RefreshExtList();
            return null;
        }
        void ShowExtensionsPage() { var t = Tabs.FirstOrDefault(x => x.Url == Omni.ExtensionsUrl); if (t != null) { Activate(t); RefreshExtList(); } else NewTab(Omni.ExtensionsUrl, true); }
        public void CancelPending()
        {
            var p = Pending; Pending = null;
            if (p != null && p.Crx != null && ExtStore.Ours(p.Crx.Name)) try { Directory.Delete(p.Crx.Name, true); } catch { }
            RefreshExtList();
        }
        // your OK: loaded (it replaces an older copy of the same extension and keeps its data)
        public async Task<string> ConfirmPending()
        {
            var p = Pending; if (p == null || p.Crx == null) return "Nothing to add.";
            Pending = null;
            var r = await LoadStoreFolder(p.Crx.Name, p.Id);
            if (r != null) { Pending = new PendingExt { Id = p.Id, Error = r }; try { Directory.Delete(p.Crx.Name, true); } catch { } }
            RefreshExtList();
            return r;
        }
        async Task<string> LoadStoreFolder(string dest, string id)
        {
            string err; var m = ExtStore.Read(dest, out err); if (m == null) return err;
            var prof = NormalProfile; if (prof == null) return "The page engine isn't ready yet - try again in a moment.";
            CoreWebView2BrowserExtension ext;
            try { ext = await prof.AddBrowserExtensionAsync(dest); }
            catch (Exception e) { return "WebView2 didn't load it (" + e.Message + "). Some extensions use APIs WebView2 doesn't have."; }
            if (ext.Id != id) { try { await ext.RemoveAsync(); } catch { } return "WebView2 loaded it as " + ext.Id + ", not " + id + " - not kept."; }
            foreach (var old in S.Extensions.Where(x => x.Id == ext.Id).ToList()) { S.Extensions.Remove(old); if (ExtStore.Ours(old.Folder) && !string.Equals(old.Folder, dest, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(old.Folder, true); } catch { } }
            m.Id = ext.Id; m.Folder = dest; m.Source = "Chrome Web Store"; m.Enabled = true; m.Store = true; m.Checked = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            S.Extensions.Add(m); S.Save(); ExtUiChanged();
            Toast(m.Name + " " + m.Version + " added from the Chrome Web Store.", 4);
            return null;
        }
        // updates: on your click, or once a day when "Check store extensions for updates" is on (never during a game)
        public async Task<string> CheckStoreUpdates(bool quiet)
        {
            var list = S.Extensions.Where(x => x.Store).ToList(); if (list.Count == 0) return quiet ? null : "No extensions from the Chrome Web Store.";
            var found = new List<string>();
            foreach (var x in list)
            {
                string err = null; var v = await Task.Run(() => WebStore.CheckUpdate(x.Id, x.Version, out err));
                x.Checked = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
                if (v != null && WebStore.Newer(v, x.Version)) found.Add(x.Id);
            }
            S.Save();
            if (found.Count == 0) { var msg = "Your Chrome Web Store extensions are up to date."; if (!quiet) Toast(msg, 3); return msg; }
            // one at a time through the same checks and the same card (it shows what the new version asks for)
            await AddFromStore(found[0], true);
            return found.Count + " update" + (found.Count == 1 ? "" : "s") + " found.";
        }
        public void StoreUpdateTick()
        {
            if (!S.StoreUpdates || snap || Private || Game != null || Program.First != this) return;
            DateTime last; if (DateTime.TryParse(S.StoreChecked ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None, out last) && DateTime.Now - last < TimeSpan.FromDays(1)) return;
            S.StoreChecked = DateTime.Now.ToString("s", CultureInfo.InvariantCulture); S.Save();
            var _ = CheckStoreUpdates(true);
        }

        // ---- hallow://extensions: the "Add from Chrome Web Store" box and the pending card
        FrameworkElement StoreBox()
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Label("Add from Chrome Web Store"));
            sp.Children.Add(Ui.Muted("Paste the extension's store link (chromewebstore.google.com/detail/...) or its 32-letter ID - or open the extension's page in the store and click \"Add to Hallow Browser\". The browser fetches it from Google's update server only when you do, checks its signature and ID, and shows what it asks for before adding it.", 12, 6));
            var box = new TextBox { MinWidth = 420, Padding = new Thickness(8, 5, 8, 5) }; box.SetResourceReference(Control.BackgroundProperty, "FieldBg"); box.SetResourceReference(Control.ForegroundProperty, "Text");
            Action go = () => { var id = WebStore.IdFrom(box.Text); if (id == null) { Toast("That isn't a Chrome Web Store link or extension ID.", 5); return; } box.Text = ""; var _ = AddFromStore(id, false); };
            box.KeyDown += (a, b) => { if (b.Key == System.Windows.Input.Key.Enter) go(); };
            AutomationName(box, "Chrome Web Store link or extension ID");
            sp.Children.Add(Ui.Row(box, Ui.Btn("Add", go, true, "plus", "Fetch it from the Chrome Web Store and check it")));
            sp.Children.Add(Ui.Switch("Check store extensions for updates once a day", "Asks Google's update server about your store extensions only (their IDs and versions). A newer version is shown here for your OK, with what it asks for.", S.StoreUpdates, on => { S.StoreUpdates = on; S.Save(); }));
            sp.Children.Add(Ui.Row(Ui.Btn("Check for updates now", () => { var _ = CheckStoreUpdates(false); }, false, "rotate")));
            sp.Children.Add(Ui.Muted("What WebView2 extensions can't do: talk to desktop apps (native messaging - password managers that need their desktop app won't connect), Google sign-in for extensions (chrome.identity), side panels, managing other extensions, and permission prompts for optional permissions. They don't run in private windows, and store sync of their settings isn't there.", 11.5, 6));
            return Ui.Card(sp);
        }
        static void AutomationName(FrameworkElement e, string n) { System.Windows.Automation.AutomationProperties.SetName(e, n); }
        FrameworkElement PendingCard()
        {
            var p = Pending; if (p == null && StoreNote == null) return null;
            var sp = new StackPanel();
            if (p == null || (p.Crx == null && p.Error == null)) { sp.Children.Add(Ui.Muted(StoreNote ?? "Working...", 12, 0)); return Ui.Card(sp); }
            if (p.Error != null)
            {
                sp.Children.Add(Ui.Row(Ui.Icon("triangle-exclamation", 13, "AmberText"), Ui.T("  " + p.Id, 13, true, "Text")));
                sp.Children.Add(Ui.Muted(p.Error, 12, 6));
                sp.Children.Add(Ui.Row(Ui.Btn("OK", () => { Pending = null; RefreshExtList(); }, false, "check")));
                return Ui.Card(sp);
            }
            sp.Children.Add(Ui.Row(Ui.Icon("puzzle-piece", 14, "AccentBrush"), Ui.T("  " + (p.Update ? "Update " : "Add ") + p.Name + " " + p.Version + "?", 14, true, "Text")));
            sp.Children.Add(Ui.Muted("From the Chrome Web Store - signature and ID " + p.Id + " checked.", 11.5, 4));
            sp.Children.Add(Ui.Label("It can:"));
            foreach (var a in p.Asks.DefaultIfEmpty("Nothing beyond its own pages")) sp.Children.Add(Ui.Muted("• " + a, 12, 2));
            if (p.Gaps.Count > 0)   // 1.16.2: said plainly, before adding (adding still works)
            {
                var warn = Ui.T(WebStore.GapWarning, 12.5, true, "Text", 6); warn.TextWrapping = TextWrapping.Wrap; warn.MaxWidth = 520; var wi = Ui.Icon("triangle-exclamation", 13, "AmberText"); wi.Margin = new Thickness(0, 2, 8, 0); wi.VerticalAlignment = VerticalAlignment.Top;
                sp.Children.Add(Ui.Row(wi, warn));
                sp.Children.Add(Ui.Label("Missing in Hallow Browser (WebView2):")); foreach (var g in p.Gaps) sp.Children.Add(Ui.Muted("• " + g, 12, 2));
            }
            var add = Ui.Btn(p.Update ? "Update" : "Add extension", () => { var _ = ConfirmPending().ContinueWith(r => Dispatcher.BeginInvoke(new Action(() => { if (r.Result != null) Toast(r.Result, 8); }))); }, true, "plus");
            sp.Children.Add(Ui.Row(add, Ui.Btn("Cancel", CancelPending, false, "xmark")));
            return Ui.Card(sp);
        }
    }
}
