using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers combined season+episode detection (AD-052).
    ///
    /// Without a season, a listing page covering five seasons was flattened into one: 65 links
    /// filed as S01E01 through S01E65. The season is usually sitting in the URL already.
    /// </summary>
    [TestClass]
    public class SeasonEpisodeDetectionTests
    {
        [TestMethod]
        [DataRow("https://tubitv.com/tv-shows/200303772/s01-e01-final-exam", 1, 1)]
        [DataRow("https://example.com/show/s02-e07-the-one-with-it", 2, 7)]
        [DataRow("https://example.com/watch/S03E12", 3, 12)]
        [DataRow("https://example.com/season-2-episode-5-title", 2, 5)]
        public void DetectSeasonAndEpisode_ReadsBothFromTheUrl(string url, int season, int episode)
        {
            var (s, e) = SeriesIndexer.DetectSeasonAndEpisode(null, url);

            Assert.AreEqual(season, s, "season");
            Assert.AreEqual(episode, e, "episode");
        }

        [TestMethod]
        public void DetectSeasonAndEpisode_PrefersLinkTextWhenItCarriesBoth()
        {
            var (s, e) = SeriesIndexer.DetectSeasonAndEpisode(
                "S02E07 - The One With It", "https://example.com/some-other-path");

            Assert.AreEqual(2, s);
            Assert.AreEqual(7, e);
        }

        [TestMethod]
        public void DetectSeasonAndEpisode_ReturnsEpisodeOnlyWhenThereIsNoSeason()
        {
            // The common single-season case, e.g. wcoanime-style slugs.
            var (s, e) = SeriesIndexer.DetectSeasonAndEpisode(
                null, "https://example.com/ben-to-episode-7-english-dubbed");

            Assert.IsNull(s, "no season is stated, so none should be invented");
            Assert.AreEqual(7, e);
        }

        [TestMethod]
        public void DetectSeasonAndEpisode_ReturnsNothingForNonEpisodeLinks()
        {
            var (s, e) = SeriesIndexer.DetectSeasonAndEpisode("Contact Us", "https://example.com/contact");

            Assert.IsNull(s);
            Assert.IsNull(e);
        }

        [TestMethod]
        public void DetectSeasonAndEpisode_DoesNotReadASeriesIdAsASeason()
        {
            // "200303772" must not become a season number.
            var (s, e) = SeriesIndexer.DetectSeasonAndEpisode(null, "https://tubitv.com/tv-shows/200303772/");

            Assert.IsNull(s);
            Assert.IsNull(e);
        }
    }
}
