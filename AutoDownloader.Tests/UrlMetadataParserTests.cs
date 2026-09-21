using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the URL parsing rules that decide which show and season get looked up.
    /// AD-002 regressed here silently: the season was parsed correctly and then ignored.
    /// </summary>
    [TestClass]
    public class UrlMetadataParserTests
    {
        [TestMethod]
        [DataRow("https://example.com/series/love-thy-neighbor/season-2", "Love Thy Neighbor", 2)]
        [DataRow("https://example.com/series/love-thy-neighbor/season_3", "Love Thy Neighbor", 3)]
        [DataRow("https://example.com/show/some-show/s02", "Some Show", 2)]
        [DataRow("https://example.com/show/some-show/s2", "Some Show", 2)]
        [DataRow("https://example.com/show/some-show/season/4/", "Some Show", 4)]
        public void ParseFromUrl_DetectsSeasonAndPrecedingTitle(string url, string expectedName, int expectedSeason)
        {
            var (name, season) = UrlMetadataParser.ParseFromUrl(url);

            Assert.AreEqual(expectedName, name);
            Assert.AreEqual(expectedSeason, season);
        }

        [TestMethod]
        public void ParseFromUrl_WithNoSeasonSegment_ReturnsNullSeason()
        {
            var (name, season) = UrlMetadataParser.ParseFromUrl("https://example.com/series/12345/the-mandalorian");

            Assert.AreEqual("The Mandalorian", name);
            Assert.IsNull(season, "A URL with no season segment must not invent one.");
        }

        [TestMethod]
        public void ParseFromUrl_IgnoresPurelyNumericAndStructuralSegments()
        {
            // "series" and the numeric id are structural, not the title.
            var (name, _) = UrlMetadataParser.ParseFromUrl("https://tubitv.com/series/300001/some-great-show");

            Assert.AreEqual("Some Great Show", name);
        }

        [TestMethod]
        public void ParseFromUrl_OnMalformedInput_DoesNotThrow()
        {
            var (name, season) = UrlMetadataParser.ParseFromUrl("not a url at all");

            Assert.AreEqual("Unknown Show", name);
            Assert.IsNull(season);
        }

        // ------------------------------------------------------------------ SanitizeFileName

        [TestMethod]
        public void SanitizeFileName_ReplacesCharactersWindowsForbids()
        {
            // AD-006: this is the case that threw out of Directory.CreateDirectory, because
            // the only call site was commented out.
            string result = UrlMetadataParser.SanitizeFileName("Star Wars: The Clone Wars");

            Assert.IsFalse(result.Contains(':'), "A colon is not a legal Windows filename character.");
            Assert.AreEqual("Star Wars_ The Clone Wars", result);
        }

        [TestMethod]
        [DataRow("Trailing dot.", "Trailing dot")]
        [DataRow("Trailing space   ", "Trailing space")]
        [DataRow("Both. ", "Both")]
        public void SanitizeFileName_StripsTrailingDotsAndSpaces(string input, string expected)
        {
            Assert.AreEqual(expected, UrlMetadataParser.SanitizeFileName(input));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("???")]
        public void SanitizeFileName_NeverReturnsAnEmptyName(string? input)
        {
            string result = UrlMetadataParser.SanitizeFileName(input);

            Assert.IsFalse(string.IsNullOrWhiteSpace(result),
                "An empty folder name would throw when the directory is created.");
        }

        [TestMethod]
        public void TidyName_ConvertsSlugToTitleCase()
        {
            Assert.AreEqual("Love Thy Neighbor", UrlMetadataParser.TidyName("love-thy-neighbor"));
            Assert.AreEqual("Phi Brain Puzzle Of God", UrlMetadataParser.TidyName("phi_brain_puzzle_of_god"));
        }
    }
}
