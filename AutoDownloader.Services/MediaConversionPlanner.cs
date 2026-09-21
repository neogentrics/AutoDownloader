using System;
using System.Linq;

namespace AutoDownloader.Services
{
    /// <summary>What has to happen to one stream.</summary>
    public enum StreamAction
    {
        /// <summary>Already fine: copy the stream through untouched, losslessly.</summary>
        Copy,

        /// <summary>Not usable in the target container: re-encode it.</summary>
        Encode
    }

    /// <summary>
    /// What converting one file would involve.
    /// </summary>
    public class ConversionPlan
    {
        /// <summary>True when the file is already in the wanted shape.</summary>
        public bool AlreadyCorrect { get; set; }

        public StreamAction Video { get; set; } = StreamAction.Copy;
        public StreamAction Audio { get; set; } = StreamAction.Copy;

        /// <summary>Target container extension, including the dot.</summary>
        public string TargetExtension { get; set; } = ".mp4";

        /// <summary>Plain-language explanation, for the log.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// True when nothing is re-encoded - a remux, which is lossless and takes seconds
        /// rather than minutes because it only rewrites the container.
        /// </summary>
        public bool IsLossless => Video == StreamAction.Copy && Audio == StreamAction.Copy;

        /// <summary>Roughly how expensive this is, for ordering and for warning the user.</summary>
        public string Cost =>
            AlreadyCorrect ? "nothing to do"
            : IsLossless ? "remux (fast, lossless)"
            : Video == StreamAction.Encode && Audio == StreamAction.Encode ? "full re-encode (slow, lossy)"
            : Video == StreamAction.Encode ? "video re-encode (slow, lossy)"
            : "audio re-encode (quick, slightly lossy)";
    }

    /// <summary>
    /// Decides how to get a file into a playable container without doing more work than
    /// necessary.
    ///
    /// The distinction that matters: changing container is nearly free and lossless, whereas
    /// re-encoding is slow and degrades quality. A file whose codecs are already valid for
    /// MP4 only needs its container rewritten - seconds, byte-identical video. Only genuinely
    /// incompatible codecs justify a re-encode, and even then often just one of the two
    /// streams.
    ///
    /// Worth saying plainly: re-encoding is always the worse option. If the source can be
    /// fetched again in a compatible codec, doing that beats converting what you have, both
    /// for quality and for time.
    /// </summary>
    public static class MediaConversionPlanner
    {
        /// <summary>
        /// Video codecs that play essentially everywhere in an MP4: TVs, phones, Plex clients
        /// without transcoding. AV1 and VP9 are technically legal in MP4 but are not
        /// hardware-decoded by most devices, so they are treated as needing conversion.
        /// </summary>
        private static readonly string[] CompatibleVideo = { "h264", "avc1", "avc" };

        /// <summary>Audio codecs an MP4 container and common players both accept.</summary>
        private static readonly string[] CompatibleAudio = { "aac", "mp4a", "ac3", "mp3" };

        /// <summary>
        /// Works out what a file needs.
        /// </summary>
        /// <param name="currentExtension">The file's extension, e.g. ".webm".</param>
        /// <param name="videoCodec">Video codec as ffprobe reports it, e.g. "av1".</param>
        /// <param name="audioCodec">Audio codec as ffprobe reports it, e.g. "opus".</param>
        public static ConversionPlan Plan(string? currentExtension, string? videoCodec, string? audioCodec)
        {
            var plan = new ConversionPlan { TargetExtension = ".mp4" };

            string ext = (currentExtension ?? string.Empty).Trim().ToLowerInvariant();
            if (ext.Length > 0 && !ext.StartsWith(".")) ext = "." + ext;

            string video = Normalise(videoCodec);
            string audio = Normalise(audioCodec);

            bool videoOk = CompatibleVideo.Any(c => video.StartsWith(c, StringComparison.Ordinal));
            bool audioOk = CompatibleAudio.Any(c => audio.StartsWith(c, StringComparison.Ordinal));

            plan.Video = videoOk ? StreamAction.Copy : StreamAction.Encode;
            plan.Audio = audioOk ? StreamAction.Copy : StreamAction.Encode;

            if (ext == ".mp4" && videoOk && audioOk)
            {
                plan.AlreadyCorrect = true;
                plan.Reason = "already MP4 with compatible codecs";
                return plan;
            }

            if (videoOk && audioOk)
            {
                plan.Reason = $"codecs are fine ({video}/{audio}); only the {ext} container needs rewriting";
                return plan;
            }

            if (videoOk)
            {
                plan.Reason = $"video is fine ({video}) and will be copied; {audio} audio needs re-encoding to AAC";
                return plan;
            }

            if (audioOk)
            {
                plan.Reason = $"{video} video needs re-encoding to H.264; audio ({audio}) will be copied";
                return plan;
            }

            plan.Reason = $"{video} video and {audio} audio both need re-encoding (H.264/AAC)";
            return plan;
        }

        private static string Normalise(string? codec)
        {
            if (string.IsNullOrWhiteSpace(codec)) return "unknown";

            codec = codec.Trim().ToLowerInvariant();

            // ffprobe reports "av1"; yt-dlp and MP4 boxes say "av01". Likewise h264/avc1.
            if (codec.StartsWith("av01")) return "av1";
            if (codec.StartsWith("avc1")) return "h264";
            if (codec.StartsWith("mp4a")) return "aac";
            if (codec.StartsWith("hev") || codec.StartsWith("hvc")) return "hevc";

            return codec;
        }
    }
}
