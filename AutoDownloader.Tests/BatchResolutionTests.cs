using AutoDownloader.Core;
using AutoDownloader.Services.Orchestration;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers asking the user once per batch rather than once per item (AD-066).
    ///
    /// A batch of ten links used to stop ten times to ask which show each one was, scattered
    /// through the run, so it could not be left alone. The point of the cache is that the
    /// second time a question comes up it is not asked - so what these tests check is how
    /// many times the underlying prompt was actually reached.
    /// </summary>
    [TestClass]
    public class BatchResolutionTests
    {
        /// <summary>A prompt that records how often it was actually consulted.</summary>
        private sealed class CountingPrompt : IUserPrompt
        {
            public int ShowNameAsks;
            public int SeriesAsks;
            public int OverwriteAsks;

            public SeriesCandidate? SeriesAnswer;
            public OverwriteDecision OverwriteAnswer = OverwriteDecision.Skip;

            public Task<string?> ConfirmShowNameAsync(string suggestedName, CancellationToken ct = default)
            {
                ShowNameAsks++;
                return Task.FromResult<string?>(suggestedName);
            }

            public Task<SeriesCandidate?> ChooseSeriesAsync(
                string searchTerm, IReadOnlyList<SeriesCandidate> candidates,
                SeriesCandidate? suggested, CancellationToken ct = default)
            {
                SeriesAsks++;
                return Task.FromResult(SeriesAnswer ?? suggested);
            }

            public Task<OverwriteDecision> ConfirmOverwriteAsync(
                ExistingEpisode existing, CancellationToken ct = default)
            {
                OverwriteAsks++;
                return Task.FromResult(OverwriteAnswer);
            }
        }

        private static SeriesCandidate Candidate(string title, int year) =>
            new SeriesCandidate { Id = year, Title = title, Year = year, Source = "TMDB" };

        private static (CountingPrompt Inner, CachingUserPrompt Prompt) Build()
        {
            var inner = new CountingPrompt();
            return (inner, new CachingUserPrompt(inner, new SeriesResolutionCache()));
        }

        [TestMethod]
        public async Task TheSameShowIsOnlyAskedAboutOnce()
        {
            var (inner, prompt) = Build();
            var candidates = new[] { Candidate("Teen Titans", 2003), Candidate("Teen Titans Go!", 2013) };
            inner.SeriesAnswer = candidates[0];

            // The download pass asks the same question the identify pass already asked.
            var first = await prompt.ChooseSeriesAsync("Teen Titans", candidates, null);
            var second = await prompt.ChooseSeriesAsync("Teen Titans", candidates, null);

            Assert.AreEqual(1, inner.SeriesAsks);
            Assert.AreEqual(first, second);
            Assert.AreEqual(2003, second!.Year);
        }

        [TestMethod]
        public async Task DifferentShowsAreStillAskedAboutSeparately()
        {
            var (inner, prompt) = Build();
            var candidates = new[] { Candidate("Popeye", 1933) };

            await prompt.ChooseSeriesAsync("Popeye", candidates, candidates[0]);
            await prompt.ChooseSeriesAsync("Duck Dodgers", candidates, candidates[0]);

            Assert.AreEqual(2, inner.SeriesAsks);
        }

        [TestMethod]
        public async Task CancellingOneItemDoesNotCancelTheRest()
        {
            // A null answer is a cancel. Remembering it would silently skip every later item
            // that happened to ask the same question.
            var (inner, prompt) = Build();
            inner.SeriesAnswer = null;
            var candidates = new[] { Candidate("Popeye", 1933) };

            await prompt.ChooseSeriesAsync("Popeye", candidates, null);
            await prompt.ChooseSeriesAsync("Popeye", candidates, null);

            Assert.AreEqual(2, inner.SeriesAsks, "a cancel must not be cached");
        }

        [TestMethod]
        public async Task TheShowNameIsConfirmedOnlyOnce()
        {
            var (inner, prompt) = Build();

            await prompt.ConfirmShowNameAsync("Ben-To");
            await prompt.ConfirmShowNameAsync("Ben-To");
            await prompt.ConfirmShowNameAsync("ben-to");

            Assert.AreEqual(1, inner.ShowNameAsks, "casing should not defeat the cache");
        }

        [TestMethod]
        public async Task ReplaceAllAppliesToTheWholeBatch()
        {
            // "All" means all. Answering Replace All on the first show should not mean being
            // asked again on the second.
            var (inner, prompt) = Build();
            inner.OverwriteAnswer = OverwriteDecision.OverwriteAll;

            var file = new ExistingEpisode { Path = "a.mp4" };

            var first = await prompt.ConfirmOverwriteAsync(file);
            var second = await prompt.ConfirmOverwriteAsync(file);
            var third = await prompt.ConfirmOverwriteAsync(file);

            Assert.AreEqual(1, inner.OverwriteAsks);
            Assert.AreEqual(OverwriteDecision.OverwriteAll, first);
            Assert.AreEqual(OverwriteDecision.OverwriteAll, second);
            Assert.AreEqual(OverwriteDecision.OverwriteAll, third);
        }

        [TestMethod]
        public async Task KeepAllAlsoAppliesToTheWholeBatch()
        {
            var (inner, prompt) = Build();
            inner.OverwriteAnswer = OverwriteDecision.SkipAll;

            await prompt.ConfirmOverwriteAsync(new ExistingEpisode { Path = "a.mp4" });
            var again = await prompt.ConfirmOverwriteAsync(new ExistingEpisode { Path = "b.mp4" });

            Assert.AreEqual(1, inner.OverwriteAsks);
            Assert.AreEqual(OverwriteDecision.SkipAll, again);
        }

        [TestMethod]
        public async Task ASingleFileAnswerIsNotTreatedAsAnAnswerForEveryFile()
        {
            // Plain Keep applies to that one file only, so the next file must still ask.
            var (inner, prompt) = Build();
            inner.OverwriteAnswer = OverwriteDecision.Skip;

            await prompt.ConfirmOverwriteAsync(new ExistingEpisode { Path = "a.mp4" });
            await prompt.ConfirmOverwriteAsync(new ExistingEpisode { Path = "b.mp4" });

            Assert.AreEqual(2, inner.OverwriteAsks);
        }

        [TestMethod]
        public void AFreshCacheKnowsNothing()
        {
            var cache = new SeriesResolutionCache();

            Assert.IsFalse(cache.TryGetShowName("anything", out _));
            Assert.IsFalse(cache.TryGetSeries("anything", out _));
            Assert.IsNull(cache.BatchOverwrite);
            Assert.AreEqual(0, cache.Count);
        }
    }
}
