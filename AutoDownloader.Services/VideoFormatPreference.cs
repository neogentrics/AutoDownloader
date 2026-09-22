using System;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Turns a plain-language format preference into a yt-dlp format selector.
    ///
    /// This exists because the obvious default is the wrong one for a media library.
    /// "bestvideo+bestaudio" means best by encoding efficiency, which on YouTube is AV1 audio
    /// in a WebM container - excellent compression, and close to unplayable on a TV or an
    /// older Plex client without transcoding.
    ///
    /// Measured on a real episode: the same video at the same 960x720 resolution is available
    /// as AV1+Opus in WebM or H.264+AAC in MP4. Choosing the second costs nothing and avoids
    /// a conversion step that would be both slow and lossy. Converting after the fact is
    /// always worse than asking for the right stream to begin with.
    /// </summary>
    public static class VideoFormatPreference
    {
        /// <summary>Widely playable: H.264 video and AAC audio in an MP4.</summary>
        public const string Compatible = "compatible";

        /// <summary>Highest quality regardless of what can play it.</summary>
        public const string BestQuality = "best";

        /// <summary>Use the format string the user wrote themselves.</summary>
        public const string Custom = "custom";

        /// <summary>Full resolution, without paying for the top bitrate rung.</summary>
        public const string Balanced = "balanced";

        /// <summary>720p, for when space matters more than detail.</summary>
        public const string Smaller = "smaller";

        /// <summary>
        /// Prefers H.264 + AAC, falling back to any MP4, then to anything at all - so an
        /// unusual source still downloads rather than failing for want of a preferred codec.
        /// </summary>
        private const string CompatibleSelector =
            "bestvideo[vcodec^=avc1]+bestaudio[acodec^=mp4a]/best[ext=mp4]/bestvideo+bestaudio/best";

        private const string BestSelector = "bestvideo+bestaudio/best";

        /// <summary>
        /// 1080p, but not at any price.
        ///
        /// A broadcaster commonly publishes the same 1920x1080 picture at several bitrates -
        /// Food Network offers it at 4.3, 6.6 and 10.3 Mbps. "Best" takes the top rung, which
        /// turned a 42-minute episode into 3.02 GB; the 4.3 Mbps rung is the same resolution
        /// and the same H.264 codec at 1.27 GB. The cap sits above 4.3 and below 6.6 so the
        /// efficient rung is chosen where one exists.
        ///
        /// Falls back through progressively looser conditions so an unusual source still
        /// downloads rather than failing for want of a rung in the right range.
        /// </summary>
        private const string BalancedSelector =
            "bestvideo[vcodec^=avc1][height<=1080][tbr<=5000]+bestaudio[acodec^=mp4a]"
            + "/bestvideo[height<=1080][tbr<=5000]+bestaudio"
            + "/bestvideo[height<=1080]+bestaudio"
            + "/best[height<=1080]/best";

        private const string SmallerSelector =
            "bestvideo[vcodec^=avc1][height<=720]+bestaudio[acodec^=mp4a]"
            + "/bestvideo[height<=720]+bestaudio"
            + "/best[height<=720]/best";

        /// <summary>
        /// Returns the yt-dlp -f argument for a preference.
        /// </summary>
        /// <param name="preference">One of the constants above.</param>
        /// <param name="customFormat">The user's own format string, used for Custom.</param>
        public static string Resolve(string? preference, string? customFormat)
        {
            switch ((preference ?? Compatible).Trim().ToLowerInvariant())
            {
                case BestQuality:
                    return BestSelector;

                case Balanced:
                    return BalancedSelector;

                case Smaller:
                    return SmallerSelector;

                case Custom:
                    return string.IsNullOrWhiteSpace(customFormat) ? CompatibleSelector : customFormat.Trim();

                default:
                    return CompatibleSelector;
            }
        }

        /// <summary>
        /// True when the preference implies an MP4 container, so yt-dlp should be told to
        /// merge into one rather than defaulting to MKV when streams differ.
        /// </summary>
        public static bool PrefersMp4(string? preference) =>
            !string.Equals((preference ?? Compatible).Trim(), BestQuality, StringComparison.OrdinalIgnoreCase)
            && !string.Equals((preference ?? Compatible).Trim(), Custom, StringComparison.OrdinalIgnoreCase);

        /// <summary>A short description for the UI and the log.</summary>
        public static string Describe(string? preference)
        {
            switch ((preference ?? Compatible).Trim().ToLowerInvariant())
            {
                case BestQuality: return "best quality (any codec; may produce WebM/AV1)";
                case Custom: return "custom format string";
                default: return "most compatible (H.264/AAC in MP4)";
            }
        }
    }
}
