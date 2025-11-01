using AutoDownloader.Core; // <-- CORRECT: For DownloadMetadata
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services // <-- CORRECT: Namespace for the Services project
{
    /// <summary>
    /// Main service for launching and managing the yt-dlp download process.
    /// This service is "pure": it receives all required paths and settings from
    /// the UI (its "host") via its constructor.
    /// </summary>
    public class YtDlpService
    {
        // --- Events ---

        /// <summary>
        /// Fires every time yt-dlp writes a line to its standard output or error stream.
        /// This is used to pipe log data back to the UI's log window.
        /// </summary>
        public event Action<string>? OnOutputReceived;

        /// <summary>
        /// Fires once when the yt-dlp process exits.
        /// The integer payload is the process's exit code (0 = success).
        /// </summary>
        public event Action<int>? OnDownloadComplete;

        // --- Private Fields ---

        /// <summary>
        /// The full file path to the yt-dlp.exe executable.
        /// Injected by MainWindow on startup.
        /// </summary>
        private readonly string _ytDlpPath;

        /// <summary>
        /// The full file path to the aria2c.exe executable.
        /// Injected by MainWindow on startup.
        /// </summary>
        private readonly string _ariaPath;

        /// <summary>
        /// The user-agent string (e.g., Firefox) to pass to yt-dlp.
        /// Injected by MainWindow on startup.
        /// </summary>
        private readonly string _userAgent;

        /// <summary>
        /// The user's preferred video quality string (e.g., "bestvideo+bestaudio/best").
        /// Injected by MainWindow from SettingsService.
        /// </summary>
        private readonly string _videoQualityFormat;

        /// <summary>
        /// A reference to the currently running yt-dlp.exe process.
        /// </summary>
        private Process? _process;

        /// <summary>
        /// A token source used to gracefully cancel the process's async wait operation.
        /// </summary>
        private CancellationTokenSource? _cancellationTokenSource;

        // --- Constructor ---

        /// <summary>
        /// Initializes a new instance of the YtDlpService.
        /// This constructor "injects" all required paths and settings from the host (MainWindow).
        /// </summary>
        /// <param name="ytDlpPath">Path to yt-dlp.exe</param>
        /// <param name="ariaPath">Path to aria2c.exe</param>
        /// <param name="userAgent">User-agent string to use</param>
        /// <param name="videoQualityFormat">yt-dlp format string (e.g., "bestvideo+bestaudio/best")</param>
        public YtDlpService(string ytDlpPath, string ariaPath, string userAgent, string videoQualityFormat)
        {
            _ytDlpPath = ytDlpPath;
            _ariaPath = ariaPath;
            _userAgent = userAgent;
            _videoQualityFormat = videoQualityFormat;
        }

        // --- Public Methods ---

        /// <summary>
        /// Asynchronously launches the yt-dlp process to download a given URL.
        /// </summary>
        /// <param name="metadata">The metadata object containing the URL and official title.</param>
        /// <param name="outputFolder">The root folder for the show (e.g., ".../TV Shows/The Mandalorian").</param>
        public async Task DownloadVideoAsync(DownloadMetadata metadata, string outputFolder)
        {
            // Reset the cancellation token for this new download
            _cancellationTokenSource = new CancellationTokenSource();

            OnOutputReceived?.Invoke("--- Download process started. ---");

            try
            {
                // --- 1. Get Tool Paths ---
                // Paths are now provided by the constructor.
                OnOutputReceived?.Invoke("Tools are ready.");
                string ytDlpPath = _ytDlpPath;
                string ariaPath = _ariaPath;

                // --- 2. Build yt-dlp Arguments ---

                // Use the official title from metadata, but if it's null,
                // fall back to a dynamic yt-dlp variable to find the best title.
                string titleForFolder = metadata.OfficialTitle ?? "%(series, show, playlist_title, title, 'NA')s";

                // Use pure yt-dlp variables for season/episode padding
                string seasonYtDlp = $"%(season_number|season|{metadata.NextSeasonNumber})02d";
                string episodeYtDlp = "%(episode_number|episode|01)02d";

                // This is the final output template that defines the Plex-friendly file structure.
                string outputTemplate = Path.Combine(outputFolder,
                    $"Season {seasonYtDlp}", // -> .../Season 01/
                    $"{titleForFolder} - s{seasonYtDlp}e{episodeYtDlp} - %(episode)s.%(ext)s"); // -> Show - s01e01 - Episode Name.mp4

                var arguments = new StringBuilder();

                // 1. Core arguments
                arguments.Append($"--windows-filenames "); // Allow long file paths
                arguments.Append($"--embed-metadata ");    // Embed metadata into the file
                arguments.Append($"--ignore-errors ");      // Don't stop if one video in a playlist fails

                // *** FIX for Issue #8 (20-Item Limit) ***
                // This flag forces yt-dlp to load all items in a paged playlist (like Tubi).
                arguments.Append($"--no-paged-list ");

                // *** FIX for Issue #7 (Preferred Quality) ***
                // Use the video quality string injected from our settings.
                if (!string.IsNullOrWhiteSpace(_videoQualityFormat))
                {
                    arguments.Append($"-f \"{_videoQualityFormat}\" ");
                }

                // 2. Subtitle arguments
                arguments.Append($"--all-subs ");           // Download all available languages
                arguments.Append($"--sub-langs all ");
                arguments.Append($"--sub-format srt ");    // Download in .srt format

                // 3. Downloader and Browser arguments
                arguments.Append($"--downloader aria2c "); // Use aria2c for multi-threaded downloads
                arguments.Append($"--downloader-args \"aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M\" ");
                arguments.Append($"--user-agent \"{_userAgent}\" "); // Use the Firefox user agent
                arguments.Append($"--cookies-from-browser firefox "); // Try to use browser cookies

                // 4. Output Template (MUST be placed before the URL)
                arguments.Append($"-o \"{outputTemplate}\" ");

                // 5. URL (MUST be the final argument)
                arguments.Append($"\"{metadata.SourceUrl}\"");


                // --- 3. Configure Process ---
                _process = new Process
                {
                    StartInfo =
                    {
                        FileName = ytDlpPath,
                        Arguments = arguments.ToString(),
                        RedirectStandardOutput = true, // Capture output
                        RedirectStandardError = true,  // Capture errors
                        UseShellExecute = false,       // Required for redirection
                        CreateNoWindow = true,         // Don't show the black console window
                        StandardOutputEncoding = Encoding.UTF8, // Ensure correct encoding
                        StandardErrorEncoding = Encoding.UTF8
                    },
                    EnableRaisingEvents = true // Allows us to use the .Exited event
                };

                // --- 4. Set Up Asynchronous Event Handlers ---

                // Handle standard output (download progress, etc.)
                _process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        OnOutputReceived?.Invoke(args.Data);
                    }
                };

                // Handle error output
                _process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        OnOutputReceived?.Invoke($"[ERR] {args.Data}");
                    }
                };

                // Handle the process exiting (either finished or was killed)
                _process.Exited += (sender, args) =>
                {
                    // This is the v1.6.1 "App Freeze" fix.
                    // We consolidate all cleanup logic here.

                    // Ensure all buffered output is read before we continue.
                    _process?.WaitForExit();

                    OnOutputReceived?.Invoke($"--- Download finished. Process exited with code {_process?.ExitCode}. ---");
                    OnDownloadComplete?.Invoke(_process?.ExitCode ?? -1);

                    // Safely dispose all resources.
                    _process?.Dispose();
                    _process = null;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                };

                // --- 5. Start Process ---
                _process.Start();

                // Begin reading the output and error streams asynchronously.
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                // Wait for the process to exit asynchronously, respecting the cancellation token.
                await _process.WaitForExitAsync(_cancellationTokenSource.Token);

                // This final blocking wait is a safety net to ensure all async I/O is flushed
                // before the 'Exited' event fires.
                _process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                // This is a "clean" exception, thrown when the user clicks "Stop".
                OnOutputReceived?.Invoke("--- Download was stopped by the user. ---");
                // The StopDownload() method takes over, and the .Exited handler will do the cleanup.
            }
            catch (Exception ex)
            {
                // This is a "dirty" exception (e.g., file not found, permissions error).
                OnOutputReceived?.Invoke($"--- [FATAL] Download task failed: {ex.Message} ---");
                OnDownloadComplete?.Invoke(-1);
                // The .Exited handler will still run and clean up the process.
            }
        }

        /// <summary>
        /// Stops the currently running download process.
        /// This is called by the "Stop" button in MainWindow.
        /// </summary>
        public void StopDownload()
        {
            if (_process != null && !_process.HasExited)
            {
                OnOutputReceived?.Invoke("--- Sending stop signal... ---");

                // 1. Cancel the async wait task
                _cancellationTokenSource?.Cancel();

                // 2. Force-kill the process and all its children
                try
                {
                    // The 'true' argument ensures all child processes (like aria2c) are also terminated.
                    _process.Kill(true);
                    OnOutputReceived?.Invoke("--- All download processes terminated successfully. ---");
                }
                catch (Exception ex)
                {
                    OnOutputReceived?.Invoke($"--- Error killing process: {ex.Message} ---");
                }
            }
        }
    }
}