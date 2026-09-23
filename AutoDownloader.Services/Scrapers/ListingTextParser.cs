using System;
using System.Text.RegularExpressions;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// Reads what a listing tile says about an episode.
    ///
    /// A tile renders its whole caption as one run of text, and scraping it whole gives a
    /// string like
    ///
    ///     S12 E1Cooking for Jeffrey: Birthday21mTV-G10/16/2016Ina throws an Italian-themed
    ///     birthday dinner with a surprise for Jeffrey.Ina throws an Italian-themed birthday
    ///     dinner with a surprise for Jeffrey.
    ///
    /// which is the season, the episode number, the title, the runtime, the rating, the air
    /// date and the description - the description twice, because the tile holds a visible copy
    /// and a screen-reader copy. Used whole it matches no database title and reads as noise.
    /// Taken apart it is better than the databases: it is what the site itself will serve, it
    /// covers seasons the databases have never heard of, and it needs no API key.
    /// </summary>
    public static class ListingTextParser
    {
        /// <summary>What a tile said, as far as it could be read.</summary>
        public class ListingDetails
        {
            public int? SeasonNumber { get; set; }

            public int? EpisodeNumber { get; set; }

            public string? Title { get; set; }

            public string? Description { get; set; }

            public DateTime? AirDate { get; set; }

            public int? RuntimeMinutes { get; set; }

            public string? Rating { get; set; }

            /// <summary>True when there is anything here worth having.</summary>
            public bool HasAnything =>
                SeasonNumber.HasValue || EpisodeNumber.HasValue
                || !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Description);
        }

        /// <summary>"S12 E1" at the front, which is the tile's own numbering.</summary>
        private static readonly Regex NumberPattern = new Regex(
            @"^\s*S(?<season>\d{1,3})\s*E(?<episode>\d{1,4})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// The run of fixed fields that follows the title: "21mTV-G10/16/2016".
        ///
        /// The runtime must not be preceded by a digit. Without that, a title ending in a
        /// number runs into the runtime - "Dinner Party 101" followed by "21m" reads as
        /// "Dinner Party 10" followed by "121m" - and the title comes out a character short
        /// with no sign anything went wrong. Refusing to guess leaves the title unread, and
        /// the caller still has the slug to fall back on.
        /// </summary>
        private static readonly Regex FieldsPattern = new Regex(
            @"(?<![0-9])(?<runtime>\d{1,3})m(?<rating>TV-Y7|TV-Y|TV-PG|TV-14|TV-MA|TV-G|NR|Not Rated|PG-13|PG|G|R)(?<date>\d{1,2}/\d{1,2}/\d{4})",
            RegexOptions.Compiled);

        /// <summary>
        /// The shape of the fields run, used only to recognise that a remainder still contains
        /// one and is therefore not a title.
        /// </summary>
        private static readonly Regex LooksLikeFields = new Regex(
            @"\d{1,3}m[A-Z]|\d{1,2}/\d{1,2}/\d{4}",
            RegexOptions.Compiled);

        /// <summary>
        /// Takes a tile's caption apart. Returns null when it is not a caption at all - a URL,
        /// a bare slug, "Watch Now".
        /// </summary>
        public static ListingDetails? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string value = text.Trim();

            var details = new ListingDetails();

            var numbers = NumberPattern.Match(value);
            int titleStart = 0;

            if (numbers.Success)
            {
                details.SeasonNumber = int.Parse(numbers.Groups["season"].Value);
                details.EpisodeNumber = int.Parse(numbers.Groups["episode"].Value);
                titleStart = numbers.Length;
            }

            var fields = FieldsPattern.Match(value, titleStart);

            if (fields.Success)
            {
                if (int.TryParse(fields.Groups["runtime"].Value, out int minutes))
                {
                    details.RuntimeMinutes = minutes;
                }

                details.Rating = fields.Groups["rating"].Value;

                if (DateTime.TryParse(fields.Groups["date"].Value, out var aired))
                {
                    details.AirDate = aired;
                }

                string title = value.Substring(titleStart, fields.Index - titleStart).Trim();
                if (title.Length > 0) details.Title = title;

                string description = value.Substring(fields.Index + fields.Length).Trim();
                description = Deduplicate(description);
                if (description.Length > 0) details.Description = description;
            }
            else if (numbers.Success)
            {
                // Numbered, but the fields run did not read. The remainder may be a plain
                // title - or it may be the whole caption with a title this refuses to guess
                // at, in which case it is not a title and taking it would name the file after
                // a runtime and an air date.
                string rest = value.Substring(titleStart).Trim();

                if (rest.Length > 0 && rest.Length <= 120 && !LooksLikeFields.IsMatch(rest))
                {
                    details.Title = rest;
                }
            }

            return details.HasAnything ? details : null;
        }

        /// <summary>
        /// Collapses a string that is the same text twice, which is how a tile carrying both a
        /// visible and a screen-reader copy of the description arrives.
        /// </summary>
        public static string Deduplicate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length % 2 != 0) return value;

            int half = value.Length / 2;

            return string.CompareOrdinal(value, 0, value, half, half) == 0
                ? value.Substring(0, half)
                : value;
        }
    }
}
