using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the fallback forms tried when a show name does not resolve (AD-053).
    ///
    /// Measured against the live APIs: "Megaman Star Force Anime" returns nothing at all,
    /// while "Megaman Star Force" returns the show. One trailing word was the difference,
    /// and the previous behaviour was to abort the download over it.
    /// </summary>
    [TestClass]
    public class SearchTermVariantsTests
    {
        [TestMethod]
        public void Generate_AlwaysTriesTheOriginalFirst()
        {
            var variants = SearchTermVariants.Generate("Ben-To");

            Assert.AreEqual("Ben-To", variants[0],
                "a name that already works must not be altered before it is tried");
        }

        [TestMethod]
        public void Generate_StripsTheTrailingQualifierThatBrokeTheRealSearch()
        {
            var variants = SearchTermVariants.Generate("Megaman Star Force Anime");

            CollectionAssert.Contains(variants.ToList(), "Megaman Star Force");
        }

        [TestMethod]
        [DataRow("Some Show English Dub", "Some Show")]
        [DataRow("Some Show Full Episodes", "Some Show")]
        [DataRow("Some Show - Complete Series", "Some Show")]
        [DataRow("Some Show HD", "Some Show")]
        public void Generate_RemovesCommonUploadQualifiers(string input, string expected)
        {
            CollectionAssert.Contains(SearchTermVariants.Generate(input).ToList(), expected);
        }

        [TestMethod]
        public void Generate_PeelsSeveralQualifiersOneAtATime()
        {
            var variants = SearchTermVariants.Generate("Some Show Anime English Dubbed").ToList();

            CollectionAssert.Contains(variants, "Some Show Anime");
            CollectionAssert.Contains(variants, "Some Show");
        }

        [TestMethod]
        public void Generate_RemovesBracketedAsides()
        {
            CollectionAssert.Contains(
                SearchTermVariants.Generate("Some Show (Complete Series)").ToList(), "Some Show");
        }

        [TestMethod]
        public void Generate_DoesNotStripAQualifierThatStartsTheTitle()
        {
            // "Anime Crimes Division" is genuinely called that; only trailing noise is removed.
            var variants = SearchTermVariants.Generate("Anime Crimes Division");

            Assert.AreEqual(1, variants.Count);
            Assert.AreEqual("Anime Crimes Division", variants[0]);
        }

        [TestMethod]
        public void Generate_DoesNotTruncateAWordThatMerelyEndsWithAQualifier()
        {
            // "Sub" must not eat the end of "Subterranean".
            var variants = SearchTermVariants.Generate("Life Subterranean");

            Assert.AreEqual(1, variants.Count);
            Assert.AreEqual("Life Subterranean", variants[0]);
        }

        [TestMethod]
        public void Generate_NeverReducesANameToNothing()
        {
            foreach (var variant in SearchTermVariants.Generate("Anime"))
            {
                Assert.IsTrue(variant.Length >= 2, $"'{variant}' is not a usable search term");
            }
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Generate_HandlesEmptyInput(string? input)
        {
            Assert.AreEqual(0, SearchTermVariants.Generate(input).Count);
        }
    }
}
