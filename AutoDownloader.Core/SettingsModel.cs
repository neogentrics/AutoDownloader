using System;
using System.IO;

namespace AutoDownloader.Core // <-- CORRECT: This is a data model, it belongs in .Core
{
    /// <summary>
    /// Represents the user-configurable application settings.
    /// This class is a "POCO" (Plain Old C# Object) - it just holds data.
    /// Default values are set here for the application's first-run initialization.
    /// This model is serialized to/from "settings.json" by the SettingsService.
    /// </summary>
    public class SettingsModel
    {
        // --- General Settings ---

        /// <summary>
        /// The default root folder where all downloads will be saved.
        /// (e.g., "C:\Users\YourUser\Videos\Downloads")
        /// </summary>
        public string DefaultOutputFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Downloads"
        );

        /// <summary>
        /// The yt-dlp format string for preferred video quality.
        /// This is read by YtDlpService and passed as the "-f" argument.
        /// </summary>
        public string PreferredVideoQuality { get; set; } = "bestvideo+bestaudio/best";

        // --- API Keys ---

        /// <summary>
        /// The user's v3 API key for The Movie Database (TMDB).
        /// Used by MetadataService for the primary metadata lookup.
        /// </summary>
        public string TmdbApiKey { get; set; } = "YOUR_TMDB_API_KEY_HERE";

        /// <summary>
        /// The user's API key for Google Gemini.
        /// Used by SearchService for the "Smart Search" feature.
        /// </summary>
        public string GeminiApiKey { get; set; } = "YOUR_GEMINI_API_KEY_HERE";

        /// <summary>
        /// The user's v4 API key for The TV Database (TVDB).
        /// Used by MetadataService as the fallback for Anime/other shows.
        /// </summary>
        public string TvdbApiKey { get; set; } = "YOUR_TVDB_API_KEY_HERE";

        // --- Advanced Controls (Future Use) ---

        /// <summary>
        /// A placeholder for a future feature to limit concurrent downloads,
        /// likely by controlling aria2c or running multiple yt-dlp instances.
        /// </summary>
        public int MaxConcurrentDownloads { get; set; } = 3;
    }
}