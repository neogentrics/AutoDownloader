using System.IO;

namespace AutoDownloader.Core
{
    /// <summary>
    /// Represents the user-configurable application settings.
    /// Default values are set here for first-run initialization.
    /// </summary>
    public class SettingsModel
    {
        // General Settings
        public string DefaultOutputFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Downloads"
        );
        public string PreferredVideoQuality { get; set; } = "bestvideo+bestaudio/best"; // yt-dlp format string

        // API Keys (Critical for v1.8)
        public string TmdbApiKey { get; set; } = "YOUR_TMDB_API_KEY_HERE";
        public string GeminiApiKey { get; set; } = "YOUR_GEMINI_API_KEY_HERE";

        // Advanced Controls
        public int MaxConcurrentDownloads { get; set; } = 3; // For future aria2c multi-download feature
    }
}