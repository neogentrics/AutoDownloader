using AutoDownloader.Core;
using AutoDownloader.Services;
using AutoDownloader.Services.Orchestration;
using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers taking real season and episode numbers from the JSON a page fetched (AD-069).
    ///
    /// A single-page app knows exactly which episode each tile is - it has to, to print
    /// "S5 E2" beside it - and then drops that on the floor in the markup. A real Food
    /// Network page opened on season 5 while the app, having been given no season, assumed
    /// season 1 and was about to file season 5 episodes as S01E01 onwards.
    /// </summary>
    [TestClass]
    public class JsonEpisodeExtractorTests
    {
        /// <summary>Shaped like the payload the live site actually returns.</summary>
        private const string RealisticPayload = @"{
          ""collections"": [{ ""items"": [
            { ""name"": ""Alex vs Chopped Judges"", ""path"": ""/video/alex-vs-america/alex-vs-chopped-judges"",
              ""seasonNumber"": 5, ""episodeNumber"": 2, ""videoType"": ""EPISODE"" },
            { ""name"": ""Alex vs Thailand"", ""path"": ""/video/alex-vs-america/alex-vs-thailand"",
              ""seasonNumber"": 5, ""episodeNumber"": 3, ""videoType"": ""EPISODE"" },
            { ""name"": ""Chefs' Cut: Alex vs Bacon"", ""path"": ""/video/alex-vs-america/chefs-cut-alex-vs-bacon"",
              ""seasonNumber"": 5, ""episodeNumber"": 102, ""videoType"": ""EPISODE"" }
          ]}]}";

        [TestMethod]
        public void RealNumbersAreTakenFromTheJson()
        {
            var episodes = JsonEpisodeExtractor.Extract(new[] { RealisticPayload });

            Assert.AreEqual(3, episodes.Count);
            Assert.IsTrue(episodes.All(e => e.IsNumbered));

            var thailand = episodes.Single(e => e.Path.EndsWith("alex-vs-thailand"));
            Assert.AreEqual(5, thailand.SeasonNumber);
            Assert.AreEqual(3, thailand.EpisodeNumber);
            Assert.AreEqual("Alex vs Thailand", thailand.Name);
        }

        [TestMethod]
        public void NestingDepthDoesNotMatter()
        {
            // Payloads bury their items several containers deep, and differently per site.
            string deep = @"{""a"":{""b"":{""c"":[{""d"":[
                {""url"":""/watch/show/ep-one"",""season"":2,""episode"":7,""title"":""Deep One""}]}]}}}";

            var episodes = JsonEpisodeExtractor.Extract(new[] { deep });

            Assert.AreEqual(1, episodes.Count);
            Assert.AreEqual(2, episodes[0].SeasonNumber);
            Assert.AreEqual(7, episodes[0].EpisodeNumber);
            Assert.AreEqual("Deep One", episodes[0].Name);
        }

        [TestMethod]
        public void NumbersSentAsStringsStillCount()
        {
            string payload = @"{""items"":[{""path"":""/v/s/e"",""seasonNumber"":""3"",""episodeNumber"":""4""}]}";

            var episodes = JsonEpisodeExtractor.Extract(new[] { payload });

            Assert.AreEqual(3, episodes[0].SeasonNumber);
            Assert.AreEqual(4, episodes[0].EpisodeNumber);
        }

        [TestMethod]
        public void ObjectsWithNoNumbering_AreIgnored()
        {
            // The nav, the footer and the hero banner all carry links.
            string payload = @"{""nav"":[{""path"":""/shows/all""},{""url"":""/help/contact""}]}";

            Assert.AreEqual(0, JsonEpisodeExtractor.Extract(new[] { payload }).Count);
        }

        [TestMethod]
        public void ProseIsNotMistakenForAPath()
        {
            string payload = @"{""items"":[{""description"":""Alex and Eric watch three chefs / judges"",
                ""path"":""/video/show/ep"",""seasonNumber"":1,""episodeNumber"":1}]}";

            var episodes = JsonEpisodeExtractor.Extract(new[] { payload });

            Assert.AreEqual("/video/show/ep", episodes[0].Path);
        }

        [TestMethod]
        public void TheSameEpisodeInSeveralPayloads_IsReturnedOnce()
        {
            var episodes = JsonEpisodeExtractor.Extract(new[] { RealisticPayload, RealisticPayload });

            Assert.AreEqual(3, episodes.Count);
        }

        [TestMethod]
        public void MalformedJson_IsSkippedRatherThanThrowing()
        {
            var episodes = JsonEpisodeExtractor.Extract(new[] { "{not json", "", RealisticPayload });

            Assert.AreEqual(3, episodes.Count);
        }

        // ------------------------------------------------------------ season adoption

        /// <summary>
        /// Real services with dummy settings. The orchestrator's constructor subscribes to
        /// their events, so nulls throw there; nothing below touches the network.
        /// </summary>
        private static DownloadOrchestrator Orchestrator() => new DownloadOrchestrator(
            new MetadataService("no-key", "no-key"),
            new SearchService("no-key"),
            new XmlService(),
            new YtDlpService("yt-dlp.exe", "aria2c.exe", "agent", "best", "ffmpeg.exe", "none", false, "compatible"),
            new AutoConfirmPrompt());

        private static EpisodeLink Ep(int season, int episode, string title) => new EpisodeLink
        {
            Url = $"https://example.com/video/show/s{season}e{episode}",
            LinkText = title,
            DetectedSeasonNumber = season,
            DetectedEpisodeNumber = episode,
        };

        [TestMethod]
        public void APageShowingAnotherSeason_CorrectsAnAssumedSeasonOne()
        {
            var metadata = new DownloadMetadata { NextSeasonNumber = 1, SeasonWasSpecified = false };

            var links = new List<EpisodeLink>
            {
                Ep(5, 1, "Alex vs TOC Winners"),
                Ep(5, 2, "Alex vs Chopped Judges"),
                Ep(5, 3, "Alex vs Thailand"),
            };

            var result = Orchestrator().AssignEpisodeNumbers(links, metadata);

            Assert.AreEqual(5, metadata.NextSeasonNumber, "should have adopted the season on the page");
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.All(l => l.DetectedSeasonNumber == 5));
        }

        [TestMethod]
        public void AnAskedForSeasonIsNeverOverridden()
        {
            // If the season was a real choice, a page showing something else is a mismatch to
            // warn about, not a correction to apply.
            var metadata = new DownloadMetadata { NextSeasonNumber = 1, SeasonWasSpecified = true };

            var links = new List<EpisodeLink> { Ep(5, 1, "a"), Ep(5, 2, "b") };

            Orchestrator().AssignEpisodeNumbers(links, metadata);

            Assert.AreEqual(1, metadata.NextSeasonNumber);
        }

        [TestMethod]
        public void WhenTheRequestedSeasonIsPresent_ItIsKept()
        {
            var metadata = new DownloadMetadata { NextSeasonNumber = 2, SeasonWasSpecified = false };

            var links = new List<EpisodeLink> { Ep(1, 1, "a"), Ep(2, 1, "b"), Ep(2, 2, "c") };

            var result = Orchestrator().AssignEpisodeNumbers(links, metadata);

            Assert.AreEqual(2, metadata.NextSeasonNumber);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(l => l.DetectedSeasonNumber == 2));
        }

        /// <summary>
        /// The case that broke a live run: the picker gathered five seasons, and the season
        /// filter then kept one and discarded the other four. Sixty-five episodes indexed,
        /// ten downloaded.
        /// </summary>
        [TestMethod]
        public void EverySelectedSeasonIsKept()
        {
            var metadata = new DownloadMetadata
            {
                NextSeasonNumber = 1,
                SeasonWasSpecified = true,
                SelectedSeasons = new List<int> { 1, 2, 3 },
            };

            var links = new List<EpisodeLink>
            {
                Ep(1, 1, "a"), Ep(1, 2, "b"),
                Ep(2, 1, "c"), Ep(2, 2, "d"),
                Ep(3, 1, "e"),
            };

            var result = Orchestrator().AssignEpisodeNumbers(links, metadata);

            Assert.AreEqual(5, result.Count, "every chosen season should survive");
            CollectionAssert.AreEquivalent(
                new[] { 1, 2, 3 },
                result.Select(l => l.DetectedSeasonNumber!.Value).Distinct().ToArray());
        }

        [TestMethod]
        public void SelectedSeasonsComeBackInOrder()
        {
            // Ordered so a multi-season run downloads season 1 before season 3, which is
            // also what makes the progress count read sensibly.
            var metadata = new DownloadMetadata
            {
                NextSeasonNumber = 1,
                SelectedSeasons = new List<int> { 1, 2 },
            };

            var links = new List<EpisodeLink> { Ep(2, 1, "c"), Ep(1, 2, "b"), Ep(1, 1, "a") };

            var result = Orchestrator().AssignEpisodeNumbers(links, metadata);

            CollectionAssert.AreEqual(
                new[] { (1, 1), (1, 2), (2, 1) },
                result.Select(l => (l.DetectedSeasonNumber!.Value, l.DetectedEpisodeNumber!.Value)).ToArray());
        }

        [TestMethod]
        public void AskingForOneSeasonStillFiltersToIt()
        {
            // A single choice must behave as before: the other seasons on the page go.
            var metadata = new DownloadMetadata
            {
                NextSeasonNumber = 2,
                SelectedSeasons = new List<int> { 2 },
            };

            var links = new List<EpisodeLink> { Ep(1, 1, "a"), Ep(2, 1, "b"), Ep(2, 2, "c") };

            var result = Orchestrator().AssignEpisodeNumbers(links, metadata);

            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(l => l.DetectedSeasonNumber == 2));
        }

        [TestMethod]
        public void TheDominantSeasonWins_WhenThePageMixesThem()
        {
            var metadata = new DownloadMetadata { NextSeasonNumber = 1, SeasonWasSpecified = false };

            var links = new List<EpisodeLink>
            {
                Ep(4, 1, "stray"),
                Ep(5, 1, "a"), Ep(5, 2, "b"), Ep(5, 3, "c"),
            };

            var result = Orchestrator().AssignEpisodeNumbers(links, metadata);

            Assert.AreEqual(5, metadata.NextSeasonNumber);
            Assert.AreEqual(3, result.Count);
        }
    }
}
