using AutoDownloader.Core;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading; // Added for the timer

namespace AutoDownloader.UI
{
    public partial class MainWindow : Window
    {
        private readonly YtDlpService _ytDlpService;
        private readonly SearchService _searchService;

        // --- NEW: For high-performance, non-blocking logging ---
        private readonly StringBuilder _logBuffer = new StringBuilder();
        private readonly DispatcherTimer _logUpdateTimer;
        // ----------------------------------------------------

        public MainWindow()
        {
            InitializeComponent();

            _ytDlpService = new YtDlpService();
            _searchService = new SearchService();

            // Subscribe to engine events
            _ytDlpService.OnOutputReceived += YtDlpService_OnOutputReceived;
            _ytDlpService.OnDownloadComplete += YtDlpService_OnDownloadComplete;

            // --- FIXED: Re-added your custom default path logic ---
            string defaultPathE = @"E:\Downloads";
            string defaultPathVideos = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "AutoDownloader Outputs"
            );

            if (Directory.Exists(defaultPathE))
            {
                OutputFolderTextBox.Text = defaultPathE;
            }
            else
            {
                try
                {
                    if (!Directory.Exists(defaultPathVideos))
                    {
                        Directory.CreateDirectory(defaultPathVideos);
                    }
                    OutputFolderTextBox.Text = defaultPathVideos;
                }
                catch (Exception ex)
                {
                    // Fallback if we can't create the Videos directory
                    OutputFolderTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    AppendLogMessage($"Could not create default directory: {ex.Message}", Brushes.Yellow);
                }
            }
            // ----------------------------------------------------

            // --- NEW: Initialize the log-batching timer ---
            _logUpdateTimer = new DispatcherTimer
            {
                // Update the log 10 times per second
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _logUpdateTimer.Tick += LogUpdateTimer_Tick;
            // ----------------------------------------------
        }

        /// <summary>
        /// This event fires 10x/sec. It takes all text from the buffer
        /// and "flushes" it to the UI in a single, non-blocking operation.
        /// </summary>
        private void LogUpdateTimer_Tick(object? sender, EventArgs e)
        {
            string bufferContent;
            lock (_logBuffer)
            {
                if (_logBuffer.Length == 0)
                    return;

                bufferContent = _logBuffer.ToString();
                _logBuffer.Clear();
            }

            // Append the entire batch of logs at once
            AppendLogMessage(bufferContent, Brushes.Gray, false);
        }

        /// <summary>
        /// This is now high-speed and non-blocking.
        /// It just adds the log data to a buffer instead of updating the UI.
        /// </summary>
        private void YtDlpService_OnOutputReceived(string data, bool isError)
        {
            // We lock the buffer to prevent a race condition with the timer
            lock (_logBuffer)
            {
                _logBuffer.AppendLine(data);
            }
        }

        private void YtDlpService_OnDownloadComplete(bool success)
        {
            // Stop the log timer
            _logUpdateTimer.Stop();

            // --- NEW: Do one final flush ---
            // This ensures any remaining messages in the buffer are displayed
            LogUpdateTimer_Tick(null, EventArgs.Empty);
            // -------------------------------

            Dispatcher.Invoke(() =>
            {
                if (success)
                {
                    AppendLogMessage("\n--- Download Complete! ---", Brushes.LimeGreen);
                    StatusTextBlock.Text = "Download complete!";
                }
                else
                {
                    AppendLogMessage("\n--- Download Failed or Cancelled. ---", Brushes.OrangeRed);
                    StatusTextBlock.Text = "Download failed or was cancelled.";
                }
                SetUiLock(false);
            });
        }

        private async void StartDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiLock(true);
            OutputLogParagraph.Inlines.Clear(); // Clear the log
            _logUpdateTimer.Start(); // Start the log timer

            string searchTerm = UrlTextBox.Text.Trim();
            string outputFolder = OutputFolderTextBox.Text;

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                AppendLogMessage("Please enter a search term or URL.", Brushes.Yellow);
                SetUiLock(false);
                _logUpdateTimer.Stop();
                return;
            }

            try
            {
                string url;
                string baseOutputFolder = outputFolder;
                bool isSearch = !searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase);

                if (isSearch)
                {
                    AppendLogMessage($"--- Starting smart search for: '{searchTerm}' ---", Brushes.Cyan);
                    StatusTextBlock.Text = "Searching...";

                    (string type, string foundUrl) = await _searchService.FindShowUrlAsync(searchTerm);
                    url = foundUrl;

                    if (url == "not-found")
                    {
                        AppendLogMessage($"Could not find a download page for '{searchTerm}'.", Brushes.OrangeRed);
                        AppendLogMessage("--- Search failed. ---", Brushes.Red);
                        SetUiLock(false);
                        _logUpdateTimer.Stop();
                        return;
                    }

                    // Set the category subfolder
                    string categoryFolder = type switch
                    {
                        "Anime" => "Anime TV Shows",
                        "TV Show" => "TV Shows",
                        "Movie" => "Movies",
                        _ => "Playlists & Misc"
                    };
                    baseOutputFolder = Path.Combine(outputFolder, categoryFolder);
                    if (!Directory.Exists(baseOutputFolder))
                    {
                        Directory.CreateDirectory(baseOutputFolder);
                    }

                    AppendLogMessage($"Found URL: {url}", Brushes.Gray);
                    AppendLogMessage($"Media Type: {type}", Brushes.Gray);
                    AppendLogMessage($"Output Folder: {baseOutputFolder}", Brushes.Gray);

                    // Auto-update UI
                    UrlTextBox.Text = url;
                    OutputFolderTextBox.Text = baseOutputFolder;
                }
                else
                {
                    url = searchTerm;
                }

                AppendLogMessage("\n--- Starting download... ---", Brushes.LimeGreen);
                StatusTextBlock.Text = "Download in progress...";

                // --- CRITICAL FIX: We must 'await' this call! ---
                // This keeps the try/catch block active and prevents
                // the UI from thinking the work is "done."
                await _ytDlpService.DownloadVideoAsync(url, baseOutputFolder);
            }
            catch (Exception ex)
            {
                AppendLogMessage($"\n--- AN ERROR OCCURRED ---", Brushes.Red);
                AppendLogMessage(ex.Message, Brushes.Red);
                StatusTextBlock.Text = "Error!";
                SetUiLock(false);
                _logUpdateTimer.Stop();
            }
        }

        private void StopDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            AppendLogMessage("\n--- Sending stop signal... ---", Brushes.Yellow);
            _ytDlpService.StopDownload();
        }

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                StartDownloadButton_Click(sender, e);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // This requires the 'Microsoft.WindowsAPICodePack-Shell' NuGet package
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    OutputFolderTextBox.Text = dialog.FileName;
                }
            }
        }

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
        /// Appends a message to the rich text log box.
        /// </summary>
        private void AppendLogMessage(string message, SolidColorBrush color, bool addNewLine = true)
        {
            // This must run on the UI thread
            if (addNewLine)
            {
                message += Environment.NewLine;
            }

            var run = new Run(message) { Foreground = color };
            OutputLogParagraph.Inlines.Add(run);

            // Auto-scroll to the end
            LogScrollViewer.ScrollToEnd();
        }
    }
}

