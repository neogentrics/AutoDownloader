using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Scrapers
{
 public interface IScraper
 {
 /// <summary>
 /// Given a canonical show or watch page URL, returns a list of playable episode URLs (may be empty).
 /// </summary>
 Task<List<string>> GetPlayableUrlsAsync(string url);
 }
}
