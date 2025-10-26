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
        // Value: 20ef6274c78ab5a18d77d2cc5d78b7f5
        private const string TmdbApiKey = "20ef6274c78ab5a18d77d2cc5d78b7f5";

        private readonly TMDbClient _client;

        /// <summary>
        /// Initializes the TMDB client.
        /// </summary>
        public MetadataService()
        {
            // TMDbLib automatically handles API access with the key.
            _client = new TMDbClient(TmdbApiKey);
        }

        /// <summary>
        /// Searches for a TV show by name and finds the next season to download.
        /// </summary>
        /// <param name="showName">The name of the show to search for.</param>
        /// <returns>A tuple containing (OfficialTitle, SeriesId, NextSeasonNumber). Returns null on failure.</returns>
        public async Task<(string OfficialTitle, int SeriesId, int NextSeasonNumber)?> FindShowMetadataAsync(string showName)
        {
            if (string.IsNullOrWhiteSpace(showName)) return null;

            // 1. Search for the show
            SearchContainer<SearchTv> searchResult = await _client.SearchTvShowAsync(showName);
            SearchTv? firstResult = searchResult.Results.FirstOrDefault();

            if (firstResult == null)
            {
                return null;
            }

            // 2. Get the full details of the show using its ID
            // We request seasons data using the TvShowMethods enum.
            TvShow fullShow = await _client.GetTvShowAsync(firstResult.Id, TvShowMethods.ExternalIds | TvShowMethods.Credits);

            if (fullShow == null)
            {
                return null;
            }

            // 3. Find the next season to potentially download. 
            // This is a placeholder assumption: we assume the user is looking for the highest
            // numbered season + 1. In future steps, we will use this to refine the URL search.
            int nextSeason = 1;

            if (fullShow.Seasons != null && fullShow.Seasons.Any())
            {
                // Find the highest season number (excluding specials, often season 0)
                int maxSeason = fullShow.Seasons
                    .Where(s => s.SeasonNumber > 0)
                    .Max(s => s.SeasonNumber);

                // We assume the user wants the next season after the highest numbered one found.
                nextSeason = maxSeason + 1;
            }

            return (fullShow.Name, fullShow.Id, nextSeason);
        }
    }
}