using AutoDownloader.Core;
using System;
using System.Collections.Concurrent;

namespace AutoDownloader.Services.Orchestration
{
    /// <summary>
    /// Remembers what the user already told us, for the length of one batch.
    ///
    /// A batch of ten links used to stop ten times to ask which show each one was, scattered
    /// through the run - so the download could not be left alone, and a question could arrive
    /// forty minutes in. The answers do not change between the question and the download, so
    /// they only need asking once.
    ///
    /// Scoped to a batch rather than persisted: a saved answer that turns out to be wrong is
    /// far more annoying to discover than one question.
    /// </summary>
    public sealed class SeriesResolutionCache
    {
        private readonly ConcurrentDictionary<string, string> _showNames =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, SeriesCandidate> _series =
            new ConcurrentDictionary<string, SeriesCandidate>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A "keep all" or "replace all" answer, which applies to the rest of the batch
        /// rather than only to the item that happened to be running when it was given.
        /// </summary>
        public OverwriteDecision? BatchOverwrite { get; set; }

        /// <summary>Answers recorded so far. Used only for reporting.</summary>
        public int Count => _showNames.Count + _series.Count;

        public bool TryGetShowName(string key, out string? value)
        {
            bool found = _showNames.TryGetValue(Normalise(key), out string? stored);
            value = stored;
            return found;
        }

        public void RememberShowName(string key, string? value)
        {
            if (value == null) return;
            _showNames[Normalise(key)] = value;
        }

        public bool TryGetSeries(string key, out SeriesCandidate? value)
        {
            bool found = _series.TryGetValue(Normalise(key), out var stored);
            value = stored;
            return found;
        }

        /// <summary>
        /// A null choice means the user cancelled, which is not remembered: cancelling one
        /// item should not silently cancel the rest of the batch.
        /// </summary>
        public void RememberSeries(string key, SeriesCandidate? value)
        {
            if (value == null) return;
            _series[Normalise(key)] = value;
        }

        private readonly ConcurrentDictionary<string, IReadOnlyList<int>> _seasons =
            new ConcurrentDictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

        public bool TryGetSeasons(string key, out IReadOnlyList<int>? value)
        {
            bool found = _seasons.TryGetValue(Normalise(key), out var stored);
            value = stored;
            return found;
        }

        /// <summary>
        /// A cancel is not remembered, for the same reason a cancelled show choice is not:
        /// giving up on one item must not silently skip the rest.
        /// </summary>
        public void RememberSeasons(string key, IReadOnlyList<int>? value)
        {
            if (value == null) return;
            _seasons[Normalise(key)] = value;
        }

        private static string Normalise(string key) => (key ?? string.Empty).Trim();
    }
}
