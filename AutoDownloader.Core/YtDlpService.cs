using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Core
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
        private readonly ToolManagerService _toolManagerService;
        private Process? _process;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Constructor now "injects" the ToolManagerService.
        /// </summary>
        public YtDlpService(ToolManagerService toolManager)
        {
            _toolManagerService = toolManager;

            // Forward logs from the tool manager to our own log event
            _toolManagerService.OnToolLogReceived += (logLine) =>
            {
                OnOutputReceived?.Invoke(logLine);
            };
        }

        /// <summary>
        /// Asynchronously downloads a video from a given URL.
        /// </summary>
        public async Task DownloadVideoAsync(string url, string outputFolder)
        {
            // Reset the cancellation token for this new download
            _cancellationTokenSource = new CancellationTokenSource();

            OnOutputReceived?.Invoke("--- Download process started. ---");

            try
            {
                // --- 1. Get Tool Paths ---
                // Ask the ToolManager to ensure tools are ready and get their paths
                OnOutputReceived?.Invoke("Checking for required tools...");
                var (ytDlpPath, ariaPath) = await _toolManagerService.EnsureToolsAvailableAsync();
                OnOutputReceived?.Invoke("Tools are ready.");

                // --- 2. Build Arguments ---
                // Use a more robust fallback system for naming
                // Use a more robust fallback system for naming
                string seriesFallback = "%(series, show, playlist_title, title, 'NA')s"; // ADDED 'title' as a final fallback
                string seasonFallback = "%(season_number, season, '1')s"; // Default to Season 1 if no season
                string episodeFallback = "%(episode_number, episode, '01')s"; // Default to 01 if no episode (changed '0' to '01' for better sorting)

                // The new output template is more resilient
                string outputTemplate = System.IO.Path.Combine(outputFolder,
                    $"{seriesFallback}",
                    $"Season {seasonFallback}",
                    $"{seriesFallback} - s{seasonFallback:02}e{episodeFallback:02} - %(title)s.%(ext)s");

                var arguments = new StringBuilder();
                arguments.Append($"--windows-filenames "); // Sanitize filenames
                arguments.Append($"--embed-metadata "); // <--- NEW: Forces yt-dlp to find and embed show/episode metadata

                arguments.Append($"--all-subs "); // Get all subtitles
                arguments.Append($"--sub-langs all "); // <--- NEW: Explicitly ask for all languages
                arguments.Append($"--sub-format srt "); // Prefer .srt format

                // The key to fixing subtitle naming is adding a specific argument to the template.
                arguments.Append($"-o \"{outputTemplate}\" "); // Set our output path/name template

                // Fix for incomplete downloads: tell yt-dlp to handle common errors gracefully
                arguments.Append($"--ignore-errors "); // <--- NEW: Skip a failing video and continue

                // --- High-Speed Download with aria2c ---
                arguments.Append($"--downloader aria2c ");
                arguments.Append($"--downloader-args \"aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M\" ");

                // --- Disguise our request as a real browser ---
                arguments.Append($"--user-agent \"{ToolManagerService.FIREFOX_USER_AGENT}\" ");

                // --- Pass Cookies (if needed) ---
                // **THE FIX:** Specify only ONE browser, not a list.
                arguments.Append($"--cookies-from-browser firefox ");

                // --- The URL to download ---
                arguments.Append($"\"{url}\""); // Finally, the URL to download

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
                };

                // --- 5. Start Process ---
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                // Wait for the process to exit asynchronously, respecting the cancellation token
                await _process.WaitForExitAsync(_cancellationTokenSource.Token);

                // Check for a *successful* exit (this might be redundant with Exited handler, but safe)
                if (_process != null && _process.ExitCode == 0)
                {
                    // The Exited handler will call OnDownloadComplete
                }
            }
            catch (OperationCanceledException)
            {
                // This is thrown when StopDownload() is called
                OnOutputReceived?.Invoke("--- Download was stopped by the user. ---");
                OnDownloadComplete?.Invoke(-1); // Indicate user cancellation
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"--- [FATAL] Download task failed: {ex.Message} ---");
                OnDownloadComplete?.Invoke(-1);
            }
            finally
            {
                // Clean up the process
                _process?.Dispose();
                _process = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
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