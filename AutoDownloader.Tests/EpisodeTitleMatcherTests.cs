using AutoDownloader.Core;
using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers matching scraped links to database episodes by title (AD-062).
    ///
    /// The case that prompted this is in the fixtures below: a real Duck Dodgers run numbered
    /// links by page order and wrote "Duck Deception" onto the file for The Trial of Duck
    /// Dodgers, and "The Trial of Duck Dodgers" onto Duck Deception. Nothing in the log
    /// suggested anything was wrong, which is what makes positional numbering dangerous
    /// rather than merely imprecise.
    /// </summary>
    [TestClass]
    public class EpisodeTitleMatcherTests
    {
        private static EpisodeLink Link(string slug, string? text = null) => new EpisodeLink
        {
            Url = $"https://www.supercartoons.net/cartoon/{slug}/",
            LinkText = text
        };

        private static DownloadEpisode Ep(int n, string title) =>
            new DownloadEpisode { EpisodeNumber = n, EpisodeTitle = title };

        // The first six Duck Dodgers episodes as the databases order them, against the page
        // order the site actually served.
        private static List<DownloadEpisode> DuckDodgers() => new List<DownloadEpisode>
        {
            Ep(1, "Duck Deception"),
            Ep(2, "The Spy Who Didn't Love Me"),
            Ep(3, "The Fowl Friend"),
            Ep(4, "The Fast and the Feathery"),
            Ep(5, "The Trial of Duck Dodgers"),
            Ep(6, "Big Bug Mamas"),
        };

        private static List<EpisodeLink> DuckDodgersPageOrder() => new List<EpisodeLink>
        {
            Link("the-trial-of-duck-dodgers"),
            Link("big-bug-mamas"),
            Link("the-fowl-friend"),
            Link("the-fast-the-feathery"),
            Link("duck-deception"),
            Link("the-spy-who-didnt-love-me"),
        };

        [TestMethod]
        public void TheSwapThatPromptedThis_IsCorrected()
        {
            var links = DuckDodgersPageOrder();

            var result = EpisodeTitleMatcher.Apply(links, DuckDodgers());

            Assert.IsTrue(result.Applied);

            // The link that IS The Trial of Duck Dodgers must be episode 5, not episode 1.
            Assert.AreEqual(5, links[0].DetectedEpisodeNumber);
            Assert.AreEqual(6, links[1].DetectedEpisodeNumber);
            Assert.AreEqual(1, links[4].DetectedEpisodeNumber);
            Assert.AreEqual(2, links[5].DetectedEpisodeNumber);
        }

        [TestMethod]
        public void SlugsThatAreNotWordForWord_StillMatch()
        {
            // "the-fast-the-feathery" against "The Fast and the Feathery" does not match, and
            // must not be forced to. This documents a real limit rather than asserting magic.
            var links = new List<EpisodeLink> { Link("the-fast-the-feathery") };

            var result = EpisodeTitleMatcher.Apply(links, DuckDodgers());

            Assert.AreEqual(0, result.Matched);
            Assert.IsFalse(result.Applied);
        }

        [TestMethod]
        public void ApostrophesAndPunctuation_DoNotBlockAMatch()
        {
            var links = new List<EpisodeLink>
            {
                Link("wheres-baby-smartypants"),
                Link("the-spy-who-didnt-love-me"),
            };

            var episodes = new List<DownloadEpisode>
            {
                Ep(8, "Where's Baby Smartypants?"),
                Ep(2, "The Spy Who Didn't Love Me"),
            };

            var result = EpisodeTitleMatcher.Apply(links, episodes);

            Assert.IsTrue(result.Applied);
            Assert.AreEqual(8, links[0].DetectedEpisodeNumber);
            Assert.AreEqual(2, links[1].DetectedEpisodeNumber);
        }

        [TestMethod]
        public void LinkTextIsPreferredWhenTheSlugIsUseless()
        {
            var links = new List<EpisodeLink>
            {
                new EpisodeLink { Url = "https://example.com/watch/48291", LinkText = "Big Bug Mamas" },
            };

            var result = EpisodeTitleMatcher.Apply(links, DuckDodgers());

            Assert.IsTrue(result.Applied);
            Assert.AreEqual(6, links[0].DetectedEpisodeNumber);
        }

        [TestMethod]
        public void TooFewMatches_LeavesNumberingAlone()
        {
            // A listing whose slugs are opaque ids must not get one real episode number and
            // five guesses: that interleaves two numbering schemes.
            var links = new List<EpisodeLink>
            {
                Link("duck-deception"),
                Link("48291"), Link("48292"), Link("48293"), Link("48294"), Link("48295"),
            };

            var result = EpisodeTitleMatcher.Apply(links, DuckDodgers());

            Assert.IsFalse(result.Applied);
            Assert.AreEqual(1, result.Matched);
            Assert.IsTrue(links.All(l => l.DetectedEpisodeNumber == null));
        }

        [TestMethod]
        public void ExtraLinksBeyondTheSeason_SortAfterTheRealEpisodes()
        {
            // supercartoons served 67 Duck Dodgers links against a 23-episode season. The
            // extras must not displace a matched episode.
            var links = new List<EpisodeLink>
            {
                Link("duck-deception"),
                Link("the-fowl-friend"),
                Link("the-trial-of-duck-dodgers"),
                Link("some-later-season-short"),
            };

            var result = EpisodeTitleMatcher.Apply(links, DuckDodgers());

            Assert.IsTrue(result.Applied);
            Assert.AreEqual(1, result.Unmatched.Count);
            Assert.IsTrue(links[3].DetectedEpisodeNumber > 6);
        }

        [TestMethod]
        public void ADuplicateTitleIdentifiesNeitherEpisode()
        {
            var episodes = new List<DownloadEpisode>
            {
                Ep(1, "Pilot"),
                Ep(9, "Pilot"),
                Ep(2, "Duck Deception"),
            };

            var links = new List<EpisodeLink> { Link("pilot"), Link("duck-deception") };

            var result = EpisodeTitleMatcher.Apply(links, episodes);

            // "Pilot" is ambiguous and stays unmatched; the unambiguous one still resolves.
            Assert.AreEqual(1, result.Matched);
        }

        [TestMethod]
        public void TheSameTitleIsNotHandedToTwoLinks()
        {
            var links = new List<EpisodeLink> { Link("duck-deception"), Link("duck-deception") };

            EpisodeTitleMatcher.Apply(links, DuckDodgers());

            Assert.AreNotEqual(links[0].DetectedEpisodeNumber, links[1].DetectedEpisodeNumber);
        }

        [TestMethod]
        public void NoEpisodeData_IsNotAMatch()
        {
            var links = DuckDodgersPageOrder();

            var result = EpisodeTitleMatcher.Apply(links, new List<DownloadEpisode>());

            Assert.IsFalse(result.Applied);
            Assert.IsTrue(links.All(l => l.DetectedEpisodeNumber == null));
        }

        [TestMethod]
        [DataRow("Where's Baby Smartypants?", "wheres baby smartypants")]
        [DataRow("M.M.O.R.P.D.", "m m o r p d")]
        [DataRow("The Fowl Friend", "fowl friend")]
        [DataRow("  Duck   Deception  ", "duck deception")]
        public void Normalise_ReducesToAComparableCore(string input, string expected)
        {
            Assert.AreEqual(expected, EpisodeTitleMatcher.Normalise(input));
        }
    }
}
