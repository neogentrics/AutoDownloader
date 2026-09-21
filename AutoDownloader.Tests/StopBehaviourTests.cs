using AutoDownloader.Core;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers telling a stopped download apart from a failed one (AD-071).
    ///
    /// Pressing Stop once used to break downloading for the rest of the session: the service
    /// cancelled its token source and never replaced it, so every later episode was handed
    /// the cancelled one and threw before yt-dlp had started. Each then reported
    /// "Stopped by the user" and exit -1, which the orchestrator read as an ordinary failure
    /// and sent round the network-capture fallback - so the log blamed the user for a bug and
    /// spent twelve seconds per episode doing it.
    /// </summary>
    [TestClass]
    public class StopBehaviourTests
    {
        [TestMethod]
        public void AStoppedDownloadSaysSo()
        {
            var outcome = EpisodeDownloadOutcome.Stopped();

            Assert.IsTrue(outcome.Cancelled);
            Assert.IsFalse(outcome.Succeeded);
        }

        [TestMethod]
        public void AnOrdinaryFailureIsNotAStop()
        {
            // The distinction is the whole point: a failure is worth retrying, a stop is not.
            var outcome = EpisodeDownloadOutcome.Failed();

            Assert.IsFalse(outcome.Cancelled);
            Assert.IsFalse(outcome.Succeeded);
        }

        [TestMethod]
        public void ASuccessIsNeitherStoppedNorProtected()
        {
            var outcome = new EpisodeDownloadOutcome { ExitCode = 0 };

            Assert.IsTrue(outcome.Succeeded);
            Assert.IsFalse(outcome.Cancelled);
            Assert.IsFalse(outcome.DrmProtected);
        }
    }
}
