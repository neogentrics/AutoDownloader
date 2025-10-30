using AutoDownloader.Core;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Linq;

namespace AutoDownloader.UI
{
    public partial class MainWindow : Window
    {
        // --- Our Core "Engines" ---
        private readonly YtDlpService _ytDlpService;
        private readonly SearchService _searchService;
        private readonly ToolManagerService _toolManagerService;

        // --- High-Performance Log Batching ---
        private readonly DispatcherTimer _logUpdateTimer;
        private readonly List<string> _logQueue = new List<string>();
        private readonly object _logLock = new object();

        // NEW: Track the current input mode
        private bool _isMultiLinkMode = false;
        private readonly MetadataService _metadataService;

        // V1.8 NEW: Settings Service
        private readonly SettingsService _settingsService;

        // NEW: Define the authoritative version number here
        private const string CurrentVersion = "v1.9.0-alpha"; // <-- Updated to v1.9.0-alpha

        public MainWindow()
        {
            InitializeComponent();
            this.Title = $"AutoDownloader {CurrentVersion} - (For Personal Use Only)";
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            // --- 1. Initialize Services in Dependency Order ---

            // A. Settings must load first to get API keys
            _settingsService = new SettingsService();

            // B. ToolManager is self-contained
            _toolManagerService = new ToolManagerService();

            // C. YtDlpService depends on ToolManager
            _ytDlpService = new YtDlpService(_toolManagerService);

            // D. MetadataService and SearchService depend on Settings for keys
            // This is where your errors were! They MUST be initialized with the key.
            _metadataService = new MetadataService(
                _settingsService.Settings.TmdbApiKey,
                _settingsService.Settings.TvdbApiKey // <-- V1.9 NEW: Passing TVDB key
            );

            _searchService = new SearchService(_settingsService.Settings.GeminiApiKey);

            // --- 2. Set Up Default Download Path ---
            // Use the path loaded from settings for the text box
            OutputFolderTextBox.Text = _settingsService.Settings.DefaultOutputFolder;
            Directory.CreateDirectory(_settingsService.Settings.DefaultOutputFolder);


            // --- 3. Set Up Event Handlers (This fixes the delegate errors) ---

            // Subscribe to the download complete event
            _ytDlpService.OnDownloadComplete += (exitCode) =>
            {
                // This event can come from a background thread, so we must use
                // the Dispatcher to safely update the UI (the "main" thread).
                Dispatcher.Invoke(() =>
                {
                    SetUiLock(false); // Re-enable the UI
                    StatusTextBlock.Text = exitCode == 0 ? "Download complete" : "Download failed or stopped";
                });
            };

            // Subscribe to the log output event
            _ytDlpService.OnOutputReceived += (logLine) =>
            {
                // This fires thousands of times. Instead of updating the UI
                // directly, we add the log to a queue.
                lock (_logLock)
                {
                    _logQueue.Add(logLine);
                }
            };

            // Subscribe to the ToolManager's log event
            _toolManagerService.OnToolLogReceived += (logLine) =>
            {
                // Also add tool logs to the same queue
                lock (_logLock)
                {
                    _logQueue.Add(logLine);
                }
            };

            // --- 4. Set Up Log Batching Timer ---
            _logUpdateTimer = new DispatcherTimer();
            _logUpdateTimer.Interval = TimeSpan.FromMilliseconds(100); // Update 10x per second
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            _logUpdateTimer.Start();

            // V1.8 Launch Validation
            ValidateApiKeysOnLaunch();
        }

        /// <summary>
        /// This method runs 10x per second to "flush" all queued logs
        /// to the screen at once. This prevents the UI from freezing.
        /// </summary>
        private void LogUpdateTimer_Tick(object? sender, EventArgs e)
        {
            List<string> logsToFlush;
            lock (_logLock)
            {
                if (_logQueue.Count == 0) return;

                // Copy the current queue and clear it in one atomic operation
                logsToFlush = new List<string>(_logQueue);
                _logQueue.Clear();
            }

            if (logsToFlush.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var line in logsToFlush)
                {
                    sb.AppendLine(line);
                }

                // **** THIS IS THE FIX ****
                // Create the Paragraph 'p' from the string builder
                var p = new Paragraph(new Run(sb.ToString()));

                // Smartly color-code the output
                // We check the *raw string* for faster performance
                string logText = sb.ToString();
                if (!string.IsNullOrEmpty(logText))
                {
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
                    else if (logText.StartsWith("Downloading tool:", StringComparison.OrdinalIgnoreCase) ||
                             logText.StartsWith("Extracting tool:", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Foreground = Brushes.Orange;
                    }
                }

                // Add the formatted paragraph to the log
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
            // This requires the Microsoft.WindowsAPICodePack-Shell NuGet package
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                InitialDirectory = OutputFolderTextBox.Text
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                // **** THIS IS THE FIX ****
                // This line was wrong and caused the build error.
                OutputFolderTextBox.Text = dialog.FileName;
            }
        }

