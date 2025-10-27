using System;
using System.Linq;
using System.Threading.Tasks;
using TMDbLib.Client; // This is for TMDbClient
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search; // <-- THIS IS FOR SearchContainer and SearchTv
using TMDbLib.Objects.TvShows; // This is for TvShow and TvShowMethods

namespace AutoDownloader.Core
{
    /// <summary>
    /// Service to search The Movie Database (TMDB) for official metadata.
    /// Uses the TMDB API Key (v3).
    /// </summary>
    public class MetadataService
    {

        // TMDB API Key (v3) is used for the TMDbLib client.
        // We REMOVE the private constant here, as it comes from Settings.

        private readonly string _tmdbApiKey;
        private readonly TMDbClient _client;

        /// <summary>
        /// Initializes the TMDB client using the key from settings.
        /// </summary>
        public MetadataService(string tmdbApiKey) // <-- MODIFIED CONSTRUCTOR
        {
            _tmdbApiKey = tmdbApiKey;

            // Initialize client only if the key is valid.
            if (!string.IsNullOrWhiteSpace(_tmdbApiKey) && _tmdbApiKey != "YOUR_TMDB_API_KEY_HERE")
            {
                _client = new TMDbClient(_tmdbApiKey);
            }
            else
            {
                // Assign a null client if the key is missing. This prevents the crash.
                _client = null!; // Use null-forgiving operator to satisfy non-nullable field
            }
        }

        // Add a helper property to check if the service is ready
        public bool IsKeyValid => _client != null;

        /// <summary>
        /// Searches for a TV show by name and finds the target season details for download.
        /// </summary>
        /// <param name="showName">The name of the show to search for.</param>
        /// <returns>A tuple containing (OfficialTitle, SeriesId, TargetSeasonNumber, ExpectedEpisodeCount). Returns null on failure.</returns>
        public async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?> FindShowMetadataAsync(string showName)
        {
            if (!IsKeyValid)
            {
                // Soft fail if the client was not initialized due to a bad key.
                return null;
            }

            if (string.IsNullOrWhiteSpace(showName)) return null;

            // 1. Search for the show
            SearchContainer<SearchTv> searchResult = await _client.SearchTvShowAsync(showName);
            SearchTv? firstResult = searchResult.Results.FirstOrDefault();

            if (firstResult == null)
            {
                return null;
            }

            // 2. Get the full details of the show
            TvShow fullShow = await _client.GetTvShowAsync(firstResult.Id, TvShowMethods.ExternalIds | TvShowMethods.Credits);

            if (fullShow == null)
            {
                return null;
            }

            // 3. Determine the Target Season
            int targetSeasonNumber = 1;
            // WORKAROUND: Default to 1 (or 0) to avoid compiler ambiguity with int? checks.
            int expectedCount = 1;

            // Find the Season 1 object (or whatever season is targetted)
            var seasonOne = fullShow.Seasons?
                // Simpler filter: just find Season 1
                .FirstOrDefault(s => s.SeasonNumber == targetSeasonNumber);

            if (seasonOne != null)
            {
                // FINAL WORKAROUND: We explicitly cast the value to its nullable type (int?) 
                // and then use GetValueOrDefault(1) to resolve the type conflict error (CS1501).
                expectedCount = ((int?)seasonOne.EpisodeCount).GetValueOrDefault(1);
            }

            return (fullShow.Name, fullShow.Id, targetSeasonNumber, expectedCount);
        }
    }
}