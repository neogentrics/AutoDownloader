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
    }

    /// <summary>
    /// A prompt implementation that accepts every suggestion without asking.
    /// Use this for unattended runs.
    /// </summary>
    public sealed class AutoConfirmPrompt : IUserPrompt
    {
        public Task<string?> ConfirmShowNameAsync(string suggestedName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(suggestedName);
    }
}
