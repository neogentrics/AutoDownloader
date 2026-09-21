using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Manages the series checked for new episodes.
    ///
    /// The same list backs `autodl --watch-run`, so anything added here is picked up by a
    /// scheduled run without being entered twice. Checking from this window is mainly for
    /// confirming an entry works before leaving it to a schedule.
    /// </summary>
    public partial class WatchListWindow : Window
    {
        private readonly WatchListService _watchList;
        private readonly SettingsService _settingsService;
        private readonly Func<IUserPrompt, DownloadOrchestrator?> _orchestratorFactory;
        private CancellationTokenSource? _cancellation;

        /// <summary>Raised for each log line produced by a check, so the main window can show it.</summary>
        public event Action<string, JobLogLevel>? OnCheckLog;

        public WatchListWindow(
            SettingsService settingsService,
            Func<IUserPrompt, DownloadOrchestrator?> orchestratorFactory)
        {
            InitializeComponent();

            _settingsService = settingsService;
            _orchestratorFactory = orchestratorFactory;

            _watchList = new WatchListService();
            _watchList.OnDiagnostic += message => SetStatus(message);

            Refresh();
        }

        private void Refresh()
        {
            var selectedId = (EntriesListBox.SelectedItem as WatchListEntry)?.Id;

            EntriesListBox.ItemsSource = null;
            EntriesListBox.ItemsSource = _watchList.Entries.ToList();

            if (selectedId != null)
            {
                EntriesListBox.SelectedItem = _watchList.Entries.FirstOrDefault(e => e.Id == selectedId);
            }

            SetStatus($"{_watchList.Entries.Count} watched.");
        }

        private void SetStatus(string message) => StatusTextBlock.Text = message;

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Enter a series or playlist URL first.");
                return;
            }

            int? season = null;
            string seasonText = SeasonTextBox.Text.Trim();

            if (seasonText.Length > 0)
            {
                if (!int.TryParse(seasonText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    || parsed < 0)
                {
                    SetStatus("Season must be a whole number, or blank.");
                    return;
                }

                season = parsed;
            }

            var entry = _watchList.Add(url, ShowNameTextBox.Text, season, _settingsService.Settings.DefaultOutputFolder);

            UrlTextBox.Clear();
            SeasonTextBox.Clear();
            ShowNameTextBox.Clear();

            Refresh();
            EntriesListBox.SelectedItem = _watchList.Entries.FirstOrDefault(x => x.Id == entry.Id);
            SetStatus($"Watching [{entry.Id}].");
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesListBox.SelectedItem is not WatchListEntry entry)
            {
                SetStatus("Select an entry first.");
                return;
            }

            var confirm = MessageBox.Show(
                $"Stop watching {entry.Display}?\n\nDownloaded files are not touched.",
                "Remove from watch list",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            _watchList.Remove(entry.Id);
            Refresh();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesListBox.SelectedItem is not WatchListEntry entry)
            {
                SetStatus("Select an entry first.");
                return;
            }

            _watchList.SetEnabled(entry.Id, !entry.Enabled);
            Refresh();
            SetStatus(entry.Enabled ? "Enabled." : "Disabled; it will be skipped by checks.");
        }

        /// <summary>
        /// Checks the selected entry now, or everything when nothing is selected.
        /// </summary>
        private async void CheckNowButton_Click(object sender, RoutedEventArgs e)
        {
            var toCheck = EntriesListBox.SelectedItem is WatchListEntry selected
                ? new[] { selected }
                : _watchList.GetDueEntries().ToArray();

            if (toCheck.Length == 0)
            {
                SetStatus("Nothing to check.");
                return;
            }

            CheckNowButton.IsEnabled = false;
            _cancellation = new CancellationTokenSource();

            int newEpisodes = 0;

            try
            {
                foreach (var entry in toCheck)
                {
                    SetStatus($"Checking {entry.Display}...");

                    // Never prompts: a check should not stop waiting for an answer.
                    var orchestrator = _orchestratorFactory(new AutoConfirmPrompt());

                    if (orchestrator == null)
                    {
                        SetStatus("Services are still starting; try again in a moment.");
                        return;
                    }

                    orchestrator.OnLog += (_, args) => OnCheckLog?.Invoke($"[{entry.Id}] {args.Message}", args.Level);

                    DownloadJobResult result;

                    try
                    {
                        result = await orchestrator.RunAsync(
                            entry.Url,
                            entry.OutputFolder ?? _settingsService.Settings.DefaultOutputFolder,
                            _cancellation.Token,
                            entry.Season);
                    }
                    catch (Exception ex)
                    {
                        _watchList.RecordCheck(entry, false, entry.LastEpisodeCount, ex.Message, null);
                        OnCheckLog?.Invoke($"[{entry.Id}] failed: {ex.Message}", JobLogLevel.Error);
                        continue;
                    }

                    bool succeeded = result.Completed && !result.Cancelled;
                    newEpisodes += result.FilesAdded;

                    string outcome = result.FilesAdded > 0
                        ? $"{result.FilesAdded} new episode(s)"
                        : succeeded ? "nothing new" : (result.FailureReason ?? "failed");

                    _watchList.RecordCheck(entry, succeeded, result.FilesPresentAfter, outcome, result.OfficialTitle);

                    if (_cancellation.IsCancellationRequested) break;
                }

                Refresh();
                SetStatus($"Checked {toCheck.Length}; {newEpisodes} new episode(s).");
            }
            finally
            {
                CheckNowButton.IsEnabled = true;
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cancellation?.Cancel();
            base.OnClosed(e);
        }
    }
}
