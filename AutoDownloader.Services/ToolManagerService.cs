using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Manages the download, extraction, and updating of external CLI tools.
    /// This class is now responsible for handling yt-dlp and aria2c.
    /// </summary>
    public class ToolManagerService
    {
        // This event will be used by YtDlpService to pass logs up to the UI
        public event Action<string>? OnToolLogReceived;

        // A standard, modern Firefox User-Agent. This is now public.
        public const string FIREFOX_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/115.0";

        private static readonly HttpClient _httpClient = new HttpClient();

        // URLs for our tools
        private const string YTDLP_URL = "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe";
        private const string ARIA2C_URL = "https://github.com/aria2/aria2/releases/latest/download/aria2-1.37.0-win-64bit-build1.zip"; // Example, may need updating

        private string _ytDlpPath = "";
        private string _aria2cPath = "";

        /// <summary>
        /// Ensures all required tools (yt-dlp, aria2c) are downloaded and ready.
        /// </summary>
        /// <returns>A tuple containing the file paths to yt-dlp.exe and aria2c.exe.</returns>
        public async Task<(string YtDlpPath, string Aria2cPath)> EnsureToolsAvailableAsync()
        {
            // Set the expected paths
            string appRoot = AppContext.BaseDirectory;
            _ytDlpPath = Path.Combine(appRoot, "yt-dlp.exe");
            _aria2cPath = Path.Combine(appRoot, "aria2c.exe");

            // Run both checks in parallel for speed
            await Task.WhenAll(
                EnsureYtDlpAsync(),
                EnsureAria2cAsync()
            );

            return (_ytDlpPath, _aria2cPath);
        }

        /// <summary>
        /// Checks for yt-dlp.exe, updating it if it's older than 24 hours.
        /// </summary>
        private async Task EnsureYtDlpAsync()
        {
            bool shouldDownload = true;
            if (File.Exists(_ytDlpPath))
            {
                // File exists, check if it's older than 24 hours
                var lastWriteTime = File.GetLastWriteTimeUtc(_ytDlpPath);
                if (DateTime.UtcNow - lastWriteTime < TimeSpan.FromHours(24))
                {
                    shouldDownload = false;
                }
                else
                {
                    OnToolLogReceived?.Invoke("yt-dlp.exe is older than 24 hours. Re-downloading...");
                    File.Delete(_ytDlpPath);
                }
            }
            else
            {
                OnToolLogReceived?.Invoke("yt-dlp.exe not found. Downloading latest version...");
            }

            if (shouldDownload)
            {
                await DownloadFileAsync(YTDLP_URL, _ytDlpPath);
                OnToolLogReceived?.Invoke("yt-dlp.exe downloaded successfully.");
            }
        }

        /// <summary>
        /// Checks for aria2c.exe. If not found, downloads and extracts it.
        /// </summary>
        private async Task EnsureAria2cAsync()
        {
            if (File.Exists(_aria2cPath))
            {
                return; // All good
            }

            OnToolLogReceived?.Invoke("aria2c.exe not found. Downloading...");
            string zipPath = Path.Combine(AppContext.BaseDirectory, "aria2c.zip");

            try
            {
                // 1. Download the zip file
                await DownloadFileAsync(ARIA2C_URL, zipPath);
                OnToolLogReceived?.Invoke("aria2c.zip downloaded. Extracting...");

                // 2. Extract the zip file
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    // Find the .exe file inside the zip (it's often in a subfolder)
                    // OLD LINE TO DELETE: var exeEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("aria2c.exe", StringComparison.OrdinalIgnoreCase));

                    // NEW LINE TO INSERT (Critical Fix for v1.5):
                    var exeEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("aria2c.exe", StringComparison.OrdinalIgnoreCase));

                    if (exeEntry != null)
                    {
                        // Extract it directly to our app root path
                        exeEntry.ExtractToFile(_aria2cPath, true);
                        OnToolLogReceived?.Invoke("aria2c.exe extracted successfully.");
                    }
                    else
                    {
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
                // 3. Clean up the zip file
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
        }

        /// <summary>
        /// A simple helper to download a file from a URL.
        /// </summary>
        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
        }
    }
}

