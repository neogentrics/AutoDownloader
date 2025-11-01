using AutoDownloader.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Handles loading and saving application settings to a JSON file.
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsFilePath;
        private SettingsModel _settings = new SettingsModel();

        public SettingsModel Settings => _settings;

        public SettingsService()
        {
            // Place the settings file in the application's local data folder
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "AutoDownloader");

            Directory.CreateDirectory(appFolder);
            _settingsFilePath = Path.Combine(appFolder, "settings.json");

            // Attempt to load settings immediately
            LoadSettings();
        }

        /// <summary>
        /// Synchronously loads settings from the JSON file or creates a default file if none exists.
        /// </summary>
        public void LoadSettings()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string jsonString = File.ReadAllText(_settingsFilePath);
                    // Deserialize the JSON. We use 'Suppress' because we know the file structure.
                    _settings = JsonSerializer.Deserialize<SettingsModel>(jsonString) ?? new SettingsModel();
                }
                catch (Exception ex)
                {
                    // Fallback to default settings on error
                    Console.WriteLine($"Error loading settings: {ex.Message}");
                    _settings = new SettingsModel();
                }
            }
            else
            {
                // File does not exist, save the default settings
                SaveSettings();
            }
        }

        /// <summary>
        /// Synchronously saves the current settings model to the JSON file.
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_settingsFilePath, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}