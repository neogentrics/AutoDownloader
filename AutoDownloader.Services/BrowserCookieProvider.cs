using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Hands the page scanner the same session the user already has in their browser.
    ///
    /// The scanner runs its own headless Chromium, signed in to nothing. On any site that
    /// only lists episodes to a signed-in visitor that is fatal and silent: a Food Network
    /// show page that shows a full season in the user's browser yields zero links to the
    /// scanner, which looks exactly like a broken indexer.
    ///
    /// yt-dlp already knows how to read a browser's cookie store, including the parts that
    /// are encrypted per-user, so rather than reimplement that per browser it is asked to
    /// dump the jar and the result is handed to Playwright.
    ///
    /// This only ever re-uses credentials the user has already established themselves. It
    /// authenticates as them; it does not unlock anything their account cannot already open,
    /// and it has no bearing on encrypted streams.
    /// </summary>
    public static class BrowserCookieProvider
    {
        /// <summary>Tab, as a code point: an escape here would be eaten by the tooling.</summary>
        private static readonly char Tab = (char)9;

        /// <summary>Raised with progress and problems. Never carries cookie values.</summary>
        public static event Action<string>? OnDiagnostic;

        /// <summary>
        /// Cookies for the configured source, ready to add to a Playwright context.
        /// Empty when none are configured or they could not be read - never throws, because
        /// scanning without cookies is still better than not scanning.
        /// </summary>
        public static async Task<List<Microsoft.Playwright.Cookie>> LoadAsync(
            string ytDlpPath, string? cookieSource, CancellationToken cancellationToken = default)
        {
            var empty = new List<Microsoft.Playwright.Cookie>();

            if (CookieSourceSpec.IsNone(cookieSource)) return empty;

            try
            {
                // A path already is a cookie file; a browser name needs exporting first.
                string? file = CookieSourceSpec.IsFilePath(cookieSource!)
                    ? cookieSource!.Trim()
                    : await ExportFromBrowserAsync(ytDlpPath, cookieSource!.Trim(), cancellationToken)
                        .ConfigureAwait(false);

                if (file == null || !File.Exists(file)) return empty;

                try
                {
                    var cookies = ParseNetscape(File.ReadAllLines(file));

                    OnDiagnostic?.Invoke($"Loaded {cookies.Count} cookie(s) for the page scanner.");
                    return cookies;
                }
                finally
                {
                    // The export is a copy of the user's session. It does not linger.
                    if (!CookieSourceSpec.IsFilePath(cookieSource!))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"Could not read cookies for the page scanner: {ex.Message}");
                return empty;
            }
        }

        /// <summary>
        /// Asks yt-dlp to dump the browser's cookie jar to a temporary file.
        ///
        /// The URL given is irrelevant and deliberately fails to resolve: yt-dlp writes the
        /// jar regardless, and this avoids fetching anything real just to get cookies.
        /// </summary>
        private static async Task<string?> ExportFromBrowserAsync(
            string ytDlpPath, string browser, CancellationToken cancellationToken)
        {
            if (!File.Exists(ytDlpPath))
            {
                OnDiagnostic?.Invoke("yt-dlp was not found, so browser cookies could not be exported.");
                return null;
            }

            string target = Path.Combine(Path.GetTempPath(), $"ad-cookies-{Guid.NewGuid():N}.txt");

            var startInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("--cookies-from-browser");
            startInfo.ArgumentList.Add(browser);
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(target);
            startInfo.ArgumentList.Add("--simulate");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("https://example.invalid/");

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            // Drained so the process cannot block on a full pipe.
            _ = process.StandardOutput.ReadToEndAsync();
            string errors = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (File.Exists(target)) return target;

            // The most common cause by far, and the one worth naming.
            if (errors.IndexOf("could not copy", StringComparison.OrdinalIgnoreCase) >= 0
                || errors.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0
                || errors.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                OnDiagnostic?.Invoke($"Could not read {browser}'s cookies - it locks them while it "
                    + "is running. Close it, or use Firefox, which does not.");
            }
            else
            {
                OnDiagnostic?.Invoke($"Could not export cookies from {browser}.");
            }

            return null;
        }

        /// <summary>
        /// Reads the Netscape cookie format yt-dlp writes:
        /// domain, includeSubdomains, path, secure, expiry, name, value - tab separated.
        /// </summary>
        public static List<Microsoft.Playwright.Cookie> ParseNetscape(IEnumerable<string> lines)
        {
            var cookies = new List<Microsoft.Playwright.Cookie>();

            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string line = raw;
                bool httpOnly = false;

                // An httpOnly cookie is written as a comment with a marker prefix, so it has
                // to be recognised before comments are skipped.
                const string HttpOnlyPrefix = "#HttpOnly_";
                if (line.StartsWith(HttpOnlyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    httpOnly = true;
                    line = line.Substring(HttpOnlyPrefix.Length);
                }
                else if (line.StartsWith("#"))
                {
                    continue;
                }

                var parts = line.Split(Tab);
                if (parts.Length < 7) continue;

                string domain = parts[0].Trim();
                string path = parts[2].Trim();
                bool secure = parts[3].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                string name = parts[5].Trim();
                string value = parts[6];

                if (domain.Length == 0 || name.Length == 0) continue;

                // A cookie with no expiry is a session cookie, which Playwright expects as -1.
                double expires = -1;
                if (long.TryParse(parts[4].Trim(), out long epoch) && epoch > 0) expires = epoch;

                cookies.Add(new Microsoft.Playwright.Cookie
                {
                    Name = name,
                    Value = value,
                    Domain = domain,
                    Path = path.Length == 0 ? "/" : path,
                    Expires = (float)expires,
                    HttpOnly = httpOnly,
                    Secure = secure,
                });
            }

            return cookies;
        }
    }
}
