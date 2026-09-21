using AngleSharp.Html.Parser;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// One video embed discovered on a page.
    /// </summary>
    public class EmbeddedPlayer
    {
        /// <summary>The URL to hand to yt-dlp.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>The platform it belongs to, e.g. "YouTube".</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>Where on the page it was found, for the log.</summary>
        public string Source { get; set; } = string.Empty;
    }

    /// <summary>
    /// Finds embedded players on a page that yt-dlp already knows how to handle.
    ///
    /// A great many pages do not host video themselves - they embed it. Lecture pages,
    /// documentation, news articles and blogs routinely put a YouTube or Vimeo iframe in the
    /// middle of the text. yt-dlp's generic extractor catches some of these, but not reliably,
    /// and when it misses one the page looks unsupported even though the video behind it is
    /// perfectly well supported.
    ///
    /// Pulling the embed URL out and handing that to yt-dlp instead turns a large class of
    /// "unsupported site" into an ordinary download, without needing any site-specific
    /// knowledge: the work is done by whichever extractor already covers the platform.
    /// </summary>
    public class EmbeddedPlayerFinder
    {
        public event Action<string>? OnDiagnostic;

        /// <summary>
        /// Hosts with a dedicated yt-dlp extractor, mapped to a readable platform name.
        /// Matched as a substring of the embed URL's host.
        /// </summary>
        private static readonly (string HostFragment, string Platform)[] KnownPlatforms =
        {
            ("youtube.com",      "YouTube"),
            ("youtube-nocookie", "YouTube"),
            ("youtu.be",         "YouTube"),
            ("player.vimeo.com", "Vimeo"),
            ("vimeo.com",        "Vimeo"),
            ("dailymotion.com",  "Dailymotion"),
            ("dai.ly",           "Dailymotion"),
            ("archive.org",      "Internet Archive"),
            ("jwplatform.com",   "JW Player"),
            ("jwplayer.com",     "JW Player"),
            ("cdn.jwplayer.com", "JW Player"),
            ("streamable.com",   "Streamable"),
            ("wistia.com",       "Wistia"),
            ("wistia.net",       "Wistia"),
            ("brightcove.net",   "Brightcove"),
            ("brightcove.com",   "Brightcove"),
            ("kaltura.com",      "Kaltura"),
            ("libsyn.com",       "Libsyn"),
            ("soundcloud.com",   "SoundCloud"),
            ("bitchute.com",     "BitChute"),
            ("odysee.com",       "Odysee"),
            ("rumble.com",       "Rumble"),
            ("twitch.tv",        "Twitch"),
            ("facebook.com/plugins/video", "Facebook"),
            ("player.twitch.tv", "Twitch"),
            ("peertube",         "PeerTube"),
            ("media.ccc.de",     "media.ccc.de"),
        };

        /// <summary>
        /// Scans HTML for embeds pointing at a platform yt-dlp supports.
        /// </summary>
        /// <param name="html">The page source, already fetched or rendered.</param>
        /// <param name="pageUrl">The page's own URL, for resolving relative sources.</param>
        public List<EmbeddedPlayer> Find(string? html, string pageUrl)
        {
            var found = new List<EmbeddedPlayer>();
            if (string.IsNullOrWhiteSpace(html)) return found;

            Uri? baseUri = null;
            try { baseUri = new Uri(pageUrl); } catch { }

            var document = new HtmlParser().ParseDocument(html);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Consider(string? candidate, string source)
            {
                if (string.IsNullOrWhiteSpace(candidate)) return;

                string absolute = candidate.Trim();

                // Protocol-relative embeds are extremely common in older markup.
                if (absolute.StartsWith("//")) absolute = "https:" + absolute;

                if (!absolute.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (baseUri == null) return;
                    try { absolute = new Uri(baseUri, absolute).ToString(); } catch { return; }
                }

                var platform = MatchPlatform(absolute);
                if (platform == null) return;

                if (!seen.Add(absolute)) return;

                found.Add(new EmbeddedPlayer { Url = absolute, Platform = platform, Source = source });
            }

            foreach (var frame in document.QuerySelectorAll("iframe"))
            {
                Consider(frame.GetAttribute("src"), "iframe");
                // Lazy-loaded players keep the real URL out of src until scrolled into view.
                Consider(frame.GetAttribute("data-src"), "iframe[data-src]");
                Consider(frame.GetAttribute("data-lazy-src"), "iframe[data-lazy-src]");
            }

            foreach (var embed in document.QuerySelectorAll("embed, object"))
            {
                Consider(embed.GetAttribute("src"), "embed");
                Consider(embed.GetAttribute("data"), "object");
            }

            foreach (var meta in document.QuerySelectorAll(
                         "meta[property='og:video'], meta[property='og:video:url'], "
                         + "meta[property='og:video:secure_url'], meta[name='twitter:player']"))
            {
                Consider(meta.GetAttribute("content"), "meta");
            }

            foreach (var anchor in document.QuerySelectorAll("a"))
            {
                Consider(anchor.GetAttribute("href"), "link");
            }

            if (found.Count > 0)
            {
                OnDiagnostic?.Invoke(
                    $"EmbeddedPlayerFinder: {found.Count} embed(s) on supported platforms: "
                    + string.Join(", ", found.Select(f => f.Platform).Distinct()));
            }

            return found;
        }

        private static string? MatchPlatform(string url)
        {
            string lowered = url.ToLowerInvariant();

            foreach (var (fragment, platform) in KnownPlatforms)
            {
                if (lowered.Contains(fragment, StringComparison.Ordinal)) return platform;
            }

            return null;
        }
    }
}
