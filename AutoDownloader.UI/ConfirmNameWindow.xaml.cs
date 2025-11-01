using System;
using System.Windows;
using System.Windows.Threading;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Interaction logic for ConfirmNameWindow.xaml.
    /// This is the first pop-up window in the v1.9.x download process.
    /// It asks the user to confirm or edit the show name found by the parser.
    /// </summary>
    public partial class ConfirmNameWindow : Window
    {
        // --- Private Fields ---

        /// <summary>
        /// Timer for the auto-confirm countdown.
        /// </summary>
        private DispatcherTimer _timer;

        /// <summary>
        /// The number of seconds remaining before auto-confirming.
        /// </summary>
        private int _countdown = 15;

        // --- Public Properties ---

        /// <summary>
        /// Public property that MainWindow reads to get the final,
        /// user-confirmed (or edited) show name.
        /// </summary>
        public string ShowName { get; private set; }

        // We no longer need IsConfirmed, as we will use DialogResult.
        // public bool IsConfirmed { get; private set; } = false;

        // --- Constructor ---

        /// <summary>
        /// Initializes a new instance of the ConfirmNameWindow.
        /// </summary>
        /// <param name="foundName">The show name that was auto-detected by the URL parser.</param>
        public ConfirmNameWindow(string foundName)
        {
            InitializeComponent();

            // Pre-fill the text box with the name we found.
            ShowNameTextBox.Text = foundName;
            ShowName = foundName;

            // Select all text and focus the box for immediate, easy editing.
            ShowNameTextBox.SelectAll();
            ShowNameTextBox.Focus();
        }

        // --- Event Handlers ---

        /// <summary>
        /// This method is called once the window has finished loading.
        /// It starts the countdown timer.
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        /// <summary>
        /// This method fires once every second.
        /// It updates the countdown button and auto-confirms when the timer hits zero.
        /// </summary>
        private void Timer_Tick(object? sender, EventArgs e)
        {
            _countdown--;
            ConfirmButton.Content = $"Confirm ({_countdown})";
            if (_countdown <= 0)
            {
                // Timer ran out, automatically trigger the "Confirm" click.
                ConfirmButton_Click(this, new RoutedEventArgs());
            }
        }

        /// <summary>
        /// Called when the user clicks the "Confirm" button.
        /// This is the "success" path.
        /// </summary>
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();

            // Get the final text from the box (in case the user edited it).
            ShowName = ShowNameTextBox.Text;

            // ** CRITICAL FIX **
            // Set DialogResult to true. This tells MainWindow's ShowDialog()
            // that the user confirmed, and it will return 'true'.
            this.DialogResult = true;

            this.Close(); // This will return control to MainWindow
        }

        /// <summary>
        /// Called when the user clicks the "Cancel" button.
        /// This is the "failure" or "abort" path.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();

            // ** CRITICAL FIX **
            // Set DialogResult to false. This tells MainWindow's ShowDialog()
            // that the user canceled, and it will return 'false'.
            this.DialogResult = false;

            this.Close(); // This will return control to MainWindow
        }
    }
}