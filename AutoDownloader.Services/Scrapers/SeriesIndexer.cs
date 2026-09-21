using AngleSharp.Html.Parser;
using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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
        /// <summary>
        /// A tile numbered at the front, as a listing usually does: "1." or "1)".
        /// The trailing punctuation is what keeps a year or a duration out.
        /// </summary>
        private static readonly Regex LeadingListNumberPattern = new Regex(
            "^([0-9]{1,3})[.)]",
            RegexOptions.Compiled);

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
            List<EpisodeLink> candidates,
            bool fromDocument = true)
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
                    FromDocument = fromDocument,
                });
            }
        }

        /// <summary>
        /// The seasons a show page offers, or an empty list when it offers no choice.
        /// </summary>
        public async Task<List<int>> DiscoverSeasonsAsync(string seriesUrl)
        {
            try
            {
                using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(
                    new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });

                var context = await NewContextAsync(browser);
                var page = await context.NewPageAsync();

                await page.GotoAsync(seriesUrl, new Microsoft.Playwright.PageGotoOptions
                {
                    WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                    Timeout = 45000
                });

                await page.WaitForTimeoutAsync(3000);

                return await SeasonNavigator.DiscoverSeasonsAsync(page);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"SeriesIndexer: could not read the season list: {ex.Message}");
                return new List<int>();
            }
        }

        /// <summary>
        /// Indexes several seasons by working the page's own season chooser.
        ///
        /// Each season is read while the page is actually showing it, so the season number is
        /// recorded rather than guessed - which is the difference between filing nineteen
        /// seasons correctly and flattening them all into season one.
        /// </summary>
        public async Task<List<EpisodeLink>> IndexSeasonsAsync(
            string seriesUrl, IReadOnlyCollection<int> seasons)
        {
            var everything = new List<EpisodeLink>();

            if (seasons == null || seasons.Count == 0) return everything;

            Uri baseUri;
            try { baseUri = new Uri(seriesUrl); }
            catch
            {
                OnLog?.Invoke($"SeriesIndexer: not a usable URL: {seriesUrl}");
                return everything;
            }

            try
            {
                using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(
                    new Microsoft.Playwright.BrowserTypeLaunchOptions { Headless = true });

                var context = await NewContextAsync(browser);
                var page = await context.NewPageAsync();

                var json = new List<string>();
                page.Response += async (_, response) =>
                {
                    try
                    {
                        if (!response.Headers.TryGetValue("content-type", out var type)) return;
                        if (type.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0) return;

                        string body = await response.TextAsync().ConfigureAwait(false);
                        lock (json) { json.Add(body); }
                    }
                    catch { }
                };

                await page.GotoAsync(seriesUrl, new Microsoft.Playwright.PageGotoOptions
                {
                    WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                    Timeout = 45000
                });

                await page.WaitForTimeoutAsync(3000);

                foreach (var season in seasons.Distinct().OrderBy(n => n))
                {
                    if (!await SeasonNavigator.SelectSeasonAsync(page, season).ConfigureAwait(false))
                    {
                        OnLog?.Invoke($"SeriesIndexer: could not switch to season {season}; skipping it.");
                        continue;
                    }

                    // Only what arrived since the switch belongs to this season.
                    List<string> seasonJson;
                    lock (json) { seasonJson = new List<string>(json); json.Clear(); }

                    await ScrollToLoadEverythingAsync(page);

                    lock (json) { seasonJson.AddRange(json); json.Clear(); }

                    string html = await page.ContentAsync();

                    var candidates = new List<EpisodeLink>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    CollectCandidates(html, page.Url, baseUri, seriesUrl, seen, candidates);

                    _lastRenderJson = seasonJson;

                    CollectFromJsonEpisodes(seasonJson, page.Url, baseUri, seriesUrl, seen, candidates);

                    string fromJson = BuildHtmlFromJsonPaths(seasonJson);
                    if (fromJson.Length > 0)
                    {
                        CollectCandidates(fromJson, page.Url, baseUri, seriesUrl, seen, candidates,
                            fromDocument: false);
                    }

                    var chosen = SelectLargestLinkGroup(candidates, baseUri.AbsolutePath, out _);

                    // The page was showing this season, so say so rather than letting the
                    // numbering be inferred from whatever the links happen to look like.
                    foreach (var link in chosen) link.DetectedSeasonNumber = season;

                    OnLog?.Invoke($"SeriesIndexer: season {season} -> {chosen.Count} episode link(s).");
                    everything.AddRange(chosen);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"SeriesIndexer: season indexing failed: {ex.Message}");
            }

            return everything;
        }

        /// <summary>The browser context used for every render, carrying the user's session.</summary>
        private async Task<Microsoft.Playwright.IBrowserContext> NewContextAsync(
            Microsoft.Playwright.IBrowser browser)
        {
            var context = await browser.NewContextAsync(new Microsoft.Playwright.BrowserNewContextOptions
            {
                UserAgent = ToolManagerService.FIREFOX_USER_AGENT,
                ViewportSize = new Microsoft.Playwright.ViewportSize { Width = 1920, Height = 1080 }
            });

            if (Cookies.Count > 0)
            {
                await context.AddCookiesAsync(Cookies).ConfigureAwait(false);
            }

            return context;
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

                // The page may have fetched its list rather than shipped it. These go in
                // alongside the document's own links rather than replacing them, so the
                // grouping below still decides which run of links is the episode list -
                // checking the candidate count first would not work, since that count is the
                // raw pre-grouping list and includes every nav and footer link on the page.
                if (renderJavaScript && _lastRenderJson.Count > 0)
                {
                    // Best case: the JSON says which episode each item is. A page has to know
                    // that to print "S5 E2" beside a tile, and taking it directly beats
                    // inferring it back from page order - which is wrong whenever the page is
                    // showing a season other than the one that was asked for.
                    int numberedFromApi = CollectFromJsonEpisodes(
                        _lastRenderJson, currentPage, baseUri, seriesUrl, seen, candidates);

                    if (numberedFromApi > 0)
                    {
                        OnLog?.Invoke($"SeriesIndexer: the page listed {added} link(s); its API responses "
                            + $"carried {numberedFromApi} more, with season and episode numbers.");
                        added += numberedFromApi;
                    }

                    string fromJson = BuildHtmlFromJsonPaths(_lastRenderJson);

                    if (fromJson.Length > 0)
                    {
                        int beforeJson = candidates.Count;
                        CollectCandidates(fromJson, currentPage, baseUri, seriesUrl, seen, candidates,
                            fromDocument: false);
                        int fromApi = candidates.Count - beforeJson;

                        if (fromApi > 0)
                        {
                            OnLog?.Invoke($"SeriesIndexer: the page listed {added} link(s) but its API "
                                + $"responses carried {fromApi} more.");
                            added += fromApi;
                        }
                    }
                }

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
            foreach (var source in new[] { StripIdentifiers(linkText), StripIdentifiers(Uri.UnescapeDataString(url)) })
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

            // A listing very often numbers its tiles - "1. Steak and Sides" - and that is a
            // far better signal than the tile's position, which is only right while the page
            // happens to be sorted the way the databases are.
            //
            // Only at the very start, and only when followed by a full stop or bracket: the
            // rest of a tile is full of numbers that are not episode numbers, like the year
            // and the running time in "TV-G 22m 2008".
            if (!string.IsNullOrWhiteSpace(linkText))
            {
                var leading = LeadingListNumberPattern.Match(linkText!.TrimStart());
                if (leading.Success
                    && int.TryParse(leading.Groups[1].Value, out int listed)
                    && listed > 0)
                {
                    return (null, listed);
                }
            }

            return (null, DetectEpisodeNumber(linkText, url));
        }

        /// <summary>
        /// Blanks out ids before anything tries to read episode numbers out of a string.
        ///
        /// A UUID is a rich source of accidental matches: in
        /// /video/watch/6149ec80-e617-4bd6-bd0e-abf48e1a30b5 the run "-e617-" reads exactly
        /// like "episode 617" to a pattern looking for e-then-digits, dashes and all.
        ///
        /// That is not a cosmetic mislabel. Two of a hundred and fifty links picking up a
        /// bogus number was enough to convince the indexer that the page numbered its
        /// episodes, so it kept those two and discarded everything else.
        ///
        /// The patterns avoid backslash escapes entirely, which keeps them readable and
        /// survives being edited by tooling that eats them.
        /// </summary>
        public static string StripIdentifiers(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            // 8-4-4-4-12 hexadecimal: a UUID in its usual written form.
            string cleaned = Regex.Replace(
                value,
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
                " ");

            // Any other long hexadecimal run: a hash or an id, never an episode number.
            cleaned = Regex.Replace(cleaned, "[0-9a-fA-F]{12,}", " ");

            return cleaned;
        }

        /// <summary>
        /// Pulls an episode number out of link text first (more reliable) then the URL.
        /// Public so the parsing rules can be unit tested without a network round trip.
        /// </summary>
        public static int? DetectEpisodeNumber(string? linkText, string url)
        {
            foreach (var source in new[] { StripIdentifiers(linkText), StripIdentifiers(Uri.UnescapeDataString(url)) })
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


            string? seriesSegment = FirstPathSegment(seriesPath);

            var groups = candidates
                .Select(c => new { Link = c, Segment = FirstPathSegmentOf(c.Url) })
                .Where(x => x.Segment != null)
                .GroupBy(x => x.Segment!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Segment = g.Key, Links = g.Select(x => x.Link).ToList() })

                // A group the page actually links to outranks one that only appeared in an
                // API payload, however many of the latter there are. On a Discovery+ show
                // page the user's own watchlist supplied 36 mined paths against 25 real
                // episode links, and won on count alone - so the indexer returned the
                // watchlist instead of the episodes.
                //
                // Size still decides between groups of equal standing, which is what keeps
                // working the pages whose markup carries no links at all and where mining is
                // the only source there is.
                .OrderByDescending(g => g.Links.Any(l => l.FromDocument) ? 1 : 0)
                .ThenByDescending(g => g.Links.Count)
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
        /// The user's browser session, so pages that only show their content to a signed-in
        /// visitor are seen the same way they see them. Empty means browse anonymously.
        /// </summary>
        public List<Microsoft.Playwright.Cookie> Cookies { get; set; } =
            new List<Microsoft.Playwright.Cookie>();

        /// <summary>
        /// The smallest run of similar links worth treating as an episode list. Shared by the
        /// grouping and by the decision to go looking in the page's API responses.
        /// </summary>
        private const int MinimumGroupSize = 3;

        /// <summary>
        /// Adds episodes the page's own API responses described, keeping the season and
        /// episode numbers they came with.
        /// </summary>
        /// <returns>How many were added.</returns>
        private static int CollectFromJsonEpisodes(
            List<string> jsonBodies,
            string pageUrl,
            Uri baseUri,
            string seriesUrl,
            HashSet<string> seen,
            List<EpisodeLink> candidates)
        {
            Uri pageUri;
            try { pageUri = new Uri(pageUrl); }
            catch { pageUri = baseUri; }

            int added = 0;

            foreach (var episode in JsonEpisodeExtractor.Extract(jsonBodies))
            {
                if (!episode.IsNumbered) continue;

                Uri absolute;
                try { absolute = new Uri(pageUri, episode.Path); }
                catch { continue; }

                if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;
                if (!string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

                string url = absolute.ToString();
                if (url.TrimEnd('/').Equals(seriesUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(url)) continue;

                candidates.Add(new EpisodeLink
                {
                    Url = url,
                    LinkText = episode.Name,
                    DetectedSeasonNumber = episode.SeasonNumber,
                    DetectedEpisodeNumber = episode.EpisodeNumber,
                });

                added++;
            }

            return added;
        }

        /// <summary>
        /// JSON bodies seen during the most recent headless render.
        /// </summary>
        private List<string> _lastRenderJson = new List<string>();

        /// <summary>
        /// Turns paths found in the page's own API responses into anchors, so the ordinary
        /// link handling can treat them like any other listing.
        ///
        /// A site built as a single-page app renders its episode tiles from JSON and gives
        /// them no href at all - clicking is handled in script. A Food Network season page
        /// carried two anchors in a 750 KB document while the API response behind it listed
        /// seventeen episodes. Reading what the page itself fetched is the difference between
        /// seeing a whole season and seeing whatever happened to be a real link.
        ///
        /// Rebuilt as HTML rather than handled separately, so grouping, slug filtering and
        /// episode detection all apply unchanged.
        /// </summary>
        public static string BuildHtmlFromJsonPaths(IEnumerable<string> jsonBodies)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Two or more path segments: enough to look like a page rather than a bare word,
            // without assuming anything about what a given site calls its episodes.
            var pattern = new Regex("\"(/[A-Za-z0-9._~-]+(?:/[A-Za-z0-9._~-]+)+)\"",
                RegexOptions.Compiled);

            foreach (var body in jsonBodies)
            {
                if (string.IsNullOrWhiteSpace(body)) continue;

                foreach (Match match in pattern.Matches(body))
                {
                    paths.Add(match.Groups[1].Value);
                }
            }

            if (paths.Count == 0) return string.Empty;

            var builder = new StringBuilder("<html><body>");

            foreach (var path in paths)
            {
                builder.Append("<a href=\"").Append(path).Append("\">").Append(path).Append("</a>");
            }

            return builder.Append("</body></html>").ToString();
        }

        /// <summary>
        /// Scrolls to the bottom until the page stops growing.
        ///
        /// Taking the HTML the moment the network goes idle captures only what has been
        /// rendered so far, and a listing that loads more as you scroll has rendered almost
        /// nothing at that point. A Food Network season page yielded two episodes this way -
        /// exactly the two above the fold - while the season actually held far more.
        ///
        /// Stops as soon as a scroll adds nothing, so an ordinary page costs one extra check
        /// rather than the full budget.
        /// </summary>
        private static async Task ScrollToLoadEverythingAsync(Microsoft.Playwright.IPage page)
        {
            const int MaxScrolls = 40;
            const int SettleMs = 600;

            try
            {
                int lastHeight = 0;
                int unchanged = 0;

                for (int i = 0; i < MaxScrolls; i++)
                {
                    int height = await page.EvaluateAsync<int>(
                        "() => { window.scrollTo(0, document.body.scrollHeight); return document.body.scrollHeight; }");

                    await page.WaitForTimeoutAsync(SettleMs);

                    if (height <= lastHeight)
                    {
                        // Two stable passes, because a slow fetch can land between them.
                        if (++unchanged >= 2) break;
                    }
                    else
                    {
                        unchanged = 0;
                        lastHeight = height;
                    }
                }

                // Back to the top: some listings render in place and only keep what is near
                // the viewport, so leaving the page at the bottom can lose the early items.
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                await page.WaitForTimeoutAsync(SettleMs);
            }
            catch
            {
                // Scrolling is an improvement, not a requirement: whatever rendered so far is
                // still worth returning.
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
                    UserAgent = ToolManagerService.FIREFOX_USER_AGENT,

                    // A taller window is a cheap way to get more of a lazy list rendered
                    // before any scrolling is needed at all.
                    ViewportSize = new Microsoft.Playwright.ViewportSize { Width = 1920, Height = 1080 }
                });

                // A show page often lists its episodes only to a signed-in visitor. Without
                // these the scanner sees an empty season and looks like a broken indexer.
                if (Cookies.Count > 0)
                {
                    await context.AddCookiesAsync(Cookies).ConfigureAwait(false);
                }

                var page = await context.NewPageAsync();

                // Keep whatever JSON the page fetches. A site that renders its episode list
                // from an API leaves nothing in the document to find, but the list itself
                // went past on the wire.
                var json = new List<string>();
                page.Response += async (_, response) =>
                {
                    try
                    {
                        if (!response.Headers.TryGetValue("content-type", out var type)) return;
                        if (type.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0) return;

                        string body = await response.TextAsync().ConfigureAwait(false);
                        lock (json) { json.Add(body); }
                    }
                    catch
                    {
                        // A body that cannot be read is simply not available to mine.
                    }
                };

                await page.GotoAsync(url, new Microsoft.Playwright.PageGotoOptions
                {
                    WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                await ScrollToLoadEverythingAsync(page);

                lock (json) { _lastRenderJson = new List<string>(json); }

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
