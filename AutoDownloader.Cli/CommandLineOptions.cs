using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoDownloader.Cli
{
    /// <summary>
    /// Parsed command-line arguments.
    ///
    /// Kept free of any I/O so the parsing rules can be unit tested. A scheduled job that
    /// misreads its own arguments fails silently at 3am, which is exactly the situation this
    /// tool exists to be trusted in.
    /// </summary>
    public class CommandLineOptions
    {
        /// <summary>The URL or search term to download. Required unless Help or Version.</summary>
        public string? Target { get; set; }

        /// <summary>Root output folder. Falls back to the saved setting when omitted.</summary>
        public string? OutputFolder { get; set; }

        /// <summary>Force a season number instead of parsing one from the URL.</summary>
        public int? Season { get; set; }

        /// <summary>yt-dlp format string. Falls back to the saved setting when omitted.</summary>
        public string? Quality { get; set; }

        /// <summary>Browser name or cookies.txt path. Falls back to the saved setting.</summary>
        public string? CookieSource { get; set; }

        /// <summary>Disable the per-series download archive for this run.</summary>
        public bool NoArchive { get; set; }

        /// <summary>Do not download ffmpeg if it is missing.</summary>
        public bool NoFfmpeg { get; set; }

        /// <summary>Emit a machine-readable JSON summary on stdout instead of prose.</summary>
        public bool Json { get; set; }

        /// <summary>Suppress progress and per-line logging; only report the outcome.</summary>
        public bool Quiet { get; set; }

        /// <summary>Replace files already on disk instead of keeping them.</summary>
        public bool Overwrite { get; set; }

        /// <summary>Add this URL to the watch list instead of downloading now.</summary>
        public string? WatchAdd { get; set; }

        /// <summary>Remove an entry by id or URL.</summary>
        public string? WatchRemove { get; set; }

        /// <summary>Print the watch list.</summary>
        public bool WatchList { get; set; }

        /// <summary>Check every enabled entry and fetch anything new.</summary>
        public bool WatchRun { get; set; }

        /// <summary>A name to look up, when the URL alone does not give a usable one.</summary>
        public string? ShowName { get; set; }

        /// <summary>Convert existing files in this folder instead of downloading.</summary>
        public string? ConvertFolder { get; set; }

        /// <summary>Replace the originals rather than writing converted copies alongside.</summary>
        public bool ReplaceOriginals { get; set; }

        /// <summary>Print usage and exit.</summary>
        public bool Help { get; set; }

        /// <summary>Print the version and exit.</summary>
        public bool Version { get; set; }

        /// <summary>Set when parsing failed; the message explains why.</summary>
        public string? Error { get; set; }

        public bool HasError => Error != null;

        /// <summary>
        /// Parses arguments. Never throws: a bad argument produces an Error rather than an
        /// exception, so the caller can print usage and exit with a sensible code.
        /// </summary>
        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();

            if (args.Length == 0)
            {
                options.Help = true;
                return options;
            }

            var positional = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                // Reads the value belonging to the current flag, or records an error.
                string? TakeValue(string flag)
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                    {
                        options.Error = $"{flag} requires a value.";
                        return null;
                    }
                    return args[++i];
                }

                switch (arg)
                {
                    case "-h":
                    case "--help":
                        options.Help = true;
                        break;

                    case "-v":
                    case "--version":
                        options.Version = true;
                        break;

                    case "-u":
                    case "--url":
                    case "--search":
                        options.Target = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "-o":
                    case "--out":
                    case "--output":
                        options.OutputFolder = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "-s":
                    case "--season":
                    {
                        string? raw = TakeValue(arg);
                        if (options.HasError) return options;

                        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int season)
                            || season < 0)
                        {
                            options.Error = $"--season expects a non-negative whole number, got '{raw}'.";
                            return options;
                        }
                        options.Season = season;
                        break;
                    }

                    case "-q":
                    case "--quality":
                        options.Quality = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "--cookies":
                        options.CookieSource = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "--watch-add":
                        options.WatchAdd = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "--watch-remove":
                        options.WatchRemove = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "--watch-list":
                        options.WatchList = true;
                        break;

                    case "--watch-run":
                        options.WatchRun = true;
                        break;

                    case "--show-name":
                        options.ShowName = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "--convert":
                        options.ConvertFolder = TakeValue(arg);
                        if (options.HasError) return options;
                        break;

                    case "--replace-originals":
                        options.ReplaceOriginals = true;
                        break;

                    case "--overwrite":
                        options.Overwrite = true;
                        break;

                    case "--no-archive":
                        options.NoArchive = true;
                        break;

                    case "--no-ffmpeg":
                        options.NoFfmpeg = true;
                        break;

                    case "--json":
                        options.Json = true;
                        break;

                    case "--quiet":
                        options.Quiet = true;
                        break;

                    default:
                        if (arg.StartsWith("-"))
                        {
                            options.Error = $"Unknown option '{arg}'. Try --help.";
                            return options;
                        }
                        positional.Add(arg);
                        break;
                }
            }

            // A bare argument is the target, so `autodl "https://..."` works as expected.
            if (options.Target == null && positional.Count > 0)
            {
                options.Target = positional[0];
            }

            if (positional.Count > 1)
            {
                options.Error = "Only one URL or search term can be given at a time.";
                return options;
            }

            // These are separate jobs that need no download target.
            bool isOtherJob = !string.IsNullOrWhiteSpace(options.ConvertFolder)
                              || !string.IsNullOrWhiteSpace(options.WatchAdd)
                              || !string.IsNullOrWhiteSpace(options.WatchRemove)
                              || options.WatchList
                              || options.WatchRun;

            if (!options.Help && !options.Version && !isOtherJob
                && string.IsNullOrWhiteSpace(options.Target))
            {
                options.Error = "No URL or search term given. Try --help.";
            }

            return options;
        }

        public const string Usage = @"AutoDownloader CLI - unattended downloads

