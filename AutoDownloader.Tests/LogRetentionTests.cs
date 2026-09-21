using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers which session logs get deleted (AD-063).
    ///
    /// Retention used to be "keep the last 20", which was under a day of real use, so a log
    /// from the run somebody wanted to ask about was usually already gone. It is now by age,
    /// with a size backstop - and since this is a delete path, the rules are tested rather
    /// than reasoned about.
    /// </summary>
    [TestClass]
    public class LogRetentionTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        private static SessionLogWriter.LogFileInfo Log(string name, int daysOld, long bytes = 4096) =>
            new SessionLogWriter.LogFileInfo(name, bytes, Now.AddDays(-daysOld));

        [TestMethod]
        public void RecentLogs_AreKept()
        {
            var logs = new[] { Log("a", 1), Log("b", 5), Log("c", 29) };

            var expired = SessionLogWriter.SelectExpiredLogs(logs, Now);

            Assert.AreEqual(0, expired.Count);
        }

        [TestMethod]
        public void LogsOlderThanThirtyDays_AreDeleted()
        {
            var logs = new[]
            {
                Log("new1", 0), Log("new2", 1), Log("new3", 2), Log("new4", 3), Log("new5", 4),
                Log("old", 31),
            };

            var expired = SessionLogWriter.SelectExpiredLogs(logs, Now);

            CollectionAssert.AreEqual(new[] { "old" }, expired.ToArray());
        }

        [TestMethod]
        public void TheLastFiveSurviveHoweverOldTheyAre()
        {
            // The most recent runs are the ones somebody is about to ask about. Coming back
            // to the app after a year away should not mean coming back to no history at all.
            var logs = new[]
            {
                Log("a", 400), Log("b", 401), Log("c", 402), Log("d", 403), Log("e", 404),
                Log("f", 405),
            };

            var expired = SessionLogWriter.SelectExpiredLogs(logs, Now);

            CollectionAssert.AreEqual(new[] { "f" }, expired.ToArray());
        }

        [TestMethod]
        public void TheSizeCapTrimsEvenWhenNothingIsOld()
        {
            // 40 files of 8 MB is 320 MB, all written today.
            var logs = Enumerable.Range(0, 40)
                .Select(i => Log($"log{i:00}", i, 8L * 1024 * 1024))
                .ToList();

            var expired = SessionLogWriter.SelectExpiredLogs(logs, Now);

            Assert.IsTrue(expired.Count > 0, "the cap should have trimmed something");

            // Whatever survives is the newest run of files, not an arbitrary subset.
            var kept = logs.Select(l => l.Path).Except(expired).ToList();
            CollectionAssert.AreEqual(
                logs.Take(kept.Count).Select(l => l.Path).ToArray(), kept.ToArray());
        }

        [TestMethod]
        public void OrdinaryUse_NeverReachesTheSizeCap()
        {
            // Real sessions run a few KB to a few hundred KB. Thirty days of heavy use - ten
            // runs a day at 500 KB - is ~150 MB, and must not be trimmed by size.
            var logs = Enumerable.Range(0, 300)
                .Select(i => Log($"log{i:000}", i / 10, 500L * 1024))
                .ToList();

            var expired = SessionLogWriter.SelectExpiredLogs(logs, Now);

            Assert.AreEqual(0, expired.Count, "thirty days of normal use should survive");
        }

        [TestMethod]
        public void OneRunawayLog_DoesNotTakeTheRecentOnesWithIt()
        {
            var logs = new[]
            {
                Log("huge", 0, 900L * 1024 * 1024),
                Log("a", 1), Log("b", 2), Log("c", 3), Log("d", 4),
            };

            var expired = SessionLogWriter.SelectExpiredLogs(logs, Now);

            // All five are inside the always-keep floor, so nothing is deleted even though
            // the folder is well over the cap.
            Assert.AreEqual(0, expired.Count);
        }

        [TestMethod]
        public void AnEmptyFolder_IsNotAProblem()
        {
            Assert.AreEqual(0,
                SessionLogWriter.SelectExpiredLogs(new SessionLogWriter.LogFileInfo[0], Now).Count);
        }
    }
}
