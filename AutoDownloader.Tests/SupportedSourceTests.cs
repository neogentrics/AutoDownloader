using AutoDownloader.Services;
using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers knowing whether a source is workable, and reaching supported players embedded
    /// in pages that are not themselves supported (AD-056).
    ///
    /// Prompted by a run that spent a minute on metadata, indexing and a headless render
    /// before ending in "Unsupported URL" - then handed the same URL to yt-dlp a second time
    /// for an identical failure.
    /// </summary>
    [TestClass]
    public class SupportedSourceTests
    {
        [TestMethod]
        [DataRow("https://www.youtube.com/watch?v=abc", "youtube")]
        [DataRow("https://tubitv.com/series/1/x", "tubitv")]
        [DataRow("https://toonplay.in/watch/series-black-torch", "toonplay")]
        [DataRow("https://player.vimeo.com/video/123", "vimeo")]
        public void HostLabels_ReducesAHostToItsDistinctivePart(string url, string expected)
        {
            CollectionAssert.Contains(SupportedSiteChecker.HostLabels(url).ToList(), expected);
        }

        [TestMethod]
        public void IsUnsupportedUrlMessage_RecognisesTheRealMessage()
        {
            Assert.IsTrue(SupportedSiteChecker.IsUnsupportedUrlMessage(
                "ERROR: Unsupported URL: https://toonplay.in/watch/series-black-torch"));
        }

        [TestMethod]
        [DataRow("ERROR: [youtube] abc: Video unavailable")]
        [DataRow("[download] 100% of 12.00MiB")]
        [DataRow(null)]
        public void IsUnsupportedUrlMessage_DoesNotFireOnOtherFailures(string? line)
        {
            Assert.IsFalse(SupportedSiteChecker.IsUnsupportedUrlMessage(line));
        }

        // ------------------------------------------------------------------ embedded players

        [TestMethod]
        public void Find_LocatesAYouTubeIframe()
        {
            const string html = """
                <html><body><h1>Lecture 3</h1>
                <iframe width="560" height="315"
                        src="https://www.youtube.com/embed/dQw4w9WgXcQ"></iframe>
                </body></html>
                """;

            var found = new EmbeddedPlayerFinder().Find(html, "https://example.edu/lectures/3");

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("YouTube", found[0].Platform);
            StringAssert.Contains(found[0].Url, "dQw4w9WgXcQ");
        }

        [TestMethod]
        public void Find_HandlesProtocolRelativeEmbeds()
        {
            // Very common in older markup, and easy to mishandle into a relative path.
            const string html = """<iframe src="//player.vimeo.com/video/76979871"></iframe>""";

            var found = new EmbeddedPlayerFinder().Find(html, "https://example.com/post");

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("Vimeo", found[0].Platform);
            StringAssert.StartsWith(found[0].Url, "https://");
        }

        [TestMethod]
        public void Find_ReadsLazyLoadedPlayers()
        {
            // A lazy player keeps the real URL out of src until it is scrolled into view.
            const string html = """<iframe data-src="https://www.dailymotion.com/embed/video/x7tgad0"></iframe>""";

            var found = new EmbeddedPlayerFinder().Find(html, "https://example.com/");

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("Dailymotion", found[0].Platform);
        }

        [TestMethod]
        public void Find_ReadsOpenGraphVideoTags()
        {
            const string html = """
                <html><head>
                <meta property="og:video" content="https://archive.org/embed/some-item">
                </head><body></body></html>
                """;

            var found = new EmbeddedPlayerFinder().Find(html, "https://example.com/");

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("Internet Archive", found[0].Platform);
        }

        [TestMethod]
        public void Find_IgnoresEmbedsFromPlatformsWithNoExtractor()
        {
            const string html = """
                <iframe src="https://ads.example.net/banner"></iframe>
                <iframe src="https://analytics.example.com/pixel"></iframe>
                """;

            Assert.AreEqual(0, new EmbeddedPlayerFinder().Find(html, "https://example.com/").Count);
        }

        [TestMethod]
        public void Find_DeduplicatesTheSameEmbedAppearingTwice()
        {
            const string html = """
                <iframe src="https://www.youtube.com/embed/abc123"></iframe>
                <a href="https://www.youtube.com/embed/abc123">Watch</a>
                """;

            Assert.AreEqual(1, new EmbeddedPlayerFinder().Find(html, "https://example.com/").Count);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("<html><body>no video here</body></html>")]
        public void Find_ReturnsNothingWhenThereIsNothingToFind(string? html)
        {
            Assert.AreEqual(0, new EmbeddedPlayerFinder().Find(html, "https://example.com/").Count);
        }
    }
}
