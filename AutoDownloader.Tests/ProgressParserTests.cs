using AutoDownloader.Core;
using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers progress parsing (AD-027).
    ///
    /// Every sample below was captured from real yt-dlp 2026.08.19 and aria2 1.37.0 output
    /// against a local server, rather than invented. The two formats both matter: measured on
    /// a 6 MB file, aria2c as the external downloader produced 12 status lines while yt-dlp
    /// produced 1 (at 100%, after the fact). Parsing only yt-dlp gives a bar that sits at zero
    /// and then snaps to done.
    /// </summary>
    [TestClass]
    public class ProgressParserTests
    {
        // ------------------------------------------------------------------ aria2c

        [TestMethod]
        public void Parse_ReadsAnAria2cStatusLine()
        {
            var p = ProgressParser.Parse("[#ab02c4 1.0MiB/6.0MiB(16%) CN:4 DL:518KiB ETA:9s]");

            Assert.IsNotNull(p);
            Assert.AreEqual(ProgressSource.Aria2c, p!.Source);
            Assert.AreEqual(16d, p.Percent);
            Assert.AreEqual("1.0MiB", p.Downloaded);
            Assert.AreEqual("6.0MiB", p.Total);
            Assert.AreEqual("518KiB/s", p.Speed);
            Assert.AreEqual("9s", p.Eta);
        }

        [TestMethod]
        public void Parse_HandlesAria2cSummaryBannerOnTheSameLine()
        {
            // aria2c appends a banner to some status lines; it must not defeat the match.
            var p = ProgressParser.Parse(
                "[#ab02c4 2.0MiB/6.0MiB(33%) CN:4 DL:686KiB ETA:5s]          "
                + "*** Download Progress Summary as of Sun Sep 20 21:48:11 2026 ***");

            Assert.IsNotNull(p);
            Assert.AreEqual(33d, p!.Percent);
            Assert.AreEqual("5s", p.Eta);
        }

        [TestMethod]
        public void Parse_HandlesAria2cLineWithoutEta()
        {
            // ETA disappears when the rate is unknown or the transfer is finishing.
            var p = ProgressParser.Parse("[#ab02c4 16KiB/6.0MiB(0%) CN:4 DL:15KiB]");

            Assert.IsNotNull(p);
            Assert.AreEqual(0d, p!.Percent);
            Assert.IsNull(p.Eta);
        }

        [TestMethod]
        public void Parse_ReadsLongEtaFormats()
        {
            var p = ProgressParser.Parse("[#ab02c4 16KiB/6.0MiB(0%) CN:4 DL:15KiB ETA:6m24s]");

            Assert.AreEqual("6m24s", p!.Eta);
        }

        // ------------------------------------------------------------------ yt-dlp

        [TestMethod]
        public void Parse_ReadsAYtDlpTemplateLine()
        {
            var p = ProgressParser.Parse("@PROG@  4.2%|   1.99MiB/s|00:02|   258KiB|   6.00MiB");

            Assert.IsNotNull(p);
            Assert.AreEqual(ProgressSource.YtDlp, p!.Source);
            Assert.AreEqual(4.2d, p.Percent);
            Assert.AreEqual("1.99MiB/s", p.Speed);
            Assert.AreEqual("00:02", p.Eta);
            Assert.AreEqual("6.00MiB", p.Total);
        }

        [TestMethod]
        public void Parse_TreatsYtDlpPlaceholdersAsMissing()
        {
            // The first tick reports "Unknown B/s" and "Unknown" before a rate is established.
            var p = ProgressParser.Parse("@PROG@  0.0%| Unknown B/s|Unknown|   0KiB|   6.00MiB");

            Assert.IsNotNull(p);
            Assert.AreEqual(0d, p!.Percent);
            Assert.IsNull(p.Speed, "'Unknown B/s' is a placeholder, not a speed to display.");
            Assert.IsNull(p.Eta);
        }

        [TestMethod]
        public void Parse_TreatsNaEtaAsMissing()
        {
            var p = ProgressParser.Parse("@PROG@100.0%|68.66MiB/s|NA|   6.00MiB|   6.00MiB");

            Assert.AreEqual(100d, p!.Percent);
            Assert.IsNull(p.Eta, "'NA' is a placeholder, not an ETA to display.");
            Assert.AreEqual("68.66MiB/s", p.Speed);
        }

        // ------------------------------------------------------------------ non-progress

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("[download] Destination: Show - S01E01 - Pilot.mp4")]
        [DataRow("[generic] Extracting URL: https://example.com/watch/1")]
        [DataRow("[info] Writing video subtitles to: Show.en.srt")]
        [DataRow("ERROR: unable to download video data")]
        public void Parse_ReturnsNullForLinesThatAreNotProgress(string? line)
        {
            Assert.IsNull(ProgressParser.Parse(line));
        }

        [TestMethod]
        public void Parse_DoesNotMistakeAFilenameForProgress()
        {
            // A show title containing bracketed text must not be read as an aria2c line.
            Assert.IsNull(ProgressParser.Parse(
                "[download] Destination: Some Show [1080p] (2024) - S01E01.mp4"));
        }
    }
}
