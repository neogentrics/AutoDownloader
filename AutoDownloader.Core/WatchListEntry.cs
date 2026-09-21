using System;

namespace AutoDownloader.Core
{
    /// <summary>
    /// A series the app checks periodically for new episodes.
    ///
    /// This is the piece that turns the app from something you operate into something that
    /// runs: the orchestrator already downloads a season, and the yt-dlp download archive
    /// already records what has been fetched, so a scheduled re-run naturally picks up only
    /// what is new. All that was missing was somewhere to keep the list.
    /// </summary>
    public class WatchListEntry
    {
        /// <summary>Stable identifier, so an entry can be referred to without its URL.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>The series or playlist URL to check.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The show name to look up, when the URL alone does not give a usable one - a
        /// playlist URL, for instance. Null means parse it from the URL as normal.
        /// </summary>
        public string? ShowName { get; set; }

        /// <summary>Season to track. Null means whatever the URL implies.</summary>
        public int? Season { get; set; }

        /// <summary>Where to file it. Null means the configured default folder.</summary>
        public string? OutputFolder { get; set; }

        /// <summary>Set false to keep an entry without checking it.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>When this entry was last checked.</summary>
        public DateTime? LastCheckedUtc { get; set; }

        /// <summary>Episodes present after the last check, for reporting what is new.</summary>
        public int LastEpisodeCount { get; set; }

        /// <summary>Outcome of the last check, in a few words.</summary>
        public string? LastResult { get; set; }

        /// <summary>
        /// Consecutive failed checks. A source that has failed repeatedly is worth reporting
        /// differently from one that simply has nothing new.
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>Title resolved from the metadata databases, once known.</summary>
        public string? ResolvedTitle { get; set; }

        /// <summary>A short label for listings.</summary>
        public string Display => string.IsNullOrWhiteSpace(ResolvedTitle)
            ? (string.IsNullOrWhiteSpace(ShowName) ? Url : ShowName!)
            : ResolvedTitle!;
    }
}
