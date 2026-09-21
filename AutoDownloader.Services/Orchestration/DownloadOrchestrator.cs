using AutoDownloader.Core;
using AutoDownloader.Services.Scrapers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Orchestration
{
    /// <summary>
    /// Runs one download job end to end: resolve a URL, identify the series, enumerate its
    /// episodes, fetch each one under a correct name, and verify the result.
    ///
    /// This logic used to live in MainWindow's code-behind, which meant it could only ever run
    /// in response to a button click. Nothing here touches WPF: the single interactive step
    /// goes through IUserPrompt, progress leaves through events, and stopping is a
    /// CancellationToken. That is what makes a headless or scheduled run possible without
    /// duplicating the pipeline.
    /// </summary>
    public class DownloadOrchestrator
    {
        private readonly MetadataService _metadataService;
        private readonly SearchService _searchService;
        private readonly XmlService _xmlService;
        private readonly YtDlpService _ytDlpService;
        private readonly IUserPrompt _prompt;
        private readonly UrlMetadataParser _urlParser = new UrlMetadataParser();

        /// <summary>Progress and diagnostics intended for the user-facing log.</summary>
        public event EventHandler<JobLogEventArgs>? OnLog;

        /// <summary>Short status-line updates ("Downloading episode 3 of 12").</summary>
        public event Action<string>? OnStatusChanged;

        /// <summary>Verbose output intended for the developer log only.</summary>
        public event Action<string>? OnDiagnostic;

        /// <summary>Video container extensions counted during verification.</summary>
        private static readonly string[] VideoExtensions =
            { ".mp4", ".mkv", ".webm", ".avi", ".m4v", ".mov" };

        public DownloadOrchestrator(
            MetadataService metadataService,
            SearchService searchService,
            XmlService xmlService,
            YtDlpService ytDlpService,
            IUserPrompt prompt)
        {
            _metadataService = metadataService;
            _searchService = searchService;
            _xmlService = xmlService;
            _ytDlpService = ytDlpService;
            _prompt = prompt;
        }

        private void Log(string message, JobLogLevel level = JobLogLevel.Info)
            => OnLog?.Invoke(this, new JobLogEventArgs(message, level));

        private void Status(string message) => OnStatusChanged?.Invoke(message);

        /// <summary>
        /// Runs one job for a single URL or search term.
        /// </summary>
        public async Task<DownloadJobResult> RunAsync(
            string searchTerm,
            string baseOutputFolder,
            CancellationToken cancellationToken = default)
        {
            var result = new DownloadJobResult();

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                result.FailureReason = "No URL or search term supplied.";
                return result;
            }

            string finalUrl = searchTerm;
            string searchTarget = searchTerm;

            // The Anime/TV Shows/Playlists choice used to be computed and then discarded,
            // because the metadata phase unconditionally rebuilt the path under "TV Shows".
            string categoryFolder = "TV Shows";

            var metadata = new DownloadMetadata { SourceUrl = finalUrl };

            // ---------------------------------------------------------------- 1. Resolve URL
            if (!searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                Log($"--- Starting Gemini web search for: '{searchTarget}' ---");
                Status($"Searching for content: {searchTarget}");

                try
                {
                    var (type, url) = await _searchService.FindShowUrlAsync(searchTarget);
                    if (url == "not-found")
                    {
                        Log($"Could not find a download page for '{searchTarget}'.", JobLogLevel.Error);
                        result.FailureReason = "Smart search found no page.";
                        return result;
                    }

                    finalUrl = url;
                    metadata.SourceUrl = url;
                    categoryFolder = type.Contains("Anime") ? "Anime TV Shows"
                                   : type.Contains("TV Show") ? "TV Shows"
                                   : "Playlists";
                }
                catch (Exception ex)
                {
                    Log($"--- Smart search failed: {ex.Message} ---", JobLogLevel.Error);
                    result.FailureReason = ex.Message;
                    return result;
                }
            }
            else
            {
                Log("--- Direct URL detected. Skipping Gemini search. ---");

                var (parsedName, parsedSeason) = await _urlParser
                    .ParseAsync(searchTerm, cancellationToken).ConfigureAwait(false);

                searchTarget = parsedName;

                if (parsedSeason.HasValue)
                {
                    metadata.NextSeasonNumber = parsedSeason.Value;
                    Log($"Detected Season: {parsedSeason.Value} from URL.", JobLogLevel.Notice);
                }

                // Site-specific scraping, only for hosts that have a dedicated scraper.
                // Everything else goes straight to yt-dlp, which has real extractors for the
                // mainstream targets and expands their playlists natively.
                try
                {
                    var scraper = ScraperFactory.GetScraperForUrl(searchTerm);
                    if (scraper != null)
                    {
                        var playables = await scraper.GetPlayableUrlsAsync(searchTerm);
                        if (playables != null && playables.Count > 0)
                        {
                            metadata.SourceUrls = playables;
                            metadata.SourceUrl = playables[0];
                            Log($"Scraper found {playables.Count} playable link(s); all will be downloaded.", JobLogLevel.Notice);
                        }
                        else
                        {
                            Log("Scraper found no playable links. Letting yt-dlp handle the original URL.", JobLogLevel.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Scraper error: {ex.Message}. Letting yt-dlp handle the original URL.", JobLogLevel.Warning);
                }
            }

            if (cancellationToken.IsCancellationRequested) return Cancelled(result);

            // ---------------------------------------------------------------- 2. Metadata
            Log($"--- Starting metadata lookup for: '{searchTarget}' ---");
            Status($"Looking up official metadata for: {searchTarget}");

            string? confirmedTarget = await _prompt
                .ConfirmShowNameAsync(searchTarget, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(confirmedTarget))
            {
                Log("--- Metadata lookup cancelled by user. Aborting. ---", JobLogLevel.Error);
                return Cancelled(result);
            }

            if (!_metadataService.IsTmdbKeyValid && !_metadataService.IsTvdbKeyValid)
            {
                Log("--- No metadata API key configured. Set one in Edit -> Preferences. ---", JobLogLevel.Error);
                result.FailureReason = "No metadata API key configured.";
                return result;
            }

            Log($"--- Searching TMDB and TVDB for: '{confirmedTarget}' (season {metadata.NextSeasonNumber}) ---");

            MergedSeriesMetadata? merged;
            try
            {
                merged = await _metadataService
                    .GetMergedMetadataAsync(confirmedTarget!, metadata.NextSeasonNumber)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"--- Metadata lookup failed: {ex.Message} ---", JobLogLevel.Error);
                result.FailureReason = ex.Message;
                return result;
            }

            if (merged == null)
            {
                Log($"Could not find official metadata for '{confirmedTarget}'. Download aborted.", JobLogLevel.Error);
                result.FailureReason = "No metadata found.";
                return result;
            }

            Log($"Official Title Found: {merged.OfficialTitle}", JobLogLevel.Notice);
            Log($"Metadata Source: {merged.SourceSummary}", JobLogLevel.Notice);
            Log($"Season {merged.SeasonNumber}: {merged.ExpectedEpisodeCount} expected episode(s), {merged.Episodes.Count} title(s) known.", JobLogLevel.Notice);

            metadata.OfficialTitle = merged.OfficialTitle;
            metadata.SeriesId = merged.TmdbSeriesId ?? merged.TvdbSeriesId;
            metadata.NextSeasonNumber = merged.SeasonNumber;
            metadata.ExpectedEpisodeCount = merged.ExpectedEpisodeCount;
            metadata.Episodes = merged.Episodes;

            result.OfficialTitle = merged.OfficialTitle;
            result.SeasonNumber = merged.SeasonNumber;
            result.ExpectedEpisodeCount = merged.ExpectedEpisodeCount;

            string safeTitle = UrlMetadataParser.SanitizeFileName(merged.OfficialTitle);
            string finalOutputFolder = Path.Combine(baseOutputFolder, categoryFolder, safeTitle);
            Directory.CreateDirectory(finalOutputFolder);
            result.OutputFolder = finalOutputFolder;

            // Write what we know before downloading, so an interrupted run still leaves a
            // usable record of the season.
            Log("--- Saving metadata to series_metadata.xml... ---", JobLogLevel.Notice);
            await _xmlService.SaveMetadataAsync(finalOutputFolder, metadata).ConfigureAwait(false);
            Log("--- Metadata saved. ---", JobLogLevel.Success);

            if (cancellationToken.IsCancellationRequested) return Cancelled(result);

            // ---------------------------------------------------------------- 3. Plan
            List<EpisodeLink> episodeLinks =
                await BuildEpisodePlanAsync(finalUrl, metadata, cancellationToken).ConfigureAwait(false);

            string seasonFolder = Path.Combine(finalOutputFolder, $"Season {metadata.NextSeasonNumber:00}");
            int filesBefore = CountVideoFiles(seasonFolder);

            // ---------------------------------------------------------------- 4. Download
            try
            {
                if (episodeLinks.Count > 0)
                {
                    var (succeeded, failed) = await DownloadEpisodesAsync(
                        episodeLinks, metadata, finalOutputFolder, cancellationToken).ConfigureAwait(false);

                    result.EpisodesSucceeded = succeeded;
                    result.EpisodesFailed = failed;
                }
                else
                {
                    Log("--- No episode list could be built; passing the URL to yt-dlp directly. ---", JobLogLevel.Warning);
                    Status($"Downloading: {metadata.SourceUrl}");
                    await _ytDlpService.DownloadVideoAsync(metadata, finalOutputFolder).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return Cancelled(result);
            }
            catch (Exception ex)
            {
                Log($"--- Download task failed: {ex.Message} ---", JobLogLevel.Error);
                result.FailureReason = ex.Message;
            }

            // ---------------------------------------------------------------- 5. Verify
            int filesAfter = CountVideoFiles(seasonFolder);
            result.FilesPresentAfter = filesAfter;
            result.FilesAdded = filesAfter - filesBefore;

            if (metadata.ExpectedEpisodeCount > 0)
            {
                int missing = metadata.ExpectedEpisodeCount - filesAfter;

                if (missing <= 0)
                {
                    Log($"CONTENT VERIFICATION: SUCCESS! All {metadata.ExpectedEpisodeCount} expected episode(s) are present ({result.FilesAdded} new this run).", JobLogLevel.Success);
                }
                else
                {
                    Log($"CONTENT VERIFICATION: WARNING! {missing} episode(s) missing (found {filesAfter} of {metadata.ExpectedEpisodeCount} expected, {result.FilesAdded} new this run).", JobLogLevel.Warning);
                }
            }
            else
            {
                Log($"CONTENT VERIFICATION: Completed. Downloaded {result.FilesAdded} file(s) this run. (No metadata count available)");
            }

            result.Completed = true;
            result.Cancelled = cancellationToken.IsCancellationRequested;
            return result;
        }

        private static DownloadJobResult Cancelled(DownloadJobResult result)
        {
            result.Cancelled = true;
            return result;
        }

        /// <summary>
        /// Counts finished video files in a season folder.
        /// </summary>
        public static int CountVideoFiles(string folder)
        {
            if (!Directory.Exists(folder)) return 0;

            return Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Count(file => VideoExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));
        }

        /// <summary>
        /// Works out the ordered list of episode pages to download.
        ///
        /// Order of preference:
        ///   1. Ask yt-dlp - it understands well over a thousand sites and expands their
        ///      playlists natively, so when it recognises the page nothing else is needed.
        ///   2. Index the page from static HTML.
        ///   3. Re-index via headless Chromium, for listings built by JavaScript.
        ///
        /// An empty result tells the caller to fall back to handing yt-dlp the whole URL.
        /// </summary>
        public async Task<List<EpisodeLink>> BuildEpisodePlanAsync(
            string seriesUrl,
            DownloadMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            var plan = new List<EpisodeLink>();

            if (string.IsNullOrWhiteSpace(seriesUrl)
                || !seriesUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return plan;
            }

            Status("Asking yt-dlp what this page contains...");
            Log("--- Probing the URL with yt-dlp... ---");

            List<string> probed;
            try
            {
                probed = await _ytDlpService.ProbeEntriesAsync(seriesUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"Probe failed: {ex.Message}", JobLogLevel.Warning);
                probed = new List<string>();
            }

            if (probed.Count > 1)
            {
                Log($"yt-dlp recognised the page as a playlist of {probed.Count} item(s).", JobLogLevel.Success);
                for (int i = 0; i < probed.Count; i++)
                {
                    plan.Add(new EpisodeLink { Url = probed[i], Ordinal = i });
                }
                return AssignEpisodeNumbers(plan, metadata);
            }

            if (cancellationToken.IsCancellationRequested) return plan;

            Log("--- yt-dlp saw no playlist; indexing the page for episode links... ---");
            Status("Indexing episode links...");

            var indexer = new SeriesIndexer();
            indexer.OnLog += line => OnDiagnostic?.Invoke(line);

            List<EpisodeLink> found;
            try
            {
                found = await indexer.IndexAsync(seriesUrl, renderJavaScript: false).ConfigureAwait(false);

                if (found.Count < 2 && !cancellationToken.IsCancellationRequested)
                {
                    Log("--- Few links in the static HTML; retrying with a headless browser... ---", JobLogLevel.Warning);
                    var rendered = await indexer.IndexAsync(seriesUrl, renderJavaScript: true).ConfigureAwait(false);
                    if (rendered.Count > found.Count) found = rendered;
                }
            }
            catch (Exception ex)
            {
                Log($"Indexing failed: {ex.Message}", JobLogLevel.Warning);
                found = new List<EpisodeLink>();
            }

            if (found.Count == 0)
            {
                Log("--- No episode links found on the page. ---", JobLogLevel.Warning);
                return plan;
            }

            Log($"Indexed {found.Count} episode link(s).", JobLogLevel.Success);
            return AssignEpisodeNumbers(found, metadata);
        }

        /// <summary>
        /// Gives every link an episode number: the one parsed from the page when there is one,
        /// otherwise its position. This is what lets a site publishing no metadata at all still
        /// produce correctly numbered, Plex-ready filenames.
        /// </summary>
        public List<EpisodeLink> AssignEpisodeNumbers(List<EpisodeLink> links, DownloadMetadata metadata)
        {
            if (!links.Any(l => l.DetectedEpisodeNumber.HasValue))
            {
                Log("--- No episode numbers on the page; using page order instead. ---", JobLogLevel.Notice);
                for (int i = 0; i < links.Count; i++)
                {
                    links[i].DetectedEpisodeNumber = i + 1;
                }
            }

            // A mismatch usually means the page covers every season at once, or the season
            // number was parsed wrong.
            if (metadata.ExpectedEpisodeCount > 0 && links.Count != metadata.ExpectedEpisodeCount)
            {
                Log($"--- NOTE: the page lists {links.Count} episode(s) but the databases expect "
                    + $"{metadata.ExpectedEpisodeCount} for season {metadata.NextSeasonNumber}. ---",
                    JobLogLevel.Warning);
            }

            return links;
        }

        /// <summary>
        /// Downloads each episode individually, naming it from the merged metadata rather than
        /// from whatever the site happens to expose.
        /// </summary>
        private async Task<(int Succeeded, int Failed)> DownloadEpisodesAsync(
            List<EpisodeLink> links,
            DownloadMetadata metadata,
            string outputFolder,
            CancellationToken cancellationToken)
        {
            var titlesByNumber = metadata.Episodes
                .Where(e => e.EpisodeNumber > 0)
                .GroupBy(e => e.EpisodeNumber)
                .ToDictionary(g => g.Key, g => g.First().EpisodeTitle);

            string showTitle = metadata.OfficialTitle ?? "Unknown Show";
            int succeeded = 0;
            int failed = 0;

            for (int i = 0; i < links.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Log("--- Stopped by the user. ---", JobLogLevel.Error);
                    break;
                }

                var link = links[i];
                int episodeNumber = link.DetectedEpisodeNumber ?? (i + 1);
                titlesByNumber.TryGetValue(episodeNumber, out string? episodeTitle);

                Status($"Downloading {showTitle} S{metadata.NextSeasonNumber:00}E{episodeNumber:00} ({i + 1} of {links.Count})");

                int exitCode = await _ytDlpService.DownloadEpisodeAsync(
                    link.Url,
                    showTitle,
                    metadata.NextSeasonNumber,
                    episodeNumber,
                    episodeTitle,
                    outputFolder).ConfigureAwait(false);

                if (exitCode != 0 && !cancellationToken.IsCancellationRequested)
                {
                    // yt-dlp could not resolve the page. Before giving up, watch what the page
                    // actually requests over the network - a player that builds its stream URL
                    // in JavaScript inside an iframe leaves nothing in the document to find.
                    Log($"--- yt-dlp could not resolve episode {episodeNumber}; watching the page's network activity... ---", JobLogLevel.Warning);

                    string? captured = await CaptureMediaUrlAsync(link.Url).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(captured))
                    {
                        Log($"--- Captured a media stream; retrying episode {episodeNumber}. ---", JobLogLevel.Notice);

                        exitCode = await _ytDlpService.DownloadEpisodeAsync(
                            captured!,
                            showTitle,
                            metadata.NextSeasonNumber,
                            episodeNumber,
                            episodeTitle,
                            outputFolder,
                            referer: link.Url).ConfigureAwait(false);
                    }
                }

                if (exitCode == 0)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    Log($"--- Episode {episodeNumber} failed (exit {exitCode}). Continuing. ---", JobLogLevel.Warning);
                }
            }

            Log($"--- Finished: {succeeded} succeeded, {failed} failed, out of {links.Count}. ---",
                failed == 0 ? JobLogLevel.Success : JobLogLevel.Warning);

            return (succeeded, failed);
        }

        /// <summary>
        /// Loads an episode page in a headless browser and returns the best media URL observed
        /// in its network traffic, or null if none appeared.
        /// </summary>
        private async Task<string?> CaptureMediaUrlAsync(string pageUrl)
        {
            try
            {
                var extractor = new MediaUrlExtractor();
                extractor.OnLog += line => OnDiagnostic?.Invoke(line);

                var urls = await extractor.ExtractMediaUrlsAsync(pageUrl).ConfigureAwait(false);

                if (urls.Count == 0)
                {
                    Log("--- No media requests were observed on that page. ---", JobLogLevel.Warning);
                    return null;
                }

                OnDiagnostic?.Invoke($"MediaUrlExtractor: {urls.Count} candidate(s) for {pageUrl}");
                return urls[0];
            }
            catch (Exception ex)
            {
                Log($"--- Media capture failed: {ex.Message} ---", JobLogLevel.Warning);
                return null;
            }
        }
    }
}
