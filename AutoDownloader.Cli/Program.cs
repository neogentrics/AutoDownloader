using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;
using System;
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
        private const string Version = "v1.11.0-beta";

        // Exit codes, chosen so a scheduler or n8n can branch on the outcome.
        private const int ExitCompleted = 0;
        private const int ExitFailed = 1;
        private const int ExitBadArguments = 2;
        private const int ExitNothingDownloaded = 3;
        private const int ExitCancelled = 130;

        public static async Task<int> Main(string[] args)
        {
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
                useArchive);

            var orchestrator = new DownloadOrchestrator(
                metadataService,
                new SearchService(settings.GeminiApiKey),
                new XmlService(),
                ytDlpService,
                new AutoConfirmPrompt());

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

            // ---------------------------------------------------------------- run
            var result = await orchestrator.RunAsync(
                options.Target!,
                outputFolder,
                cancellationToken,
                options.Season);

            renderer.Clear();

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
                    expectedEpisodeCount = result.ExpectedEpisodeCount,
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
            if (result.FilesAdded == 0 && result.EpisodesSucceeded == 0) return ExitNothingDownloaded;

            return ExitCompleted;
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

            string counts = result.EpisodesFailed > 0
                ? $"{result.EpisodesSucceeded} succeeded, {result.EpisodesFailed} failed"
                : $"{result.EpisodesSucceeded} succeeded";

            string verification = result.ExpectedEpisodeCount > 0
                ? $"{result.FilesPresentAfter} of {result.ExpectedEpisodeCount} expected episode(s) present"
                : $"{result.FilesPresentAfter} file(s) present";

            return $"{title} season {result.SeasonNumber}: {counts}. "
                 + $"{result.FilesAdded} new this run; {verification}.\n{where}";
        }
    }
}
