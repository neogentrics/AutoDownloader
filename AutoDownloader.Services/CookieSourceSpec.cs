using System;
using System.IO;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Interprets the <c>CookieSource</c> setting, which is a single string that may be either
    /// a browser specification or a path to a cookies.txt file.
    ///
    /// This lives in one place deliberately. The Preferences window and the yt-dlp command
    /// builder both have to make the same judgement, and they previously disagreed: the window
    /// classified by path shape while the downloader used <c>File.Exists</c>. A mistyped path
    /// therefore looked like a file to the UI and like a browser name to yt-dlp, which then
    /// failed with a confusing error about an unknown browser.
    /// </summary>
    public static class CookieSourceSpec
    {
        /// <summary>
        /// True when the value should be passed to yt-dlp as <c>--cookies</c> (a file) rather
        /// than <c>--cookies-from-browser</c>.
        ///
        /// Classification is by shape, not by existence: a path that does not exist yet is
        /// still a path, and reporting "that file is missing" is far more useful than silently
        /// handing it to yt-dlp as a browser name.
        ///
        /// Anything else is treated as a browser specification. yt-dlp accepts more browsers
        /// than any list we would hardcode, and supports
        /// <c>BROWSER[+KEYRING][:PROFILE][::CONTAINER]</c> - so "chrome:Profile 2" is valid and
        /// must not be mistaken for a path.
        /// </summary>
        public static bool IsFilePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            value = value.Trim();

            if (value.Contains(Path.DirectorySeparatorChar)) return true;
            if (value.Contains(Path.AltDirectorySeparatorChar)) return true;
            if (value.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return true;

            // A Windows drive-qualified path such as "D:cookies" - but not "chrome:Profile 2",
            // where the character before the colon is part of a longer word.
            if (value.Length > 2 && value[1] == ':' && char.IsLetter(value[0])) return true;

            return false;
        }

        /// <summary>
        /// True when no cookies should be sent at all. This is the default, and the only safe
        /// one: yt-dlp treats an unreadable cookie source as fatal and downloads nothing.
        /// </summary>
        public static bool IsNone(string? value) => string.IsNullOrWhiteSpace(value);
    }
}
