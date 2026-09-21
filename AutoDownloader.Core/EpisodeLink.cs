namespace AutoDownloader.Core
{
    /// <summary>
    /// One episode-page link discovered on a series or season page, in the order it appeared.
    ///
    /// The ordering matters: for sites that publish no machine-readable episode metadata, the
    /// position of a link in the page is the only ordering signal available, and it is what
    /// gets matched against the episode list from TMDB/TVDB to produce a correct filename.
    /// </summary>
    public class EpisodeLink
    {
        /// <summary>
        /// Absolute URL of the episode page (not the video file itself - yt-dlp resolves that).
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The link's visible text, e.g. "Episode 7" or "Phi Brain Episode 7 English Dub".
        /// Used both for episode-number detection and for logging.
        /// </summary>
        public string? LinkText { get; set; }

        /// <summary>
        /// Episode number parsed out of the link text or URL, when one could be found.
        /// Null means "unknown", in which case ordinal position is used instead.
        /// </summary>
        public int? DetectedEpisodeNumber { get; set; }

        /// <summary>
        /// Zero-based position of this link within the page, preserved from document order.
        /// </summary>
        public int Ordinal { get; set; }
    }
}
