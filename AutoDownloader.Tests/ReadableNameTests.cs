using AutoDownloader.Core;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers naming a link the databases do not cover (AD-102).
    ///
    /// The scraper does not always find anchor text, and the link text is then the URL path.
    /// Used whole it produced
    /// "S01E05 - _video_be-my-guest-with-ina-garten-food-network-atve-us_jon-batiste.mp4".
    /// </summary>
    [TestClass]
    public class ReadableNameTests
    {
        private static EpisodeLink Link(string url, string? text = null) =>
            new EpisodeLink { Url = url, LinkText = text };

        private const string Show = "https://watch.foodnetwork.com/video/be-my-guest-with-ina-garten-food-network-atve-us";

        [TestMethod]
        public void APathForLinkText_BecomesTheLastSegment()
        {
            var link = Link(
                Show + "/jon-batiste",
                "/video/be-my-guest-with-ina-garten-food-network-atve-us/jon-batiste");

            Assert.AreEqual("Jon Batiste", DownloadOrchestrator.ReadableName(link));
        }

        [TestMethod]
        public void NoLinkTextAtAll_FallsBackToTheUrlSlug()
        {
            Assert.AreEqual("Hoda Kotb", DownloadOrchestrator.ReadableName(Link(Show + "/hoda-kotb")));
        }

        [TestMethod]
        public void RealAnchorText_IsKeptAsWritten()
        {
            var link = Link(Show + "/jon-batiste", "Jon Batiste Drops By");

            Assert.AreEqual("Jon Batiste Drops By", DownloadOrchestrator.ReadableName(link));
        }

        [TestMethod]
        public void SmallWordsStayLowerUnlessTheyOpenTheName()
        {
            Assert.AreEqual(
                "Rob Marshall and John Deluca",
                DownloadOrchestrator.ReadableName(Link(Show + "/rob-marshall-and-john-deluca")));

            Assert.AreEqual(
                "The Trial of Duck Dodgers",
                DownloadOrchestrator.ReadableName(Link("https://example.com/v/the-trial-of-duck-dodgers")));
        }

        [TestMethod]
        public void AFileExtensionIsNotPartOfTheName()
        {
            Assert.AreEqual(
                "Episode One",
                DownloadOrchestrator.ReadableName(Link("https://example.com/v/episode-one.html")));
        }

        [TestMethod]
        public void SomethingUnusableGivesNothingRatherThanRubbish()
        {
            Assert.IsNull(DownloadOrchestrator.ReadableName(Link("https://example.com/")));
            Assert.IsNull(DownloadOrchestrator.ReadableName(Link(string.Empty, "   ")));
        }

        [TestMethod]
        public void AnAbsurdlyLongNameIsRefused()
        {
            var link = Link("https://example.com/v/x", new string('a', 200));

            Assert.IsNull(DownloadOrchestrator.ReadableName(link), "that is not a title, and not a filename");
        }
    }
}
