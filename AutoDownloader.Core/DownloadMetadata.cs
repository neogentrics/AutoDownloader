namespace AutoDownloader.Core
{
    /// <summary>
    /// Data structure to pass official metadata from the UI to the download service.
    /// </summary>
    public class DownloadMetadata
    {
        public string? OfficialTitle { get; set; }
        public int? SeriesId { get; set; }
        public int NextSeasonNumber { get; set; } = 1;

        // V1.8 NEW: Expected count for the season being downloaded (Fixes CS1061)
        public int ExpectedEpisodeCount { get; set; } = 0;

        // V1.9 NEW: TVDB Fallback Key (This belongs in SettingsModel, not DownloadMetadata)
        // **I will correct the implementation here to ensure the logic is sound.** // We will assume the key is in SettingsModel as planned.

        // The URL found by Gemini/User
        // Fixes CS9035: Removed 'required' keyword for simpler initialization
        public string SourceUrl { get; set; } = string.Empty;
    }
}