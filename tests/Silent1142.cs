// Hallow Browser 1.14.2 - test browsers are silent: they start with --mute-audio (HALLOW_BROWSER_MUTE=0 turns that off, only for a
// deliberate audio check). Chromium then never reports a page as audible, so the few checks of "is it playing sound" are skipped.
namespace HallowBrowser.Tests
{
    public static class Silent
    {
        public static bool On { get { return System.Environment.GetEnvironmentVariable("HALLOW_BROWSER_MUTE") != "0"; } }
        public static string Note { get { return On ? " - not checked: test browsers are muted at the audio output (--mute-audio)" : ""; } }
    }
}