using AutoDownloader.Core; // <-- CORRECT: For DownloadMetadata
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

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

            Process? processRef = null;

            try
            {
                // ---0. Basic validation ---
                if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath))
                {
                    OnOutputReceived?.Invoke($"[FATAL] yt-dlp executable not found at '{_ytDlpPath}'.");
                    OnDownloadComplete?.Invoke(-1);
                    return;
                }

                // Ensure base output folder exists (yt-dlp will create deeper folders, but be safe)
                try { Directory.CreateDirectory(outputFolder); } catch { }

                // ---1. Get Tool Paths ---
                OnOutputReceived?.Invoke("Tools are ready.");
                string ytDlpPath = _ytDlpPath;
                string ariaPath = _ariaPath;

                // ---2. Build yt-dlp Arguments ---

                // Use the official title from metadata, but if it's null,
                // fall back to a dynamic yt-dlp variable to find the best title.
                string titleForFolder = metadata.OfficialTitle ?? "%(series, show, playlist_title, title, 'NA')s";

                // Use pure yt-dlp variables for season/episode padding
                string seasonYtDlp = $"%(season_number|season|{metadata.NextSeasonNumber})02d";
                string episodeYtDlp = "%(episode_number|episode|01)02d";

                // This is the final output template that defines the Plex-friendly file structure.
                string outputTemplate = Path.Combine(outputFolder,
                    $"Season {seasonYtDlp}", // -> .../Season01/
                    $"{titleForFolder} - s{seasonYtDlp}e{episodeYtDlp} - %(episode)s.%(ext)s"); // -> Show - s01e01 - Episode Name.mp4

                // Build argument list using ArgumentList to avoid shell quoting issues
                var startInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                // Core flags
                startInfo.ArgumentList.Add("--windows-filenames");
                startInfo.ArgumentList.Add("--embed-metadata");
                startInfo.ArgumentList.Add("--ignore-errors");

                // Format
                if (!string.IsNullOrWhiteSpace(_videoQualityFormat))
                {
                    startInfo.ArgumentList.Add("-f");
                    startInfo.ArgumentList.Add(_videoQualityFormat);
                }

                // Subtitles
                startInfo.ArgumentList.Add("--all-subs");
                startInfo.ArgumentList.Add("--sub-langs");
                startInfo.ArgumentList.Add("all");
                startInfo.ArgumentList.Add("--sub-format");
                startInfo.ArgumentList.Add("srt");

                // Downloader args (single value)
                startInfo.ArgumentList.Add("--downloader");
                startInfo.ArgumentList.Add("aria2c");
                startInfo.ArgumentList.Add("--downloader-args");
                startInfo.ArgumentList.Add($"aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M");

                // User agent and cookies
                startInfo.ArgumentList.Add("--user-agent");
                startInfo.ArgumentList.Add(_userAgent);
                startInfo.ArgumentList.Add("--cookies-from-browser");
                startInfo.ArgumentList.Add("firefox");

                // Output template
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add(outputTemplate);

                // URL (final argument)
                startInfo.ArgumentList.Add(metadata.SourceUrl);

                // ---3. Configure Process ---
                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                // ---4. Set Up Asynchronous Event Handlers ---

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

                // Handle the process exiting (log only). Do NOT dispose or null _process here --
                // disposal is centralized below in this method to avoid races.
                _process.Exited += (sender, args) =>
                {
                    try
                    {
                        OnOutputReceived?.Invoke("--- Process exited (handler) ---");
                    }
                    catch { /* swallow logging exceptions */ }
                };

                // ---5. Start Process ---
                try
                {
                    _process.Start();
                }
                catch (Exception ex)
                {
                    OnOutputReceived?.Invoke($"--- Failed to start yt-dlp: {ex.Message} ---");
                    OnDownloadComplete?.Invoke(-1);
                    return;
                }

                // Begin reading the output and error streams asynchronously.
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                // Capture a local stable reference to the process to avoid races
                // with external callers swapping/disposing the field.
                processRef = _process;
                if (processRef == null)
                {
                    // Unexpected: process became null before we could wait. Exit gracefully.
                    return;
                }

                // Wait for the process to exit asynchronously, respecting the cancellation token.
                await processRef.WaitForExitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

                // After WaitForExitAsync completes we centrally handle exit logic and cleanup.
                int exitCode = -1;
                try
                {
                    exitCode = processRef.ExitCode;
                }
                catch { /* ignore */ }

                OnOutputReceived?.Invoke($"--- Download finished. Process exited with code {exitCode}. ---");
                OnDownloadComplete?.Invoke(exitCode);
            }
            catch (OperationCanceledException)
            {
                // This is a "clean" exception, thrown when the user clicks "Stop".
                OnOutputReceived?.Invoke("--- Download was stopped by the user. ---");
                // StopDownload() already attempted to kill the process; we still centralize disposal below.
            }
            catch (Exception ex)
            {
                // This is a "dirty" exception (e.g., file not found, permissions error).
                OnOutputReceived?.Invoke($"--- [FATAL] Download task failed: {ex.Message} ---");
                OnDownloadComplete?.Invoke(-1);
            }
            finally
            {
                // CENTRALIZED CLEANUP: only this method disposes and nulls _process and the token source.
                try
                {
                    if (processRef != null)
                    {
                        try
                        {
                            // If it hasn't exited yet for any reason, ensure it's terminated.
                            if (!processRef.HasExited)
                            {
                                try { processRef.Kill(true); } catch { /* ignore kill failures */ }
                                // Wait a short time for termination to avoid racing Dispose with Exited handler.
                                try { processRef.WaitForExit(2000); } catch { }
                            }
                        }
                        catch { /* ignore */ }

                        try { processRef.Dispose(); } catch { /* ignore */ }
                    }
                }
                finally
                {
                    // Clear shared field and dispose token source.
                    _process = null;
                    try { _cancellationTokenSource?.Dispose(); } catch { }
                    _cancellationTokenSource = null;
                }
            }
        }

        /// <summary>
        /// Stops the currently running download process.
        /// This is called by the "Stop" button in MainWindow.
        /// </summary>
        public void StopDownload()
        {
            // Snapshot reference to avoid races with central cleanup.
            var proc = _process;
            if (proc != null && !proc.HasExited)
            {
                OnOutputReceived?.Invoke("--- Sending stop signal... ---");

                //1. Cancel the async wait task
                _cancellationTokenSource?.Cancel();

                //2. Force-kill the process and all its children
                try
                {
                    // The 'true' argument ensures all child processes (like aria2c) are also terminated.
                    proc.Kill(true);
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