using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers reading a DASH manifest to find out what is in it (AD-084).
    ///
    /// A run produced a thirty-second car advert named as episode one and reported success.
    /// The stream was not the wrong one - it was the right one with the adverts stitched into
    /// the programme's own timeline, and the downloader took a single period. The shape below
    /// is the real Discovery+ manifest that caused it.
    /// </summary>
    [TestClass]
    public class ManifestInspectorTests
    {
        /// <summary>
        /// The real thing, reduced to the parts that matter: four pieces of programme broken
        /// up by three advert breaks of thirty-second spots.
        /// </summary>
        private const string StitchedManifest = @"<MPD mediaPresentationDuration=""PT26M31.0S"">
            <Period id=""0"" duration=""PT442.1S""/>
            <Period id=""442141"" duration=""PT30.0S""/>
            <Period id=""472171"" duration=""PT30.0S""/>
            <Period id=""502201"" duration=""PT30.0S""/>
            <Period id=""532231"" duration=""PT30.0S""/>
            <Period id=""562261"" duration=""PT304.4S""/>
            <Period id=""866631"" duration=""PT30.0S""/>
            <Period id=""986751"" duration=""PT266.4S""/>
            <Period id=""1343240"" duration=""PT248.2S""/>
        </MPD>";

        private const string CleanManifest = @"<MPD mediaPresentationDuration=""PT21M0.0S"">
            <Period id=""0"" duration=""PT1260.7S""/>
        </MPD>";

        [TestMethod]
        public void StitchedAdvertsAreRecognised()
        {
            var facts = ManifestInspector.Parse(StitchedManifest);

            Assert.IsTrue(facts.HasStitchedAdverts);
            Assert.IsFalse(facts.IsSinglePeriod);
            Assert.AreEqual(4, facts.ContentPeriods.Count());
            Assert.AreEqual(5, facts.AdvertPeriods.Count());
        }

        [TestMethod]
        public void ProgrammeLengthExcludesTheAdverts()
        {
            var facts = ManifestInspector.Parse(StitchedManifest);

            // 442.1 + 304.4 + 266.4 + 248.2, and nothing else.
            Assert.AreEqual(1261.1, facts.ContentSeconds, 0.5);
            Assert.AreEqual(150.0, facts.AdvertSecondsTotal, 0.5);
        }

        [TestMethod]
        public void AnOrdinaryStreamIsNotFlagged()
        {
            var facts = ManifestInspector.Parse(CleanManifest);

            Assert.IsFalse(facts.HasStitchedAdverts);
            Assert.IsTrue(facts.IsSinglePeriod);
            Assert.AreEqual(1260.7, facts.ContentSeconds, 0.5);
        }

        [TestMethod]
        public void TheTotalIsReadFromTheManifest()
        {
            Assert.AreEqual(26 * 60 + 31, ManifestInspector.Parse(StitchedManifest).TotalSeconds, 0.5);
        }

        [TestMethod]
        [DataRow("PT30.0S", 30.0)]
        [DataRow("PT442.1S", 442.1)]
        [DataRow("PT22M13.4S", 1333.4)]
        [DataRow("PT1H2M3S", 3723.0)]
        [DataRow("P1DT1H", 90000.0)]
        public void DurationsAreReadInEveryShapeAManifestUses(string value, double expected)
        {
            Assert.AreEqual(expected, ManifestInspector.ParseIso8601(value), 0.01);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("nonsense")]
        [DataRow("22 minutes")]
        public void AnUnreadableDurationIsZeroRatherThanAThrow(string value)
        {
            // A malformed duration must not fail a download that would otherwise work.
            Assert.AreEqual(0, ManifestInspector.ParseIso8601(value));
        }

        [TestMethod]
        public void AManifestWithNoPeriodsIsHarmless()
        {
            var facts = ManifestInspector.Parse("<MPD/>");

            Assert.IsFalse(facts.HasStitchedAdverts);
            Assert.IsTrue(facts.IsSinglePeriod);
            Assert.AreEqual(0, facts.ContentSeconds);
        }

        [TestMethod]
        public void AnAllAdvertManifestIsNotCalledStitched()
        {
            // Nothing to interleave with, so this is a different problem - and calling it
            // stitched would produce a warning about splitting up a programme that is absent.
            var facts = ManifestInspector.Parse(
                @"<MPD><Period duration=""PT30.0S""/><Period duration=""PT30.0S""/></MPD>");

            Assert.IsFalse(facts.HasStitchedAdverts);
            Assert.AreEqual(0, facts.ContentSeconds);
        }
    }
}
