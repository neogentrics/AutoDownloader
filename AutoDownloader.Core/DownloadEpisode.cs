using System;

namespace AutoDownloader.Core
{
    public class DownloadEpisode
    {
        public int EpisodeNumber { get; set; }

        public string? EpisodeTitle { get; set; }

        /// <summary>
        /// What the episode is about, when the source said. The databases do not carry this
        /// on the free tiers; a listing tile does, and gives it away with the title.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>The date the episode first aired, when the source said.</summary>
        public DateTime? AirDate { get; set; }

        /// <summary>Runtime in minutes, when the source said.</summary>
        public int? RuntimeMinutes { get; set; }

        /// <summary>The content rating, when the source said.</summary>
        public string? Rating { get; set; }
    }
}
