using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace AutoDownloader.Services // <-- CORRECTED: Now part of the Services project
{
    /// <summary>
    /// Manages the download, extraction, and auto-updating of the external
    /// command-line tools (yt-dlp and aria2c) that the application depends on.
    /// This service is initialized once by the UI (MainWindow) on startup.
    /// </summary>
    public class ToolManagerService
    {
        // --- Events ---

        /// <summary>
        /// This event is used to send log messages (like "Downloading tool...")
        /// back to the UI (MainWindow) to be displayed in the log.
        /// </summary>
        public event Action<string>? OnToolLogReceived;

        // --- Constants ---

        /// <summary>
        /// A standard, modern Firefox User-Agent string.
        /// This is used by yt-dlp to bypass simple bot-detection on some websites.
        /// </summary>
        public const string FIREFOX_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0";

        /// <summary>
        /// A shared, static HttpClient for downloading the tools.
        /// </summary>
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// The official GitHub release URL for the latest *nightly* build of yt-dlp.
        /// We use nightly to get the most up-to-date website extractors.
        /// </summary>
        private const string YTDLP_URL = "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe";

        /// <summary>
        /// The official GitHub release URL for the 64-bit Windows build of aria2c.
        /// </summary>
        private const string ARIA2C_URL = "https://github.com/aria2/aria2/releases/latest/download/aria2-1.37.0-win-64bit-build1.zip";

        // --- Private Fields ---

        /// <summary>
        /// The full path to where yt-dlp.exe should be.
        /// (e.g., C:\...\AutoDownloader\yt-dlp.exe)
        /// </summary>
        private string _ytDlpPath = "";

        /// <summary>
        /// The full path to where aria2c.exe should be.
        /// (e.g., C:\...\AutoDownloader\aria2c.exe)
        /// </summary>
        private string _aria2cPath = "";

        // --- Public Methods ---

        /// <summary>
        /// This is the main public method called by MainWindow on startup.
        /// It ensures all required tools are present and ready to be used.
        /// It runs the checks for both tools in parallel to speed up app launch.
        /// </summary>
        /// <returns>A tuple containing the file paths to yt-dlp.exe and aria2c.exe.</returns>
        public async Task<(string YtDlpPath, string Aria2cPath)> EnsureToolsAvailableAsync()
        {
            // Get the application's root directory (e.g., ...\bin\Debug\net9.0-windows)
            string appRoot = AppContext.BaseDirectory;

            // Define the expected final paths for our tools.
            _ytDlpPath = Path.Combine(appRoot, "yt-dlp.exe");
            _aria2cPath = Path.Combine(appRoot, "aria2c.exe");

            // Run both checks at the same time to save time.
            await Task.WhenAll(
                EnsureYtDlpAsync(),
                EnsureAria2cAsync()
            );

            // Return the paths to MainWindow, which will pass them to YtDlpService.
            return (_ytDlpPath, _aria2cPath);
        }

        // --- Private Helper Methods ---

        /// <summary>
        /// Checks for yt-dlp.exe. If it exists, it checks how old it is.
        /// If it's older than 24 hours or doesn't exist, it downloads a fresh copy.
        /// </summary>
        private async Task EnsureYtDlpAsync()
        {
            bool shouldDownload = true;
            if (File.Exists(_ytDlpPath))
            {
                // File exists. Let's check its age.
                var lastWriteTime = File.GetLastWriteTimeUtc(_ytDlpPath);

                // If the file is less than 24 hours old, we can skip downloading.
                if (DateTime.UtcNow - lastWriteTime < TimeSpan.FromHours(24))
                {
                    shouldDownload = false;
                }
                else
                {
                    // File is stale. Log, delete, and re-download.
                    OnToolLogReceived?.Invoke("yt-dlp.exe is older than 24 hours. Re-downloading...");
                    File.Delete(_ytDlpPath);
                }
            }
            else
            {
                // File does not exist.
                OnToolLogReceived?.Invoke("yt-dlp.exe not found. Downloading latest version...");
            }

            if (shouldDownload)
            {
                await DownloadFileAsync(YTDLP_URL, _ytDlpPath);
                OnToolLogReceived?.Invoke("yt-dlp.exe downloaded successfully.");
            }
        }

        /// <summary>
        /// Checks for aria2c.exe. If it doesn't exist, it downloads the .zip archive,
        /// extracts *only* the aria2c.exe file, and deletes the .zip.
        /// </summary>
        private async Task EnsureAria2cAsync()
        {
            // If the .exe already exists, our work is done.
            if (File.Exists(_aria2cPath))
            {
                return;
            }

            OnToolLogReceived?.Invoke("aria2c.exe not found. Downloading...");

            // Define a temporary path for the downloaded .zip file.
            string zipPath = Path.Combine(AppContext.BaseDirectory, "aria2c.zip");

            try
            {
                // 1. Download the zip file
                await DownloadFileAsync(ARIA2C_URL, zipPath);
                OnToolLogReceived?.Invoke("aria2c.zip downloaded. Extracting...");

                // 2. Open the .zip archive to read its contents
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    // Find the .exe file inside the zip.
                    // We use FullName.EndsWith because the .exe is often in a subfolder
                    // (e.g., "aria2-1.37.0-win-64bit-build1/aria2c.exe").
                    var exeEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("aria2c.exe", StringComparison.OrdinalIgnoreCase));

                    if (exeEntry != null)
                    {
                        // 3. Extract the .exe file to our app's root directory.
                        exeEntry.ExtractToFile(_aria2cPath, true); // 'true' = overwrite
                        OnToolLogReceived?.Invoke("aria2c.exe extracted successfully.");
                    }
                    else
                    {
                        // This would happen if the GitHub release structure changed.
                        throw new FileNotFoundException("Could not find aria2c.exe inside the downloaded zip file.");
                    }
                }
            }
            catch (Exception ex)
            {
                OnToolLogReceived?.Invoke($"--- ERROR: Failed to get aria2c: {ex.Message} ---");
            }
            finally
            {
                // 4. Clean up: ALWAYS delete the .zip file, even if extraction failed.
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
        }

        /// <summary>
        /// A simple helper method to download a file from a URL and save it to a path.
        /// </summary>
        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            // Use GetAsync with ResponseHeadersRead for better performance with large files.
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                // Throw an exception if the HTTP response was not successful (e.g., 404, 500).
                response.EnsureSuccessStatusCode();

                // Get the content stream from the response.
                using (var stream = await response.Content.ReadAsStreamAsync())
                // Create a new file stream to write the downloaded data to.
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // Copy the download stream directly to the file stream.
                    await stream.CopyToAsync(fileStream);
                }
            }
        }

        /// <summary>
        /// A helper method to get the cached tool paths without re-running the check.
        /// This is used by MainWindow when reloading settings.
        /// </summary>
        public (string YtDlpPath, string Aria2cPath) GetToolPaths()
        {
            return (_ytDlpPath, _aria2cPath);
        }
    }
}