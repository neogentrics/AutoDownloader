using AutoDownloader.Core; // References AutoDownloader.Core for models (DownloadMetadata)
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections;
using System.Reflection;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;
using TvDbSharper; // The patched client library
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
 private readonly TvDbClient _tvdbClient;

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

 // Initialize TVDB client (Fallback)
 if (IsTvdbKeyValid)
 {
 _tvdbClient = new TvDbClient();
 }
 else
 {
 _tvdbClient = null!;
 }
 }

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

 int targetSeasonNumber =1;
 var seasonOne = fullShow.Seasons?.FirstOrDefault(s => s.SeasonNumber == targetSeasonNumber);

 int expectedCount =1;

 if (seasonOne != null)
 {
 int? epCount = GetIntFromObject(seasonOne, "EpisodeCount", "episode_count", "episodecount");
 if (epCount.HasValue && epCount.Value >0)
 {
 expectedCount = epCount.Value;
 }
 else
 {
 try
 {
 var directVal = seasonOne.EpisodeCount;
 if (directVal != null)
 {
 int v = Convert.ToInt32(directVal);
 if (v >0) expectedCount = v;
 }
 }
 catch { }
 }
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
 /// This implementation uses reflection to adapt to differences in the patched TVDB client API.
 /// It intentionally avoids compile-time dependencies on specific response wrapper types.
 /// </summary>
 public async Task<(string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)?>
 GetTvdbMetadataAsync(string showName)
 {
 if (!IsTvdbKeyValid) return null;
 if (_tvdbClient == null) return null;

 try
 {
 var loginMethod = _tvdbClient.GetType().GetMethod("Login", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
 if (loginMethod != null)
 {
 var loginResult = loginMethod.Invoke(_tvdbClient, new object[] { _tvdbApiKey, string.Empty });
 if (loginResult is Task loginTask)
 {
 await loginTask.ConfigureAwait(false);
 }
 }

 var searchCandidates = new[] { "SearchSeriesByNameAsync", "SearchAsync", "SearchSeriesAsync", "SearchSeriesByName", "Search" };

 object? searchResult = await InvokeClientAsyncMethodIfExists(_tvdbClient, searchCandidates, new object[] { showName });
 if (searchResult == null)
 {
 var searchProp = _tvdbClient.GetType().GetProperty("Search", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
 if (searchProp != null)
 {
 var searchObj = searchProp.GetValue(_tvdbClient);
 if (searchObj != null)
 {
 searchResult = await InvokeClientAsyncMethodIfExists(searchObj, searchCandidates, new object[] { showName });
 }
 }
 }

 if (searchResult == null) return null;

 object? dataCollection = GetPropertyValue(searchResult, "Data", "data", "Results", "results");
 object? firstResult = GetFirstFromEnumerable(dataCollection ?? searchResult);
 if (firstResult == null) return null;

 long? seriesIdLong = GetLongFromObject(firstResult, "Id", "id", "SeriesId", "seriesId");
 if (!seriesIdLong.HasValue) return null;
 int seriesId = (int)seriesIdLong.Value;

 var seriesFetchCandidates = new[] { "GetAsync", "GetSeriesAsync", "GetSeries", "Get", "Series" };
 object? fullShowResponse = await InvokeClientAsyncMethodIfExists(_tvdbClient, seriesFetchCandidates, new object[] { seriesId });
 if (fullShowResponse == null)
 {
 var seriesProp = _tvdbClient.GetType().GetProperty("Series", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
 if (seriesProp != null)
 {
 var seriesObj = seriesProp.GetValue(_tvdbClient);
 if (seriesObj != null)
 {
 fullShowResponse = await InvokeClientAsyncMethodIfExists(seriesObj, seriesFetchCandidates, new object[] { seriesId });
 }
 }
 }

 if (fullShowResponse == null)
 {
 fullShowResponse = GetPropertyValue(firstResult, "Series", "series") ?? firstResult;
 }

 string? officialTitle = GetStringFromObject(fullShowResponse, "Name", "name", "SeriesName", "seriesName", "title");
 if (string.IsNullOrWhiteSpace(officialTitle))
 {
 officialTitle = GetStringFromObject(firstResult, "Name", "name", "SeriesName", "seriesName", "title");
 }

 var seasonsCandidates = new[] { "GetSeasonsAsync", "GetSeasons", "GetSeasonList", "GetAllSeasonsAsync", "GetAllSeasons" };
 object? seasonsResponse = await InvokeClientAsyncMethodIfExists(_tvdbClient, seasonsCandidates, new object[] { seriesId });
 if (seasonsResponse == null)
 {
 var seriesObj = fullShowResponse;
 seasonsResponse = GetPropertyValue(seriesObj, "Seasons", "seasons");
 }

 object? seasonsCollection = GetPropertyValue(seasonsResponse, "Data", "data", "Seasons", "seasons") ?? seasonsResponse;

 int targetSeasonNumber =1;
 int expectedCount =1;

 object? seasonOne = FindSeasonByNumberAndType(seasonsCollection, targetSeasonNumber, "Aired");
 if (seasonOne == null)
 seasonOne = FindSeasonByNumber(seasonsCollection, targetSeasonNumber);

 if (seasonOne != null)
 {
 int? epCount = GetIntFromObject(seasonOne, "EpisodeCount", "episodeCount", "Episodes", "episodes", "EpisodeCountValue");
 if (epCount.HasValue && epCount.Value >0)
 expectedCount = epCount.Value;
 }

 // Attempt to cache episodes from found season
 try
 {
 var episodesList = GetPropertyValue(seasonOne, "Episodes", "episodes") as IEnumerable;
 if (episodesList != null)
 {
 var eps = new List<DownloadEpisode>();
 foreach (var e in episodesList)
 {
 int? num = GetIntFromObject(e, "Number", "number", "EpisodeNumber", "episodeNumber", "EpisodeNumberValue");
 string? title = GetStringFromObject(e, "Name", "name", "EpisodeName", "episodeName", "Title");
 if (num.HasValue)
 {
 eps.Add(new DownloadEpisode { EpisodeNumber = num.Value, EpisodeTitle = title });
 }
 }
 if (eps.Any()) EpisodeCache.StoreEpisodes(seriesId, targetSeasonNumber, eps);
 }
 }
 catch { /* ignore */ }

 if (string.IsNullOrWhiteSpace(officialTitle)) return null;

 return (officialTitle, seriesId, targetSeasonNumber, expectedCount);
 }
 catch (Exception)
 {
 return null;
 }
 }

 /// <summary>
 /// Fetches episodes for a given series and season using TMDB (if available) or TVDB via reflection.
 /// Returns null if episodes could not be retrieved.
 /// </summary>
 public async Task<List<DownloadEpisode>?> GetEpisodesForSeasonAsync(int seriesId, int seasonNumber)
 {
 try
 {
 // Try TMDB first
 if (IsTmdbKeyValid && _tmdbClient != null)
 {
 try
 {
 var seasonDetails = await _tmdbClient.GetTvSeasonAsync(seriesId, seasonNumber);
 if (seasonDetails != null && seasonDetails.Episodes != null)
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
 catch { /* ignore TMDB failures and try TVDB/cache */ }
 }

 // Try cached value
 var cached = EpisodeCache.GetEpisodes(seriesId, seasonNumber);
 if (cached != null) return cached;

 // Try TVDB via reflection (if available)
 if (IsTvdbKeyValid && _tvdbClient != null)
 {
 try
 {
 // Ensure login if required
 var loginMethod = _tvdbClient.GetType().GetMethod("Login", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
 if (loginMethod != null)
 {
 var loginResult = loginMethod.Invoke(_tvdbClient, new object[] { _tvdbApiKey, string.Empty });
 if (loginResult is Task loginTask) await loginTask.ConfigureAwait(false);
 }

 var seasonsCandidates = new[] { "GetSeasonsAsync", "GetSeasons", "GetSeasonList", "GetAllSeasonsAsync", "GetAllSeasons" };
 object? seasonsResponse = await InvokeClientAsyncMethodIfExists(_tvdbClient, seasonsCandidates, new object[] { seriesId });
 if (seasonsResponse == null)
 {
 var seriesObj = await InvokeClientAsyncMethodIfExists(_tvdbClient, new[] { "GetAsync", "GetSeriesAsync", "GetSeries", "Get" }, new object[] { seriesId });
 seasonsResponse = GetPropertyValue(seriesObj, "Seasons", "seasons");
 }

 object? seasonsCollection = GetPropertyValue(seasonsResponse, "Data", "data", "Seasons", "seasons") ?? seasonsResponse;
 var seasonObj = FindSeasonByNumber(seasonsCollection, seasonNumber);
 if (seasonObj != null)
 {
 var episodesList = GetPropertyValue(seasonObj, "Episodes", "episodes") as IEnumerable;
 if (episodesList != null)
 {
 var eps = new List<DownloadEpisode>();
 foreach (var e in episodesList)
 {
 int? num = GetIntFromObject(e, "Number", "number", "EpisodeNumber", "episodeNumber", "EpisodeNumberValue");
 string? title = GetStringFromObject(e, "Name", "name", "EpisodeName", "episodeName", "Title");
 if (num.HasValue)
 {
 eps.Add(new DownloadEpisode { EpisodeNumber = num.Value, EpisodeTitle = title });
 }
 }
 if (eps.Any())
 {
 EpisodeCache.StoreEpisodes(seriesId, seasonNumber, eps);
 return eps;
 }
 }
 }
 }
 catch { /* ignore TVDB reflection errors */ }
 }

 return null;
 }
 catch
 {
 return null;
 }
 }

 // --- Reflection / helper methods ---

 private static async Task<object?> InvokeClientAsyncMethodIfExists(object target, string[] candidateMethodNames, object[] args)
 {
 if (target == null) return null;
 var type = target.GetType();
 foreach (var name in candidateMethodNames)
 {
 MethodInfo? method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
 .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)
 && m.GetParameters().Length == args.Length);
 if (method == null)
 {
 method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
 .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
 }

 if (method == null)
 continue;

 try
 {
 object? invokeResult = method.Invoke(target, args.Length ==0 ? null : args);

 if (invokeResult == null)
 return null;

 if (invokeResult is Task task)
 {
 await task.ConfigureAwait(false);
 var resultProperty = task.GetType().GetProperty("Result");
 if (resultProperty != null)
 {
 return resultProperty.GetValue(task);
 }

 return null;
 }
 else
 {
 return invokeResult;
 }
 }
 catch
 {
 // ignore and try next candidate
 }
 }

 return null;
 }

 private static object? GetFirstFromEnumerable(object? enumerable)
 {
 if (enumerable == null) return null;
 if (enumerable is string) return null;
 if (enumerable is IEnumerable en)
 {
 foreach (var item in en)
 return item;
 }
 return null;
 }

 private static object? GetPropertyValue(object? obj, params string[] propertyNames)
 {
 if (obj == null) return null;
 var type = obj.GetType();

 foreach (var name in propertyNames)
 {
 var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
 if (prop != null)
 {
 try { var val = prop.GetValue(obj); if (val != null) return val; } catch { }
 }

 var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
 if (field != null)
 {
 try { var val = field.GetValue(obj); if (val != null) return val; } catch { }
 }

 if (obj is IDictionary dict && dict.Contains(name))
 {
 try { var val = dict[name]; if (val != null) return val; } catch { }
 }
 }

 return null;
 }

 private static long? GetLongFromObject(object? obj, params string[] names)
 {
 var val = GetPropertyValue(obj, names);
 if (val == null) return null;
 try
 {
 if (val is long l) return l;
 if (val is int i) return (long)i;
 if (val is string s && long.TryParse(s, out var parsed)) return parsed;
 return Convert.ToInt64(val);
 }
 catch { return null; }
 }

 private static int? GetIntFromObject(object? obj, params string[] names)
 {
 var val = GetPropertyValue(obj, names);
 if (val == null) return null;
 try
 {
 if (val is int i) return i;
 if (val is long l) return (int)l;
 if (val is string s && int.TryParse(s, out var parsed)) return parsed;
 return Convert.ToInt32(val);
 }
 catch { return null; }
 }

 private static string? GetStringFromObject(object? obj, params string[] names)
 {
 var val = GetPropertyValue(obj, names);
 if (val == null) return null;
 try { return Convert.ToString(val); } catch { return null; }
 }

 private static object? FindSeasonByNumberAndType(object? seasonsCollection, int number, string typeName)
 {
 if (seasonsCollection == null) return null;
 if (seasonsCollection is IEnumerable en)
 {
 foreach (var item in en)
 {
 var num = GetIntFromObject(item, "Number", "number", "SeasonNumber", "seasonNumber");
 var t = GetPropertyValue(item, "Type", "type");
 string? tName = null;
 if (t != null)
 tName = GetStringFromObject(t, "Name", "name");
 if (num.HasValue && num.Value == number && string.Equals(tName, typeName, StringComparison.OrdinalIgnoreCase))
 return item;
 }
 }
 return null;
 }

 private static object? FindSeasonByNumber(object? seasonsCollection, int number)
 {
 if (seasonsCollection == null) return null;
 if (seasonsCollection is IEnumerable en)
 {
 foreach (var item in en)
 {
 var num = GetIntFromObject(item, "Number", "number", "SeasonNumber", "seasonNumber");
 if (num.HasValue && num.Value == number)
 return item;
 }
 }
 return null;
 }

 /// <summary>
 /// Returns any episodes cached during metadata lookup for the given series and season.
 /// </summary>
 public List<DownloadEpisode>? GetCachedEpisodes(int seriesId, int seasonNumber)
 {
 return EpisodeCache.GetEpisodes(seriesId, seasonNumber);
 }
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