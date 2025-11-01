using System;
using System.Windows;
using System.Windows.Threading;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Enum to define the user's database choice.
    /// This is passed back to MainWindow.
    /// </summary>
    public enum DatabaseSource { Canceled, TMDB, TVDB }

    /// <summary>
    /// Interaction logic for SelectDatabaseWindow.xaml.
    /// This is the second pop-up, asking the user which metadata database to search.
    /// </summary>
    public partial class SelectDatabaseWindow : Window
    {
        // --- Private Fields ---

        /// <summary>
        /// Timer for the auto-select countdown.
        /// </summary>
        private DispatcherTimer _timer;

        /// <summary>
        /// Countdown in seconds. You can change this value.
        /// </summary>
        private int _countdown = 30; // 30 second timeout

        // --- Public Properties ---

        /// <summary>
        /// The final choice made by the user (or by the timer).
        /// MainWindow reads this property after the window closes.
        /// Defaults to Canceled in case the user closes the window.
        /// </summary>
        public DatabaseSource SelectedSource { get; private set; } = DatabaseSource.Canceled;

        // --- Constructor ---

        /// <summary>
        /// Initializes a new instance of the SelectDatabaseWindow.
        /// This constructor is now "injected" with the status of the API keys.
        /// </summary>
        /// <param name="isTmdbValid">True if the user has a valid TMDB API key.</param>
        /// <param name="isTvdbValid">True if the user has a valid TVDB API key.</param>
        public SelectDatabaseWindow(bool isTmdbValid, bool isTvdbValid)
        {
            InitializeComponent();

            // --- THIS IS THE FIX ---
            // Enable or disable the buttons based on the keys
            // provided by the user in the Preferences window.
            TmdbButton.IsEnabled = isTmdbValid;
            TvdbButton.IsEnabled = isTvdbValid;

            // ** CRITICAL FIX **
            // If the user has no valid keys at all, don't even show the window.
            // Just set the result to Canceled and close immediately.
            if (!isTmdbValid && !isTvdbValid)
            {
                SelectedSource = DatabaseSource.Canceled;
                this.Close();
            }
        }

        // --- Event Handlers ---

        /// <summary>
        /// Called when the window is loaded. Starts the countdown timer.
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        /// <summary>
        /// Fires once per second to update the countdown.
        /// </summary>
        private void Timer_Tick(object? sender, EventArgs e)
        {
            _countdown--;
            // Update the button text with the remaining time.
            TmdbButton.Content = $"Search TMDB (Default: {_countdown}s)";

            if (_countdown <= 0)
            {
                // ** CRITICAL FIX **
                // Time's up. Check if the TMDB button is usable.
                // If it is, "click" it.
                if (TmdbButton.IsEnabled)
                {
                    TmdbButton_Click(this, new RoutedEventArgs());
                }
                else
                {
                    // If TMDB is disabled (no key), and the timer runs out,
                    // just cancel the operation.
                    _timer.Stop();
                    SelectedSource = DatabaseSource.Canceled;
                    this.Close();
                }
            }
        }

        /// <summary>
        /// Called when the user clicks the "Search TMDB" button.
        /// </summary>
        private void TmdbButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            SelectedSource = DatabaseSource.TMDB;
            this.Close();
        }

        /// <summary>
        /// Called when the user clicks the "Search TVDB" button.
        /// </summary>
        private void TvdbButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            SelectedSource = DatabaseSource.TVDB;
            this.Close();
        }
    }
}