using AutoDownloader.Services; // <-- CORRECT: Using the .Services project
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.IO;
using System.Windows;
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
        /// Called when the user clicks the "Browse..." button for the output folder.
        /// </summary>
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Use the WindowsAPICodePack to show a modern folder picker dialog.
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                InitialDirectory = OutputFolderTextBox.Text
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                // Set the text box to the folder path the user selected.
                OutputFolderTextBox.Text = dialog.FileName;
            }
        }
    }
}