using AutoDownloader.Core;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Main service for handling the yt-dlp download process.
    /// It now relies on ToolManagerService to provide the tool paths.
    /// </summary>
    public class YtDlpService
    {
        // --- Events to send data back to the UI ---
        public event Action<string>? OnOutputReceived;
        public event Action<int>? OnDownloadComplete;

        // --- Private Fields ---
        private readonly string _ytDlpPath;
        private readonly string _ariaPath;
        private Process? _process;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Constructor now "injects" the tool paths.
        /// </summary>
        public YtDlpService(string ytDlpPath, string ariaPath)
        {
            _ytDlpPath = ytDlpPath;
            _ariaPath = ariaPath;
        }

        /// <summary>
        /// Asynchronously downloads a video from a given URL.
        /// </summary>
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

                // --- 2. Build Arguments ---

                // --- CODE in YtDlpService.cs (Inside DownloadVideoAsync, replace the argument building block) ---

                // V1.7 FIX: Use Official Title from metadata if available!
                string titleForFolder = metadata.OfficialTitle ?? "%(series, show, playlist_title, title, 'NA')s";

                // V1.7.3 FIX: Use pure YT-DLP template variables for padding.
                // %02d in the tag name forces two-digit padding.
                string seasonYtDlp = "%(season_number|season|1)02d";
                string episodeYtDlp = "%(episode_number|episode|01)02d";

                // We use '|' for fallback inside the tag for better compatibility.

                // File Name: We will only use %(title)s, which usually contains the episode name.
                string outputTemplate = Path.Combine(outputFolder,
                    // Season Folder: Use the correct two-digit padding tag.
                    $"Season {seasonYtDlp}",
                    // File Name: Uses the official title and the correct two-digit tags, 
                    // then uses %(title)s, which we hope contains just the episode title.
                    $"{titleForFolder} - s{seasonYtDlp}e{episodeYtDlp} - %(episode)s.%(ext)s"); // <--- Changed to %(episode)s

                var arguments = new StringBuilder();

                // 1. Core arguments
                arguments.Append($"--windows-filenames ");
                arguments.Append($"--embed-metadata ");
                arguments.Append($"--ignore-errors ");
                // arguments.Append($"--no-paged-list "); // <--- V1.7.5 FIX: Use correct argument to force all items!

                // 2. Subtitle arguments
                arguments.Append($"--all-subs ");
                arguments.Append($"--sub-langs all ");
                arguments.Append($"--sub-format srt ");

                // 3. Downloader and Browser arguments
                arguments.Append($"--downloader aria2c ");
                arguments.Append($"--downloader-args \"aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M\" ");
                arguments.Append($"--user-agent \"{ToolManagerService.FIREFOX_USER_AGENT}\" ");
                arguments.Append($"--cookies-from-browser firefox ");

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
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        // Set encoding for output streams
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    },
                    EnableRaisingEvents = true
                };

                // --- 4. Set Up Event Handlers ---
                _process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        OnOutputReceived?.Invoke(args.Data);
                    }
                };
                _process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        OnOutputReceived?.Invoke($"[ERR] {args.Data}");
                    }
                };

                _process.Exited += (sender, args) =>
                {
                    // Ensure all output is processed before completing
                    _process?.WaitForExit();
                    OnOutputReceived?.Invoke($"--- Download finished. Process exited with code {_process?.ExitCode}. ---");
                    OnDownloadComplete?.Invoke(_process?.ExitCode ?? -1);

                    // --- NEW CLEANUP LOGIC FOR BUGFIX v1.6 ---
                    // Clean up and dispose resources safely after the process exits.
                    _process?.Dispose();
                    _process = null;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                };

                // --- 5. Start Process ---
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                // Wait for the process to exit asynchronously, respecting the cancellation token
                await _process.WaitForExitAsync(_cancellationTokenSource.Token);

                // V1.8.1 FIX: CRITICAL: Add manual blocking wait to ensure asynchronous I/O read operations are complete.
                _process.WaitForExit();

            }
            catch (OperationCanceledException)
            {
                // This is thrown when StopDownload() is called gracefully BEFORE Kill()
                OnOutputReceived?.Invoke("--- Download was stopped by the user. ---");
                // The StopDownload() method takes over the final process kill, 
                // and the Exited handler will fire after the kill.
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"--- [FATAL] Download task failed: {ex.Message} ---");
                OnDownloadComplete?.Invoke(-1);
                // The Exited handler will clean up the process.
            }
            // We remove the entire 'finally' block that was causing a race condition and cleanup issues.
            // Cleanup is now consolidated into the Exited event handler.
        }

        /// <summary>
        /// Stops the currently running download process.
        /// </summary>
        public void StopDownload()
        {
            if (_process != null && !_process.HasExited)
            {
                OnOutputReceived?.Invoke("--- Sending stop signal... ---");
                _cancellationTokenSource?.Cancel();

                // --- IMMEDIATELY KILL THE PROCESS AND ALL CHILDREN ---
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