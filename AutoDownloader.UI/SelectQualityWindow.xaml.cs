using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Offers the quality rungs a source actually publishes, with their real sizes.
    ///
    /// A preset can only guess. This show publishes the same 1920x1080 picture at 4.3, 6.6
    /// and 10.3 Mbps, and the top rung is not a better picture - only a file three times the
    /// size. Showing the real rungs turns that from a guess into a decision, and shows what
    /// the whole run will cost before it starts rather than after.
    /// </summary>
    public partial class SelectQualityWindow : Window
    {
        private readonly ObservableCollection<FormatOption> _options = new ObservableCollection<FormatOption>();
        private readonly int _episodeCount;

        private AutoAnswerTimer? _timer;

        /// <summary>
        /// Long enough to compare a list of rungs, and it stops the moment anyone touches it.
        /// </summary>
        private const int AutoAnswerSeconds = 60;

        /// <summary>
        /// The chosen format selector, or null to leave Preferences in charge - which is also
        /// what the timer answers, since silence should not change anybody's settings.
        /// </summary>
        public string? SelectedFormat { get; private set; }

        public SelectQualityWindow(string showTitle, IReadOnlyList<FormatOption> options, int episodeCount)
        {
            InitializeComponent();

            _episodeCount = episodeCount;

            HeadingTextBlock.Text = $"{showTitle} is available at {options.Count} qualities.";
            SubheadingTextBlock.Text =
                "Sizes are per episode and approximate. Where a resolution appears more than "
                + "once, the larger file is the same picture at a higher bitrate - not a "
                + "sharper one.";

            foreach (var option in options) _options.Add(option);

            OptionsListBox.ItemsSource = _options;

            // Start on the best rung that is not the most extravagant one, which is the
            // choice most people would make after reading the list.
            OptionsListBox.SelectedItem = Recommended(options);

            UpdateTotal();
        }

        /// <summary>
        /// The highest resolution on offer, at its most efficient bitrate: the same picture
        /// as the top rung, without paying for it.
        /// </summary>
        private static FormatOption? Recommended(IReadOnlyList<FormatOption> options)
        {
            if (options.Count == 0) return null;

            int best = options.Max(o => o.Height);

            return options
                .Where(o => o.Height == best)
                .OrderBy(o => o.Bitrate)
                .FirstOrDefault()
                ?? options[0];
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer = new AutoAnswerTimer(
                this,
                AutoAnswerSeconds,
                remaining => UseButton.Content = remaining < 0 ? "Use this" : $"Use this ({remaining})",
                () => KeepSetting_Click(this, new RoutedEventArgs()));

            _timer.Start();
        }

        private void UpdateTotal()
        {
            if (OptionsListBox.SelectedItem is not FormatOption chosen || chosen.EstimatedBytes <= 0)
            {
                TotalTextBlock.Text = string.Empty;
                return;
            }

            if (_episodeCount <= 1)
            {
                TotalTextBlock.Text = $"About {chosen.SizeDisplay} for this episode.";
                return;
            }

            double totalGb = chosen.EstimatedBytes * (double)_episodeCount / 1_073_741_824.0;

            TotalTextBlock.Text =
                $"About {chosen.SizeDisplay} per episode - roughly {totalGb:0.0} GB for all "
                + $"{_episodeCount} episodes.";
        }

        private void OptionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateTotal();

        private void OptionsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (OptionsListBox.SelectedItem != null) Use_Click(sender, new RoutedEventArgs());
        }

        private void Use_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();

            SelectedFormat = OptionsListBox.SelectedItem is FormatOption chosen
                ? chosen.ToSelector()
                : null;

            DialogResult = true;
            Close();
        }

        private void KeepSetting_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();

            SelectedFormat = null;
            DialogResult = true;
            Close();
        }
    }
}
