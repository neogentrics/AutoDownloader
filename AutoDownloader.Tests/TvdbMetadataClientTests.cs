using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers TVDB series-id parsing (AD-023).
    ///
    /// This one line was the entire bug. TVDB v4 search results identify a series as
    /// "series-78874", and the old reflection fed that straight into Convert.ToInt64, which
    /// threw and was swallowed - so TVDB silently contributed nothing to every lookup.
    /// </summary>
    [TestClass]
    public class TvdbMetadataClientTests
    {
        [TestMethod]
        public void TryParseSeriesId_HandlesThePrefixedFormTvdbActuallyReturns()
        {
            Assert.IsTrue(TvdbMetadataClient.TryParseSeriesId("series-78874", out int id));
            Assert.AreEqual(78874, id);
        }

        [TestMethod]
        public void TryParseSeriesId_HandlesABareNumber()
        {
            Assert.IsTrue(TvdbMetadataClient.TryParseSeriesId("78874", out int id));
            Assert.AreEqual(78874, id);
        }

        [TestMethod]
        [DataRow("movie-12345", 12345)]
        [DataRow("series-1", 1)]
        public void TryParseSeriesId_AcceptsOtherPrefixes(string raw, int expected)
        {
            Assert.IsTrue(TvdbMetadataClient.TryParseSeriesId(raw, out int id));
            Assert.AreEqual(expected, id);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("series-")]
        [DataRow("no-digits-here")]
        [DataRow("series-0")]
        public void TryParseSeriesId_RejectsWhatItCannotUse(string? raw)
        {
            Assert.IsFalse(TvdbMetadataClient.TryParseSeriesId(raw, out int id));
            Assert.AreEqual(0, id);
        }
    }
}
