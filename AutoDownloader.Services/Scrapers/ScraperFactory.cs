using System;
using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Services
{
 public static class ScraperFactory
 {
 /// <summary>
 /// Returns a scraper written specifically for this host, or null when there is none.
 ///
 /// This deliberately does NOT fall back to Playwright for unknown hosts. yt-dlp has
 /// purpose-built extractors for the sites this app targets (Tubi, Pluto, YouTube, ...)
 /// and handles their playlists natively. Routing those through a generic regex scraper
 /// first replaced the season page with a single scraped link - often an advert or
 /// trailer - and threw the rest of the season away.
 ///
 /// Use GetFallbackScraper() explicitly when yt-dlp has already failed.
 /// </summary>
 public static IScraper? GetScraperForUrl(string url)
 {
 try
 {
 var uri = new Uri(url);
 var host = uri.Host.ToLowerInvariant();
 if (host.Contains("hianime")) return new HianimeScraper();
 if (host.Contains("wcoanimesub") || host.Contains("watchcartoononline")) return new WcoAnimeScraper();
 return null;
 }
 catch { return null; }
 }

 /// <summary>
 /// The generic JS-capable scraper, for use as a LAST RESORT after yt-dlp itself has
 /// failed on a JS or Cloudflare protected page.
 /// </summary>
 public static IScraper GetFallbackScraper() => new PlaywrightScraper();
 }
}
