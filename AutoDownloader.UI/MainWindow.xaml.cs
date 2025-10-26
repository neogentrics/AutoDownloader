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

        // NEW: Track the current input mode for v1.6
        private bool _isMultiLinkMode = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Title = "AutoDownloader v1.6.1"; // Updated version title for bugfix release!

            // --- 1. Initialize Services (This fixes the build errors) ---
            // Create the ToolManager first
            _toolManagerService = new ToolManagerService();
            // "Inject" it into the YtDlpService
            _ytDlpService = new YtDlpService(_toolManagerService);
            // Create the SearchService
            _searchService = new SearchService();

            // --- 2. Set Up Default Download Path ---
            string defaultPath = @"E:\Downloads";
            if (!Directory.Exists(defaultPath))
            {
                // Fallback to "Videos\Downloads"
                defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                    "Downloads"
                );
            }
            // Ensure the directory exists
            Directory.CreateDirectory(defaultPath);
            OutputFolderTextBox.Text = defaultPath;


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

            // --- 4. Set Up Log Batching Timer (This fixes the UI freeze) ---
            _logUpdateTimer = new DispatcherTimer();
            _logUpdateTimer.Interval = TimeSpan.FromMilliseconds(100); // Update 10x per second
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            _logUpdateTimer.Start();
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

        // --- REPLACED CODE FOR ProcessSingleDownloadAsync ---

        /// <summary>
        /// Contains the core logic to process a single URL or search term.
        /// </summary>
        private async Task ProcessSingleDownloadAsync(string searchTerm)
        {
            // The searchTerm is passed in from the batch loop.
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return;
            }

            // Use the folder from the UI, which will be the base path.
            string baseOutputFolder = OutputFolderTextBox.Text;
            string finalUrl = searchTerm;
            string finalOutputFolder = baseOutputFolder;

            // --- Smart Search Logic ---
            if (!searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog($"--- Starting smart search for: '{searchTerm}' ---", Brushes.Aqua);
                StatusTextBlock.Text = $"Searching for: {searchTerm}";

                try
                {
                    var (type, url) = await _searchService.FindShowUrlAsync(searchTerm);

                    if (url == "not-found")
                    {
                        AppendLog($"Could not find a download page for '{searchTerm}'.", Brushes.Red);
                        AppendLog("--- Search failed. ---", Brushes.Red);
                        return; // End this individual item without affecting the batch lock
                    }

                    // --- Search successful! ---
                    AppendLog($"Found URL: {url}", Brushes.Aqua);
                    finalUrl = url;

                    // Set the output folder based on type
                    string category = type.Contains("Anime") ? "Anime TV Shows" :
                                      type.Contains("TV Show") ? "TV Shows" :
                                      type.Contains("Movie") ? "Movies" : "Playlists";

                    // Set the final folder for the download
                    finalOutputFolder = Path.Combine(baseOutputFolder, category);

                    // NOTE: We don't update OutputFolderTextBox.Text here, as it might confuse the user 
                    // if we change the base folder during a batch operation.
                    Directory.CreateDirectory(finalOutputFolder);
                }
                catch (Exception ex)
                {
                    AppendLog($"--- Smart search failed: {ex.Message} ---", Brushes.Red);
                    return; // End this individual item without affecting the batch lock
                }
            }

            // --- Download Logic ---
            StatusTextBlock.Text = $"Downloading: {finalUrl}";
            try
            {
                // We MUST await this so the try/catch block works correctly
                await _ytDlpService.DownloadVideoAsync(finalUrl, finalOutputFolder);
            }
            catch (Exception ex)
            {
                AppendLog($"--- Download task failed: {ex.Message} ---", Brushes.Red);
                // DO NOT UNLOCK UI HERE. The batch loop or the Exited handler does it.
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

        // --- New Methods in MainWindow.xaml.cs ---

        /// <summary>
        /// Handles the "Exit" menu item click.
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Handles the "Preferences" menu item click (Placeholder for v1.7).
        /// </summary>
        private void Preferences_Click(object sender, RoutedEventArgs e)
        {
            // Implementation for settings window will go here in v1.7
            AppendLog("Preferences window not yet implemented (scheduled for v1.7).", Brushes.Orange);
        }

        // --- MODIFIED CODE in MainWindow.xaml.cs (About_Click method) ---

        /// <summary>
        /// Handles the "About" menu item click, showing project information.
        /// </summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            // Extract the version from the window title, which we ensure is always up-to-date.
            string version = this.Title.Split(' ').LastOrDefault() ?? "Unknown";

            string aboutText = $@"
            AutoDownloader - Version {version}

            Developed By: Neo Gentrics
            AI Development Partner: Gemini (Google)

            --- Core Technologies ---
              - High-Speed Downloads: yt-dlp, aria2c
              - Smart Search: Google Gemini API
              - Framework: .NET 9.0 (WPF)

            --- Bugfix Release v1.6.1 ---
              - Fixed critical app freeze after stopping a download.
              - Fixed menu bar dropdown visibility in dark theme.

            Thank you for using AutoDownloader!
            ";
            MessageBox.Show(aboutText, "About AutoDownloader", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // --- New Method in MainWindow.xaml.cs ---

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

        // --- Modified Method in MainWindow.xaml.cs ---

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

        
        
    }
}

