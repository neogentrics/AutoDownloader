using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers how a job result distinguishes a failed download from an incomplete source
    /// (AD-057).
    ///
    /// From a real run: a source listed 9 episodes where TMDB expected 26. Measuring only
    /// against the database meant downloading everything available still reported seventeen
    /// episodes "missing", which reads as the app failing rather than the source being short.
    /// </summary>
    [TestClass]
    public class VerificationReportingTests
    {
        private static DownloadJobResult Result(int offered, int expected, int present, int added = 0) =>
            new DownloadJobResult
            {
                EpisodesOffered = offered,
                ExpectedEpisodeCount = expected,
                FilesPresentAfter = present,
                FilesAdded = added,
                Completed = true
            };

        [TestMethod]
        public void ASourceShorterThanTheSeasonIsNotAFailedDownload()
        {
            // The real case: got all 9 the source had; the databases list 26.
            var result = Result(offered: 9, expected: 26, present: 9, added: 9);

            Assert.AreEqual(result.EpisodesOffered, result.FilesPresentAfter,
                "everything the source offered was obtained");
            Assert.IsTrue(result.ExpectedEpisodeCount > result.EpisodesOffered,
                "and the shortfall belongs to the source, not the run");
        }

        [TestMethod]
        public void AGenuinelyIncompleteDownloadIsStillVisible()
        {
            // 11 of 13 offered: this one really did miss two.
            var result = Result(offered: 13, expected: 55, present: 11, added: 11);

            Assert.IsTrue(result.FilesPresentAfter < result.EpisodesOffered,
                "a real shortfall against what the source offered must remain detectable");
        }

        [TestMethod]
        public void OfferedAndExpectedAreTrackedSeparately()
        {
            var result = Result(offered: 13, expected: 55, present: 11);

            Assert.AreEqual(13, result.EpisodesOffered);
            Assert.AreEqual(55, result.ExpectedEpisodeCount);
        }

        [TestMethod]
        public void ASourceListingMoreThanTheSeasonIsAlsoRecognisable()
        {
            // A page covering specials, or more than one season.
            var result = Result(offered: 65, expected: 13, present: 13);

            Assert.IsTrue(result.EpisodesOffered > result.ExpectedEpisodeCount);
        }

        [TestMethod]
        public void NoOfferedCountFallsBackToTheDatabaseComparison()
        {
            // When no episode list could be built, the database count is all there is.
            var result = Result(offered: 0, expected: 12, present: 0);

            Assert.AreEqual(0, result.EpisodesOffered);
            Assert.AreEqual(12, result.ExpectedEpisodeCount);
        }

        [TestMethod]
        public void ProtectedEpisodesAreCountedApartFromFailures()
        {
            var result = Result(offered: 13, expected: 13, present: 0);
            result.EpisodesProtected = 13;

            Assert.AreEqual(0, result.EpisodesFailed,
                "DRM is not a failure to retry; it is a source that cannot be downloaded");
            Assert.AreEqual(13, result.EpisodesProtected);
        }
    }
}
