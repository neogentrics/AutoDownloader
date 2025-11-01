using AutoDownloader.Core; // <-- CORRECT: References the .Core project for SettingsModel
using System;
using System.IO;
using System.Text.Json;

namespace AutoDownloader.Services // <-- CORRECT: Namespace for the Services project
{
    /// <summary>
    /// Handles loading and saving application settings (the SettingsModel)
    /// to a persistent JSON file on the user's computer.
    /// This service ensures that API keys and user preferences are saved between sessions.
    /// </summary>
    public class SettingsService
    {
        // --- Private Fields ---

        /// <summary>
        /// The full, absolute path to the "settings.json" file.
        /// (e.g., "C:\Users\YourUser\AppData\Roaming\AutoDownloader\settings.json")
        /// </summary>
        private readonly string _settingsFilePath;

        /// <summary>
        /// The in-memory cache of the application settings.
        /// This is the "single source of truth" while the app is running.
        /// It is initialized with default values from SettingsModel.
        /// </summary>
        private SettingsModel _settings = new SettingsModel();

        // --- Public Properties ---

        /// <summary>
        /// Public read-only property to allow other services (and the UI)
        /// to access the currently loaded settings.
        /// </summary>
        public SettingsModel Settings => _settings;

        // --- Constructor ---

        /// <summary>
        /// Initializes a new instance of the SettingsService.
        /// This is called once by MainWindow when the application starts.
        /// </summary>
        public SettingsService()
        {
            // Get the user's "Roaming" AppData folder.
            // Using "Roaming" ensures settings would follow a user in a domain environment.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // Create a dedicated folder for our application inside AppData.
            string appFolder = Path.Combine(appData, "AutoDownloader");

            // This command safely creates the directory if it doesn't already exist.
            Directory.CreateDirectory(appFolder);

            // Define the full path to our settings file.
            _settingsFilePath = Path.Combine(appFolder, "settings.json");

            // Immediately attempt to load settings from the file when the service is created.
            LoadSettings();
        }

        // --- Public Methods ---

        /// <summary>
        /// Synchronously loads settings from the "settings.json" file.
        /// If the file does not exist (e.g., first run), it creates one.
        /// </summary>
        public void LoadSettings()
        {
            // Check if the file exists on disk.
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    // Read the entire JSON file into a string.
                    string jsonString = File.ReadAllText(_settingsFilePath);

                    // Deserialize the JSON string back into our SettingsModel object.
                    // If the file is corrupt or empty (jsonString is null),
                    // the '??' (null-coalescing) operator will initialize a new, default SettingsModel.
                    _settings = JsonSerializer.Deserialize<SettingsModel>(jsonString) ?? new SettingsModel();
                }
                catch (Exception ex)
                {
                    // If the JSON is badly malformed, deserialization will fail.
                    // We log the error and fall back to the default settings
                    // to prevent the app from crashing on startup.
                    Console.WriteLine($"Error loading settings: {ex.Message}");
                    _settings = new SettingsModel();
                }
            }
            else
            {
                // This is the "first run" scenario.
                // The file doesn't exist, so we create one by saving
                // the default _settings object (which was initialized in the field).
                SaveSettings();
            }
        }

        /// <summary>
        /// Synchronously saves the current in-memory settings (the _settings object)
        /// to the "settings.json" file, overwriting it.
        /// This is called from the PreferencesWindow when the user clicks "Save".
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                // Configure the serializer to "pretty-print" the JSON.
                // This makes the settings.json file human-readable.
                var options = new JsonSerializerOptions { WriteIndented = true };

                // Serialize the _settings object into a JSON string.
                string jsonString = JsonSerializer.Serialize(_settings, options);

                // Write the string to the file, overwriting any existing content.
                File.WriteAllText(_settingsFilePath, jsonString);
            }
            catch (Exception ex)
            {
                // This could fail if the file is read-only or locked by another process.
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}