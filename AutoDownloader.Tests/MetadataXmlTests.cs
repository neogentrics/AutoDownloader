using System.Xml.Linq;
using AutoDownloader.Core;
using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers what series_metadata.xml records (AD-104).
    ///
    /// It used to carry an episode number and a title and nothing else, while the listing had
    /// been handing over a summary, an air date, a runtime and a rating for free.
    /// </summary>
    [TestClass]
    public class MetadataXmlTests
    {
        private string _folder = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "adl-xml-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_folder, recursive: true); } catch { }
        }

        private XElement Saved() =>
            XDocument.Load(Path.Combine(_folder, "series_metadata.xml")).Root!;

        private async Task<XElement> Save(int season, params DownloadEpisode[] episodes)
        {
            await new XmlService().SaveMetadataAsync(_folder, new DownloadMetadata
            {
                OfficialTitle = "Barefoot Contessa: Back to Basics",
                SeriesId = 208731,
                NextSeasonNumber = season,
                ExpectedEpisodeCount = episodes.Length,
                Episodes = episodes.ToList(),
            });

            return Saved();
        }

        private static DownloadEpisode Full(int number) => new DownloadEpisode
        {
            EpisodeNumber = number,
            EpisodeTitle = "Cooking for Jeffrey: Jeffrey's Birthday Dinner",
            Description = "Ina throws an Italian-themed birthday dinner with a surprise for Jeffrey.",
            AirDate = new DateTime(2016, 10, 16),
            RuntimeMinutes = 21,
            Rating = "TV-G",
        };

        [TestMethod]
        public async Task TheSummaryAirDateRuntimeAndRating_AreAllRecorded()
        {
            var root = await Save(12, Full(1));

            var episode = root.Descendants("Episode").Single();

            StringAssert.Contains(episode.Element("Description")!.Value, "Italian-themed birthday");
            Assert.AreEqual("2016-10-16", episode.Element("AirDate")!.Value);
            Assert.AreEqual("21", episode.Element("RuntimeMinutes")!.Value);
            Assert.AreEqual("TV-G", episode.Element("Rating")!.Value);
        }

        [TestMethod]
        public async Task WhatTheSourceDidNotSay_IsLeftOutRatherThanWrittenEmpty()
        {
            var root = await Save(12, new DownloadEpisode { EpisodeNumber = 1, EpisodeTitle = "Bare" });

            var episode = root.Descendants("Episode").Single();

            Assert.AreEqual("Bare", episode.Element("Title")!.Value);
            Assert.IsNull(episode.Element("Description"), "an empty element says nothing");
            Assert.IsNull(episode.Element("AirDate"));
            Assert.IsNull(episode.Element("RuntimeMinutes"));
            Assert.IsNull(episode.Element("Rating"));
        }

        [TestMethod]
        public async Task SeveralSeasons_AccumulateInOneFile()
        {
            await Save(12, Full(1), Full(2));
            await Save(19, Full(1));

            var root = Saved();

            var seasons = root.Elements("Season")
                              .Select(e => (string?)e.Attribute("Number"))
                              .ToList();

            CollectionAssert.AreEquivalent(new List<string?> { "12", "19" }, seasons);
            Assert.AreEqual(3, root.Descendants("Episode").Count());
        }

        [TestMethod]
        public async Task SavingASeasonAgain_ReplacesItRatherThanDoublingIt()
        {
            await Save(12, Full(1), Full(2));
            await Save(12, Full(1));

            var root = Saved();

            Assert.AreEqual(1, root.Elements("Season").Count());
            Assert.AreEqual(1, root.Descendants("Episode").Count());
        }

        [TestMethod]
        public async Task TheShowItselfIsStillRecorded()
        {
            var root = await Save(12, Full(1));

            Assert.AreEqual("Barefoot Contessa: Back to Basics", root.Element("Title")!.Value);
            Assert.AreEqual("208731", root.Element("SeriesId")!.Value);
        }
    }
}
