using AutoDownloader.Core; // For the data models (SettingsModel, DownloadMetadata)
using AutoDownloader.Services; // For all the logic (YtDlpService, SettingsService, etc.)
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        /// The authoritative version number for the application.
        /// </summary>
        private const string CurrentVersion = "v1.10.0-beta";

        // --- Constructor & Initializers ---

        /// <summary>
        /// Main entry point for the application window.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            this.Title = $"AutoDownloader {CurrentVersion} - (For Personal Use Only)";
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            // ---1. Initialize Synchronous Services (Order matters!) ---

            _settingsService = new SettingsService();
            _settingsService.LoadSettings(); // Loads keys and paths immediately

            _toolManagerService = new ToolManagerService();
            _xmlService = new XmlService(); // Ready for the v3.2 feature

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
        }

        /// <summary>
        /// Asynchronously initializes all services that require API keys or tool checks.
        /// </summary>
        private async Task InitializeAsyncServices()
        {
            SetUiLock(true); // Lock the UI
            StatusTextBlock.Text = "Initializing tools (yt-dlp, aria2c)...";

            // Wire up ToolManager logging (must be done before EnsureToolsAvailableAsync)
            _toolManagerService.OnToolLogReceived += (logLine) =>
            {
                lock (_logLock) { _logQueue.Add(logLine); }
            };

            //1. Download/Verify yt-dlp and aria2c.
            var (ytDlpPath, ariaPath) = await _toolManagerService.EnsureToolsAvailableAsync();
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
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Try to find a static InstallAsync via reflection (some Playwright builds expose it)
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
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    Dispatcher.Invoke(() => AppendLog($"--- Playwright reflection install failed: {ex.Message} ---", Brushes.Orange));
                                }
                            }
                        }

                        // Fallback: try to create Playwright and launch Chromium to detect presence
                        try
                        {
                            var pl = await Microsoft.Playwright.Playwright.CreateAsync();
                            try
                            {
                                await pl.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });
                                Dispatcher.Invoke(() => AppendLog("--- Playwright is usable; browsers present. ---", Brushes.Green));
                            }
                            catch (Exception launchEx)
                            {
                                Dispatcher.Invoke(() => AppendLog($"--- Playwright browsers missing or launch failed: {launchEx.Message} ---", Brushes.Orange));

                                // Try CLI fallback: run `playwright install chromium` if 'playwright' CLI is available
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
                                        string outp = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                                        string err = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
                                        await proc.WaitForExitAsync().ConfigureAwait(false);
                                        Dispatcher.Invoke(() => AppendLog($"--- Playwright CLI install finished. Output length: {outp.Length}. Error length: {err.Length} ---", Brushes.Yellow));
                                    }
                                }
                                catch (Exception cliEx)
                                {
                                    Dispatcher.Invoke(() => AppendLog($"--- Playwright CLI install failed: {cliEx.Message} ---", Brushes.Orange));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => AppendLog($"--- Playwright initialization failed: {ex.Message} ---", Brushes.Orange));
                        }
                    }
                    catch { /* swallow */ }
                });
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
                _settingsService.Settings.PreferredVideoQuality
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
                Dispatcher.Invoke(() =>
                {
                    SetUiLock(false);
                    StatusTextBlock.Text = exitCode ==0 ? "Download complete" : "Download failed or stopped";
                });
            };

            _ytDlpService.OnOutputReceived += (logLine) =>
            {
                lock (_logLock) { _logQueue.Add(logLine); }
            };
        }

        // --- Core Application Logic ---

        /// <summary>
        /// This is the main download orchestration method, called by StartDownloadButton_Click.
        /// </summary>
        private async Task ProcessSingleDownloadAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return;

            // CRITICAL: Guard against premature button click.
            if (_ytDlpService == null || _metadataService == null || _searchService == null)
            {
                AppendLog("--- ERROR: Services are still initializing. Please wait a moment and try again. ---", Brushes.Red);
                return;
            }

            string baseOutputFolder = OutputFolderTextBox.Text;
            string finalUrl = searchTerm;
            string finalOutputFolder = baseOutputFolder;
            string searchTarget = searchTerm;

            DownloadMetadata metadataToPass = new DownloadMetadata { SourceUrl = finalUrl };

            // --- PHASE1: Determine the Final Download URL ---
            if (!searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // Case A: Search Term
                AppendLog($"--- Starting Gemini web search for: '{searchTarget}' ---", Brushes.Aqua);
                StatusTextBlock.Text = $"Searching for content: {searchTarget}";
                try
                {
                    var (type, url) = await _searchService.FindShowUrlAsync(searchTarget);
                    if (url == "not-found")
                    {
                        AppendLog($"Could not find a download page for '{searchTarget}'.", Brushes.Red);
                        AppendLog("--- Search failed. ---", Brushes.Red);
                        return;
                    }
                    finalUrl = url;
                    metadataToPass.SourceUrl = url;
                    string category = type.Contains("Anime") ? "Anime TV Shows" :
                                        type.Contains("TV Show") ? "TV Shows" : "Playlists";
                    finalOutputFolder = Path.Combine(baseOutputFolder, category);
                    Directory.CreateDirectory(finalOutputFolder);
                }
                catch (Exception ex)
                {
                    AppendLog($"--- Smart search failed: {ex.Message} ---", Brushes.Red);
                    return;
                }
            }
            else
            {
                // Case B: Direct URL
                AppendLog("--- Direct URL detected. Skipping Gemini search. ---", Brushes.Aqua);
                finalOutputFolder = Path.Combine(baseOutputFolder, "TV Shows");
                Directory.CreateDirectory(finalOutputFolder);

                // Parse the URL for a name and (hopefully) a season number.
                var (parsedName, parsedSeason) = await ParseMetadataFromUrlAsync(searchTerm);

                searchTarget = parsedName;
                if (parsedSeason.HasValue)
                {
                    metadataToPass.NextSeasonNumber = parsedSeason.Value;
                    AppendLog($"Detected Season: {parsedSeason.Value} from URL.", Brushes.Yellow);
                }

                // Try site-specific scraping to find playable episode URLs (AngleSharp). If found, prefer first playable link as source.
                try
                {
                    var scraper = ScraperFactory.GetScraperForUrl(searchTerm);
                    if (scraper != null)
                    {
                        var playables = await scraper.GetPlayableUrlsAsync(searchTerm);
                        if (playables != null && playables.Count >0)
                        {
                            // Prefer first playable URL
                            metadataToPass.SourceUrl = playables[0];
                            AppendLog($"Scraper found {playables.Count} playable link(s). Using first: {playables[0]}", Brushes.Yellow);
                        }
                        else
                        {
                            AppendLog("Scraper did not find playable links (site may be JS-protected). Falling back to original URL.", Brushes.Orange);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Scraper error: {ex.Message}. Falling back to original URL.", Brushes.Orange);
                }
            }

            // --- PHASE2: Official Metadata Lookup (v1.9.2 Pop-up Strategy) ---
            AppendLog($"--- Starting metadata lookup for: '{searchTarget}' ---", Brushes.Aqua);
            StatusTextBlock.Text = $"Looking up official metadata for: {searchTarget}";

            // Step1: Confirm the show name (v1.9.2 Pop-up)
            ConfirmNameWindow confirmDialog = new ConfirmNameWindow(searchTarget) { Owner = this };
            bool? confirmResult = confirmDialog.ShowDialog();

            if (confirmResult != true)
            {
                AppendLog("--- Metadata lookup cancelled by user. Aborting. ---", Brushes.Red);
                return;
            }

            string confirmedSearchTarget = confirmDialog.ShowName;

            // Step2: Select the database (v1.9.2 Pop-up)
            SelectDatabaseWindow selectDbDialog = new SelectDatabaseWindow(
                _metadataService.IsTmdbKeyValid,
                _metadataService.IsTvdbKeyValid
            )
            { Owner = this };
            selectDbDialog.ShowDialog();
            DatabaseSource dbChoice = selectDbDialog.SelectedSource;

            // Step3: Run the selected metadata search
            (string OfficialTitle, int SeriesId, int TargetSeasonNumber, int ExpectedEpisodeCount)? metadataResult = null;

            if (dbChoice == DatabaseSource.TMDB)
            {
                AppendLog($"--- Searching TMDB for: '{confirmedSearchTarget}' ---", Brushes.Aqua);
                metadataResult = await _metadataService.GetTmdbMetadataAsync(confirmedSearchTarget);
            }
            else if (dbChoice == DatabaseSource.TVDB)
            {
                AppendLog($"--- Searching TVDB for: '{confirmedSearchTarget}' ---", Brushes.Aqua);
                metadataResult = await _metadataService.GetTvdbMetadataAsync(confirmedSearchTarget);
            }
            else
            {
                AppendLog("--- No database selected or canceled. Aborting. ---", Brushes.Red);
                return;
            }

            // Step4: Process Metadata and Save XML
            if (metadataResult != null)
            {
                var (officialTitle, seriesId, targetSeasonNumber, expectedCount) = metadataResult.Value;

                AppendLog($"Official Title Found: {officialTitle}", Brushes.Yellow);
                AppendLog($"Metadata Source: {dbChoice}", Brushes.Yellow);

                // If the URL parser found a season (Case B), override the default season1.
                if (metadataToPass.NextSeasonNumber !=1)
                {
                    AppendLog($"Using Season {metadataToPass.NextSeasonNumber} (Detected from URL)", Brushes.Yellow);
                }
                else
                {
                    metadataToPass.NextSeasonNumber = targetSeasonNumber; // Use the one from metadata (usually1)
                }

                AppendLog($"Expected Episode Count: {expectedCount}", Brushes.Yellow);

                metadataToPass.OfficialTitle = officialTitle;
                metadataToPass.SeriesId = seriesId;
                metadataToPass.ExpectedEpisodeCount = expectedCount;

                // Create final output folder (e.g., ".../TV Shows/The Mandalorian")
                finalOutputFolder = Path.Combine(baseOutputFolder, "TV Shows", metadataToPass.OfficialTitle);
                Directory.CreateDirectory(finalOutputFolder);

                // Attempt to fetch episode list for this season and attach to metadata
                try
                {
                    var eps = await _metadataService.GetEpisodesForSeasonAsync(seriesId, metadataToPass.NextSeasonNumber);
                    if (eps != null && eps.Any())
                    {
                        metadataToPass.Episodes = eps;
                        AppendLog($"--- Found {eps.Count} episode(s) for Season {metadataToPass.NextSeasonNumber}. ---", Brushes.Yellow);
                    }
                    else
                    {
                        AppendLog("--- Episode list not found via metadata service; XML will contain only season summary. ---", Brushes.Orange);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"--- Failed to retrieve episode list: {ex.Message} ---", Brushes.Orange);
                }

                // V1.9.2 FIX: Save the metadata XML to the show's root folder (including episodes if available)
                AppendLog("--- Saving metadata to local series.xml... ---", Brushes.Yellow);
                await _xmlService.SaveMetadataAsync(finalOutputFolder, metadataToPass);
                AppendLog("--- Metadata saved successfully. ---", Brushes.Green);
            }
            else
            {
                // CRITICAL FIX (Closes Issue #13)
                AppendLog($"Could not find official metadata for '{confirmedSearchTarget}'. Download aborted.", Brushes.Red);
                return;
            }

            // --- PHASE3: Download and Verification Logic ---

            string finalSeasonFolder = Path.Combine(finalOutputFolder, $"Season {metadataToPass.NextSeasonNumber:00}");

            // Count files *before* download
            int filesBefore =0;
            if (Directory.Exists(finalSeasonFolder))
            {
                filesBefore = Directory.GetFiles(finalSeasonFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Count(file => file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   file.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
            }

            // Execute Download
            StatusTextBlock.Text = $"Downloading: {metadataToPass.SourceUrl}";
            try
            {
                await _ytDlpService.DownloadVideoAsync(metadataToPass, finalOutputFolder);
            }
            catch (Exception ex)
            {
                AppendLog($"--- Download task failed: {ex.Message} ---", Brushes.Red);
            }

            // Run Content Verification
            int filesAfter =0;
            int filesDownloaded =0;

            if (Directory.Exists(finalSeasonFolder))
            {
                filesAfter = Directory.GetFiles(finalSeasonFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Count(file => file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   file.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
                filesDownloaded = filesAfter - filesBefore;
            }

            if (metadataToPass.ExpectedEpisodeCount >0)
            {
                int missingCount = metadataToPass.ExpectedEpisodeCount - filesAfter;

                if (missingCount ==0)
                {
                    AppendLog("CONTENT VERIFICATION: SUCCESS! All expected episodes are present.", Brushes.Green);
                }
                else if (missingCount >0)
                {
                    AppendLog($"CONTENT VERIFICATION: WARNING! {missingCount} episode(s) are MISSING from the Season folder (Found {filesAfter} of {metadataToPass.ExpectedEpisodeCount} expected).", Brushes.OrangeRed);
                }
                else
                {
                    AppendLog($"CONTENT VERIFICATION: Completed. Downloaded {filesDownloaded} file(s) in this session. (Found {filesAfter} files, {metadataToPass.ExpectedEpisodeCount} expected)", Brushes.Cyan);
                }
            }
            else
            {
                AppendLog($"CONTENT VERIFICATION: Completed. Downloaded {filesDownloaded} file(s) in this session. (No metadata count available)", Brushes.Cyan);
            }
        }

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
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                InitialDirectory = OutputFolderTextBox.Text
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputFolderTextBox.Text = dialog.FileName;
            }
        }

        /// <summary>
        /// Handles the "Stop" button click.
        /// </summary>
        private void StopDownloadButton_Click(object sender, RoutedEventArgs e)
        {
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
            var p = new Paragraph(new Run(message));
            p.Foreground = color;
            OutputLogTextBox.Document.Blocks.Add(p);
            LogScrollViewer.ScrollToEnd();
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
            var (ytDlpPath, ariaPath) = _toolManagerService.GetToolPaths();
            _ytDlpService = new YtDlpService(
               ytDlpPath,
               ariaPath,
               ToolManagerService.FIREFOX_USER_AGENT,
               _settingsService.Settings.PreferredVideoQuality
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

            // ** v1.9.2 UPDATE **
            // Updated the "About" text to be accurate.
            string aboutText = $@"
AutoDownloader - Version {version}
Developed By: Neo Gentrics
AI Development Partner: Gemini (Google)
--- Core Technologies ---
 - High-Speed Downloads: yt-dlp, aria2c
 - Smart Search: Google Gemini API
 - Metadata: TMDB API & TVDB API
 - Framework: .NET9.0 (WPF)
--- Implemented Features (v1.9) ---
 - **Multi-Scraper Engine:** User-selectable metadata lookup (TMDB or TVDB).
 - **Local Metadata:** Saves downloaded show info to 'series_metadata.xml'.
 - **Smarter Parsing:** Detects season numbers (e.g., 'season-2') from direct URLs.
--- Implemented Features (v1.8) ---
 - **Content Verification:** Checks downloaded files against official episode counts.
 - **Settings System Backend:** Securely load/save API keys and user preferences.
 - **Multi-Link Batch Processing:** Sequential downloading of multiple items.
";
            MessageBox.Show(aboutText, "About AutoDownloader", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Toggles between single-link (TextBox) and multi-link (RichTextBox) mode.
        /// </summary>
        private void MultiLinkToggle_Click(object sender, RoutedEventArgs e)
        {
            _isMultiLinkMode = !_isMultiLinkMode;

            if (_isMultiLinkMode)
            {
                UrlLabel.Content = "URLs or Search Terms (One per line):";
                UrlTextBox.Visibility = Visibility.Collapsed;
                MultiUrlTextBox.Visibility = Visibility.Visible;
                MultiLinkToggle.Content = "Toggle Single-Link";
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
            OutputLogTextBox.Document.Blocks.Clear(); // Clear the log
            StatusTextBlock.Text = "Starting...";

            //1. Get the list of search terms/URLs
            List<string> searchTerms;
            if (_isMultiLinkMode)
            {
                searchTerms = MultiUrlTextBox.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
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
                await ProcessSingleDownloadAsync(term);

                // Check if user cancelled after each download
                if (StopDownloadButton.Visibility == Visibility.Collapsed)
                {
                    // The UI was unlocked by a completed or failed download
                }
                else
                {
                    // If the Stop button is still visible, the user manually hit Stop.
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

        /// <summary>
        /// Helper to parse show name and season number from a given URL.
        /// V1.9.2 FIX: This logic fixes the "Season2" bug (Issue #14).
        /// This async variant attempts to scrape the page for a title when the URL segments don't yield a clear show name.
        /// </summary>
        private async Task<(string ShowName, int? SeasonNumber)> ParseMetadataFromUrlAsync(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.Segments.Select(s => s.TrimEnd('/')).Where(s => !string.IsNullOrEmpty(s)).ToList();

                int? seasonNum = null;
                string showName = "";

                // Look for explicit season-like segments (e.g., "season-2", "s2", "season", or "s/2")
                for (int i =0; i < segments.Count; i++)
                {
                    var seg = segments[i].Trim().Trim('/');
                    if (string.IsNullOrEmpty(seg)) continue;

                    // Normalize for matching
                    string segLower = seg.ToLowerInvariant();

                    // Pattern: "season-2", "season_2", "s2", "s02"
                    var m = Regex.Match(segLower, "^(?:season[-_]?|s)(\\d{1,3})$", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        if (int.TryParse(m.Groups[1].Value, out int s)) seasonNum = s;
                        if (i >0) showName = segments[i -1];
                        break;
                    }

                    // Pattern: segment == "season" and next segment is numeric (e.g., "/season/2/")
                    if (segLower == "season" && i +1 < segments.Count)
                    {
                        var next = segments[i +1].ToLowerInvariant();
                        if (int.TryParse(next, out int s2))
                        {
                            seasonNum = s2;
                            if (i >0) showName = segments[i -1];
                            break;
                        }
                    }
                }

                // If we didn't find an explicit season segment, fall back to older logic: pick the last meaningful segment
                if (string.IsNullOrWhiteSpace(showName))
                {
                    showName = segments
                        .Where(s => !s.All(char.IsDigit)
                        && !s.Equals("series", StringComparison.OrdinalIgnoreCase)
                        && !s.Equals("seasons", StringComparison.OrdinalIgnoreCase)
                        && !Regex.IsMatch(s, "^(?:season[-_]?|s)\\d+$", RegexOptions.IgnoreCase))
                        .LastOrDefault() ?? string.Empty;
                }

                // If showName is still empty or looks like a season placeholder, try scraping the page for a title as a fallback.
                if (string.IsNullOrWhiteSpace(showName) || Regex.IsMatch(showName, "^(?:season[-_]?|s)\\d+$", RegexOptions.IgnoreCase))
                {
                    try
                    {
                        using var http = new HttpClient();
                        http.Timeout = TimeSpan.FromSeconds(6);
                        var resp = await http.GetAsync(uri).ConfigureAwait(false);
                        if (resp.IsSuccessStatusCode)
                        {
                            var html = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                            // Try common metadata tags in order: og:title, twitter:title, <title>
                            string? title = null;
                            var og = Regex.Match(html, "<meta\\s+property=[\"']og:title[\"']\\s+content=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                            if (!og.Success)
                                og = Regex.Match(html, "<meta\\s+name=[\"']og:title[\"']\\s+content=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                            if (og.Success) title = og.Groups[1].Value;

                            if (string.IsNullOrWhiteSpace(title))
                            {
                                var tw = Regex.Match(html, "<meta\\s+name=[\"']twitter:title[\"']\\s+content=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                                if (tw.Success) title = tw.Groups[1].Value;
                            }

                            if (string.IsNullOrWhiteSpace(title))
                            {
                                var t = Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                                if (t.Success) title = Regex.Replace(t.Groups[1].Value, "\\s+", " ").Trim();
                            }

                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                // Remove site suffixes (e.g., " - Tubi", " | YouTube")
                                var cleaned = title.Split(new[] { "|", " - ", " — " }, StringSplitOptions.None)[0].Trim();
                                showName = cleaned;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore network failures; we will fall back to URL segment logic
                    }
                }

                // Clean up the name (e.g., "love-thy-neighbor" -> "Love Thy Neighbor")
                if (string.IsNullOrWhiteSpace(showName)) showName = "Unknown Show";
                string cleanName = showName.Replace('-', ' ').Replace('_', ' ').Trim();
                TextInfo ti = new CultureInfo("en-US", false).TextInfo;

                return (ti.ToTitleCase(cleanName), seasonNum);
            }
            catch
            {
                return ("Unknown Show", null);
            }
        }
    }
}