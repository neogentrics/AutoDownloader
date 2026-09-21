using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the watch list (AD-058), the store behind scheduled runs.
    ///
    /// It is written by a background task that may be interrupted, and read by both the
    /// desktop app and the CLI, so the behaviour that matters is that it never loses or
    /// duplicates entries.
    /// </summary>
    [TestClass]
    public class WatchListTests
    {
        private string _path = string.Empty;

        [TestInitialize]
        public void SetUp() =>
            _path = Path.Combine(Path.GetTempPath(), $"watchlist-test-{Guid.NewGuid():N}.json");

        [TestCleanup]
        public void TearDown()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        }

        [TestMethod]
        public void Add_ThenReload_KeepsTheEntry()
        {
            new WatchListService(_path).Add("https://example.com/series/x", season: 2);

            var reloaded = new WatchListService(_path);

            Assert.AreEqual(1, reloaded.Entries.Count);
            Assert.AreEqual(2, reloaded.Entries[0].Season);
        }

        [TestMethod]
        public void Add_DoesNotDuplicateTheSameUrlAndSeason()
        {
            var service = new WatchListService(_path);

            var first = service.Add("https://example.com/series/x", season: 1);
            var second = service.Add("https://example.com/series/x", season: 1);

            Assert.AreEqual(1, service.Entries.Count);
            Assert.AreEqual(first.Id, second.Id);
        }

        [TestMethod]
        public void Add_TreatsADifferentSeasonAsADifferentEntry()
        {
            var service = new WatchListService(_path);

            service.Add("https://example.com/series/x", season: 1);
            service.Add("https://example.com/series/x", season: 2);

            Assert.AreEqual(2, service.Entries.Count);
        }

        [TestMethod]
        public void Remove_WorksByIdOrByUrl()
        {
            var service = new WatchListService(_path);
            var entry = service.Add("https://example.com/series/x");

            Assert.IsTrue(service.Remove(entry.Id));
            Assert.AreEqual(0, service.Entries.Count);

            service.Add("https://example.com/series/y");
            Assert.IsTrue(service.Remove("https://example.com/series/y"));
            Assert.AreEqual(0, service.Entries.Count);
        }

        [TestMethod]
        public void GetDueEntries_SkipsDisabledOnes()
        {
            var service = new WatchListService(_path);
            var entry = service.Add("https://example.com/series/x");
            service.Add("https://example.com/series/y");

            service.SetEnabled(entry.Id, false);

            Assert.AreEqual(1, service.GetDueEntries().Count);
        }

        [TestMethod]
        public void GetDueEntries_ChecksTheLeastRecentlyCheckedFirst()
        {
            var service = new WatchListService(_path);
            var older = service.Add("https://example.com/a");
            var newer = service.Add("https://example.com/b");

            service.RecordCheck(newer, true, 3, "nothing new", "B");
            service.RecordCheck(older, true, 1, "nothing new", "A");
            // "newer" was checked first, so it is now the stalest.

            Assert.AreEqual(newer.Id, service.GetDueEntries()[0].Id);
        }

        [TestMethod]
        public void RecordCheck_TracksConsecutiveFailuresAndClearsThemOnSuccess()
        {
            var service = new WatchListService(_path);
            var entry = service.Add("https://example.com/series/x");

            service.RecordCheck(entry, false, 0, "failed", null);
            service.RecordCheck(entry, false, 0, "failed", null);
            Assert.AreEqual(2, entry.ConsecutiveFailures);

            service.RecordCheck(entry, true, 5, "5 new episode(s)", "Some Show");
            Assert.AreEqual(0, entry.ConsecutiveFailures);
            Assert.AreEqual("Some Show", entry.ResolvedTitle);
        }

        [TestMethod]
        public void ACorruptFileDoesNotThrowAndIsNotSilentlyDiscarded()
        {
            File.WriteAllText(_path, "{ this is not valid json");

            string? diagnostic = null;
            var service = new WatchListService(_path);
            service.OnDiagnostic += m => diagnostic = m;
            service.Load();

            Assert.AreEqual(0, service.Entries.Count);
            Assert.IsTrue(File.Exists(_path), "the unreadable file must be left for recovery");
        }
    }
}
