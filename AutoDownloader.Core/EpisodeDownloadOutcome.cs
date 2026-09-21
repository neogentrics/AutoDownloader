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

        public bool Succeeded => ExitCode == 0;

        public static EpisodeDownloadOutcome Failed(int exitCode = -1) =>
            new EpisodeDownloadOutcome { ExitCode = exitCode };
    }
}
