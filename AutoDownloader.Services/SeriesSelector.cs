using AutoDownloader.Core;
using System.Collections.Generic;
using System.Linq;

namespace AutoDownloader.Services
{
    /// <summary>
    /// What to do with a set of search results.
    /// </summary>
    public enum SeriesSelectionKind
    {
        /// <summary>Nothing matched.</summary>
        None,

        /// <summary>One clear answer; take it without troubling anyone.</summary>
        Automatic,

        /// <summary>Several plausible shows; the choice has to be made explicitly.</summary>
        Ambiguous
    }

    /// <summary>
    /// The outcome of examining search results.
    /// </summary>
    public class SeriesSelection
    {
        public SeriesSelectionKind Kind { get; set; }

        /// <summary>The chosen candidate for Automatic, or the best guess for Ambiguous.</summary>
        public SeriesCandidate? Selected { get; set; }

        /// <summary>All plausible candidates, best first. Populated when Ambiguous.</summary>
        public List<SeriesCandidate> Candidates { get; set; } = new List<SeriesCandidate>();

        /// <summary>Why this outcome was reached, for the log.</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Decides whether search results identify one show or need a human to choose.
    ///
    /// The rule matters because the failure it guards against is silent. Searching
    /// "teen titans" returns both "Teen Titans" (2003) and "Teen Titans Go!" (2013), and
    /// taking the first result produced plausible-looking filenames from entirely the wrong
    /// series. Being asked once is much cheaper than noticing later.
    /// </summary>
    public static class SeriesSelector
    {
        /// <summary>How many candidates are worth offering.</summary>
        private const int MaxCandidates = 6;

        public static SeriesSelection Choose(string searchTerm, IReadOnlyList<SeriesCandidate> results)
        {
            if (results == null || results.Count == 0)
            {
                return new SeriesSelection { Kind = SeriesSelectionKind.None };
            }

            if (results.Count == 1)
            {
                return new SeriesSelection
                {
                    Kind = SeriesSelectionKind.Automatic,
                    Selected = results[0],
                    Reason = "only one match"
                };
            }

            string target = SeriesCandidate.Normalise(searchTerm);

            // Candidates whose title is the search term, or begins with it. A reboot almost
            // always keeps the original name and adds to it ("Teen Titans" -> "Teen Titans
            // Go!"), so this is exactly where confusion lives.
            var related = results
                .Where(c => c.NormalisedTitle == target || c.NormalisedTitle.StartsWith(target + " "))
                .ToList();

            var exact = related.Where(c => c.NormalisedTitle == target).ToList();

            // A single exact title match, with nothing else sharing the name, is unambiguous.
            if (exact.Count == 1 && related.Count == 1)
            {
                return new SeriesSelection
                {
                    Kind = SeriesSelectionKind.Automatic,
                    Selected = exact[0],
                    Reason = $"exact title match: {exact[0].Display}"
                };
            }

            // Several shows share or extend the name - a reboot, a sequel, or a remake.
            if (related.Count > 1)
            {
                return new SeriesSelection
                {
                    Kind = SeriesSelectionKind.Ambiguous,
                    // Prefer an exact title match as the default, then the earliest year: the
                    // original rather than whichever reboot happens to be more popular today.
                    Selected = exact.OrderBy(c => c.Year ?? int.MaxValue).FirstOrDefault()
                               ?? related.OrderBy(c => c.Year ?? int.MaxValue).First(),
                    Candidates = related.Take(MaxCandidates).ToList(),
                    Reason = $"{related.Count} shows share this name"
                };
            }

            // Nothing matched the name closely. The top result is a guess, so say so and let
            // it be corrected.
            return new SeriesSelection
            {
                Kind = SeriesSelectionKind.Ambiguous,
                Selected = results[0],
                Candidates = results.Take(MaxCandidates).ToList(),
                Reason = "no exact title match"
            };
        }
    }
}
