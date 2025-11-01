namespace AutoDownloader.Core // <-- CORRECT: This is a data model, it belongs in .Core
{
    /// <summary>
    /// A data structure (POCO) used to pass information between the UI, 
    /// the metadata services, and the download service.
    /// It collects all necessary data for a single download job.
    /// </summary>
    public class DownloadMetadata
    {
        /// <summary>
        /// The official show title as found by a metadata service (TMDB or TVDB).
        /// Example: "The Mandalorian"
        /// </summary>
        public string? OfficialTitle { get; set; }

        /// <summary>
        /// The unique ID for the series from the metadata database (TMDB or TVDB).
        /// Example: 82856 (TMDB ID)
        /// </summary>
        public int? SeriesId { get; set; }

        /// <summary>
        /// The season number to be downloaded.
        /// This defaults to 1 but can be overridden by the URL parser.
        /// </summary>
        public int NextSeasonNumber { get; set; } = 1;

        /// <summary>
        /// The official number of episodes for the 'NextSeasonNumber'.
        /// This is used by the Content Verification step in MainWindow.
        /// </summary>
        public int ExpectedEpisodeCount { get; set; } = 0;

        /// <summary>
        /// The source URL for the content, either provided by the user
        /// or found by the SearchService (Gemini).
        /// Example: "https://tubitv.com/series/12345/show-name"
        /// </summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>
        /// A list of episodes (episode number + title) for the targeted season.
        /// Filled by MetadataService when available (e.g., from TMDB).
        /// </summary>
        public List<DownloadEpisode> Episodes { get; set; } = new List<DownloadEpisode>();
    }
}