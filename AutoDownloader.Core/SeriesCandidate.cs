using System;

namespace AutoDownloader.Core
{
    /// <summary>
    /// One possible match for a show name, before a choice has been made between them.
    ///
    /// This type exists because taking the first search result silently produced the wrong
    /// series: a URL for "teen-titans" resolved to "Teen Titans Go!" (2013) rather than
    /// "Teen Titans" (2003), and every episode would then have been named from the wrong
    /// show. The failure is invisible - the names look entirely plausible - which is what
    /// makes it worth an explicit choice.
    /// </summary>
    public class SeriesCandidate
    {
        /// <summary>The series id, valid only against the database it came from.</summary>
        public int Id { get; set; }

        /// <summary>The official title, e.g. "Teen Titans Go!".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>First air year, the main thing that separates a show from its reboot.</summary>
        public int? Year { get; set; }

        /// <summary>A short description, to tell near-identical titles apart.</summary>
        public string? Overview { get; set; }

        /// <summary>
        /// Episodes the source reports for the whole show, where it says. Zero means it did
        /// not, not that there are none.
        /// </summary>
        public int EpisodeCount { get; set; }

        /// <summary>Which database produced this candidate.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>"Teen Titans (2003)", or just the title when the year is unknown.</summary>
        public string Display => Year.HasValue ? $"{Title} ({Year})" : Title;

        /// <summary>
        /// Lowercased, punctuation-stripped title for comparison, so "Teen Titans Go!" and
        /// "teen titans go" are recognised as the same string.
        /// </summary>
        public string NormalisedTitle => Normalise(Title);

        /// <summary>
        /// Reduces a title or search term to comparable form: lowercase, alphanumerics and
        /// single spaces only.
        /// </summary>
        public static string Normalise(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length);
            bool lastWasSpace = false;

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                    lastWasSpace = false;
                }
                else if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
            }

            return builder.ToString().Trim();
        }
    }
}
