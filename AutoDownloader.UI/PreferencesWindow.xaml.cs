using AutoDownloader.Core;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace AutoDownloader.UI
{
    public partial class PreferencesWindow : Window
    {
        private readonly SettingsService _settingsService;

        public PreferencesWindow(SettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var settings = _settingsService.Settings;
            TmdbKeyTextBox.Text = settings.TmdbApiKey;
            GeminiKeyTextBox.Text = settings.GeminiApiKey;
            OutputFolderTextBox.Text = settings.DefaultOutputFolder;
            QualityTextBox.Text = settings.PreferredVideoQuality;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.Settings;

            // 1. Update the Model
            settings.TmdbApiKey = TmdbKeyTextBox.Text.Trim();
            settings.GeminiApiKey = GeminiKeyTextBox.Text.Trim();
            settings.DefaultOutputFolder = OutputFolderTextBox.Text.Trim();
            settings.PreferredVideoQuality = QualityTextBox.Text.Trim();

            // 2. Save to File
            _settingsService.SaveSettings();

            // 3. Notify and Close
            MessageBox.Show("Settings saved successfully! Restart the application to fully apply changes.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the window without saving changes
            this.Close();
        }

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
    }
}