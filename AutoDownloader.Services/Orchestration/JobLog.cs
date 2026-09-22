using System;
using System.Collections.Generic;

namespace AutoDownloader.Services.Orchestration
{
    /// <summary>
    /// Severity of a message emitted by the orchestrator. The host decides how to present it -
    /// the WPF log maps these onto colours, a console host would map them onto prefixes.
    /// </summary>
    public enum JobLogLevel
    {
        /// <summary>Routine progress.</summary>
        Info,

        /// <summary>A step completed successfully.</summary>
        Success,

        /// <summary>A fact worth noticing: a detected season, a resolved title.</summary>
        Notice,

        /// <summary>Recoverable: a fallback was taken, or something looks inconsistent.</summary>
        Warning,

        /// <summary>The job, or a part of it, failed.</summary>
        Error
    }

    /// <summary>
    /// The outcome of one download job.
    /// </summary>
    public class DownloadJobResult
    {
        /// <summary>False when the job aborted before downloading anything.</summary>
        public bool Completed { get; set; }

        /// <summary>True when the job stopped because cancellation was requested.</summary>
        public bool Cancelled { get; set; }

        /// <summary>Why the job stopped early, when it did.</summary>
        public string? FailureReason { get; set; }

        /// <summary>The official title resolved from the metadata databases, if any.</summary>
        public string? OfficialTitle { get; set; }

        /// <summary>The season this job targeted.</summary>
        public int SeasonNumber { get; set; }

        /// <summary>Episodes yt-dlp reported as downloaded successfully.</summary>
        public int EpisodesSucceeded { get; set; }

        /// <summary>Episodes that failed even after the media-capture retry.</summary>
        public int EpisodesFailed { get; set; }

        /// <summary>
        /// Episodes left alone because a copy was already on disk and the answer was to keep
        /// it. Counted apart from failures: nothing went wrong, the work was simply not
        /// needed.
        /// </summary>
        public int EpisodesAlreadyPresent { get; set; }

        /// <summary>
        /// Episodes skipped because the content is DRM protected. Counted separately from
        /// failures: these cannot be downloaded by any means, so they are not worth retrying
        /// and should not read as a fault in the app.
        /// </summary>
        public int EpisodesProtected { get; set; }

        /// <summary>Video files present in the season folder once the job finished.</summary>
        public int FilesPresentAfter { get; set; }

        /// <summary>Video files added by this run.</summary>
        public int FilesAdded { get; set; }

        /// <summary>Episode count the metadata databases expected for this season.</summary>
        public int ExpectedEpisodeCount { get; set; }

        /// <summary>
        /// How many episodes this source actually listed.
        ///
        /// Tracked separately from ExpectedEpisodeCount because the two legitimately
        /// disagree: a source may carry a partial upload, a single cour of a longer run, or
        /// a season the databases count differently. Without this, downloading everything a
        /// source has still reported episodes "missing", which reads as a failure of the app
        /// rather than a limit of the source.
        /// </summary>
        public int EpisodesOffered { get; set; }

        /// <summary>
        /// Episode count the databases expect, per season this run touched.
        ///
        /// A run spanning five seasons cannot be described by one number. Comparing the whole
        /// plan against a single season's expected count produced "this source lists 61
        /// episode(s) but the databases expect 5 for season 1", which is nonsense and hid a
        /// season that really was short.
        /// </summary>
        public Dictionary<int, int> ExpectedBySeason { get; } = new Dictionary<int, int>();

        /// <summary>The folder the series was written to.</summary>
        public string? OutputFolder { get; set; }
    }

    /// <summary>
    /// Progress for a job: how far through the current file, and how far through the season.
    /// </summary>
    public class JobProgress
    {
        /// <summary>Progress of the file currently transferring, if reported.</summary>
        public AutoDownloader.Core.DownloadProgress? File { get; set; }

        /// <summary>1-based index of the episode being fetched.</summary>
        public int EpisodeIndex { get; set; }

        /// <summary>Total episodes in this job.</summary>
        public int EpisodeCount { get; set; }

        /// <summary>The season/episode label, e.g. "S02E07".</summary>
        public string? EpisodeLabel { get; set; }

        /// <summary>
        /// Overall completion across the whole job, 0-100: finished episodes plus how far
        /// through the current one. Null when the episode count is unknown.
        /// </summary>
        public double? OverallPercent
        {
            get
            {
                if (EpisodeCount <= 0) return null;

                double completed = Math.Max(0, EpisodeIndex - 1);
                double withinCurrent = (File?.Percent ?? 0d) / 100d;

                return Math.Min(100d, (completed + withinCurrent) / EpisodeCount * 100d);
            }
        }
    }

    /// <summary>
    /// A message from the orchestrator, carrying its own severity.
    /// </summary>
    public class JobLogEventArgs : EventArgs
    {
        public JobLogEventArgs(string message, JobLogLevel level)
        {
            Message = message;
            Level = level;
        }

        public string Message { get; }
        public JobLogLevel Level { get; }
    }
}
