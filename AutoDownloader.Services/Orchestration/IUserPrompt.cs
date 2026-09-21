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
    }
}
