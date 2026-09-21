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

            string? html = renderJavaScript
                ? await RenderWithPlaywrightAsync(seriesUrl)
                : await FetchAsync(seriesUrl);

            if (string.IsNullOrWhiteSpace(html))
            {
                OnLog?.Invoke($"SeriesIndexer: no HTML retrieved for {seriesUrl}");
                return results;
            }

            var baseUri = new Uri(seriesUrl);
            var doc = new HtmlParser().ParseDocument(html);

            // Collect every same-host anchor, in document order, de-duplicated by URL.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<EpisodeLink>();

            foreach (var anchor in doc.QuerySelectorAll("a"))
            {
                var href = anchor.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href)) continue;
                if (href.StartsWith("#") || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;

                Uri absolute;
                try { absolute = new Uri(baseUri, href); }
                catch { continue; }

                if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps) continue;

                // Stay on the same site; off-site links are adverts, socials or mirrors.
                if (!string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;

                string url = absolute.ToString();
                if (url.TrimEnd('/').Equals(seriesUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(url)) continue;

                string text = (anchor.TextContent ?? string.Empty).Trim();
                text = Regex.Replace(text, @"\s+", " ");

                candidates.Add(new EpisodeLink
                {
                    Url = url,
                    LinkText = text,
                    DetectedEpisodeNumber = DetectEpisodeNumber(text, url),
                });
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
                // No numbering to go on. Fall back to URL-shape heuristics.
                chosen = candidates.Where(c => LooksLikeEpisodeUrl(c.Url)).ToList();
                OnLog?.Invoke($"SeriesIndexer: no episode numbering found; {chosen.Count} link(s) matched by URL shape.");
            }

            // Sort by detected episode number when we have it, otherwise keep document order.
            if (chosen.Any(c => c.DetectedEpisodeNumber.HasValue))
            {
                chosen = chosen
                    .OrderBy(c => c.DetectedEpisodeNumber ?? int.MaxValue)
                    .ToList();
            }

            for (int i = 0; i < chosen.Count; i++)
            {
                chosen[i].Ordinal = i;
                results.Add(chosen[i]);
            }

            return results;
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
