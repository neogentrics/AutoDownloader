using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers treating the listing's own numbering as authoritative (AD-103).
    ///
    /// Food Network captions each tile "S12 E1Cooking for Jeffrey: Birthday21mTV-G...". That
    /// is the site saying which episode it is about to serve, and it covers seasons the
    /// databases have never heard of - TVDB returns nothing for Barefoot Contessa seasons 13
    /// through 19, all of which the site numbers and names.
    /// </summary>
    [TestClass]
    public class ListingNumberingTests
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

        private static EpisodeLink Stated(int season, int episode, string slug, string title) =>
            new EpisodeLink
            {
                Url = "https://watch.foodnetwork.com/video/show/" + slug,
                LinkText = title,
                DetectedSeasonNumber = season,
                DetectedEpisodeNumber = episode,
                NumbersFromListing = true,
                Description = "A summary.",
                AirDate = new DateTime(2016, 10, 16),
                RuntimeMinutes = 21,
                Rating = "TV-G",
            };

        private static EpisodeLink Bare(int season, int? episode, string slug, string? text = null) =>
            new EpisodeLink
            {
                Url = "https://watch.foodnetwork.com/video/show/" + slug,
                LinkText = text,
                DetectedSeasonNumber = season,
                DetectedEpisodeNumber = episode,
            };

        private static DownloadMetadata Meta(params int[] seasons) => new DownloadMetadata
        {
            NextSeasonNumber = 1,
            SelectedSeasons = seasons.ToList(),
        };

        /// <summary>
        /// The show page lists an episode twice: once as a bare "Watch Now" tile and once
        /// with its caption. Which comes first is an accident of the page.
        /// </summary>
        [TestMethod]
        public void ACaptionedDuplicate_UpgradesTheBareCopy()
        {
            var (orchestrator, _) = Build();

            var links = new List<EpisodeLink>
            {
                Bare(1, null, "cooking-for-jeffrey-birthday", "Watch Now"),
                Stated(12, 1, "cooking-for-jeffrey-birthday", "Cooking for Jeffrey: Birthday"),
            };

            var kept = orchestrator.AssignEpisodeNumbers(links, Meta(1, 12));

            var only = kept.Single();

            Assert.IsTrue(only.NumbersFromListing, "the captioned copy should win");
            Assert.AreEqual(12, only.DetectedSeasonNumber);
            Assert.AreEqual(1, only.DetectedEpisodeNumber);
            Assert.AreEqual("Cooking for Jeffrey: Birthday", only.LinkText);
            Assert.AreEqual("A summary.", only.Description);

            // Every field, not just the two that were noticed first.
            Assert.AreEqual(new DateTime(2016, 10, 16), only.AirDate);
            Assert.AreEqual(21, only.RuntimeMinutes);
            Assert.AreEqual("TV-G", only.Rating);
        }

        [TestMethod]
        public void NumbersFromTheListing_AreNotRenumbered()
        {
            var (orchestrator, _) = Build();

            // Two seasons whose stated numbers both start at 1 - which the continuous-
            // numbering correction would have taken as proof of nothing to do, and a page
            // numbering straight through would have had it renumber these.
            var links = new List<EpisodeLink>
            {
                Stated(12, 1, "a", "A"), Stated(12, 2, "b", "B"),
                Stated(19, 1, "c", "C"), Stated(19, 2, "d", "D"),
            };

            var kept = orchestrator.AssignEpisodeNumbers(links, Meta(12, 19));

            Assert.AreEqual(1, kept.Single(l => l.Url.EndsWith("c")).DetectedEpisodeNumber);
            Assert.AreEqual(2, kept.Single(l => l.Url.EndsWith("d")).DetectedEpisodeNumber);
        }

        [TestMethod]
        public void ASeasonTheListingNumbered_IsNotRematchedByTitle()
        {
            var (orchestrator, log) = Build();

            // The site calls it "Cooking for Jeffrey: Birthday"; the databases call it
            // "Cooking for Jeffrey: Jeffrey's Birthday Dinner". Matching on title fails, and
            // before this the season silently fell back to page order.
            var links = new List<EpisodeLink>
            {
                Stated(12, 1, "cooking-for-jeffrey-birthday", "Cooking for Jeffrey: Birthday"),
                Stated(12, 2, "cooking-for-jeffrey-deans", "Cooking for Jeffrey: Deans"),
            };

            var titles = new Dictionary<int, Dictionary<int, string?>>
            {
                [12] = new Dictionary<int, string?>
                {
                    [1] = "Cooking for Jeffrey: Jeffrey's Birthday Dinner",
                    [2] = "Cooking for Jeffrey: Deans for Dinner",
                },
            };

            orchestrator.AlignNumbersToDatabase(links, titles, Meta(12));

            Assert.AreEqual(1, links.Single(l => l.Url.EndsWith("birthday")).DetectedEpisodeNumber);
            Assert.AreEqual(2, links.Single(l => l.Url.EndsWith("deans")).DetectedEpisodeNumber);

            Assert.IsTrue(log.Any(l => l.Contains("numbers 2 of its episodes itself")));
        }

        /// <summary>
        /// Skipping the whole season over its captioned tiles left the uncaptioned one with
        /// no number at all, which is how "S01E - steak-and-sides" happened.
        /// </summary>
        [TestMethod]
        public void AnUncaptionedLink_IsStillPlacedAgainstTheNumbersLeft()
        {
            var (orchestrator, _) = Build();

            var links = new List<EpisodeLink>
            {
                Bare(1, null, "steak-and-sides", "Watch Now"),
                Stated(1, 2, "perfect-dinner-party", "Perfect Dinner Party"),
                Stated(1, 3, "chicken-all-the-time", "Chicken All the Time"),
            };

            var titles = new Dictionary<int, Dictionary<int, string?>>
            {
                [1] = new Dictionary<int, string?>
                {
                    [1] = "Steak and Sides",
                    [2] = "Perfect Dinner Party",
                    [3] = "Chicken All the Time",
                },
            };

            orchestrator.AlignNumbersToDatabase(links, titles, Meta(1));

            Assert.AreEqual(
                1,
                links.Single(l => l.Url.EndsWith("steak-and-sides")).DetectedEpisodeNumber,
                "E01 was the number left, and the slug names it");
        }

        [TestMethod]
        public void ARejectedTitleMatch_IsReportedRatherThanSilent()
        {
            var (orchestrator, log) = Build();

            var links = new List<EpisodeLink>
            {
                Bare(3, 1, "9f2a41bc"), Bare(3, 2, "7d0e55aa"), Bare(3, 3, "1b83cc90"),
            };

            var titles = new Dictionary<int, Dictionary<int, string?>>
            {
                [3] = new Dictionary<int, string?> { [1] = "One", [2] = "Two", [3] = "Three" },
            };

            orchestrator.AlignNumbersToDatabase(links, titles, Meta(3));

            Assert.IsTrue(
                log.Any(l => l.Contains("too few to trust")),
                "falling back to page order must be visible in the log");
        }
    }
}