        /// <summary>
        /// Contains the core logic to process a single URL or search term.
        /// V1.9.0-alpha: Reworked for pop-up confirmation strategy.
        /// </summary>
        private async Task ProcessSingleDownloadAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return;
            }

            string baseOutputFolder = OutputFolderTextBox.Text;
            string finalUrl = searchTerm;
            string finalOutputFolder = baseOutputFolder;
            string searchTarget = searchTerm;

            DownloadMetadata metadataToPass = new DownloadMetadata { SourceUrl = finalUrl };

            // --- PHASE 1: Determine the Final Download URL ---
            if (!searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // Case A: Input is a Search Term (Gemini Search is REQUIRED)
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
                    AppendLog($"Found URL: {url}", Brushes.Aqua);
                    finalUrl = url;
                    metadataToPass.SourceUrl = url;

                    string category = type.Contains("Anime") ? "Anime TV Shows" :
                                        type.Contains("TV Show") ? "TV Shows" :
                                        type.Contains("Movie") ? "Movies" : "Playlists";
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
                // Case B: Input is a Direct URL (Gemini Search is SKIPPED)
                AppendLog($"--- Direct URL detected. Skipping Gemini search. ---", Brushes.Aqua);
                finalOutputFolder = Path.Combine(baseOutputFolder, "TV Shows");
                Directory.CreateDirectory(finalOutputFolder);

                // V1.8.1 FIX: Extract show name from URL
                searchTarget = ExtractShowNameFromUrl(searchTerm);
            }

            // --- PHASE 2: V1.9.0-alpha POP-UP STRATEGY ---

            // Step 1: Confirm the name with the user (15s timeout)
            AppendLog($"--- Parsing complete. Confirming show name for: '{searchTarget}' ---", Brushes.Aqua);
            ConfirmNameWindow nameDialog = new ConfirmNameWindow(searchTarget) { Owner = this };
            nameDialog.ShowDialog(); // Pauses execution

            if (!nameDialog.IsConfirmed)
            {
                AppendLog("--- User cancelled metadata search. Download aborted. ---", Brushes.Red);
                return;
            }

            string confirmedName = nameDialog.ShowName;
            AppendLog($"--- User confirmed name: '{confirmedName}' ---", Brushes.Aqua);

            // Step 2: Select the Database (30s timeout)
            StatusTextBlock.Text = "Waiting for database selection...";
            SelectDatabaseWindow dbDialog = new SelectDatabaseWindow() { Owner = this };
            dbDialog.ShowDialog(); // Pauses execution

            if (dbDialog.SelectedSource == DatabaseSource.Canceled)
            {
                AppendLog("--- User cancelled database selection. Download aborted. ---", Brushes.Red);
                return;
            }

            // Step 3: Call the correct metadata service based on selection
            Task<(string, int, int, int)?> metadataTask;
            if (dbDialog.SelectedSource == DatabaseSource.TVDB)
            {
                AppendLog("--- Searching The TV Database (TVDB)... ---", Brushes.Yellow);
                StatusTextBlock.Text = "Searching TVDB...";
                metadataTask = _metadataService.GetTvdbMetadataAsync(confirmedName);
            }
            else
            {
                AppendLog("--- Searching The Movie Database (TMDB)... ---", Brushes.Yellow);
                StatusTextBlock.Text = "Searching TMDB...";
                metadataTask = _metadataService.GetTmdbMetadataAsync(confirmedName);
            }

            var metadataResult = await metadataTask;

            // Step 4: Process Metadata and Save XML (Future Step)
            if (metadataResult != null)
            {
                var (officialTitle, seriesId, targetSeasonNumber, expectedCount) = metadataResult.Value;

                AppendLog($"Official Title Found: {officialTitle}", Brushes.Yellow);
                AppendLog($"Target Season {targetSeasonNumber} Expected Episode Count: {expectedCount}", Brushes.Yellow);

                metadataToPass.OfficialTitle = officialTitle;
                metadataToPass.SeriesId = seriesId;
                metadataToPass.NextSeasonNumber = targetSeasonNumber;
                metadataToPass.ExpectedEpisodeCount = expectedCount;

                finalOutputFolder = Path.Combine(baseOutputFolder, "TV Shows", metadataToPass.OfficialTitle);
                Directory.CreateDirectory(finalOutputFolder);
                AppendLog($"Output folder set to: {finalOutputFolder}", Brushes.Yellow);

                // TODO (v1.9.0-alpha): Add XML saving logic here.
                AppendLog("--- Metadata saving to XML not yet implemented. ---", Brushes.Orange);
            }
            else
            {
                // This is the CRASH fix: if metadata fails, we stop gracefully.
                AppendLog($"Could not find official metadata for '{confirmedName}'. Download aborted.", Brushes.Red);
                return;
            }

