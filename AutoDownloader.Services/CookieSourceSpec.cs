using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
        /// Chromium-based browsers hold an exclusive lock on their cookie database while
        /// running on Windows, so yt-dlp cannot read it and aborts the download outright.
        /// Firefox is absent deliberately: it can be read while running.
        /// </summary>
        private static readonly (string Browser, string ProcessName)[] LockingBrowsers =
        {
            ("chrome",   "chrome"),
            ("edge",     "msedge"),
            ("brave",    "brave"),
            ("chromium", "chromium"),
            ("opera",    "opera"),
            ("vivaldi",  "vivaldi"),
            ("whale",    "whale"),
        };

        /// <summary>
        /// Returns a warning when the configured cookie source cannot currently be read, or
        /// null when it looks usable.
        ///
        /// This exists because the failure is otherwise invisible until the download is
        /// already running, and then it repeats once per episode. yt-dlp reports it as
        /// "Could not copy Chrome cookie database" even for Edge, which does not make the
        /// cause obvious.
        /// </summary>
        public static string? GetPreflightWarning(string? cookieSource)
        {
            if (IsNone(cookieSource)) return null;

            string value = cookieSource!.Trim();

            if (IsFilePath(value))
            {
                return File.Exists(value)
                    ? null
                    : $"The cookies file was not found: {value}. yt-dlp treats an unreadable "
                      + "cookie source as fatal, so downloads will fail.";
            }

            // Match the browser name before any +keyring / :profile suffix.
            string browser = value.Split('+', ':')[0].Trim().ToLowerInvariant();

            var locking = LockingBrowsers.FirstOrDefault(b => b.Browser == browser);
            if (locking.Browser == null) return null;

            if (!IsProcessRunning(locking.ProcessName)) return null;

            return $"{char.ToUpperInvariant(browser[0])}{browser.Substring(1)} is currently running, "
                 + "and it locks its cookie database while open. yt-dlp will fail on every episode "
                 + $"until you either close {browser} completely or set cookies to None in Preferences.";
        }

        private static bool IsProcessRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch
            {
                // Process enumeration can be denied; a missing warning is better than a crash.
                return false;
            }
        }

        /// <summary>
        /// True when no cookies should be sent at all. This is the default, and the only safe
        /// one: yt-dlp treats an unreadable cookie source as fatal and downloads nothing.
        /// </summary>
        /// <summary>
        /// Whether no cookies were asked for.
        ///
        /// The Preferences dropdown stores an empty string for "None", but a person typing
        /// --cookies on the command line naturally writes "none", and a settings file edited
        /// by hand often says so too. yt-dlp treats that as a browser name and aborts with
        /// "unsupported browser specified for cookies", which reads like a bug in the app
        /// rather than a value it could simply have understood.
        /// </summary>
        public static bool IsNone(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;

            string trimmed = value!.Trim();

            return trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("off", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("no", StringComparison.OrdinalIgnoreCase);
        }
    }
}
