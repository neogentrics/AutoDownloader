using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AutoDownloader.Core
{
    public class YtDlpService
    {
        // Event to send live console output back to the UI
        public event Action<string, bool>? OnOutputReceived;
        // Event to signal completion
        public event Action<bool>? OnDownloadComplete;

        private Process? _process;
        private bool _isStopping = false;

        // --- NEW: Our service now depends on the ToolManagerService ---
        private readonly ToolManagerService _toolManager;

        public YtDlpService()
        {
            // --- NEW: Create an instance of the ToolManagerService ---
            _toolManager = new ToolManagerService();

            // --- NEW: "Bubble up" log messages from the tool manager to our UI ---
            // This ensures "Downloading aria2c..." messages still appear in the log
            _toolManager.OnLogMessage += (message, isError) =>
            {
                OnOutputReceived?.Invoke(message, isError);
            };
        }

        public void StopDownload()
        {
            if (_process != null && !_process.HasExited)
            {
                _isStopping = true;
                // Send "q" to the console, which yt-dlp interprets as "quit"
                try
                {
                    _process.StandardInput.Write("q");
                    _process.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    OnOutputReceived?.Invoke($"Failed to send 'q' command: {ex.Message}", true);
                    // Fallback to force-kill
                    _process.Kill();
                }
            }
        }

        public async Task DownloadVideoAsync(string url, string outputFolder)
        {
            _isStopping = false;

            try
            {
                // --- REFACTORED: All tool-checking logic is now one clean call ---
                // We ask the tool manager to get everything ready and give us the paths.
                var (ytDlpPath, aria2cPath) = await _toolManager.EnsureToolsAvailableAsync();

                // Plex-friendly output template
                string outputTemplate = Path.Combine(outputFolder,
                    "%(series)s",
                    "Season %(season_number)s",
                    "%(series)s - s%(season_number)02de%(episode_number)02d - %(title)s.%(ext)s");

                var arguments = new StringBuilder();
                arguments.Append($"--windows-filenames ");
                arguments.Append($"--all-subs ");
                arguments.Append($"--sub-format srt ");
                arguments.Append($"--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0 Safari/537.36\" ");

                // --- REFACTORED: Use the paths returned by our ToolManager ---
                arguments.Append($"--downloader \"{aria2cPath}\" ");
                arguments.Append($"--downloader-args \"aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M\" ");

                arguments.Append($"-o \"{outputTemplate}\" ");
                arguments.Append($"\"{url}\"");

                var startInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath, // Use the path from our ToolManager
                    Arguments = arguments.ToString(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true, // Needed to send the 'q' command
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (_process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
                {
                    _process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null) OnOutputReceived?.Invoke(e.Data, false);
                    };
                    _process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null) OnOutputReceived?.Invoke(e.Data, true);
                    };

                    _process.Start();
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();

                    await _process.WaitForExitAsync();

                    OnDownloadComplete?.Invoke(_isStopping ? false : _process.ExitCode == 0);
                }
            }
            catch
            {
                OnDownloadComplete?.Invoke(false);
                throw; // Re-throw the exception to be caught by the UI
            }
            finally
            {
                _process = null;
                _isStopping = false;
            }
        }

        // --- ALL THE 'EnsureToolsAvailableAsync' and 'DownloadFileAsync' METHODS ARE GONE ---
        // --- They now live in ToolManagerService.cs ---
    }
}

