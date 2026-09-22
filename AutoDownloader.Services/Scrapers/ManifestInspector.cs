using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services.Scrapers
{
    /// <summary>
    /// Reads a DASH manifest to find out what is actually in it.
    ///
    /// This exists because a run produced a thirty-second car advert named as episode one,
    /// and reported success. The stream was not the wrong stream: it was the right one, with
    /// the adverts stitched into the same timeline as the programme - fifteen periods, four
    /// of them the episode and eleven of them thirty-second spots. yt-dlp's generic handler
    /// takes one period, and the first period it can use is an advert.
    ///
    /// yt-dlp reports the duration of these as NA, so the manifest has to be read directly.
    /// The point is to be able to say so, rather than hand over an advert and call it done.
    /// </summary>
    public static class ManifestInspector
    {
        /// <summary>
        /// A period at or under this is an advertising spot, not programme material. Ad
        /// breaks are sold in fixed lengths and come out as exact 15/30/60 second periods,
        /// while the programme between them runs to minutes.
        /// </summary>
        private const double AdvertSeconds = 61;

        public class ManifestFacts
        {
            public double TotalSeconds { get; set; }

            public List<double> PeriodSeconds { get; set; } = new List<double>();

            /// <summary>Periods long enough to be programme material.</summary>
            public IEnumerable<double> ContentPeriods =>
                PeriodSeconds.Where(p => p > AdvertSeconds);

            public IEnumerable<double> AdvertPeriods =>
                PeriodSeconds.Where(p => p > 0 && p <= AdvertSeconds);

            public double ContentSeconds => ContentPeriods.Sum();

            public double AdvertSecondsTotal => AdvertPeriods.Sum();

            /// <summary>
            /// True when adverts are stitched into the programme's own timeline, so no single
            /// period is the episode.
            /// </summary>
            public bool HasStitchedAdverts => AdvertPeriods.Any() && ContentPeriods.Any();

            /// <summary>
            /// True when the whole programme is one period, which is the only shape yt-dlp
            /// will download correctly on its own.
            /// </summary>
            public bool IsSinglePeriod => PeriodSeconds.Count(p => p > 0) <= 1;
        }

        /// <summary>
        /// Reads a manifest. Returns null when it cannot be fetched or is not DASH, which is
        /// not an error: plenty of streams are neither, and they download fine.
        /// </summary>
        public static async Task<ManifestFacts?> InspectAsync(
            string manifestUrl,
            string? referer,
            string userAgent,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl)) return null;
            if (manifestUrl.IndexOf(".mpd", StringComparison.OrdinalIgnoreCase) < 0) return null;

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.Add("User-Agent", userAgent);

                if (!string.IsNullOrWhiteSpace(referer))
                {
                    http.DefaultRequestHeaders.Add("Referer", referer);
                }

                string xml = await http.GetStringAsync(manifestUrl, cancellationToken).ConfigureAwait(false);

                return Parse(xml);
            }
            catch
            {
                // A manifest that cannot be read simply goes uninspected; the download is
                // still attempted, exactly as before this existed.
                return null;
            }
        }

        /// <summary>Public so the parsing can be tested without a network round trip.</summary>
        public static ManifestFacts Parse(string xml)
        {
            var facts = new ManifestFacts();

            var total = Regex.Match(xml, "mediaPresentationDuration=\"([^\"]+)\"");
            if (total.Success) facts.TotalSeconds = ParseIso8601(total.Groups[1].Value);

            foreach (Match period in Regex.Matches(xml, "<Period[^>]*>"))
            {
                var duration = Regex.Match(period.Value, "duration=\"([^\"]+)\"");
                facts.PeriodSeconds.Add(duration.Success ? ParseIso8601(duration.Groups[1].Value) : 0);
            }

            return facts;
        }

        /// <summary>
        /// An ISO 8601 duration as seconds, e.g. PT22M13.4S. Only the parts a media manifest
        /// actually uses are handled; anything unrecognised comes back as zero rather than
        /// throwing, since a bad duration should not fail a download.
        /// </summary>
        public static double ParseIso8601(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;

            var match = Regex.Match(
                value,
                "^P(?:([0-9.]+)D)?(?:T(?:([0-9.]+)H)?(?:([0-9.]+)M)?(?:([0-9.]+)S)?)?$",
                RegexOptions.IgnoreCase);

            if (!match.Success) return 0;

            double Part(int group) =>
                match.Groups[group].Success
                && double.TryParse(match.Groups[group].Value, NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out double parsed)
                    ? parsed
                    : 0;

            return Part(1) * 86400 + Part(2) * 3600 + Part(3) * 60 + Part(4);
        }
    }
}
