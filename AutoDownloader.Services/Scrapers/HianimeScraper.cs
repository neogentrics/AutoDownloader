using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Scrapers
{
 public class HianimeScraper : IScraper
 {
 private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

 public async Task<List<string>> GetPlayableUrlsAsync(string url)
 {
 var results = new List<string>();
 try
 {
 var resp = await _http.GetStringAsync(url).ConfigureAwait(false);
 var parser = new HtmlParser();
 var doc = parser.ParseDocument(resp);

 // hianime's watch links are usually under anchors pointing to '/watch/...'
 var anchors = doc.QuerySelectorAll("a");
 foreach (var a in anchors)
 {
 var href = a.GetAttribute("href");
 if (string.IsNullOrWhiteSpace(href)) continue;
 if (href.Contains("/watch/", StringComparison.OrdinalIgnoreCase))
 {
 // normalize to absolute
 var absolute = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : new Uri(new Uri(url), href).ToString();
 if (!results.Contains(absolute)) results.Add(absolute);
 }
 }

 // Some sites embed playlist JSON inside scripts. Try to extract video links by regex (fallback)
 var scriptMatches = Regex.Matches(resp, @"https?://[^\""]+\.(?:m3u8|mp4)", RegexOptions.IgnoreCase);
 foreach (Match m in scriptMatches)
 {
 var raw = m.Value;
 if (!results.Contains(raw)) results.Add(raw);
 }
 }
 catch
 {
 // ignore; return what we have
 }
 return results;
 }
 }
}
