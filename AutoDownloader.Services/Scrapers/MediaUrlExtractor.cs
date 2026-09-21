using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// Finds the media URL on a page by watching what the page REQUESTS, rather than by
    /// reading what the page CONTAINS.
    ///
    /// This is the difference that matters. Parsing HTML only works when the media URL is
    /// actually written in the markup - true of most course/lecture sites, where you get a
    /// plain &lt;video src&gt; or a direct file link. A typical streaming page instead serves an
    /// &lt;iframe&gt; pointing at a separate player host, and the real stream URL is assembled by
    /// JavaScript inside that frame at runtime, usually as an HLS .m3u8 manifest rather than a
    /// single file. None of that text exists in the HTML you downloaded, so no amount of
    /// regexing the document will ever find it.
    ///
    /// A player cannot play without fetching the media, though. So we load the page in a real
    /// browser and record the network traffic - including traffic from child frames, which is
    /// what reading page.Content() misses.
    ///
    /// This is a LAST RESORT. Try yt-dlp on the page first: it already understands well over a
    /// thousand sites, follows iframes, and handles HLS properly. Only reach for this when
    /// yt-dlp has come back with nothing.
    ///
    /// Note that a captured stream URL is frequently single-use: tied to a session cookie, a
    /// signed token, or an expiry. Hand it to yt-dlp promptly and pass along the referer.
    /// </summary>
    public class MediaUrlExtractor
    {
        /// <summary>
        /// The user's browser session, so pages that only show their content to a signed-in
        /// visitor are seen the same way they see them. Empty means browse anonymously.
        /// </summary>
        public List<Microsoft.Playwright.Cookie> Cookies { get; set; } =
            new List<Microsoft.Playwright.Cookie>();

        public event Action<string>? OnLog;

        /// <summary>
        /// File extensions and path markers that indicate a media stream or media manifest.
        /// </summary>
        private static readonly string[] MediaMarkers =
        {
            ".m3u8",   // HLS manifest - by far the most common for streaming
            ".mpd",    // MPEG-DASH manifest
            ".mp4",
            ".m4v",
            ".webm",
            ".mkv",
        };

        /// <summary>
        /// Content types that indicate a media response even when the URL gives nothing away
        /// (many CDNs serve streams from extension-less, signed URLs).
        /// </summary>
        private static readonly string[] MediaContentTypes =
        {
            "application/vnd.apple.mpegurl",
            "application/x-mpegurl",
            "application/dash+xml",
            "video/",
        };

        /// <summary>
        /// Loads the page, waits for the player to start fetching, and returns every distinct
        /// media URL observed, best candidate first.
        /// </summary>
        /// <param name="pageUrl">The episode/watch page to inspect.</param>
        /// <param name="settleSeconds">
        /// How long to keep watching after load. Players often defer the media request until
        /// after an advert or a user gesture, so a few seconds of patience matters.
        /// </param>
        public async Task<List<string>> ExtractMediaUrlsAsync(string pageUrl, int settleSeconds = 12)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions { Headless = true });

                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = ToolManagerService.FIREFOX_USER_AGENT
                });

                // Without these the player never starts on a page behind a login, so there
                // is no traffic to watch and the page looks as though it has no video at all.
                if (Cookies.Count > 0)
                {
                    await context.AddCookiesAsync(Cookies).ConfigureAwait(false);
                }

                var page = await context.NewPageAsync();

                // Watch EVERY request, including those issued by nested frames. This is the
                // whole point: the player lives in an iframe, so top-level DOM inspection
                // never sees its traffic.
                page.Request += (_, request) =>
                {
                    if (LooksLikeMedia(request.Url) && seen.Add(request.Url))
                    {
                        found.Add(request.Url);
                        OnLog?.Invoke($"MediaUrlExtractor: request -> {Truncate(request.Url)}");
                    }
                };

                // Responses give us the content type, which catches signed URLs that carry no
                // file extension at all.
                page.Response += (_, response) =>
                {
                    try
                    {
                        if (seen.Contains(response.Url)) return;

                        response.Headers.TryGetValue("content-type", out var contentType);
                        if (!string.IsNullOrEmpty(contentType)
                            && MediaContentTypes.Any(t => contentType.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (seen.Add(response.Url))
                            {
                                found.Add(response.Url);
                                OnLog?.Invoke($"MediaUrlExtractor: {contentType} -> {Truncate(response.Url)}");
                            }
                        }
                    }
                    catch { /* header access can race with navigation */ }
                };

                await page.GotoAsync(pageUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });

                // An episode link often does not lead to a player at all. Discovery+ bounces
                // its own deep links back to the show page, so what actually loads is a
                // listing with a "Watch S1 E1" button - and clicking blankly in the middle of
                // that hits artwork, starts nothing, and the capture comes back empty.
                //
                // So look for something that starts playback, press it, and keep listening
                // afterwards: pressing it usually navigates to the real player page, and the
                // manifest is only requested once we are there.
                await StartPlaybackAsync(page);

                // Keep listening while the player does its work.
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, settleSeconds)));

                await context.CloseAsync();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"MediaUrlExtractor: failed on {pageUrl}: {ex.Message}");
            }

            // Prefer manifests over individual files: an .m3u8 describes the whole stream at
            // every quality, whereas a stray .mp4 is often a trailer, a preroll advert or a
            // single low-quality rendition.
            return found
                .OrderByDescending(u => u.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                                     || u.Contains(".mpd", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(u => u.Length)
                .ToList();
        }

        private static bool LooksLikeMedia(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // Compare against the path only, so query strings full of tokens do not cause
            // false positives.
            string path;
            try { path = new Uri(url).AbsolutePath; }
            catch { path = url; }

            return MediaMarkers.Any(m => path.EndsWith(m, StringComparison.OrdinalIgnoreCase)
                                      || path.Contains(m + "?", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets something playing, by whatever control the page offers.
        ///
        /// Tried in order of how specific they are: a labelled play control first, then a
        /// link to a watch page, then the middle of the viewport for players that sit there
        /// waiting for any click. Every step is optional - many players simply autostart.
        /// </summary>
        private static async Task StartPlaybackAsync(IPage page)
        {
            string[] selectors =
            {
                "button[aria-label*='play' i]",
                "button[title*='play' i]",
                "[data-testid*='play' i]",
                "button:has-text('Watch')",
                "a:has-text('Watch')",
                "button:has-text('Play')",
                "a[href*='/video/watch/']",
                "a[href*='/watch/']",
            };

            foreach (var selector in selectors)
            {
                try
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element == null) continue;
                    if (!await element.IsVisibleAsync()) continue;

                    await element.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 });

                    // Pressing it usually navigates; the player then loads on the new page.
                    try
                    {
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                            new PageWaitForLoadStateOptions { Timeout = 15000 });
                    }
                    catch { /* a player that loads in place never goes idle */ }

                    return;
                }
                catch
                {
                    // Covered by another selector, or by the blank click below.
                }
            }

            try { await page.Mouse.ClickAsync(640, 360); }
            catch { /* not fatal - many players autostart */ }
        }

        private static string Truncate(string value) =>
            value.Length <= 140 ? value : value.Substring(0, 137) + "...";
    }
}
