using AutoDownloader.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Shows which sites yt-dlp can extract from, searchable.
    ///
    /// Worth having visible rather than asserted. It is easy to assume the app only works
    /// with whatever was last tried successfully, when the real answer is well over a
    /// thousand sites - and equally easy to assume a site is supported when it is not. The
    /// list is read from the installed yt-dlp, so it is accurate for this machine today
    /// rather than whatever was true when the app was written.
    /// </summary>
    public partial class SupportedSitesWindow : Window
    {
        private readonly string _ytDlpPath;
        private List<string> _allSites = new List<string>();

        public SupportedSitesWindow(string ytDlpPath)
        {
            InitializeComponent();
            _ytDlpPath = ytDlpPath;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var checker = new SupportedSiteChecker(_ytDlpPath);
                _allSites = await checker.GetAllSiteNamesAsync();

                if (_allSites.Count == 0)
                {
                    HeadingTextBlock.Text = "Could not read the list from yt-dlp.";
                    CountTextBlock.Text = string.Empty;
                    return;
                }

                HeadingTextBlock.Text = $"yt-dlp can extract from {_allSites.Count:N0} sites.";
                Apply(string.Empty);
            }
            catch (Exception ex)
            {
                HeadingTextBlock.Text = $"Could not read the list: {ex.Message}";
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHintTextBlock.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            Apply(SearchTextBox.Text);
        }

        private void Apply(string filter)
        {
            IEnumerable<string> shown = _allSites;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                shown = _allSites.Where(s => s.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            var list = shown.ToList();

            SitesListBox.ItemsSource = list;

            CountTextBlock.Text = list.Count == _allSites.Count
                ? $"{list.Count:N0} sites"
                : $"{list.Count:N0} of {_allSites.Count:N0}";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
