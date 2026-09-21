using AutoDownloader.Core;
using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers how a listing page's items are picked out when nothing is numbered (AD-059).
    ///
    /// The previous approach matched a keyword list - "/watch/", "/episode", "/ep-",
    /// "/video/" - and so found nothing on a page whose items lived under "/cartoon/". The
    /// links were ordinary anchors; the heuristic simply did not know that site's word for
    /// them. Every site invents its own, so structure is the more reliable signal: a listing
    /// repeats one path prefix many times, while navigation is few and scattered.
    /// </summary>
    [TestClass]
    public class LinkGroupingTests
    {
        private static List<EpisodeLink> Links(params string[] urls) =>
            urls.Select(u => new EpisodeLink { Url = u }).ToList();

        [TestMethod]
        public void PicksTheRepeatedItemPathOverNavigation()
        {
            var candidates = Links(
                "https://example.net/",
                "https://example.net/series/",
                "https://example.net/privacy/",
                "https://example.net/cartoon/one/",
                "https://example.net/cartoon/two/",
                "https://example.net/cartoon/three/",
                "https://example.net/cartoon/four/");

            var chosen = SeriesIndexer.SelectLargestLinkGroup(
                candidates, "/serie/the-show/", out string? segment);

            Assert.AreEqual("cartoon", segment);
            Assert.AreEqual(4, chosen.Count);
        }

        [TestMethod]
        public void IgnoresAGroupTooSmallToBeAListing()
        {
            // Two links sharing a path is as likely to be a menu as a listing.
            var candidates = Links(
                "https://example.net/about/us/",
                "https://example.net/about/contact/");

            var chosen = SeriesIndexer.SelectLargestLinkGroup(candidates, "/serie/x/", out _);

            Assert.AreEqual(0, chosen.Count);
        }

        [TestMethod]
        public void PrefersItemsOverOtherSeriesUnderTheSamePathAsThePage()
        {
            // Links sharing the series page's own segment are usually sibling series, not
            // this page's episodes.
            var candidates = Links(
                "https://example.net/serie/another-show/",
                "https://example.net/serie/a-third-show/",
                "https://example.net/serie/yet-another/",
                "https://example.net/cartoon/one/",
                "https://example.net/cartoon/two/",
                "https://example.net/cartoon/three/");

            var chosen = SeriesIndexer.SelectLargestLinkGroup(
                candidates, "/serie/the-show/", out string? segment);

            Assert.AreEqual("cartoon", segment);
            Assert.AreEqual(3, chosen.Count);
        }

        [TestMethod]
        public void FallsBackToTheSeriesPathWhenNothingElseQualifies()
        {
            // If the only sizeable group is under the page's own segment, use it rather than
            // reporting nothing at all.
            var candidates = Links(
                "https://example.net/serie/one/",
                "https://example.net/serie/two/",
                "https://example.net/serie/three/",
                "https://example.net/about/");

            var chosen = SeriesIndexer.SelectLargestLinkGroup(
                candidates, "/serie/the-show/", out string? segment);

            Assert.AreEqual("serie", segment);
            Assert.AreEqual(3, chosen.Count);
        }

        [TestMethod]
        public void ChoosesTheLargestGroupWhenSeveralQualify()
        {
            var candidates = Links(
                "https://example.net/clip/a/", "https://example.net/clip/b/", "https://example.net/clip/c/",
                "https://example.net/video/1/", "https://example.net/video/2/",
                "https://example.net/video/3/", "https://example.net/video/4/");

            var chosen = SeriesIndexer.SelectLargestLinkGroup(candidates, "/show/x/", out string? segment);

            Assert.AreEqual("video", segment);
            Assert.AreEqual(4, chosen.Count);
        }

        [TestMethod]
        public void HandlesAnEmptyPageWithoutThrowing()
        {
            Assert.AreEqual(0, SeriesIndexer.SelectLargestLinkGroup(
                new List<EpisodeLink>(), "/serie/x/", out _).Count);
        }
    }
}
