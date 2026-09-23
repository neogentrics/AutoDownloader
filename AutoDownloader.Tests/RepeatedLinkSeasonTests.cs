using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers settling which season a link repeated across several tabs belongs to (AD-101).
    ///
    /// Food Network lists season 7 of Be My Guest with Ina Garten under season 1 as well.
    /// Dropping the repeat and keeping the earliest season filed Allison Janney, Jon Batiste,
    /// Hoda Kotb and Michael Barbaro as season 1 episodes 5 to 8, and left season 7 with no
    /// links at all. Keeping the latest would break Alex vs America, where one promotional
    /// tile really is repeated into every tab and belongs to season 1 - so neither position
    /// rule is right, and the databases decide.
    /// </summary>
    [TestClass]
    public class RepeatedLinkSeasonTests
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

        private static EpisodeLink Repeated(string slug, int keptSeason, params int[] candidates) =>
            new EpisodeLink
            {
                Url = "https://watch.foodnetwork.com/video/show/" + slug,
                LinkText = slug.Replace('-', ' '),
                DetectedSeasonNumber = keptSeason,
                CandidateSeasons = candidates.ToList(),
            };

        private static Dictionary<int, string?> Titles(params string[] titles) =>
            titles.Select((t, i) => (t, i))
                  .ToDictionary(x => x.i + 1, x => (string?)x.t);

        /// <summary>Be My Guest: season 1's tab also carries season 7.</summary>
        private static Dictionary<int, Dictionary<int, string?>> InaGarten() =>
            new Dictionary<int, Dictionary<int, string?>>
            {
                [1] = Titles("Julianna Marguiles", "Chef Erin French", "Willie Geist",
                             "Rob Marshall and John Deluca"),
                [7] = Titles("Allison Janney", "Jon Batiste", "Hoda Kotb", "Michael Barbaro"),
            };

        [TestMethod]
        public void ALaterSeasonListedUnderTheFirst_IsMovedBackToItsOwnSeason()
        {
            var (orchestrator, _) = Build();

            var links = new List<EpisodeLink>
            {
                Repeated("allison-janney", keptSeason: 1, 1, 7),
                Repeated("jon-batiste", keptSeason: 1, 1, 7),
                Repeated("hoda-kotb", keptSeason: 1, 1, 7),
                Repeated("michael-barbaro", keptSeason: 1, 1, 7),
            };

            orchestrator.ResolveSeasonForRepeatedLinks(links, InaGarten());

            Assert.IsTrue(links.All(l => l.DetectedSeasonNumber == 7),
                "season 7 is the season that names them");
        }

        [TestMethod]
        public void ARealSeasonOneEpisode_StaysWhereItIs()
        {
            var (orchestrator, _) = Build();

            // Alex vs America's promotional tile, repeated into all five season tabs.
            var links = new List<EpisodeLink>
            {
                Repeated("alex-vs-shellfish", keptSeason: 1, 1, 2, 3, 4, 5),
            };

            var titles = new Dictionary<int, Dictionary<int, string?>>
            {
                [1] = Titles("Alex vs Shellfish", "Alex vs Beef"),
                [2] = Titles("Alex vs Italian", "Alex vs Brunch"),
                [3] = Titles("Alex vs Chicken"),
                [4] = Titles("Alex vs Lobster"),
                [5] = Titles("Alex vs Mexico"),
            };

            orchestrator.ResolveSeasonForRepeatedLinks(links, titles);

            Assert.AreEqual(1, links[0].DetectedSeasonNumber);
        }

        [TestMethod]
        public void WhenNoSeasonNamesIt_TheSeasonItAlreadyHasStands()
        {
            var (orchestrator, _) = Build();

            // The databases spell this one differently, so nothing recognises it.
            var links = new List<EpisodeLink> { Repeated("julianna-margulies", keptSeason: 1, 1, 2, 7) };

            orchestrator.ResolveSeasonForRepeatedLinks(links, InaGarten());

            Assert.AreEqual(1, links[0].DetectedSeasonNumber,
                "Marguiles against margulies matches nothing, so there is nothing better");
        }

        [TestMethod]
        public void WhenTwoSeasonsNameIt_NeitherIsChosen()
        {
            var (orchestrator, _) = Build();

            var links = new List<EpisodeLink> { Repeated("the-reunion", keptSeason: 2, 2, 5) };

            var titles = new Dictionary<int, Dictionary<int, string?>>
            {
                [2] = Titles("The Reunion"),
                [5] = Titles("The Reunion"),
            };

            orchestrator.ResolveSeasonForRepeatedLinks(links, titles);

            Assert.AreEqual(2, links[0].DetectedSeasonNumber, "an ambiguous move is not made");
        }

        [TestMethod]
        public void ALinkThatWasNeverRepeated_IsNotTouched()
        {
            var (orchestrator, _) = Build();

            var links = new List<EpisodeLink> { Repeated("willie-geist", keptSeason: 1, 1) };

            orchestrator.ResolveSeasonForRepeatedLinks(links, InaGarten());

            Assert.AreEqual(1, links[0].DetectedSeasonNumber);
        }

        [TestMethod]
        public void TheMoveIsReportedInTheLog()
        {
            var (orchestrator, log) = Build();

            var links = new List<EpisodeLink> { Repeated("hoda-kotb", keptSeason: 1, 1, 7) };

            orchestrator.ResolveSeasonForRepeatedLinks(links, InaGarten());

            Assert.IsTrue(log.Any(l => l.Contains("hoda-kotb") && l.Contains("season 7")),
                "the move should be visible, not silent");
        }

        // ------------------------------------------------- dedupe keeps the candidates

        [TestMethod]
        public void DeduplicatingRecordsEverySeasonThatCarriedTheLink()
        {
            var (orchestrator, _) = Build();

            EpisodeLink In(int season, string slug) => new EpisodeLink
            {
                Url = "https://watch.foodnetwork.com/video/show/" + slug,
                LinkText = slug.Replace('-', ' '),
                DetectedSeasonNumber = season,
                DetectedEpisodeNumber = 1,
            };

            var links = new List<EpisodeLink>
            {
                In(1, "willie-geist"),
                In(1, "allison-janney"),
                In(7, "allison-janney"),
            };

            var kept = orchestrator.AssignEpisodeNumbers(
                links,
                new DownloadMetadata { NextSeasonNumber = 1, SelectedSeasons = new List<int> { 1, 7 } });

            var janney = kept.Single(l => l.Url.EndsWith("allison-janney"));

            CollectionAssert.AreEquivalent(
                new List<int> { 1, 7 },
                janney.CandidateSeasons,
                "both tabs that carried it must be remembered");
        }
    }
}
