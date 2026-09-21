using AutoDownloader.Cli;

namespace AutoDownloader.Tests
{
    /// <summary>
    /// Covers CLI argument parsing (AD-030).
    ///
    /// A scheduled job that misreads its own arguments fails at 3am with nobody watching, so
    /// the rules are worth pinning down: bad input must produce a clear error and a distinct
    /// exit code rather than a default that silently downloads the wrong thing.
    /// </summary>
    [TestClass]
    public class CommandLineOptionsTests
    {
        [TestMethod]
        public void Parse_TakesABareArgumentAsTheTarget()
        {
            var o = CommandLineOptions.Parse(new[] { "https://example.com/series/show" });

            Assert.IsFalse(o.HasError);
            Assert.AreEqual("https://example.com/series/show", o.Target);
        }

        [TestMethod]
        [DataRow("-u")]
        [DataRow("--url")]
        [DataRow("--search")]
        public void Parse_AcceptsTheTargetByFlag(string flag)
        {
            var o = CommandLineOptions.Parse(new[] { flag, "The Mandalorian" });

            Assert.IsFalse(o.HasError);
            Assert.AreEqual("The Mandalorian", o.Target);
        }

        [TestMethod]
        public void Parse_ReadsTheFullOptionSet()
        {
            var o = CommandLineOptions.Parse(new[]
            {
                "https://example.com/show", "--season", "3", "--out", @"D:\Media",
                "--quality", "best", "--cookies", "firefox",
                "--no-archive", "--no-ffmpeg", "--json", "--quiet"
            });

            Assert.IsFalse(o.HasError);
            Assert.AreEqual("https://example.com/show", o.Target);
            Assert.AreEqual(3, o.Season);
            Assert.AreEqual(@"D:\Media", o.OutputFolder);
            Assert.AreEqual("best", o.Quality);
            Assert.AreEqual("firefox", o.CookieSource);
            Assert.IsTrue(o.NoArchive);
            Assert.IsTrue(o.NoFfmpeg);
            Assert.IsTrue(o.Json);
            Assert.IsTrue(o.Quiet);
        }

        [TestMethod]
        public void Parse_WithNoArgumentsShowsHelpRatherThanFailing()
        {
            var o = CommandLineOptions.Parse(new string[0]);

            Assert.IsTrue(o.Help);
            Assert.IsFalse(o.HasError);
        }

        [TestMethod]
        public void Parse_RequiresATargetWhenOptionsAreGivenWithoutOne()
        {
            var o = CommandLineOptions.Parse(new[] { "--season", "2" });

            Assert.IsTrue(o.HasError);
            StringAssert.Contains(o.Error!, "No URL or search term");
        }

        [TestMethod]
        [DataRow("abc")]
        [DataRow("2.5")]
        [DataRow("-1")]
        public void Parse_RejectsANonNumericOrNegativeSeason(string value)
        {
            // Silently defaulting here would download the wrong season without saying so.
            var o = CommandLineOptions.Parse(new[] { "https://example.com/show", "--season", value });

            Assert.IsTrue(o.HasError, $"'{value}' should not be accepted as a season.");
        }

        [TestMethod]
        public void Parse_RejectsAFlagThatIsMissingItsValue()
        {
            var o = CommandLineOptions.Parse(new[] { "https://example.com/show", "--season" });

            Assert.IsTrue(o.HasError);
            StringAssert.Contains(o.Error!, "--season");
        }

        [TestMethod]
        public void Parse_DoesNotSwallowTheNextFlagAsAValue()
        {
            // "--out --json" must be an error, not an output folder literally named "--json".
            var o = CommandLineOptions.Parse(new[] { "https://example.com/show", "--out", "--json" });

            Assert.IsTrue(o.HasError);
            Assert.IsNull(o.OutputFolder);
        }

        [TestMethod]
        public void Parse_RejectsAnUnknownOption()
        {
            var o = CommandLineOptions.Parse(new[] { "https://example.com/show", "--turbo" });

            Assert.IsTrue(o.HasError);
            StringAssert.Contains(o.Error!, "--turbo");
        }

        [TestMethod]
        public void Parse_RejectsMoreThanOneTarget()
        {
            var o = CommandLineOptions.Parse(new[] { "https://a.example/show", "https://b.example/show" });

            Assert.IsTrue(o.HasError);
            StringAssert.Contains(o.Error!, "one URL");
        }

        [TestMethod]
        [DataRow("-h")]
        [DataRow("--help")]
        public void Parse_HelpNeedsNoTarget(string flag)
        {
            var o = CommandLineOptions.Parse(new[] { flag });

            Assert.IsTrue(o.Help);
            Assert.IsFalse(o.HasError);
        }

        [TestMethod]
        [DataRow("-v")]
        [DataRow("--version")]
        public void Parse_VersionNeedsNoTarget(string flag)
        {
            var o = CommandLineOptions.Parse(new[] { flag });

            Assert.IsTrue(o.Version);
            Assert.IsFalse(o.HasError);
        }

        [TestMethod]
        public void Parse_OverwriteIsOffUnlessAskedFor()
        {
            // The CLI is what scheduled runs use, so the flag has to be opt-in: an unattended
            // job must never replace a library because a default said it could.
            var o = CommandLineOptions.Parse(new[] { "https://example.com/show" });

            Assert.IsFalse(o.Overwrite);
        }

        [TestMethod]
        public void Parse_OverwriteFlagIsPickedUp()
        {
            var o = CommandLineOptions.Parse(new[] { "https://example.com/show", "--overwrite" });

            Assert.IsTrue(o.Overwrite);
            Assert.IsFalse(o.HasError);
        }

        [TestMethod]
        public void Parse_LeavesUnsetOptionsNullSoSavedSettingsWin()
        {
            // Null, not empty string: the caller distinguishes "not given" from "given as
            // blank" when deciding whether to fall back to settings.json.
            var o = CommandLineOptions.Parse(new[] { "https://example.com/show" });

            Assert.IsNull(o.OutputFolder);
            Assert.IsNull(o.Quality);
            Assert.IsNull(o.CookieSource);
            Assert.IsNull(o.Season);
        }
    }
}
