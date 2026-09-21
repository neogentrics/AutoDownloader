using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services
{
    /// <summary>The outcome of converting one file.</summary>
    public class ConversionResult
    {
        public string SourcePath { get; set; } = string.Empty;
        public string? OutputPath { get; set; }
        public bool Skipped { get; set; }
        public bool Succeeded { get; set; }
        public string? Message { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Converts downloaded files into a container that plays everywhere, doing the least work
    /// that achieves it.
    ///
    /// Most conversions turn out to be remuxes: the codecs are already fine and only the
    /// container is wrong, which ffmpeg does by copying streams byte for byte in seconds. A
    /// re-encode is reserved for codecs that genuinely will not play, because it is slow and
    /// it degrades the picture.
    /// </summary>
    public class MediaConverter
    {
        private readonly string _ffmpegPath;
        private readonly string _ffprobePath;

        /// <summary>Progress and diagnostics.</summary>
        public event Action<string>? OnLog;

        /// <summary>Extensions worth examining. Deliberately excludes yt-dlp's intermediates.</summary>
        private static readonly string[] VideoExtensions =
            { ".webm", ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".flv", ".ts", ".wmv" };

        public MediaConverter(string ffmpegPath)
        {
            _ffmpegPath = ffmpegPath;

            // ffprobe sits beside ffmpeg in every distribution of it.
            string? dir = Path.GetDirectoryName(ffmpegPath);
            _ffprobePath = string.IsNullOrEmpty(dir)
                ? "ffprobe.exe"
                : Path.Combine(dir, "ffprobe.exe");
        }

        public bool IsUsable => File.Exists(_ffmpegPath);

        /// <summary>
        /// Reads the video and audio codecs from a file.
        /// </summary>
        public async Task<(string? Video, string? Audio)> ProbeAsync(
            string path, CancellationToken cancellationToken = default)
        {
            string? video = await RunProbeAsync(path, "v:0", cancellationToken).ConfigureAwait(false);
            string? audio = await RunProbeAsync(path, "a:0", cancellationToken).ConfigureAwait(false);

            return (video, audio);
        }

        private async Task<string?> RunProbeAsync(string path, string stream, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            foreach (var arg in new[]
                     {
                         "-v", "error",
                         "-select_streams", stream,
                         "-show_entries", "stream=codec_name",
                         "-of", "default=nokey=1:noprint_wrappers=1",
                         path
                     })
            {
                startInfo.ArgumentList.Add(arg);
            }

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                output = output.Trim();
                return string.IsNullOrWhiteSpace(output) ? null : output.Split('\n')[0].Trim();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Could not probe {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts one file according to its plan.
        ///
        /// The converted file is written alongside the original and only replaces it once
        /// ffmpeg has succeeded, so an interrupted run cannot destroy the source.
        /// </summary>
        public async Task<ConversionResult> ConvertAsync(
            string path,
            bool replaceOriginal = false,
            CancellationToken cancellationToken = default)
        {
            var result = new ConversionResult { SourcePath = path };
            var started = DateTime.UtcNow;

            if (!IsUsable)
            {
                result.Message = "ffmpeg is not available.";
                return result;
            }

            var (video, audio) = await ProbeAsync(path, cancellationToken).ConfigureAwait(false);

            if (video == null)
            {
                result.Skipped = true;
                result.Message = "not a readable video file";
                return result;
            }

            var plan = MediaConversionPlanner.Plan(Path.GetExtension(path), video, audio);

            if (plan.AlreadyCorrect)
            {
                result.Skipped = true;
                result.Succeeded = true;
                result.Message = plan.Reason;
                return result;
            }

            string target = Path.ChangeExtension(path, plan.TargetExtension);

            // Converting "x.mp4" to "x.mp4" would collide with its own source.
            string working = Path.Combine(
                Path.GetDirectoryName(path) ?? ".",
                Path.GetFileNameWithoutExtension(path) + ".converting" + plan.TargetExtension);

            OnLog?.Invoke($"{Path.GetFileName(path)}: {plan.Cost} - {plan.Reason}");

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(path);

            if (plan.Video == StreamAction.Copy)
            {
                startInfo.ArgumentList.Add("-c:v");
                startInfo.ArgumentList.Add("copy");
            }
            else
            {
                startInfo.ArgumentList.Add("-c:v");
                startInfo.ArgumentList.Add("libx264");
                startInfo.ArgumentList.Add("-preset");
                startInfo.ArgumentList.Add("medium");
                // CRF 20 is visually near-transparent for this kind of source without
                // producing files larger than the original.
                startInfo.ArgumentList.Add("-crf");
                startInfo.ArgumentList.Add("20");
                startInfo.ArgumentList.Add("-pix_fmt");
                startInfo.ArgumentList.Add("yuv420p");
            }

            if (audio == null)
            {
                startInfo.ArgumentList.Add("-an");
            }
            else if (plan.Audio == StreamAction.Copy)
            {
                startInfo.ArgumentList.Add("-c:a");
                startInfo.ArgumentList.Add("copy");
            }
            else
            {
                startInfo.ArgumentList.Add("-c:a");
                startInfo.ArgumentList.Add("aac");
                startInfo.ArgumentList.Add("-b:a");
                startInfo.ArgumentList.Add("192k");
            }

            // Puts the index at the front so the file can start playing before it is fully
            // read - which matters for streaming from a NAS or to a Plex client.
            startInfo.ArgumentList.Add("-movflags");
            startInfo.ArgumentList.Add("+faststart");

            startInfo.ArgumentList.Add(working);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string errors = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (process.ExitCode != 0 || !File.Exists(working))
                {
                    result.Message = "ffmpeg failed: " + LastMeaningfulLine(errors);
                    TryDelete(working);
                    return result;
                }

                // Only now is it safe to touch the original.
                if (replaceOriginal)
                {
                    if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(path);
                    }

                    TryDelete(target);
                    File.Move(working, target);
                    result.OutputPath = target;
                }
                else
                {
                    string kept = MakeUniquePath(target);
                    File.Move(working, kept);
                    result.OutputPath = kept;
                }

                result.Succeeded = true;
                result.Message = plan.Cost;
                return result;
            }
            catch (OperationCanceledException)
            {
                TryDelete(working);
                result.Message = "cancelled";
                return result;
            }
            catch (Exception ex)
            {
                TryDelete(working);
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                result.Duration = DateTime.UtcNow - started;
            }
        }

        /// <summary>
        /// Converts every video file under a folder.
        /// </summary>
        public async Task<List<ConversionResult>> ConvertFolderAsync(
            string folder,
            bool replaceOriginals = false,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ConversionResult>();

            if (!Directory.Exists(folder))
            {
                OnLog?.Invoke($"Folder not found: {folder}");
                return results;
            }

            var files = Directory
                .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                // yt-dlp's unfinished downloads and unmerged streams are not library files.
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileNameWithoutExtension(f).Contains(".converting", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();

            OnLog?.Invoke($"Examining {files.Count} file(s) in {folder}...");

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var result = await ConvertAsync(file, replaceOriginals, cancellationToken).ConfigureAwait(false);
                results.Add(result);

                if (result.Skipped && result.Succeeded)
                {
                    OnLog?.Invoke($"  = {Path.GetFileName(file)}: {result.Message}");
                }
                else if (result.Succeeded)
                {
                    OnLog?.Invoke($"  + {Path.GetFileName(result.OutputPath!)} ({result.Duration.TotalSeconds:0.0}s, {result.Message})");
                }
                else if (!result.Skipped)
                {
                    OnLog?.Invoke($"  ! {Path.GetFileName(file)}: {result.Message}");
                }
            }

            return results;
        }

        private static string MakeUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path) ?? ".";
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }

            return path;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// ffmpeg's last line is usually the actual error, under a wall of banner output.
        /// </summary>
        private static string LastMeaningfulLine(string errors)
        {
            var line = errors
                .Split('\n')
                .Select(l => l.Trim())
                .LastOrDefault(l => l.Length > 0 && !l.StartsWith("frame=", StringComparison.OrdinalIgnoreCase));

            return string.IsNullOrWhiteSpace(line) ? "no output" : line;
        }
    }
}
