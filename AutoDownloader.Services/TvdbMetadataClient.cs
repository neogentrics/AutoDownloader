using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TvDbSharper;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Typed access to TheTVDB v4, replacing the reflection-based lookup that preceded it.
    ///
    /// The old code probed for method names at runtime and caught every exception, returning
    /// null on any failure - so "no such show", "the guess was wrong" and "authentication
    /// failed" were indistinguishable, and TVDB silently contributed nothing for a long time.
    ///
    /// The actual fault, found by probing the live API: search results carry a prefixed string
    /// id such as "series-78874", and the reflection fed that straight into Convert.ToInt64,
    /// which threw and was swallowed. Nothing about the key or the login was ever wrong.
    ///
    /// Failures here are reported rather than hidden. A metadata source that quietly does
    /// nothing is worse than one that says why it could not help.
    /// </summary>
    public class TvdbMetadataClient
    {
        private readonly string _apiKey;
        private readonly TvDbClient _client = new TvDbClient();
        private readonly SemaphoreSlim _loginLock = new SemaphoreSlim(1, 1);
        private bool _loggedIn;

        /// <summary>
        /// Raised with the reason a lookup could not be completed.
        /// </summary>
        public event Action<string>? OnDiagnostic;

        public TvdbMetadataClient(string apiKey)
        {
            _apiKey = apiKey;
        }

        /// <summary>
        /// Authenticates once and reuses the token. The previous implementation logged in on
        /// every single call.
        /// </summary>
        private async Task<bool> EnsureLoggedInAsync(CancellationToken cancellationToken)
        {
            if (_loggedIn) return true;

            await _loginLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_loggedIn) return true;

                // The second argument is the subscriber PIN, required only for user-supported
                // keys. An empty string is correct for a project API key.
                await _client.Login(_apiKey, string.Empty, cancellationToken).ConfigureAwait(false);
                _loggedIn = true;
                return true;
            }
            catch (TvDbServerException ex)
            {
                OnDiagnostic?.Invoke($"TVDB login rejected: {ex.Message}. "
                    + "If this key is user-supported rather than a project key, it also needs a PIN.");
                return false;
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"TVDB login failed: {ex.Message}");
                return false;
            }
            finally
            {
                _loginLock.Release();
            }
        }

        /// <summary>
        /// Finds a series by name. Returns null when there is genuinely no match, having said
        /// so via OnDiagnostic when the cause was an error rather than an empty result.
        /// </summary>
        public async Task<(int SeriesId, string Name)?> SearchSeriesAsync(
            string showName, int? preferredYear = null, CancellationToken cancellationToken = default)
        {
            if (!await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false)) return null;

            try
            {
                var response = await _client.Search(
                    new SearchOptionalParams { Query = showName, Type = "series" },
                    cancellationToken).ConfigureAwait(false);

                var results = response?.Data;
                if (results == null || results.Length == 0)
                {
                    OnDiagnostic?.Invoke($"TVDB found no series matching '{showName}'.");
                    return null;
                }

                // When the caller has already established which show is meant, match on the
                // year rather than taking whatever ranks first.
                var first = results[0];

                if (preferredYear.HasValue)
                {
                    var byYear = results.FirstOrDefault(r =>
                        int.TryParse(r.Year, out int y) && y == preferredYear.Value);

                    if (byYear != null) first = byYear;
                }

                if (!TryParseSeriesId(first.Id, out int seriesId))
                {
                    // This is the exact shape that defeated the old reflection.
                    OnDiagnostic?.Invoke($"TVDB returned an unrecognised id format: '{first.Id}'.");
                    return null;
                }

                string name = !string.IsNullOrWhiteSpace(first.Name) ? first.Name : showName;
                return (seriesId, name);
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"TVDB search for '{showName}' failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns several possible matches, so the caller can tell a show apart from its
        /// reboot rather than being handed whichever happens to rank first.
        /// </summary>
        public async Task<List<SeriesCandidate>> SearchCandidatesAsync(
            string showName, CancellationToken cancellationToken = default)
        {
            var candidates = new List<SeriesCandidate>();

            if (!await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false)) return candidates;

            try
            {
                var response = await _client.Search(
                    new SearchOptionalParams { Query = showName, Type = "series" },
                    cancellationToken).ConfigureAwait(false);

                foreach (var result in response?.Data ?? Array.Empty<SearchResultDto>())
                {
                    if (!TryParseSeriesId(result.Id, out int id)) continue;

                    int? year = null;
                    if (int.TryParse(result.Year, out int parsedYear) && parsedYear > 1800) year = parsedYear;

                    candidates.Add(new SeriesCandidate
                    {
                        Id = id,
                        Title = string.IsNullOrWhiteSpace(result.Name) ? showName : result.Name,
                        Year = year,
                        Overview = result.Overview,
                        Source = "TVDB"
                    });
                }
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"TVDB search for '{showName}' failed: {ex.Message}");
            }

            return candidates;
        }

        /// <summary>
        /// Returns the episodes of one season, ordered by episode number.
        /// </summary>
        public async Task<List<DownloadEpisode>> GetSeasonEpisodesAsync(
            int seriesId, int seasonNumber, CancellationToken cancellationToken = default)
        {
            var episodes = new List<DownloadEpisode>();

            if (!await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false)) return episodes;

            try
            {
                // "default" is the series' own ordering. The Season parameter filters
                // server-side, but the response is filtered again below because the API also
                // returns specials attached to the series.
                var response = await _client.SeriesEpisodes(
                    seriesId,
                    "default",
                    new SeriesEpisodesOptionalParams { Season = seasonNumber },
                    cancellationToken).ConfigureAwait(false);

                var data = response?.Data?.Episodes;
                if (data == null || data.Length == 0)
                {
                    OnDiagnostic?.Invoke($"TVDB returned no episodes for series {seriesId} season {seasonNumber}.");
                    return episodes;
                }

                episodes = data
                    .Where(e => e.SeasonNumber == seasonNumber && e.Number > 0)
                    .OrderBy(e => e.Number)
                    .Select(e => new DownloadEpisode
                    {
                        EpisodeNumber = e.Number,
                        EpisodeTitle = string.IsNullOrWhiteSpace(e.Name) ? null : e.Name
                    })
                    .ToList();

                return episodes;
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"TVDB episode lookup for series {seriesId} season {seasonNumber} failed: {ex.Message}");
                return episodes;
            }
        }

        /// <summary>
        /// TVDB v4 search results identify a series as "series-78874" rather than a bare
        /// number. Accepts either form.
        /// </summary>
        public static bool TryParseSeriesId(string? raw, out int seriesId)
        {
            seriesId = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // Take the trailing run of digits, so both "series-78874" and "78874" work.
            int end = raw.Length;
            int start = end;
            while (start > 0 && char.IsDigit(raw[start - 1])) start--;

            if (start == end) return false;

            return int.TryParse(raw.Substring(start, end - start), out seriesId) && seriesId > 0;
        }
    }
}
