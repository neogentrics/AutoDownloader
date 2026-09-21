using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers detection of URLs whose path names the page type rather than the content
    /// (AD-052).
    ///
    /// A YouTube playlist URL is the clearest case: everything identifying the content sits
    /// in the query string, so the path yields "playlist" and the metadata lookup would go
    /// looking for a show by that name. The page title has to be consulted instead.
    /// </summary>
    [TestClass]
    public class PlaceholderNameTests
    {
        [TestMethod]
        public void ParseFromUrl_OnAYouTubePlaylistYieldsOnlyAPlaceholder()
        {
            var (name, _) = UrlMetadataParser.ParseFromUrl(
                "https://www.youtube.com/playlist?list=PLfIRruq-CiKBluyAyrZ8l5WVDK-_Ov5oX");

            Assert.IsTrue(UrlMetadataParser.IsPlaceholderName(name),
                $"'{name}' describes the page type, not the show, so the page title must be used.");
        }

        [TestMethod]
        [DataRow("playlist")]
        [DataRow("Playlist")]
        [DataRow("watch")]
        [DataRow("Videos")]
        [DataRow("browse")]
        [DataRow("anime")]
        [DataRow("tv")]
        [DataRow("")]
        [DataRow(null)]
        public void IsPlaceholderName_RecognisesStructuralNames(string? name)
        {
            Assert.IsTrue(UrlMetadataParser.IsPlaceholderName(name));
        }

        [TestMethod]
        [DataRow("Ben To")]
        [DataRow("Teen Titans")]
        [DataRow("Mega Man Star Force")]
        [DataRow("The Mandalorian")]
        public void IsPlaceholderName_LeavesRealShowNamesAlone(string name)
        {
            Assert.IsFalse(UrlMetadataParser.IsPlaceholderName(name));
        }

        [TestMethod]
        public void ParseFromUrl_StillReadsARealNameWhenTheUrlHasOne()
        {
            var (name, _) = UrlMetadataParser.ParseFromUrl("https://www.wcoanimedub.tv/anime/ben-to/");

            Assert.AreEqual("Ben To", name);
            Assert.IsFalse(UrlMetadataParser.IsPlaceholderName(name));
        }
    }
}
