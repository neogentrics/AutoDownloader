using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers reading a listing tile's caption (AD-103).
    ///
    /// Every string here was captured from a live Food Network listing.
    /// </summary>
    [TestClass]
    public class ListingTextParserTests
    {
        private const string Jeffrey =
            "S12 E1Cooking for Jeffrey: Birthday21mTV-G10/16/2016"
            + "Ina throws an Italian-themed birthday dinner with a surprise for Jeffrey."
            + "Ina throws an Italian-themed birthday dinner with a surprise for Jeffrey.";

        private const string Pro =
            "S18 E1Cook Like a Pro: Best in Class21mTV-G5/16/2020"
            + "Ina shares best-in-class dishes like Lemon Ricotta Pancakes with Figs."
            + "Ina shares best-in-class dishes like Lemon Ricotta Pancakes with Figs.";

        [TestMethod]
        public void TheSeasonAndEpisodeAreReadFromTheTile()
        {
            var details = ListingTextParser.Parse(Jeffrey);

            Assert.IsNotNull(details);
            Assert.AreEqual(12, details!.SeasonNumber);
            Assert.AreEqual(1, details.EpisodeNumber);
        }

        [TestMethod]
        public void TheTitleStopsBeforeTheRuntime()
        {
            Assert.AreEqual("Cooking for Jeffrey: Birthday", ListingTextParser.Parse(Jeffrey)!.Title);
            Assert.AreEqual("Cook Like a Pro: Best in Class", ListingTextParser.Parse(Pro)!.Title);
        }

        [TestMethod]
        public void TheDescriptionIsReadOnceNotTwice()
        {
            Assert.AreEqual(
                "Ina throws an Italian-themed birthday dinner with a surprise for Jeffrey.",
                ListingTextParser.Parse(Jeffrey)!.Description);
        }

        [TestMethod]
        public void TheRuntimeRatingAndAirDateAreRead()
        {
            var details = ListingTextParser.Parse(Jeffrey)!;

            Assert.AreEqual(21, details.RuntimeMinutes);
            Assert.AreEqual("TV-G", details.Rating);
            Assert.AreEqual(new DateTime(2016, 10, 16), details.AirDate);
        }

        [TestMethod]
        public void SeasonsWithDoubleDigitEpisodes_AreRead()
        {
            var details = ListingTextParser.Parse("S3 E12Some Title21mTV-PG1/2/2011Words.Words.")!;

            Assert.AreEqual(3, details.SeasonNumber);
            Assert.AreEqual(12, details.EpisodeNumber);
            Assert.AreEqual("Some Title", details.Title);
            Assert.AreEqual("Words.", details.Description);
        }

        /// <summary>
        /// A title ending in a number runs straight into the runtime. Reading it as
        /// "Dinner Party 10" plus "121m" is worse than not reading it: the file would be
        /// named confidently and wrongly.
        /// </summary>
        [TestMethod]
        public void ATitleEndingInANumber_IsNotGuessedAt()
        {
            var details = ListingTextParser.Parse("S12 E6Dinner Party 10121mTV-G11/27/2016Words.Words.");

            Assert.IsNotNull(details);
            Assert.AreEqual(12, details!.SeasonNumber, "the numbering is still trustworthy");
            Assert.AreEqual(6, details.EpisodeNumber);
            Assert.IsNull(details.Title, "an ambiguous title is left unread");
        }

        [TestMethod]
        public void SomethingThatIsNotACaption_IsNotParsed()
        {
            Assert.IsNull(ListingTextParser.Parse("Watch Now"));
            Assert.IsNull(ListingTextParser.Parse("/video/barefoot-contessa/simply-seafood"));
            Assert.IsNull(ListingTextParser.Parse(null));
            Assert.IsNull(ListingTextParser.Parse("   "));
        }

        [TestMethod]
        public void ANumberedTileWithNothingElse_StillGivesItsNumbers()
        {
            var details = ListingTextParser.Parse("S4 E7Just a Title")!;

            Assert.AreEqual(4, details.SeasonNumber);
            Assert.AreEqual(7, details.EpisodeNumber);
            Assert.AreEqual("Just a Title", details.Title);
        }

        [TestMethod]
        public void ADescriptionThatIsNotDoubled_IsLeftAlone()
        {
            Assert.AreEqual("Only once.", ListingTextParser.Deduplicate("Only once."));
            Assert.AreEqual("abab", ListingTextParser.Deduplicate("abababab"));
        }

        [TestMethod]
        public void EveryRealSeasonTwelveTile_Parses()
        {
            string[] tiles =
            {
                "S12 E2Cooking for Jeffrey: Deans21mTV-G10/23/2016W.W.",
                "S12 E3Cooking for Jeffrey: Pizza21mTV-G10/30/2016W.W.",
                "S12 E4Cooking for Jeffrey: Friday21mTV-G11/6/2016W.W.",
                "S12 E5Cooking for Jeffrey: Surprise21mTV-G11/13/2016W.W.",
                "S12 E7Cooking for Jeffrey: Breakfast21mTV-G12/4/2016W.W.",
                "S12 E8Cooking for Jeffrey: Weekend21mTV-G12/11/2016W.W.",
            };

            foreach (var tile in tiles)
            {
                var details = ListingTextParser.Parse(tile);

                Assert.IsNotNull(details, tile);
                Assert.AreEqual(12, details!.SeasonNumber, tile);
                StringAssert.StartsWith(details.Title, "Cooking for Jeffrey: ", tile);
                Assert.AreEqual("W.", details.Description, tile);
            }
        }
    }
}
