using AutoDownloader.Core;
using AutoDownloader.Services.Orchestration;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AutoDownloader.UI
{
    /// <summary>
    /// The WPF implementation of the orchestrator's one interactive step.
    ///
    /// Keeping this adapter in the UI project is what lets DownloadOrchestrator stay free of
    /// any WPF reference, so the same pipeline can run unattended behind AutoConfirmPrompt.
    /// </summary>
    public sealed class WpfUserPrompt : IUserPrompt
    {
        private readonly Window _owner;

        public WpfUserPrompt(Window owner)
        {
            _owner = owner;
        }

        public Task<string?> ConfirmShowNameAsync(string suggestedName, CancellationToken cancellationToken = default)
        {
            // The orchestrator may be running on a background thread; dialogs must not.
            return _owner.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmNameWindow(suggestedName) { Owner = _owner };

                // ConfirmNameWindow auto-confirms after 15 seconds, so an unattended run still
                // makes progress even through this interactive path.
                bool? confirmed = dialog.ShowDialog();

                return confirmed == true ? dialog.ShowName : null;
            }).Task;
        }

        public Task<SeriesCandidate?> ChooseSeriesAsync(
            string searchTerm,
            IReadOnlyList<SeriesCandidate> candidates,
            SeriesCandidate? suggested,
            CancellationToken cancellationToken = default)
        {
            return _owner.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new SelectSeriesWindow(searchTerm, candidates, suggested) { Owner = _owner };

                // Auto-confirms after 20 seconds, so an unattended run still proceeds.
                bool? chosen = dialog.ShowDialog();

                return chosen == true ? dialog.SelectedSeries : null;
            }).Task;
        }

        public Task<IReadOnlyList<int>?> ChooseSeasonsAsync(
            string showTitle,
            IReadOnlyList<int> available,
            int showing,
            CancellationToken cancellationToken = default)
        {
            return _owner.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new SelectSeasonsWindow(showTitle, available, showing) { Owner = _owner };
                dialog.ShowDialog();
                return dialog.SelectedSeasons;
            }).Task;
        }

        public Task<OverwriteDecision> ConfirmOverwriteAsync(
            ExistingEpisode existing, CancellationToken cancellationToken = default)
        {
            return _owner.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new ConfirmOverwriteWindow(existing) { Owner = _owner };

                // Auto-answers "Keep" after 30 seconds, so an unattended run never destroys a
                // file because nobody was there to say otherwise.
                dialog.ShowDialog();

                return dialog.Decision;
            }).Task;
        }
    }
}
