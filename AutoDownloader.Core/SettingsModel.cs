using System;
using System.IO;

namespace AutoDownloader.Core // <-- CORRECT: This is a data model, it belongs in .Core
{
    /// <summary>
    /// Represents the user-configurable application settings.
    /// This class is a "POCO" (Plain Old C# Object) - it just holds data.
    /// Default values are set here for the application's first-run initialization.
    /// This model is serialized to/from "settings.json" by the SettingsService.
    /// </summary>
    public class SettingsModel
    {
        // --- General Settings ---

        /// <summary>
        /// The default root folder where all downloads will be saved.
        /// (e.g., "C:\Users\YourUser\Videos\Downloads")
        /// </summary>
        public string DefaultOutputFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Downloads"
        );

        /// <summary>
        /// The yt-dlp format string for preferred video quality.
        /// This is read by YtDlpService and passed as the "-f" argument.
        /// </summary>
        public string PreferredVideoQuality { get; set; } = "bestvideo+bestaudio/best";

        // --- API Keys ---

        /// <summary>
        /// The user's v3 API key for The Movie Database (TMDB).
        /// Used by MetadataService for the primary metadata lookup.
        /// </summary>
        public string TmdbApiKey { get; set; } = "YOUR_TMDB_API_KEY_HERE";

        /// <summary>
        /// The user's API key for Google Gemini.
        /// Used by SearchService for the "Smart Search" feature.
        /// </summary>
        public string GeminiApiKey { get; set; } = "YOUR_GEMINI_API_KEY_HERE";

        /// <summary>
        /// The user's v4 API key for The TV Database (TVDB).
        /// Used by MetadataService as the fallback for Anime/other shows.
        /// </summary>
        public string TvdbApiKey { get; set; } = "YOUR_TVDB_API_KEY_HERE";

        /// <summary>
        /// How to choose between the formats a site offers: "compatible", "best" or "custom".
        ///
        /// Defaults to compatible, because "best" means best by compression efficiency, which
        /// on YouTube is AV1 in a WebM container - superb quality per byte, and close to
        /// unplayable on a TV or an older Plex client. The same video is usually also offered
        /// as H.264/AAC in MP4 at the same resolution, which costs nothing to prefer.
        ///
        /// Use "custom" to supply your own selector in PreferredVideoQuality.
        /// </summary>
        public string FormatPreference { get; set; } = "compatible";

        /// <summary>
        /// The theme by name, matching one offered by the application. An unrecognised value
        /// falls back to the first rather than failing, since this file gets hand-edited.
        /// </summary>
        public string Theme { get; set; } = "Dark";

        /// <summary>
        /// Where yt-dlp should read cookies from, for sites that gate content behind a login.
        /// Empty (the default) means send no cookies at all.
        /// Accepts either a browser name ("firefox", "chrome", "edge", "brave", ...) or the
        /// full path to a Netscape-format cookies.txt file.
        ///
        /// This must default to empty: yt-dlp treats an unreadable cookie source as a FATAL
        /// error and downloads nothing, so hardcoding a browser breaks every machine that
        /// does not have that browser installed.
        /// </summary>
        public string CookieSource { get; set; } = string.Empty;

        /// <summary>
        /// If true, download a private copy of ffmpeg when none is found on PATH or beside
        /// the application. ffmpeg is required to merge separate video/audio streams (which
        /// the default "bestvideo+bestaudio" format produces), to embed metadata, and to
        /// convert subtitles. Without it most downloads fail at the merge step.
        /// </summary>
        public bool AutoDownloadFfmpeg { get; set; } = true;

        /// <summary>
        /// If true, keep a yt-dlp download archive per series so already-downloaded episodes
        /// are skipped on subsequent runs. This is what makes repeat/scheduled runs cheap.
        /// </summary>
        public bool UseDownloadArchive { get; set; } = true;

        // --- Advanced Controls (Future Use) ---

        /// <summary>
        /// A placeholder for a future feature to limit concurrent downloads,
        /// likely by controlling aria2c or running multiple yt-dlp instances.
        /// </summary>
        public int MaxConcurrentDownloads { get; set; } = 3;

        /// <summary>
        /// If true, the application will attempt to automatically install Playwright browsers
        /// (or use the Playwright API) on first run. This may require network access.
        /// Default: true (recommended for scraper fallback).
        /// </summary>
        public bool AutoInstallPlaywrightBrowsers { get; set; } = true;
    }
}