USAGE
  autodl <url-or-search-term> [options]
  autodl --url <url> [options]

OPTIONS
  -u, --url, --search <value>  The series URL, or a show name to search for.
                               May also be given as the first bare argument.
  -o, --out <folder>           Root output folder. Defaults to the saved setting.
  -s, --season <n>             Force a season number instead of parsing the URL.
  -q, --quality <format>       yt-dlp format string, e.g. ""bestvideo+bestaudio/best"".
      --cookies <value>        Browser name (firefox, chrome, ...) or a cookies.txt
                               path. Defaults to the saved setting, normally none.
      --overwrite              Replace episodes already on disk. Without this they are
                               kept, since an unattended run should not destroy files.
      --no-archive             Do not skip episodes recorded as already downloaded.
      --no-ffmpeg              Do not download ffmpeg if it is missing.
      --json                   Print a JSON summary on stdout instead of prose.
      --quiet                  Only report the outcome, with no progress output.
WATCH LIST
      --watch-add <url>        Track a series and fetch new episodes on later runs.
                               Combine with --season, --out and --show-name.
      --watch-list             Show what is being watched.
      --watch-remove <id|url>  Stop watching an entry.
      --watch-run              Check every entry and download anything new. This is
                               the command to put on a schedule.
      --show-name <name>       The name to look up, for URLs that do not contain one
                               (a playlist, for instance).

OTHER
      --convert <folder>       Convert existing files to a widely playable MP4 instead
                               of downloading. Remuxes where the codecs already allow
                               it (seconds, lossless) and only re-encodes when needed.
      --replace-originals      With --convert, replace each original rather than
                               writing the converted file alongside it.
  -h, --help                   Show this help.
  -v, --version                Show the version.

NOTES
  Settings and API keys are shared with the desktop application, in
  %APPDATA%\AutoDownloader\settings.json. At least one of the TMDB or TVDB keys
  must be set there for naming to work.

  The show name is auto-confirmed rather than prompted for, so this never blocks
  waiting for input.

EXIT CODES
  0  completed
  1  failed
  2  bad arguments
  3  nothing was downloaded
  130 cancelled (Ctrl+C)

EXAMPLES
  autodl --watch-add ""https://example.com/series/some-show"" --season 2
  autodl --watch-run --json --quiet
  autodl ""https://example.com/series/some-show/season-2""
  autodl ""https://example.com/series/some-show"" --season 3 --out ""D:\Media""
  autodl ""The Mandalorian"" --json --quiet
";
    }
}
