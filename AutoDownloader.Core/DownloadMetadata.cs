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

        // The URL found by Gemini/User
        // Fixes CS9035: Removed 'required' keyword for simpler initialization
        public string SourceUrl { get; set; } = string.Empty;
    }
}