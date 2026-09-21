using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers episode-number detection. This is what lets a site that publishes no metadata
    /// still produce correctly numbered files, so a regression here silently mislabels an
    /// entire season rather than failing loudly.
    /// </summary>
    [TestClass]
    public class SeriesIndexerTests
    {
        [TestMethod]
        [DataRow("Episode 7", 7)]
        [DataRow("episode 12", 12)]
        [DataRow("Ep 3", 3)]
        [DataRow("EP-07", 7)]
        [DataRow("Episode_21", 21)]
        [DataRow("Watch Some Show Episode 104 English Dub", 104)]
        public void DetectEpisodeNumber_ReadsTheNumberFromLinkText(string linkText, int expected)
        {
            Assert.AreEqual(expected, SeriesIndexer.DetectEpisodeNumber(linkText, "https://example.com/x"));
        }

        [TestMethod]
        public void DetectEpisodeNumber_FallsBackToTheUrlSlug()
        {
            int? result = SeriesIndexer.DetectEpisodeNumber(
                linkText: "",
                url: "https://example.com/phi-brain-puzzle-of-god-episode-1-english-dubbed");

            Assert.AreEqual(1, result);
        }

        [TestMethod]
        public void DetectEpisodeNumber_PrefersLinkTextOverTheUrl()
        {
            // The slug carries a trailing mirror number; the visible text is authoritative.
            int? result = SeriesIndexer.DetectEpisodeNumber(
                linkText: "Episode 5",
                url: "https://example.com/some-show-episode-9-dubbed");

            Assert.AreEqual(5, result);
        }

        [TestMethod]
        [DataRow("Home")]
        [DataRow("Contact Us")]
        [DataRow("Latest Releases")]
        public void DetectEpisodeNumber_ReturnsNullForSiteChrome(string linkText)
        {
            // Navigation links must not be mistaken for episodes, or they pad the season.
            Assert.IsNull(SeriesIndexer.DetectEpisodeNumber(linkText, "https://example.com/home"));
        }

        [TestMethod]
        public void DetectEpisodeNumber_IgnoresZero()
        {
            Assert.IsNull(SeriesIndexer.DetectEpisodeNumber("Episode 0", "https://example.com/x"));
        }
    }
}
