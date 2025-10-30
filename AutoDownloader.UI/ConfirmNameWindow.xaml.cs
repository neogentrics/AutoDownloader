using System;
using System.Windows;
using System.Windows.Threading;

namespace AutoDownloader.UI
{
    public partial class ConfirmNameWindow : Window
    {
        private DispatcherTimer _timer;
        private int _countdown = 60;

        public string ShowName { get; private set; }
        public bool IsConfirmed { get; private set; } = false;

        public ConfirmNameWindow(string foundName)
        {
            InitializeComponent();
            ShowNameTextBox.Text = foundName;
            ShowName = foundName;
            ShowNameTextBox.SelectAll(); // Highlight text for easy editing
            ShowNameTextBox.Focus();
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
            ConfirmButton.Content = $"Confirm ({_countdown})";
            if (_countdown <= 0)
            {
                // Auto-accept
                ConfirmButton_Click(this, new RoutedEventArgs());
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            ShowName = ShowNameTextBox.Text;
            IsConfirmed = true;
            this.Close(); // This will return control to MainWindow
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            IsConfirmed = false;
            this.Close(); // This will return control to MainWindow
        }
    }
}