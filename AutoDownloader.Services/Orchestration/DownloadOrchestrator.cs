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

        /// <summary>The cookie source in force, checked once before downloading.</summary>
        public string? CookieSource { get; set; }
        private readonly UrlMetadataParser _urlParser = new UrlMetadataParser();

        /// <summary>Progress and diagnostics intended for the user-facing log.</summary>
        public event EventHandler<JobLogEventArgs>? OnLog;

        /// <summary>Short status-line updates ("Downloading episode 3 of 12").</summary>
        public event Action<string>? OnStatusChanged;

        /// <summary>Verbose output intended for the developer log only.</summary>
        public event Action<string>? OnDiagnostic;

        /// <summary>Progress of the current file and of the job as a whole.</summary>
        public event Action<JobProgress>? OnProgress;

        /// <summary>
        /// Set when the page turns out to be a site yt-dlp has no extractor for and nothing
        /// embedded could be found. Handing the URL to yt-dlp again would fail identically,
        /// so the caller skips that second attempt.
        /// </summary>
        private bool _sourceUnsupported;

        /// <summary>
        /// Set once the user answers "all" to an overwrite question, so a season does not ask
        /// the same thing twelve times. Reset at the start of every job.
        /// </summary>
        private bool _overwriteAll;
        private bool _skipAll;

        /// <summary>
        /// When true, existing files are replaced without asking. Set by --overwrite.
        /// </summary>
        public bool AlwaysOverwrite { get; set; }

        /// <summary>
        /// Below this, a file on disk is a stub rather than an episode - enough to tell a
        /// finished download from a truncated one, without ruling out genuinely short videos.
        /// </summary>
        private const long MinimumUsableFileBytes = 100 * 1024;

        /// <summary>Tracks which episode progress readings belong to.</summary>
        private int _currentEpisodeIndex;
        private int _currentEpisodeCount;
        private string? _currentEpisodeLabel;

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

            // Surface metadata problems instead of letting a source fail silently. This is
            // the visibility half of the TVDB fix: a database that contributes nothing should
            // say why, not look identical to one that simply had no match.
            _metadataService.OnDiagnostic += message => Log(message, JobLogLevel.Warning);

            // Re-raise file progress with the episode context the UI needs to show
            // "episode 3 of 12" alongside the bar.
            _ytDlpService.OnProgress += fileProgress =>
            {
                OnProgress?.Invoke(new JobProgress
                {
                    File = fileProgress,
                    EpisodeIndex = _currentEpisodeIndex,
                    EpisodeCount = _currentEpisodeCount,
                    EpisodeLabel = _currentEpisodeLabel,
                });
            };
        }

        private void Log(string message, JobLogLevel level = JobLogLevel.Info)
            => OnLog?.Invoke(this, new JobLogEventArgs(message, level));

        private void Status(string message) => OnStatusChanged?.Invoke(message);

        /// <summary>
        /// Runs one job for a single URL or search term.
        /// </summary>
        /// <param name="seasonOverride">
        /// Forces a season number instead of parsing one out of the URL. Useful when a site's
        /// URL carries no season, or carries the wrong one.
        /// </param>
        public async Task<DownloadJobResult> RunAsync(
            string searchTerm,
            string baseOutputFolder,
            CancellationToken cancellationToken = default,
            int? seasonOverride = null)
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

            _overwriteAll = AlwaysOverwrite;
            _skipAll = false;

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

            // An explicit season always wins over whatever the URL implied.
            if (seasonOverride.HasValue && seasonOverride.Value > 0)
            {
                metadata.NextSeasonNumber = seasonOverride.Value;
                Log($"Using season {seasonOverride.Value} (given explicitly).", JobLogLevel.Notice);
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

            // Resolve the show, then fall back to simpler forms of the name, and finally ask
            // for a different one.
            //
            // Names taken from a page title carry qualifiers the databases do not have:
            // "Megaman Star Force Anime" returns nothing at all while "Megaman Star Force"
            // returns the show. Aborting the whole download over a trailing word - after a
            // dialog that auto-confirmed - was a poor way to spend the user's time.
            MergedSeriesMetadata? merged = null;
            string attemptName = confirmedTarget!;
            int? preferredYear = null;

            for (int attempt = 0; attempt < 3 && merged == null; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) return Cancelled(result);

                if (attempt > 0)
                {
                    Log($"--- Nothing matched '{attemptName}'. Asking for a different name. ---",
                        JobLogLevel.Warning);

                    string? retry = await _prompt
                        .ConfirmShowNameAsync(attemptName, cancellationToken)
                        .ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(retry))
                    {
                        Log("--- Cancelled. ---", JobLogLevel.Error);
                        return Cancelled(result);
                    }

                    // Unchanged means there is nothing new to try - and it is also how a
                    // non-interactive prompt answers, so this is what stops the loop.
                    if (string.Equals(retry.Trim(), attemptName, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    attemptName = retry.Trim();
                }

                var resolved = await TryResolveSeriesAsync(
                    attemptName, metadata.NextSeasonNumber, cancellationToken).ConfigureAwait(false);

                if (resolved.Cancelled) return Cancelled(result);

                merged = resolved.Merged;
                preferredYear = resolved.Year;

                if (merged != null) confirmedTarget = resolved.MatchedName ?? attemptName;
            }

            if (merged == null)
            {
                Log($"Could not find official metadata for '{attemptName}'. Download aborted.", JobLogLevel.Error);
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

            // Check the cookie source once, here, rather than discovering the problem one
            // episode at a time after the downloads have already started.
            string? cookieWarning = CookieSourceSpec.GetPreflightWarning(CookieSource);
            if (cookieWarning != null)
            {
                Log($"--- COOKIE WARNING: {cookieWarning} ---", JobLogLevel.Error);
            }

            string seasonFolder = Path.Combine(finalOutputFolder, $"Season {metadata.NextSeasonNumber:00}");
            int filesBefore = CountVideoFiles(seasonFolder);

            // ---------------------------------------------------------------- 4. Download
            try
            {
                if (episodeLinks.Count > 0)
                {
                    var (succeeded, failed, protectedCount, alreadyPresent) = await DownloadEpisodesAsync(
                        episodeLinks, metadata, finalOutputFolder, cancellationToken).ConfigureAwait(false);

                    result.EpisodesSucceeded = succeeded;
                    result.EpisodesFailed = failed;
                    result.EpisodesProtected = protectedCount;
                    result.EpisodesAlreadyPresent = alreadyPresent;
                }
                else if (_sourceUnsupported)
                {
                    // Already established that yt-dlp cannot read this site. Trying again
                    // would produce the same error a second time and read like a bug.
                    result.FailureReason = "The source site is not supported.";
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
            result.EpisodesOffered = episodeLinks.Count;

            ReportVerification(result, metadata.NextSeasonNumber);

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
        /// Looks a show up, trying progressively simpler forms of the name.
        ///
        /// Returns as soon as one form resolves. Cancelled is set when the user declined to
        /// choose between candidates, which is a deliberate stop rather than a failure.
        /// </summary>
        /// <summary>
        /// The outcome of working out what one batch item actually is, before anything is
        /// downloaded.
        /// </summary>
        public class IdentityResolution
        {
            public string SearchTerm { get; set; } = string.Empty;

            /// <summary>The show as the databases know it, once the user has confirmed it.</summary>
            public string? ResolvedTitle { get; set; }

            public int? Year { get; set; }

            public int Season { get; set; } = 1;

            public bool Cancelled { get; set; }

            /// <summary>Set when the item could not be identified; it is still downloadable.</summary>
            public string? Problem { get; set; }

            public string Display => ResolvedTitle == null
                ? SearchTerm
                : Year.HasValue ? $"{ResolvedTitle} ({Year})" : ResolvedTitle;
        }

        /// <summary>
        /// Works out which show a batch item is, asking whatever needs asking, without
        /// downloading anything.
        ///
        /// Running this over the whole batch first means every question is asked while the
        /// user is still at the keyboard, instead of one arriving forty minutes into a run
        /// that then sits waiting. The answers are recorded by the prompt this orchestrator
        /// was given, so the download pass finds them already answered.
        ///
        /// An item that cannot be identified is not an error here. It is reported and left
        /// alone, because the download may still work and the metadata step will try again.
        /// </summary>
        public async Task<IdentityResolution> ResolveIdentityAsync(
            string searchTerm,
            CancellationToken cancellationToken = default,
            int? seasonOverride = null)
        {
            var resolution = new IdentityResolution { SearchTerm = searchTerm };

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                resolution.Problem = "Empty entry.";
                return resolution;
            }

            string searchTarget = searchTerm;

            if (searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // Reading the page for its title is the expensive half of identifying a URL,
                // and it is exactly the "index it first" the batch is asking for.
                var (parsedName, parsedSeason) = await _urlParser
                    .ParseAsync(searchTerm, cancellationToken).ConfigureAwait(false);

                searchTarget = parsedName;
                if (parsedSeason.HasValue) resolution.Season = parsedSeason.Value;
            }

            if (seasonOverride.HasValue && seasonOverride.Value > 0)
            {
                resolution.Season = seasonOverride.Value;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                resolution.Cancelled = true;
                return resolution;
            }

            string? confirmed = await _prompt
                .ConfirmShowNameAsync(searchTarget, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(confirmed))
            {
                resolution.Cancelled = true;
                return resolution;
            }

            if (!_metadataService.IsTmdbKeyValid && !_metadataService.IsTvdbKeyValid)
            {
                resolution.Problem = "No metadata API key configured.";
                return resolution;
            }

            var resolved = await TryResolveSeriesAsync(
                confirmed!, resolution.Season, cancellationToken).ConfigureAwait(false);

            if (resolved.Cancelled)
            {
                resolution.Cancelled = true;
                return resolution;
            }

            if (resolved.Merged == null)
            {
                resolution.Problem = $"No database match for '{confirmed}'.";
                return resolution;
            }

            resolution.ResolvedTitle = resolved.MatchedName ?? confirmed;
            resolution.Year = resolved.Year;
            return resolution;
        }

        private async Task<(MergedSeriesMetadata? Merged, int? Year, string? MatchedName, bool Cancelled)>
            TryResolveSeriesAsync(string searchTerm, int seasonNumber, CancellationToken cancellationToken)
        {
            foreach (var variant in SearchTermVariants.Generate(searchTerm))
            {
                if (cancellationToken.IsCancellationRequested) return (null, null, null, true);

                if (!string.Equals(variant, searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"--- Retrying without qualifiers: '{variant}' ---", JobLogLevel.Notice);
                }
                else
                {
                    Log($"--- Searching TMDB and TVDB for: '{variant}' (season {seasonNumber}) ---");
                }

                string resolvedName = variant;
                int? preferredYear = null;

                try
                {
                    var candidates = await _metadataService
                        .SearchCandidatesAsync(variant)
                        .ConfigureAwait(false);

                    if (candidates.Count == 0) continue;

                    // A name can be exactly right and still match both a show and its reboot.
                    var selection = SeriesSelector.Choose(variant, candidates);

                    if (selection.Kind == SeriesSelectionKind.Ambiguous && selection.Candidates.Count > 1)
                    {
                        Log($"--- {selection.Reason}. Asking which one. ---", JobLogLevel.Notice);

                        var chosen = await _prompt
                            .ChooseSeriesAsync(variant, selection.Candidates, selection.Selected, cancellationToken)
                            .ConfigureAwait(false);

                        if (chosen == null)
                        {
                            Log("--- No show chosen. Aborting. ---", JobLogLevel.Error);
                            return (null, null, null, true);
                        }

                        preferredYear = chosen.Year;
                        resolvedName = chosen.Title;
                        Log($"Using: {chosen.Display} (via {chosen.Source})", JobLogLevel.Notice);
                    }
                    else if (selection.Selected != null)
                    {
                        preferredYear = selection.Selected.Year;
                        resolvedName = selection.Selected.Title;
                        Log($"Matched: {selection.Selected.Display} ({selection.Reason})", JobLogLevel.Notice);
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal: the direct lookup below may still succeed.
                    Log($"Could not list candidate shows: {ex.Message}", JobLogLevel.Warning);
                }

                try
                {
                    var merged = await _metadataService
                        .GetMergedMetadataAsync(resolvedName, seasonNumber, preferredYear)
                        .ConfigureAwait(false);

                    if (merged != null) return (merged, preferredYear, resolvedName, false);
                }
                catch (Exception ex)
                {
                    Log($"--- Metadata lookup failed: {ex.Message} ---", JobLogLevel.Warning);
                }
            }

            return (null, null, null, false);
        }

        /// <summary>
        /// Reports what was obtained, separating what this source could give from what the
        /// databases say the season contains.
        ///
        /// These legitimately disagree. A source may hold a partial upload, one cour of a
        /// longer run, or a season the databases count differently - one real case listed 9
        /// episodes where TMDB expected 26. Measuring only against the database made a
        /// complete download of everything available report seventeen episodes "missing",
        /// which reads as the app failing rather than the source being incomplete.
        /// </summary>
        private void ReportVerification(DownloadJobResult result, int seasonNumber)
        {
            int offered = result.EpisodesOffered;
            int expected = result.ExpectedEpisodeCount;
            int present = result.FilesPresentAfter;

            // Nothing at all arrived, and every episode was protected: that is the reason.
            if (result.EpisodesProtected > 0 && result.EpisodesSucceeded == 0)
            {
                Log($"CONTENT VERIFICATION: {result.EpisodesProtected} episode(s) are DRM protected "
                    + "and cannot be downloaded from this source.", JobLogLevel.Warning);
                return;
            }

            if (offered > 0)
            {
                int missingFromSource = offered - present;

                if (missingFromSource <= 0)
                {
                    Log($"CONTENT VERIFICATION: SUCCESS! All {offered} episode(s) this source offers "
                        + $"are present ({result.FilesAdded} new this run).", JobLogLevel.Success);
                }
                else
                {
                    Log($"CONTENT VERIFICATION: {present} of the {offered} episode(s) this source offers "
                        + $"are present; {missingFromSource} did not download ({result.FilesAdded} new this run).",
                        JobLogLevel.Warning);
                }

                // Say separately whether the source itself is short of the full season, so a
                // gap in the source is not mistaken for a gap in the download.
                if (expected > offered)
                {
                    Log($"--- NOTE: the databases list {expected} episode(s) for season {seasonNumber}, "
                        + $"so this source carries {expected - offered} fewer. That is a limit of the "
                        + "source, not a failed download. ---", JobLogLevel.Notice);
                }
                else if (expected > 0 && offered > expected)
                {
                    Log($"--- NOTE: this source lists {offered} episode(s) but the databases expect "
                        + $"{expected} for season {seasonNumber}; it may include specials or "
                        + "another season. ---", JobLogLevel.Notice);
                }

                return;
            }

            // No episode list was built, so the database count is all there is to go on.
            if (expected > 0)
            {
                int missing = expected - present;

                if (missing <= 0)
                {
                    Log($"CONTENT VERIFICATION: SUCCESS! All {expected} expected episode(s) are present "
                        + $"({result.FilesAdded} new this run).", JobLogLevel.Success);
                }
                else
                {
                    Log($"CONTENT VERIFICATION: WARNING! {missing} episode(s) missing (found {present} of "
                        + $"{expected} expected, {result.FilesAdded} new this run).", JobLogLevel.Warning);
                }

                return;
            }

            Log($"CONTENT VERIFICATION: Completed. Downloaded {result.FilesAdded} file(s) this run. "
                + "(No episode count available)");
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

            _sourceUnsupported = false;
            bool probeSaidUnsupported = false;

            void WatchForUnsupported(string line)
            {
                if (SupportedSiteChecker.IsUnsupportedUrlMessage(line)) probeSaidUnsupported = true;
            }

            _ytDlpService.OnOutputReceived += WatchForUnsupported;

            List<EpisodeLink> probed;
            try
            {
                probed = await _ytDlpService.ProbeEntriesAsync(seriesUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"Probe failed: {ex.Message}", JobLogLevel.Warning);
                probed = new List<EpisodeLink>();
            }
            finally
            {
                _ytDlpService.OnOutputReceived -= WatchForUnsupported;
            }

            if (probed.Count > 1)
            {
                Log($"yt-dlp recognised the page as a playlist of {probed.Count} item(s).", JobLogLevel.Success);
                return AssignEpisodeNumbers(probed, metadata);
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

                // The page may not host the video at all - plenty of sites simply frame a
                // player from somewhere yt-dlp already supports.
                Log("--- Looking for embedded players from supported platforms... ---");

                List<EmbeddedPlayer> embeds;
                try
                {
                    embeds = await indexer.FindEmbeddedPlayersAsync(seriesUrl, renderJavaScript: true)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"Embed search failed: {ex.Message}", JobLogLevel.Warning);
                    embeds = new List<EmbeddedPlayer>();
                }

                if (embeds.Count > 0)
                {
                    Log($"Found {embeds.Count} embedded video(s) on: "
                        + string.Join(", ", embeds.Select(e => e.Platform).Distinct()) + ".",
                        JobLogLevel.Success);

                    for (int i = 0; i < embeds.Count; i++)
                    {
                        plan.Add(new EpisodeLink { Url = embeds[i].Url, Ordinal = i, LinkText = embeds[i].Platform });
                    }

                    return AssignEpisodeNumbers(plan, metadata);
                }

                await ExplainUnsupportedSourceAsync(seriesUrl, probeSaidUnsupported, cancellationToken)
                    .ConfigureAwait(false);

                return plan;
            }

            Log($"Indexed {found.Count} episode link(s).", JobLogLevel.Success);
            return AssignEpisodeNumbers(found, metadata);
        }

        /// <summary>
        /// Says, once and clearly, why nothing could be downloaded from a site - instead of
        /// handing the URL back to yt-dlp for a second identical failure.
        /// </summary>
        private async Task ExplainUnsupportedSourceAsync(
            string seriesUrl, bool probeSaidUnsupported, CancellationToken cancellationToken)
        {
            bool likelySupported = true;

            try
            {
                var checker = new SupportedSiteChecker(_ytDlpService.YtDlpPath);
                checker.OnDiagnostic += message => OnDiagnostic?.Invoke(message);

                likelySupported = await checker.IsLikelySupportedAsync(seriesUrl, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch { /* the explanation is a courtesy; never fail because of it */ }

            if (probeSaidUnsupported || !likelySupported)
            {
                string host;
                try { host = new Uri(seriesUrl).Host; } catch { host = seriesUrl; }

                _sourceUnsupported = true;

                // Say only what was actually established. An earlier version asserted that
                // the page built its list in JavaScript, which was a guess - and wrong at least
                // once, where the links were ordinary anchors the indexer had failed to
                // recognise. Reporting a cause that was never checked sends the next person
                // looking in the wrong place.
                Log($"--- {host} is not one of the ~1,750 sites yt-dlp has an extractor for, "
                    + "no episode links could be identified on the page, and it embeds no player "
                    + "from a supported platform. There is nothing further to try here. ---",
                    JobLogLevel.Error);

                Log("--- Sources that do work include YouTube, Vimeo, Dailymotion, the Internet "
                    + "Archive, and any page embedding a player from one of them. ---",
                    JobLogLevel.Notice);
            }
        }

        /// <summary>
        /// Gives every link an episode number: the one parsed from the page when there is one,
        /// otherwise its position. This is what lets a site publishing no metadata at all still
        /// produce correctly numbered, Plex-ready filenames.
        /// </summary>
        public List<EpisodeLink> AssignEpisodeNumbers(List<EpisodeLink> links, DownloadMetadata metadata)
        {
            int targetSeason = metadata.NextSeasonNumber;

            // When the links say which season they belong to, keep only the one asked for.
            // A listing that covers every season at once would otherwise be flattened into
            // the target season - five seasons of a show arriving as S01E01 to S01E65.
            var withSeason = links.Where(l => l.DetectedSeasonNumber.HasValue).ToList();

            if (withSeason.Count > 0)
            {
                var thisSeason = withSeason.Where(l => l.DetectedSeasonNumber == targetSeason).ToList();

                if (thisSeason.Count > 0)
                {
                    if (thisSeason.Count < links.Count)
                    {
                        var seasons = withSeason.Select(l => l.DetectedSeasonNumber!.Value)
                            .Distinct().OrderBy(n => n).ToList();

                        Log($"--- The page covers season(s) {string.Join(", ", seasons)}; "
                            + $"keeping the {thisSeason.Count} link(s) for season {targetSeason}. ---",
                            JobLogLevel.Notice);
                    }

                    links = thisSeason;
                }
                else
                {
                    Log($"--- WARNING: no link on the page belongs to season {targetSeason}. "
                        + "Continuing with everything found, which may be misnumbered. ---",
                        JobLogLevel.Warning);
                }
            }

            // Before falling back to position, see whether the links say what they are. A
            // listing is often ordered by date or alphabetically, so pairing page order with
            // database order writes the wrong title onto nearly every file.
            if (!links.Any(l => l.DetectedEpisodeNumber.HasValue) && metadata.Episodes.Count > 0)
            {
                var match = EpisodeTitleMatcher.Apply(links, metadata.Episodes);

                if (match.Applied)
                {
                    Log($"--- Matched {match.Matched} of {match.Total} link(s) to episodes by title. ---",
                        JobLogLevel.Notice);

                    if (match.Unmatched.Count > 0)
                    {
                        Log($"--- {match.Unmatched.Count} link(s) matched no known episode and were "
                            + "placed after the last one. ---", JobLogLevel.Warning);
                    }
                }
                else if (match.Matched > 0)
                {
                    Log($"--- Only {match.Matched} of {match.Total} link(s) matched an episode title, "
                        + "too few to trust. Falling back to page order. ---", JobLogLevel.Warning);
                }
            }

            if (!links.Any(l => l.DetectedEpisodeNumber.HasValue))
            {
                // Position is the only ordering signal left. That is fine for a single season
                // and wrong for anything else, so say so rather than quietly guessing.
                if (metadata.ExpectedEpisodeCount > 0 && links.Count > metadata.ExpectedEpisodeCount)
                {
                    Log($"--- WARNING: {links.Count} link(s) found but season {targetSeason} has "
                        + $"{metadata.ExpectedEpisodeCount} episode(s), and the page gives no episode "
                        + "numbers. Numbering by position is unlikely to be correct. ---",
                        JobLogLevel.Error);
                }
                else
                {
                    Log("--- No episode numbers on the page; using page order instead. ---", JobLogLevel.Notice);
                }

                for (int i = 0; i < links.Count; i++)
                {
                    links[i].DetectedEpisodeNumber = i + 1;
                }
            }
            else if (metadata.ExpectedEpisodeCount > 0 && links.Count != metadata.ExpectedEpisodeCount)
            {
                Log($"--- NOTE: the page lists {links.Count} episode(s) but the databases expect "
                    + $"{metadata.ExpectedEpisodeCount} for season {targetSeason}. ---",
                    JobLogLevel.Warning);
            }

            return links.OrderBy(l => l.DetectedEpisodeNumber ?? int.MaxValue).ToList();
        }

        /// <summary>
        /// Downloads each episode individually, naming it from the merged metadata rather than
        /// from whatever the site happens to expose.
        /// </summary>
        private async Task<(int Succeeded, int Failed, int Protected, int AlreadyPresent)> DownloadEpisodesAsync(
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
            int protectedCount = 0;
            int alreadyPresent = 0;

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

                _currentEpisodeIndex = i + 1;
                _currentEpisodeCount = links.Count;
                _currentEpisodeLabel = $"S{metadata.NextSeasonNumber:00}E{episodeNumber:00}";

                // Ask before replacing anything already on disk. Previously an existing file
                // was skipped silently, which is a sensible default and a poor answer when the
                // existing file is precisely the one you wanted to replace.
                bool forceOverwrite = false;

                var existing = YtDlpService.FindExistingEpisode(
                    showTitle, metadata.NextSeasonNumber, episodeNumber, episodeTitle, outputFolder);

                if (existing != null)
                {
                    if (_skipAll)
                    {
                        alreadyPresent++;
                        Log($"--- {_currentEpisodeLabel}: already present, keeping it. ---", JobLogLevel.Notice);
                        continue;
                    }

                    if (_overwriteAll)
                    {
                        forceOverwrite = true;
                    }
                    else
                    {
                        var decision = await _prompt
                            .ConfirmOverwriteAsync(existing, cancellationToken)
                            .ConfigureAwait(false);

                        switch (decision)
                        {
                            case OverwriteDecision.Cancel:
                                Log("--- Stopped by the user. ---", JobLogLevel.Error);
                                return (succeeded, failed, protectedCount, alreadyPresent);

                            case OverwriteDecision.SkipAll:
                                _skipAll = true;
                                goto case OverwriteDecision.Skip;

                            case OverwriteDecision.Skip:
                                alreadyPresent++;
                                Log($"--- {_currentEpisodeLabel}: already present, keeping it. ---", JobLogLevel.Notice);
                                continue;

                            case OverwriteDecision.OverwriteAll:
                                _overwriteAll = true;
                                goto case OverwriteDecision.Overwrite;

                            case OverwriteDecision.Overwrite:
                                forceOverwrite = true;
                                Log($"--- {_currentEpisodeLabel}: replacing the existing file "
                                    + $"({existing.SizeDisplay}). ---", JobLogLevel.Notice);
                                break;
                        }
                    }
                }

                Status($"Downloading {showTitle} {_currentEpisodeLabel} ({i + 1} of {links.Count})");

                var outcome = await _ytDlpService.DownloadEpisodeAsync(
                    link.Url,
                    showTitle,
                    metadata.NextSeasonNumber,
                    episodeNumber,
                    episodeTitle,
                    outputFolder,
                    referer: null,
                    forceOverwrite: forceOverwrite).ConfigureAwait(false);

                // Protected content is encrypted at source. Watching the page's network
                // traffic cannot recover it, so skip the fallback rather than spending
                // twelve seconds per episode proving that again.
                if (outcome.DrmProtected)
                {
                    protectedCount++;
                    Log($"--- {_currentEpisodeLabel}: skipped - this video is DRM protected and cannot be downloaded. ---",
                        JobLogLevel.Warning);
                    continue;
                }

                int exitCode = outcome.ExitCode;

                // yt-dlp exits non-zero when a step after the download fails - writing
                // metadata, renaming its temp file - even though the video itself is already
                // downloaded, merged and playable. A locked file is enough to cause it, and
                // antivirus or an open Explorer preview pane will do that.
                //
                // Treating that as "could not resolve the page" sent a finished episode round
                // the network-capture fallback, which fetched the same episode by another
                // route and left junk beside the good file. So before falling back, look at
                // whether the download actually produced something.
                if (exitCode != 0 && !cancellationToken.IsCancellationRequested)
                {
                    var produced = YtDlpService.FindExistingEpisode(
                        showTitle, metadata.NextSeasonNumber, episodeNumber, episodeTitle, outputFolder);

                    // It has to be a file this run produced. An untouched file left over from
                    // an earlier run says nothing about whether this attempt worked.
                    bool isFromThisRun = produced != null
                        && produced.SizeBytes > MinimumUsableFileBytes
                        && (existing == null
                            || produced.LastModified != existing.LastModified
                            || produced.SizeBytes != existing.SizeBytes);

                    if (isFromThisRun)
                    {
                        succeeded++;
                        Log($"--- {_currentEpisodeLabel}: downloaded ({produced!.SizeDisplay}). A step after "
                            + "the download failed, so its metadata or subtitles may be incomplete, but the "
                            + "video itself is finished. ---", JobLogLevel.Warning);
                        continue;
                    }
                }

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

                        var retry = await _ytDlpService.DownloadEpisodeAsync(
                            captured!,
                            showTitle,
                            metadata.NextSeasonNumber,
                            episodeNumber,
                            episodeTitle,
                            outputFolder,
                            referer: link.Url).ConfigureAwait(false);

                        if (retry.DrmProtected)
                        {
                            protectedCount++;
                            Log($"--- {_currentEpisodeLabel}: skipped - this video is DRM protected. ---",
                                JobLogLevel.Warning);
                            continue;
                        }

                        exitCode = retry.ExitCode;
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

            string summary = $"{succeeded} succeeded, {failed} failed";
            if (alreadyPresent > 0) summary += $", {alreadyPresent} already present";
            if (protectedCount > 0) summary += $", {protectedCount} DRM protected";

            Log($"--- Finished: {summary}, out of {links.Count}. ---",
                failed == 0 && protectedCount == 0 ? JobLogLevel.Success : JobLogLevel.Warning);

            if (protectedCount == links.Count && links.Count > 0)
            {
                Log("--- Every episode on this source is DRM protected. This content cannot be "
                    + "downloaded by any tool; it is not a fault in the app. ---", JobLogLevel.Error);
            }

            return (succeeded, failed, protectedCount, alreadyPresent);
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
