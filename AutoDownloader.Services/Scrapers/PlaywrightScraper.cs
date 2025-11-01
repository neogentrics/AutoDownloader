using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Scrapers
{
 public class PlaywrightScraper : IScraper
 {
 public async Task<List<string>> GetPlayableUrlsAsync(string url)
 {
 var results = new List<string>();
 try
 {
 using var playwright = await Playwright.CreateAsync();
 await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
 var context = await browser.NewContextAsync();
 var page = await context.NewPageAsync();
 await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout =15000 });

 // Get document content
 var content = await page.ContentAsync();

 // Try to extract anchors to watch pages
 var anchors = await page.QuerySelectorAllAsync("a");
 foreach (var a in anchors)
 {
 var href = await a.GetAttributeAsync("href");
 if (string.IsNullOrWhiteSpace(href)) continue;
 if (href.Contains("/watch/") || href.Contains("/episode/"))
 {
 var abs = href.StartsWith("http") ? href : new Uri(new Uri(url), href).ToString();
 if (!results.Contains(abs)) results.Add(abs);
 }
 }

 // Extract direct video sources (m3u8/mp4) from the HTML
 var matches = Regex.Matches(content, @"https?://[^""']+\.(?:m3u8|mp4)", RegexOptions.IgnoreCase);
 foreach (Match m in matches)
 {
 var raw = m.Value;
 if (!results.Contains(raw)) results.Add(raw);
 }

 // Try to read og:video or twitter player streams via DOM
 var ogVideo = await page.EvaluateAsync<string?>("() => (document.querySelector('meta[property=\'og:video\']') || document.querySelector('meta[property=\'og:video:url\']') || document.querySelector('meta[name=\'twitter:player:stream\']'))?.getAttribute('content')");
 if (!string.IsNullOrWhiteSpace(ogVideo) && !results.Contains(ogVideo)) results.Add(ogVideo);

 await browser.CloseAsync();
 }
 catch
 {
 // ignore Playwright failures; caller will handle
 }
 return results;
 }
 }
}
