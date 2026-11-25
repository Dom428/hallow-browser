// Hallow Browser 1.18 - two glyphs drawn here in the same 512-unit box as the Font Awesome ones (no new download): "share-nodes"
// (three dots joined, the page's Share action) and "picture-in-picture" (a frame with a small window in its corner).
namespace HallowBrowser
{
    public static partial class Icons
    {
        static Icons()
        {
            if (!Data.ContainsKey("share-nodes"))
                Data["share-nodes"] = "F1 M0 0L0 0M512 512L512 512" +
                    "M320 96a80 80 0 1 0 160 0a80 80 0 1 0 -160 0z" +
                    "M32 256a80 80 0 1 0 160 0a80 80 0 1 0 -160 0z" +
                    "M320 416a80 80 0 1 0 160 0a80 80 0 1 0 -160 0z" +
                    "M123 275L411 115L389 77L101 237Z" +
                    "M101 275L389 435L411 397L123 237Z";
            if (!Data.ContainsKey("picture-in-picture"))
                Data["picture-in-picture"] = "M0 0L0 0M512 512L512 512" +
                    "M16 64h480v384h-480z M64 112v288h384v-288z" +
                    "M240 248h176v120h-176z";
        }
    }
}
