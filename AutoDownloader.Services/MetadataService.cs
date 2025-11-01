using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using TMDbLib.Client;
using TMDbLib.Objects.General; // <-- FIX: Added for SearchContainer
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;
// We no longer need TvDbSharper

namespace AutoDownloader.Services
{
    /// <summary>
    /// Service to search The Movie Database (TMDB) for official metadata.
    /// V1.9: Reworked for user-selectable database lookups.
    /// </summary>
    public class MetadataService
    {
        private readonly string _tmdbApiKey;
        private readonly TMDbClient _tmdbClient;

        public MetadataService(string tmdbApiKey, string tvdbApiKey)
        {
            _tmdbApiKey = tmdbApiKey;
            // _tvdbApiKey = tvdbApiKey; // We no longer use this

            // Initialize TMDB client (Primary)
            if (IsTmdbKeyValid)
            {
                _tmdbClient = new TMDbClient(_tmdbApiKey);
            }
            else
            {
                _tmdbClient = null!;
            }
        }

        // Helper properties to check if keys are valid
        public bool IsTmdbKeyValid => !string.IsNullOrWhiteSpace(_tmdbApiKey) && _tmdbApiKey != "YOUR_TMDB_API_KEY_HERE";
        public bool IsTvdbKeyValid => false; // FIX: Always return false for now


        /// <summary>
        /// V1.9: Searches TMDB for metadata.
        /// </summary>
        public async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?>
            GetTmdbMetadataAsync(string showName)
        {
            if (!IsTmdbKeyValid) return null;

            try
            {
                SearchContainer<SearchTv> searchResult = await _tmdbClient.SearchTvShowAsync(showName);
                SearchTv? firstResult = searchResult.Results.FirstOrDefault();

                if (firstResult == null) return null;

                TvShow fullShow = await _tmdbClient.GetTvShowAsync(firstResult.Id, TvShowMethods.ExternalIds | TvShowMethods.Credits);
                if (fullShow == null) return null;

                int targetSeasonNumber = 1;
                int expectedCount = 1;

                var seasonOne = fullShow.Seasons?.FirstOrDefault(s => s.SeasonNumber == targetSeasonNumber);
                if (seasonOne != null)
                {
                    expectedCount = ((int?)seasonOne.EpisodeCount).GetValueOrDefault(1);
                }
                if (expectedCount == 0) expectedCount = 1;

                return (fullShow.Name, firstResult.Id, targetSeasonNumber, expectedCount);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // We have removed the broken GetTvdbMetadataAsync method entirely.
    }
}