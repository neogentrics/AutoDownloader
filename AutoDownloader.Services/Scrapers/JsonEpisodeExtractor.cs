using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// Pulls episodes, with their real season and episode numbers, out of the JSON a page
    /// fetched to build itself.
    ///
    /// A single-page app knows exactly which episode each tile is - it has to, to render
    /// "S5 E2" next to it - and then throws that away in the markup. Inferring the numbering
    /// back from page order afterwards is guesswork that gets it wrong whenever the page is
    /// not sorted the way the databases are, or is showing a different season entirely.
    ///
    /// Field names are matched by shape rather than hardcoded, since every site names them
    /// slightly differently, and nothing here is specific to any one site.
    /// </summary>
    public static class JsonEpisodeExtractor
    {
        public class JsonEpisode
        {
            /// <summary>The link, as the JSON gave it: may be a path or an absolute URL.</summary>
            public string Path { get; set; } = string.Empty;

            public int? SeasonNumber { get; set; }

            public int? EpisodeNumber { get; set; }

            public string? Name { get; set; }

            /// <summary>True when this carries enough to place it in a season.</summary>
            public bool IsNumbered => SeasonNumber.HasValue && EpisodeNumber.HasValue;
        }

        /// <summary>
        /// Finds every object that looks like an episode: something with a link, and a number
        /// saying which episode it is.
        /// </summary>
        public static List<JsonEpisode> Extract(IEnumerable<string> jsonBodies)
        {
            var found = new List<JsonEpisode>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var body in jsonBodies ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(body)) continue;

                JsonDocument document;
                try { document = JsonDocument.Parse(body); }
                catch { continue; }

                using (document)
                {
                    foreach (var element in Walk(document.RootElement))
                    {
                        if (element.ValueKind != JsonValueKind.Object) continue;

                        var episode = ReadEpisode(element);
                        if (episode == null) continue;

                        // The same episode usually appears in several payloads, and in several
                        // collections within one payload.
                        if (!seen.Add(episode.Path)) continue;

                        found.Add(episode);
                    }
                }
            }

            return found;
        }

        private static JsonEpisode? ReadEpisode(JsonElement obj)
        {
            string? path = null;
            int? season = null;
            int? episode = null;
            string? name = null;

            foreach (var property in obj.EnumerateObject())
            {
                string key = property.Name.ToLowerInvariant();

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    string value = property.Value.GetString() ?? string.Empty;

                    // A link field: the name says so and the value looks like a path, not a
                    // sentence. Several fields may qualify; the first usable one wins.
                    if (path == null
                        && (key == "path" || key == "url" || key == "href" || key == "link"
                            || key == "alternateid" || key == "slug")
                        && LooksLikePath(value))
                    {
                        path = value;
                    }
                    else if (name == null && (key == "name" || key == "title" || key == "episodename"))
                    {
                        name = value;
                    }
                }

                // Numbers arrive as numbers or as strings depending on the site.
                int? number = ReadInt(property.Value);
                if (number == null) continue;

                if (season == null && key.Contains("season")) season = number;

                // "episodenumber" contains both words, so episode is checked for its own
                // marker rather than by elimination.
                if (episode == null
                    && (key == "episodenumber" || key == "episode" || key == "episodenum"
                        || key == "number" || key == "episodeindex"))
                {
                    episode = number;
                }
            }

            if (path == null) return null;
            if (episode == null && season == null) return null;

            return new JsonEpisode
            {
                Path = path,
                SeasonNumber = season,
                EpisodeNumber = episode,
                Name = string.IsNullOrWhiteSpace(name) ? null : name
            };
        }

        private static int? ReadInt(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i)) return i;

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), out int parsed))
            {
                return parsed;
            }

            return null;
        }

        /// <summary>
        /// A path, not prose. Rules out descriptions and ids while allowing both
        /// "/video/show/episode" and "show/episode".
        /// </summary>
        private static bool LooksLikePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.Contains(' ')) return false;
            if (value.Length > 400) return false;

            return value.Contains('/');
        }

        private static IEnumerable<JsonElement> Walk(JsonElement element)
        {
            yield return element;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var child in Walk(property.Value)) yield return child;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var child in Walk(item)) yield return child;
                }
            }
        }
    }
}
