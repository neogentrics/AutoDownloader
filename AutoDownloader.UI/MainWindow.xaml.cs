using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AutoDownloader.Core;
using Microsoft.WindowsAPICodePack.Dialogs; // For folder picker

namespace AutoDownloader.UI
{
    public partial class MainWindow : Window
    {
        // We now have two services: one for downloading, one for searching
        private readonly YtDlpService _ytDlpService;
        private readonly SearchService _searchService;

        // Base download directory
        private readonly string _baseDownloadPath;

        public MainWindow()
        {
            InitializeComponent();

            _ytDlpService = new YtDlpService();
            _searchService = new SearchService();

            // --- Set up our smart default paths ---
            string preferredPath = @"E:\Downloads";
            string fallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Downloads");

            if (Directory.Exists(preferredPath))
            {
                _baseDownloadPath = preferredPath;
            }
            else
            {
                _baseDownloadPath = fallbackPath;
                // Ensure the fallback directory exists
                Directory.CreateDirectory(_baseDownloadPath);
            }

            // Set the default folder to "TV Shows"
            OutputFolderTextBox.Text = Path.Combine(_baseDownloadPath, "TV Shows");
            // --- End of path logic ---

            // Wire up the events from our "engine" to our UI
            _ytDlpService.OnOutputReceived += (logLine) =>
            {
                // This code runs on a background thread, so we must
                // use Dispatcher.Invoke to safely update the UI.
                Dispatcher.Invoke(() =>
                {
                    OutputLogTextBox.AppendText(logLine + Environment.NewLine);
                    OutputLogTextBox.ScrollToEnd();
                });
            };

            _ytDlpService.OnDownloadComplete += (isSuccess, message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusTextBlock.Text = message;
                    SetUiLock(true); // Re-enable the UI
                });
            };
        }

        /// <summary>
        /// Locks or unlocks the UI elements during download
        /// </summary>
        private void SetUiLock(bool isEnabled)
        {
            StartDownloadButton.IsEnabled = isEnabled;
            UrlTextBox.IsEnabled = isEnabled;
            BrowseButton.IsEnabled = isEnabled;
            OutputFolderTextBox.IsEnabled = isEnabled;
        }

        /// <summary>
        /// This method is now the main "entry point" for starting the work.
        /// </summary>
        private async void StartDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string urlOrSearchTerm = UrlTextBox.Text;
            if (string.IsNullOrWhiteSpace(urlOrSearchTerm))
            {
                StatusTextBlock.Text = "Please enter a URL or search term.";
                return;
            }

            SetUiLock(false);
            OutputLogTextBox.Clear();
            StatusTextBlock.Text = "Starting...";

            try
            {
                // Check if the user entered a URL or a search term
                if (urlOrSearchTerm.StartsWith("http://") || urlOrSearchTerm.StartsWith("https://"))
                {
                    // --- This is the OLD logic ---
                    // It's a URL, just download it.
                    StatusTextBlock.Text = "URL detected. Starting download...";
                    OutputLogTextBox.AppendText("URL detected. Starting download..." + Environment.NewLine);
                    await _ytDlpService.DownloadVideoAsync(urlOrSearchTerm, OutputFolderTextBox.Text);
                }
                else
                {
                    // --- This is the NEW logic ---
                    // It's a search term.
                    StatusTextBlock.Text = $"Searching for '{urlOrSearchTerm}'...";

                    // --- START: New logging ---
                    OutputLogTextBox.AppendText($"--- Starting smart search for: '{urlOrSearchTerm}' ---" + Environment.NewLine);
                    OutputLogTextBox.AppendText("Calling Gemini API..." + Environment.NewLine);
                    // --- END: New logging ---

                    var searchResult = await _searchService.FindShowUrlAsync(urlOrSearchTerm);

                    if (searchResult.Url == "not-found")
                    {
                        string notFoundMsg = $"Could not find a download page for '{urlOrSearchTerm}'.";
                        StatusTextBlock.Text = notFoundMsg;

                        // --- START: New logging ---
                        OutputLogTextBox.AppendText(notFoundMsg + Environment.NewLine);
                        OutputLogTextBox.AppendText("--- Search failed. ---" + Environment.NewLine);
                        // --- END: New logging ---

                        SetUiLock(true);
                        return;
                    }

                    // --- We found it! Now, auto-configure the UI ---
                    StatusTextBlock.Text = $"Found: {searchResult.Url}. Starting download...";

                    // 1. Set the URL box to what we found
                    UrlTextBox.Text = searchResult.Url;

                    // 2. Set the output folder based on the type
                    string categoryFolder = "TV Shows"; // Default
                    if (searchResult.Type.Equals("Anime", StringComparison.OrdinalIgnoreCase))
                    {
                        categoryFolder = "Anime TV Shows";
                    }
                    else if (searchResult.Type.Equals("Movie", StringComparison.OrdinalIgnoreCase))
                    {
                        categoryFolder = "Movies";
                    }
                    else if (searchResult.Type.Equals("Playlist", StringComparison.OrdinalIgnoreCase))
                    {
                        categoryFolder = "Playlists";
                    }

                    OutputFolderTextBox.Text = Path.Combine(_baseDownloadPath, categoryFolder);
                    Directory.CreateDirectory(OutputFolderTextBox.Text); // Ensure it exists

                    // --- START: New logging ---
                    OutputLogTextBox.AppendText($"Search complete. Found URL: {searchResult.Url}" + Environment.NewLine);
                    OutputLogTextBox.AppendText($"Type classified as: {searchResult.Type}" + Environment.NewLine);
                    OutputLogTextBox.AppendText($"Output folder set to: {OutputFolderTextBox.Text}" + Environment.NewLine);
                    OutputLogTextBox.AppendText("--- Handing off to yt-dlp... ---" + Environment.NewLine);
                    // --- END: New logging ---

                    // 3. Start the download
                    await _ytDlpService.DownloadVideoAsync(searchResult.Url, OutputFolderTextBox.Text);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"An error occurred: {ex.Message}";

                // --- START: New logging ---
                OutputLogTextBox.AppendText($"---!! AN ERROR OCCURRED !!---" + Environment.NewLine);
                OutputLogTextBox.AppendText(ex.Message + Environment.NewLine);
                OutputLogTextBox.AppendText(ex.StackTrace ?? "No stack trace available." + Environment.NewLine);
                OutputLogTextBox.AppendText($"-------------------------------" + Environment.NewLine);
                // --- END: New logging ---

                SetUiLock(true);
            }
        }

        /// <summary>
        /// Opens the modern folder picker dialog
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
        /// Handles the Enter key press in the URL text box
        /// </summary>
        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // This is a simple way to "click" the button
                StartDownloadButton_Click(sender, new RoutedEventArgs());
            }
        }
    }
}

