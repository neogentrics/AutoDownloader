using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers series-slug extraction, which is what stops the indexer collecting other
    /// titles' episodes (AD-050).
    ///
    /// Measured on a real 12-episode listing page: taking every numbered link on the page
    /// gave 59, because sidebars and "recently added" rails are full of other series'
    /// episode links numbered exactly the same way.
    /// </summary>
    [TestClass]
    public class SeriesIndexerSlugTests
    {
        private static string? Slug(string url) =>
            SeriesIndexer.ExtractSeriesSlug(new System.Uri(url));

        [TestMethod]
        public void ExtractSeriesSlug_IgnoresStructuralSegmentsAndQueryStrings()
        {
            Assert.AreEqual("ben-to", Slug("https://example.com/anime/ben-to/?season=all&lang=dub"));
        }

        [TestMethod]
        [DataRow("https://example.com/series/some-great-show", "some-great-show")]
        [DataRow("https://example.com/show/some-great-show/season-2", "some-great-show")]
        [DataRow("https://example.com/tv/some-great-show/s03", "some-great-show")]
        [DataRow("https://example.com/anime/some-great-show/season/4/", "some-great-show")]
        public void ExtractSeriesSlug_SkipsSeasonAndContainerSegments(string url, string expected)
        {
            Assert.AreEqual(expected, Slug(url));
        }

        [TestMethod]
        public void ExtractSeriesSlug_SkipsBareNumericIds()
        {
            // "300001" is a series id, not a name.
            Assert.AreEqual("some-great-show", Slug("https://tubitv.com/series/300001/some-great-show"));
        }

        [TestMethod]
        [DataRow("https://example.com/")]
        [DataRow("https://example.com/anime/")]
        [DataRow("https://example.com/tv/ab")]
        public void ExtractSeriesSlug_ReturnsNullWhenNothingDistinctiveExists(string url)
        {
            // Too short or absent: filtering on it would match everything and help nobody.
            Assert.IsNull(Slug(url));
        }
    }
}
