using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Asks which of several shows was meant when a name matches more than one.
    ///
    /// Searching "teen titans" returns both the 2003 series and the 2013 reboot. Taking the
    /// first result produced episode names from the wrong show - and because the names look
    /// entirely plausible, nothing about the output revealed the mistake.
    ///
    /// Like the other dialogs in this app it auto-confirms on a timer, so an unattended run
    /// still makes progress rather than waiting for someone who is not there.
    /// </summary>
    public partial class SelectSeriesWindow : Window
    {
        private AutoAnswerTimer? _timer;

        /// <summary>
        /// Long enough to actually compare two versions of a show and check their years,
        /// which 20 seconds was not. It also stops at the first sign of a person, so the
        /// number only matters to a run nobody is watching.
        /// </summary>
        private const int AutoAnswerSeconds = 60;

        /// <summary>The chosen show, or null if the user cancelled.</summary>
        public SeriesCandidate? SelectedSeries { get; private set; }

        public SelectSeriesWindow(
            string searchTerm,
            IReadOnlyList<SeriesCandidate> candidates,
            SeriesCandidate? suggested)
        {
            InitializeComponent();

            HeadingTextBlock.Text = $"\"{searchTerm}\" matches {candidates.Count} shows:";

            foreach (var candidate in candidates)
            {
                CandidatesListBox.Items.Add(candidate);
            }

            // Pre-select the suggestion so the timer picks something sensible.
            var preselect = suggested != null
                ? candidates.FirstOrDefault(c => c.Id == suggested.Id && c.Source == suggested.Source)
                : null;

            CandidatesListBox.SelectedItem = preselect ?? candidates.FirstOrDefault();
            CandidatesListBox.ScrollIntoView(CandidatesListBox.SelectedItem);

            SelectedSeries = CandidatesListBox.SelectedItem as SeriesCandidate;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer = new AutoAnswerTimer(
                this,
                AutoAnswerSeconds,
                remaining => ChooseButton.Content = remaining < 0
                    ? "Use This Show"
                    : $"Use This Show ({remaining})",
                () => ChooseButton_Click(this, new RoutedEventArgs()));

            _timer.Start();
        }

        private void ChooseButton_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();

            SelectedSeries = CandidatesListBox.SelectedItem as SeriesCandidate;

            DialogResult = SelectedSeries != null;
            Close();
        }

        private void CandidatesListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (CandidatesListBox.SelectedItem != null)
            {
                ChooseButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();

            SelectedSeries = null;
            DialogResult = false;
            Close();
        }
    }
}
