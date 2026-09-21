using AutoDownloader.Core; // For the data models (SettingsModel, DownloadMetadata)
using AutoDownloader.Services;
using AutoDownloader.Services.Scrapers;
using AutoDownloader.Services.Orchestration; // For all the logic (YtDlpService, DownloadOrchestrator, etc.)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Diagnostics;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// This class is the "Host" or "Orchestrator" for the entire application.
    /// Its job is to:
    /// 1. Initialize all services from the .Services project.
    /// 2. Handle all user UI events (button clicks, key presses).
    /// 3. Call the appropriate services to perform logic.
    /// 4. Receive data and events back from the services to update the UI (log, status bar).
    /// </summary>
    public partial class MainWindow : Window
    {
        // --- Service Fields (Injected from AutoDownloader.Services) ---

        private YtDlpService _ytDlpService = null!; // Initialized async in InitializeAsyncServices
        private SearchService _searchService = null!; // Initialized async
        private MetadataService _metadataService = null!; // Initialized async
        private readonly ToolManagerService _toolManagerService;
        private readonly SettingsService _settingsService;
        private readonly XmlService _xmlService;

        // --- UI & Logging Fields ---

        private readonly DispatcherTimer _logUpdateTimer;
        private readonly List<string> _logQueue = new List<string>();
        private readonly object _logLock = new object();
        private bool _isMultiLinkMode = false;

        /// <summary>
        /// Set when the user presses Stop. The batch loop used to infer cancellation from
        /// StopDownloadButton.Visibility, which meant any item that failed early (metadata not
        /// found, search failed) left Stop visible and aborted the remaining items with a
        /// bogus "cancelled by user" message.
        /// </summary>
        private CancellationTokenSource? _cancellation;

        /// <summary>
        /// The authoritative version number for the application.
        /// </summary>
        private static string CurrentVersion => AppInfo.Version;

        // --- Constructor & Initializers ---

        /// <summary>
        /// Main entry point for the application window.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // Start the session log before anything else, so startup problems are captured
            // too - those are exactly the ones nobody is watching the window for.
            SessionLogWriter.Start("ui");

            this.Title = $"AutoDownloader {CurrentVersion} - (For Personal Use Only)";

            // Keep the welcome banner in step with the real version.
            if (WelcomeVersionRun != null)
            {
                WelcomeVersionRun.Text = $"Welcome to AutoDownloader {CurrentVersion}!";
            }
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            // ---1. Initialize Synchronous Services (Order matters!) ---

            _settingsService = new SettingsService();
            _settingsService.LoadSettings(); // Loads keys and paths immediately

            _toolManagerService = new ToolManagerService();
            _xmlService = new XmlService(); // Ready for the v3.2 feature

            // NOTE for other developers:
            // We subscribe to the DeveloperLogger here so any part of the app can forward
            // verbose installer/scraper logs for debugging. The Developer Log UI is
            // optional (hidden by default) and intended as a troubleshooting aid.
            // Use DeveloperLogger.Append("message") from services to surface details.

            // Set Up Default Download Path in UI
            OutputFolderTextBox.Text = _settingsService.Settings.DefaultOutputFolder;
            Directory.CreateDirectory(_settingsService.Settings.DefaultOutputFolder);

            // Set Up Log Batching Timer
            _logUpdateTimer = new DispatcherTimer();
            _logUpdateTimer.Interval = TimeSpan.FromMilliseconds(100);
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            _logUpdateTimer.Start();

            // Start Asynchronous Initialization of services that rely on settings/tools
            _ = InitializeAsyncServices();

            // Subscribe to developer logger events
            // BeginInvoke, not Invoke: this fires from yt-dlp's output thread for every line
            // of a chatty download, and a blocking marshal per line stalls the UI thread.
            DeveloperLogger.OnLogReceived += (line) =>
            {
                SessionLogWriter.Append(line);

                Dispatcher.BeginInvoke(() =>
                {
                    if (DeveloperLogTextBox != null)
                    {
                        DeveloperLogTextBox.AppendText(line + "\n");
                        DeveloperLogTextBox.ScrollToEnd();
                    }
                });
            };
        }

        /// <summary>
        /// Asynchronously initializes all services that require API keys or tool checks.
        /// </summary>
        private async Task InitializeAsyncServices()
        {
            // Everything in here is wrapped: this method is fire-and-forgotten, so an
            // unhandled exception (no network on first run, GitHub unreachable) used to be
            // swallowed silently and leave the UI locked on "Initializing tools..." forever.
            try
            {
                await InitializeAsyncServicesCore();
            }
            catch (Exception ex)
            {
                AppendLog($"--- [FATAL] Startup failed: {ex.Message} ---", Brushes.Red);
                AppendLog("--- The app is usable, but downloads will not work until this is resolved. ---", Brushes.Orange);
                DeveloperLogger.Append($"InitializeAsyncServices failed: {ex}");
                StatusTextBlock.Text = "Startup failed - see log.";
                SetUiLock(false); // never leave the UI stuck locked
            }
        }

        private async Task InitializeAsyncServicesCore()
        {
            SetUiLock(true); // Lock the UI
            StatusTextBlock.Text = "Initializing tools (yt-dlp, aria2c, ffmpeg)...";

            // Wire up ToolManager logging (must be done before EnsureToolsAvailableAsync)
            _toolManagerService.OnToolLogReceived += (logLine) =>
            {
                // Keep the batched UI log queue in sync for performance (flushes on timer)
                lock (_logLock) { _logQueue.Add(logLine); }

                // Also forward raw tool/log output into the Developer Logger so
                // engineers can inspect full stdout/stderr later (exportable).
                // We intentionally forward unmodified lines to preserve original content.
                DeveloperLogger.Append(logLine);
            };

            //1. Download/Verify yt-dlp, aria2c and ffmpeg.
            _toolManagerService.AutoDownloadFfmpeg = _settingsService.Settings.AutoDownloadFfmpeg;
            var (ytDlpPath, ariaPath, ffmpegPath) = await _toolManagerService.EnsureToolsAvailableAsync();
            StatusTextBlock.Text = "Tools ready. Initializing API services...";

            //2. Initialize API-dependent services with keys from settings.
            _metadataService = new MetadataService(
                _settingsService.Settings.TmdbApiKey,
                _settingsService.Settings.TvdbApiKey
            );

            _searchService = new SearchService(_settingsService.Settings.GeminiApiKey);

            // Kick off a non-blocking task to ensure Playwright browsers are installed if user allows it in preferences.
            if (_settingsService.Settings.AutoInstallPlaywrightBrowsers)
            {
                // Run the installer in background but reuse the same installer code so retry can call it later
                _ = Task.Run(async () => await RunPlaywrightInstallerAsync());
            }
            else
            {
                AppendLog("--- Automatic Playwright browser install is disabled in Preferences. ---", Brushes.Yellow);
            }

            //3. Initialize the Download service, injecting the tool paths and settings.
            _ytDlpService = new YtDlpService(
                ytDlpPath,
                ariaPath,
                ToolManagerService.FIREFOX_USER_AGENT,
                _settingsService.Settings.PreferredVideoQuality,
                ffmpegPath,
                _settingsService.Settings.CookieSource,
                _settingsService.Settings.UseDownloadArchive,
                _settingsService.Settings.FormatPreference
            );

            //4. Wire up the event handlers for the download service.
            WireUpDlpEvents();

            //5. Run final checks and unlock the UI.
            ValidateApiKeysOnLaunch();
            StatusTextBlock.Text = "Ready.";
            SetUiLock(false); // Unlock the UI
        }

        /// <summary>
        /// Helper method to wire up YtDlpService events (used in init and preferences reload).
        /// </summary>
        private void WireUpDlpEvents()
        {
            // Do not attempt to assign to events directly. Attach handlers only.
            // The _ytDlpService instance is recreated when preferences change, so duplicate subscriptions are unlikely.

            _ytDlpService.OnDownloadComplete += (exitCode) =>
            {
                // Report status only. Unlocking here released the UI in the middle of a batch;
                // the batch loop owns the lock now.
                Dispatcher.BeginInvoke(() =>
                {
                    StatusTextBlock.Text = exitCode ==0 ? "Download complete" : "Download failed or stopped";
                });
            };

            _ytDlpService.OnOutputReceived += (logLine) =>
            {
                // yt-dlp outputs can be very chatty. We keep them batched to the UI
                // (reduces cross-thread updates) and forward them to DeveloperLogger
                // (timestamps and persistence) for post-mortem analysis.
                lock (_logLock) { _logQueue.Add(logLine); }
                DeveloperLogger.Append(logLine);
            };
        }

        // --- Core Application Logic ---
        /// <summary>
        /// Runs one download job by handing it to the orchestrator.
        ///
        /// The pipeline itself now lives in AutoDownloader.Services.Orchestration, so it can
        /// also be driven headlessly. This method only adapts it to the window: events in,
        /// log lines and status text out.
        /// </summary>
        private async Task ProcessSingleDownloadAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return;

            // Guard against a click landing before async initialisation has finished.
            if (_ytDlpService == null || _metadataService == null || _searchService == null)
            {
                AppendLog("--- ERROR: Services are still initializing. Please wait a moment and try again. ---", Brushes.Red);
                return;
            }

            var orchestrator = new DownloadOrchestrator(
                _metadataService,
                _searchService,
                _xmlService,
                _ytDlpService,
                new WpfUserPrompt(this))
            {
                CookieSource = _settingsService.Settings.CookieSource
            };

            orchestrator.OnLog += (_, e) => Dispatcher.Invoke(() => AppendLog(e.Message, BrushForLevel(e.Level)));
            orchestrator.OnStatusChanged += text => Dispatcher.BeginInvoke(() => StatusTextBlock.Text = text);
            orchestrator.OnDiagnostic += line => DeveloperLogger.Append(line);
            orchestrator.OnProgress += p => Dispatcher.BeginInvoke(() => ShowProgress(p));

            try
            {
                await orchestrator.RunAsync(
                    searchTerm,
                    OutputFolderTextBox.Text,
                    _cancellation?.Token ?? CancellationToken.None);
            }
            finally
            {
                HideProgress();
            }
        }

        /// <summary>
        /// Updates the status-bar progress display.
        ///
        /// The bar shows progress across the whole job rather than the current file, because
        /// "62% of episode 7 of 12" is what someone actually wants to know; the per-file
        /// percentage is in the detail text beside it.
        /// </summary>
        private void ShowProgress(JobProgress progress)
        {
            ProgressPanel.Visibility = Visibility.Visible;

            double? overall = progress.OverallPercent;

            if (overall.HasValue)
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = overall.Value;
                ProgressPercentTextBlock.Text = $"{overall.Value:0}%";
            }
            else if (progress.File?.Percent is double filePercent)
            {
                // No episode count to scale against - show the file's own progress instead.
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = filePercent;
                ProgressPercentTextBlock.Text = $"{filePercent:0}%";
            }
            else
            {
                DownloadProgressBar.IsIndeterminate = true;
                ProgressPercentTextBlock.Text = string.Empty;
            }

            var parts = new List<string>();

            if (progress.EpisodeCount > 1 && progress.EpisodeIndex > 0)
            {
                string label = string.IsNullOrEmpty(progress.EpisodeLabel)
                    ? $"{progress.EpisodeIndex}/{progress.EpisodeCount}"
                    : $"{progress.EpisodeLabel} ({progress.EpisodeIndex}/{progress.EpisodeCount})";
                parts.Add(label);
            }

            if (progress.File?.Percent is double pct) parts.Add($"{pct:0.0}%");
            if (!string.IsNullOrWhiteSpace(progress.File?.Speed)) parts.Add(progress.File!.Speed!);
            if (!string.IsNullOrWhiteSpace(progress.File?.Eta)) parts.Add($"ETA {progress.File!.Eta}");

            ProgressDetailTextBlock.Text = string.Join("   ", parts);
            ProgressDetailTextBlock.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Clears the progress display once a job ends, so a stale bar does not linger.
        /// </summary>
        private void HideProgress()
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ProgressDetailTextBlock.Visibility = Visibility.Collapsed;
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 0;

        }

        /// <summary>
        /// Maps an orchestrator log level onto the colour the log window uses.
        /// </summary>
        private static SolidColorBrush BrushForLevel(JobLogLevel level) => level switch
        {
            JobLogLevel.Success => Brushes.Green,
            JobLogLevel.Notice => Brushes.Yellow,
            JobLogLevel.Warning => Brushes.Orange,
            JobLogLevel.Error => Brushes.Red,
            _ => Brushes.Aqua,
        };

        // --- UI Event Handlers & Helpers ---

        /// <summary>
        /// This method runs10x per second to "flush" all queued logs
        /// to the screen at once.
        /// </summary>
        private void LogUpdateTimer_Tick(object? sender, EventArgs e)
        {
            List<string> logsToFlush;
            lock (_logLock)
            {
                if (_logQueue.Count ==0) return;
                logsToFlush = new List<string>(_logQueue);
                _logQueue.Clear();
            }

            if (logsToFlush.Count >0)
            {
                var sb = new StringBuilder();
                foreach (var line in logsToFlush)
                {
                    sb.AppendLine(line);
                }

                var p = new Paragraph(new Run(sb.ToString()));
                string logText = sb.ToString();
                if (!string.IsNullOrEmpty(logText))
                {
                    // Coloring logic for different output types
                    if (logText.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Foreground = Brushes.Red;
                    }
                    else if (logText.StartsWith("[download]", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Foreground = Brushes.Green;
                    }
                    else if (logText.StartsWith("---", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Foreground = Brushes.Aqua;
                    }
                    else if (logText.Contains("Downloading tool:") || logText.Contains("Extracting tool:"))
                    {
                        p.Foreground = Brushes.Orange;
                    }
                }
                OutputLogTextBox.Document.Blocks.Add(p);
                LogScrollViewer.ScrollToEnd();
            }
        }

        /// <summary>
        /// Handles the "Enter" key in the text box.
        /// </summary>
        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StartDownloadButton_Click(sender, e);
            }
        }

        /// <summary>
        /// Handles the "Browse..." button click.
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = OutputFolderTextBox.Text
            };

            if (dialog.ShowDialog(this) == true)
            {
                OutputFolderTextBox.Text = dialog.FolderName;
            }
        }

        /// <summary>
        /// Handles the "Stop" button click.
        /// </summary>
        private void StopDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellation?.Cancel();
            _ytDlpService?.StopDownload();
            StatusTextBlock.Text = "Stopping download...";
        }

        /// <summary>
        /// Helper to lock/unlock the UI during a download.
        /// </summary>
        private void SetUiLock(bool isLocked)
        {
            UrlTextBox.IsEnabled = !isLocked;
            OutputFolderTextBox.IsEnabled = !isLocked;
            BrowseButton.IsEnabled = !isLocked;
            StartDownloadButton.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
            StopDownloadButton.Visibility = isLocked ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// A helper to add a single, colored log line to the UI.
        /// </summary>
        private void AppendLog(string message, SolidColorBrush color)
        {
            SessionLogWriter.Append(message);

            var p = new Paragraph(new Run(message));
            p.Foreground = color;
            OutputLogTextBox.Document.Blocks.Add(p);
            LogScrollViewer.ScrollToEnd();
            try { DeveloperLogger.Append(message); } catch { }
        }

        /// <summary>
        /// Handles the "File -> Exit" menu item click.
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Handles the "Edit -> Preferences..." menu item click.
        /// </summary>
        private void Preferences_Click(object sender, RoutedEventArgs e)
        {
            PreferencesWindow settingsWindow = new PreferencesWindow(_settingsService) { Owner = this };
            settingsWindow.ShowDialog();

            // ** CRITICAL RELOAD FIX **
            // After the user saves settings, we must re-load everything to apply changes.
            _settingsService.LoadSettings();

            //1. Re-initialize services that depend on new keys.
            _metadataService = new MetadataService(
                _settingsService.Settings.TmdbApiKey,
                _settingsService.Settings.TvdbApiKey
            );
            _searchService = new SearchService(_settingsService.Settings.GeminiApiKey);

            //2. Re-initialize YtDlpService with the (potentially new) video quality setting.
            // We use GetToolPaths() to avoid re-downloading tools.
            var (ytDlpPath, ariaPath, ffmpegPath) = _toolManagerService.GetToolPaths();
            _ytDlpService = new YtDlpService(
               ytDlpPath,
               ariaPath,
               ToolManagerService.FIREFOX_USER_AGENT,
               _settingsService.Settings.PreferredVideoQuality,
               ffmpegPath,
               _settingsService.Settings.CookieSource,
               _settingsService.Settings.UseDownloadArchive,
               _settingsService.Settings.FormatPreference
           );

            //3. Re-wire events for the new service instance.
            WireUpDlpEvents();

            //4. Update the UI.
            OutputFolderTextBox.Text = _settingsService.Settings.DefaultOutputFolder;
            AppendLog("Preferences updated. API keys and settings reloaded.", Brushes.Yellow);
        }

        /// <summary>
        /// Handles the "Help -> About" menu item click.
        /// </summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            string version = CurrentVersion;

            // Updated About text for v1.10.0-beta
            string aboutText = $@"
AutoDownloader - Version {version}
Developed By: Neo Gentrics
AI Development Partner: Gemini (Google)
--- Core Technologies ---
 - High-Speed Downloads: yt-dlp, aria2c
 - Smart Search: Google Gemini API
 - Metadata: TMDB API & TVDB API
 - Framework: .NET9.0 (WPF)
--- v1.10.0-beta Highlights ---
 - Playwright-based scraper fallback for JS/Cloudflare-protected sites (automatic browser install available in Preferences)
 - TVDB reflection-based episode extraction and local caching
 - Multi-scraper engine (AngleSharp + Playwright) with per-site scrapers
 - Developer Log: detailed installer/scraper logs (View -> Log). Export/Retry Playwright install from there.
 - Safe file/folder name sanitization to avoid invalid paths on Windows
 - Metadata XML persistence: saves per-series metadata to 'series_metadata.xml'
 - CI workflow: installs Playwright browsers during Windows CI builds to produce ready-to-run artifacts

--- Usage Notes ---
 - Edit -> Preferences... to set TMDB and Gemini API keys (TMDB required for naming/verification).
 - Toggle automatic Playwright browser install in Preferences if you want the app to attempt browser installs automatically.
 - Use the Developer Log to inspect detailed output and to retry Playwright installation when needed.

--- Recent Improvements ---
 - Improved URL parsing and season detection
 - Better scraper reliability and fallback strategies
 - Safer folder naming and improved error logging

Thank you for using AutoDownloader. For issues or feature requests, open an issue on the project's GitHub page.
";
            MessageBox.Show(aboutText, "About AutoDownloader", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Toggles between single-link (TextBox) and multi-link (RichTextBox) mode.
        /// </summary>
        /// <summary>
        /// The hint shown in the multi-link box. It is the box's Text rather than a
        /// watermark, so it must be recognised and ignored wherever that text is read -
        /// otherwise the app goes looking for a show by that name.
        /// </summary>
        private const string MultiUrlPlaceholder = "Enter one URL or search term per line.";

        /// <summary>
        /// Clears the hint the first time the box is used, so it does not have to be
        /// deleted by hand before typing.
        /// </summary>
        private void MultiUrlTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (MultiUrlTextBox.Text.Trim() == MultiUrlPlaceholder)
            {
                MultiUrlTextBox.Clear();
            }
        }

        private void MultiLinkToggle_Click(object sender, RoutedEventArgs e)
        {
            _isMultiLinkMode = !_isMultiLinkMode;

            if (_isMultiLinkMode)
            {
                UrlLabel.Content = "URLs or Search Terms (One per line):";
                UrlTextBox.Visibility = Visibility.Collapsed;
                MultiUrlTextBox.Visibility = Visibility.Visible;
                MultiLinkToggle.Content = "Toggle Single-Link";
                MultiUrlTextBox.Focus();
                AppendLog("--- Multi-Link Mode Activated. ---", Brushes.Yellow);
            }
            else
            {
                UrlLabel.Content = "Search Term or URL:";
                UrlTextBox.Visibility = Visibility.Visible;
                MultiUrlTextBox.Visibility = Visibility.Collapsed;
                MultiLinkToggle.Content = "Toggle Multi-Link";
                AppendLog("--- Single-Link Mode Activated. ---", Brushes.Yellow);
            }
        }

        /// <summary>
        /// Handles the "Download" button click. This is the main orchestrator for batch jobs.
        /// </summary>
        private async void StartDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiLock(true);
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            OutputLogTextBox.Document.Blocks.Clear(); // Clear the log
            StatusTextBlock.Text = "Starting...";

            //1. Get the list of search terms/URLs
            List<string> searchTerms;
            if (_isMultiLinkMode)
            {
                searchTerms = MultiUrlTextBox.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0 && s != MultiUrlPlaceholder)
                    .ToList();
            }
            else
            {
                searchTerms = new List<string> { UrlTextBox.Text.Trim() };
            }

            if (searchTerms.Count ==0 || (searchTerms.Count ==1 && string.IsNullOrWhiteSpace(searchTerms[0])))
            {
                AppendLog("Please enter at least one URL or search term.", Brushes.Red);
                SetUiLock(false);
                return;
            }

            //2. Process each item sequentially
            AppendLog($"--- Starting Batch Download ({searchTerms.Count} items) ---", Brushes.Aqua);

            foreach (var term in searchTerms)
            {
                if (string.IsNullOrWhiteSpace(term)) continue;

                AppendLog($"\n--- Processing Item: {term} ---", Brushes.Aqua);

                // Keep the UI locked for the whole batch. Previously each completed download
                // unlocked it mid-batch, which let a second click start a concurrent run.
                SetUiLock(true);

                await ProcessSingleDownloadAsync(term);

                if (_cancellation?.IsCancellationRequested == true)
                {
                    AppendLog("--- Batch operation cancelled by user. ---", Brushes.Red);
                    SetUiLock(false);
                    return;
                }
            }

            AppendLog("\n--- Batch Download Finished. ---", Brushes.Aqua);
            StatusTextBlock.Text = "Batch complete.";
            SetUiLock(false);
        }

        /// <summary>
        /// Checks for critical missing API keys on launch and notifies the user.
        /// </summary>
        private void ValidateApiKeysOnLaunch()
        {
            string message = ""; // This variable holds all warning/error text.

            bool tmdbKeyMissing = !_metadataService.IsTmdbKeyValid;
            if (tmdbKeyMissing)
            {
                message += "CRITICAL: The TMDB API Key is missing or invalid. TMDB metadata search will fail.\n\n";
            }

            // v1.9.2: Check for TVDB key
            bool tvdbKeyMissing = !_metadataService.IsTvdbKeyValid;
            if (tvdbKeyMissing)
            {
                if (!string.IsNullOrEmpty(message)) message += "\n---\n\n";
                message += "WARNING: The TVDB API Key is missing. Fallback search for Anime and other shows will be disabled.\n\n";
            }

            bool geminiKeyMissing = string.IsNullOrWhiteSpace(_settingsService.Settings.GeminiApiKey) ||
                                    _settingsService.Settings.GeminiApiKey == "YOUR_GEMINI_API_KEY_HERE";
            if (geminiKeyMissing)
            {
                if (!string.IsNullOrEmpty(message)) message += "\n---\n\n";
                message += "WARNING: The Gemini API Key is missing. Smart Search (non-URL) functionality will be disabled.\n\n";
            }

            if (!string.IsNullOrEmpty(message))
            {
                message += "Please get the required keys and enter them in the 'Edit -> Preferences...' menu.";
                MessageBox.Show(
                    message,
                    "API Key Validation Warning",
                    MessageBoxButton.OK,
                    tmdbKeyMissing ? MessageBoxImage.Error : MessageBoxImage.Warning // Show Error icon if TMDB is missing
                );
            }
        }

        /// <summary>
        /// Handles the Ctrl+P keyboard shortcut to open the Preferences window.
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                Preferences_Click(sender, e);
            }
        }

        private void ToggleDeveloperLog_Click(object sender, RoutedEventArgs e)
        {
         if (DeveloperLogPanel.Visibility == Visibility.Visible)
         {
          DeveloperLogPanel.Visibility = Visibility.Collapsed;
         }
         else
         {
          DeveloperLogPanel.Visibility = Visibility.Visible;
         }
        }

        /// <summary>
        /// Opens the watch list. The orchestrator is built on demand so a check uses the
        /// current settings and a non-interactive prompt.
        /// </summary>
        private void WatchList_Click(object sender, RoutedEventArgs e)
        {
            var window = new WatchListWindow(_settingsService, BuildOrchestrator) { Owner = this };

            window.OnCheckLog += (message, level) =>
                Dispatcher.BeginInvoke(() => AppendLog(message, BrushForLevel(level)));

            window.ShowDialog();
        }

        /// <summary>
        /// Builds an orchestrator with the current services, or null if they are not ready.
        /// </summary>
        private DownloadOrchestrator? BuildOrchestrator(IUserPrompt prompt)
        {
            if (_ytDlpService == null || _metadataService == null || _searchService == null) return null;

            return new DownloadOrchestrator(
                _metadataService, _searchService, _xmlService, _ytDlpService, prompt)
            {
                CookieSource = _settingsService.Settings.CookieSource
            };
        }

        /// <summary>
        /// Shows which sites yt-dlp supports, so the answer is visible rather than guessed at.
        /// </summary>
        private void SupportedSites_Click(object sender, RoutedEventArgs e)
        {
            var (ytDlpPath, _, _) = _toolManagerService.GetToolPaths();

            if (string.IsNullOrWhiteSpace(ytDlpPath))
            {
                AppendLog("yt-dlp is still being set up; try again in a moment.", Brushes.Orange);
                return;
            }

            new SupportedSitesWindow(ytDlpPath) { Owner = this }.ShowDialog();
        }

        /// <summary>
        /// Opens the folder holding the automatic session logs.
        /// </summary>
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(SessionLogWriter.LogFolder);
                Process.Start(new ProcessStartInfo(SessionLogWriter.LogFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog($"Could not open the log folder: {ex.Message}", Brushes.Red);
            }
        }

        /// <summary>
        /// Copies this session's log path, so it can be pasted somewhere useful.
        /// </summary>
        private void CopyLogPath_Click(object sender, RoutedEventArgs e)
        {
            string? path = SessionLogWriter.CurrentPath;

            if (string.IsNullOrEmpty(path))
            {
                AppendLog("No session log is being written.", Brushes.Orange);
                return;
            }

            try
            {
                Clipboard.SetText(path);
                AppendLog($"Log path copied: {path}", Brushes.Green);
            }
            catch (Exception ex)
            {
                AppendLog($"Could not copy the path: {ex.Message}. The log is at: {path}", Brushes.Orange);
            }
        }

        private void DeveloperLogClear_Click(object sender, RoutedEventArgs e)
        {
         DeveloperLogTextBox.Clear();
        }

        private void DeveloperLogExport_Click(object sender, RoutedEventArgs e)
        {
         try
         {
          var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Text Files|*.txt", FileName = "developer_log.txt" };
          if (dlg.ShowDialog() == true)
          {
           File.WriteAllText(dlg.FileName, DeveloperLogTextBox.Text);
           AppendLog($"Developer log exported to: {dlg.FileName}", Brushes.Green);
          }
         }
         catch (Exception ex)
         {
          AppendLog($"Failed to export developer log: {ex.Message}", Brushes.Red);
         }
        }

        /// <summary>
        /// Runs the Playwright installation flow (reflection, create+launch, script, or CLI fallback).
        /// Returns true if installation/detection succeeded, false otherwise.
        /// Also forwards verbose output to DeveloperLogger.
        /// </summary>
        private async Task<bool> RunPlaywrightInstallerAsync()
        {
            // This method implements a multi-strategy approach to ensure Playwright
            // browser binaries are available to the app. We try lighter-weight
            // detection first (reflection/create+launch) to avoid unnecessary network
            // use. If that fails, we run a generated PowerShell installer script
            // (playwright.ps1) if present; finally we fall back to the 'playwright'
            // CLI. All outputs are forwarded to DeveloperLogger for debugging.
 try
 {
                // Try reflection InstallAsync
                var playwrightType = Type.GetType("Microsoft.Playwright.Playwright, Microsoft.Playwright");
                if (playwrightType != null)
                {
                    var installMethod = playwrightType.GetMethod("InstallAsync", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                    if (installMethod != null)
                    {
                        try
                        {
                            var installTask = (Task?)installMethod.Invoke(null, null);
                            if (installTask != null) await installTask.ConfigureAwait(false);
                            Dispatcher.Invoke(() => AppendLog("--- Playwright browsers installed (reflection) ---", Brushes.Green));
                            DeveloperLogger.Append("Playwright install via reflection succeeded.");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => AppendLog($"--- Playwright reflection install failed: {ex.Message} ---", Brushes.Orange));
                            DeveloperLogger.Append($"Reflection install failed: {ex}");
                        }
                    }
                }

                // Try create and launch to detect already-present browsers
                try
                {
                    var pl = await Microsoft.Playwright.Playwright.CreateAsync();
                    try
                    {
                        await pl.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
                        Dispatcher.Invoke(() => AppendLog("--- Playwright is usable; browsers present. ---", Brushes.Green));
                        DeveloperLogger.Append("Playwright create+launch succeeded; browsers present.");
                        return true;
                    }
                    catch (Exception launchEx)
                    {
                        Dispatcher.Invoke(() => AppendLog($"--- Playwright browsers missing or launch failed: {launchEx.Message} ---", Brushes.Orange));
                        DeveloperLogger.Append($"Playwright launch failed: {launchEx}");

                        // Continue to installer fallbacks below
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog($"--- Playwright initialization failed: {ex.Message} ---", Brushes.Orange));
                    DeveloperLogger.Append($"Playwright CreateAsync failed: {ex}");
                }

                // Look for playwright.ps1 and run it via pwsh/powershell if present
                try
                {
                    string? scriptPath = null;
                    string baseDir = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
                    string[] candidates = new string[] {
                        Path.Combine(baseDir, "playwright.ps1"),
                        Path.Combine(baseDir, "..", "playwright.ps1"),
                        Path.Combine(baseDir, "bin", "Debug", "net9.0-windows", "playwright.ps1"),
                        Path.Combine(baseDir, "..", "..", "playwright.ps1")
                    };
                    foreach (var c in candidates)
                    {
                        try { var p = Path.GetFullPath(c); if (File.Exists(p)) { scriptPath = p; break; } } catch { }
                    }

                    if (!string.IsNullOrEmpty(scriptPath))
                    {
                        DeveloperLogger.Append($"Found playwright script at: {scriptPath}");
                        var shell = "pwsh";
                        var psiShell = new ProcessStartInfo(shell, $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" install")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };

                        Process? procShell = null;
                        try { procShell = Process.Start(psiShell); }
                        catch (Exception)
                        {
                            // try windows powershell if pwsh not found
                            shell = "powershell";
                            psiShell = new ProcessStartInfo(shell, $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" install")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };
                            procShell = Process.Start(psiShell);
                        }

                        if (procShell != null)
                        {
                          string outp = await procShell.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                          string err = await procShell.StandardError.ReadToEndAsync().ConfigureAwait(false);
                          await procShell.WaitForExitAsync().ConfigureAwait(false);
                          Dispatcher.Invoke(() => AppendLog($"--- Playwright script install finished. Output len: {outp.Length}. Error len: {err.Length} ---", Brushes.Yellow));
                          DeveloperLogger.Append($"playwright.ps1 stdout:\n{outp}");
                          if (!string.IsNullOrWhiteSpace(err)) DeveloperLogger.Append($"playwright.ps1 stderr:\n{err}");

                          // After running script, try create+launch again
                          try
                          {
                            var pl2 = await Microsoft.Playwright.Playwright.CreateAsync();
                            await pl2.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
                            Dispatcher.Invoke(() => AppendLog("--- Playwright browsers now installed and usable. ---", Brushes.Green));
                            DeveloperLogger.Append("Playwright browsers installed via script and are now usable.");
                            return true;
                          }
                          catch (Exception ex)
                          {
                            // If post-install launch fails it usually indicates the
                            // installer didn't complete successfully or the runtime
                            // can't access the extracted browser files. The developer
                            // log contains the full stderr to investigate further.
                            DeveloperLogger.Append($"Post-install launch failed: {ex}");
                            return false;
                          }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DeveloperLogger.Append($"Playwright script execution error: {ex}");
                }

                // Final fallback: attempt 'playwright install chromium' via CLI
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "playwright",
                        Arguments = "install chromium",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string outp2 = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                        string err2 = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                        await proc.WaitForExitAsync().ConfigureAwait(false);
                        Dispatcher.Invoke(() => AppendLog($"--- Playwright CLI install finished. Output length: {outp2.Length}. Error length: {err2.Length} ---", Brushes.Yellow));
                        DeveloperLogger.Append($"playwright CLI stdout:\n{outp2}");
                        if (!string.IsNullOrWhiteSpace(err2)) DeveloperLogger.Append($"playwright CLI stderr:\n{err2}");

                        // Try create+launch after CLI
                        try
                        {
                          var pl3 = await Microsoft.Playwright.Playwright.CreateAsync();
                          await pl3.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
                          Dispatcher.Invoke(() => AppendLog("--- Playwright browsers now installed (CLI) and usable. ---", Brushes.Green));
                          DeveloperLogger.Append("Playwright browsers installed via CLI and are now usable.");
                          return true;
                        }
                        catch (Exception ex)
                        {
                          DeveloperLogger.Append($"Post-CLI launch failed: {ex}");
                          return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DeveloperLogger.Append($"Playwright CLI install attempt failed: {ex}");
                }

                return false;
            }
            catch (Exception ex)
            {
                DeveloperLogger.Append($"RunPlaywrightInstallerAsync unhandled error: {ex}");
                return false;
            }
        }

        private async void RetryPlaywrightInstall_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("--- Manual Playwright install retry started... ---", Brushes.Aqua);
            DeveloperLogger.Append("User triggered manual Playwright install retry.");
            bool ok = await RunPlaywrightInstallerAsync();
            if (ok) AppendLog("--- Playwright install succeeded. ---", Brushes.Green);
            else AppendLog("--- Playwright install failed. Check Developer Log for details. ---", Brushes.Orange);
        }

        // Helper to sanitize strings for use as file/folder names on Windows
    }
}