using AutoDownloader.Core; // References AutoDownloader.Core for models (DownloadMetadata)
using System;
using System.Linq;
using System.Threading.Tasks;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;
using System.Collections.Generic;

namespace AutoDownloader.Services // CORRECT: Namespace for the Services project
{
 /// <summary>
 /// Handles all external metadata lookups from TMDB (Primary) and TVDB (Fallback).
 /// This service is initialized by the UI and receives the necessary API keys from SettingsService.
 /// </summary>
 public class MetadataService
 {
 // --- Private Fields ---

 private readonly string _tmdbApiKey;
 private readonly string _tvdbApiKey;
 private readonly TMDbClient _tmdbClient;
 private readonly TvdbMetadataClient _tvdbClient;

 // --- Constructor ---

 /// <summary>
 /// Initializes a new instance of the MetadataService, setting up the API clients.
 /// Clients are only created if the corresponding API key is valid.
 /// </summary>
 public MetadataService(string tmdbApiKey, string tvdbApiKey)
 {
 _tmdbApiKey = tmdbApiKey;
 _tvdbApiKey = tvdbApiKey;

 // Initialize TMDB client (Primary)
 if (IsTmdbKeyValid)
 {
 _tmdbClient = new TMDbClient(_tmdbApiKey);
 }
 else
 {
 _tmdbClient = null!;
 }

 // Initialize TVDB client (second metadata source, merged with TMDB)
 if (IsTvdbKeyValid)
 {
 _tvdbClient = new TvdbMetadataClient(_tvdbApiKey);
 _tvdbClient.OnDiagnostic += message => OnDiagnostic?.Invoke(message);
 }
 else
 {
 _tvdbClient = null!;
 }
 }

 /// <summary>
 /// Raised when a lookup could not be completed, with the reason.
 ///
 /// This exists because the previous TVDB implementation swallowed every failure and
 /// returned null, so a source that was contributing nothing looked identical to one
 /// that simply had no match. Silence is not an acceptable answer from a metadata source.
 /// </summary>
 public event Action<string>? OnDiagnostic;

 // --- Public Properties ---

 /// <summary>
 /// Helper property to check if the TMDB key is valid and usable.
 /// </summary>
 public bool IsTmdbKeyValid => !string.IsNullOrWhiteSpace(_tmdbApiKey) && _tmdbApiKey != "YOUR_TMDB_API_KEY_HERE";

 /// <summary>
 /// Helper property to check if the TVDB key is valid and usable.
 /// </summary>
 public bool IsTvdbKeyValid => !string.IsNullOrWhiteSpace(_tvdbApiKey) && _tvdbApiKey != "YOUR_TVDB_API_KEY_HERE";

 // --- Public Methods ---

 /// <summary>
 /// Searches TMDB for a TV show and returns its metadata.
 /// </summary>
 public async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?>
 GetTmdbMetadataAsync(string showName, int seasonNumber = 1)
 {
 if (!IsTmdbKeyValid) return null;

 try
 {
 SearchContainer<SearchTv> searchResult = await _tmdbClient.SearchTvShowAsync(showName);
 SearchTv? firstResult = searchResult.Results.FirstOrDefault();

 if (firstResult == null) return null;

 TvShow fullShow = await _tmdbClient.GetTvShowAsync(firstResult.Id, TvShowMethods.ExternalIds | TvShowMethods.Credits);
 if (fullShow == null) return null;

 // Look up the season the caller actually asked for. This was hardcoded to 1, so
 // the URL parser could correctly detect "season-2" and the episode count and
 // episode list would still come back describing season 1.
 int targetSeasonNumber = seasonNumber > 0 ? seasonNumber : 1;
 var targetSeason = fullShow.Seasons?.FirstOrDefault(s => s.SeasonNumber == targetSeasonNumber);

 if (targetSeason == null && targetSeasonNumber != 1)
 {
 // The show exists but not that season - fall back rather than reporting nothing.
 targetSeasonNumber = 1;
 targetSeason = fullShow.Seasons?.FirstOrDefault(s => s.SeasonNumber == 1);
 }

 int expectedCount =0;

 if (targetSeason != null && targetSeason.EpisodeCount >0)
 {
 // TMDbLib exposes EpisodeCount as a plain int; the old reflection lookup and the
 // "!= null" check on it were dead code (warning CS0472).
 expectedCount = targetSeason.EpisodeCount;
 }

 // Attempt to cache episodes from TMDB season details
 try
 {
 var seasonDetails = await _tmdbClient.GetTvSeasonAsync(firstResult.Id, targetSeasonNumber);
 if (seasonDetails != null && seasonDetails.Episodes != null)
 {
 var episodes = seasonDetails.Episodes.Select(e => new DownloadEpisode
 {
 EpisodeNumber = e.EpisodeNumber,
 EpisodeTitle = e.Name
 }).ToList();

 EpisodeCache.StoreEpisodes(firstResult.Id, targetSeasonNumber, episodes);

 // Season summaries are sometimes stale; the episode list is authoritative.
 if (expectedCount ==0 && episodes.Count >0) expectedCount = episodes.Count;
 }
 }
 catch { /* ignore */ }

 return (fullShow.Name, firstResult.Id, targetSeasonNumber, expectedCount);
 }
 catch (Exception)
 {
 return null;
 }
 }

