namespace AutoDownloader.Core
{
    /// <summary>
    /// How one episode download ended.
    ///
    /// Richer than a bare exit code because "failed" and "cannot be downloaded at all" call
    /// for different handling: a failure is worth retrying through the media-capture
    /// fallback, whereas protected content is encrypted at source and retrying only wastes
    /// time and produces a misleading message.
    /// </summary>
    public class EpisodeDownloadOutcome
    {
        /// <summary>yt-dlp's exit code. Zero means the file was written.</summary>
        public int ExitCode { get; set; }

        /// <summary>True when the downloader reported the content as DRM protected.</summary>
        public bool DrmProtected { get; set; }

        /// <summary>
        /// True when the download was stopped rather than having failed on its own.
        ///
        /// Kept apart from a plain failure because there is nothing to recover: retrying a
        /// stopped download through the media-capture fallback wastes twelve seconds per
        /// episode proving that the user still wants it stopped.
        /// </summary>
        public bool Cancelled { get; set; }

        public bool Succeeded => ExitCode == 0;

        public static EpisodeDownloadOutcome Failed(int exitCode = -1) =>
            new EpisodeDownloadOutcome { ExitCode = exitCode };

        public static EpisodeDownloadOutcome Stopped() =>
            new EpisodeDownloadOutcome { ExitCode = -1, Cancelled = true };
    }
}
