using AutoDownloader.Services;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers the browser-versus-file rule behind the CookieSource setting (AD-026).
    ///
    /// This matters more than its size suggests. yt-dlp treats an unreadable cookie source as
    /// a fatal error, so misclassifying one value stops every download rather than degrading
    /// gracefully. It also has to round-trip through the Preferences window without rewriting
    /// a setting that already worked.
    /// </summary>
    [TestClass]
    public class CookieSourceSpecTests
    {
        [TestMethod]
        [DataRow("firefox")]
        [DataRow("chrome")]
        [DataRow("edge")]
        [DataRow("brave")]
        [DataRow("safari")]
        [DataRow("whale")]
        public void IsFilePath_IsFalseForBrowserNames(string value)
        {
            Assert.IsFalse(CookieSourceSpec.IsFilePath(value));
        }

        [TestMethod]
        [DataRow("chrome:Profile 2")]
        [DataRow("firefox:default-release")]
        [DataRow("chrome+gnomekeyring")]
        [DataRow("firefox::personal")]
        public void IsFilePath_IsFalseForYtDlpBrowserSuffixes(string value)
        {
            // yt-dlp accepts BROWSER[+KEYRING][:PROFILE][::CONTAINER]. Treating one of these
            // as a path would silently rewrite a working setting into a broken one.
            Assert.IsFalse(CookieSourceSpec.IsFilePath(value),
                $"'{value}' is a browser specification, not a path.");
        }

        [TestMethod]
        [DataRow(@"D:\media\cookies.txt")]
        [DataRow(@"C:\Users\me\Downloads\cookies.txt")]
        [DataRow("/home/user/cookies.txt")]
        [DataRow("cookies.txt")]
        [DataRow(@"relative\path\jar.txt")]
        public void IsFilePath_IsTrueForPaths(string value)
        {
            Assert.IsTrue(CookieSourceSpec.IsFilePath(value));
        }

        [TestMethod]
        public void IsFilePath_ClassifiesByShapeNotExistence()
        {
            // A path that does not exist is still a path. Reporting a missing file is far more
            // useful than handing the typo to yt-dlp as a browser name.
            Assert.IsTrue(CookieSourceSpec.IsFilePath(@"D:\definitely\not\here\cookies.txt"));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void IsNone_IsTrueForEmptyValues(string? value)
        {
            Assert.IsTrue(CookieSourceSpec.IsNone(value));
            Assert.IsFalse(CookieSourceSpec.IsFilePath(value),
                "An empty value means 'send no cookies', not 'a file'.");
        }

        [TestMethod]
        public void IsNone_IsFalseWhenASourceIsConfigured()
        {
            Assert.IsFalse(CookieSourceSpec.IsNone("firefox"));
            Assert.IsFalse(CookieSourceSpec.IsNone(@"D:\cookies.txt"));
        }

        /// <summary>
        /// The Preferences dropdown stores an empty string for "None", but a person typing
        /// --cookies on the command line writes "none", and yt-dlp then aborts with
        /// "unsupported browser specified for cookies" - which reads like a bug in the app
        /// rather than a value it could have understood.
        /// </summary>
        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("none")]
        [DataRow("None")]
        [DataRow("NONE")]
        [DataRow(" off ")]
        [DataRow("no")]
        public void WaysOfSayingNoCookiesAreAllUnderstood(string value)
        {
            Assert.IsTrue(CookieSourceSpec.IsNone(value));
        }

        [TestMethod]
        [DataRow("firefox")]
        [DataRow("chrome")]
        [DataRow(@"C:\cookies.txt")]
        public void ARealCookieSourceIsNotMistakenForNone(string value)
        {
            Assert.IsFalse(CookieSourceSpec.IsNone(value));
        }
    }
}
