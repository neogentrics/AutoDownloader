using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// Finds the next page of a paginated listing.
    ///
    /// Without this an indexer reads page one and stops, which is worse than failing: a show
    /// spread over six pages yields a sixth of its episodes while every message says the run
    /// succeeded. The verification step would then blame the source for being incomplete,
    /// which is confidently wrong.
    ///
    /// Three signals are tried, most reliable first. The markup says so explicitly
    /// (rel="next"), a link is labelled as the next page, or a numbered pager contains a link
    /// to the number after the current one.
    /// </summary>
    public static class PaginationFinder
    {
        /// <summary>
        /// Words and glyphs used for "next" across the sites this is likely to meet.
        /// Compared after stripping arrows and whitespace.
        /// </summary>
        private static readonly string[] NextLabels =
        {
            "next", "next page", "older", "older posts", "more", "continue", "forward"
        };

        /// <summary>Arrow glyphs that appear alone or alongside the word.</summary>
        private static readonly char[] ArrowGlyphs =
        {
            '→', // right arrow
            '»', // >>
            '›', // >
            '>', '⮕', '➡'
        };

        /// <summary>
        /// Reads the page number out of a URL: "/page/3/", "?page=3", "?p=3", "?paged=3".
        /// Returns 1 when none is present, since an unnumbered URL is the first page.
        /// </summary>
        public static int CurrentPageNumber(string url)
        {
            try
            {
                var uri = new Uri(url);

                var inPath = Regex.Match(uri.AbsolutePath, @"/page/(\d{1,4})", RegexOptions.IgnoreCase);
                if (inPath.Success && int.TryParse(inPath.Groups[1].Value, out int fromPath)) return fromPath;

                var inQuery = Regex.Match(uri.Query, @"[?&](?:page|paged|p|pg)=(\d{1,4})", RegexOptions.IgnoreCase);
                if (inQuery.Success && int.TryParse(inQuery.Groups[1].Value, out int fromQuery)) return fromQuery;
            }
            catch { }

            return 1;
        }

        /// <summary>
        /// Returns the absolute URL of the next page, or null when there is not one.
        /// </summary>
        /// <param name="html">The current page's source.</param>
        /// <param name="currentUrl">The current page's URL, for resolving relative links.</param>
        public static string? FindNextPage(string? html, string currentUrl)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            Uri baseUri;
            try { baseUri = new Uri(currentUrl); }
            catch { return null; }

            var document = new HtmlParser().ParseDocument(html);

            string? Resolve(string? href)
            {
                if (string.IsNullOrWhiteSpace(href)) return null;
                if (href.StartsWith("#") || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return null;

                try
                {
                    var absolute = new Uri(baseUri, href);

                    if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) return null;

                    // Staying on the same host keeps a stray "next article" off-site link from
                    // sending the indexer somewhere unrelated.
                    if (!string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) return null;

                    string result = absolute.ToString();

                    // A "next" that points at the page we are on is a dead end, not a next page.
                    return string.Equals(result.TrimEnd('/'), currentUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                        ? null
                        : result;
                }
                catch { return null; }
            }

            // 1. The markup says so outright. Both <link rel="next"> and <a rel="next"> occur.
            foreach (var element in document.QuerySelectorAll("link[rel~='next'], a[rel~='next']"))
            {
                string? resolved = Resolve(element.GetAttribute("href"));
                if (resolved != null) return resolved;
            }

            // 2. A link labelled as the next page.
            foreach (var anchor in document.QuerySelectorAll("a"))
            {
                if (!LooksLikeNextLabel(anchor.TextContent)
                    && !LooksLikeNextLabel(anchor.GetAttribute("title"))
                    && !LooksLikeNextLabel(anchor.GetAttribute("aria-label")))
                {
                    continue;
                }

                string? resolved = Resolve(anchor.GetAttribute("href"));
                if (resolved != null) return resolved;
            }

            // 3. A numbered pager: look for a link whose text is the next number along.
            int current = CurrentPageNumber(currentUrl);
            string wanted = (current + 1).ToString(CultureInfo.InvariantCulture);

            foreach (var anchor in document.QuerySelectorAll("a"))
            {
                string text = (anchor.TextContent ?? string.Empty).Trim();
                if (!string.Equals(text, wanted, StringComparison.Ordinal)) continue;

                string? resolved = Resolve(anchor.GetAttribute("href"));

                // Only trust a bare number when the link it points at actually looks like a
                // page, otherwise an episode titled "2" would be mistaken for a pager.
                if (resolved != null && CurrentPageNumber(resolved) == current + 1) return resolved;
            }

            return null;
        }

        /// <summary>
        /// True when a label means "next page" - a word, an arrow glyph, or both.
        /// </summary>
        public static bool LooksLikeNextLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;

            string text = Regex.Replace(label, @"\s+", " ").Trim();

            // An arrow on its own is a next link on a great many sites.
            string withoutArrows = new string(text.Where(c => !ArrowGlyphs.Contains(c)).ToArray()).Trim();

            bool hadArrow = withoutArrows.Length < text.Length;

            if (withoutArrows.Length == 0) return hadArrow;

            // Keep this tight: "next episode" is a link to content, not to another page.
            return NextLabels.Any(l => string.Equals(withoutArrows, l, StringComparison.OrdinalIgnoreCase));
        }
    }
}
