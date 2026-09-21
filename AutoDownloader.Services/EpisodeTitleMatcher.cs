using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Works out which database episode each scraped link actually is, by its own title.
    ///
    /// Numbering links by their position on the page and then handing them database titles in
    /// order assumes the page is sorted the way the database is. It usually is not, and the
    /// result is confidently wrong: a run of Duck Dodgers wrote "Duck Deception" onto the file
    /// for The Trial of Duck Dodgers and vice versa, with nothing in the log to suggest
    /// anything had gone wrong.
    ///
    /// The link already carries its identity, in its text and in its URL slug. Matching on
    /// that is both more accurate and site-agnostic.
    /// </summary>
    public static class EpisodeTitleMatcher
    {
        /// <summary>
        /// Below this share of links matched, the titles are assumed not to be comparable at
        /// all - a foreign-language listing, or slugs that are not titles - and position is
        /// used instead. Matching a handful of links and guessing the rest would interleave
        /// two different numbering schemes, which is worse than one consistent guess.
        /// </summary>
        private const double MinimumMatchRatio = 0.5;

        public class MatchResult
        {
            /// <summary>Links whose title was recognised, in database episode order.</summary>
            public int Matched { get; set; }

            public int Total { get; set; }

            /// <summary>True when the matching was trusted and applied.</summary>
            public bool Applied { get; set; }

            /// <summary>Links whose title matched nothing in the database.</summary>
            public List<EpisodeLink> Unmatched { get; set; } = new List<EpisodeLink>();
        }

        /// <summary>
        /// Reduces a title to its comparable core: case, punctuation and spacing all vary
        /// between a site's slug and a database's title for what is plainly the same episode.
        /// "Where's Baby Smartypants?" and "wheres-baby-smartypants" both land on
        /// "wheres baby smartypants".
        /// </summary>
        public static string Normalise(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string working = value.ToLowerInvariant();

            // An apostrophe closes up rather than splitting, so "don't" does not become
            // "don t" and stop matching "dont".
            working = working.Replace("'", string.Empty)
                             .Replace("\u2019", string.Empty);

            working = Regex.Replace(working, "[^a-z0-9]+", " ");

            // Leading articles are dropped inconsistently by sites, so they carry no signal.
            working = Regex.Replace(working, "^(the|a|an) ", string.Empty);

            return working.Trim();
        }

        /// <summary>
        /// The candidate titles a link offers: its visible text, and its URL's last path
        /// segment. Either may be the real title and the other noise, so both are tried.
        /// </summary>
        public static IEnumerable<string> CandidateTitles(EpisodeLink link)
        {
            if (!string.IsNullOrWhiteSpace(link.LinkText))
            {
                yield return link.LinkText!;
            }

            string slug = SlugOf(link.Url);
            if (!string.IsNullOrWhiteSpace(slug))
            {
                yield return slug;
            }
        }

        private static string SlugOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            try
            {
                string path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

                string last = path.TrimEnd('/')
                                  .Split('/')
                                  .LastOrDefault() ?? string.Empty;

                // A trailing extension is part of the file, not the title.
                int dot = last.LastIndexOf('.');
                if (dot > 0) last = last.Substring(0, dot);

                return last.Replace('-', ' ').Replace('_', ' ');
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Assigns each link the episode number of the database episode whose title it
        /// carries. Links are left untouched, and Applied is false, when too few match to
        /// trust the result.
        /// </summary>
        public static MatchResult Apply(List<EpisodeLink> links, List<DownloadEpisode> episodes)
        {
            var result = new MatchResult { Total = links.Count };

            if (links.Count == 0 || episodes == null || episodes.Count == 0) return result;

            // A title shared by two episodes identifies neither, so ambiguous titles are
            // dropped rather than resolved arbitrarily.
            var byTitle = new Dictionary<string, int?>();

            foreach (var episode in episodes.Where(e => e.EpisodeNumber > 0))
            {
                string key = Normalise(episode.EpisodeTitle);
                if (key.Length == 0) continue;

                byTitle[key] = byTitle.ContainsKey(key) ? (int?)null : episode.EpisodeNumber;
            }

            var assignments = new Dictionary<EpisodeLink, int>();
            var takenNumbers = new HashSet<int>();

            foreach (var link in links)
            {
                int? found = null;

                foreach (var candidate in CandidateTitles(link))
                {
                    string key = Normalise(candidate);
                    if (key.Length == 0) continue;

                    if (byTitle.TryGetValue(key, out int? number) && number.HasValue
                        && !takenNumbers.Contains(number.Value))
                    {
                        found = number;
                        break;
                    }
                }

                if (found.HasValue)
                {
                    assignments[link] = found.Value;
                    takenNumbers.Add(found.Value);
                }
                else
                {
                    result.Unmatched.Add(link);
                }
            }

            result.Matched = assignments.Count;

            if (result.Matched == 0 || (double)result.Matched / links.Count < MinimumMatchRatio)
            {
                return result;
            }

            foreach (var pair in assignments)
            {
                pair.Key.DetectedEpisodeNumber = pair.Value;
            }

            // Anything unmatched is given a number above every real episode, so it sorts to
            // the end and cannot silently displace an episode that was matched.
            int next = Math.Max(
                episodes.Max(e => e.EpisodeNumber),
                takenNumbers.Count == 0 ? 0 : takenNumbers.Max()) + 1;

            foreach (var link in result.Unmatched)
            {
                link.DetectedEpisodeNumber = next++;
            }

            result.Applied = true;
            return result;
        }
    }
}
