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

        // The URL found by Gemini/User
        public required string SourceUrl { get; set; }
    }
}