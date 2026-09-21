using AutoDownloader.Services.Orchestration;
using System;
using System.Collections.Generic;
using System.Text;

namespace AutoDownloader.Cli
{
    /// <summary>
    /// Renders progress to the console.
    ///
    /// Behaviour differs by destination on purpose. On a terminal it rewrites one line in
    /// place, which is what a person wants to watch. When output is redirected - a scheduled
    /// task, a CI log, an n8n execution record - carriage returns produce an unreadable smear,
    /// so it instead prints a line only when the reported percentage has moved by a meaningful
    /// step.
    /// </summary>
    public class ConsoleProgressRenderer
    {
        private readonly bool _interactive;
        private readonly bool _quiet;
        private int _lastLoggedBucket = -1;
        private int _lastLineLength;
        private string? _lastEpisodeLabel;

        /// <summary>Percentage step between lines when output is redirected.</summary>
        private const int RedirectedStepPercent = 10;

        public ConsoleProgressRenderer(bool quiet)
        {
            _quiet = quiet;
            _interactive = !Console.IsOutputRedirected;
        }

        public void Report(JobProgress progress)
        {
            if (_quiet) return;

            double? overall = progress.OverallPercent ?? progress.File?.Percent;

            string text = Compose(progress, overall);

            if (_interactive)
            {
                WriteInPlace(text);
                return;
            }

            // Redirected: a new episode always prints, otherwise only on a step change.
            bool episodeChanged = progress.EpisodeLabel != _lastEpisodeLabel;
            int bucket = overall.HasValue ? (int)(overall.Value / RedirectedStepPercent) : -1;

            if (episodeChanged || bucket != _lastLoggedBucket)
            {
                _lastEpisodeLabel = progress.EpisodeLabel;
                _lastLoggedBucket = bucket;
                Console.WriteLine("  " + text);
            }
        }

        /// <summary>
        /// Clears the in-place line so following output does not land on top of it.
        /// </summary>
        public void Clear()
        {
            if (_quiet || !_interactive || _lastLineLength == 0) return;

            Console.Write("\r" + new string(' ', _lastLineLength) + "\r");
            _lastLineLength = 0;
        }

        private void WriteInPlace(string text)
        {
            // Pad to erase whatever the previous, possibly longer, line left behind.
            var padded = new StringBuilder(text);
            if (text.Length < _lastLineLength)
            {
                padded.Append(' ', _lastLineLength - text.Length);
            }

            Console.Write("\r" + padded);
            _lastLineLength = text.Length;
        }

        private static string Compose(JobProgress progress, double? overall)
        {
            var parts = new List<string>();

            if (progress.EpisodeCount > 1 && progress.EpisodeIndex > 0)
            {
                string label = string.IsNullOrEmpty(progress.EpisodeLabel)
                    ? $"episode {progress.EpisodeIndex}/{progress.EpisodeCount}"
                    : $"{progress.EpisodeLabel} ({progress.EpisodeIndex}/{progress.EpisodeCount})";
                parts.Add(label);
            }

            if (overall.HasValue) parts.Add(Bar(overall.Value));
            if (progress.File?.Percent is double pct) parts.Add($"{pct,5:0.0}%");
            if (!string.IsNullOrWhiteSpace(progress.File?.Speed)) parts.Add(progress.File!.Speed!);
            if (!string.IsNullOrWhiteSpace(progress.File?.Eta)) parts.Add($"ETA {progress.File!.Eta}");

            return string.Join("  ", parts);
        }

        /// <summary>
        /// A fixed-width text bar, so the line does not jitter as the numbers change width.
        /// </summary>
        private static string Bar(double percent, int width = 24)
        {
            int filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100d * width);
            return "[" + new string('#', filled) + new string('-', width - filled) + "]";
        }
    }
}
