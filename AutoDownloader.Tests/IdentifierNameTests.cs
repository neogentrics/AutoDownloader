using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers recognising a URL that names a show by id rather than title (AD-073).
    ///
    /// Plenty of sites address a show by a UUID, so the URL yields
    /// "Cba7f3ea 50Cd 40Fa Aae3 Cba25feb1c3e". That is not a placeholder word like "watch",
    /// so it was accepted as the show name and searched for verbatim - and the user had to
    /// retype the real title by hand on every run.
    /// </summary>
    [TestClass]
    public class IdentifierNameTests
    {
        [TestMethod]
        [DataRow("Cba7f3ea 50Cd 40Fa Aae3 Cba25feb1c3e")]
        [DataRow("419bec47 9f30 4ac1 A0d1 2d65b2c95b64")]
        [DataRow("f4b41e45 d264 4156 Ab2c 930c3b3e0e79")]
        [DataRow("5174439c")]
        public void AnIdIsRecognisedAsNotAName(string name)
        {
            Assert.IsTrue(UrlMetadataParser.LooksLikeAnIdentifier(name), name);
            Assert.IsTrue(UrlMetadataParser.IsPlaceholderName(name), name);
        }

        [TestMethod]
        [DataRow("Alex Vs America Food Network Atve Us")]
        [DataRow("Barefoot Contessa Back To Basics")]
        [DataRow("Ben To")]
        [DataRow("Steins Gate")]
        [DataRow("Fate Zero")]
        public void ARealTitleIsNotMistakenForAnId(string name)
        {
            Assert.IsFalse(UrlMetadataParser.LooksLikeAnIdentifier(name), name);
        }

        [TestMethod]
        [DataRow("Cafe")]
        [DataRow("Deadbeef")]
        [DataRow("Face Off")]
        public void HexadecimalWordsWithoutDigitsAreStillWords(string name)
        {
            // "Cafe" and "Deadbeef" are valid hex, but a real id carries digits. Requiring
            // one is what keeps ordinary words out of this.
            Assert.IsFalse(UrlMetadataParser.LooksLikeAnIdentifier(name), name);
        }

        [TestMethod]
        public void OneStrayCodeDoesNotCondemnATitle()
        {
            Assert.IsFalse(UrlMetadataParser.LooksLikeAnIdentifier("Babylon 5 Season 2a3f Special Edition"));
        }

        [TestMethod]
        [DataRow("Watch Barefoot Contessa: Back To Basics", "Barefoot Contessa: Back To Basics")]
        [DataRow("Stream Alex vs America", "Alex Vs America")]
        public void TheSitesOwnVerbIsNotPartOfTheShowName(string pageTitle, string expected)
        {
            // It would otherwise go into the database search verbatim and match nothing.
            Assert.AreEqual(expected, UrlMetadataParser.TidyName(pageTitle));
        }

        [TestMethod]
        public void APageActuallyTitledWatchIsLeftAlone()
        {
            Assert.AreEqual("Watch", UrlMetadataParser.TidyName("Watch"));
        }
    }
}
