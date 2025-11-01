using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoDownloader.Services // <-- CORRECTED: Now part of the Services project
{
    /// <summary>
    /// Handles "Smart Search" functionality.
    /// This service uses the Google Gemini API to perform a web search and find a 
    /// downloadable URL (like Tubi, YouTube) from a simple text query (e.g., "The Mandalorian").
    /// </summary>
    public class SearchService
    {
        /// <summary>
        /// A static, shared HttpClient for performance.
        /// </summary>
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// The user's private Gemini API key, passed in from the UI.
        /// </summary>
        private readonly string _geminiApiKey;

        /// <summary>
        /// Initializes a new instance of the SearchService.
        /// This uses Dependency Injection; it receives the API key from the host (the UI)
        /// rather than trying to read settings itself.
        /// </summary>
        /// <param name="geminiApiKey">The user's Gemini API key.</param>
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
            // --- 1. Guard Clause (Key Check) ---
            // If the key is missing or is still the default placeholder, don't even try.
            // This prevents API errors and unnecessary crashes if the user doesn't want to use this feature.
            if (string.IsNullOrWhiteSpace(_geminiApiKey) || _geminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
            {
                // Return "not-found" so the UI can tell the user the search failed.
                return ("TV Show", "not-found");
            }

            // --- 2. Create the AI Prompt ---
            // This prompt instructs the AI on its exact role, what sites to prioritize,
            // and (most importantly) the exact JSON format to respond with.
            string prompt = $@"
                You are an expert web search assistant and media specialist.
                Your task is to find the most relevant, official, or high-quality URL for the TV series ""{searchTerm}"".

                **Action:**
                1. Search the web to find the main series or season page for ""{searchTerm}"" that is supported by yt-dlp.
                2. Prioritize finding content on these sites: Tubi (tubitv.com), Pluto TV (pluto.tv), The Roku Channel, Plex, or YouTube.
                3. Ensure the URL points to the highest-level page (playlist or series hub).
                
                **Classification:** Classify the search term as 'Anime', 'TV Show', 'Movie', or 'Playlist'.
                
                **Response:** You *must* respond with only a JSON object.
                - If you find a URL: {{""type"": ""(your classification)"", ""url"": ""(the full URL you found)""}}
                - If you cannot find one: {{""type"": ""(your classification)"", ""url"": ""not-found""}}
            ";

            // --- 3. Build the API Request ---
            string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-preview-09-2025:generateContent?key={_geminiApiKey}";

            var payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                // CRITICAL: This "tools" section is what gives Gemini permission
                // to use its built-in Google Search tool to find live, real-time results.
                tools = new[]
                {
                    new { google_search = new {} }
                }
            };

            // --- 4. Execute the API Call with Retries ---
            try
            {
                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Basic retry logic for API rate limiting
                int maxRetries = 3;
                for (int i = 0; i < maxRetries; i++)
                {
                    var response = await _httpClient.PostAsync(apiUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        // --- 5. Parse the Response ---
                        var responseBody = await response.Content.ReadAsStringAsync();
                        using (var jsonDoc = JsonDocument.Parse(responseBody))
                        {
                            // Navigate the complex JSON structure from Gemini
                            var candidate = jsonDoc.RootElement
                                .GetProperty("candidates")[0];

                            // Check if the AI finished for a non-safe reason
                            if (candidate.TryGetProperty("finishReason", out var finishReason) && finishReason.GetString() != "STOP")
                            {
                                throw new Exception($"Search failed. Reason: {finishReason.GetString()}");
                            }

                            // Drill down into the response text
                            if (candidate.TryGetProperty("content", out var contentElem) &&
                                contentElem.TryGetProperty("parts", out var partsElem) &&
                                partsElem[0].TryGetProperty("text", out var textElem))
                            {
                                // The AI's response is *itself* a JSON string. We must parse *that* string.
                                var innerJson = textElem.GetString() ?? "{}";

                                // The AI sometimes wraps its JSON response in markdown. We must remove it.
                                if (innerJson.StartsWith("```json"))
                                {
                                    innerJson = innerJson.Substring(7, innerJson.Length - 10).Trim();
                                }

                                // Parse the *inner* JSON to get our final data
                                using (var innerDoc = JsonDocument.Parse(innerJson))
                                {
                                    var root = innerDoc.RootElement;
                                    string type = root.TryGetProperty("type", out var typeElem) ? typeElem.GetString() ?? "TV Show" : "TV Show";
                                    string url = root.TryGetProperty("url", out var urlElem) ? urlElem.GetString() ?? "not-found" : "not-found";

                                    // Success! Return the found data.
                                    return (type, url);
                                }
                            }
                        }
                    }
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429) // 429 = Too Many Requests
                    {
                        // Wait exponentially longer each time we fail
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
                    }
                    else
                    {
                        // Any other HTTP error
                        var errorBody = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Gemini API Error: {response.StatusCode}. Details: {errorBody}");
                    }
                }

                // If we exit the loop, all retries have failed.
                throw new Exception("Search failed after retries (rate-limited).");
            }
            catch (Exception ex)
            {
                // Catch any exception (API error, parsing error, etc.)
                // Log it to the console and return "not-found" so the app doesn't crash.
                Console.WriteLine($"Error in SearchService: {ex.Message}");
                return ("TV Show", "not-found"); // Fallback on any exception
            }
        }
    }
}