using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers numbering episodes by the title a link carries rather than by its position
    /// (AD-099).
    ///
    /// From the real run: Food Network does not list Alex vs Southern Comfort, season 3
    /// episode 4. Numbering by position wrote that title onto Alex vs Salmon and shifted the
    /// five episodes after it, and Alex vs California - the real episode 10 - was filed as
    /// episode 9 under the name Alex vs Potatoes.
    /// </summary>
    [TestClass]
    public class DatabaseAlignmentTests
    {
        private static (DownloadOrchestrator Orchestrator, List<string> Log) Build()
        {
            var orchestrator = new DownloadOrchestrator(
                new MetadataService("no-key", "no-key"),
                new SearchService("no-key"),
                new XmlService(),
                new YtDlpService("yt-dlp.exe", "aria2c.exe", "agent", "best", "ffmpeg.exe", "none", false, "compatible"),
                new AutoConfirmPrompt());

            var lines = new List<string>();
            orchestrator.OnLog += (_, e) => lines.Add(e.Message);

            return (orchestrator, lines);
        }

        private static EpisodeLink Link(int season, int positionNumber, string slug) => new EpisodeLink
        {
            Url = "https://watch.foodnetwork.com/video/alex-vs-america/" + slug,
            LinkText = slug.Replace('-', ' '),
            DetectedSeasonNumber = season,
            DetectedEpisodeNumber = positionNumber,
        };

        /// <summary>Season 3 as the databases list it - ten episodes.</summary>
        private static Dictionary<int, string?> SeasonThreeTitles() => new Dictionary<int, string?>
        {
            [1] = "Alex vs James Beard Winners",
            [2] = "Alex vs Chicken",
            [3] = "Alex vs Bacon",
            [4] = "Alex vs Southern Comfort",
            [5] = "Alex vs Salmon",
            [6] = "Alex vs Hawaii",
            [7] = "Alex vs Pastry",
            [8] = "Alex vs Game Day",
            [9] = "Alex vs Potatoes",
            [10] = "Alex vs California",
        };

        /// <summary>The nine the source actually carries, numbered 1-9 by position.</summary>
        private static List<EpisodeLink> SeasonThreeLinks()
        {
            string[] slugs =
            {
                "alex-vs-james-beard-winners", "alex-vs-chicken", "alex-vs-bacon",
                "alex-vs-salmon", "alex-vs-hawaii", "alex-vs-pastry",
                "alex-vs-game-day", "alex-vs-potatoes", "alex-vs-california",
            };

            return slugs.Select((s, i) => Link(3, i + 1, s)).ToList();
        }

        private static Dictionary<int, Dictionary<int, string?>> Titles(int season, Dictionary<int, string?> titles) =>
            new Dictionary<int, Dictionary<int, string?>> { [season] = titles };

        private static int NumberOf(List<EpisodeLink> links, string slug) =>
            links.Single(l => l.Url.EndsWith(slug)).DetectedEpisodeNumber ?? -1;

        [TestMethod]
        public void AMissingEpisode_LeavesAGapInsteadOfShiftingTheRest()
        {
            var (orchestrator, _) = Build();
            var links = SeasonThreeLinks();

            orchestrator.AlignNumbersToDatabase(
                links,
                Titles(3, SeasonThreeTitles()),
                new DownloadMetadata { NextSeasonNumber = 3 });

            Assert.AreEqual(5, NumberOf(links, "alex-vs-salmon"), "salmon is E05, not E04");
            Assert.AreEqual(6, NumberOf(links, "alex-vs-hawaii"));
            Assert.AreEqual(9, NumberOf(links, "alex-vs-potatoes"));
            Assert.AreEqual(10, NumberOf(links, "alex-vs-california"), "california is E10, not E09");

            Assert.IsFalse(
                links.Any(l => l.DetectedEpisodeNumber == 4),
                "E04 is not carried by this source and must stay empty");
        }

        [TestMethod]
        public void TheMissingEpisodeIsNamedInTheLog()
        {
            var (orchestrator, log) = Build();

            orchestrator.AlignNumbersToDatabase(
                SeasonThreeLinks(),
                Titles(3, SeasonThreeTitles()),
                new DownloadMetadata { NextSeasonNumber = 3 });

            var gap = log.SingleOrDefault(l => l.Contains("does not carry"));

            Assert.IsNotNull(gap, "the gap should be reported");
            StringAssert.Contains(gap, "E04");
        }

        [TestMethod]
        public void AlternateCuts_AreNumberedAfterTheRealEpisodes()
        {
            // Season 1: five real episodes plus five alternate cuts the databases do not
            // list. The cuts must not displace an episode.
            var (orchestrator, _) = Build();

            string[] slugs =
            {
                "alex-vs-shellfish", "alex-vs-beef", "alex-vs-spicy", "alex-vs-noodles",
                "alex-vs-chocolate", "chefs-cut-alex-vs-shellfish", "chefs-cut-alex-vs-beef",
                "chefs-cut-alex-vs-spicy", "chefs-cut-alex-vs-noodles", "chefs-cut-alex-vs-chocolate",
            };

            var links = slugs.Select((s, i) => Link(1, i + 1, s)).ToList();

            var titles = new Dictionary<int, string?>
            {
                [1] = "Alex vs Shellfish", [2] = "Alex vs Beef", [3] = "Alex vs Spicy",
                [4] = "Alex vs Noodles", [5] = "Alex vs Chocolate",
            };

            orchestrator.AlignNumbersToDatabase(
                links, Titles(1, titles), new DownloadMetadata { NextSeasonNumber = 1 });

            Assert.AreEqual(1, NumberOf(links, "/alex-vs-shellfish"));
            Assert.AreEqual(5, NumberOf(links, "/alex-vs-chocolate"));

            Assert.IsTrue(
                links.Where(l => l.Url.Contains("chefs-cut"))
                     .All(l => l.DetectedEpisodeNumber >= 6),
                "alternate cuts belong after the last real episode");
        }

        [TestMethod]
        public void TheResultIsOrderedBySeasonThenEpisode()
        {
            var (orchestrator, _) = Build();

            // Deliberately out of order, the way a listing often arrives.
            var links = new List<EpisodeLink>
            {
                Link(3, 1, "alex-vs-potatoes"),
                Link(3, 2, "alex-vs-chicken"),
                Link(3, 3, "alex-vs-california"),
            };

            orchestrator.AlignNumbersToDatabase(
                links, Titles(3, SeasonThreeTitles()), new DownloadMetadata { NextSeasonNumber = 3 });

            CollectionAssert.AreEqual(
                new List<int?> { 2, 9, 10 },
                links.Select(l => l.DetectedEpisodeNumber).ToList(),
                "downloads should run in episode order");
        }

        [TestMethod]
        public void WithNoDatabaseTitles_TheExistingNumberingIsLeftAlone()
        {
            var (orchestrator, _) = Build();
            var links = SeasonThreeLinks();

            orchestrator.AlignNumbersToDatabase(
                links,
                Titles(3, new Dictionary<int, string?>()),
                new DownloadMetadata { NextSeasonNumber = 3 });

            CollectionAssert.AreEqual(
                Enumerable.Range(1, 9).Cast<int?>().ToList(),
                links.Select(l => l.DetectedEpisodeNumber).ToList());
        }
    }
}