 /// <summary>
 /// Searches TVDB for a TV show and returns its metadata.
 ///
 /// This used ~200 lines of reflection that probed for method names at runtime and caught
 /// every exception. It now calls the typed client directly, so failures are reported
 /// rather than indistinguishable from "no match".
 /// </summary>
 public async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?>
 GetTvdbMetadataAsync(string showName, int seasonNumber = 1)
 {
 if (!IsTvdbKeyValid || _tvdbClient == null) return null;

 var found = await _tvdbClient.SearchSeriesAsync(showName).ConfigureAwait(false);
 if (found == null) return null;

 int targetSeasonNumber = seasonNumber > 0 ? seasonNumber : 1;

 var episodes = await _tvdbClient
 .GetSeasonEpisodesAsync(found.Value.SeriesId, targetSeasonNumber)
 .ConfigureAwait(false);

 // The show exists but not that season - fall back rather than reporting nothing.
 if (episodes.Count == 0 && targetSeasonNumber != 1)
 {
 targetSeasonNumber = 1;
 episodes = await _tvdbClient
 .GetSeasonEpisodesAsync(found.Value.SeriesId, 1)
 .ConfigureAwait(false);
 }

 if (episodes.Count > 0)
 {
 EpisodeCache.StoreEpisodes(found.Value.SeriesId, targetSeasonNumber, episodes);
 }

 return (found.Value.Name, found.Value.SeriesId, targetSeasonNumber, episodes.Count);
 }

 /// <summary>
 /// Fetches episodes for a given series and season from TMDB, falling back to the cache
 /// and then to TVDB. Returns null if nothing could be retrieved.
 /// </summary>
 public async Task<List<DownloadEpisode>?> GetEpisodesForSeasonAsync(int seriesId, int seasonNumber)
 {
 // Try TMDB first.
 if (IsTmdbKeyValid && _tmdbClient != null)
 {
 try
 {
 var seasonDetails = await _tmdbClient.GetTvSeasonAsync(seriesId, seasonNumber);
 if (seasonDetails?.Episodes != null && seasonDetails.Episodes.Count > 0)
 {
 var episodes = seasonDetails.Episodes.Select(e => new DownloadEpisode
 {
 EpisodeNumber = e.EpisodeNumber,
 EpisodeTitle = e.Name
 }).ToList();

 EpisodeCache.StoreEpisodes(seriesId, seasonNumber, episodes);
 return episodes;
 }
 }
 catch (Exception ex)
 {
 OnDiagnostic?.Invoke($"TMDB episode lookup for series {seriesId} season {seasonNumber} failed: {ex.Message}");
 }
 }

 var cached = EpisodeCache.GetEpisodes(seriesId, seasonNumber);
 if (cached != null) return cached;

 // Note: the id is only valid against the database it came from, so this is reached
 // only when the caller already resolved the series through TVDB.
 if (IsTvdbKeyValid && _tvdbClient != null)
 {
 var episodes = await _tvdbClient
 .GetSeasonEpisodesAsync(seriesId, seasonNumber)
 .ConfigureAwait(false);

 if (episodes.Count > 0)
 {
 EpisodeCache.StoreEpisodes(seriesId, seasonNumber, episodes);
 return episodes;
 }
 }

 return null;
 }

