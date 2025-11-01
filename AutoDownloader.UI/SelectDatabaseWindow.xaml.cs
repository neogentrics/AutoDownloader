using System;
using System.Windows;
using System.Windows.Threading;

namespace AutoDownloader.UI
{
    // Enum to define the user's choice
    public enum DatabaseSource { Canceled, TMDB, TVDB }

    public partial class SelectDatabaseWindow : Window
    {
        private DispatcherTimer _timer;
        private int _countdown = 60;

        public DatabaseSource SelectedSource { get; private set; } = DatabaseSource.Canceled;

        public SelectDatabaseWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _countdown--;
            TmdbButton.Content = $"Search TMDB (Default: {_countdown}s)";
            if (_countdown <= 0)
            {
                // Auto-select TMDB
                TmdbButton_Click(this, new RoutedEventArgs());
            }
        }

        private void TmdbButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            SelectedSource = DatabaseSource.TMDB;
            this.Close();
        }

        private void TvdbButton_Click(object sender, RoutedEventArgs e)
         {
             _timer.Stop();
             SelectedSource = DatabaseSource.TVDB;
             this.Close();
         }
    }
}