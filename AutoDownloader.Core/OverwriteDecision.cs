namespace AutoDownloader.Core
{
    /// <summary>
    /// What to do about an episode that is already on disk.
    ///
    /// The "all" answers exist because a season is the usual unit of work: being asked the
    /// same question twelve times in a row is not a choice, it is an obstacle.
    /// </summary>
    public enum OverwriteDecision
    {
        /// <summary>Keep the existing file and move on.</summary>
        Skip,

        /// <summary>Download again, replacing what is there.</summary>
        Overwrite,

        /// <summary>Keep every existing file for the rest of this job, without asking again.</summary>
        SkipAll,

        /// <summary>Replace every existing file for the rest of this job, without asking again.</summary>
        OverwriteAll,

        /// <summary>Abandon the job.</summary>
        Cancel
    }

    /// <summary>
    /// An episode found already present, described well enough to decide about.
    /// </summary>
    public class ExistingEpisode
    {
        /// <summary>Full path of the file already on disk.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Its size in bytes, so an obviously truncated file is recognisable.</summary>
        public long SizeBytes { get; set; }

        /// <summary>When it was written.</summary>
        public System.DateTime LastModified { get; set; }

        /// <summary>"S01E03", for the prompt.</summary>
        public string EpisodeLabel { get; set; } = string.Empty;

        /// <summary>The episode's title, when known.</summary>
        public string? EpisodeTitle { get; set; }

        /// <summary>Human-readable size, e.g. "62.2 MB".</summary>
        public string SizeDisplay
        {
            get
            {
                double size = SizeBytes;
                string[] units = { "bytes", "KB", "MB", "GB" };
                int unit = 0;

                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }

                return unit == 0 ? $"{SizeBytes} bytes" : $"{size:0.0} {units[unit]}";
            }
        }
    }
}
