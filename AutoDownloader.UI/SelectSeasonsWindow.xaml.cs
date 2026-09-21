using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Asks which seasons to fetch, before anything downloads.
    ///
    /// A show page displays one season at a time, so without this the app takes whichever
    /// season happened to be on screen and gives no hint that eighteen others exist. Asking
    /// here rather than later is the point: the alternative is discovering after several
    /// hours that only one season was wanted.
    ///
    /// Nothing is pre-selected except the season the page is already showing, because
    /// defaulting to all of them turns one careless Enter into a very long download.
    /// </summary>
    public partial class SelectSeasonsWindow : Window
    {
        /// <summary>One row: a season and whether it is wanted.</summary>
        public sealed class SeasonChoice : INotifyPropertyChanged
        {
            private bool _isSelected;

            public int Season { get; set; }

            public string Label { get; set; } = string.Empty;

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly ObservableCollection<SeasonChoice> _choices = new ObservableCollection<SeasonChoice>();
        private readonly int _showing;

        private AutoAnswerTimer? _timer;

        /// <summary>
        /// Long enough to actually read a list of nineteen seasons, and it stops at the first
        /// sign of a person anyway.
        /// </summary>
        private const int AutoAnswerSeconds = 60;

        /// <summary>The chosen seasons, or null if the user cancelled.</summary>
        public IReadOnlyList<int>? SelectedSeasons { get; private set; }

        public SelectSeasonsWindow(string showTitle, IReadOnlyList<int> available, int showing)
        {
            InitializeComponent();

            _showing = showing;

            HeadingTextBlock.Text = $"{showTitle} has {available.Count} seasons.";
            SubheadingTextBlock.Text =
                $"The page is showing season {showing}. Tick whichever you want; "
                + "each one is read separately, so they are filed under their real season numbers.";

            CurrentOnlyButton.Content = $"Only season {showing}";

            foreach (var season in available)
            {
                _choices.Add(new SeasonChoice
                {
                    Season = season,
                    Label = $"Season {season}",
                    IsSelected = season == showing,
                });
            }

            SeasonsItemsControl.ItemsSource = _choices;
            UpdateSummary();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer = new AutoAnswerTimer(
                this,
                AutoAnswerSeconds,
                remaining => DownloadButton.Content = remaining < 0 ? "Download" : $"Download ({remaining})",
                () => Download_Click(this, new RoutedEventArgs()));

            _timer.Start();
        }

        private void UpdateSummary()
        {
            int count = _choices.Count(c => c.IsSelected);

            SelectionSummaryTextBlock.Text = count == 0
                ? "Nothing selected."
                : count == 1
                    ? "1 season selected."
                    : $"{count} seasons selected.";
        }

        private void SeasonCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateSummary();

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var choice in _choices) choice.IsSelected = true;
            UpdateSummary();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var choice in _choices) choice.IsSelected = false;
            UpdateSummary();
        }

        private void CurrentOnly_Click(object sender, RoutedEventArgs e)
        {
            foreach (var choice in _choices) choice.IsSelected = choice.Season == _showing;
            UpdateSummary();
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();

            var chosen = _choices.Where(c => c.IsSelected).Select(c => c.Season).OrderBy(n => n).ToList();

            // Selecting nothing is not a way of saying "everything". Fall back to the season
            // already on screen, which is what the app would have done unasked.
            SelectedSeasons = chosen.Count > 0 ? chosen : new List<int> { _showing };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();

            SelectedSeasons = null;
            DialogResult = false;
            Close();
        }
    }
}
