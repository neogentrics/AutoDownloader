using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Cli
{
    /// <summary>
    /// Unattended entry point for the download pipeline (AD-030).
    ///
    /// This exists because the pipeline lives in DownloadOrchestrator rather than in a window,
    /// and takes its one interactive step through IUserPrompt. Here that is AutoConfirmPrompt,
    /// so nothing ever waits for a human. Settings are shared with the desktop application, so
    /// API keys configured there apply here too.
    /// </summary>
    public static class Program
    {
        private static string Version => AutoDownloader.Core.AppInfo.Version;

        // Exit codes, chosen so a scheduler or n8n can branch on the outcome.
        private const int ExitCompleted = 0;
        private const int ExitFailed = 1;
        private const int ExitBadArguments = 2;
        private const int ExitNothingDownloaded = 3;
        private const int ExitCancelled = 130;

        public static async Task<int> Main(string[] args)
        {
            SessionLogWriter.Start("cli");

            var options = CommandLineOptions.Parse(args);

            if (options.HasError)
            {
                Console.Error.WriteLine(options.Error);
                return ExitBadArguments;
            }

            if (options.Help)
            {
                Console.WriteLine(CommandLineOptions.Usage);
                return ExitCompleted;
            }

            if (options.Version)
            {
                Console.WriteLine($"AutoDownloader CLI {Version}");
                return ExitCompleted;
            }

            if (!string.IsNullOrWhiteSpace(options.ConvertFolder))
            {
                return await ConvertAsync(options).ConfigureAwait(false);
            }

            if (options.WatchList || !string.IsNullOrWhiteSpace(options.WatchAdd)
                || !string.IsNullOrWhiteSpace(options.WatchRemove))
            {
                return ManageWatchList(options);
            }


            using var cancellation = new CancellationTokenSource();

            // Ctrl+C asks the job to stop rather than killing the process outright, so the
            // running yt-dlp child gets terminated and partial files are not orphaned.
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Console.Error.WriteLine();
                Console.Error.WriteLine("Stopping... (press Ctrl+C again to force)");
                cancellation.Cancel();
            };

            try
            {
                // Checked here rather than earlier so a watch run gets the same Ctrl+C
                // handling as an ordinary download.
                if (options.WatchRun)
                {
                    return await RunWatchListAsync(options, cancellation.Token).ConfigureAwait(false);
                }

                return await RunAsync(options, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return ExitCancelled;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal: {ex.Message}");
                return ExitFailed;
            }
        }

        private static async Task<int> RunAsync(CommandLineOptions options, CancellationToken cancellationToken)
        {
            bool prose = !options.Json && !options.Quiet;

            // ---------------------------------------------------------------- settings
            var settingsService = new SettingsService();
            var settings = settingsService.Settings;

            // Command-line values override the saved settings for this run only; nothing is
            // written back, so a one-off run cannot quietly change the desktop app's config.
            string outputFolder = options.OutputFolder ?? settings.DefaultOutputFolder;
            string quality = options.Quality ?? settings.PreferredVideoQuality;
            string cookieSource = options.CookieSource ?? settings.CookieSource;
            bool useArchive = !options.NoArchive && settings.UseDownloadArchive;
            bool autoFfmpeg = !options.NoFfmpeg && settings.AutoDownloadFfmpeg;

            if (prose)
            {
                Console.WriteLine($"AutoDownloader CLI {Version}");
                Console.WriteLine($"Target : {options.Target}");
                Console.WriteLine($"Output : {outputFolder}");
                if (options.Season.HasValue) Console.WriteLine($"Season : {options.Season.Value}");
                Console.WriteLine();
            }

            // ---------------------------------------------------------------- tools
            var toolManager = new ToolManagerService { AutoDownloadFfmpeg = autoFfmpeg };

            if (prose)
            {
                toolManager.OnToolLogReceived += line => Console.WriteLine($"  {line}");
            }

            var (ytDlpPath, ariaPath, ffmpegPath) = await toolManager.EnsureToolsAvailableAsync();

            // ---------------------------------------------------------------- services
            var metadataService = new MetadataService(settings.TmdbApiKey, settings.TvdbApiKey);

            if (!metadataService.IsTmdbKeyValid && !metadataService.IsTvdbKeyValid)
            {
                Console.Error.WriteLine(
                    "No TMDB or TVDB API key is configured, so episodes cannot be named.");
                Console.Error.WriteLine(
                    @"Set one in %APPDATA%\AutoDownloader\settings.json, or in the desktop app under Edit > Preferences.");
                return ExitBadArguments;
            }

            var ytDlpService = new YtDlpService(
                ytDlpPath,
                ariaPath,
                ToolManagerService.FIREFOX_USER_AGENT,
                quality,
                ffmpegPath,
                cookieSource,
                useArchive,
                settings.FormatPreference);

            var orchestrator = new DownloadOrchestrator(
                metadataService,
                new SearchService(settings.GeminiApiKey),
                new XmlService(),
                ytDlpService,
                new AutoConfirmPrompt())
            {
                CookieSource = cookieSource,
                AlwaysOverwrite = options.Overwrite
            };

            // ---------------------------------------------------------------- output
            var renderer = new ConsoleProgressRenderer(options.Quiet || options.Json);

            orchestrator.OnLog += (_, e) =>
            {
                if (options.Quiet || options.Json)
                {
                    // Still surface real problems, on stderr so --json stdout stays clean.
                    if (e.Level == JobLogLevel.Error) Console.Error.WriteLine(e.Message);
                    return;
                }

                renderer.Clear();
                Console.WriteLine(e.Message);
            };

            orchestrator.OnProgress += renderer.Report;

            // The file gets everything, regardless of --quiet or --json.
            orchestrator.OnLog += (_, e) => SessionLogWriter.Append(e.Message);
            orchestrator.OnDiagnostic += SessionLogWriter.Append;

            // ---------------------------------------------------------------- run
            var result = await orchestrator.RunAsync(
                options.Target!,
                outputFolder,
                cancellationToken,
                options.Season);

            renderer.Clear();

            SessionLogWriter.NoteRunFinished(Summarise(result).Replace("\n", " | "));

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    completed = result.Completed,
                    cancelled = result.Cancelled,
                    failureReason = result.FailureReason,
                    title = result.OfficialTitle,
                    season = result.SeasonNumber,
                    episodesSucceeded = result.EpisodesSucceeded,
                    episodesFailed = result.EpisodesFailed,
                    episodesAlreadyPresent = result.EpisodesAlreadyPresent,
                    episodesDrmProtected = result.EpisodesProtected,
                    expectedEpisodeCount = result.ExpectedEpisodeCount,
                    episodesOfferedBySource = result.EpisodesOffered,
                    filesAdded = result.FilesAdded,
                    filesPresent = result.FilesPresentAfter,
                    outputFolder = result.OutputFolder,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!options.Quiet)
            {
                Console.WriteLine();
                Console.WriteLine(Summarise(result));
            }

            // ---------------------------------------------------------------- exit code
            if (result.Cancelled) return ExitCancelled;
            if (!result.Completed) return ExitFailed;
            // A run that found everything already on disk did its job: nothing was missing.
            // Reporting that as "nothing downloaded" would make a nightly watch-list run look
            // like a failure to a scheduler on every night that had no new episode.
            if (result.FilesAdded == 0 && result.EpisodesSucceeded == 0
                && result.EpisodesAlreadyPresent == 0)
            {
                return ExitNothingDownloaded;
            }

            return ExitCompleted;
        }

        /// <summary>
        /// Adds, removes or prints watch-list entries. No downloading.
        /// </summary>
        private static int ManageWatchList(CommandLineOptions options)
        {
            var watchList = new WatchListService();
            watchList.OnDiagnostic += Console.Error.WriteLine;

            if (!string.IsNullOrWhiteSpace(options.WatchAdd))
            {
                var entry = watchList.Add(options.WatchAdd!, options.ShowName, options.Season, options.OutputFolder);
                Console.WriteLine($"Watching [{entry.Id}] {entry.Url}"
                                + (entry.Season.HasValue ? $" (season {entry.Season})" : ""));
                Console.WriteLine("Run 'autodl --watch-run' to check it, or put that on a schedule.");
                return ExitCompleted;
            }

            if (!string.IsNullOrWhiteSpace(options.WatchRemove))
            {
                if (watchList.Remove(options.WatchRemove!))
                {
                    Console.WriteLine($"Removed {options.WatchRemove}.");
                    return ExitCompleted;
                }

                Console.Error.WriteLine($"No watch-list entry matches '{options.WatchRemove}'.");
                return ExitBadArguments;
            }

            var entries = watchList.Entries;

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
                return ExitCompleted;
            }

            if (entries.Count == 0)
            {
                Console.WriteLine("Nothing is being watched yet. Add something with --watch-add <url>.");
                return ExitCompleted;
            }

            Console.WriteLine($"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} in {watchList.FilePath}:");
            Console.WriteLine();

            foreach (var entry in entries)
            {
                string when = entry.LastCheckedUtc.HasValue
                    ? entry.LastCheckedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : "never";

                Console.WriteLine($"  [{entry.Id}] {entry.Display}{(entry.Enabled ? "" : "  (disabled)")}");
                Console.WriteLine($"        {entry.Url}");
                Console.WriteLine($"        season {(entry.Season?.ToString() ?? "auto")}"
                                + $" | last checked {when}"
                                + $" | {entry.LastEpisodeCount} episode(s)"
                                + (entry.ConsecutiveFailures > 0 ? $" | {entry.ConsecutiveFailures} failed check(s)" : ""));

                if (!string.IsNullOrWhiteSpace(entry.LastResult))
                {
                    Console.WriteLine($"        {entry.LastResult}");
                }

                Console.WriteLine();
            }

            return ExitCompleted;
        }

        /// <summary>
        /// Checks every watched series and downloads whatever is new.
        ///
        /// The yt-dlp download archive does the real work here: an episode already recorded is
        /// skipped, so a repeat run costs a metadata lookup and a page index rather than a
        /// re-download. That is what makes this cheap enough to schedule.
        /// </summary>
        private static async Task<int> RunWatchListAsync(CommandLineOptions options, CancellationToken cancellationToken)
        {
            var watchList = new WatchListService();
            watchList.OnDiagnostic += Console.Error.WriteLine;

            var due = watchList.GetDueEntries();

            if (due.Count == 0)
            {
                if (!options.Quiet) Console.WriteLine("Nothing is being watched.");
                return ExitCompleted;
            }

            var settingsService = new SettingsService();
            var settings = settingsService.Settings;

            var toolManager = new ToolManagerService
            {
                AutoDownloadFfmpeg = !options.NoFfmpeg && settings.AutoDownloadFfmpeg
            };

            var (ytDlpPath, ariaPath, ffmpegPath) = await toolManager.EnsureToolsAvailableAsync().ConfigureAwait(false);

            var metadataService = new MetadataService(settings.TmdbApiKey, settings.TvdbApiKey);

            if (!metadataService.IsTmdbKeyValid && !metadataService.IsTvdbKeyValid)
            {
                Console.Error.WriteLine("No TMDB or TVDB API key is configured, so episodes cannot be named.");
                return ExitBadArguments;
            }

            var summaries = new List<object>();
            int totalNew = 0;
            int failures = 0;

            if (!options.Quiet && !options.Json)
            {
                Console.WriteLine($"Checking {due.Count} watched series...");
                Console.WriteLine();
            }

            foreach (var entry in due)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (!options.Quiet && !options.Json) Console.WriteLine($"[{entry.Id}] {entry.Display}");

                var ytDlpService = new YtDlpService(
                    ytDlpPath, ariaPath, ToolManagerService.FIREFOX_USER_AGENT,
                    options.Quality ?? settings.PreferredVideoQuality,
                    ffmpegPath,
                    options.CookieSource ?? settings.CookieSource,
                    !options.NoArchive && settings.UseDownloadArchive,
                    settings.FormatPreference);

                var orchestrator = new DownloadOrchestrator(
                    metadataService,
                    new SearchService(settings.GeminiApiKey),
                    new XmlService(),
                    ytDlpService,
                    new AutoConfirmPrompt())
                {
                    CookieSource = options.CookieSource ?? settings.CookieSource
                };

                orchestrator.OnLog += (_, e) =>
                {
                    SessionLogWriter.Append($"[{entry.Id}] {e.Message}");
                    if (e.Level == JobLogLevel.Error && !options.Quiet) Console.Error.WriteLine("  " + e.Message);
                };
                orchestrator.OnDiagnostic += SessionLogWriter.Append;

                DownloadJobResult result;

                try
                {
                    // A stored show name matters for URLs that contain none, such as a playlist.
                    string target = string.IsNullOrWhiteSpace(entry.ShowName) ? entry.Url : entry.Url;

                    result = await orchestrator.RunAsync(
                        target,
                        entry.OutputFolder ?? options.OutputFolder ?? settings.DefaultOutputFolder,
                        cancellationToken,
                        entry.Season).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  [{entry.Id}] failed: {ex.Message}");
                    watchList.RecordCheck(entry, false, entry.LastEpisodeCount, ex.Message, null);
                    failures++;
                    continue;
                }

                bool succeeded = result.Completed && !result.Cancelled;
                totalNew += result.FilesAdded;
                if (!succeeded) failures++;

                string outcome = result.FilesAdded > 0
                    ? $"{result.FilesAdded} new episode(s)"
                    : succeeded ? "nothing new" : (result.FailureReason ?? "failed");

                watchList.RecordCheck(entry, succeeded, result.FilesPresentAfter, outcome, result.OfficialTitle);

                if (!options.Quiet && !options.Json) Console.WriteLine($"  {outcome}");

                summaries.Add(new
                {
                    id = entry.Id,
                    title = result.OfficialTitle ?? entry.Display,
                    url = entry.Url,
                    newEpisodes = result.FilesAdded,
                    present = result.FilesPresentAfter,
                    offered = result.EpisodesOffered,
                    succeeded,
                    reason = result.FailureReason
                });
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    checkedCount = summaries.Count,
                    newEpisodes = totalNew,
                    failures,
                    entries = summaries
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!options.Quiet)
            {
                Console.WriteLine();
                Console.WriteLine($"Checked {summaries.Count}, {totalNew} new episode(s), {failures} failed.");
            }

            SessionLogWriter.NoteRunFinished($"watch-run: {summaries.Count} checked, {totalNew} new, {failures} failed");

            // Nothing new is a perfectly good outcome for a scheduled run, so only real
            // failures are worth a non-zero exit.
            return failures > 0 ? ExitFailed : ExitCompleted;
        }

        /// <summary>
        /// Converts an existing folder rather than downloading anything.
        /// </summary>
        private static async Task<int> ConvertAsync(CommandLineOptions options)
        {
            var settingsService = new SettingsService();

            var toolManager = new ToolManagerService
            {
                AutoDownloadFfmpeg = !options.NoFfmpeg && settingsService.Settings.AutoDownloadFfmpeg
            };

            if (!options.Quiet) toolManager.OnToolLogReceived += line => Console.WriteLine($"  {line}");

            var (_, _, ffmpegPath) = await toolManager.EnsureToolsAvailableAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                Console.Error.WriteLine("ffmpeg is required to convert and could not be found.");
                return ExitFailed;
            }

            var converter = new MediaConverter(ffmpegPath);
            if (!options.Quiet) converter.OnLog += Console.WriteLine;
            converter.OnLog += SessionLogWriter.Append;

            if (options.ReplaceOriginals && !options.Quiet)
            {
                Console.WriteLine("Originals will be replaced once each conversion succeeds.");
            }

            var results = await converter
                .ConvertFolderAsync(options.ConvertFolder!, options.ReplaceOriginals)
                .ConfigureAwait(false);

            int converted = results.Count(r => r.Succeeded && !r.Skipped);
            int skipped = results.Count(r => r.Skipped);
            int failed = results.Count(r => !r.Succeeded && !r.Skipped);

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    converted,
                    skipped,
                    failed,
                    files = results.Select(r => new { source = r.SourcePath, output = r.OutputPath, r.Message })
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!options.Quiet)
            {
                Console.WriteLine();
                Console.WriteLine($"{converted} converted, {skipped} already fine, {failed} failed.");
            }

            SessionLogWriter.NoteRunFinished($"convert: {converted} converted, {skipped} skipped, {failed} failed");

            return failed > 0 ? ExitFailed : ExitCompleted;
        }

        private static string Summarise(DownloadJobResult result)
        {
            if (result.Cancelled) return "Cancelled.";

            if (!result.Completed)
            {
                return $"Failed: {result.FailureReason ?? "unknown reason"}";
            }

            string title = result.OfficialTitle ?? "Unknown";
            string where = result.OutputFolder ?? "(unknown folder)";

            string counts = $"{result.EpisodesSucceeded} succeeded";
            if (result.EpisodesFailed > 0) counts += $", {result.EpisodesFailed} failed";
            if (result.EpisodesProtected > 0) counts += $", {result.EpisodesProtected} DRM protected";

            string verification;

            if (result.EpisodesOffered > 0)
            {
                verification = $"{result.FilesPresentAfter} of the {result.EpisodesOffered} episode(s) this source offers";

                if (result.ExpectedEpisodeCount > result.EpisodesOffered)
                {
                    verification += $" (the databases list {result.ExpectedEpisodeCount} for the season)";
                }
            }
            else if (result.ExpectedEpisodeCount > 0)
            {
                verification = $"{result.FilesPresentAfter} of {result.ExpectedEpisodeCount} expected episode(s) present";
            }
            else
            {
                verification = $"{result.FilesPresentAfter} file(s) present";
            }

            return $"{title} season {result.SeasonNumber}: {counts}. "
                 + $"{result.FilesAdded} new this run; {verification}.\n{where}";
        }
    }
}
