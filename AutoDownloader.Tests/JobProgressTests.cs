using AutoDownloader.Core;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the overall-progress calculation behind the status bar (AD-027).
    ///
    /// The bar reports progress across the whole job rather than the current file, because
    /// "62% through episode 7 of 12" is the useful number. Getting this wrong produces a bar
    /// that jumps backwards between episodes, which reads as a bug even when the download is
    /// fine.
    /// </summary>
    [TestClass]
    public class JobProgressTests
    {
        private static JobProgress At(int index, int count, double? filePercent) => new JobProgress
        {
            EpisodeIndex = index,
            EpisodeCount = count,
            File = filePercent.HasValue ? new DownloadProgress { Percent = filePercent } : null
        };

        [TestMethod]
        public void OverallPercent_IsZeroAtTheStartOfTheFirstEpisode()
        {
            Assert.AreEqual(0d, At(1, 10, 0).OverallPercent);
        }

        [TestMethod]
        public void OverallPercent_CountsCompletedEpisodesPlusTheCurrentOne()
        {
            // Episode 3 of 10, halfway through: 2 done + 0.5 = 2.5/10 = 25%.
            Assert.AreEqual(25d, At(3, 10, 50).OverallPercent);
        }

        [TestMethod]
        public void OverallPercent_ReachesOneHundredOnTheLastEpisode()
        {
            Assert.AreEqual(100d, At(10, 10, 100).OverallPercent);
        }

        [TestMethod]
        public void OverallPercent_NeverGoesBackwardsBetweenEpisodes()
        {
            // The end of episode 4 must not read higher than the start of episode 5.
            double endOfFour = At(4, 12, 100).OverallPercent!.Value;
            double startOfFive = At(5, 12, 0).OverallPercent!.Value;

            Assert.AreEqual(endOfFour, startOfFive,
                "A bar that jumps backwards between episodes reads as a bug.");
        }

        [TestMethod]
        public void OverallPercent_IncreasesMonotonicallyAcrossAWholeSeason()
        {
            double previous = -1;

            for (int episode = 1; episode <= 12; episode++)
            {
                foreach (var filePercent in new double[] { 0, 25, 50, 75, 100 })
                {
                    double current = At(episode, 12, filePercent).OverallPercent!.Value;
                    Assert.IsTrue(current >= previous,
                        $"Progress went backwards at episode {episode}, file {filePercent}%: {current} < {previous}");
                    previous = current;
                }
            }

            Assert.AreEqual(100d, previous);
        }

        [TestMethod]
        public void OverallPercent_IsNullWhenTheEpisodeCountIsUnknown()
        {
            // A single URL handed straight to yt-dlp has no episode count; the UI falls back
            // to the file's own percentage rather than showing a meaningless bar.
            Assert.IsNull(At(0, 0, 50).OverallPercent);
        }

        [TestMethod]
        public void OverallPercent_TreatsAMissingFileReadingAsZeroWithinTheEpisode()
        {
            // 2 episodes done out of 8, nothing known about the third yet.
            Assert.AreEqual(25d, At(3, 8, null).OverallPercent);
        }

        [TestMethod]
        public void OverallPercent_IsClampedToOneHundred()
        {
            // Defensive: a downloader reporting >100% must not overflow the bar.
            var progress = At(10, 10, 150);
            Assert.IsTrue(progress.OverallPercent <= 100d);
        }
    }
}