 /// <summary>
 /// Looks the show up in BOTH TMDB and TVDB and merges the results.
 ///
 /// Neither database is complete on its own - TVDB tends to be better for anime and for
 /// shows with irregular season splits, TMDB for mainstream western TV - so picking one
 /// or the other (as the old "choose a database" dialog forced you to) loses episode
 /// titles for no good reason. Querying both and merging by episode number fills gaps
 /// that either source has on its own.
 ///
 /// Both lookups are best-effort: if one fails or has no key, the other still counts.
 /// </summary>
 /// <returns>
 /// The merged result, or null when NEITHER database could identify the show.
 /// </returns>
 public async Task<MergedSeriesMetadata?> GetMergedMetadataAsync(string showName, int seasonNumber = 1)
 {
 // Run both lookups concurrently; neither depends on the other.
 var tmdbTask = IsTmdbKeyValid
 ? GetTmdbMetadataAsync(showName, seasonNumber)
 : Task.FromResult<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?>(null);

 var tvdbTask = IsTvdbKeyValid
 ? GetTvdbMetadataAsync(showName, seasonNumber)
 : Task.FromResult<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?>(null);

 await Task.WhenAll(tmdbTask, tvdbTask).ConfigureAwait(false);

 var tmdb = tmdbTask.Result;
 var tvdb = tvdbTask.Result;

 if (tmdb == null && tvdb == null) return null;

 var result = new MergedSeriesMetadata
 {
 // Prefer the TMDB title purely for consistency of naming; fall back to TVDB.
 OfficialTitle = tmdb?.OfficialTitle ?? tvdb?.OfficialTitle ?? showName,
 TmdbSeriesId = tmdb?.SeriesId,
 TvdbSeriesId = tvdb?.SeriesId,
 SeasonNumber = tmdb?.TargetSeasonNumber ?? tvdb?.TargetSeasonNumber ?? seasonNumber,
 UsedTmdb = tmdb != null,
 UsedTvdb = tvdb != null,
 };

 // Gather episode lists from whichever sources identified the show.
 List<DownloadEpisode> tmdbEpisodes = new List<DownloadEpisode>();
 List<DownloadEpisode> tvdbEpisodes = new List<DownloadEpisode>();

 if (tmdb != null)
 {
 tmdbEpisodes = GetCachedEpisodes(tmdb.Value.SeriesId, result.SeasonNumber)
 ?? await GetEpisodesForSeasonAsync(tmdb.Value.SeriesId, result.SeasonNumber).ConfigureAwait(false)
 ?? new List<DownloadEpisode>();
 }

 if (tvdb != null)
 {
 tvdbEpisodes = GetCachedEpisodes(tvdb.Value.SeriesId, result.SeasonNumber) ?? new List<DownloadEpisode>();
 }

 // Merge by episode number. TMDB titles win where both have one; TVDB fills the gaps
 // and contributes any episodes TMDB does not list at all.
 var byNumber = new SortedDictionary<int, DownloadEpisode>();

 foreach (var episode in tvdbEpisodes.Where(e => e.EpisodeNumber > 0))
 {
 byNumber[episode.EpisodeNumber] = new DownloadEpisode
 {
 EpisodeNumber = episode.EpisodeNumber,
 EpisodeTitle = episode.EpisodeTitle
 };
 }

 foreach (var episode in tmdbEpisodes.Where(e => e.EpisodeNumber > 0))
 {
 if (byNumber.TryGetValue(episode.EpisodeNumber, out var existing))
 {
 // Keep whichever actually has a title.
 existing.EpisodeTitle = !string.IsNullOrWhiteSpace(episode.EpisodeTitle)
 ? episode.EpisodeTitle
 : existing.EpisodeTitle;
 }
 else
 {
 byNumber[episode.EpisodeNumber] = new DownloadEpisode
 {
 EpisodeNumber = episode.EpisodeNumber,
 EpisodeTitle = episode.EpisodeTitle
 };
 }
 }

 result.Episodes = byNumber.Values.ToList();

 // Expected count: trust the merged list when we have one, else the higher of the two
 // reported counts (a database that lists fewer episodes is usually the stale one).
 result.ExpectedEpisodeCount = result.Episodes.Count > 0
 ? result.Episodes.Count
 : Math.Max(tmdb?.ExpectedEpisodeCount ?? 0, tvdb?.ExpectedEpisodeCount ?? 0);

 return result;
 }

 /// <summary>
 /// Returns any episodes cached during metadata lookup for the given series and season.
 /// </summary>
 public List<DownloadEpisode>? GetCachedEpisodes(int seriesId, int seasonNumber)
 {
 return EpisodeCache.GetEpisodes(seriesId, seasonNumber);
 }
 }

 /// <summary>
 /// The combined view of a series as described by TMDB and TVDB together.
 /// </summary>
 public class MergedSeriesMetadata
 {
 public string OfficialTitle { get; set; } = string.Empty;
 public int? TmdbSeriesId { get; set; }
 public int? TvdbSeriesId { get; set; }
 public int SeasonNumber { get; set; } = 1;
 public int ExpectedEpisodeCount { get; set; }
 public List<DownloadEpisode> Episodes { get; set; } = new List<DownloadEpisode>();

 /// <summary>True when TMDB identified the show.</summary>
 public bool UsedTmdb { get; set; }

 /// <summary>True when TVDB identified the show.</summary>
 public bool UsedTvdb { get; set; }

 /// <summary>Human-readable summary of which databases contributed, for the log.</summary>
 public string SourceSummary =>
 UsedTmdb && UsedTvdb ? "TMDB + TVDB" :
 UsedTmdb ? "TMDB" :
 UsedTvdb ? "TVDB" : "none";
 }

 // Simple in-memory cache to attach episodes discovered during metadata lookup to the series ID + season
 internal static class EpisodeCache
 {
 private static readonly Dictionary<string, List<DownloadEpisode>> _cache = new Dictionary<string, List<DownloadEpisode>>();

 public static void StoreEpisodes(int seriesId, int seasonNumber, List<DownloadEpisode> episodes)
 {
 try
 {
 string key = Key(seriesId, seasonNumber);
 _cache[key] = episodes;
 }
 catch { }
 }

 public static List<DownloadEpisode>? GetEpisodes(int seriesId, int seasonNumber)
 {
 string key = Key(seriesId, seasonNumber);
 if (_cache.TryGetValue(key, out var list)) return list;
 return null;
 }

 private static string Key(int seriesId, int seasonNumber) => $"{seriesId}:{seasonNumber}";
 }
}