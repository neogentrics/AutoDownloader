using AutoDownloader.Services.Orchestration;
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
    }
}
