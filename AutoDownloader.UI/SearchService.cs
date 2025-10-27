using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoDownloader.Core
{
    /// <summary>
    /// Service to find a valid yt-dlp URL from a search term using the Gemini API.
    /// </summary>
    public class SearchService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // V1.8 NEW: Field to hold the API key passed from SettingsService
        private readonly string _geminiApiKey;

        // V1.8 NEW: Constructor now accepts the Gemini API key (Fixes CS0161 dependency)
        public SearchService(string geminiApiKey)
        {
            _geminiApiKey = geminiApiKey;
        }

        /// <summary>
        /// Attempts to find a valid yt-dlp URL and media type from a search term.
        /// </summary>
        /// <param name="searchTerm">The name of the show/movie to search for.</param>
        /// <returns>A tuple containing the (Type, Url). Url will be "not-found" on failure.</returns>
        public async Task<(string Type, string Url)> FindShowUrlAsync(string searchTerm)
        {
            // V1.8 FIX: If the key is missing, return a default value immediately.
            // This ensures all code paths return a value (Fixes CS0161).
            if (string.IsNullOrWhiteSpace(_geminiApiKey) || _geminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
            {
                return ("TV Show", "not-found");
            }

            // --- The rest of the prompt and search logic starts here ---
            string prompt = $@"
                You are an expert web search assistant and media specialist for personal media archival.
                Your task is to find the most relevant, official, and high-quality URL for the TV series ""{searchTerm}"".

                **Action:**
                1. Search the web to find the main series or season page for ""{searchTerm}"" that is supported by **yt-dlp**.
                2. **CRITICAL:** Prioritize finding the content on these high-quality, free, and legal streaming sites: Tubi (tubitv.com), Pluto TV (pluto.tv), The Roku Channel, Plex, or YouTube.
                3. **Ensure the URL points to the highest-level page that contains multiple episodes (a playlist or series hub).**
                
                **Classification:** Classify the search term as 'Anime', 'TV Show', 'Movie', or 'Playlist'.
                
                **Response:** You *must* respond with only a JSON object.
                - If you find a URL: {{""type"": ""(your classification)"", ""url"": ""(the full series URL you found)""}}
                - If you cannot find one: {{""type"": ""(your classification)"", ""url"": ""not-found""}}
            ";

            // V1.8 FIX: Use the key from the class field
            string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-preview-09-2025:generateContent?key={_geminiApiKey}";

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