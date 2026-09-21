using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Path segments that describe the kind of page rather than naming anything.
        ///
        /// A YouTube playlist URL is the clearest case: everything identifying the content is
        /// in the query string, so the path yields "playlist" and the metadata lookup would
        /// dutifully go looking for a show called Playlist. When the URL only offers one of
        /// these, the page title is a far better source.
        /// </summary>
        private static readonly HashSet<string> PlaceholderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "playlist", "playlists", "watch", "video", "videos", "embed", "list",
            "index", "home", "browse", "channel", "channels", "user", "c",
            "series", "show", "shows", "tv", "anime", "episode", "episodes", "title"
        };

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
        /// <summary>
        /// True when a parsed name describes the page type rather than naming the content,
        /// so the caller knows the URL alone was not enough.
        /// </summary>
        public static bool IsPlaceholderName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;

            return PlaceholderNames.Contains(name)
                || PlaceholderNames.Contains(name.Replace(" ", string.Empty))
                || LooksLikeAnIdentifier(name!);
        }

        /// <summary>
        /// Whether a name is really an id the site happened to put in its URL.
        ///
        /// Plenty of sites address a show by a UUID rather than its title, so the URL yields
        /// something like "Cba7f3ea 50Cd 40Fa Aae3 Cba25feb1c3e". That is not a placeholder
        /// word like "watch" or "playlist", so it was accepted as the show name and searched
        /// for verbatim - and the user had to retype the title by hand every time.
        ///
        /// Recognising it as useless is what makes the parser go and read the page title
        /// instead, which is where the real name was all along.
        ///
        /// A word only counts as id-like if it is pure hexadecimal AND contains a digit, so
        /// ordinary titles survive: "Cafe" and "Deadbeef" are hexadecimal but have no digit,
        /// and anything containing a letter past F - most words - cannot qualify at all.
        /// </summary>
        public static bool LooksLikeAnIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            var words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return false;

            int idLike = words.Count(IsHexChunk);

            // Half or more, so a title with one stray code in it is not thrown away.
            return idLike * 2 >= words.Length;
        }

        private static bool IsHexChunk(string word)
        {
            if (word.Length < 4) return false;

            bool hasDigit = false;

            foreach (char c in word)
            {
                char lower = char.ToLowerInvariant(c);

                bool isHexLetter = lower >= 'a' && lower <= 'f';
                bool isDigit = lower >= '0' && lower <= '9';

                if (!isHexLetter && !isDigit) return false;
                if (isDigit) hasDigit = true;
            }

            return hasDigit;
        }

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
                || PlaceholderNames.Contains(showName.Replace(" ", string.Empty))
                || PlaceholderNames.Contains(showName)
                || LooksLikeAnIdentifier(showName)
                || SeasonSegmentPattern.IsMatch(showName.Replace(" ", "-"));

            if (!needsFallback) return (showName, seasonNumber);

            string? scraped = await TryScrapeTitleAsync(url, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(scraped))
            {
                string tidied = TidyName(scraped!);

                // Guard against swapping one placeholder for another, e.g. a page titled
                // simply "Playlist" or "YouTube".
                if (!PlaceholderNames.Contains(tidied.Replace(" ", string.Empty))
                    && !tidied.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    return (tidied, seasonNumber);
                }
            }

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

            // Page titles routinely open with the site's own verb - "Watch X", "Stream X" -
            // and that word then goes into the database search verbatim, where it matches
            // nothing. Only stripped when something is left afterwards, so a page actually
            // titled "Watch" is not reduced to nothing.
            foreach (var prefix in new[] { "watch ", "stream ", "play " })
            {
                if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && cleaned.Length > prefix.Length + 2)
                {
                    cleaned = cleaned.Substring(prefix.Length).Trim();
                    break;
                }
            }

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
