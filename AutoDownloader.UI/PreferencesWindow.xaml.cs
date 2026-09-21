using AutoDownloader.Services; // <-- CORRECT: Using the .Services project
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoDownloader.UI // <-- CORRECT: This is a UI file
{
    /// <summary>
    /// Interaction logic for PreferencesWindow.xaml.
    /// This window allows the user to edit and save the application's settings.
    /// It directly interacts with the SettingsService.
    /// </summary>
    public partial class PreferencesWindow : Window
    {
        // --- Private Fields ---

        /// <summary>
        /// A reference to the application's single SettingsService instance.
        /// This is "injected" via the constructor.
        /// </summary>
        private readonly SettingsService _settingsService;

        /// <summary>
        /// Sentinel tag for the "use a cookies.txt file" option, which is not a browser name.
        /// </summary>
        private const string CookieFileOptionTag = "__file__";

        /// <summary>
        /// The browsers yt-dlp can read cookies from, paired with the exact token it expects.
        /// Getting this token wrong is not a soft failure: yt-dlp aborts the download.
        /// </summary>
        private static readonly (string Label, string Value)[] CookieSourceOptions =
        {
            ("None (recommended)", ""),
            ("Firefox",  "firefox"),
            ("Chrome",   "chrome"),
            ("Edge",     "edge"),
            ("Brave",    "brave"),
            ("Chromium", "chromium"),
            ("Opera",    "opera"),
            ("Vivaldi",  "vivaldi"),
            ("Cookies.txt file...", CookieFileOptionTag),
        };

        // --- Constructor ---

        /// <summary>
        /// Initializes a new instance of the PreferencesWindow.
        /// </summary>
        /// <param name="settingsService">The application's active SettingsService instance.</param>
        public PreferencesWindow(SettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;

            // Immediately load the current settings into the text boxes.
            LoadCurrentSettings();
        }

        // --- Private Methods ---

        /// <summary>
        /// Reads the settings from the _settingsService.Settings model
        /// and populates the UI text boxes with those values.
        /// </summary>
        private void LoadCurrentSettings()
        {
            var settings = _settingsService.Settings;

            // Data flows FROM the Model TO the View (here)
            TmdbKeyTextBox.Text = settings.TmdbApiKey;
            GeminiKeyTextBox.Text = settings.GeminiApiKey;
            TvdbKeyTextBox.Text = settings.TvdbApiKey; // v1.9.2: Added TVDB key
            OutputFolderTextBox.Text = settings.DefaultOutputFolder;
            QualityTextBox.Text = settings.PreferredVideoQuality;
            AutoInstallPlaywrightCheckBox.IsChecked = settings.AutoInstallPlaywrightBrowsers;
            AutoDownloadFfmpegCheckBox.IsChecked = settings.AutoDownloadFfmpeg;
            UseDownloadArchiveCheckBox.IsChecked = settings.UseDownloadArchive;

            LoadCookieSource(settings.CookieSource);
        }

        /// <summary>
        /// Populates the cookie dropdown and selects whichever option matches the stored value.
        /// An unrecognised value is treated as a file path, which is how SettingsModel defines
        /// it: anything that is not a browser name is passed to yt-dlp as --cookies.
        /// </summary>
        private void LoadCookieSource(string? stored)
        {
            CookieSourceComboBox.Items.Clear();

            foreach (var (label, value) in CookieSourceOptions)
            {
                CookieSourceComboBox.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = value,
                    Foreground = Brushes.Black
                });
            }

            stored = (stored ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(stored))
            {
                CookieSourceComboBox.SelectedIndex = 0;
                return;
            }

            var match = CookieSourceComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string?)i.Tag, stored, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                CookieSourceComboBox.SelectedItem = match;
                return;
            }

            if (LooksLikeFilePath(stored))
            {
                CookieSourceComboBox.SelectedItem = CookieSourceComboBox.Items
                    .OfType<ComboBoxItem>()
                    .First(i => (string?)i.Tag == CookieFileOptionTag);

                CookieFileTextBox.Text = stored;
                return;
            }

            // A browser spec we do not list. yt-dlp accepts more than the common names, and
            // also BROWSER[+KEYRING][:PROFILE][::CONTAINER] - so "chrome:Profile 2" is valid.
            // Preserve it verbatim as its own option rather than mistaking it for a path and
            // silently rewriting a setting that already worked.
            var custom = new ComboBoxItem
            {
                Content = stored,
                Tag = stored,
                Foreground = Brushes.Black
            };

            CookieSourceComboBox.Items.Insert(CookieSourceComboBox.Items.Count - 1, custom);
            CookieSourceComboBox.SelectedItem = custom;
        }

        /// <summary>
        /// Distinguishes a cookies-file path from a browser specification.
        ///
        /// Anything containing a directory separator, a drive letter or a .txt extension is a
        /// path; everything else is a browser name, possibly with yt-dlp's profile/container
        /// suffixes.
        /// </summary>
        private static bool LooksLikeFilePath(string value) => CookieSourceSpec.IsFilePath(value);

        /// <summary>
        /// Returns the value to store for the current dropdown selection.
        /// </summary>
        private string ResolveCookieSource()
        {
            if (CookieSourceComboBox.SelectedItem is not ComboBoxItem selected) return string.Empty;

            string tag = (string?)selected.Tag ?? string.Empty;

            if (tag != CookieFileOptionTag) return tag;

            return CookieFileTextBox.Text.Trim();
        }

        /// <summary>
        /// Called when the user clicks the "Save Settings" button.
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.Settings;

            // 1. Update the Model in memory
            // Data flows FROM the View (text boxes) TO the Model
            settings.TmdbApiKey = TmdbKeyTextBox.Text.Trim();
            settings.GeminiApiKey = GeminiKeyTextBox.Text.Trim();
            settings.TvdbApiKey = TvdbKeyTextBox.Text.Trim(); // v1.9.2: Added TVDB key
            settings.DefaultOutputFolder = OutputFolderTextBox.Text.Trim();
            settings.PreferredVideoQuality = QualityTextBox.Text.Trim();
            settings.AutoInstallPlaywrightBrowsers = AutoInstallPlaywrightCheckBox.IsChecked == true;
            settings.AutoDownloadFfmpeg = AutoDownloadFfmpegCheckBox.IsChecked == true;
            settings.UseDownloadArchive = UseDownloadArchiveCheckBox.IsChecked == true;

            string cookieSource = ResolveCookieSource();

            // Catch an unusable cookies file here rather than letting yt-dlp abort the whole
            // download over it later.
            if (!string.IsNullOrEmpty(cookieSource)
                && cookieSource.Contains(Path.DirectorySeparatorChar)
                && !File.Exists(cookieSource))
            {
                var answer = MessageBox.Show(
                    $"The cookies file was not found:\n\n{cookieSource}\n\n"
                    + "yt-dlp treats an unreadable cookie source as a fatal error, so downloads "
                    + "will fail until this is corrected.\n\nSave anyway?",
                    "Cookies file not found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer != MessageBoxResult.Yes) return;
            }

            settings.CookieSource = cookieSource;

            // 2. Save the updated Model to the "settings.json" file
            _settingsService.SaveSettings();

            // 3. Notify and Close
            // **UPDATED:** The message no longer says "Restart the application"
            // because MainWindow.xaml.cs now reloads the services automatically.
            MessageBox.Show("Settings saved successfully! API keys have been reloaded.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        /// <summary>
        /// Called when the user clicks the "Cancel" button.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the window without saving any changes.
            this.Close();
        }

        /// <summary>
        /// Shows the cookies-file row only when that option is selected.
        /// </summary>
        private void CookieSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Fires during InitializeComponent, before the row exists.
            if (CookieFileRow == null) return;

            bool isFileOption = CookieSourceComboBox.SelectedItem is ComboBoxItem item
                                && (string?)item.Tag == CookieFileOptionTag;

            CookieFileRow.Visibility = isFileOption ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Picks a Netscape-format cookies.txt file.
        /// </summary>
        private void BrowseCookieFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a cookies.txt file",
                Filter = "Cookie files (*.txt)|*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                CookieFileTextBox.Text = dialog.FileName;
            }
        }

        /// <summary>
        /// Called when the user clicks the "Browse..." button for the output folder.
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Built-in WPF folder picker (.NET 8+); replaces the WindowsAPICodePack dependency.
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = OutputFolderTextBox.Text
            };

            if (dialog.ShowDialog(this) == true)
            {
                // Set the text box to the folder path the user selected.
                OutputFolderTextBox.Text = dialog.FolderName;
            }
        }
    }
}