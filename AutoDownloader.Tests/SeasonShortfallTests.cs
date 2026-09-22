using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers saying, per season, when a source carries fewer episodes than the databases
    /// list (AD-098).
    ///
    /// A five-season run compared the whole plan against one season's expected count and
    /// announced "this source lists 61 episode(s) but the databases expect 5 for season 1".
    /// That is nonsense, and it hid season 3 really being one episode short.
    /// </summary>
    [TestClass]
    public class SeasonShortfallTests
    {
        private static DownloadOrchestrator Orchestrator() => new DownloadOrchestrator(
            new MetadataService("no-key", "no-key"),
            new SearchService("no-key"),
            new XmlService(),
            new YtDlpService("yt-dlp.exe", "aria2c.exe", "agent", "best", "ffmpeg.exe", "none", false, "compatible"),
            new AutoConfirmPrompt());

        /// <summary>Runs the report and returns every line it logged.</summary>
        private static List<string> Notes(DownloadJobResult result, List<EpisodeLink> links)
        {
            var lines = new List<string>();

            var orchestrator = Orchestrator();
            orchestrator.OnLog += (_, e) => lines.Add(e.Message);

            orchestrator.ReportSeasonShortfalls(
                result, links, Path.Combine("X:", "Shows", "Alex vs America"),
                fallbackSeason: 1,
                expectedOverall: result.ExpectedEpisodeCount,
                offeredOverall: links.Count);

            return lines;
        }

        private static List<EpisodeLink> Links(params (int Season, int Count)[] seasons)
        {
            var links = new List<EpisodeLink>();

            foreach (var (season, count) in seasons)
            {
                for (int i = 1; i <= count; i++)
                {
                    links.Add(new EpisodeLink
                    {
                        Url = $"https://example.com/s{season}e{i}",
                        DetectedSeasonNumber = season,
                        DetectedEpisodeNumber = i,
                    });
                }
            }

            return links;
        }

        /// <summary>The real run: season 3 carries 9 where the databases list 10.</summary>
        private static DownloadJobResult AlexVsAmerica()
        {
            var result = new DownloadJobResult { ExpectedEpisodeCount = 5, EpisodesOffered = 61 };

            result.ExpectedBySeason[1] = 5;
            result.ExpectedBySeason[2] = 8;
            result.ExpectedBySeason[3] = 10;
            result.ExpectedBySeason[4] = 10;
            result.ExpectedBySeason[5] = 8;

            return result;
        }

        private static List<EpisodeLink> AlexVsAmericaLinks() =>
            Links((1, 10), (2, 16), (3, 9), (4, 10), (5, 16));

        [TestMethod]
        public void TheSeasonThatIsShort_IsNamed()
        {
            var notes = Notes(AlexVsAmerica(), AlexVsAmericaLinks());

            var shortfall = notes.SingleOrDefault(n => n.Contains("are not available here"));

            Assert.IsNotNull(shortfall, "the short season should be reported");
            StringAssert.Contains(shortfall, "season 3");
            StringAssert.Contains(shortfall, "10 episode(s)");
            StringAssert.Contains(shortfall, "carries 9");
            StringAssert.Contains(shortfall, "1 are not available here");
        }

        [TestMethod]
        public void TheNonsenseWholePlanComparison_IsGone()
        {
            var notes = Notes(AlexVsAmerica(), AlexVsAmericaLinks());

            Assert.IsFalse(
                notes.Any(n => n.Contains("61 episode(s)")),
                "the whole plan must never be compared against one season's expected count");
        }

        [TestMethod]
        public void ASeasonMatchingTheDatabases_IsNotMentioned()
        {
            var notes = Notes(AlexVsAmerica(), AlexVsAmericaLinks());

            Assert.IsFalse(notes.Any(n => n.Contains("season 4")),
                "season 4 offers exactly the 10 expected, so there is nothing to say");
        }

        [TestMethod]
        public void SeasonsCarryingExtras_AreExplainedAsExtras()
        {
            var notes = Notes(AlexVsAmerica(), AlexVsAmericaLinks());

            var extras = notes.Where(n => n.Contains("most likely specials")).ToList();

            Assert.AreEqual(3, extras.Count, "seasons 1, 2 and 5 all carry alternate cuts");
            Assert.IsTrue(extras.Any(n => n.Contains("season 2") && n.Contains("16 episode(s)")));
        }

        [TestMethod]
        public void WithNoPerSeasonCounts_TheSingleSeasonComparisonStillWorks()
        {
            // AD-057's case, which must keep working: 9 offered where the databases list 26.
            var result = new DownloadJobResult { ExpectedEpisodeCount = 26, EpisodesOffered = 9 };

            var notes = Notes(result, Links((1, 9)));

            Assert.AreEqual(1, notes.Count);
            StringAssert.Contains(notes[0], "17");
            StringAssert.Contains(notes[0], "a limit of the source");
        }
    }
}