            // --- PHASE 3: Download and Verification Logic ---
            // (This block remains the same as your v1.8.1 code)

            string finalSeasonFolder = Path.Combine(finalOutputFolder, $"Season {metadataToPass.NextSeasonNumber:00}");

            int filesBefore = 0;
            if (Directory.Exists(finalSeasonFolder))
            {
                filesBefore = Directory.GetFiles(finalSeasonFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Count(file => file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   file.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
            }
            AppendLog($"Files in Season Folder before download: {filesBefore}", Brushes.Cyan);

            StatusTextBlock.Text = $"Downloading: {metadataToPass.SourceUrl}";
            try
            {
                await _ytDlpService.DownloadVideoAsync(metadataToPass, finalOutputFolder);
            }
            catch (Exception ex)
            {
                AppendLog($"--- Download task failed: {ex.Message} ---", Brushes.Red);
            }

            int filesAfter = 0;
            int filesDownloaded = 0;

            if (Directory.Exists(finalSeasonFolder))
            {
                filesAfter = Directory.GetFiles(finalSeasonFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Count(file => file.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                                   file.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));

                filesDownloaded = filesAfter - filesBefore;
            }

            if (metadataToPass.ExpectedEpisodeCount > 0)
            {
                int missingCount = metadataToPass.ExpectedEpisodeCount - filesAfter;

                if (missingCount == 0)
                {
                    AppendLog("CONTENT VERIFICATION: SUCCESS! All expected episodes are present.", Brushes.Green);
                }
                else if (missingCount > 0)
                {
                    AppendLog($"CONTENT VERIFICATION: WARNING! {missingCount} episode(s) are MISSING from the Season folder (Found {filesAfter} of {metadataToPass.ExpectedEpisodeCount} expected).", Brushes.OrangeRed);
                }
                else
                {
                    AppendLog($"CONTENT VERIFICATION: Completed. Downloaded {filesDownloaded} file(s) in this session.", Brushes.Cyan);
                }
            }
            else
            {
                AppendLog($"CONTENT VERIFICATION: Completed. Downloaded {filesDownloaded} file(s) in this session. (No metadata count available)", Brushes.Cyan);
            }
        }

        /// <summary>
        /// Handles the "Stop" button click.
        /// </summary>
        private void StopDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            _ytDlpService.StopDownload();
            // The OnDownloadComplete event will handle unlocking the UI
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

            // Swap button visibility
            StartDownloadButton.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
            StopDownloadButton.Visibility = isLocked ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// A helper to add a single, colored log line (used for important messages)
        /// </summary>
        private void AppendLog(string message, SolidColorBrush color)
        {
            var p = new Paragraph(new Run(message));
            p.Foreground = color;
            OutputLogTextBox.Document.Blocks.Add(p);
            LogScrollViewer.ScrollToEnd();
        }


        /// <summary>
        /// Handles the "Exit" menu item click.
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Handles the "Preferences" menu item click, opening the settings window.
        /// </summary>
        private void Preferences_Click(object sender, RoutedEventArgs e)
        {
            // Create a new instance of the PreferencesWindow, passing the SettingsService instance
            PreferencesWindow settingsWindow = new PreferencesWindow(_settingsService);

            // Show the window modally (blocks input to the main window until closed)
            settingsWindow.ShowDialog();

            // When the preferences window closes, reload the default path just in case it was changed
            OutputFolderTextBox.Text = _settingsService.Settings.DefaultOutputFolder;

            // Log that the action was performed
            AppendLog("Preferences window opened. Settings may require app restart to take full effect.", Brushes.Yellow);
        }

        /// <summary>
        /// Handles the "About" menu item click, showing project information.
        /// </summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            // Use the authoritative constant instead of parsing the window title.
            string version = CurrentVersion;

            string aboutText = $@"
AutoDownloader - Version {version}

Developed By: Neo Gentrics
AI Development Partner: Gemini (Google)

--- Core Technologies ---
  - High-Speed Downloads: yt-dlp, aria2c
  - Smart Search: Google Gemini API
  - Metadata: TMDB API
  - Framework: .NET 9.0 (WPF)

--- Implemented Features (v1.8) ---
  - **Content Verification Structure:** Added structure to retrieve official TMDB episode counts.
  - **Settings System Backend:** Implemented data model and service to securely load/save API keys and user preferences (v1.8.0).
  - **API Key Validation:** Added launch time pop-up warning for required missing API keys.
  - **Multi-Link Batch Processing:** Enabled sequential downloading of multiple items (v1.6).

--- Bug Fixes (v1.6.1 - v1.7.5) ---
  - **Fixed:** Critical app freezing when stopping a download (v1.6.1).
  - **Fixed:** Incorrect file/folder naming (NA/Duplication) (v1.7.3).
  - **Fixed:** Template syntax error and UI visibility issues (v1.7.4).

Thank you for using AutoDownloader!
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
        /// Handles the "Download" button click, supporting single or multiple links.
        /// </summary>
        private async void StartDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiLock(true);
            OutputLogTextBox.Document.Blocks.Clear(); // Clear the log
            StatusTextBlock.Text = "Starting...";

            // 1. Get the list of search terms/URLs
            List<string> searchTerms;
            if (_isMultiLinkMode)
            {
                // Split the multi-line text box by new lines, filter out empty lines
                searchTerms = MultiUrlTextBox.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList();
            }
            else
            {
                // Single link mode
                searchTerms = new List<string> { UrlTextBox.Text.Trim() };
            }

            if (searchTerms.Count == 0 || (searchTerms.Count == 1 && string.IsNullOrWhiteSpace(searchTerms[0])))
            {
                AppendLog("Please enter at least one URL or search term.", Brushes.Red);
                SetUiLock(false);
                return;
            }

            // 2. Process each item sequentially
            AppendLog($"--- Starting Batch Download ({searchTerms.Count} items) ---", Brushes.Aqua);

            foreach (var term in searchTerms)
            {
                if (string.IsNullOrWhiteSpace(term)) continue;

                AppendLog($"\n--- Processing Item: {term} ---", Brushes.Aqua);
                await ProcessSingleDownloadAsync(term);

                // Crucial: check for user cancellation after each download
                if (StopDownloadButton.Visibility == Visibility.Collapsed)
                {
                    // The UI was unlocked, meaning the download either failed or completed
                }
                else
                {
                    // If the UI is still locked (Stop button is visible), 
                    // the user must have hit the Stop button during the last download.
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
            bool tmdbKeyMissing = !this._metadataService.IsTmdbKeyValid;

            bool geminiKeyMissing = string.IsNullOrWhiteSpace(_settingsService.Settings.GeminiApiKey) ||
                                    _settingsService.Settings.GeminiApiKey == "YOUR_GEMINI_API_KEY_HERE";

            string message = "";

            if (tmdbKeyMissing)
            {
                message += "CRITICAL: The TMDB API Key is missing or invalid. Metadata lookup (official show names, episode counts) will fail.\n\n";
                message += "Please get a key and enter it in the 'Edit -> Preferences...' menu (v1.8 feature).\n";
            }

            if (geminiKeyMissing)
            {
                if (!string.IsNullOrEmpty(message)) message += "\n---\n\n";
                message += "WARNING: The Gemini API Key is missing. Smart Search functionality will be disabled. You must use direct URLs.\n\n";
                message += "Find your key and enter it in the 'Edit -> Preferences...' menu (v1.8 feature).\n";
            }

            if (!string.IsNullOrEmpty(message))
            {
                MessageBox.Show(
                    message,
                    "API Key Validation Warning",
                    MessageBoxButton.OK,
                    tmdbKeyMissing ? MessageBoxImage.Error : MessageBoxImage.Warning // Critical error if TMDB is missing
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
                // Prevent the key from being processed further
                e.Handled = true;

                // Call the existing menu click handler
                Preferences_Click(sender, e);
            }
        }

        /// <summary>
        /// Helper to parse show name from a simple URL format like Tubi for metadata search.
        /// </summary>
        private string ExtractShowNameFromUrl(string url)
        {
            // Find the last segment after the last '/'
            string lastSegment = url.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;

            // Replace hyphens with spaces
            string cleanName = lastSegment.Replace('-', ' ');

            // Use crude Title Casing for a better search result in TMDB
            System.Globalization.TextInfo ti = new System.Globalization.CultureInfo("en-US", false).TextInfo;
            return ti.ToTitleCase(cleanName);
        }

    }
}

