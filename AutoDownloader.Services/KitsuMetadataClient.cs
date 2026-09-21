using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Anime metadata from Kitsu.
    ///
    /// Added as a third database because TMDB and TVDB between them still miss a lot of
    /// anime, particularly anything that never had an English release, and a run that finds
    /// no metadata falls back to numbering files by position - which is where the wrong
    /// filenames come from.
    ///
    /// Kitsu was chosen over the better-known options for two practical reasons: it needs no
    /// API key at all, so there is nothing to configure and nothing to expire, and it returns
    /// real per-episode titles, which is what the filenames actually need. AniList has the
    /// better catalogue but very patchy episode titles; Jikan (MyAnimeList) has good episode
    /// titles but depends on MyAnimeList being reachable, and was returning 504 while this was
    /// being written. AniDB is the canonical anime database and is deliberately not used here:
    /// it requires client registration and bans aggressively on rate limits.
    /// </summary>
    public class KitsuMetadataClient
    {
        private const string BaseUrl = "https://kitsu.io/api/edge";

        /// <summary>Kitsu pages its results; this is its maximum page size.</summary>
        private const int PageSize = 20;

        /// <summary>Stops a malformed response paging forever.</summary>
        private const int MaxPages = 20;

        private static readonly HttpClient Http = CreateClient();

        /// <summary>Raised for problems worth showing in the log rather than swallowing.</summary>
        public event Action<string>? OnDiagnostic;

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // Kitsu asks for a real User-Agent, and a JSON:API Accept header.
            client.DefaultRequestHeaders.Add("User-Agent", $"AutoDownloader/{AppInfo.Version}");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.api+json");

            return client;
        }

        /// <summary>
        /// Shows matching a name, newest information first as Kitsu ranks it.
        /// </summary>
        public async Task<List<SeriesCandidate>> SearchCandidatesAsync(
            string showName, CancellationToken cancellationToken = default)
        {
            var results = new List<SeriesCandidate>();

            if (string.IsNullOrWhiteSpace(showName)) return results;

            string url = $"{BaseUrl}/anime?filter[text]={Uri.EscapeDataString(showName)}"
                       + $"&page[limit]=5";

            var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            if (root == null) return results;

            if (!root.Value.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement)) continue;
                if (!int.TryParse(idElement.GetString(), out int id)) continue;
                if (!item.TryGetProperty("attributes", out var attributes)) continue;

                string title = ReadString(attributes, "canonicalTitle") ?? showName;

                // Kitsu's text search always returns something. Asked for "Alex vs America"
                // it offers Chocolate Underground, Totally Spies! and Captain Laserhawk - so
                // without this, a non-anime show whose other databases came up empty would be
                // given a completely unrelated anime's episode list.
                if (!IsPlausibleMatch(showName, title)) continue;

                results.Add(new SeriesCandidate
                {
                    Id = id,
                    Title = title,
                    Year = ReadYear(attributes),
                    Overview = ReadString(attributes, "synopsis"),
                    Source = "Kitsu",
                    EpisodeCount = ReadInt(attributes, "episodeCount") ?? 0,
                });
            }

            return results;
        }

        /// <summary>
        /// Identifies a show, preferring a given year when several share a name.
        /// </summary>
        /// <returns>Null when nothing matched; Kitsu covers anime, so that is routine.</returns>
        public async Task<(string OfficialTitle, int SeriesId, int EpisodeCount)?> GetSeriesAsync(
            string showName, int? preferredYear = null, CancellationToken cancellationToken = default)
        {
            var candidates = await SearchCandidatesAsync(showName, cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0) return null;

            var chosen = preferredYear.HasValue
                ? candidates.FirstOrDefault(c => c.Year == preferredYear) ?? candidates[0]
                : candidates[0];

            return (chosen.Title, chosen.Id, chosen.EpisodeCount);
        }

        /// <summary>
        /// Every episode Kitsu lists for a season, with its title.
        ///
        /// Kitsu numbers episodes across the whole run as well as within a season. Where it
        /// gives a season number, only that season is kept; where it gives none - common for
        /// a single-season show - everything is returned rather than nothing.
        /// </summary>
        public async Task<List<DownloadEpisode>> GetEpisodesAsync(
            int seriesId, int seasonNumber, CancellationToken cancellationToken = default)
        {
            var collected = new List<(int Number, int? Season, string? Title)>();

            for (int pageIndex = 0; pageIndex < MaxPages; pageIndex++)
            {
                string url = $"{BaseUrl}/episodes?filter[mediaId]={seriesId}"
                           + $"&page[limit]={PageSize}&page[offset]={pageIndex * PageSize}&sort=number";

                var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
                if (root == null) break;

                if (!root.Value.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                int countThisPage = 0;

                foreach (var item in data.EnumerateArray())
                {
                    countThisPage++;

                    if (!item.TryGetProperty("attributes", out var attributes)) continue;

                    int? number = ReadInt(attributes, "number");
                    if (number == null || number <= 0) continue;

                    collected.Add((number.Value,
                                   ReadInt(attributes, "seasonNumber"),
                                   ReadString(attributes, "canonicalTitle")));
                }

                if (countThisPage < PageSize) break;
            }

            // Only filter by season when this show actually reports seasons. A single-season
            // show often reports none at all, and filtering then would discard everything.
            var wanted = collected.Any(e => e.Season.HasValue)
                ? collected.Where(e => e.Season == seasonNumber)
                : collected;

            return wanted
                // A numbered entry with no title adds nothing to a filename, and would
                // otherwise show up as an episode the other databases never listed.
                .Where(e => !string.IsNullOrWhiteSpace(e.Title))
                .GroupBy(e => e.Number)
                .Select(g => new DownloadEpisode
                {
                    EpisodeNumber = g.Key,
                    EpisodeTitle = g.Select(x => x.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
                })
                .OrderBy(e => e.EpisodeNumber)
                .ToList();
        }

        /// <summary>
        /// Whether a result is about the show that was asked for.
        ///
        /// Compares the significant words rather than the whole string, because a legitimate
        /// anime match often carries a romanised subtitle the query does not: "Phi Brain:
        /// Puzzle of God" against "Phi Brain: Kami no Puzzle" is the same show, while
        /// "Alex vs America" against "Totally Spies!" shares nothing at all.
        /// </summary>
        public static bool IsPlausibleMatch(string query, string candidateTitle)
        {
            var wanted = SignificantWords(query);
            if (wanted.Count == 0) return false;

            var offered = SignificantWords(candidateTitle);
            if (offered.Count == 0) return false;

            int shared = wanted.Count(w => offered.Contains(w));

            return (double)shared / wanted.Count >= 0.5;
        }

        /// <summary>
        /// Lowercased words of three characters or more. Short words - "vs", "of", "the" -
        /// match everything and so distinguish nothing.
        /// </summary>
        private static HashSet<string> SignificantWords(string value)
        {
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(value)) return words;

            // Separators built from a string plus two code points (39 apostrophe,
            // 92 backslash) so no character escape appears in this source at all.
            var separators = " -:;,.!?()[]/_"
                .ToCharArray()
                .Concat(new[] { (char)39, (char)92 })
                .ToArray();

            foreach (var raw in value.ToLowerInvariant().Split(
                separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Length >= 3) words.Add(raw);
            }

            return words;
        }

        private async Task<JsonElement?> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    OnDiagnostic?.Invoke($"Kitsu returned {(int)response.StatusCode} for a lookup.");
                    return null;
                }

                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                using var document = JsonDocument.Parse(body);
                return document.RootElement.Clone();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A third database is a bonus, never a dependency: it must not be able to
                // fail a run that TMDB and TVDB could have answered on their own.
                OnDiagnostic?.Invoke($"Kitsu lookup failed: {ex.Message}");
                return null;
            }
        }

        private static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int? ReadInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value)) return null;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), out int parsed))
            {
                return parsed;
            }

            return null;
        }

        /// <summary>Kitsu dates are "YYYY-MM-DD"; only the year is wanted.</summary>
        private static int? ReadYear(JsonElement attributes)
        {
            string? startDate = ReadString(attributes, "startDate");
            if (string.IsNullOrWhiteSpace(startDate) || startDate!.Length < 4) return null;

            return int.TryParse(startDate.Substring(0, 4), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int year)
                ? year
                : null;
        }
    }
}
