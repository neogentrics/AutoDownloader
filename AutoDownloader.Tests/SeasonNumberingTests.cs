using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the two defects that made a live five-season run unusable (AD-094, AD-095).
    ///
    /// Food Network numbers its tiles straight through the series rather than per season, so
    /// season two arrived as S02E11 to S02E20; and its season-two listing repeats an episode
    /// season one already carried, so the same video was planned twice.
    /// </summary>
    [TestClass]
    public class SeasonNumberingTests
    {
        private static DownloadOrchestrator Orchestrator() => new DownloadOrchestrator(
            new MetadataService("no-key", "no-key"),
            new SearchService("no-key"),
            new XmlService(),
            new YtDlpService("yt-dlp.exe", "aria2c.exe", "agent", "best", "ffmpeg.exe", "none", false, "compatible"),
            new AutoConfirmPrompt());

        private static DownloadMetadata FiveSeasons() => new DownloadMetadata
        {
            NextSeasonNumber = 1,
            SeasonWasSpecified = false,
            SelectedSeasons = new List<int> { 1, 2, 3, 4, 5 },
        };

        private static EpisodeLink Link(int season, int number, string slug) => new EpisodeLink
        {
            Url = "https://watch.foodnetwork.com/video/alex-vs-america/" + slug,
            LinkText = slug,
            DetectedSeasonNumber = season,
            DetectedEpisodeNumber = number,
        };

        /// <summary>
        /// The shape of the real run: ten links in season one numbered 1-10, ten in season two
        /// numbered 11-20.
        /// </summary>
        private static List<EpisodeLink> ContinuouslyNumbered()
        {
            var links = new List<EpisodeLink>();

            for (int i = 1; i <= 10; i++) links.Add(Link(1, i, "s1-slug-" + i));
            for (int i = 11; i <= 20; i++) links.Add(Link(2, i, "s2-slug-" + i));

            return links;
        }

        [TestMethod]
        public void NumbersRunningStraightThroughTheSeries_RestartAtOneEachSeason()
        {
            var result = Orchestrator().AssignEpisodeNumbers(ContinuouslyNumbered(), FiveSeasons());

            var seasonTwo = result.Where(l => l.DetectedSeasonNumber == 2)
                                  .Select(l => l.DetectedEpisodeNumber)
                                  .ToList();

            CollectionAssert.AreEqual(
                Enumerable.Range(1, 10).Cast<int?>().ToList(),
                seasonTwo,
                "season two should be E01-E10, not E11-E20");

            var seasonOne = result.Where(l => l.DetectedSeasonNumber == 1)
                                  .Select(l => l.DetectedEpisodeNumber)
                                  .ToList();

            CollectionAssert.AreEqual(
                Enumerable.Range(1, 10).Cast<int?>().ToList(),
                seasonOne,
                "season one was already correct and must stay that way");
        }

        [TestMethod]
        public void RenumberingKeepsTheOrderThePageGaveThem()
        {
            var result = Orchestrator().AssignEpisodeNumbers(ContinuouslyNumbered(), FiveSeasons());

            var first = result.First(l => l.DetectedSeasonNumber == 2);

            Assert.AreEqual(1, first.DetectedEpisodeNumber);
            Assert.AreEqual("s2-slug-11", first.LinkText, "the page's first season-two tile should be E01");
        }

        [TestMethod]
        public void APageThatAlreadyNumbersPerSeason_IsLeftAlone()
        {
            // Nothing to correct here, and renumbering would be the thing that broke it.
            var links = new List<EpisodeLink>();

            for (int i = 1; i <= 3; i++) links.Add(Link(1, i, "s1-" + i));
            for (int i = 1; i <= 3; i++) links.Add(Link(2, i, "s2-" + i));

            var result = Orchestrator().AssignEpisodeNumbers(links, FiveSeasons());

            CollectionAssert.AreEqual(
                new List<int?> { 1, 2, 3 },
                result.Where(l => l.DetectedSeasonNumber == 2).Select(l => l.DetectedEpisodeNumber).ToList());
        }

        [TestMethod]
        public void AnEpisodeListedUnderTwoSeasons_IsPlannedOnce()
        {
            // Exactly the live case: Alex vs Shellfish appears in both listings.
            var links = new List<EpisodeLink>
            {
                Link(1, 1, "alex-vs-shellfish"),
                Link(1, 2, "alex-vs-beef"),
                Link(2, 11, "alex-vs-shellfish"),
                Link(2, 12, "alex-vs-italian"),
            };

            var result = Orchestrator().AssignEpisodeNumbers(links, FiveSeasons());

            Assert.AreEqual(3, result.Count, "the repeated video should have been dropped");

            Assert.AreEqual(
                1,
                result.Count(l => l.LinkText == "alex-vs-shellfish"),
                "it should survive exactly once");

            Assert.AreEqual(
                1,
                result.Single(l => l.LinkText == "alex-vs-shellfish").DetectedSeasonNumber,
                "the earlier season keeps it");
        }

        // ------------------------------------------------- verification scope (AD-097)

        [TestMethod]
        public void AMultiSeasonRun_CountsEverySeasonFolderItWritesInto()
        {
            var links = new List<EpisodeLink>
            {
                Link(1, 1, "a"), Link(1, 2, "b"), Link(2, 1, "c"), Link(5, 1, "d"),
            };

            var folders = DownloadOrchestrator.SeasonFoldersFor(
                Path.Combine("X:", "Shows", "Alex vs America"), links, FiveSeasons());

            Assert.AreEqual(3, folders.Count, "seasons 1, 2 and 5 were written to");

            CollectionAssert.AreEqual(
                new List<string> { "Season 01", "Season 02", "Season 05" },
                folders.Select(f => new DirectoryInfo(f).Name).ToList());
        }

        [TestMethod]
        public void WhenNoLinkSaysItsSeason_TheTargetSeasonIsStillCounted()
        {
            var links = new List<EpisodeLink>
            {
                new EpisodeLink { Url = "https://example.com/a" },
            };

            var metadata = new DownloadMetadata { NextSeasonNumber = 3 };

            var folders = DownloadOrchestrator.SeasonFoldersFor(
                Path.Combine("X:", "Shows", "Some Show"), links, metadata);

            Assert.AreEqual(1, folders.Count);
            Assert.AreEqual("Season 03", new DirectoryInfo(folders[0]).Name);
        }
    }
}
