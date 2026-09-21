using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the relevance guard on the third metadata database (AD-070).
    ///
    /// Kitsu's text search always returns something. Asked for "Alex vs America" it offers
    /// Chocolate Underground, Totally Spies! and Captain Laserhawk. Without a guard, any show
    /// the other two databases could not identify would be handed an unrelated anime's
    /// episode list - and the filenames would look entirely plausible.
    ///
    /// The guard has to reject that while still accepting a real anime match whose official
    /// title is romanised differently from the search term, which is the normal case.
    /// </summary>
    [TestClass]
    public class KitsuMatchTests
    {
        [TestMethod]
        [DataRow("Ben-To", "Ben-To")]
        [DataRow("ben to", "Ben-To")]
        [DataRow("Phi Brain: Puzzle of God", "Phi Brain: Kami no Puzzle")]
        [DataRow("Attack on Titan", "Attack on Titan Season 2")]
        [DataRow("Naruto", "Naruto")]
        public void ARealMatchIsAccepted(string query, string candidate)
        {
            Assert.IsTrue(KitsuMetadataClient.IsPlausibleMatch(query, candidate),
                $"'{candidate}' should be accepted for '{query}'");
        }

        [TestMethod]
        [DataRow("Alex vs America", "Chocolate Underground: Bokura no Chocolate Sensou")]
        [DataRow("Alex vs America", "Totally Spies!")]
        [DataRow("Alex vs America", "Captain Laserhawk: A Blood Dragon Remix")]
        [DataRow("The Pink Panther Show", "Cowboy Bebop")]
        [DataRow("Popeye the Sailor", "Sailor Moon")]
        public void UnrelatedResultsAreRejected(string query, string candidate)
        {
            Assert.IsFalse(KitsuMetadataClient.IsPlausibleMatch(query, candidate),
                $"'{candidate}' should NOT be accepted for '{query}'");
        }

        [TestMethod]
        public void ShortWordsDoNotCarryAMatch()
        {
            // "vs", "of", "the" appear everywhere and distinguish nothing. If they counted,
            // "Alex vs America" would match anything with "the" in it.
            Assert.IsFalse(KitsuMetadataClient.IsPlausibleMatch("Alex vs America", "The Vs Of It"));
        }

        [TestMethod]
        public void EmptyInputIsNeverAMatch()
        {
            Assert.IsFalse(KitsuMetadataClient.IsPlausibleMatch("", "Ben-To"));
            Assert.IsFalse(KitsuMetadataClient.IsPlausibleMatch("Ben-To", ""));
        }

        [TestMethod]
        public void PunctuationDoesNotDefeatAMatch()
        {
            Assert.IsTrue(KitsuMetadataClient.IsPlausibleMatch(
                "Fullmetal Alchemist: Brotherhood", "Fullmetal Alchemist Brotherhood"));
        }

        [TestMethod]
        public void HalfTheWordsIsEnough()
        {
            // The threshold: a subtitle the query does not have must not sink a real match.
            Assert.IsTrue(KitsuMetadataClient.IsPlausibleMatch(
                "Steins Gate", "Steins;Gate 0"));
        }
    }
}
