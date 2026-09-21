using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the cookie pre-flight check (AD-051).
    ///
    /// This came out of a real test session where a cookie source that could not be read
    /// failed once per episode, 59 times, with yt-dlp reporting "Could not copy Chrome cookie
    /// database" even though the configured browser was Edge. The cause took three runs to
    /// identify. Catching it once, up front, is the whole point.
    /// </summary>
    [TestClass]
    public class CookiePreflightTests
    {
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void GetPreflightWarning_SaysNothingWhenCookiesAreDisabled(string? value)
        {
            Assert.IsNull(CookieSourceSpec.GetPreflightWarning(value));
        }

        [TestMethod]
        public void GetPreflightWarning_DoesNotWarnAboutFirefox()
        {
            // Firefox can be read while running; only Chromium browsers hold an exclusive lock.
            Assert.IsNull(CookieSourceSpec.GetPreflightWarning("firefox"));
        }

        [TestMethod]
        public void GetPreflightWarning_ReportsAMissingCookiesFile()
        {
            string? warning = CookieSourceSpec.GetPreflightWarning(@"D:\definitely\not\here\cookies.txt");

            Assert.IsNotNull(warning);
            StringAssert.Contains(warning!, "not found");
        }

        [TestMethod]
        public void GetPreflightWarning_AcceptsAnExistingCookiesFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                Assert.IsNull(CookieSourceSpec.GetPreflightWarning(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void GetPreflightWarning_DoesNotThrowForAnyBrowserName()
        {
            // Whether a warning is produced depends on what is running on this machine, so the
            // contract under test is only that every input is handled without throwing.
            foreach (var browser in new[] { "chrome", "edge", "brave", "chromium", "opera",
                                            "vivaldi", "whale", "chrome:Profile 2", "safari" })
            {
                _ = CookieSourceSpec.GetPreflightWarning(browser);
            }
        }
    }
}
