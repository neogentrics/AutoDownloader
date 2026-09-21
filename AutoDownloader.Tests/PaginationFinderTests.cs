using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers following a paginated listing (AD-060).
    ///
    /// Reading only page one is worse than failing outright: a show spread across six pages
    /// yields a sixth of its episodes while every message reports success, and the
    /// verification step then blames the source for being incomplete. Confidently wrong.
    /// </summary>
    [TestClass]
    public class PaginationFinderTests
    {
        [TestMethod]
        public void FindsAnExplicitRelNextLink()
        {
            const string html = """<html><head><link rel="next" href="/serie/x/page/2/"></head><body></body></html>""";

            string? next = PaginationFinder.FindNextPage(html, "https://example.net/serie/x/");

            Assert.AreEqual("https://example.net/serie/x/page/2/", next);
        }

        [TestMethod]
        [DataRow("""<a href="/serie/x/page/2/">Next</a>""")]
        [DataRow("""<a href="/serie/x/page/2/">Next &#8594;</a>""")]
        [DataRow("""<a href="/serie/x/page/2/">&#187;</a>""")]
        [DataRow("""<a href="/serie/x/page/2/" title="Next page">&gt;</a>""")]
        public void FindsALabelledNextLink(string html)
        {
            string? next = PaginationFinder.FindNextPage(html, "https://example.net/serie/x/");

            Assert.AreEqual("https://example.net/serie/x/page/2/", next);
        }

        [TestMethod]
        public void FollowsANumberedPagerToTheNextNumber()
        {
            const string html = """
                <div class="pager">
                  <a href="/serie/x/page/1/">1</a>
                  <a href="/serie/x/page/3/">3</a>
                  <a href="/serie/x/page/4/">4</a>
                </div>
                """;

            // Currently on page 2, so page 3 is next - not page 1, and not the highest.
            string? next = PaginationFinder.FindNextPage(html, "https://example.net/serie/x/page/2/");

            Assert.AreEqual("https://example.net/serie/x/page/3/", next);
        }

        [TestMethod]
        public void ReturnsNullOnTheLastPage()
        {
            const string html = """<div class="pager"><a href="/serie/x/page/1/">1</a></div>""";

            Assert.IsNull(PaginationFinder.FindNextPage(html, "https://example.net/serie/x/page/6/"));
        }

        [TestMethod]
        public void IgnoresALinkPointingAtThePageWeAreOn()
        {
            // A self-referencing "next" would loop forever.
            const string html = """<a href="/serie/x/page/2/">Next</a>""";

            Assert.IsNull(PaginationFinder.FindNextPage(html, "https://example.net/serie/x/page/2/"));
        }

        [TestMethod]
        public void IgnoresAnOffSiteNextLink()
        {
            const string html = """<a rel="next" href="https://elsewhere.example/page/2/">Next</a>""";

            Assert.IsNull(PaginationFinder.FindNextPage(html, "https://example.net/serie/x/"));
        }

        [TestMethod]
        public void DoesNotMistakeAnEpisodeTitledWithANumberForAPager()
        {
            // An episode simply called "2" must not be followed as page 2.
            const string html = """<a href="/cartoon/two/">2</a>""";

            Assert.IsNull(PaginationFinder.FindNextPage(html, "https://example.net/serie/x/"));
        }

        [TestMethod]
        [DataRow("https://example.net/serie/x/", 1)]
        [DataRow("https://example.net/serie/x/page/4/", 4)]
        [DataRow("https://example.net/serie/x/?page=7", 7)]
        [DataRow("https://example.net/serie/x/?paged=3&lang=en", 3)]
        public void ReadsTheCurrentPageNumberFromTheUrl(string url, int expected)
        {
            Assert.AreEqual(expected, PaginationFinder.CurrentPageNumber(url));
        }

        [TestMethod]
        [DataRow("Next episode")]
        [DataRow("Next season")]
        [DataRow("Previous")]
        [DataRow("")]
        [DataRow(null)]
        public void DoesNotTreatContentLinksAsPagination(string? label)
        {
            // "Next episode" goes to content, not to another page of the listing.
            Assert.IsFalse(PaginationFinder.LooksLikeNextLabel(label));
        }
    }
}
