using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Counts down to a default answer, and gets out of the way the moment somebody is
    /// clearly there.
    ///
    /// The timer exists so an unattended run still makes progress. It should never be a
    /// deadline for a person who is actually reading the dialog - comparing two versions of
    /// a show and checking their years takes longer than any countdown worth having, and
    /// having the window answer itself mid-decision is worse than it never having been
    /// automatic at all.
    ///
    /// So the first sign of a person - a key, a click, a scroll, anywhere in the window -
    /// cancels it permanently rather than merely restarting it.
    /// </summary>
    public sealed class AutoAnswerTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly Window _window;
        private readonly Action<int> _onTick;
        private readonly Action _onElapsed;

        private int _remaining;
        private bool _cancelled;

        public bool IsCancelled => _cancelled;

        /// <param name="onTick">Called each second with the seconds remaining, to label the button.</param>
        /// <param name="onElapsed">Called once if the countdown reaches zero untouched.</param>
        public AutoAnswerTimer(Window window, int seconds, Action<int> onTick, Action onElapsed)
        {
            _window = window;
            _remaining = seconds;
            _onTick = onTick;
            _onElapsed = onElapsed;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Tick;

            // Preview events, so interaction with any control inside the window counts, not
            // just the window's own background.
            _window.PreviewKeyDown += Interacted;
            _window.PreviewMouseDown += Interacted;
            _window.PreviewMouseWheel += Interacted;
        }

        public void Start() => _timer.Start();

        public void Stop()
        {
            _timer.Stop();
            Detach();
        }

        /// <summary>
        /// Stops the countdown for good. Called on the first interaction, and safe to call
        /// repeatedly.
        /// </summary>
        public void Cancel()
        {
            if (_cancelled) return;

            _cancelled = true;
            _timer.Stop();
            Detach();
            _onTick(-1);
        }

        private void Detach()
        {
            _window.PreviewKeyDown -= Interacted;
            _window.PreviewMouseDown -= Interacted;
            _window.PreviewMouseWheel -= Interacted;
        }

        private void Interacted(object sender, EventArgs e) => Cancel();

        private void Tick(object? sender, EventArgs e)
        {
            _remaining--;
            _onTick(_remaining);

            if (_remaining <= 0)
            {
                Stop();
                _onElapsed();
            }
        }
    }
}
