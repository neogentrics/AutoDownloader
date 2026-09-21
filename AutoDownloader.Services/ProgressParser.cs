using AutoDownloader.Core;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Turns a line of downloader output into a progress reading, or null if it is not one.
    ///
    /// Both formats are handled because which one appears depends on who is doing the
    /// transfer, and that varies per site. Measured against a 6 MB file over a local server:
    /// with aria2c as the external downloader, yt-dlp emitted 1 progress line (at 100%, after
    /// the fact) while aria2c emitted 12. When yt-dlp downloads directly it is the other way
    /// round. Reading only one source gives a bar that sits at zero and then jumps to done.
    /// </summary>
    public static class ProgressParser
    {
        /// <summary>
        /// The marker prefix used in the --progress-template passed to yt-dlp. Chosen to be
        /// something no site or filename would produce.
        /// </summary>
        public const string YtDlpMarker = "@PROG@";

        /// <summary>
        /// The --progress-template argument value matching <see cref="YtDlpMarker"/>.
        /// </summary>
        public const string YtDlpProgressTemplate =
            "download:" + YtDlpMarker +
            "%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|" +
            "%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s";

        /// <summary>
        /// aria2c status line, e.g.
        /// <c>[#ab02c4 1.0MiB/6.0MiB(16%) CN:4 DL:518KiB ETA:9s]</c>
        /// ETA is absent once the rate is unknown or the transfer is finishing, so it is
        /// optional. Trailing text after the bracket (aria2c appends a summary banner to some
        /// lines) is ignored.
        /// </summary>
        private static readonly Regex Aria2cPattern = new Regex(
            @"\[#\w+\s+(?<done>[\d.]+\w*)/(?<total>[\d.]+\w*)\((?<pct>\d+)%\)"
            + @"(?:\s+CN:\d+)?(?:\s+DL:(?<dl>[\d.]+\w*))?(?:\s+ETA:(?<eta>[^\]\s]+))?",
            RegexOptions.Compiled);

        /// <summary>
        /// Parses one line of output. Returns null when the line is not a progress update,
        /// which is the common case - callers should simply ignore nulls.
        /// </summary>
        public static DownloadProgress? Parse(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            return ParseYtDlp(line) ?? ParseAria2c(line);
        }

        private static DownloadProgress? ParseYtDlp(string line)
        {
            int marker = line.IndexOf(YtDlpMarker, StringComparison.Ordinal);
            if (marker < 0) return null;

            string payload = line.Substring(marker + YtDlpMarker.Length);
            string[] parts = payload.Split('|');
            if (parts.Length < 3) return null;

            return new DownloadProgress
            {
                Source = ProgressSource.YtDlp,
                Percent = ParsePercent(parts[0]),
                Speed = Clean(parts[1]),
                Eta = Clean(parts[2]),
                Downloaded = parts.Length > 3 ? Clean(parts[3]) : null,
                Total = parts.Length > 4 ? Clean(parts[4]) : null,
            };
        }

        private static DownloadProgress? ParseAria2c(string line)
        {
            var match = Aria2cPattern.Match(line);
            if (!match.Success) return null;

            double? percent = null;
            if (double.TryParse(match.Groups["pct"].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double pct))
            {
                percent = pct;
            }

            string? speed = match.Groups["dl"].Success ? match.Groups["dl"].Value + "/s" : null;

            return new DownloadProgress
            {
                Source = ProgressSource.Aria2c,
                Percent = percent,
                Speed = speed,
                Eta = match.Groups["eta"].Success ? match.Groups["eta"].Value : null,
                Downloaded = match.Groups["done"].Value,
                Total = match.Groups["total"].Value,
            };
        }

        /// <summary>
        /// Reads "  12.3%" into 12.3. yt-dlp pads these and may report "NA".
        /// </summary>
        private static double? ParsePercent(string raw)
        {
            string text = raw.Trim().TrimEnd('%').Trim();
            if (text.Length == 0) return null;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : null;
        }

        /// <summary>
        /// Normalises a field, mapping yt-dlp's "NA"/"Unknown" placeholders onto null so the
        /// UI can decide how to present a missing value rather than displaying the word.
        /// </summary>
        private static string? Clean(string raw)
        {
            string text = raw.Trim();

            if (text.Length == 0) return null;
            if (text.Equals("NA", StringComparison.OrdinalIgnoreCase)) return null;
            if (text.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)) return null;

            return text;
        }
    }
}
