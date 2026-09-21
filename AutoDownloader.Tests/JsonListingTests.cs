using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers reading an episode list out of the JSON a page fetched (AD-067).
    ///
    /// A single-page app renders its episode tiles from an API response and gives them no
    /// href at all - clicking is handled in script. A real Food Network season page carried
    /// two anchors in a 750 KB document while the API behind it listed seventeen episodes.
    /// </summary>
    [TestClass]
    public class JsonListingTests
    {
        [TestMethod]
        public void PathsInJson_BecomeAnchors()
        {
            string json = "{\"items\":[{\"path\":\"/video/some-show/first-one\"},"
                        + "{\"path\":\"/video/some-show/second-one\"}]}";

            string html = SeriesIndexer.BuildHtmlFromJsonPaths(new[] { json });

            StringAssert.Contains(html, "/video/some-show/first-one");
            StringAssert.Contains(html, "/video/some-show/second-one");
        }

        [TestMethod]
        public void SingleSegmentValues_AreNotMistakenForPaths()
        {
            // "/" alone, or a lone word, is not a page. Treating them as links would flood
            // the grouping with noise.
            string json = "{\"a\":\"/\",\"b\":\"/home\",\"c\":\"plain\",\"d\":\"/video/show/ep\"}";

            string html = SeriesIndexer.BuildHtmlFromJsonPaths(new[] { json });

            StringAssert.Contains(html, "/video/show/ep");
            Assert.IsFalse(html.Contains("\"plain\""));
        }

        [TestMethod]
        public void DuplicatePaths_AppearOnce()
        {
            string json = "{\"a\":\"/video/show/ep\",\"b\":\"/video/show/ep\"}";

            string html = SeriesIndexer.BuildHtmlFromJsonPaths(new[] { json });

            int occurrences = html.Split("/video/show/ep").Length - 1;

            // Once as the href, once as the link text - but not twice over.
            Assert.AreEqual(2, occurrences);
        }

        [TestMethod]
        public void SeveralResponses_AreCombined()
        {
            string html = SeriesIndexer.BuildHtmlFromJsonPaths(new[]
            {
                "{\"a\":\"/video/show/one\"}",
                "{\"a\":\"/video/show/two\"}",
            });

            StringAssert.Contains(html, "/video/show/one");
            StringAssert.Contains(html, "/video/show/two");
        }

        [TestMethod]
        public void NothingUsable_ProducesNothing()
        {
            Assert.AreEqual(string.Empty,
                SeriesIndexer.BuildHtmlFromJsonPaths(new[] { "{\"count\":17}", "", "not json at all" }));
        }

        [TestMethod]
        public void NoResponses_IsNotAProblem()
        {
            Assert.AreEqual(string.Empty, SeriesIndexer.BuildHtmlFromJsonPaths(new string[0]));
        }
    }
}
