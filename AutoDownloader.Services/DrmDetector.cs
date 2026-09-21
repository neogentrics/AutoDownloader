using System;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Recognises when a download failed because the content is DRM protected.
    ///
    /// This is worth telling apart from an ordinary failure. A protected stream is encrypted
    /// at source: no downloader recovers it, and no amount of retrying, scraping or watching
    /// network traffic will change that. Treating it as a normal failure meant the app spent
    /// twelve seconds per episode driving a headless browser to discover nothing, then
    /// reported "no media requests were observed", which hides the actual reason.
    ///
    /// Detecting it lets the app skip the fallback and say plainly what happened.
    /// </summary>
    public static class DrmDetector
    {
        /// <summary>
        /// Phrases that identify protected content. The three named systems are the
        /// commercial DRM schemes used by streaming services; the rest are how yt-dlp words
        /// the failure.
        /// </summary>
        private static readonly string[] Markers =
        {
            "drm protected",
            "drm-protected",
            "protected by drm",
            "is drm",
            "widevine",
            "playready",
            "fairplay",
        };

        /// <summary>
        /// True when a line of downloader output reports DRM.
        /// </summary>
        public static bool IsDrmMessage(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            foreach (var marker in Markers)
            {
                if (line.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
