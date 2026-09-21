namespace AutoDownloader.Core
{
    /// <summary>
    /// Which tool reported a progress update.
    /// </summary>
    public enum ProgressSource
    {
        /// <summary>yt-dlp's own progress template (native downloader, HLS fragments, ...).</summary>
        YtDlp,

        /// <summary>aria2c's status line, used when it is the external downloader.</summary>
        Aria2c
    }

    /// <summary>
    /// A single progress reading for the file currently transferring.
    ///
    /// Two sources are necessary rather than one. When aria2c is the external downloader it
    /// performs the transfer itself, and yt-dlp reports progress exactly once - at 100%, after
    /// the fact - so a bar driven only by yt-dlp would sit at zero and then snap to done.
    /// aria2c prints its own status line roughly once a second, which is the useful signal.
    /// When yt-dlp downloads directly (HLS and similar) the reverse is true.
    /// </summary>
    public class DownloadProgress
    {
        /// <summary>Completion of the current file, 0-100. Null when not reported.</summary>
        public double? Percent { get; set; }

        /// <summary>Transfer rate as text, e.g. "1.95MiB/s". Null when not reported.</summary>
        public string? Speed { get; set; }

        /// <summary>Estimated time remaining as text, e.g. "00:42" or "9s". Null when unknown.</summary>
        public string? Eta { get; set; }

        /// <summary>Bytes transferred so far, as text, e.g. "1.0MiB".</summary>
        public string? Downloaded { get; set; }

        /// <summary>Total size as text, e.g. "6.0MiB".</summary>
        public string? Total { get; set; }

        /// <summary>Which tool produced this reading.</summary>
        public ProgressSource Source { get; set; }
    }
}
