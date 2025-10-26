using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AutoDownloader.Core
{
    /// <summary>
    /// Manages the acquisition and updating of external tools like yt-dlp and aria2c.
    /// </summary>
    public class ToolManagerService
    {
        // This event will be used to send log messages back to whoever is using this service.
        public event Action<string, bool>? OnLogMessage;

        private const string YTDLP_URL = "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe";
        private const string ARIA2C_URL = "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip";

        private readonly string _ytDlpPath;
        private readonly string _aria2cPath;
        private readonly string _tempDir;

        public ToolManagerService()
        {
            _tempDir = Path.Combine(AppContext.BaseDirectory, "temp");
            _ytDlpPath = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
            _aria2cPath = Path.Combine(AppContext.BaseDirectory, "aria2c.exe");
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>
        /// Public method to ensure all required tools are present and up-to-date.
        /// Returns the paths to the tools.
        /// </summary>
        public async Task<(string YtDlpPath, string Aria2cPath)> EnsureToolsAvailableAsync()
        {
            await EnsureYtDlpAsync();
            await EnsureAria2cAsync();
            return (_ytDlpPath, _aria2cPath);
        }

        /// <summary>
        /// Checks for yt-dlp and downloads it if missing or old.
        /// </summary>
        private async Task EnsureYtDlpAsync()
        {
            if (File.Exists(_ytDlpPath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(_ytDlpPath)).TotalHours < 24)
            {
                // File exists and is new, do nothing.
                return;
            }
            await DownloadFileAsync(YTDLP_URL, _ytDlpPath, "yt-dlp.exe");
        }

        /// <summary>
        /// Checks for aria2c and downloads/extracts it if missing.
        /// </summary>
        private async Task EnsureAria2cAsync()
        {
            if (File.Exists(_aria2cPath))
            {
                // File exists, we're good.
                return;
            }

            OnLogMessage?.Invoke("aria2c.exe not found. Downloading...", false);
            string zipPath = Path.Combine(_tempDir, "aria2.zip");

            await DownloadFileAsync(ARIA2C_URL, zipPath, "aria2c");

            OnLogMessage?.Invoke("Extracting aria2c...", false);
            try
            {
                string extractDir = Path.Combine(_tempDir, "aria2-extract");
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, true);
                }
                Directory.CreateDirectory(extractDir);

                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

                var exePath = Directory.GetFiles(extractDir, "aria2c.exe", SearchOption.AllDirectories);
                if (exePath.Length == 0)
                {
                    throw new FileNotFoundException("Could not find aria2c.exe inside the downloaded zip.");
                }

                File.Move(exePath[0], _aria2cPath, true);
                OnLogMessage?.Invoke("aria2c installed successfully.", false);
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"Failed to extract aria2c: {ex.Message}", true);
                throw;
            }
            finally
            {
                // Clean up
                if (File.Exists(zipPath)) File.Delete(zipPath);
                if (Directory.Exists(Path.Combine(_tempDir, "aria2-extract")))
                {
                    Directory.Delete(Path.Combine(_tempDir, "aria2-extract"), true);
                }
            }
        }

        /// <summary>
        /// A helper function to download a file from a URL.
        /// </summary>
        private async Task DownloadFileAsync(string url, string destinationPath, string toolName)
        {
            OnLogMessage?.Invoke($"{toolName} not found or is old. Downloading latest version...", false);
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "AutoDownloaderApp");
                    using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await stream.CopyToAsync(fileStream);
                        }
                    }
                }
                OnLogMessage?.Invoke($"{toolName} download complete.", false);
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"Failed to download {toolName}: {ex.Message}", true);
                throw; // Re-throw to stop the download process
            }
        }
    }
}
