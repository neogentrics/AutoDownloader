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
        /// Below this share matched, the titles are assumed not to be comparable at all - a
        /// foreign-language listing, or slugs that are not titles - and position is used
        /// instead. Matching a handful and guessing the rest would interleave two different
        /// numbering schemes, which is worse than one consistent guess.
        ///
        /// Measured against whichever is smaller, the links or the database episodes, not
        /// against the links alone. A listing carrying eight episodes and eight alternate
        /// cuts can never match more than half its links however well it is read, and
        /// judging it on that put a season of Alex vs America back on positional numbering -
        /// which is what the matching exists to replace. A source carrying three episodes of
        /// a twenty-six episode season has the same problem from the other direction.
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

            /// <summary>
            /// Links whose title matched nothing, but whose place in the listing identified
            /// them: they sit between two recognised episodes with exactly one number free
            /// between those episodes.
            /// </summary>
            public int PlacedByPosition { get; set; }
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

            int comparable = Math.Min(links.Count, episodes.Count);

            if (result.Matched == 0 || (double)result.Matched / comparable < MinimumMatchRatio)
            {
                return result;
            }

            foreach (var pair in assignments)
            {
                pair.Key.DetectedEpisodeNumber = pair.Value;
            }

            // A title the databases spell differently will not match: a site calling an
            // episode "Alex vs TOC Winners" against "Alex vs Tournament of Champions
            // Winners", or "Alex vs Ultimate Fruits" against "Alex vs Fruit". Where such a
            // link sits between two episodes that DID match, and a number is free between
            // them, that number is what it is - the listing itself is the evidence.
            //
            // Everything still unplaced goes above every real episode, so extras and
            // alternate cuts sort to the end and cannot displace an episode.
            var free = episodes
                .Select(e => e.EpisodeNumber)
                .Where(n => !takenNumbers.Contains(n))
                .OrderBy(n => n)
                .ToList();

            int next = Math.Max(
                episodes.Max(e => e.EpisodeNumber),
                takenNumbers.Count == 0 ? 0 : takenNumbers.Max()) + 1;

            var appended = new List<EpisodeLink>();

            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (assignments.ContainsKey(link)) continue;

                int before = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (assignments.TryGetValue(links[j], out int earlier)) { before = earlier; break; }
                }

                int after = int.MaxValue;
                for (int j = i + 1; j < links.Count; j++)
                {
                    if (assignments.TryGetValue(links[j], out int later)) { after = later; break; }
                }

                // A link must be bracketed from above by an episode that matched. Past the
                // last recognised episode is exactly where a listing keeps its extras - a
                // later season's shorts, alternate cuts - and a free trailing number would
                // swallow them. Nothing anchors them there, so they are appended instead.
                var fits = after == int.MaxValue
                    ? new List<int>()
                    : free.Where(n => n > before && n < after).ToList();

                // Only when the gap holds exactly one candidate. Two free numbers between the
                // same pair of anchors is a guess, and a guess here misnames a file.
                if (fits.Count == 1)
                {
                    int slot = fits[0];

                    free.Remove(slot);
                    link.DetectedEpisodeNumber = slot;
                    assignments[link] = slot;
                    takenNumbers.Add(slot);
                    result.PlacedByPosition++;
                }
                else
                {
                    link.DetectedEpisodeNumber = next++;
                    appended.Add(link);
                }
            }

            result.Unmatched = appended;

            result.Applied = true;
            return result;
        }
    }
}
