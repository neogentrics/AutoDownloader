using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Service to find a valid yt-dlp URL from a search term using the Gemini API.
    /// </summary>
    public class SearchService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Attempts to find a valid yt-dlp URL and media type from a search term.
        /// </summary>
        /// <param name="searchTerm">The name of the show/movie to search for.</param>
        /// <returns>A tuple containing the (Type, Url). Url will be "not-found" on failure.</returns>
        public async Task<(string Type, string Url)> FindShowUrlAsync(string searchTerm)
        {
            // --- This is the new, flexible, and robust prompt ---
            // It is now "primed" with the knowledge from your research document.
            string prompt = $@"
                You are an expert web search assistant and media specialist.
                Your task is to find the most relevant, official, or high-quality URL for a show and classify it.

                **High-Priority Sites to Check First:**
                Based on user research, please prioritize finding the content on these high-quality, legal sites:
                - Tubi (tubitv.com)
                - Pluto TV (pluto.tv)
                - The Roku Channel (therokuchannel.roku.com)
                - Plex (plex.tv)
                - YouTube (youtube.com)
                - Crunchyroll (crunchyroll.com)
                - Kanopy (kanopy.com)
                
                1.  **Search Term:** ""{searchTerm}""
                2.  **Action:** Search the web to find the *most official or popular* main page for this show. **Prioritize the sites listed above.**
                3.  **Classification:** Classify the search term as 'Anime', 'TV Show', 'Movie', or 'Playlist'.
                4.  **Response:** You *must* respond with only a JSON object.
                    - If you find a URL: `{{""type"": ""(your classification)"", ""url"": ""(the full URL you found)""}}`
                    - If you cannot find one: `{{""type"": ""(your classification)"", ""url"": ""not-found""}}`
            ";

            // The API key is an empty string. The environment will provide it at runtime.
            const string apiKey = "";
            string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-preview-09-2025:generateContent?key={apiKey}";

            var payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                // We MUST include the search tool for it to browse the web!
                tools = new[]
                {
                    new { google_search = new {} }
                }
                // --- We have REMOVED the rigid 'generationConfig' block ---
                // This makes the request far more flexible and less likely to
                // fail, as our parsing logic (below) handles a text response.
            };

            try
            {
                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Implement basic exponential backoff for retries
                int maxRetries = 3;
                for (int i = 0; i < maxRetries; i++)
                {
                    var response = await _httpClient.PostAsync(apiUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        using (var jsonDoc = JsonDocument.Parse(responseBody))
                        {
                            // Navigate the complex JSON structure from Gemini
                            var candidate = jsonDoc.RootElement
                                .GetProperty("candidates")[0];

                            // Check for safety ratings first
                            if (candidate.TryGetProperty("finishReason", out var finishReason) && finishReason.GetString() != "STOP")
                            {
                                throw new Exception($"Search failed. Reason: {finishReason.GetString()}");
                            }

                            if (candidate.TryGetProperty("content", out var contentElem) &&
                                contentElem.TryGetProperty("parts", out var partsElem) &&
                                partsElem[0].TryGetProperty("text", out var textElem))
                            {
                                // The AI's response is *itself* a JSON string. We need to parse *that*.
                                var innerJson = textElem.GetString() ?? "{}";

                                // Sanitize the inner JSON string if it's wrapped in markdown
                                if (innerJson.StartsWith("```json"))
                                {
                                    innerJson = innerJson.Substring(7, innerJson.Length - 10).Trim();
                                }

                                using (var innerDoc = JsonDocument.Parse(innerJson))
                                {
                                    var root = innerDoc.RootElement;
                                    string type = root.TryGetProperty("type", out var typeElem) ? typeElem.GetString() ?? "TV Show" : "TV Show";
                                    string url = root.TryGetProperty("url", out var urlElem) ? urlElem.GetString() ?? "not-found" : "not-found";
                                    return (type, url);
                                }
                            }
                        }
                    }
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429) // Too Many Requests
                    {
                        // Wait and retry
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
                    }
                    else
                    {
                        // Handle other non-success statuses
                        var errorBody = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Gemini API Error: {response.StatusCode}. Details: {errorBody}");
                    }
                }

                throw new Exception("Search failed after retries (rate-limited).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SearchService: {ex.Message}");
                return ("TV Show", "not-found"); // Fallback on any exception
            }
        }
    }
}

