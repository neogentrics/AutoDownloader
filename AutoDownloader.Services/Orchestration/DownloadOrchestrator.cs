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

        /// <summary>
        /// The user's browser session, loaded once per job and shared by the page scanner and
        /// the stream capture. Null until first asked for.
        /// </summary>
        private List<Microsoft.Playwright.Cookie>? _pageCookies;

        /// <summary>
        /// The signed-in session for the headless scanner, or an empty list.
        ///
        /// Loaded on demand rather than at the start of every job, because a job whose site
        /// yt-dlp handles outright never needs it, and exporting cookies means running
        /// yt-dlp an extra time.
        /// </summary>
        private async Task<List<Microsoft.Playwright.Cookie>> GetPageCookiesAsync(
            CancellationToken cancellationToken)
        {
            if (_pageCookies != null) return _pageCookies;

            BrowserCookieProvider.OnDiagnostic += LogCookieDiagnostic;

            try
            {
                _pageCookies = await BrowserCookieProvider
                    .LoadAsync(_ytDlpService.YtDlpPath, CookieSource, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                BrowserCookieProvider.OnDiagnostic -= LogCookieDiagnostic;
            }

            return _pageCookies;
        }

        private void LogCookieDiagnostic(string message) => Log(message, JobLogLevel.Notice);

        /// <summary>
        /// Under this many seconds a stream is an advert, not an episode. Generous on
        /// purpose: the point is to notice that everything on offer is too short, not to
        /// rule out a genuinely brief programme.
        /// </summary>
        private const int AdvertSeconds = 120;

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
                    metadata.SeasonWasSpecified = true;
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
                metadata.SeasonWasSpecified = true;
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
            int seasonTitlesWereFetchedFor = metadata.NextSeasonNumber;

            List<EpisodeLink> episodeLinks =
                await BuildEpisodePlanAsync(finalUrl, metadata, cancellationToken).ConfigureAwait(false);

            // The plan may have discovered the page is showing a different season. The episode
            // titles in hand belong to the old one, so they would be written onto the wrong
            // episodes - exactly the failure the title matching was added to stop.
            if (metadata.NextSeasonNumber != seasonTitlesWereFetchedFor)
            {
                Log($"--- Re-reading episode titles for season {metadata.NextSeasonNumber}. ---",
                    JobLogLevel.Notice);

                try
                {
                    var reread = await _metadataService
                        .GetMergedMetadataAsync(attemptName, metadata.NextSeasonNumber, preferredYear)
                        .ConfigureAwait(false);

                    if (reread != null && reread.Episodes.Count > 0)
                    {
                        metadata.Episodes = reread.Episodes;
                        metadata.ExpectedEpisodeCount = reread.ExpectedEpisodeCount;
                        result.ExpectedEpisodeCount = reread.ExpectedEpisodeCount;

                        Log($"Season {metadata.NextSeasonNumber}: {reread.ExpectedEpisodeCount} "
                            + $"expected episode(s), {reread.Episodes.Count} title(s) known.",
                            JobLogLevel.Notice);
                    }
                    else
                    {
                        // Better no titles than the previous season's titles.
                        metadata.Episodes = new List<DownloadEpisode>();
                        Log("--- No titles found for that season; episodes will be numbered only. ---",
                            JobLogLevel.Warning);
                    }
                }
                catch (Exception ex)
                {
                    metadata.Episodes = new List<DownloadEpisode>();
                    Log($"--- Could not re-read titles: {ex.Message} ---", JobLogLevel.Warning);
                }

                await _xmlService.SaveMetadataAsync(finalOutputFolder, metadata).ConfigureAwait(false);
            }

            // Check the cookie source once, here, rather than discovering the problem one
            // episode at a time after the downloads have already started.
            string? cookieWarning = CookieSourceSpec.GetPreflightWarning(CookieSource);
            if (cookieWarning != null)
            {
                Log($"--- COOKIE WARNING: {cookieWarning} ---", JobLogLevel.Error);
            }

            var seasonFolders = SeasonFoldersFor(finalOutputFolder, episodeLinks, metadata);
            int filesBefore = seasonFolders.Sum(CountVideoFiles);

            // ---------------------------------------------------------------- 4. Download
            try
            {
                if (episodeLinks.Count > 0)
                {
                    var (succeeded, failed, protectedCount, alreadyPresent) = await DownloadEpisodesAsync(
                        episodeLinks, metadata, finalOutputFolder, cancellationToken,
                        result.ExpectedBySeason).ConfigureAwait(false);

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
            int filesAfter = seasonFolders.Sum(CountVideoFiles);
            result.FilesPresentAfter = filesAfter;
            result.FilesAdded = filesAfter - filesBefore;
            result.EpisodesOffered = episodeLinks.Count;

            ReportVerification(result, metadata.NextSeasonNumber, episodeLinks, finalOutputFolder);

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

            /// <summary>The seasons chosen for this item, when its page offered a choice.</summary>
            public List<int> Seasons { get; set; } = new List<int>();

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

            await ResolveSeasonsAsync(searchTerm, resolved.Merged, resolution, cancellationToken)
                .ConfigureAwait(false);

            return resolution;
        }

        /// <summary>
        /// Asks which seasons are wanted while the rest of the batch is still being
        /// identified, rather than when this item's turn comes round.
        ///
        /// Without this a second show stops the run halfway and waits for an answer - which
        /// defeats the point of asking everything up front, since the whole reason for the
        /// identify pass is that a batch can be started and left alone.
        ///
        /// The answer is recorded against the show's official title, which is the same key
        /// the download pass uses, so that pass finds it already answered.
        /// </summary>
        private async Task ResolveSeasonsAsync(
            string searchTerm,
            MergedSeriesMetadata merged,
            IdentityResolution resolution,
            CancellationToken cancellationToken)
        {
            // Only a real page has a season chooser; a bare search term has nothing to read.
            if (!searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                var indexer = new SeriesIndexer
                {
                    Cookies = await GetPageCookiesAsync(cancellationToken).ConfigureAwait(false),
                };

                var seasons = await indexer.DiscoverSeasonsAsync(searchTerm).ConfigureAwait(false);

                // One season, or none on offer, is not a choice worth interrupting anyone for.
                if (seasons.Count <= 1) return;

                int showing = seasons.Contains(resolution.Season) ? resolution.Season : seasons[0];

                var wanted = await _prompt.ChooseSeasonsAsync(
                        merged.OfficialTitle, seasons, showing, cancellationToken)
                    .ConfigureAwait(false);

                if (wanted == null)
                {
                    resolution.Cancelled = true;
                    return;
                }

                resolution.Seasons = wanted.ToList();
            }
            catch (Exception ex)
            {
                // Failing to ask early is not fatal: the download pass asks again, which is
                // merely the behaviour this method exists to improve on.
                OnDiagnostic?.Invoke($"Could not read the season list up front: {ex.Message}");
            }
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
        private void ReportVerification(
            DownloadJobResult result,
            int seasonNumber,
            List<EpisodeLink> links,
            string showFolder)
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
                // gap in the source is not mistaken for a gap in the download. Season by
                // season: one number cannot describe a run spanning five of them.
                ReportSeasonShortfalls(result, links, showFolder, seasonNumber, expected, offered);

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
        /// <summary>
        /// Writes what is known about every season this run touched into series_metadata.xml.
        ///
        /// The first save happens before the page has been read, so it covers one season and
        /// carries titles alone. By now the listing has given its own name, summary, air date,
        /// runtime and rating for most episodes - including for seasons the databases do not
        /// cover at all - and none of that was being kept anywhere.
        ///
        /// The databases still win on the title where they have one: their spelling is what a
        /// media library will match against. Everything else comes from the listing, because
        /// nothing else has it.
        /// </summary>
        private async Task SaveListingMetadataAsync(
            List<EpisodeLink> links,
            Dictionary<int, Dictionary<int, string?>> titlesBySeason,
            DownloadMetadata metadata,
            string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(metadata.OfficialTitle)) return;

            int seasonsWritten = 0;
            int described = 0;

            foreach (var season in links
                .GroupBy(l => l.DetectedSeasonNumber ?? metadata.NextSeasonNumber)
                .OrderBy(g => g.Key))
            {
                var titles = titlesBySeason.TryGetValue(season.Key, out var known)
                    ? known
                    : new Dictionary<int, string?>();

                var episodes = season
                    .Where(l => l.DetectedEpisodeNumber.HasValue)
                    .GroupBy(l => l.DetectedEpisodeNumber!.Value)
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var link = g.First();
                        titles.TryGetValue(g.Key, out string? fromDatabase);

                        return new DownloadEpisode
                        {
                            EpisodeNumber = g.Key,
                            EpisodeTitle = !string.IsNullOrWhiteSpace(fromDatabase)
                                ? fromDatabase
                                : ReadableName(link),
                            Description = link.Description,
                            AirDate = link.AirDate,
                            RuntimeMinutes = link.RuntimeMinutes,
                            Rating = link.Rating,
                        };
                    })
                    .ToList();

                if (episodes.Count == 0) continue;

                described += episodes.Count(e => !string.IsNullOrWhiteSpace(e.Description));

                var forSeason = new DownloadMetadata
                {
                    OfficialTitle = metadata.OfficialTitle,
                    SeriesId = metadata.SeriesId,
                    SourceUrl = metadata.SourceUrl,
                    NextSeasonNumber = season.Key,
                    ExpectedEpisodeCount = titles.Count > 0 ? titles.Count : episodes.Count,
                    Episodes = episodes,
                };

                try
                {
                    await _xmlService.SaveMetadataAsync(outputFolder, forSeason).ConfigureAwait(false);
                    seasonsWritten++;
                }
                catch (Exception ex)
                {
                    Log($"--- Could not record season {season.Key} in series_metadata.xml: "
                        + $"{ex.Message} ---", JobLogLevel.Warning);
                }
            }

            if (seasonsWritten > 0)
            {
                Log($"--- series_metadata.xml now covers {seasonsWritten} season(s), "
                    + $"{described} of them with a summary from the listing. ---",
                    JobLogLevel.Notice);
            }
        }

        /// <summary>
        /// A name for a link the databases do not cover, fit to put in a filename.
        ///
        /// The scraper does not always find anchor text, and then the link text is the URL
        /// path - which produced
        /// "S01E05 - _video_be-my-guest-with-ina-garten-food-network-atve-us_jon-batiste.mp4".
        /// The last segment of that path is the episode's name in every case seen so far, so
        /// it is used instead: "Jon Batiste".
        /// </summary>
        /// <summary>
        /// The last segment of a URL's or path's path portion, or an empty string when there
        /// is none. The host is deliberately not a candidate: it names a site, not an episode.
        /// </summary>
        private static string LastPathSegment(string value)
        {
            string path = value ?? string.Empty;

            if (Uri.TryCreate(path, UriKind.Absolute, out var uri)) path = uri.AbsolutePath;

            var segments = path.Split(PathMarks, StringSplitOptions.RemoveEmptyEntries);

            return segments.Length == 0 ? string.Empty : segments[segments.Length - 1];
        }

        /// <summary>The characters that mark a value as a path rather than a title.</summary>
        private static readonly char[] PathMarks = { '/', (char)92 };

        public static string? ReadableName(EpisodeLink link)
        {
            string text = (link.LinkText ?? string.Empty).Trim();

            // A path or a URL is not a title; its last path segment usually is. Taken from
            // the path proper, so a bare host gives nothing rather than "Example.com".
            if (text.Length == 0 || text.IndexOfAny(PathMarks) >= 0)
            {
                text = LastPathSegment(text.Length == 0 ? link.Url : text);
            }

            if (text.Length == 0 || text.Length > 120) return null;

            int dot = text.LastIndexOf('.');
            if (dot > 0 && text.Length - dot <= 5) text = text.Substring(0, dot);

            var words = text.Replace('-', ' ')
                            .Replace('_', ' ')
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0) return null;

            // Title case, leaving the small words alone unless they open the name - a slug is
            // all lower case, and "jon batiste" reads worse than the file deserves.
            var small = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "a", "an", "and", "the", "of", "or", "vs", "with", "for", "in", "on", "at", "to",
            };

            for (int w = 0; w < words.Length; w++)
            {
                if (w > 0 && small.Contains(words[w])) continue;

                words[w] = char.ToUpperInvariant(words[w][0]) + words[w].Substring(1);
            }

            return string.Join(" ", words);
        }

        /// <summary>
        /// Settles which season a link repeated across several tabs actually belongs to, by
        /// asking which season's episode list names it.
        ///
        /// A season tab is not always cleanly scoped. Food Network lists season 7 of Be My
        /// Guest with Ina Garten under season 1 as well, and keeping the earliest season
        /// filed Allison Janney, Jon Batiste, Hoda Kotb and Michael Barbaro as season 1
        /// episodes 5 to 8 while season 7 came out empty. Keeping the latest instead would
        /// break the opposite case, where a promotional tile really is repeated into every
        /// tab - so neither position rule is right, and the databases decide.
        ///
        /// Only when exactly one candidate season names the link. Two would be a guess, and
        /// none means there is nothing better than the season it already has.
        /// </summary>
        public void ResolveSeasonForRepeatedLinks(
            List<EpisodeLink> links, Dictionary<int, Dictionary<int, string?>> titlesBySeason)
        {
            int moved = 0;

            foreach (var link in links.Where(l => l.CandidateSeasons.Count > 1 && !l.NumbersFromListing))
            {
                var naming = link.CandidateSeasons
                    .Where(season => titlesBySeason.TryGetValue(season, out var titles)
                                     && NamesTheLink(titles, link))
                    .ToList();

                if (naming.Count != 1 || naming[0] == link.DetectedSeasonNumber) continue;

                Log($"--- {SlugOf(link.Url)} is listed under season(s) "
                    + string.Join(", ", link.CandidateSeasons.OrderBy(n => n))
                    + $", and season {naming[0]} is the one that names it. ---", JobLogLevel.Notice);

                link.DetectedSeasonNumber = naming[0];
                moved++;
            }

            if (moved > 0)
            {
                Log($"--- {moved} link(s) listed under more than one season were moved to the "
                    + "season the databases place them in. ---", JobLogLevel.Notice);
            }
        }

        /// <summary>True when one of a season's episode titles is this link's own title.</summary>
        private static bool NamesTheLink(Dictionary<int, string?> titles, EpisodeLink link)
        {
            var known = titles.Values
                .Select(EpisodeTitleMatcher.Normalise)
                .Where(t => t.Length > 0)
                .ToHashSet();

            return EpisodeTitleMatcher.CandidateTitles(link)
                .Select(EpisodeTitleMatcher.Normalise)
                .Any(t => t.Length > 0 && known.Contains(t));
        }

        private static string SlugOf(string url)
        {
            int slash = (url ?? string.Empty).TrimEnd('/').LastIndexOf('/');

            return slash >= 0 && slash < url!.Length - 1 ? url.Substring(slash + 1) : url ?? string.Empty;
        }

        /// <summary>
        /// Gives each link the number of the database episode whose title it carries, season
        /// by season, instead of its position in the listing.
        ///
        /// Position only works when the source carries the whole season. Food Network does
        /// not list Alex vs Southern Comfort, season 3 episode 4, so numbering by position
        /// wrote that title onto Alex vs Salmon and shifted the five episodes after it - and
        /// Alex vs California, the real episode 10, was filed as episode 9 under the name
        /// Alex vs Potatoes. Matching on the title the link itself carries leaves the gap
        /// where it belongs and names every file correctly.
        ///
        /// Links matching no database episode - alternate cuts and extras, which the
        /// databases do not list - keep their order and are numbered after the last real
        /// episode, so they cannot displace one.
        /// </summary>
        public void AlignNumbersToDatabase(
            List<EpisodeLink> links,
            Dictionary<int, Dictionary<int, string?>> titlesBySeason,
            DownloadMetadata metadata)
        {
            foreach (int season in titlesBySeason.Keys.OrderBy(n => n))
            {
                var episodes = titlesBySeason[season]
                    .Where(kv => kv.Key > 0 && !string.IsNullOrWhiteSpace(kv.Value))
                    .Select(kv => new DownloadEpisode { EpisodeNumber = kv.Key, EpisodeTitle = kv.Value })
                    .OrderBy(e => e.EpisodeNumber)
                    .ToList();

                if (episodes.Count == 0) continue;

                var seasonLinks = links
                    .Where(l => (l.DetectedSeasonNumber ?? metadata.NextSeasonNumber) == season)
                    .ToList();

                if (seasonLinks.Count == 0) continue;

                var stated = seasonLinks.Where(l => l.NumbersFromListing).ToList();
                var toPlace = seasonLinks.Where(l => !l.NumbersFromListing).ToList();

                var free = episodes;

                if (stated.Count > 0)
                {
                    Log($"--- Season {season}: the listing numbers {stated.Count} of its "
                        + "episodes itself, so those numbers are used as given. ---",
                        JobLogLevel.Notice);

                    // The rest still need placing, against whatever numbers are left. A show
                    // page carries one episode twice - a bare "Watch Now" tile and a full
                    // caption - and skipping the whole season over the captioned copy left
                    // the other one with no number at all.
                    var taken = new HashSet<int>(
                        stated.Where(l => l.DetectedEpisodeNumber.HasValue)
                              .Select(l => l.DetectedEpisodeNumber!.Value));

                    free = episodes.Where(e => !taken.Contains(e.EpisodeNumber)).ToList();
                }

                if (toPlace.Count == 0 || free.Count == 0) continue;

                var match = EpisodeTitleMatcher.Apply(toPlace, free);

                // Too few matched to trust it, so the numbering already in hand stands - but
                // not silently: this is the path that files a season by page order, and a
                // page ordered differently from the databases names every file wrongly.
                if (!match.Applied)
                {
                    Log($"--- Season {season}: only {match.Matched} of {match.Total} link(s) "
                        + "could be matched to an episode by title, which is too few to trust. "
                        + "Falling back to the order the page lists them in. ---",
                        JobLogLevel.Warning);
                    continue;
                }

                Log($"--- Season {season}: matched {match.Matched} of {match.Total} remaining "
                    + "link(s) to episodes by the title they carry. ---", JobLogLevel.Notice);

                var gaps = episodes
                    .Select(e => e.EpisodeNumber)
                    .Where(n => !seasonLinks.Any(l => l.DetectedEpisodeNumber == n))
                    .ToList();

                if (gaps.Count > 0)
                {
                    Log($"--- Season {season}: this source does not carry "
                        + string.Join(", ", gaps.Select(n => $"E{n:00}"))
                        + ". Those numbers are left empty rather than shifting the episodes "
                        + "after them. ---", JobLogLevel.Warning);
                }
            }

            links.Sort((a, b) =>
            {
                int bySeason = (a.DetectedSeasonNumber ?? 0).CompareTo(b.DetectedSeasonNumber ?? 0);

                return bySeason != 0
                    ? bySeason
                    : (a.DetectedEpisodeNumber ?? 0).CompareTo(b.DetectedEpisodeNumber ?? 0);
            });
        }

        /// <summary>
        /// Says, for each season, whether the source carries fewer episodes than the
        /// databases list - or more.
        ///
        /// Comparing the whole plan against one season's expected count produced "this source
        /// lists 61 episode(s) but the databases expect 5 for season 1" on a five-season run.
        /// That is nonsense, and it hid a season that really was one episode short.
        /// </summary>
        public void ReportSeasonShortfalls(
            DownloadJobResult result,
            List<EpisodeLink> links,
            string showFolder,
            int fallbackSeason,
            int expectedOverall,
            int offeredOverall)
        {
            var offeredBySeason = links
                .GroupBy(l => l.DetectedSeasonNumber ?? fallbackSeason)
                .ToDictionary(g => g.Key, g => g.Count());

            // Nothing per-season to go on: fall back to the single-season comparison, which
            // is correct when there is only one season in play.
            if (result.ExpectedBySeason.Count == 0)
            {
                if (expectedOverall > offeredOverall)
                {
                    Log($"--- NOTE: the databases list {expectedOverall} episode(s) for season "
                        + $"{fallbackSeason}, so this source carries {expectedOverall - offeredOverall} "
                        + "fewer. That is a limit of the source, not a failed download. ---",
                        JobLogLevel.Notice);
                }
                else if (expectedOverall > 0 && offeredOverall > expectedOverall)
                {
                    Log($"--- NOTE: this source lists {offeredOverall} episode(s) but the databases "
                        + $"expect {expectedOverall} for season {fallbackSeason}; it may include "
                        + "specials or another season. ---", JobLogLevel.Notice);
                }

                return;
            }

            foreach (int season in offeredBySeason.Keys.OrderBy(n => n))
            {
                if (!result.ExpectedBySeason.TryGetValue(season, out int expected) || expected <= 0)
                {
                    continue;
                }

                int offered = offeredBySeason[season];

                if (expected > offered)
                {
                    Log($"--- NOTE: season {season}: the databases list {expected} episode(s) and "
                        + $"this source carries {offered}, so {expected - offered} are not available "
                        + "here. That is a limit of the source, not a failed download. ---",
                        JobLogLevel.Notice);
                }
                else if (offered > expected)
                {
                    Log($"--- NOTE: season {season}: this source lists {offered} episode(s) against "
                        + $"the {expected} the databases expect; the extras are most likely specials "
                        + "or alternate cuts. ---", JobLogLevel.Notice);
                }
            }
        }

        /// <summary>
        /// Every season folder this run writes into.
        ///
        /// Counting only the target season while measuring against every link in the plan is
        /// what made a five-season run report "4 of the 65 episode(s) are present" with
        /// thirteen files on disk: one season's folder was being compared against five
        /// seasons' worth of episodes.
        /// </summary>
        public static List<string> SeasonFoldersFor(
            string showFolder, List<EpisodeLink> links, DownloadMetadata metadata)
        {
            var seasons = links
                .Where(l => l.DetectedSeasonNumber.HasValue)
                .Select(l => l.DetectedSeasonNumber!.Value)
                .Distinct()
                .ToList();

            if (seasons.Count == 0) seasons.Add(metadata.NextSeasonNumber);

            return seasons
                .OrderBy(n => n)
                .Select(n => Path.Combine(showFolder, $"Season {n:00}"))
                .ToList();
        }

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

            var indexer = new SeriesIndexer
            {
                Cookies = await GetPageCookiesAsync(cancellationToken).ConfigureAwait(false),
            };
            indexer.OnLog += line => OnDiagnostic?.Invoke(line);

            List<EpisodeLink> found;
            try
            {
                found = await indexer.IndexAsync(seriesUrl, renderJavaScript: false).ConfigureAwait(false);

                // The rendered pass always runs, not only when the static HTML looked empty.
                //
                // "More than two links" was never a test of whether those links work. A
                // Discovery+ show page ships twenty-five route links in its raw HTML that all
                // redirect to an error page, which was enough to skip rendering entirely -
                // and with it the signed-in session, the episode list in the page's API
                // responses, and the season chooser. The run failed every episode while the
                // log reported it had indexed twenty-five of them.
                //
                // Rendering is what a person actually sees, so it wins ties. It costs one
                // browser load per job, not per episode.
                if (!cancellationToken.IsCancellationRequested)
                {
                    var seasonsOffered = await indexer.DiscoverSeasonsAsync(seriesUrl).ConfigureAwait(false);

                    List<EpisodeLink> rendered;

                    if (seasonsOffered.Count > 1)
                    {
                        int showing = seasonsOffered.Contains(metadata.NextSeasonNumber)
                            ? metadata.NextSeasonNumber
                            : seasonsOffered[0];

                        Log($"--- This page offers {seasonsOffered.Count} seasons. ---", JobLogLevel.Notice);

                        var wanted = await _prompt.ChooseSeasonsAsync(
                            metadata.OfficialTitle ?? seriesUrl, seasonsOffered, showing, cancellationToken)
                            .ConfigureAwait(false);

                        if (wanted == null)
                        {
                            Log("--- No seasons chosen. Stopping. ---", JobLogLevel.Error);
                            return plan;
                        }

                        Log($"--- Reading season(s) {string.Join(", ", wanted)}. ---", JobLogLevel.Notice);

                        rendered = await indexer.IndexSeasonsAsync(seriesUrl, wanted.ToList())
                            .ConfigureAwait(false);

                        // A season came from the chooser, so it is known rather than guessed -
                        // and every season chosen is recorded, not just the first, or the
                        // filter below keeps one and discards the rest.
                        if (rendered.Count > 0)
                        {
                            metadata.SeasonWasSpecified = true;
                            metadata.SelectedSeasons = wanted.ToList();
                        }
                    }
                    else
                    {
                        rendered = await indexer.IndexAsync(seriesUrl, renderJavaScript: true).ConfigureAwait(false);
                    }

                    if (rendered.Count >= found.Count && rendered.Count > 0)
                    {
                        if (found.Count > 0 && !rendered.SequenceEqual(found))
                        {
                            Log($"--- The rendered page gives {rendered.Count} link(s); using those "
                                + $"rather than the {found.Count} in the raw HTML. ---", JobLogLevel.Notice);
                        }

                        found = rendered;
                    }
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

            var planned = AssignEpisodeNumbers(found, metadata);

            await ChooseQualityAsync(planned, metadata, cancellationToken).ConfigureAwait(false);

            return planned;
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
        /// Drops links pointing at a URL already seen, keeping the first.
        ///
        /// A season listing can repeat an episode the previous season also carried - Food
        /// Network lists Alex vs Shellfish under both season one and season two - and the
        /// same video then gets planned twice under two different numbers. Earliest wins,
        /// which after ordering means the lowest season keeps it.
        /// </summary>
        private List<EpisodeLink> WithoutDuplicateUrls(List<EpisodeLink> links)
        {
            var keptByUrl = new Dictionary<string, EpisodeLink>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<EpisodeLink>(links.Count);

            foreach (var link in links)
            {
                string key = (link.Url ?? string.Empty).Trim();

                if (key.Length > 0 && keptByUrl.TryGetValue(key, out var already))
                {
                    // The repeat is dropped, but which season carried it is not: a tab that
                    // lists another season's episodes would otherwise take them for good.
                    if (link.DetectedSeasonNumber.HasValue
                        && !already.CandidateSeasons.Contains(link.DetectedSeasonNumber.Value))
                    {
                        already.CandidateSeasons.Add(link.DetectedSeasonNumber.Value);
                    }

                    // The repeat may be the better copy. A show page lists an episode once
                    // as a bare "Watch Now" tile and again with its full caption, and which
                    // came first is an accident of the page - so the captioned one wins.
                    if (link.NumbersFromListing && !already.NumbersFromListing)
                    {
                        already.DetectedSeasonNumber = link.DetectedSeasonNumber;
                        already.DetectedEpisodeNumber = link.DetectedEpisodeNumber;
                        already.NumbersFromListing = true;

                        if (!string.IsNullOrWhiteSpace(link.LinkText)) already.LinkText = link.LinkText;

                        already.Description ??= link.Description;
                        already.AirDate ??= link.AirDate;
                    }

                    continue;
                }

                if (key.Length > 0)
                {
                    keptByUrl[key] = link;

                    if (link.DetectedSeasonNumber.HasValue
                        && !link.CandidateSeasons.Contains(link.DetectedSeasonNumber.Value))
                    {
                        link.CandidateSeasons.Add(link.DetectedSeasonNumber.Value);
                    }
                }

                kept.Add(link);
            }

            int dropped = links.Count - kept.Count;

            if (dropped > 0)
            {
                Log($"--- {dropped} link(s) pointed at a video already in the list and were "
                    + "dropped. ---", JobLogLevel.Notice);
            }

            return kept;
        }

        /// <summary>
        /// Restarts episode numbering at one for each season.
        ///
        /// A listing commonly numbers its tiles straight through the whole show - 1 to 65
        /// across five seasons - and reading those numbers literally files season two as
        /// S02E11 through S02E20 rather than S02E01 through S02E10. Plex, and anyone
        /// looking at the folder, expects the latter.
        ///
        /// Only applied when the numbering really is continuous: if a season already starts
        /// at one then the page numbers per season and there is nothing to correct.
        /// </summary>
        private void RenumberWithinSeasons(List<EpisodeLink> links)
        {
            var seasons = links
                .Where(l => l.DetectedSeasonNumber.HasValue)
                .GroupBy(l => l.DetectedSeasonNumber!.Value)
                .OrderBy(g => g.Key)
                .ToList();

            if (seasons.Count < 2) return;

            // Where the listing states the numbering outright, it is not the page's running
            // order and there is nothing to correct.
            if (links.Any(l => l.NumbersFromListing)) return;

            // Continuous numbering shows up as every season after the first starting above
            // one. A single season starting at one is enough to say the page numbers per
            // season, and then renumbering would be the thing that broke it.
            bool alreadyPerSeason = seasons.Any(g =>
                g.Min(l => l.DetectedEpisodeNumber ?? int.MaxValue) == 1 && g.Key != seasons[0].Key);

            if (alreadyPerSeason) return;

            foreach (var season in seasons)
            {
                int number = 1;

                foreach (var link in season.OrderBy(l => l.DetectedEpisodeNumber ?? int.MaxValue))
                {
                    link.DetectedEpisodeNumber = number++;
                }
            }

            Log("--- The page numbers its episodes straight through the series, so they have "
                + "been renumbered to start at 1 in each season. ---", JobLogLevel.Notice);
        }

        /// <summary>
        /// The key the quality choice is remembered under.
        ///
        /// Deliberately not the show's name: this is one decision about how much disk the
        /// run should cost, and asking it again for the second show in a batch would be
        /// asking the same question twice.
        /// </summary>
        private const string QualityChoiceKey = "(quality for this run)";

        /// <summary>
        /// Offers the quality rungs the source publishes, once the episode list is known so
        /// the cost of the whole run can be shown rather than the cost of one episode.
        ///
        /// Probed from the first episode and applied to the rest: a packager mints the same
        /// ladder for every episode of a series, and probing each one would add a round trip
        /// per episode to answer a question already answered.
        /// </summary>
        private async Task ChooseQualityAsync(
            List<EpisodeLink> links, DownloadMetadata metadata, CancellationToken cancellationToken)
        {
            if (links.Count == 0) return;

            try
            {
                var formats = await _ytDlpService
                    .ProbeFormatsAsync(links[0].Url, metadata.SourceUrl, cancellationToken)
                    .ConfigureAwait(false);

                var worthwhile = FormatOption.Worthwhile(formats);

                // One rung is not a choice, and no rungs means the source did not say.
                if (worthwhile.Count < 2)
                {
                    Log("--- The source publishes no quality choice, so the Format preference "
                        + "applies. ---");
                    return;
                }

                Log($"--- This source offers {worthwhile.Count} qualities, "
                    + $"from {worthwhile.Last().SizeDisplay} to {worthwhile.First().SizeDisplay} "
                    + "per episode. ---", JobLogLevel.Notice);

                string? chosen = await _prompt
                    .ChooseQualityAsync(QualityChoiceKey, worthwhile, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(chosen)) return;

                _ytDlpService.QualityOverride = chosen;

                Log("--- Using the chosen quality for this run. ---", JobLogLevel.Notice);
            }
            catch (Exception ex)
            {
                // Not being able to offer the choice is no reason to stop: the configured
                // preference still applies, exactly as it did before this existed.
                Log($"--- Could not read the quality list, so the Format preference still "
                    + $"applies: {ex.Message} ---", JobLogLevel.Warning);
            }
        }

        /// <summary>
        /// Gives every link an episode number: the one parsed from the page when there is one,
        /// otherwise its position. This is what lets a site publishing no metadata at all still
        /// produce correctly numbered, Plex-ready filenames.
        /// </summary>
        public List<EpisodeLink> AssignEpisodeNumbers(List<EpisodeLink> links, DownloadMetadata metadata)
        {
            links = WithoutDuplicateUrls(links);

            int targetSeason = metadata.NextSeasonNumber;

            // When the links say which season they belong to, keep only the one asked for.
            // A listing that covers every season at once would otherwise be flattened into
            // the target season - five seasons of a show arriving as S01E01 to S01E65.
            var withSeason = links.Where(l => l.DetectedSeasonNumber.HasValue).ToList();

            // More than one season was asked for, so there is nothing to filter down to.
            // Keeping only targetSeason here is what turned sixty-five indexed episodes into
            // ten: the picker gathered all five seasons and this threw four of them away.
            if (metadata.SelectedSeasons.Count > 1 && withSeason.Count > 0)
            {
                var wanted = links
                    .Where(l => l.DetectedSeasonNumber.HasValue
                                && metadata.SelectedSeasons.Contains(l.DetectedSeasonNumber.Value))
                    .ToList();

                if (wanted.Count > 0)
                {
                    Log($"--- Keeping {wanted.Count} link(s) across season(s) "
                        + $"{string.Join(", ", metadata.SelectedSeasons)}. ---", JobLogLevel.Notice);

                    var ordered = wanted
                        .OrderBy(l => l.DetectedSeasonNumber ?? int.MaxValue)
                        .ThenBy(l => l.DetectedEpisodeNumber ?? int.MaxValue)
                        .ToList();

                    RenumberWithinSeasons(ordered);

                    return ordered;
                }
            }

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
                else if (!metadata.SeasonWasSpecified)
                {
                    // Nobody asked for season 1 - it is just the default. A show page that
                    // opens on its newest season would otherwise have that season's episodes
                    // filed as season 1, which is wrong in the most confusing way: the
                    // filenames look right.
                    int pageSeason = withSeason
                        .GroupBy(l => l.DetectedSeasonNumber!.Value)
                        .OrderByDescending(g => g.Count())
                        .First().Key;

                    Log($"--- The page is showing season {pageSeason}, and no season was asked "
                        + $"for. Using season {pageSeason} instead of the default. ---",
                        JobLogLevel.Notice);

                    metadata.NextSeasonNumber = pageSeason;
                    targetSeason = pageSeason;
                    links = withSeason.Where(l => l.DetectedSeasonNumber == pageSeason).ToList();
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
            CancellationToken cancellationToken,
            Dictionary<int, int>? expectedBySeason = null)
        {
            string showTitle = metadata.OfficialTitle ?? "Unknown Show";

            // Titles per season, not one set for all of them.
            //
            // The season picker hands back links from several seasons, each stamped with the
            // season it was read under - and this loop used to ignore that and file everything
            // under one number. Five seasons would have arrived as S01E01 upwards in a single
            // folder, wearing season one's episode titles.
            var titlesBySeason = new Dictionary<int, Dictionary<int, string?>>();

            Dictionary<int, string?> TitlesFor(int season)
            {
                return titlesBySeason.TryGetValue(season, out var found)
                    ? found
                    : new Dictionary<int, string?>();
            }

            // Candidate seasons are included: a link repeated across tabs may belong to a
            // season that, after the repeats were dropped, has no links left to ask about.
            foreach (var season in links
                .SelectMany(l => l.CandidateSeasons
                    .Concat(new[] { l.DetectedSeasonNumber ?? metadata.NextSeasonNumber }))
                .Distinct()
                .OrderBy(n => n))
            {
                // The season already looked up is in hand; the rest need fetching.
                if (season == metadata.NextSeasonNumber && metadata.Episodes.Count > 0)
                {
                    titlesBySeason[season] = metadata.Episodes
                        .Where(e => e.EpisodeNumber > 0)
                        .GroupBy(e => e.EpisodeNumber)
                        .ToDictionary(g => g.Key, g => g.First().EpisodeTitle);

                    if (expectedBySeason != null)
                    {
                        expectedBySeason[season] = metadata.ExpectedEpisodeCount;
                    }

                    continue;
                }

                try
                {
                    var merged = await _metadataService
                        .GetMergedMetadataAsync(showTitle, season)
                        .ConfigureAwait(false);

                    titlesBySeason[season] = merged?.Episodes
                        .Where(e => e.EpisodeNumber > 0)
                        .GroupBy(e => e.EpisodeNumber)
                        .ToDictionary(g => g.Key, g => g.First().EpisodeTitle)
                        ?? new Dictionary<int, string?>();

                    if (expectedBySeason != null)
                    {
                        // Fall back to the number of titles when the databases give no count:
                        // knowing ten titles is itself evidence of ten episodes.
                        int count = merged?.ExpectedEpisodeCount ?? 0;

                        expectedBySeason[season] = count > 0 ? count : titlesBySeason[season].Count;
                    }

                    Log($"Season {season}: {titlesBySeason[season].Count} title(s) known.",
                        JobLogLevel.Notice);
                }
                catch (Exception ex)
                {
                    // Better a numbered file than one wearing another season's title.
                    titlesBySeason[season] = new Dictionary<int, string?>();
                    Log($"--- Could not read titles for season {season}: {ex.Message} ---",
                        JobLogLevel.Warning);
                }
            }

            ResolveSeasonForRepeatedLinks(links, titlesBySeason);

            AlignNumbersToDatabase(links, titlesBySeason, metadata);

            await SaveListingMetadataAsync(links, titlesBySeason, metadata, outputFolder)
                .ConfigureAwait(false);

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

                // The season the link was actually read under, not whichever one the job
                // started with.
                int seasonNumber = link.DetectedSeasonNumber ?? metadata.NextSeasonNumber;

                TitlesFor(seasonNumber).TryGetValue(episodeNumber, out string? episodeTitle);

                // The databases cover a season's episodes but rarely its extras, so a page
                // listing specials alongside them would file those as bare numbers. The link
                // usually carries the real name, and a named file beats "S05E102.mp4".
                if (string.IsNullOrWhiteSpace(episodeTitle))
                {
                    episodeTitle = ReadableName(link);
                }

                _currentEpisodeIndex = i + 1;
                _currentEpisodeCount = links.Count;
                _currentEpisodeLabel = $"S{seasonNumber:00}E{episodeNumber:00}";

                // Ask before replacing anything already on disk. Previously an existing file
                // was skipped silently, which is a sensible default and a poor answer when the
                // existing file is precisely the one you wanted to replace.
                bool forceOverwrite = false;

                var existing = YtDlpService.FindExistingEpisode(
                    showTitle, seasonNumber, episodeNumber, episodeTitle, outputFolder);

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
                    seasonNumber,
                    episodeNumber,
                    episodeTitle,
                    outputFolder,
                    referer: null,
                    forceOverwrite: forceOverwrite,

                    // The archive records what was downloaded, not what is still there. A
                    // file deleted since - or never written because a run was stopped - would
                    // otherwise be skipped for ever and counted as a success, which is how a
                    // run reported nineteen episodes with four on disk.
                    bypassArchive: existing == null).ConfigureAwait(false);

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

                // A stopped download is not a failed one, and there is nothing to retry.
                if (outcome.Cancelled)
                {
                    Log("--- Stopped. ---", JobLogLevel.Error);
                    break;
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
                        showTitle, seasonNumber, episodeNumber, episodeTitle, outputFolder);

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
                            seasonNumber,
                            episodeNumber,
                            episodeTitle,
                            outputFolder,
                            referer: link.Url,
                            forceOverwrite: forceOverwrite,
                            bypassArchive: true).ConfigureAwait(false);

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
        /// <summary>
        /// Of the streams a page requested, the one that is actually the programme.
        ///
        /// A player loads its pre-roll first, so the first manifest observed is routinely an
        /// advert - which downloads perfectly and is the wrong video entirely. A real run
        /// produced a thirty-second car commercial named as episode one.
        ///
        /// Length is what separates them, and it needs nothing site-specific: an advert runs
        /// well under two minutes and an episode does not. Anything whose duration cannot be
        /// read keeps its place in the order rather than being discarded, so a site that
        /// reports no duration behaves as it did before.
        /// </summary>
        private async Task<string?> ChooseLongestStreamAsync(List<string> candidates, string pageUrl)
        {
            string? best = null;
            double bestContent = 0;
            ManifestInspector.ManifestFacts? bestFacts = null;

            foreach (var candidate in candidates)
            {
                var facts = await ManifestInspector
                    .InspectAsync(candidate, pageUrl, ToolManagerService.FIREFOX_USER_AGENT)
                    .ConfigureAwait(false);

                if (facts == null) continue;

                OnDiagnostic?.Invoke($"Manifest: {facts.TotalSeconds / 60:0.0} min across "
                    + $"{facts.PeriodSeconds.Count} period(s), {facts.ContentSeconds / 60:0.0} min of content.");

                // A manifest that is one piece is worth more than a longer one chopped up,
                // because only the former downloads as a whole.
                bool better = facts.ContentSeconds > bestContent
                              || (bestFacts != null && bestFacts.HasStitchedAdverts && facts.IsSinglePeriod);

                if (better)
                {
                    best = candidate;
                    bestContent = facts.ContentSeconds;
                    bestFacts = facts;
                }
            }

            if (best == null) return candidates.FirstOrDefault();

            if (bestFacts != null && bestFacts.HasStitchedAdverts)
            {
                Log($"--- WARNING: this stream has adverts stitched into it - "
                    + $"{bestFacts.ContentPeriods.Count()} pieces of programme ({bestFacts.ContentSeconds / 60:0.0} min) "
                    + $"broken up by {bestFacts.AdvertPeriods.Count()} advert breaks "
                    + $"({bestFacts.AdvertSecondsTotal / 60:0.0} min). ---", JobLogLevel.Warning);

                Log("--- The downloader takes one piece at a time, so what arrives will be a "
                    + "fragment of the episode - often an advert - rather than the whole thing. "
                    + "Downloading it anyway so the file is there to look at. ---", JobLogLevel.Warning);
            }

            return best;
        }

        private async Task<string?> CaptureMediaUrlAsync(string pageUrl)
        {
            try
            {
                var extractor = new MediaUrlExtractor
                {
                    Cookies = _pageCookies ?? new List<Microsoft.Playwright.Cookie>(),
                };

                extractor.OnLog += line => OnDiagnostic?.Invoke(line);

                var urls = await extractor.ExtractMediaUrlsAsync(pageUrl).ConfigureAwait(false);

                if (urls.Count == 0)
                {
                    // Say which of the two it is. "No media requests" reads like the page had
                    // no video on it, when the usual cause is that the scanner is a separate
                    // browser with no session - so a page behind a login never starts its
                    // player, and there is nothing to observe.
                    // Now that the scanner carries the configured browser session, a login
                    // wall is no longer the likeliest explanation - so this no longer claims
                    // it is. The usual cause is a link that is not a player page at all.
                    Log("--- No media requests were observed on that page. Either it is not a "
                        + "player page, or the player never started. If cookies are set to None "
                        + "in Preferences, a site that needs a sign-in will look empty here. ---",
                        JobLogLevel.Warning);
                    return null;
                }

                OnDiagnostic?.Invoke($"MediaUrlExtractor: {urls.Count} candidate(s) for {pageUrl}");

                return await ChooseLongestStreamAsync(urls, pageUrl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"--- Media capture failed: {ex.Message} ---", JobLogLevel.Warning);
                return null;
            }
        }
    }
}
