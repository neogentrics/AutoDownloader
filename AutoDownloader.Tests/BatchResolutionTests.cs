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

            public int QualityAsks;
            public string? QualityAnswer;

            public Task<string?> ChooseQualityAsync(
                string showTitle, IReadOnlyList<FormatOption> options,
                CancellationToken ct = default)
            {
                QualityAsks++;
                return Task.FromResult(QualityAnswer);
            }

            public int SeasonAsks;
            public IReadOnlyList<int>? SeasonAnswer;

            /// <summary>
            /// Separate from a null answer, because null already means "nothing set" here.
            /// Conflating the two is what made an earlier version of these tests never
            /// exercise the cancel path at all.
            /// </summary>
            public bool SeasonCancels;

            public Task<IReadOnlyList<int>?> ChooseSeasonsAsync(
                string showTitle, IReadOnlyList<int> available, int showing,
                CancellationToken ct = default)
            {
                SeasonAsks++;

                if (SeasonCancels) return Task.FromResult<IReadOnlyList<int>?>(null);

                return Task.FromResult(SeasonAnswer ?? (IReadOnlyList<int>?)new[] { showing });
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
        public async Task TheSeasonChoiceIsOnlyAskedOnce()
        {
            // The identify pass asks; the download pass must not ask again.
            var (inner, prompt) = Build();
            inner.SeasonAnswer = new[] { 1, 2, 3 };

            var first = await prompt.ChooseSeasonsAsync("Barefoot Contessa", new[] { 1, 2, 3, 4 }, 1);
            var second = await prompt.ChooseSeasonsAsync("Barefoot Contessa", new[] { 1, 2, 3, 4 }, 1);

            Assert.AreEqual(1, inner.SeasonAsks);
            CollectionAssert.AreEqual(first!.ToArray(), second!.ToArray());
        }

        [TestMethod]
        public async Task CancellingTheSeasonChoiceIsNotRemembered()
        {
            var (inner, prompt) = Build();
            inner.SeasonCancels = true;

            // A cancel must not be cached, or giving up on one show silently skips the rest.
            var cancelled = await prompt.ChooseSeasonsAsync("A Show", new[] { 1, 2 }, 1);
            Assert.IsNull(cancelled);

            inner.SeasonCancels = false;
            inner.SeasonAnswer = new[] { 2 };
            var again = await prompt.ChooseSeasonsAsync("A Show", new[] { 1, 2 }, 1);

            Assert.AreEqual(2, inner.SeasonAsks);
            CollectionAssert.AreEqual(new[] { 2 }, again!.ToArray());
        }

        [TestMethod]
        public async Task TheQualityChoiceIsOnlyAskedOnce()
        {
            var (inner, prompt) = Build();
            inner.QualityAnswer = "bestvideo[height<=1080][tbr<=4528]+bestaudio";

            var options = new[] { new FormatOption { Height = 1080, Width = 1920, Bitrate = 4312 } };

            var first = await prompt.ChooseQualityAsync("Alex vs America", options);
            var second = await prompt.ChooseQualityAsync("Alex vs America", options);

            Assert.AreEqual(1, inner.QualityAsks);
            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public async Task KeepingTheSettingIsRememberedToo()
        {
            // Null means "leave Preferences alone". That is an answer, and asking again would
            // be asking the same question about the same show once per episode.
            var (inner, prompt) = Build();
            inner.QualityAnswer = null;

            await prompt.ChooseQualityAsync("A Show", new FormatOption[0]);
            var again = await prompt.ChooseQualityAsync("A Show", new FormatOption[0]);

            Assert.AreEqual(1, inner.QualityAsks);
            Assert.IsNull(again);
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
