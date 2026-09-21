using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Produces progressively simpler forms of a show name to search for.
    ///
    /// Names taken from a page title carry qualifiers the metadata databases do not have.
    /// Measured against the live APIs: "Megaman Star Force Anime" returns nothing at all,
    /// while "Megaman Star Force" returns the show. A single trailing word was the whole
    /// difference, and the previous behaviour was to abort the download over it.
    /// </summary>
    public static class SearchTermVariants
    {
        /// <summary>
        /// Qualifiers that describe the upload rather than the show. Only ever removed from
        /// the END of a name: a title genuinely beginning with one of these ("Anime Crimes
        /// Division") must survive intact, whereas playlist titles put their noise last.
        /// </summary>
        private static readonly string[] TrailingNoise =
        {
            "complete series", "full series", "full episodes", "all episodes",
            "english dubbed", "english dub", "english subbed", "english sub",
            "tv series", "the series", "series", "anime", "episodes", "playlist",
            "dubbed", "dub", "subbed", "sub", "subtitled", "official", "uncut",
            "uncensored", "remastered", "hd", "fhd", "1080p", "720p", "4k",
        };

        /// <summary>
        /// Returns the name, then progressively simpler forms of it, without duplicates.
        /// The original is always first, so a name that already works is unaffected.
        /// </summary>
        public static IReadOnlyList<string> Generate(string? term)
        {
            var variants = new List<string>();

            if (string.IsNullOrWhiteSpace(term)) return variants;

            void Add(string? candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate)) return;

                candidate = candidate.Trim();

                // Refuse to reduce a name to almost nothing; "Anime" alone is not a search.
                if (candidate.Length < 2) return;

                if (!variants.Any(v => string.Equals(v, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    variants.Add(candidate);
                }
            }

            Add(term);

            // Strip trailing qualifiers one at a time, so "Show Anime English Dub" reduces
            // through "Show Anime" to "Show", and each step is tried.
            string current = term.Trim();

            for (int pass = 0; pass < 4; pass++)
            {
                string stripped = StripOneTrailingQualifier(current);
                if (stripped == current) break;

                Add(stripped);
                current = stripped;
            }

            // Bracketed asides are almost always qualifiers: "Show (Complete Series)".
            Add(RemoveBracketed(term));

            return variants;
        }

        private static string StripOneTrailingQualifier(string value)
        {
            string trimmed = value.TrimEnd(' ', '-', ':', '|', '.', ',');

            foreach (var noise in TrailingNoise.OrderByDescending(n => n.Length))
            {
                if (trimmed.Length <= noise.Length) continue;

                if (!trimmed.EndsWith(noise, StringComparison.OrdinalIgnoreCase)) continue;

                // Only strip on a word boundary, so "Sub" does not truncate "Subterranean".
                char before = trimmed[trimmed.Length - noise.Length - 1];
                if (char.IsLetterOrDigit(before)) continue;

                return trimmed.Substring(0, trimmed.Length - noise.Length)
                              .TrimEnd(' ', '-', ':', '|', '.', ',');
            }

            return value;
        }

        private static string RemoveBracketed(string value)
        {
            var result = new System.Text.StringBuilder(value.Length);
            int depth = 0;

            foreach (char c in value)
            {
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') { if (depth > 0) depth--; }
                else if (depth == 0) result.Append(c);
            }

            return System.Text.RegularExpressions.Regex
                .Replace(result.ToString(), @"\s+", " ")
                .Trim();
        }
    }
}
