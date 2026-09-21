using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Orchestration
{
    /// <summary>
    /// Derives a show name and season number from a URL.
    ///
    /// Moved out of the window code-behind so it can be unit tested without a UI: the parsing
    /// rules here decide which show gets looked up, so a regression is expensive and silent.
    /// </summary>
    public class UrlMetadataParser
    {
        /// <summary>
        /// "season-2", "season_2", "s2", "s02" as a whole path segment.
        /// </summary>
        private static readonly Regex SeasonSegmentPattern =
            new Regex(@"^(?:season[-_]?|s)(\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ToolManagerService.FIREFOX_USER_AGENT);
            return client;
        }

        /// <summary>
        /// Parses the URL only. No network access, so this is deterministic and testable.
        /// </summary>
        public static (string ShowName, int? SeasonNumber) ParseFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.Segments
                    .Select(s => s.TrimEnd('/'))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                int? seasonNumber = null;
                string showName = string.Empty;

                for (int i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i].Trim().Trim('/');
                    if (string.IsNullOrEmpty(segment)) continue;

                    string lowered = segment.ToLowerInvariant();

                    // "season-2" / "s02" - the show name is the segment before it.
                    var match = SeasonSegmentPattern.Match(lowered);
                    if (match.Success)
                    {
                        if (int.TryParse(match.Groups[1].Value, out int parsed)) seasonNumber = parsed;
                        if (i > 0) showName = segments[i - 1];
                        break;
                    }

                    // "/season/2/" - the number is the following segment.
                    if (lowered == "season" && i + 1 < segments.Count)
                    {
                        if (int.TryParse(segments[i + 1].ToLowerInvariant(), out int parsed))
                        {
                            seasonNumber = parsed;
                            if (i > 0) showName = segments[i - 1];
                            break;
                        }
                    }
                }

                // No explicit season segment: take the last meaningful segment.
                if (string.IsNullOrWhiteSpace(showName))
                {
                    showName = segments
                        .Where(s => !s.All(char.IsDigit)
                                    && !s.Equals("series", StringComparison.OrdinalIgnoreCase)
                                    && !s.Equals("seasons", StringComparison.OrdinalIgnoreCase)
                                    && !SeasonSegmentPattern.IsMatch(s))
                        .LastOrDefault() ?? string.Empty;
                }

                return (TidyName(showName), seasonNumber);
            }
            catch
            {
                return ("Unknown Show", null);
            }
        }

        /// <summary>
        /// Parses the URL, and if that yields nothing usable, falls back to fetching the page
        /// and reading its og:title / twitter:title / &lt;title&gt;.
        /// </summary>
        public async Task<(string ShowName, int? SeasonNumber)> ParseAsync(
            string url, CancellationToken cancellationToken = default)
        {
            var (showName, seasonNumber) = ParseFromUrl(url);

            bool needsFallback =
                string.IsNullOrWhiteSpace(showName)
                || showName.Equals("Unknown Show", StringComparison.OrdinalIgnoreCase)
                || SeasonSegmentPattern.IsMatch(showName.Replace(" ", "-"));

            if (!needsFallback) return (showName, seasonNumber);

            string? scraped = await TryScrapeTitleAsync(url, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(scraped)) return (TidyName(scraped!), seasonNumber);

            return (showName, seasonNumber);
        }

        private static async Task<string?> TryScrapeTitleAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                string? title = MatchGroup(html, "<meta\\s+property=[\"']og:title[\"']\\s+content=[\"']([^\"']+)[\"']")
                             ?? MatchGroup(html, "<meta\\s+name=[\"']og:title[\"']\\s+content=[\"']([^\"']+)[\"']")
                             ?? MatchGroup(html, "<meta\\s+name=[\"']twitter:title[\"']\\s+content=[\"']([^\"']+)[\"']");

                if (string.IsNullOrWhiteSpace(title))
                {
                    var titleTag = Regex.Match(html, "<title[^>]*>(.*?)</title>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (titleTag.Success)
                    {
                        title = Regex.Replace(titleTag.Groups[1].Value, "\\s+", " ").Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(title)) return null;

                // Strip site suffixes such as " - Tubi" or " | YouTube".
                return title!.Split(new[] { "|", " - ", " — " }, StringSplitOptions.None)[0].Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string? MatchGroup(string input, string pattern)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// "love-thy-neighbor" -> "Love Thy Neighbor".
        /// </summary>
        public static string TidyName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Unknown Show";

            string cleaned = raw.Replace('-', ' ').Replace('_', ' ').Trim();
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            if (string.IsNullOrWhiteSpace(cleaned)) return "Unknown Show";

            return new CultureInfo("en-US", false).TextInfo.ToTitleCase(cleaned);
        }

        /// <summary>
        /// Makes a string safe to use as a Windows file or folder name.
        ///
        /// This existed in the UI but its only call site was commented out, so a title such as
        /// "Star Wars: The Clone Wars" threw straight out of Directory.CreateDirectory.
        /// </summary>
        public static string SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown Show";

            var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray());

            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                cleaned = cleaned.Replace(c, '_');
            }

            foreach (var c in System.IO.Path.GetInvalidPathChars())
            {
                cleaned = cleaned.Replace(c, '_');
            }

            cleaned = cleaned.Trim();

            // Windows forbids trailing dots and spaces.
            while (cleaned.Length > 0 && (cleaned.EndsWith(".") || cleaned.EndsWith(" ")))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }

            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown Show" : cleaned;
        }
    }
}
