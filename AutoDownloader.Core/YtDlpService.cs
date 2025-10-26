using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AutoDownloader.Core
{
    /// <summary>
    /// This is our "engine". It knows nothing about the UI.
    /// Its only job is to run yt-dlp.exe with the correct arguments
    /// and report back what's happening.
    /// </summary>
    public class YtDlpService
    {
        // Event for sending live console output to the UI
        public event Action<string?>? OnOutputReceived;

        // Event for reporting when the download is fully complete
        public event Action<bool, string>? OnDownloadComplete;

        private static readonly HttpClient _httpClient = new HttpClient();

        // We will keep the nightly build URL. This is the best version.
        private const string YTDLP_URL = "https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe";

        // This is the "ID Card" for a standard, modern Firefox browser on Windows.
        private const string FIREFOX_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/119.0";

        private readonly string _ytDlpPath;
        private Process? _process;

        public YtDlpService()
        {
            _ytDlpPath = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        }

        /// <summary>
        /// Checks if yt-dlp.exe exists.
        /// If it's older than 24 hours, it's deleted.
        /// If it's missing (or was deleted), it's downloaded.
        /// </summary>
        private async Task EnsureToolsAvailableAsync()
        {
            // We check if the file exists and how old it is.
            if (File.Exists(_ytDlpPath))
            {
                // Get file info
                var fileInfo = new FileInfo(_ytDlpPath);

                // Check if the file was written to more than 24 hours ago
                if (fileInfo.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1))
                {
                    OnOutputReceived?.Invoke("yt-dlp.exe is older than 24 hours. Deleting to force update...");
                    try
                    {
                        File.Delete(_ytDlpPath);
                        OnOutputReceived?.Invoke("Old version deleted.");
                    }
                    catch (Exception ex)
                    {
                        // This can happen if the file is locked, etc.
                        OnOutputReceived?.Invoke($"Could not delete old yt-dlp.exe: {ex.Message}. Will try to use it...");
                        return; // Exit and try to use the old file, as we can't delete it.
                    }
                }
                else
                {
                    // File exists and is recent. All good.
                    return;
                }
            }

            // File is missing (or was just deleted), let's download it.
            OnOutputReceived?.Invoke("yt-dlp.exe not found. Downloading latest version...");
            try
            {
                // Set a modern user-agent for our downloader, too, just in case.
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(FIREFOX_USER_AGENT);

                using (var response = await _httpClient.GetAsync(YTDLP_URL, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode(); // Throw if we get a 404, 500, etc.

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(_ytDlpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
                OnOutputReceived?.Invoke("Download complete: yt-dlp.exe");
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"Failed to download yt-dlp.exe: {ex.Message}");
                // This is a critical failure, so we throw to stop the download.
                throw new InvalidOperationException("Could not download required tool: yt-dlp.exe", ex);
            }
        }

        /// <summary>
        /// Starts the download process on a background thread.
        /// </summary>
        public async Task DownloadVideoAsync(string url, string outputFolder)
        {
            // First, make sure yt-dlp.exe exists (or download/update it).
            await EnsureToolsAvailableAsync();

            // Example: C:\Users\User\Videos\My Show\Season 01\My Show - s01e01 - Pilot.mp4
            string outputTemplate = Path.Combine(outputFolder,
                "%(series)s", // Show Name folder
                "Season %(season_number)s", // Season subfolder
                "%(series)s - s%(season_number)02de%(episode_number)02d - %(title)s.%(ext)s"); // Plex-friendly filename

            var arguments = new StringBuilder();

            // --- WE HAVE REVERTED TO A CLEAN, GENERIC COMMAND SET ---

            // --- Download and Naming Arguments ---
            arguments.Append($"--windows-filenames "); // Sanitize filenames
            arguments.Append($"--all-subs "); // Get all subtitles
            arguments.Append($"--sub-format srt "); // Prefer .srt format
            arguments.Append($"--write-auto-subs "); // Get auto-generated subs if no others exist
            arguments.Append($"-o \"{outputTemplate}\" "); // Set our output path/name template
            arguments.Append($"\"{url}\""); // Finally, the URL to download

            // We run the main download logic on a background thread
            // using Task.Run() so we don't block the UI.
            await Task.Run(() =>
            {
                try
                {
                    _process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _ytDlpPath,
                            Arguments = arguments.ToString(),
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8, // Tell C# how to READ the output
                            StandardErrorEncoding = Encoding.UTF8
                        },
                        EnableRaisingEvents = true
                    };

                    _process.OutputDataReceived += (sender, args) => Process_OutputDataReceived(args.Data);
                    _process.ErrorDataReceived += (sender, args) => Process_OutputDataReceived(args.Data); // We pipe both to the same log

                    _process.Exited += (sender, args) => Process_Exited(string.Empty); // Handle normal exit

                    _process.Start();

                    // Begin reading the output streams asynchronously
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();

                    _process.WaitForExit(); // Wait for the process to complete
                }
                catch (Exception ex)
                {
                    // This catches sync errors (like "process not found")
                    Process_Exited($"Failed to start process: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Fires every time yt-dlp writes a line to its console.
        /// </summary>
        private void Process_OutputDataReceived(string? data)
        {
            if (data != null)
            {
                // Send the line to our UI
                OnOutputReceived?.Invoke(data);
            }
        }

        /// <summary>
        /// Fires when the process has fully exited.
        /// </summary>
        private void Process_Exited(string startupErrorMessage)
        {
            if (_process == null) return; // Guard clause

            if (!string.IsNullOrEmpty(startupErrorMessage))
            {
                OnDownloadComplete?.Invoke(false, startupErrorMessage);
                return;
            }

            int exitCode = _process.ExitCode;
            _process.Dispose(); // Clean up resources
            _process = null;

            if (exitCode == 0)
            {
                OnDownloadComplete?.Invoke(true, "Download complete!");
            }
            else
            {
                OnDownloadComplete?.Invoke(false, $"Download failed. Process exited with code {exitCode}.");
            }
        }
    }
}

