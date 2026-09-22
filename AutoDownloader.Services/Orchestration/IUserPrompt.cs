using AutoDownloader.Core;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Orchestration
{
    /// <summary>
    /// How the orchestrator asks a human a question.
    ///
    /// The orchestrator must not reference WPF: keeping the one interactive step behind this
    /// interface is what allows the same pipeline to run headless (from a CLI, a scheduled
    /// task, or an automation webhook) with no code duplicated.
    /// </summary>
    public interface IUserPrompt
    {
        /// <summary>
        /// Confirms or corrects the show name parsed out of the URL.
        /// </summary>
        /// <returns>
        /// The name to search the metadata databases for, or null if the user cancelled.
        /// </returns>
        Task<string?> ConfirmShowNameAsync(string suggestedName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asks which of several shows was meant, when a name matches more than one.
        ///
        /// This is a separate question from confirming the name because it has a different
        /// answer shape: the name may be exactly right and still match a show and its reboot.
        /// The year is what tells them apart.
        /// </summary>
        /// <param name="searchTerm">What was searched for.</param>
        /// <param name="candidates">The possible matches, best first.</param>
        /// <param name="suggested">The one that will be used if no choice is made.</param>
        /// <returns>The chosen show, or null to abort.</returns>
        Task<SeriesCandidate?> ChooseSeriesAsync(
            string searchTerm,
            IReadOnlyList<SeriesCandidate> candidates,
            SeriesCandidate? suggested,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Asks what to do about an episode already present on disk.
        ///
        /// Previously these were skipped silently, which is a reasonable default and a poor
        /// answer when the existing file is the one you wanted to replace - a truncated
        /// download, or a better source found since.
        /// </summary>
        Task<OverwriteDecision> ConfirmOverwriteAsync(
            ExistingEpisode existing,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Asks which seasons to fetch, when the page offers a choice.
        ///
        /// Asked before anything downloads, because the alternative is finding out after
        /// nineteen seasons that only one was wanted.
        /// </summary>
        /// <param name="available">Every season the page offers, ascending.</param>
        /// <param name="showing">The season the page is currently displaying.</param>
        /// <returns>The seasons to fetch, or null if the user cancelled.</returns>
        Task<IReadOnlyList<int>?> ChooseSeasonsAsync(
            string showTitle,
            IReadOnlyList<int> available,
            int showing,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Offers the quality rungs a source actually publishes, with their real sizes.
        /// </summary>
        /// <returns>
        /// A yt-dlp format selector, or null to keep whatever Preferences already says -
        /// which is also the answer when nobody is there to choose.
        /// </returns>
        Task<string?> ChooseQualityAsync(
            string showTitle,
            IReadOnlyList<FormatOption> options,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A prompt implementation that accepts every suggestion without asking.
    /// Use this for unattended runs.
    /// </summary>
    public sealed class AutoConfirmPrompt : IUserPrompt
    {
        public Task<string?> ConfirmShowNameAsync(string suggestedName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(suggestedName);

        /// <summary>
        /// Takes the suggested match without asking. SeriesSelector already prefers an exact
        /// title match and then the earliest year, so this is the original rather than
        /// whichever reboot is currently more popular.
        /// </summary>
        public Task<SeriesCandidate?> ChooseSeriesAsync(
            string searchTerm,
            IReadOnlyList<SeriesCandidate> candidates,
            SeriesCandidate? suggested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(suggested ?? (candidates.Count > 0 ? candidates[0] : null));

        /// <summary>
        /// Keeps what is already there. An unattended run must not silently re-download a
        /// library, and must never destroy a file nobody asked it to replace.
        /// </summary>
        public Task<OverwriteDecision> ConfirmOverwriteAsync(
            ExistingEpisode existing, CancellationToken cancellationToken = default)
            => Task.FromResult(OverwriteDecision.SkipAll);

        /// <summary>
        /// Takes only the season already on screen.
        ///
        /// An unattended run must not decide by itself to fetch nineteen seasons because a
        /// page happened to offer them; that is a surprise measured in hundreds of gigabytes.
        /// </summary>
        public Task<IReadOnlyList<int>?> ChooseSeasonsAsync(
            string showTitle,
            IReadOnlyList<int> available,
            int showing,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<int>?>(new[] { showing });

        /// <summary>
        /// Keeps the configured preference. An unattended run has no business quietly
        /// choosing a different file size from the one its settings ask for.
        /// </summary>
        public Task<string?> ChooseQualityAsync(
            string showTitle,
            IReadOnlyList<FormatOption> options,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
