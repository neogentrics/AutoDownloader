using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the "this episode is already downloaded" check (AD-061).
    ///
    /// The point of the check is to stop a re-run quietly re-downloading a library, without
    /// making it impossible to deliberately replace a file. Both halves matter, so both are
    /// tested: what counts as the same episode, and what does not.
    /// </summary>
    [TestClass]
    public class ExistingEpisodeTests
    {
        private string _root = string.Empty;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), $"existing-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_root, "Season 01"));
        }

        [TestCleanup]
        public void TearDown()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private string WriteEpisode(string fileName, int bytes = 1024)
        {
            string path = Path.Combine(_root, "Season 01", fileName);
            File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        [TestMethod]
        public void NothingOnDisk_ReturnsNull()
        {
            Assert.IsNull(YtDlpService.FindExistingEpisode("Ben-To", 1, 3, "Rice", _root));
        }

        [TestMethod]
        public void MissingSeasonFolder_ReturnsNull()
        {
            // Season 02 has never been downloaded, so there is no folder at all. This must be
            // an ordinary "no" rather than a directory-not-found throw.
            Assert.IsNull(YtDlpService.FindExistingEpisode("Ben-To", 2, 1, null, _root));
        }

        [TestMethod]
        public void ExactMatch_IsFound()
        {
            string path = WriteEpisode("Ben-To - S01E03 - Rice.mp4", 65_000_000);

            var found = YtDlpService.FindExistingEpisode("Ben-To", 1, 3, "Rice", _root);

            Assert.IsNotNull(found);
            Assert.AreEqual(path, found!.Path);
            Assert.AreEqual(65_000_000, found.SizeBytes);
            Assert.AreEqual("S01E03", found.EpisodeLabel);
        }

        [TestMethod]
        public void DifferentEpisodeTitle_StillCountsAsTheSameEpisode()
        {
            // The whole reason the match is on the S__E__ prefix: metadata can rename an
            // episode between runs, and a title-sensitive check would download it twice.
            WriteEpisode("Ben-To - S01E03 - An Older Title.mkv");

            var found = YtDlpService.FindExistingEpisode("Ben-To", 1, 3, "A Newer Title", _root);

            Assert.IsNotNull(found);
        }

        [TestMethod]
        public void DifferentEpisodeNumber_IsNotAMatch()
        {
            WriteEpisode("Ben-To - S01E03 - Rice.mp4");

            Assert.IsNull(YtDlpService.FindExistingEpisode("Ben-To", 1, 4, "Rice", _root));
        }

        [TestMethod]
        public void DifferentShow_IsNotAMatch()
        {
            WriteEpisode("Ben-To - S01E03 - Rice.mp4");

            Assert.IsNull(YtDlpService.FindExistingEpisode("Teen Titans", 1, 3, "Rice", _root));
        }

        [TestMethod]
        public void PartialDownloads_AreNotTreatedAsFinishedFiles()
        {
            // yt-dlp leaves .part files behind when a download is interrupted. Counting one as
            // "already downloaded" would strand the episode as a permanent partial.
            WriteEpisode("Ben-To - S01E03 - Rice.mp4.part");

            Assert.IsNull(YtDlpService.FindExistingEpisode("Ben-To", 1, 3, "Rice", _root));
        }

        [TestMethod]
        public void SubtitlesAndArtwork_AreNotMistakenForTheVideo()
        {
            WriteEpisode("Ben-To - S01E03 - Rice.en.srt");
            WriteEpisode("Ben-To - S01E03 - Rice.jpg");

            Assert.IsNull(YtDlpService.FindExistingEpisode("Ben-To", 1, 3, "Rice", _root));
        }

        [TestMethod]
        public void AnyVideoContainer_Counts()
        {
            WriteEpisode("Ben-To - S01E03 - Rice.webm");

            Assert.IsNotNull(YtDlpService.FindExistingEpisode("Ben-To", 1, 3, "Rice", _root));
        }

        [TestMethod]
        public void ShowTitlesWithAwkwardCharacters_AreMatchedAsWritten()
        {
            // The file on disk was written through the same sanitising the lookup uses, so a
            // colon in the show name must not stop it being found again.
            WriteEpisode("Megaman Star Force_ Tribe - S01E01 - Pilot.mp4");

            var found = YtDlpService.FindExistingEpisode(
                "Megaman Star Force: Tribe", 1, 1, "Pilot", _root);

            Assert.IsNotNull(found);
        }

        [TestMethod]
        public void SizeDisplay_IsReadable()
        {
            WriteEpisode("Ben-To - S01E03 - Rice.mp4", 65_200_128);

            var found = YtDlpService.FindExistingEpisode("Ben-To", 1, 3, "Rice", _root);

            Assert.IsNotNull(found);
            StringAssert.Contains(found!.SizeDisplay, "MB");
        }

        [TestMethod]
        public void UnattendedRuns_KeepWhatIsAlreadyThere()
        {
            // A scheduled watch-list run has nobody to answer the question. The answer it
            // gives itself must be the one that destroys nothing.
            var decision = new AutoConfirmPrompt()
                .ConfirmOverwriteAsync(new ExistingEpisode { Path = "x.mp4" })
                .GetAwaiter().GetResult();

            Assert.AreEqual(OverwriteDecision.SkipAll, decision);
        }
    }
}
