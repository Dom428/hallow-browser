// Hallow Browser 1.16 - developer test commands for Chrome Web Store installs (only with HALLOW_BROWSER_TESTDIR, like the others):
// add by ID (as a paste / the store button does), the pending card's state, Add / Cancel (the same code the card's buttons call),
// the update check, and a script in an extension's popup. The store is a local stand-in in tests (HALLOW_BROWSER_CWS_URL).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HallowBrowser
{
    public static partial class TestHooks
    {
        static object PendingJson()
        {
            var p = BrowserWindow.Pending; if (p == null) return null;
            return new Dictionary<string, object> { { "id", p.Id }, { "name", p.Name }, { "version", p.Version }, { "error", p.Error }, { "asks", p.Asks }, { "gaps", p.Gaps }, { "update", p.Update }, { "folder", p.Crx == null ? null : p.Crx.Name } };
        }
        static string V116(BrowserWindow w, string cmd, string arg)
        {
            switch (cmd)
            {
                case "add": return Async(w, async () => { var e = await w.AddFromStore(arg.Trim(), false); return "ok " + js.Serialize(new Dictionary<string, object> { { "error", e }, { "pending", PendingJson() } }); });
                case "state": return Program.OnUi(w, () => "ok " + js.Serialize(new Dictionary<string, object> { { "pending", PendingJson() }, { "note", BrowserWindow.StoreNote }, { "fetches", BrowserWindow.StoreFetches } }));
                case "confirm": return Async(w, async () => { var e = await w.ConfirmPending(); return e == null ? "ok" : "error " + e; });
                case "cancel": return Program.OnUi(w, () => { w.CancelPending(); return "ok"; });
                case "updates": return Async(w, async () => { var r = await w.CheckStoreUpdates(true); return "ok " + js.Serialize(new Dictionary<string, object> { { "result", r }, { "pending", PendingJson() } }); });
                case "popupeval": return Async(w, async () => { var c = BrowserWindow.ExtPopupCore; if (c == null) return "error no popup"; return "ok " + await Panels.Eval(c, arg); });
            }
            return "error unknown test command";
        }
    }
}
