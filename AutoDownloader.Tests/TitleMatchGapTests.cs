using AutoDownloader.Core;
using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the two ways strict title matching went wrong on real data (AD-100).
    ///
    /// A database spells an episode differently from the site - "Alex vs Tournament of
    /// Champions Winners" against the slug "alex-vs-toc-winners" - so the link matches
    /// nothing and would be pushed past the real episodes. And a listing carrying as many
    /// alternate cuts as episodes can never match more than half its links, which made the
    /// trust check reject a season it had read correctly.
    /// </summary>
    [TestClass]
    public class TitleMatchGapTests
    {
        private static DownloadEpisode Ep(int number, string title) =>
            new DownloadEpisode { EpisodeNumber = number, EpisodeTitle = title };

        private static EpisodeLink Link(string slug) => new EpisodeLink
        {
            Url = "https://watch.foodnetwork.com/video/alex-vs-america/" + slug,
            LinkText = slug.Replace('-', ' '),
        };

        private static int NumberOf(List<EpisodeLink> links, string slug) =>
            links.Single(l => l.Url.EndsWith(slug)).DetectedEpisodeNumber ?? -1;

        /// <summary>Season 4: the site calls episode 6 "Ultimate Fruits", the databases "Fruit".</summary>
        [TestMethod]
        public void ATitleSpelledDifferently_TakesTheOneFreeNumberBetweenItsNeighbours()
        {
            var episodes = new List<DownloadEpisode>
            {
                Ep(4, "Alex vs Lobster"),
                Ep(5, "Alex vs Mediterranean"),
                Ep(6, "Alex vs Fruit"),
                Ep(7, "Alex vs Japan"),
            };

            var links = new List<EpisodeLink>
            {
                Link("alex-vs-lobster"),
                Link("alex-vs-mediterranean"),
                Link("alex-vs-ultimate-fruits"),
                Link("alex-vs-japan"),
            };

            var result = EpisodeTitleMatcher.Apply(links, episodes);

            Assert.IsTrue(result.Applied);
            Assert.AreEqual(1, result.PlacedByPosition);
            Assert.AreEqual(6, NumberOf(links, "alex-vs-ultimate-fruits"),
                "it sits between 5 and 7, and 6 is the only number free there");
        }

        /// <summary>Season 5: "alex-vs-toc-winners" opens the listing; E01 is free.</summary>
        [TestMethod]
        public void AnUnmatchedFirstLink_TakesTheFreeNumberBeforeTheSecond()
        {
            var episodes = new List<DownloadEpisode>
            {
                Ep(1, "Alex vs Tournament of Champions Winners"),
                Ep(2, "Alex vs Chopped Judges"),
                Ep(3, "Alex vs Thailand"),
            };

            var links = new List<EpisodeLink>
            {
                Link("alex-vs-toc-winners"),
                Link("alex-vs-chopped-judges"),
                Link("alex-vs-thailand"),
            };

            EpisodeTitleMatcher.Apply(links, episodes);

            Assert.AreEqual(1, NumberOf(links, "alex-vs-toc-winners"));
        }

        [TestMethod]
        public void TwoFreeNumbersBetweenTheSameNeighbours_IsNotGuessedAt()
        {
            // 2 and 3 are both free between 1 and 4. Choosing either is a coin toss, and a
            // wrong choice writes the wrong title onto the file.
            var episodes = new List<DownloadEpisode>
            {
                Ep(1, "First"), Ep(2, "Second"), Ep(3, "Third"), Ep(4, "Fourth"),
            };

            var links = new List<EpisodeLink> { Link("first"), Link("mystery"), Link("fourth") };

            var result = EpisodeTitleMatcher.Apply(links, episodes);

            Assert.AreEqual(0, result.PlacedByPosition);
            Assert.IsTrue(NumberOf(links, "mystery") > 4, "it belongs after the real episodes");
        }

        /// <summary>
        /// Season 5 as it really is: eight episodes and eight alternate cuts. Six matched of
        /// sixteen links is 37%, which the old check rejected - putting the season back on
        /// the positional numbering that this matching exists to replace.
        /// </summary>
        [TestMethod]
        public void AListingHalfMadeOfAlternateCuts_IsStillTrusted()
        {
            // Season 5's real shape: 8 episodes in the databases, 6 of them matched, and
            // 8 alternate cuts that can never match. 6 of 14 links is 43%, which the old
            // check rejected; 6 of the 8 comparable episodes is 75%.
            var episodes = new List<DownloadEpisode>
            {
                Ep(1, "Alex vs Chopped Judges"), Ep(2, "Alex vs Thailand"),
                Ep(3, "Alex vs Winners"), Ep(4, "Alex vs Mexico"),
                Ep(5, "Alex vs Her Favorite Chefs"), Ep(6, "Alex vs Restaurant Teams"),
                Ep(7, "Alex vs Pastry"), Ep(8, "Alex vs Bacon"),
            };

            var links = new List<EpisodeLink>
            {
                Link("alex-vs-chopped-judges"), Link("alex-vs-thailand"),
                Link("alex-vs-winners"), Link("alex-vs-mexico"),
                Link("alex-vs-her-favorite-chefs"), Link("alex-vs-restaurant-teams"),
                Link("chefs-cut-one"), Link("chefs-cut-two"), Link("chefs-cut-three"),
                Link("chefs-cut-four"), Link("chefs-cut-five"), Link("chefs-cut-six"),
                Link("chefs-cut-seven"), Link("chefs-cut-eight"),
            };

            var result = EpisodeTitleMatcher.Apply(links, episodes);

            Assert.IsTrue(result.Applied, "6 of the 8 comparable episodes matched");
            Assert.AreEqual(6, result.Matched);

            Assert.AreEqual(1, NumberOf(links, "alex-vs-chopped-judges"),
                "the real episodes keep their database numbers");

            Assert.IsTrue(
                links.Where(l => l.Url.Contains("chefs-cut"))
                     .All(l => l.DetectedEpisodeNumber > 8),
                "the cuts belong after the last real episode");
        }

        [TestMethod]
        public void APartialSourceOfALongSeason_IsStillTrusted()
        {
            // Three episodes of a twenty-six episode season, all read correctly. Judging
            // against the season's 26 rather than the 3 comparable would make this 12%.
            var episodes = Enumerable.Range(1, 26)
                .Select(n => Ep(n, "Episode " + n))
                .ToList();

            var links = new List<EpisodeLink> { Link("episode-7"), Link("episode-8"), Link("episode-9") };

            var result = EpisodeTitleMatcher.Apply(links, episodes);

            Assert.IsTrue(result.Applied);
            Assert.AreEqual(7, NumberOf(links, "episode-7"));
            Assert.AreEqual(9, NumberOf(links, "episode-9"));
        }

        [TestMethod]
        public void SlugsThatAreNotTitlesAtAll_StillFallBackToPosition()
        {
            // The guard's original purpose, which must survive the change.
            var episodes = new List<DownloadEpisode>
            {
                Ep(1, "Alex vs Shellfish"), Ep(2, "Alex vs Beef"), Ep(3, "Alex vs Spicy"),
            };

            var links = new List<EpisodeLink>
            {
                Link("9f2a41bc"), Link("7d0e55aa"), Link("1b83cc90"),
            };

            var result = EpisodeTitleMatcher.Apply(links, episodes);

            Assert.IsFalse(result.Applied, "nothing was recognised, so position must stand");
        }
    }
}
