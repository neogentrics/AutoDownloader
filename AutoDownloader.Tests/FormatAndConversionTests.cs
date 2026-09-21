using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers format selection and conversion planning (AD-055).
    ///
    /// Both come from the same real finding: a YouTube playlist downloaded as AV1 video and
    /// Opus audio in WebM, which is superb compression and close to unplayable on a TV. The
    /// same videos were also offered as H.264/AAC in MP4 at the identical resolution.
    /// </summary>
    [TestClass]
    public class FormatAndConversionTests
    {
        [TestMethod]
        public void Resolve_DefaultsToTheCompatibleSelector()
        {
            string selector = VideoFormatPreference.Resolve(null, null);

            StringAssert.Contains(selector, "avc1", "H.264 should be preferred by default");
            StringAssert.Contains(selector, "mp4a", "AAC should be preferred by default");
            StringAssert.Contains(selector, "/best", "there must be a fallback so odd sources still download");
        }

        [TestMethod]
        public void Resolve_BestQualityDoesNotConstrainCodecs()
        {
            string selector = VideoFormatPreference.Resolve(VideoFormatPreference.BestQuality, null);

            Assert.AreEqual("bestvideo+bestaudio/best", selector);
        }

        [TestMethod]
        public void Resolve_CustomUsesTheUsersOwnString()
        {
            Assert.AreEqual("bestvideo[height<=720]",
                VideoFormatPreference.Resolve(VideoFormatPreference.Custom, "bestvideo[height<=720]"));
        }

        [TestMethod]
        public void Resolve_CustomFallsBackWhenNoStringWasGiven()
        {
            // An empty custom format would make yt-dlp download nothing useful.
            StringAssert.Contains(VideoFormatPreference.Resolve(VideoFormatPreference.Custom, "  "), "avc1");
        }

        [TestMethod]
        public void PrefersMp4_OnlyForTheCompatiblePreference()
        {
            Assert.IsTrue(VideoFormatPreference.PrefersMp4(VideoFormatPreference.Compatible));
            Assert.IsFalse(VideoFormatPreference.PrefersMp4(VideoFormatPreference.BestQuality));
            Assert.IsFalse(VideoFormatPreference.PrefersMp4(VideoFormatPreference.Custom));
        }

        // ------------------------------------------------------------------ conversion planning

        [TestMethod]
        public void Plan_RemuxesWhenOnlyTheContainerIsWrong()
        {
            // H.264/AAC in an MKV: seconds of work, byte-identical video.
            var plan = MediaConversionPlanner.Plan(".mkv", "h264", "aac");

            Assert.IsFalse(plan.AlreadyCorrect);
            Assert.IsTrue(plan.IsLossless, "no stream needs re-encoding here");
            StringAssert.Contains(plan.Cost, "remux");
        }

        [TestMethod]
        public void Plan_SkipsAFileThatIsAlreadyCorrect()
        {
            var plan = MediaConversionPlanner.Plan(".mp4", "h264", "aac");

            Assert.IsTrue(plan.AlreadyCorrect);
        }

        [TestMethod]
        public void Plan_ReEncodesBothStreamsForTheRealWorldCase()
        {
            // Exactly what the YouTube run produced.
            var plan = MediaConversionPlanner.Plan(".webm", "av1", "opus");

            Assert.AreEqual(StreamAction.Encode, plan.Video);
            Assert.AreEqual(StreamAction.Encode, plan.Audio);
            Assert.IsFalse(plan.IsLossless);
            StringAssert.Contains(plan.Cost, "slow");
        }

        [TestMethod]
        public void Plan_CopiesTheVideoWhenOnlyTheAudioIsIncompatible()
        {
            // The cheap middle case: keep the picture untouched, re-encode only the sound.
            var plan = MediaConversionPlanner.Plan(".mkv", "h264", "opus");

            Assert.AreEqual(StreamAction.Copy, plan.Video);
            Assert.AreEqual(StreamAction.Encode, plan.Audio);
            StringAssert.Contains(plan.Cost, "audio re-encode");
        }

        [TestMethod]
        [DataRow("avc1.4d401f", "mp4a.40.2")]
        [DataRow("AVC1", "MP4A")]
        public void Plan_UnderstandsCodecTagsAsWellAsNames(string video, string audio)
        {
            var plan = MediaConversionPlanner.Plan(".mp4", video, audio);

            Assert.IsTrue(plan.AlreadyCorrect, "avc1/mp4a are h264/aac by another name");
        }

        [TestMethod]
        public void Plan_TreatsAv1AndVp9AsNeedingConversionDespiteBeingLegalInMp4()
        {
            // Legal in the container, but not hardware-decoded by most TVs and players.
            Assert.AreEqual(StreamAction.Encode, MediaConversionPlanner.Plan(".mp4", "av1", "aac").Video);
            Assert.AreEqual(StreamAction.Encode, MediaConversionPlanner.Plan(".mp4", "vp9", "aac").Video);
        }

        // ------------------------------------------------------------------ downloader retry

        [TestMethod]
        [DataRow("ERROR: aria2c exited with code 22")]
        [DataRow("  -> [HttpSkipResponseCommand.cc:239] errorCode=22 The response status is not successful. status=403")]
        public void IsExternalDownloaderFailure_RecognisesAria2cBeingRejected(string line)
        {
            Assert.IsTrue(YtDlpService.IsExternalDownloaderFailure(line));
        }

        [TestMethod]
        [DataRow("ERROR: [youtube] Oor-qtuyY6c: Video unavailable")]
        [DataRow("ERROR: [tubitv] 200303773: This video is DRM protected")]
        [DataRow("[download] 100% of 62.23MiB")]
        [DataRow(null)]
        public void IsExternalDownloaderFailure_DoesNotFireOnRealFailures(string? line)
        {
            // Retrying these without aria2c would waste time and change nothing.
            Assert.IsFalse(YtDlpService.IsExternalDownloaderFailure(line));
        }
    }
}
