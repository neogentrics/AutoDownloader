using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;

// We no longer need TvDbSharper
// using TvDbSharper; 

namespace AutoDownloader.Core
{
    /// <summary>
    /// Service to search The Movie Database (TMDB) and TheTVDB (TVDB) for official metadata.
    /// V1.9: Reworked to use a self-contained HttpClient for TVDB.
    /// </summary>
    public class MetadataService
    {
        // Shared HttpClient
        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly string _tmdbApiKey;
        private readonly string _tvdbApiKey;
        private readonly TMDbClient _tmdbClient;

        // V1.9: TVDB token
        private string _tvdbToken = string.Empty;

        public MetadataService(string tmdbApiKey, string tvdbApiKey)
        {
            _tmdbApiKey = tmdbApiKey;
            _tvdbApiKey = tvdbApiKey;

            if (IsTmdbKeyValid)
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

        public bool IsTmdbKeyValid => !string.IsNullOrWhiteSpace(_tmdbApiKey) && _tmdbApiKey != "YOUR_TMDB_API_KEY_HERE";
        public bool IsTvdbKeyValid => !string.IsNullOrWhiteSpace(_tvdbApiKey) && _tvdbApiKey != "YOUR_TVDB_API_KEY_HERE";


        // This is the new public-facing method that implements your Pop-up Strategy
        public async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?>
            GetMetadataAsync(string showName, Func<string, Task<bool>> confirmTvdbSearch)
        {
            var tmdbResult = await GetTmdbMetadataAsync(showName);
            if (tmdbResult != null)
            {
                return tmdbResult;
            }

            // If TMDB fails, ask the user (your pop-up idea)
            if (IsTvdbKeyValid)
            {
                bool userWantsTvdb = await confirmTvdbSearch(showName);
                if (userWantsTvdb)
                {
                    var tvdbResult = await GetTvdbMetadataAsync(showName);
                    if (tvdbResult != null)
                    {
                        return tvdbResult;
                    }
                }
            }

            return null; // All searches failed or were cancelled
        }


        private async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?> GetTmdbMetadataAsync(string showName)
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

        /// <summary>
        /// V1.9: Reworked to use HttpClient directly, bypassing TvDbSharper.
        /// </summary>
        private async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?> GetTvdbMetadataAsync(string showName)
        {
            if (!IsTvdbKeyValid) return null;

            try
            {
                // 1. Authenticate with TVDB
                if (string.IsNullOrWhiteSpace(_tvdbToken))
                {
                    await LoginToTvdbAsync();
                }

                // 2. Search for the series
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _tvdbToken);
                var searchResponse = await _httpClient.GetAsync($"https://api4.thetvdb.com/v4/search?query={Uri.EscapeDataString(showName)}&type=series");
                if (!searchResponse.IsSuccessStatusCode) return null;

                var searchJson = await searchResponse.Content.ReadAsStringAsync();
                var searchResult = JsonDocument.Parse(searchJson).RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();

                string seriesTvdbId = searchResult.GetProperty("tvdb_id").GetString() ?? "";
                string seriesName = searchResult.GetProperty("name").GetString() ?? showName;

                if (string.IsNullOrWhiteSpace(seriesTvdbId)) return null;

                // 3. Get series data (for season list)
                var seriesResponse = await _httpClient.GetAsync($"https://api4.thetvdb.com/v4/series/{seriesTvdbId}/extended");
                if (!seriesResponse.IsSuccessStatusCode) return null;

                var seriesJson = await seriesResponse.Content.ReadAsStringAsync();
                var seriesData = JsonDocument.Parse(seriesJson).RootElement.GetProperty("data");

                // 4. Find Season 1
                var seasonOne = seriesData.GetProperty("seasons").EnumerateArray()
                    .FirstOrDefault(s => s.GetProperty("type").GetProperty("name").GetString() == "Aired" && s.GetProperty("number").GetInt32() == 1);

                int expectedCount = 1;
                if (seasonOne.TryGetProperty("id", out var seasonIdElement))
                {
                    long seasonId = seasonIdElement.GetInt64();
                    var episodesResponse = await _httpClient.GetAsync($"https://api4.thetvdb.com/v4/seasons/{seasonId}/episodes/default");
                    if (episodesResponse.IsSuccessStatusCode)
                    {
                        var episodesJson = await episodesResponse.Content.ReadAsStringAsync();
                        expectedCount = JsonDocument.Parse(episodesJson).RootElement.GetProperty("data").GetProperty("episodes").EnumerateArray().Count();
                    }
                }

                if (expectedCount == 0) expectedCount = 1;

                int.TryParse(seriesTvdbId, out int seriesIdInt);
                return (seriesName, seriesIdInt, 1, expectedCount);
            }
            catch (Exception)
            {
                _tvdbToken = string.Empty; // Token might be bad, clear it.
                return null; // Soft fail
            }
        }

        private async Task LoginToTvdbAsync()
        {
            var authPayload = new
            {
                apikey = _tvdbApiKey
            };
            var content = new StringContent(JsonSerializer.Serialize(authPayload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://api4.thetvdb.com/v4/login", content);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            _tvdbToken = JsonDocument.Parse(json).RootElement.GetProperty("data").GetProperty("token").GetString() ?? "";
        }

        // We have removed the broken GetTvdbMetadataAsync method entirely.
    }
}