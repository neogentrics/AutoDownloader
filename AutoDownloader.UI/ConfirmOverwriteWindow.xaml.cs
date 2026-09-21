using AutoDownloader.Core;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Asks what to do about an episode already on disk.
    ///
    /// The "all" answers matter more than they look: a season is the usual unit of work, and
    /// being asked the same question twelve times in a row is an obstacle rather than a
    /// choice.
    ///
    /// Like the other dialogs this auto-answers on a timer, and the timed answer is Keep -
    /// the choice that destroys nothing. An unattended run must never delete a file because
    /// nobody was there to say otherwise.
    /// </summary>
    public partial class ConfirmOverwriteWindow : Window
    {
        private AutoAnswerTimer? _timer;

        /// <summary>
        /// Stops at the first key, click or scroll, so this only governs a run nobody is
        /// watching - where the answer it gives itself is Keep.
        /// </summary>
        private const int AutoAnswerSeconds = 60;

        public OverwriteDecision Decision { get; private set; } = OverwriteDecision.Skip;

        public ConfirmOverwriteWindow(ExistingEpisode existing)
        {
            InitializeComponent();

            string title = string.IsNullOrWhiteSpace(existing.EpisodeTitle)
                ? existing.EpisodeLabel
                : $"{existing.EpisodeLabel} - {existing.EpisodeTitle}";

            HeadingTextBlock.Text = $"{title} is already downloaded.";
            FileNameTextBlock.Text = Path.GetFileName(existing.Path);
            FileDetailTextBlock.Text =
                $"{existing.SizeDisplay}  ·  last modified {existing.LastModified:yyyy-MM-dd HH:mm}";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer = new AutoAnswerTimer(
                this,
                AutoAnswerSeconds,
                remaining => KeepButton.Content = remaining < 0 ? "Keep" : $"Keep ({remaining})",
                () => Keep_Click(this, new RoutedEventArgs()));

            _timer.Start();
        }

        private void Finish(OverwriteDecision decision)
        {
            _timer?.Stop();
            Decision = decision;
            DialogResult = decision != OverwriteDecision.Cancel;
            Close();
        }

        private void Keep_Click(object sender, RoutedEventArgs e) => Finish(OverwriteDecision.Skip);
        private void KeepAll_Click(object sender, RoutedEventArgs e) => Finish(OverwriteDecision.SkipAll);
        private void Replace_Click(object sender, RoutedEventArgs e) => Finish(OverwriteDecision.Overwrite);
        private void ReplaceAll_Click(object sender, RoutedEventArgs e) => Finish(OverwriteDecision.OverwriteAll);
        private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(OverwriteDecision.Cancel);
    }
}
