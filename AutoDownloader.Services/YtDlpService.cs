using AutoDownloader.Core; // <-- CORRECT: For DownloadMetadata
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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

        /// <summary>
        /// Fires whenever the downloader reports progress for the file being transferred.
        /// Readings come from aria2c or from yt-dlp depending on which is doing the work.
        /// </summary>
        public event Action<DownloadProgress>? OnProgress;

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
        /// Full path to ffmpeg.exe, or empty when unavailable. Passed to yt-dlp via
        /// --ffmpeg-location so it does not have to rely on PATH.
        /// </summary>
        private readonly string _ffmpegPath;

        /// <summary>
        /// Browser name or cookies.txt path for --cookies-from-browser / --cookies.
        /// Empty means send no cookies, which is the only safe default: yt-dlp treats an
        /// unreadable cookie source as fatal and aborts the whole download.
        /// </summary>
        private readonly string _cookieSource;

        /// <summary>
        /// When true, yt-dlp keeps a per-series archive file and skips episodes already
        /// recorded in it.
        /// </summary>
        private readonly bool _useDownloadArchive;

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
        /// <param name="ffmpegPath">Path to ffmpeg.exe, or empty if it could not be found</param>
        /// <param name="cookieSource">Browser name or cookies.txt path; empty for no cookies</param>
        /// <param name="useDownloadArchive">Skip episodes already recorded in the series archive</param>
        public YtDlpService(
            string ytDlpPath,
            string ariaPath,
            string userAgent,
            string videoQualityFormat,
            string ffmpegPath = "",
            string cookieSource = "",
            bool useDownloadArchive = true)
        {
            _ytDlpPath = ytDlpPath;
            _ariaPath = ariaPath;
            _userAgent = userAgent;
            _videoQualityFormat = videoQualityFormat;
            _ffmpegPath = ffmpegPath ?? string.Empty;
            _cookieSource = cookieSource ?? string.Empty;
            _useDownloadArchive = useDownloadArchive;
        }

        // --- Public Methods ---

        /// <summary>
        /// Asynchronously launches the yt-dlp process to download a given URL.
        /// </summary>
        /// <param name="metadata">The metadata object containing the URL(s) and official title.</param>
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

                // The season number is OURS, not the site's. Two reasons:
                //   * many sites expose no season metadata at all, and yt-dlp then emits the
                //     literal text of the field chain into the path (e.g. "Season season2");
                //   * when a site does expose one it often disagrees with TMDB/TVDB.
                // Writing it as a literal also guarantees this folder matches the one the
                // verification step later counts files in.
                string seasonFolder = $"Season {metadata.NextSeasonNumber:00}";
                string seasonToken = metadata.NextSeasonNumber.ToString("00");

                // Prefer the official title we already resolved. Only fall back to yt-dlp
                // fields when we have none.
                // NOTE: alternates are separated by commas with NO SPACES. A space makes
                // yt-dlp treat the rest of the chain as part of a field name, so it silently
                // skips straight to the default - which is how every file ended up named "NA".
                string titleToken = !string.IsNullOrWhiteSpace(metadata.OfficialTitle)
                    ? EscapeTemplateLiteral(metadata.OfficialTitle!)
                    : "%(series,show,playlist_title,title)s";

                // Episode number must come from the site; playlist_index is the fallback for
                // sites that expose ordering but not episode numbers. If neither exists the
                // episode title still differentiates the files, so they cannot collide.
                const string episodeToken = "%(episode_number,playlist_index)02d";
                const string episodeTitleToken = "%(episode,title)s";

                string outputTemplate = Path.Combine(outputFolder,
                    seasonFolder,
                    $"{titleToken} - S{seasonToken}E{episodeToken} - {episodeTitleToken}.%(ext)s");

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
                // Keep going when one episode in a season fails rather than aborting the batch.
                startInfo.ArgumentList.Add("--ignore-errors");
                AddProgressArguments(startInfo);
                // Never silently clobber an existing episode.
                startInfo.ArgumentList.Add("--no-overwrites");

                // Let yt-dlp use our provisioned ffmpeg rather than depending on PATH.
                if (!string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath))
                {
                    startInfo.ArgumentList.Add("--ffmpeg-location");
                    startInfo.ArgumentList.Add(_ffmpegPath);
                }
                else
                {
                    OnOutputReceived?.Invoke("[WARN] ffmpeg was not found. Formats that need merging will fail.");
                }

                // Per-series archive: makes a repeat run skip what is already downloaded.
                if (_useDownloadArchive)
                {
                    startInfo.ArgumentList.Add("--download-archive");
                    startInfo.ArgumentList.Add(Path.Combine(outputFolder, "downloaded.txt"));
                }

                // Format
                if (!string.IsNullOrWhiteSpace(_videoQualityFormat))
                {
                    startInfo.ArgumentList.Add("-f");
                    startInfo.ArgumentList.Add(_videoQualityFormat);
                }

                // Subtitles. --sub-format only expresses a PREFERENCE; sites that serve VTT
                // would yield nothing. --convert-subs actually performs the conversion
                // (it needs ffmpeg, which is why --ffmpeg-location is set above).
                startInfo.ArgumentList.Add("--write-subs");
                startInfo.ArgumentList.Add("--sub-langs");
                startInfo.ArgumentList.Add("all");
                startInfo.ArgumentList.Add("--convert-subs");
                startInfo.ArgumentList.Add("srt");

                // Downloader args (single value)
                startInfo.ArgumentList.Add("--downloader");
                startInfo.ArgumentList.Add("aria2c");
                startInfo.ArgumentList.Add("--downloader-args");
                startInfo.ArgumentList.Add($"aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M");

                // User agent
                startInfo.ArgumentList.Add("--user-agent");
                startInfo.ArgumentList.Add(_userAgent);

                // Cookies are opt-in. yt-dlp treats an unreadable cookie source as FATAL, so
                // hardcoding a browser here broke every machine without that browser installed.
                AddCookieArguments(startInfo);

                // Output template
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add(outputTemplate);

                // URLs (final arguments). A season page can resolve to many playable links;
                // passing only the first one downloaded exactly one episode.
                var urls = metadata.SourceUrls != null && metadata.SourceUrls.Count > 0
                    ? metadata.SourceUrls
                    : new List<string> { metadata.SourceUrl };

                foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    startInfo.ArgumentList.Add(url);
                }

                OnOutputReceived?.Invoke($"--- Passing {urls.Count} URL(s) to yt-dlp. ---");

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
                    if (args.Data == null) return;
                    if (TryReportProgress(args.Data)) return;
                    OnOutputReceived?.Invoke(args.Data);
                };

                // Handle error output
                _process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data == null) return;
                    // aria2c writes its status lines to stderr.
                    if (TryReportProgress(args.Data)) return;
                    OnOutputReceived?.Invoke($"[ERR] {args.Data}");
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
        /// Asks yt-dlp what it thinks this URL contains, without downloading anything.
        ///
        /// This is the first thing the orchestrator should try, because yt-dlp already has
        /// site-specific extractors for well over a thousand sites plus a generic one, and it
        /// expands their playlists natively. Only when this comes back with nothing (or a
        /// single item for a page that is clearly a season listing) is it worth indexing the
        /// page ourselves.
        /// </summary>
        /// <returns>The entry URLs yt-dlp found, in order. Empty when it understood nothing.</returns>
        public async Task<List<string>> ProbeEntriesAsync(string url)
        {
            var entries = new List<string>();

            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath)) return entries;

            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // --flat-playlist stops it recursing into every entry, which keeps this fast.
            startInfo.ArgumentList.Add("--flat-playlist");
            startInfo.ArgumentList.Add("--dump-single-json");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("--ignore-errors");
            startInfo.ArgumentList.Add("--user-agent");
            startInfo.ArgumentList.Add(_userAgent);
            AddCookieArguments(startInfo);
            startInfo.ArgumentList.Add(url);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string json = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    OnOutputReceived?.Invoke($"[probe] {stderr.Trim()}");
                }

                if (string.IsNullOrWhiteSpace(json)) return entries;

                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                // yt-dlp prints a bare "null" (or nothing useful) when extraction fails, and
                // TryGetProperty throws on a non-object root rather than returning false. That
                // turned a plain extraction failure into a confusing type error in the log.
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return entries;
                }

                if (root.TryGetProperty("entries", out var entriesElement)
                    && entriesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entriesElement.EnumerateArray())
                    {
                        if (entry.ValueKind != JsonValueKind.Object) continue;

                        string? entryUrl = null;
                        if (entry.TryGetProperty("url", out var u)) entryUrl = u.GetString();
                        if (string.IsNullOrWhiteSpace(entryUrl)
                            && entry.TryGetProperty("webpage_url", out var w)) entryUrl = w.GetString();

                        if (!string.IsNullOrWhiteSpace(entryUrl)) entries.Add(entryUrl!);
                    }
                }
                else if (root.TryGetProperty("webpage_url", out var single))
                {
                    // A single playable video rather than a playlist.
                    var only = single.GetString();
                    if (!string.IsNullOrWhiteSpace(only)) entries.Add(only!);
                }
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"[probe] Could not inspect {url}: {ex.Message}");
            }

            return entries;
        }

        /// <summary>
        /// Downloads exactly one episode to a filename we choose outright.
        ///
        /// This is the path that makes correct naming possible on sites that expose no
        /// metadata at all: the show title, season and episode number come from TMDB/TVDB and
        /// the page ordering, never from the site, so nothing is left to guess.
        /// </summary>
        public async Task<int> DownloadEpisodeAsync(
            string url,
            string showTitle,
            int seasonNumber,
            int episodeNumber,
            string? episodeTitle,
            string outputFolder,
            string? referer = null)
        {
            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath))
            {
                OnOutputReceived?.Invoke($"[FATAL] yt-dlp executable not found at '{_ytDlpPath}'.");
                return -1;
            }

            string seasonFolder = $"Season {seasonNumber:00}";
            string safeShow = EscapeTemplateLiteral(showTitle);

            // Every component is a literal except the extension, so the filename is fully
            // determined before yt-dlp runs.
            string stem = $"{safeShow} - S{seasonNumber:00}E{episodeNumber:00}";
            if (!string.IsNullOrWhiteSpace(episodeTitle))
            {
                stem += $" - {EscapeTemplateLiteral(episodeTitle!)}";
            }

            string outputTemplate = Path.Combine(outputFolder, seasonFolder, stem + ".%(ext)s");

            try { Directory.CreateDirectory(Path.Combine(outputFolder, seasonFolder)); } catch { }

            var startInfo = BuildCommonStartInfo(outputFolder);

            // A stream URL captured from a player is usually only served to requests carrying
            // the originating page as referer.
            if (!string.IsNullOrWhiteSpace(referer))
            {
                startInfo.ArgumentList.Add("--referer");
                startInfo.ArgumentList.Add(referer!);
            }

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputTemplate);
            startInfo.ArgumentList.Add(url);

            OnOutputReceived?.Invoke($"--- S{seasonNumber:00}E{episodeNumber:00}: {url} ---");

            _cancellationTokenSource ??= new CancellationTokenSource();

            Process? process = null;
            try
            {
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null) return;
                    if (TryReportProgress(args.Data)) return;
                    OnOutputReceived?.Invoke(args.Data);
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null) return;
                    // aria2c writes its status lines to stderr.
                    if (TryReportProgress(args.Data)) return;
                    OnOutputReceived?.Invoke($"[ERR] {args.Data}");
                };

                process.Start();
                _process = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                OnOutputReceived?.Invoke("--- Stopped by the user. ---");
                return -1;
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"--- [ERROR] Episode download failed: {ex.Message} ---");
                return -1;
            }
            finally
            {
                if (process != null)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    try { process.Dispose(); } catch { }
                }
                _process = null;
            }
        }

        /// <summary>
        /// The flags shared by every yt-dlp invocation that actually downloads something.
        /// </summary>
        private ProcessStartInfo BuildCommonStartInfo(string outputFolder)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("--windows-filenames");
            startInfo.ArgumentList.Add("--embed-metadata");
            startInfo.ArgumentList.Add("--ignore-errors");
            startInfo.ArgumentList.Add("--no-overwrites");
            AddProgressArguments(startInfo);

            if (!string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath))
            {
                startInfo.ArgumentList.Add("--ffmpeg-location");
                startInfo.ArgumentList.Add(_ffmpegPath);
            }

            if (_useDownloadArchive)
            {
                startInfo.ArgumentList.Add("--download-archive");
                startInfo.ArgumentList.Add(Path.Combine(outputFolder, "downloaded.txt"));
            }

            if (!string.IsNullOrWhiteSpace(_videoQualityFormat))
            {
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add(_videoQualityFormat);
            }

            startInfo.ArgumentList.Add("--write-subs");
            startInfo.ArgumentList.Add("--sub-langs");
            startInfo.ArgumentList.Add("all");
            startInfo.ArgumentList.Add("--convert-subs");
            startInfo.ArgumentList.Add("srt");

            startInfo.ArgumentList.Add("--downloader");
            startInfo.ArgumentList.Add("aria2c");
            startInfo.ArgumentList.Add("--downloader-args");
            startInfo.ArgumentList.Add("aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M");

            startInfo.ArgumentList.Add("--user-agent");
            startInfo.ArgumentList.Add(_userAgent);

            AddCookieArguments(startInfo);

            return startInfo;
        }

        /// <summary>
        /// Asks for machine-readable progress on its own line.
        ///
        /// --newline matters: without it yt-dlp rewrites one line with carriage returns, which
        /// never completes a line for the redirected reader, so nothing arrives until the end.
        /// </summary>
        private static void AddProgressArguments(ProcessStartInfo startInfo)
        {
            startInfo.ArgumentList.Add("--newline");
            startInfo.ArgumentList.Add("--progress-template");
            startInfo.ArgumentList.Add(ProgressParser.YtDlpProgressTemplate);
        }

        /// <summary>
        /// Raises OnProgress when a line is a progress reading. Returns true when the line was
        /// progress and should not also be logged verbatim, since these arrive once a second
        /// and would otherwise drown the log.
        /// </summary>
        private bool TryReportProgress(string line)
        {
            var progress = ProgressParser.Parse(line);
            if (progress == null) return false;

            OnProgress?.Invoke(progress);
            return true;
        }

        /// <summary>
        /// Adds cookie flags only when a source is configured. yt-dlp treats an unreadable
        /// cookie source as fatal, so this must stay opt-in.
        /// </summary>
        private void AddCookieArguments(ProcessStartInfo startInfo)
        {
            if (CookieSourceSpec.IsNone(_cookieSource)) return;

            if (CookieSourceSpec.IsFilePath(_cookieSource))
            {
                // Classified by shape, not existence. A mistyped path used to fall through to
                // --cookies-from-browser, where yt-dlp reported an unknown browser instead of
                // a missing file.
                if (!File.Exists(_cookieSource))
                {
                    OnOutputReceived?.Invoke($"[WARN] Cookies file not found: {_cookieSource}");
                }

                startInfo.ArgumentList.Add("--cookies");
                startInfo.ArgumentList.Add(_cookieSource);
            }
            else
            {
                startInfo.ArgumentList.Add("--cookies-from-browser");
                startInfo.ArgumentList.Add(_cookieSource);
            }
        }

        /// <summary>
        /// Escapes a literal string for safe use inside a yt-dlp output template.
        /// A '%' in a show title would otherwise start a field expression and corrupt the path.
        /// Illegal filename characters are also replaced, since the title becomes a filename.
        /// </summary>
        private static string EscapeTemplateLiteral(string value)
        {
            var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray());

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                cleaned = cleaned.Replace(c, '_');
            }

            // Doubling a percent sign is how yt-dlp escapes a literal '%'.
            cleaned = cleaned.Replace("%", "%%");

            cleaned = cleaned.Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Unknown Show" : cleaned;
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