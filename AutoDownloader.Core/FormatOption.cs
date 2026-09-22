using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoDownloader.Core
{
    /// <summary>
    /// One rung of a source's quality ladder, as the site actually publishes it.
    ///
    /// Presets can only guess. A broadcaster commonly offers the same 1920x1080 picture at
    /// several bitrates - Food Network publishes it at 4.3, 6.6 and 10.3 Mbps - and the top
    /// rung is not a better picture, only a bigger file. Showing the real rungs with their
    /// real sizes turns that from a guess into a choice.
    /// </summary>
    public class FormatOption
    {
        public string FormatId { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>Bitrate in kbps, as the source reports it.</summary>
        public double Bitrate { get; set; }

        /// <summary>Size in bytes, or zero when the source does not say.</summary>
        public long EstimatedBytes { get; set; }

        public string? VideoCodec { get; set; }

        public bool IsVideo => Height > 0;

        public string ResolutionLabel => Height > 0 ? $"{Width}x{Height}" : "audio only";

        /// <summary>
        /// A rough name for the resolution, because "1080p" means more to most people than
        /// "1920x1080" does.
        /// </summary>
        public string QualityLabel => Height switch
        {
            >= 2000 => "4K",
            >= 1000 => "1080p",
            >= 700 => "720p",
            >= 500 => "540p",
            >= 300 => "360p",
            > 0 => "low",
            _ => "audio",
        };

        public string SizeDisplay => EstimatedBytes > 0
            ? EstimatedBytes >= 1_073_741_824
                ? $"{EstimatedBytes / 1_073_741_824.0:0.00} GB"
                : $"{EstimatedBytes / 1_048_576.0:0} MB"
            : "size unknown";

        /// <summary>What the dialog shows for this rung.</summary>
        public string Display =>
            $"{QualityLabel}  ({ResolutionLabel})   {SizeDisplay}"
            + (Bitrate > 0 ? $"   {Bitrate / 1000:0.0} Mbps" : string.Empty);

        /// <summary>
        /// A yt-dlp selector for this rung that survives the next episode.
        ///
        /// Deliberately expressed as a height and a bitrate ceiling rather than the literal
        /// format id: ids like "hls-4312" are minted per episode by the packager, so reusing
        /// one across a season is a good way to fail on episode two. The ceiling sits just
        /// above the chosen rung so the same rung is picked where it exists, and the next one
        /// down where it does not.
        /// </summary>
        public string ToSelector()
        {
            if (!IsVideo) return "best";

            int cap = Bitrate > 0 ? (int)Math.Round(Bitrate * 1.05) : 0;

            string bitrate = cap > 0 ? $"[tbr<={cap}]" : string.Empty;

            return $"bestvideo[height<={Height}]{bitrate}+bestaudio"
                 + $"/bestvideo[height<={Height}]+bestaudio"
                 + $"/best[height<={Height}]"
                 + "/best";
        }

        /// <summary>
        /// The rungs worth showing: video only, one entry per distinct resolution and
        /// bitrate, largest first.
        ///
        /// Rungs whose resolution AND size are both indistinguishable are collapsed, since
        /// offering the same choice twice only makes the list harder to read.
        /// </summary>
        public static List<FormatOption> Worthwhile(IEnumerable<FormatOption> formats)
        {
            return formats
                .Where(f => f.IsVideo)
                .GroupBy(f => (f.Height, Rounded: (int)Math.Round(f.Bitrate / 50)))
                .Select(g => g.OrderByDescending(f => f.EstimatedBytes).First())
                .OrderByDescending(f => f.Height)
                .ThenByDescending(f => f.Bitrate)
                .ToList();
        }
    }
}
