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
        /// Season number parsed from the link, when the site puts one there (for example
        /// ".../s01-e01-final-exam"). Null means unknown.
        ///
        /// This matters for listing pages that cover every season at once: without it, five
        /// seasons of links all get filed as season 1.
        /// </summary>
        public int? DetectedSeasonNumber { get; set; }

        /// <summary>
        /// Zero-based position of this link within the page, preserved from document order.
        /// </summary>
        public int Ordinal { get; set; }

        /// <summary>
        /// True when this link was in the page itself, rather than mined out of an API
        /// response the page happened to fetch.
        ///
        /// Mining is what rescues a listing whose markup carries no links at all, but it
        /// scoops up everything a payload mentions - navigation, recommendations, the user's
        /// own watchlist - and those can easily outnumber the episodes.
        /// </summary>
        public bool FromDocument { get; set; }
    }
}
