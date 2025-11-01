using System;
using AutoDownloader.Services.Scrapers;

namespace AutoDownloader.Services
{
 public static class ScraperFactory
 {
 public static IScraper? GetScraperForUrl(string url)
 {
 try
 {
 var uri = new Uri(url);
 var host = uri.Host.ToLowerInvariant();
 if (host.Contains("hianime")) return new HianimeScraper();
 if (host.Contains("wcoanimesub") || host.Contains("watchcartoononline")) return new WcoAnimeScraper();
 // Fallback to Playwright-based scraper for JS-heavy sites
 return new PlaywrightScraper();
 }
 catch { return null; }
 }
 }
}
