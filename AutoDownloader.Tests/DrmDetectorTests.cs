using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers recognition of DRM-protected content (AD-054).
    ///
    /// The message below is verbatim from a real run against a streaming service. Before
    /// this, protected content was treated as an ordinary failure, so the app launched a
    /// headless browser for twelve seconds per episode to watch for media requests that an
    /// encrypted stream never makes - then reported "no media requests were observed",
    /// which hides the actual reason.
    /// </summary>
    [TestClass]
    public class DrmDetectorTests
    {
        [TestMethod]
        public void IsDrmMessage_RecognisesTheRealWorldMessage()
        {
            Assert.IsTrue(DrmDetector.IsDrmMessage(
                "ERROR: [tubitv] 200303773: This video is DRM protected"));
        }

        [TestMethod]
        [DataRow("This video is DRM protected")]
        [DataRow("the stream is drm-protected")]
        [DataRow("Content protected by DRM")]
        [DataRow("Widevine license required")]
        [DataRow("PlayReady protected content")]
        [DataRow("FairPlay stream")]
        public void IsDrmMessage_RecognisesProtectionSchemes(string line)
        {
            Assert.IsTrue(DrmDetector.IsDrmMessage(line));
        }

        [TestMethod]
        public void IsDrmMessage_IgnoresCase()
        {
            Assert.IsTrue(DrmDetector.IsDrmMessage("THIS VIDEO IS DRM PROTECTED"));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("[download] 100% of 123.45MiB in 00:01:23")]
        [DataRow("ERROR: Got HTTP Error 403 caused by Cloudflare anti-bot challenge")]
        [DataRow("ERROR: unable to download video data")]
        [DataRow("[generic] Extracting URL: https://example.com/watch/1")]
        public void IsDrmMessage_DoesNotFireOnOrdinaryOutput(string? line)
        {
            // A Cloudflare block in particular must NOT be read as DRM: that one is worth
            // retrying, and mislabelling it would hide a genuinely different problem.
            Assert.IsFalse(DrmDetector.IsDrmMessage(line));
        }
    }
}
