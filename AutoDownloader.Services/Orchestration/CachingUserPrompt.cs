using AutoDownloader.Core;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Orchestration
{
    /// <summary>
    /// Answers from what the user already said, and only asks when it genuinely does not know.
    ///
    /// This is what lets a batch be resolved up front and then left alone: the same questions
    /// asked during the download find their answers already recorded.
    /// </summary>
    public sealed class CachingUserPrompt : IUserPrompt
    {
        private readonly IUserPrompt _inner;
        private readonly SeriesResolutionCache _cache;

        public CachingUserPrompt(IUserPrompt inner, SeriesResolutionCache cache)
        {
            _inner = inner;
            _cache = cache;
        }

        public async Task<string?> ConfirmShowNameAsync(
            string suggestedName, CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetShowName(suggestedName, out string? remembered))
            {
                return remembered;
            }

            string? answer = await _inner
                .ConfirmShowNameAsync(suggestedName, cancellationToken)
                .ConfigureAwait(false);

            _cache.RememberShowName(suggestedName, answer);
            return answer;
        }

        public async Task<SeriesCandidate?> ChooseSeriesAsync(
            string searchTerm,
            IReadOnlyList<SeriesCandidate> candidates,
            SeriesCandidate? suggested,
            CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetSeries(searchTerm, out var remembered))
            {
                return remembered;
            }

            var chosen = await _inner
                .ChooseSeriesAsync(searchTerm, candidates, suggested, cancellationToken)
                .ConfigureAwait(false);

            _cache.RememberSeries(searchTerm, chosen);
            return chosen;
        }

        public async Task<IReadOnlyList<int>?> ChooseSeasonsAsync(
            string showTitle,
            IReadOnlyList<int> available,
            int showing,
            CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetSeasons(showTitle, out var remembered)) return remembered;

            var chosen = await _inner
                .ChooseSeasonsAsync(showTitle, available, showing, cancellationToken)
                .ConfigureAwait(false);

            _cache.RememberSeasons(showTitle, chosen);
            return chosen;
        }

        public async Task<string?> ChooseQualityAsync(
            string showTitle,
            IReadOnlyList<FormatOption> options,
            CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetQuality(showTitle, out var remembered)) return remembered;

            string? chosen = await _inner
                .ChooseQualityAsync(showTitle, options, cancellationToken)
                .ConfigureAwait(false);

            // Cached either way: "leave it alone" is an answer, and asking again would be
            // asking the same question about the same show.
            _cache.RememberQuality(showTitle, chosen);
            return chosen;
        }

        public async Task<OverwriteDecision> ConfirmOverwriteAsync(
            ExistingEpisode existing, CancellationToken cancellationToken = default)
        {
            // "All" means all, not "all of whichever item happened to be running". Without
            // this, answering Replace All on the first show asks again on the second.
            if (_cache.BatchOverwrite.HasValue)
            {
                return _cache.BatchOverwrite.Value;
            }

            var decision = await _inner
                .ConfirmOverwriteAsync(existing, cancellationToken)
                .ConfigureAwait(false);

            if (decision == OverwriteDecision.SkipAll || decision == OverwriteDecision.OverwriteAll)
            {
                _cache.BatchOverwrite = decision;
            }

            return decision;
        }
    }
}
