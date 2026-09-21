using AngleSharp.Html.Parser;
using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// Finds the ordered list of episode-page links on a series or season page.
    ///
    /// This is deliberately site-agnostic. It does not try to locate the video file - yt-dlp
    /// is far better at that, and re-implementing extraction per site is what made the old
    /// scrapers break the moment a site changed its markup. All this has to do is answer
    /// "which pages on this site are the episodes, and in what order".
    ///
    /// Static HTML is read with AngleSharp. If that yields too little (a JavaScript-rendered
    /// listing), the caller can retry with renderJavaScript: true, which pays for a headless
    /// browser only when it is actually needed.
    /// </summary>
    public class SeriesIndexer
    {
        /// <summary>
        /// Raised with progress/diagnostic messages for the developer log.
        /// </summary>
        public event Action<string>? OnLog;

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // Some sites return a trimmed page or a block page to an unknown client.
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ToolManagerService.FIREFOX_USER_AGENT);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            return client;
        }

        /// <summary>
        /// Matches an episode number in link text or a URL slug: "Episode 7", "ep 7", "ep-07",
        /// "e07", "episode_7". Anchored on a word boundary so it does not fire on arbitrary
        /// digits such as a year or a series id.
        /// </summary>
        private static readonly Regex EpisodeNumberPattern = new Regex(
            @"(?:\bepisode|\bep|\be)[\s._-]*(\d{1,4})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Matches a season and episode together: "s01-e01", "S01E01", "season 2 episode 5".
        /// Requiring both in one expression avoids reading an unrelated number as a season.
        /// </summary>
        private static readonly Regex SeasonEpisodePattern = new Regex(
            @"\bs(?:eason)?[\s._-]*(\d{1,3})[\s._-]*e(?:p(?:isode)?)?[\s._-]*(\d{1,4})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// How many pages of a listing to follow. High enough for a long back catalogue,
        /// low enough that a malformed pager cannot run for an hour.
        /// </summary>
        private const int MaxPages = 25;

        /// <summary>
        /// Adds this page's same-host anchors to the running list, skipping any already seen.
        /// </summary>
        private static void CollectCandidates(
            string html,
            string pageUrl,
            Uri baseUri,
            string seriesUrl,
            HashSet<string> seen,
            List<EpisodeLink> candidates)
        {
            Uri pageUri;
            try { pageUri = new Uri(pageUrl); }
            catch { pageUri = baseUri; }

            var document = new HtmlParser().ParseDocument(html);

            foreach (var anchor in document.QuerySelectorAll("a"))
            {
                var href = anchor.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (href.StartsWith("#") || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;

                Uri absolute;
                try { absolute = new Uri(pageUri, href); }
                catch { continue; }

                if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;

                // Stay on the same site; off-site links are adverts, socials or mirrors.
                if (!string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

                string url = absolute.ToString();
                if (url.TrimEnd('/').Equals(seriesUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(url)) continue;

                string text = (anchor.TextContent ?? string.Empty).Trim();
                text = Regex.Replace(text, @"\s+", " ");

                var (season, episode) = DetectSeasonAndEpisode(text, url);

                candidates.Add(new EpisodeLink
                {
                    Url = url,
                    LinkText = text,
                    DetectedSeasonNumber = season,
                    DetectedEpisodeNumber = episode,
                });
            }
        }

        /// <summary>
        /// Indexes a series/season page and returns its episode links in document order.
        /// </summary>
        /// <param name="seriesUrl">The series or season page to index.</param>
        /// <param name="renderJavaScript">
        /// When true, render the page in headless Chromium first. Use this only after a plain
        /// fetch has come back with too few links.
        /// </param>
        public async Task<List<EpisodeLink>> IndexAsync(string seriesUrl, bool renderJavaScript = false)
        {
            var results = new List<EpisodeLink>();

            Uri baseUri;
            try { baseUri = new Uri(seriesUrl); }
            catch
            {
                OnLog?.Invoke($"SeriesIndexer: not a usable URL: {seriesUrl}");
                return results;
            }

            // Collect same-host anchors across every page of the listing, in document order,
            // de-duplicated by URL.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<EpisodeLink>();
            var visitedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string? currentPage = seriesUrl;
            int pagesRead = 0;

            while (currentPage != null && pagesRead < MaxPages)
            {
                // A pager that points back at somewhere already read would otherwise loop for
                // as long as the cap allows.
                if (!visitedPages.Add(currentPage.TrimEnd('/'))) break;

                string? html = renderJavaScript
                    ? await RenderWithPlaywrightAsync(currentPage)
                    : await FetchAsync(currentPage);

                if (string.IsNullOrWhiteSpace(html))
                {
                    if (pagesRead == 0)
                    {
                        OnLog?.Invoke($"SeriesIndexer: no HTML retrieved for {currentPage}");
                        return results;
                    }

                    // A later page failing is not fatal; keep what earlier pages gave.
                    OnLog?.Invoke($"SeriesIndexer: could not read page {pagesRead + 1}; keeping what was found.");
                    break;
                }

                pagesRead++;

                int before = candidates.Count;
                CollectCandidates(html, currentPage, baseUri, seriesUrl, seen, candidates);
                int added = candidates.Count - before;

                // A page contributing nothing new means the pager is going in circles or has
                // run past the end, whatever its links claim.
                if (pagesRead > 1 && added == 0)
                {
                    OnLog?.Invoke($"SeriesIndexer: page {pagesRead} added no new links; stopping.");
                    break;
                }

                currentPage = PaginationFinder.FindNextPage(html, currentPage);
            }

            if (pagesRead > 1)
            {
                OnLog?.Invoke($"SeriesIndexer: followed {pagesRead} page(s) of the listing, "
                            + $"{candidates.Count} link(s) in total.");
            }

            if (pagesRead >= MaxPages)
            {
                OnLog?.Invoke($"SeriesIndexer: stopped at the {MaxPages}-page limit; there may be more.");
            }

            // Prefer links that actually look like episodes. If a decent number of them carry a
            // detectable episode number, trust that signal and discard the site's chrome
            // (nav bars, "related shows", footers) entirely.
            var numbered = candidates.Where(c => c.DetectedEpisodeNumber.HasValue).ToList();

            List<EpisodeLink> chosen;
            if (numbered.Count >= 2)
            {
                chosen = numbered;
                OnLog?.Invoke($"SeriesIndexer: {numbered.Count} link(s) carry an episode number.");
            }
            else
            {
                // No numbering to go on, so fall back to the shape of the links themselves.
                //
                // A keyword list was tried first and is too brittle: it recognised "/watch/"
                // and "/episode" but not "/cartoon/", so a page whose episode links were
                // perfectly ordinary anchors was reported as having none. Every site invents
                // its own word for this.
                //
                // Structure is more reliable than vocabulary. A listing page links its items
                // through one common path prefix and repeats it many times, whereas navigation
                // and chrome are few and scattered, so the largest such group is almost always
                // the content.
                chosen = FindLargestLinkGroup(candidates, baseUri);

                if (chosen.Count == 0)
                {
                    chosen = candidates.Where(c => LooksLikeEpisodeUrl(c.Url)).ToList();
                    OnLog?.Invoke($"SeriesIndexer: no episode numbering found; {chosen.Count} link(s) matched by URL shape.");
                }
            }

            // Keep only links belonging to THIS series.
            //
            // A numbered link is not necessarily one of our episodes: listing pages carry
            // sidebars, "recently added" panels and related-show rails, all full of other
            // series' episode links that are numbered exactly the same way. Measured on a real
            // 12-episode page, taking every numbered link gave 59 - so 47 episodes of other
            // shows would have been downloaded and filed under this one.
            //
            // Episode URLs almost universally contain the series slug, so that is the filter.
            // It is applied only when it leaves a plausible result, so a site that names its
            // episode pages differently still works.
            string? slug = ExtractSeriesSlug(baseUri);
            if (slug != null)
            {
                var onThisSeries = chosen
                    .Where(c => c.Url.Contains(slug, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (onThisSeries.Count >= 2 && onThisSeries.Count < chosen.Count)
                {
                    OnLog?.Invoke($"SeriesIndexer: kept {onThisSeries.Count} of {chosen.Count} link(s) matching series slug '{slug}'; "
                                + $"discarded {chosen.Count - onThisSeries.Count} belonging to other titles.");
                    chosen = onThisSeries;
                }
                else if (onThisSeries.Count == 0)
                {
                    OnLog?.Invoke($"SeriesIndexer: no link contained the slug '{slug}'; keeping all {chosen.Count}.");
                }
            }

            // Sort by detected episode number when we have it, otherwise keep document order.
            if (chosen.Any(c => c.DetectedEpisodeNumber.HasValue))
            {
                chosen = chosen
                    .OrderBy(c => c.DetectedEpisodeNumber ?? int.MaxValue)
                    .ToList();
            }

            // One link per episode number. The same episode is frequently linked more than
            // once on a page (a thumbnail and a title, say), and without this each duplicate
            // becomes a repeated download.
            if (chosen.Any(c => c.DetectedEpisodeNumber.HasValue))
            {
                int before = chosen.Count;

                chosen = chosen
                    .GroupBy(c => c.DetectedEpisodeNumber)
                    .Select(g => g.First())
                    .OrderBy(c => c.DetectedEpisodeNumber ?? int.MaxValue)
                    .ToList();

                if (chosen.Count < before)
                {
                    OnLog?.Invoke($"SeriesIndexer: collapsed {before} link(s) to {chosen.Count} distinct episode(s).");
                }
            }

            for (int i = 0; i < chosen.Count; i++)
            {
                chosen[i].Ordinal = i;
                results.Add(chosen[i]);
            }

            return results;
        }

        /// <summary>
        /// Derives the series slug from a series/season URL, so episode links belonging to
        /// other titles can be discarded.
        ///
        /// Takes the last path segment that is not structural ("anime", "series", "season-2",
        /// a bare number). Returns null when nothing distinctive enough is found - a very short
        /// slug would match half the page and do more harm than good.
        /// </summary>
        public static string? ExtractSeriesSlug(Uri seriesUri)
        {
            var structural = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "anime", "series", "show", "shows", "tv", "watch", "video", "seasons", "season", "episode"
            };

            var segments = seriesUri.Segments
                .Select(seg => seg.Trim('/'))
                .Where(seg => seg.Length > 0)
                .ToList();

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                string segment = segments[i];

                if (structural.Contains(segment)) continue;
                if (segment.All(char.IsDigit)) continue;
                if (Regex.IsMatch(segment, @"^(?:season[-_]?|s)\d+$", RegexOptions.IgnoreCase)) continue;

                // Too short to be distinctive; matching on it would keep everything.
                if (segment.Length < 3) continue;

                return segment;
            }

            return null;
        }

        /// <summary>
        /// Finds embedded players on the page that yt-dlp already supports.
        ///
        /// Used when the page itself is not a site yt-dlp knows: the video is frequently
        /// hosted somewhere it does know, and the page is only framing it.
        /// </summary>
        public async Task<List<EmbeddedPlayer>> FindEmbeddedPlayersAsync(
            string pageUrl, bool renderJavaScript = false)
        {
            string? html = renderJavaScript
                ? await RenderWithPlaywrightAsync(pageUrl)
                : await FetchAsync(pageUrl);

            if (string.IsNullOrWhiteSpace(html)) return new List<EmbeddedPlayer>();

            var finder = new EmbeddedPlayerFinder();
            finder.OnDiagnostic += message => OnLog?.Invoke(message);

            return finder.Find(html, pageUrl);
        }

        /// <summary>
        /// Reads a season and episode from a link, preferring a combined "S01E01" form because
        /// it is unambiguous. Falls back to an episode number alone.
        /// </summary>
        public static (int? Season, int? Episode) DetectSeasonAndEpisode(string? linkText, string url)
        {
            foreach (var source in new[] { linkText, Uri.UnescapeDataString(url) })
            {
                if (string.IsNullOrWhiteSpace(source)) continue;

                var combined = SeasonEpisodePattern.Match(source);
                if (combined.Success
                    && int.TryParse(combined.Groups[1].Value, out int season)
                    && int.TryParse(combined.Groups[2].Value, out int episode)
                    && episode > 0)
                {
                    return (season, episode);
                }
            }

            return (null, DetectEpisodeNumber(linkText, url));
        }

        /// <summary>
        /// Pulls an episode number out of link text first (more reliable) then the URL.
        /// Public so the parsing rules can be unit tested without a network round trip.
        /// </summary>
        public static int? DetectEpisodeNumber(string? linkText, string url)
        {
            foreach (var source in new[] { linkText, Uri.UnescapeDataString(url) })
            {
                if (string.IsNullOrWhiteSpace(source)) continue;

                var match = EpisodeNumberPattern.Match(source);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number) && number > 0)
                {
                    return number;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the largest group of links sharing a first path segment, when that group is
        /// big enough to be a content listing rather than a menu.
        ///
        /// For a page at /serie/the-pink-panther-show/ whose items live at /cartoon/..., this
        /// finds the two dozen /cartoon/ links and ignores the handful of nav links, without
        /// needing to know that this particular site calls them cartoons.
        /// </summary>
        private List<EpisodeLink> FindLargestLinkGroup(List<EpisodeLink> candidates, Uri baseUri)
        {
            var chosen = SelectLargestLinkGroup(candidates, baseUri.AbsolutePath, out string? segment);

            if (chosen.Count > 0)
            {
                OnLog?.Invoke($"SeriesIndexer: {chosen.Count} link(s) share the path '/{segment}/', "
                            + "which looks like this page's item listing.");
            }

            return chosen;
        }

        /// <summary>
        /// The grouping decision, without any I/O or logging, so it can be tested directly.
        /// </summary>
        public static List<EpisodeLink> SelectLargestLinkGroup(
            List<EpisodeLink> candidates, string seriesPath, out string? chosenSegment)
        {
            chosenSegment = null;

            // Below this a "group" is just as likely to be a menu as a listing.
            const int MinimumGroupSize = 3;

            string? seriesSegment = FirstPathSegment(seriesPath);

            var groups = candidates
                .Select(c => new { Link = c, Segment = FirstPathSegmentOf(c.Url) })
                .Where(x => x.Segment != null)
                .GroupBy(x => x.Segment!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Segment = g.Key, Links = g.Select(x => x.Link).ToList() })
                .OrderByDescending(g => g.Links.Count)
                .ToList();

            foreach (var group in groups)
            {
                if (group.Links.Count < MinimumGroupSize) break;

                // Links sharing the series page's own segment are usually other series, not
                // this one's episodes - unless nothing else is on offer.
                bool isSeriesSegment = seriesSegment != null
                    && string.Equals(group.Segment, seriesSegment, StringComparison.OrdinalIgnoreCase);

                if (isSeriesSegment && groups.Any(g => g.Links.Count >= MinimumGroupSize
                                                       && !string.Equals(g.Segment, seriesSegment, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                chosenSegment = group.Segment;
                return group.Links;
            }

            return new List<EpisodeLink>();
        }

        private static string? FirstPathSegmentOf(string url)
        {
            try { return FirstPathSegment(new Uri(url).AbsolutePath); }
            catch { return null; }
        }

        private static string? FirstPathSegment(string absolutePath)
        {
            var segment = absolutePath
                .Split('/')
                .FirstOrDefault(p => p.Length > 0);

            return string.IsNullOrWhiteSpace(segment) ? null : segment;
        }

        /// <summary>
        /// Last-resort heuristic for sites that name episode pages without the word "episode".
        /// </summary>
        private static bool LooksLikeEpisodeUrl(string url)
        {
            string lowered = url.ToLowerInvariant();
            return lowered.Contains("/watch/")
                || lowered.Contains("/episode")
                || lowered.Contains("/ep-")
                || lowered.Contains("/video/");
        }

        private async Task<string?> FetchAsync(string url)
        {
            try
            {
                using var response = await _http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    OnLog?.Invoke($"SeriesIndexer: {url} returned HTTP {(int)response.StatusCode}.");
                    return null;
                }
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"SeriesIndexer: fetch failed for {url}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Renders the page in headless Chromium, for listings built by JavaScript.
        /// </summary>
        private async Task<string?> RenderWithPlaywrightAsync(string url)
        {
            try
            {
                using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(
                    new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });

                var context = await browser.NewContextAsync(new Microsoft.Playwright.BrowserNewContextOptions
                {
                    UserAgent = ToolManagerService.FIREFOX_USER_AGENT
                });

                var page = await context.NewPageAsync();
                await page.GotoAsync(url, new Microsoft.Playwright.PageGotoOptions
                {
                    WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                return await page.ContentAsync();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"SeriesIndexer: headless render failed for {url}: {ex.Message}");
                return null;
            }
        }
    }
}
