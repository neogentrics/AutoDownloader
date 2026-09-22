using AutoDownloader.Core; // <-- CORRECT: For DownloadMetadata and EpisodeLink
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

        /// <summary>The yt-dlp executable this service drives.</summary>
        public string YtDlpPath => _ytDlpPath;

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
        /// A format selector chosen for this run, which outranks the saved preference.
        /// Null leaves Preferences in charge.
        /// </summary>
        public string? QualityOverride { get; set; }

        /// <summary>
        /// When true, yt-dlp keeps a per-series archive file and skips episodes already
        /// recorded in it.
        /// </summary>
        private readonly bool _useDownloadArchive;

        /// <summary>"compatible", "best" or "custom" - see VideoFormatPreference.</summary>
        private readonly string _formatPreference;

        /// <summary>
        /// Set for one invocation when the external downloader has failed, so the retry uses
        /// yt-dlp's own. YouTube's CDN rejects aria2c's ranged requests for some URLs with a
        /// 403 that does not recur, which cost an episode in testing.
        /// </summary>
        private bool _skipExternalDownloader;

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
            bool useDownloadArchive = true,
            string formatPreference = "compatible")
        {
            _formatPreference = formatPreference ?? "compatible";
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
                string formatSelector = string.IsNullOrWhiteSpace(QualityOverride)
                    ? VideoFormatPreference.Resolve(_formatPreference, _videoQualityFormat)
                    : QualityOverride!;

                if (!string.IsNullOrWhiteSpace(formatSelector))
                {
                    startInfo.ArgumentList.Add("-f");
                    startInfo.ArgumentList.Add(formatSelector);
                }

                if (VideoFormatPreference.PrefersMp4(_formatPreference))
                {
                    // Otherwise yt-dlp merges into MKV whenever the two streams disagree.
                    startInfo.ArgumentList.Add("--merge-output-format");
                    startInfo.ArgumentList.Add("mp4");
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
        /// <summary>
        /// The quality ladder a source publishes for one video.
        ///
        /// Probed from a single episode and applied to the whole show: a packager mints the
        /// same ladder for every episode of a series, so asking once is enough and asking
        /// per episode would add a round trip to each one.
        /// </summary>
        public async Task<List<FormatOption>> ProbeFormatsAsync(
            string url, string? referer = null, CancellationToken cancellationToken = default)
        {
            var options = new List<FormatOption>();

            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath)) return options;

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

            startInfo.ArgumentList.Add("--simulate");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("-J");
            startInfo.ArgumentList.Add("--user-agent");
            startInfo.ArgumentList.Add(_userAgent);

            if (!string.IsNullOrWhiteSpace(referer))
            {
                startInfo.ArgumentList.Add("--referer");
                startInfo.ArgumentList.Add(referer!);
            }

            AddCookieArguments(startInfo);
            startInfo.ArgumentList.Add(url);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string json = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                _ = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(json)) return options;

                using var document = System.Text.Json.JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("formats", out var formats)
                    || formats.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return options;
                }

                // A fragmented stream reports no file size, because nothing has counted the
                // fragments yet. yt-dlp's own listing derives the "~1.27GiB" it shows from
                // bitrate and duration, and so does this - otherwise every rung reads "size
                // unknown", which is the one thing the chooser exists to tell you.
                double durationSeconds = Number(document.RootElement, "duration");

                foreach (var format in formats.EnumerateArray())
                {
                    string id = Text(format, "format_id");
                    if (id.Length == 0 || id.StartsWith("images", StringComparison.OrdinalIgnoreCase)) continue;

                    options.Add(new FormatOption
                    {
                        FormatId = id,
                        Width = Int(format, "width"),
                        Height = Int(format, "height"),
                        Bitrate = Number(format, "tbr"),
                        EstimatedBytes = EstimateBytes(format, durationSeconds),
                        VideoCodec = Text(format, "vcodec"),
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A ladder that cannot be read simply is not offered as a choice.
                OnOutputReceived?.Invoke($"[formats] Could not read the quality list: {ex.Message}");
            }

            return options;
        }

        /// <summary>
        /// The size a rung would come to: whatever the source states, or bitrate times
        /// duration when it states nothing. Zero when neither is available, which the
        /// display renders as unknown rather than as a confident wrong number.
        /// </summary>
        private static long EstimateBytes(System.Text.Json.JsonElement format, double durationSeconds)
        {
            if (Long(format, "filesize") is long exact && exact > 0) return exact;
            if (Long(format, "filesize_approx") is long approx && approx > 0) return approx;

            double bitrate = Number(format, "tbr");
            if (bitrate <= 0 || durationSeconds <= 0) return 0;

            // tbr is kilobits per second; bytes are what the file manager will show.
            return (long)(bitrate * 1000 * durationSeconds / 8);
        }

        private static string Text(System.Text.Json.JsonElement element, string name) =>
            element.TryGetProperty(name, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static int Int(System.Text.Json.JsonElement element, string name) =>
            element.TryGetProperty(name, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.Number
            && value.TryGetInt32(out int parsed)
                ? parsed
                : 0;

        private static double Number(System.Text.Json.JsonElement element, string name) =>
            element.TryGetProperty(name, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.Number
                ? value.GetDouble()
                : 0;

        private static long? Long(System.Text.Json.JsonElement element, string name) =>
            element.TryGetProperty(name, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.Number
            && value.TryGetInt64(out long parsed)
                ? parsed
                : null;

        /// <summary>
        /// How long a stream runs, in seconds, or null when it cannot be read.
        ///
        /// Exists to tell an advert from an episode. A player loads its pre-roll first, so
        /// the first manifest the capture sees is routinely a thirty-second car commercial -
        /// which downloads perfectly and is entirely the wrong video. Duration is the honest
        /// way to tell them apart, and it does not depend on knowing anything about the site.
        /// </summary>
        public async Task<double?> ProbeDurationAsync(string url, string? referer = null)
        {
            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath)) return null;

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

            startInfo.ArgumentList.Add("--simulate");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("--print");
            startInfo.ArgumentList.Add("%(duration)s");
            startInfo.ArgumentList.Add("--user-agent");
            startInfo.ArgumentList.Add(_userAgent);

            if (!string.IsNullOrWhiteSpace(referer))
            {
                startInfo.ArgumentList.Add("--referer");
                startInfo.ArgumentList.Add(referer!);
            }

            AddCookieArguments(startInfo);
            startInfo.ArgumentList.Add(url);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                _ = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);

                foreach (var line in output.Split((char)10))
                {
                    if (double.TryParse(line.Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double seconds)
                        && seconds > 0)
                    {
                        return seconds;
                    }
                }
            }
            catch
            {
                // A duration that cannot be read simply does not participate in the choice.
            }

            return null;
        }

        public async Task<List<EpisodeLink>> ProbeEntriesAsync(string url)
        {
            var entries = new List<EpisodeLink>();

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

                        if (string.IsNullOrWhiteSpace(entryUrl)) continue;

                        var link = new EpisodeLink { Url = entryUrl!, Ordinal = entries.Count };

                        // Playlist entries often carry their own title, and sometimes explicit
                        // season/episode numbers. Both beat guessing from position.
                        if (entry.TryGetProperty("title", out var t)) link.LinkText = t.GetString();

                        if (entry.TryGetProperty("season_number", out var sn)
                            && sn.ValueKind == JsonValueKind.Number)
                        {
                            link.DetectedSeasonNumber = sn.GetInt32();
                        }

                        if (entry.TryGetProperty("episode_number", out var en)
                            && en.ValueKind == JsonValueKind.Number)
                        {
                            link.DetectedEpisodeNumber = en.GetInt32();
                        }

                        // Nothing explicit - fall back to reading the title and the URL.
                        if (!link.DetectedEpisodeNumber.HasValue)
                        {
                            var (season, episode) = Scrapers.SeriesIndexer
                                .DetectSeasonAndEpisode(link.LinkText, link.Url);

                            link.DetectedSeasonNumber ??= season;
                            link.DetectedEpisodeNumber = episode;
                        }

                        entries.Add(link);
                    }
                }
                else if (root.TryGetProperty("webpage_url", out var single))
                {
                    // A single playable video rather than a playlist.
                    var only = single.GetString();
                    if (!string.IsNullOrWhiteSpace(only))
                    {
                        entries.Add(new EpisodeLink { Url = only!, Ordinal = 0 });
                    }
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
        /// <summary>
        /// Returns the episode's existing file, if one is already on disk.
        ///
        /// Matched on the show/season/episode part of the name rather than the whole thing, so
        /// a file written under an older episode title is recognised as the same episode
        /// instead of being downloaded again alongside it.
        /// </summary>
        public static ExistingEpisode? FindExistingEpisode(
            string showTitle, int seasonNumber, int episodeNumber, string? episodeTitle, string outputFolder)
        {
            try
            {
                string seasonFolder = Path.Combine(outputFolder, $"Season {seasonNumber:00}");
                if (!Directory.Exists(seasonFolder)) return null;

                string prefix = $"{EscapeTemplateLiteral(showTitle)} - S{seasonNumber:00}E{episodeNumber:00}";

                string[] videoExtensions = { ".mp4", ".mkv", ".webm", ".avi", ".m4v", ".mov" };

                var match = Directory
                    .EnumerateFiles(seasonFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .FirstOrDefault(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                if (match == null) return null;

                var info = new FileInfo(match);

                return new ExistingEpisode
                {
                    Path = match,
                    SizeBytes = info.Length,
                    LastModified = info.LastWriteTime,
                    EpisodeLabel = $"S{seasonNumber:00}E{episodeNumber:00}",
                    EpisodeTitle = episodeTitle
                };
            }
            catch
            {
                // Never let a permissions problem here stop a download being attempted.
                return null;
            }
        }

        /// <param name="forceOverwrite">
        /// Replace an existing file. This also bypasses the download archive, since an episode
        /// already recorded there would be skipped before the file was even examined.
        /// </param>
        public async Task<EpisodeDownloadOutcome> DownloadEpisodeAsync(
            string url,
            string showTitle,
            int seasonNumber,
            int episodeNumber,
            string? episodeTitle,
            string outputFolder,
            string? referer = null,
            bool forceOverwrite = false,
            bool bypassArchive = false)
        {
            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath))
            {
                OnOutputReceived?.Invoke($"[FATAL] yt-dlp executable not found at '{_ytDlpPath}'.");
                return EpisodeDownloadOutcome.Failed();
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

            var startInfo = BuildCommonStartInfo(outputFolder, forceOverwrite, bypassArchive);

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

            // A fresh source for every download. This used to be a null-coalescing
            // assignment, which meant StopDownload cancelled the source and then every later
            // download in the session was handed that same cancelled source back - so one
            // press of Stop permanently broke downloading until the app was restarted, with
            // each episode failing instantly and blaming the user for it.
            try { _cancellationTokenSource?.Dispose(); } catch { }
            _cancellationTokenSource = new CancellationTokenSource();

            // Set when the downloader reports protected content, so the caller can skip the
            // media-capture fallback that cannot possibly succeed against an encrypted stream.
            bool drmProtected = false;

            // aria2c is fast, but YouTube's CDN rejects its ranged requests for some URLs.
            // yt-dlp's own downloader handles those, so it is worth one retry rather than
            // losing the episode.
            bool downloaderRejected = false;

            Process? process = null;
            try
            {
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null) return;
                    if (TryReportProgress(args.Data)) return;
                    if (DrmDetector.IsDrmMessage(args.Data)) drmProtected = true;
                    if (IsExternalDownloaderFailure(args.Data)) downloaderRejected = true;
                    OnOutputReceived?.Invoke(args.Data);
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null) return;
                    // aria2c writes its status lines to stderr.
                    if (TryReportProgress(args.Data)) return;
                    if (DrmDetector.IsDrmMessage(args.Data)) drmProtected = true;
                    if (IsExternalDownloaderFailure(args.Data)) downloaderRejected = true;
                    OnOutputReceived?.Invoke($"[ERR] {args.Data}");
                };

                process.Start();
                _process = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

                // One retry without the external downloader, which recovers the transient
                // CDN rejections. Not attempted for DRM, where nothing would change.
                if (process.ExitCode != 0 && downloaderRejected && !drmProtected && !_skipExternalDownloader)
                {
                    OnOutputReceived?.Invoke("[WARN] The external downloader was rejected by the server. "
                                           + "Retrying with yt-dlp's own downloader...");
                    _skipExternalDownloader = true;

                    try
                    {
                        return await DownloadEpisodeAsync(
                            url, showTitle, seasonNumber, episodeNumber, episodeTitle,
                            outputFolder, referer).ConfigureAwait(false);
                    }
                    finally
                    {
                        _skipExternalDownloader = false;
                    }
                }

                return new EpisodeDownloadOutcome
                {
                    ExitCode = process.ExitCode,
                    DrmProtected = drmProtected
                };
            }
            catch (OperationCanceledException)
            {
                OnOutputReceived?.Invoke("--- Stopped by the user. ---");
                return EpisodeDownloadOutcome.Stopped();
            }
            catch (Exception ex)
            {
                OnOutputReceived?.Invoke($"--- [ERROR] Episode download failed: {ex.Message} ---");
                return EpisodeDownloadOutcome.Failed();
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
        /// <param name="bypassArchive">
        /// Skip the download archive without forcing an overwrite. Needed for a captured
        /// stream: the generic extractor calls every DASH manifest "dash", so the archive
        /// records one id for all of them and every later episode looks already downloaded.
        /// Conflating this with forceOverwrite would replace files the user asked to keep.
        /// </param>
        private ProcessStartInfo BuildCommonStartInfo(
            string outputFolder, bool forceOverwrite = false, bool bypassArchive = false)
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
            startInfo.ArgumentList.Add(forceOverwrite ? "--force-overwrites" : "--no-overwrites");
            AddProgressArguments(startInfo);

            if (!string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath))
            {
                startInfo.ArgumentList.Add("--ffmpeg-location");
                startInfo.ArgumentList.Add(_ffmpegPath);
            }

            // Skipped when overwriting deliberately: an episode already recorded in the
            // archive would be passed over before the file was even looked at.
            if (_useDownloadArchive && !forceOverwrite && !bypassArchive)
            {
                startInfo.ArgumentList.Add("--download-archive");
                startInfo.ArgumentList.Add(Path.Combine(outputFolder, "downloaded.txt"));
            }

            string formatSelector = string.IsNullOrWhiteSpace(QualityOverride)
                    ? VideoFormatPreference.Resolve(_formatPreference, _videoQualityFormat)
                    : QualityOverride!;

            if (!string.IsNullOrWhiteSpace(formatSelector))
            {
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add(formatSelector);
            }

            if (VideoFormatPreference.PrefersMp4(_formatPreference))
            {
                // Otherwise yt-dlp merges into MKV whenever the two streams disagree.
                startInfo.ArgumentList.Add("--merge-output-format");
                startInfo.ArgumentList.Add("mp4");
            }

            startInfo.ArgumentList.Add("--write-subs");
            startInfo.ArgumentList.Add("--sub-langs");
            startInfo.ArgumentList.Add("all");
            startInfo.ArgumentList.Add("--convert-subs");
            startInfo.ArgumentList.Add("srt");

            if (!_skipExternalDownloader)
            {
                startInfo.ArgumentList.Add("--downloader");
                startInfo.ArgumentList.Add("aria2c");
                startInfo.ArgumentList.Add("--downloader-args");
                startInfo.ArgumentList.Add("aria2c:--max-connection-per-server=16 --split=16 --min-split-size=1M");
            }

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
        /// True when a line reports the EXTERNAL downloader failing, as distinct from the
        /// download being impossible. Deliberately narrow, so a genuine 404 or a DRM error is
        /// not retried pointlessly.
        /// </summary>
        public static bool IsExternalDownloaderFailure(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (line.Contains("aria2c exited with code", StringComparison.OrdinalIgnoreCase)) return true;

            // aria2's own wording when the server refuses a ranged request.
            return line.Contains("errorCode=22", StringComparison.OrdinalIgnoreCase)
                && line.Contains("status=403", StringComparison.OrdinalIgnoreCase);
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