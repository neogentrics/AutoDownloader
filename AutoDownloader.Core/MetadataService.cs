using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using TMDbLib.Client;
using TMDbLib.Objects.General; // <-- FIX: Added for SearchContainer
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;
using TvDbSharper; // Required for TVDB
using TvDbSharper.Clients.Authentication; // <-- FIX: Added for v4 Auth
using TvDbSharper.Clients.Series; // <-- FIX: Added for v4 Series Client

namespace AutoDownloader.Core
{
    /// <summary>
    /// Service to search The Movie Database (TMDB) and TheTVDB (TVDB) for official metadata.
    /// </summary>
    public class MetadataService
    {
        private readonly string _tmdbApiKey;
        private readonly string _tvdbApiKey;
        private readonly TMDbClient _tmdbClient;
        private readonly TvDbClient _tvdbClient;

        /// <summary>
        /// Initializes the metadata services with keys from settings.
        /// </summary>
        public MetadataService(string tmdbApiKey, string tvdbApiKey)
        {
            _tmdbApiKey = tmdbApiKey;
            _tvdbApiKey = tvdbApiKey;

            // Initialize TMDB client (Primary)
            if (!string.IsNullOrWhiteSpace(_tmdbApiKey) && _tmdbApiKey != "YOUR_TMDB_API_KEY_HERE")
            {
                _tmdbClient = new TMDbClient(_tmdbApiKey);
            }
            else
            {
                _tmdbClient = null!;
            }

            // Initialize TVDB client (Secondary)
            _tvdbClient = new TvDbClient();
        }

        // FIX: Added missing helper property for MainWindow.xaml.cs
        public bool IsKeyValid => _tmdbClient != null;
        public bool IsTvdbValid => !string.IsNullOrWhiteSpace(_tvdbApiKey) && _tvdbApiKey != "YOUR_TVDB_API_KEY_HERE";

        /// <summary>
        /// Attempts to find metadata using TMDB, falling back to TVDB if TMDB fails.
        /// </summary>
        public async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?> FindShowMetadataAsync(string showName)
        {
            if (string.IsNullOrWhiteSpace(showName)) return null;

            // 1. TRY TMDB (Primary Source)
            var tmdbResult = await GetTmdbMetadataAsync(showName);
            if (tmdbResult != null)
            {
                return tmdbResult;
            }

            // 2. TRY TVDB (Fallback Source - V1.9 Feature)
            var tvdbResult = await GetTvdbMetadataAsync(showName);
            if (tvdbResult != null)
            {
                return tvdbResult;
            }

            return null; // Both failed
        }

        private async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?> GetTmdbMetadataAsync(string showName)
        {
            if (!IsKeyValid) return null; // TMDB key missing or invalid

            try
            {
                SearchContainer<SearchTv> searchResult = await _tmdbClient.SearchTvShowAsync(showName);
                SearchTv? firstResult = searchResult.Results.FirstOrDefault();

                if (firstResult == null) return null;

                TvShow fullShow = await _tmdbClient.GetTvShowAsync(firstResult.Id, TvShowMethods.ExternalIds | TvShowMethods.Credits);

                if (fullShow == null) return null;

                int targetSeasonNumber = 1;
                int expectedCount = 1; // Default to 1

                var seasonOne = fullShow.Seasons?.FirstOrDefault(s => s.SeasonNumber == targetSeasonNumber);

                if (seasonOne != null)
                {
                    // V1.8 FIX: Final working cast to resolve compiler error
                    expectedCount = ((int?)seasonOne.EpisodeCount).GetValueOrDefault(1);
                }

                // Use the TMDB ID as the standard ID
                return (fullShow.Name, firstResult.Id, targetSeasonNumber, expectedCount);
            }
            catch (Exception)
            {
                return null; // Soft fail on TMDB error
            }
        }

        private async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?> GetTvdbMetadataAsync(string showName)
        {
            if (!IsTvdbValid) return null; // TVDB key missing or invalid

            try
            {
                // 1. Authenticate with TVDB (v4 API syntax)
                // FIX: Use _tvdbClient.Authentication.LoginAsync
                await _tvdbClient.Authentication.LoginAsync(_tvdbApiKey);

                // 2. Search for the series (v4 API syntax)
                // FIX: Use _tvdbClient.Search.SearchSeriesAsync
                var searchResult = await _tvdbClient.Search.SearchSeriesAsync(showName);
                var firstResult = searchResult?.Data?.FirstOrDefault();

                if (firstResult == null) return null;

                // 3. Get full series data to check seasons
                // TVDB uses string IDs (e.g., "78804")
                string seriesTvdbId = firstResult.TvdbId;
                // FIX: Use _tvdbClient.Series.GetSeriesAsync
                var seriesData = await _tvdbClient.Series.GetSeriesAsync(seriesTvdbId);

                if (seriesData?.Data == null) return null;

                // 4. Calculate Expected Count for Season 1 (Simplification)
                var seasonOne = seriesData.Data.Seasons?
                    .FirstOrDefault(s => s.Type.Name == "Aired" && s.Number == 1);

                int expectedCount = 1; // Default to 1
                if (seasonOne != null)
                {
                    // Get episodes for that specific season ID
                    // FIX: Use _tvdbClient.Series.GetSeasonEpisodesAsync
                    var episodes = await _tvdbClient.Series.GetSeasonEpisodesAsync(seasonOne.Id, 1);
                    expectedCount = episodes.Data?.Count() ?? 1;
                }

                // 5. Return data (we must parse the string ID to int for our model)
                int.TryParse(seriesTvdbId, out int seriesIdInt);
                return (firstResult.Name, seriesIdInt, 1, expectedCount);
            }
            catch (Exception)
            {
                // Log exception if necessary, but soft fail
                return null;
            }
        }
    }
}