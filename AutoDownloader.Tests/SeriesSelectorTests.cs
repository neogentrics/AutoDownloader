using AutoDownloader.Core;
using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers when a search result set needs a human to choose (AD-052).
    ///
    /// Taken from a real failure: a Tubi URL for the 2003 "Teen Titans" resolved to
    /// "Teen Titans Go!" (2013), because that ranks higher. Every episode would have been
    /// named from the wrong series, and nothing in the output would have looked wrong.
    /// </summary>
    [TestClass]
    public class SeriesSelectorTests
    {
        private static SeriesCandidate C(string title, int? year, int id = 0) =>
            new SeriesCandidate { Title = title, Year = year, Id = id, Source = "TMDB" };

        [TestMethod]
        public void Choose_AsksWhenAShowAndItsRebootBothMatch()
        {
            // TMDB returns the reboot first, which is exactly the trap.
            var results = new[] { C("Teen Titans Go!", 2013, 1), C("Teen Titans", 2003, 2) };

            var selection = SeriesSelector.Choose("teen titans", results);

            Assert.AreEqual(SeriesSelectionKind.Ambiguous, selection.Kind);
            Assert.AreEqual(2, selection.Candidates.Count);
            Assert.AreEqual("Teen Titans", selection.Selected!.Title,
                "the exact title match should be the default, not the higher-ranked reboot");
            Assert.AreEqual(2003, selection.Selected.Year);
        }

        [TestMethod]
        public void Choose_PrefersTheEarliestYearAmongIdenticalTitles()
        {
            // Some remakes keep the name exactly; then only the year separates them.
            var results = new[] { C("Battlestar Galactica", 2004, 1), C("Battlestar Galactica", 1978, 2) };

            var selection = SeriesSelector.Choose("battlestar galactica", results);

            Assert.AreEqual(SeriesSelectionKind.Ambiguous, selection.Kind);
            Assert.AreEqual(1978, selection.Selected!.Year, "the original should be the default");
        }

        [TestMethod]
        public void Choose_DoesNotAskWhenOnlyOneShowCarriesTheName()
        {
            var results = new[] { C("Ben-To", 2011, 1), C("Something Else Entirely", 2015, 2) };

            var selection = SeriesSelector.Choose("ben to", results);

            Assert.AreEqual(SeriesSelectionKind.Automatic, selection.Kind);
            Assert.AreEqual("Ben-To", selection.Selected!.Title);
        }

        [TestMethod]
        public void Choose_DoesNotAskForASingleResult()
        {
            var selection = SeriesSelector.Choose("firefly", new[] { C("Firefly", 2002, 1) });

            Assert.AreEqual(SeriesSelectionKind.Automatic, selection.Kind);
        }

        [TestMethod]
        public void Choose_ReportsNoneWhenNothingMatched()
        {
            Assert.AreEqual(SeriesSelectionKind.None,
                SeriesSelector.Choose("nothing", new SeriesCandidate[0]).Kind);
        }

        [TestMethod]
        public void Choose_AsksWhenNothingMatchesTheNameClosely()
        {
            // The top result is then only a guess, so it should be confirmed rather than assumed.
            var results = new[] { C("Some Unrelated Show", 2019, 1), C("Another One", 2020, 2) };

            var selection = SeriesSelector.Choose("mega man star force", results);

            Assert.AreEqual(SeriesSelectionKind.Ambiguous, selection.Kind);
            StringAssert.Contains(selection.Reason!, "no exact title match");
        }

        [TestMethod]
        public void Choose_IgnoresPunctuationAndCaseWhenComparing()
        {
            var results = new[] { C("Ben-To", 2011, 1) };

            Assert.AreEqual(SeriesSelectionKind.Automatic, SeriesSelector.Choose("BEN TO", results).Kind);
        }
    }
}
