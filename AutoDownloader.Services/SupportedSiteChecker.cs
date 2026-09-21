using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Answers "does yt-dlp know this site?" before a run rather than after it.
    ///
    /// yt-dlp ships around 1,750 site extractors. When a URL matches none of them it falls
    /// back to a generic extractor that looks for a plain video element, and when that finds
    /// nothing the run ends with "Unsupported URL". Discovering this at the end - after
    /// metadata lookup, page indexing and a headless browser render - wastes a minute to
    /// learn something knowable in advance.
    ///
    /// The extractor list is read once from yt-dlp itself and cached, so it stays accurate as
    /// yt-dlp updates rather than drifting against a list hardcoded here.
    /// </summary>
    public class SupportedSiteChecker
    {
        private readonly string _ytDlpPath;
        private static readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private static HashSet<string>? _extractors;

        public event Action<string>? OnDiagnostic;

        /// <summary>
        /// Extractor names that match anything and so prove nothing about support.
        /// </summary>
        private static readonly HashSet<string> NonSpecific =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "generic", "genericquotedhtml", "html5" };

        /// <summary>
        /// Line separator, named rather than written as an escape so that editing this file
        /// with tools that mangle backslashes cannot silently break it.
        /// </summary>
        private static readonly char NewLine = (char)10;

        public SupportedSiteChecker(string ytDlpPath)
        {
            _ytDlpPath = ytDlpPath;
        }

        /// <summary>
        /// True when a dedicated extractor appears to exist for the URL's host.
        ///
        /// This is a strong hint rather than a guarantee: extractor names and domain names do
        /// not correspond perfectly. It is used to explain a failure, never to refuse to try.
        /// </summary>
        public async Task<bool> IsLikelySupportedAsync(string url, CancellationToken cancellationToken = default)
        {
            var extractors = await GetExtractorsAsync(cancellationToken).ConfigureAwait(false);
            if (extractors.Count == 0) return true; // Unknown: do not discourage an attempt.

            foreach (var label in HostLabels(url))
            {
                if (extractors.Contains(label)) return true;

                // "archive.org" is listed with its dot; "JWPlatform" without.
                if (extractors.Any(e => e.Contains(label, StringComparison.OrdinalIgnoreCase)
                                        && label.Length >= 4))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The distinctive parts of a host, most specific first: "www.tubitv.com" gives
        /// "tubitv", then "tubitvcom".
        /// </summary>
        public static IEnumerable<string> HostLabels(string url)
        {
            string host;

            try { host = new Uri(url).Host.ToLowerInvariant(); }
            catch { yield break; }

            if (host.StartsWith("www.")) host = host.Substring(4);

            var parts = host.Split('.').Where(p => p.Length > 0).ToList();

            // Drop the public suffix, so "toonplay.in" reduces to "toonplay".
            if (parts.Count >= 2)
            {
                string main = parts[parts.Count - 2];
                if (main.Length >= 3) yield return main;
            }

            yield return host.Replace(".", string.Empty);
            yield return host;
        }

        /// <summary>
        /// Reads the extractor list from yt-dlp, once per process.
        /// </summary>
        private async Task<HashSet<string>> GetExtractorsAsync(CancellationToken cancellationToken)
        {
            if (_extractors != null) return _extractors;

            await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_extractors != null) return _extractors;

                var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath))
                {
                    _extractors = loaded;
                    return loaded;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("--list-extractors");

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                foreach (var line in output.Split('\n'))
                {
                    string name = line.Trim();
                    if (name.Length == 0) continue;

                    // Names use colons for sub-extractors ("dailymotion:playlist").
                    name = name.Split(':')[0].Trim();

                    string normalised = new string(name.Where(char.IsLetterOrDigit).ToArray());
                    if (normalised.Length == 0) continue;
                    if (NonSpecific.Contains(normalised)) continue;

                    loaded.Add(normalised);
                    loaded.Add(name);
                }

                OnDiagnostic?.Invoke($"SupportedSiteChecker: {loaded.Count} extractor name(s) known to yt-dlp.");

                _extractors = loaded;
                return loaded;
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"SupportedSiteChecker: could not read the extractor list: {ex.Message}");
                _extractors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return _extractors;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>
        /// The full list of site names yt-dlp can extract from, sorted for display.
        ///
        /// Read from yt-dlp itself rather than maintained here, so it cannot drift: the list
        /// is whatever the currently installed yt-dlp actually supports.
        /// </summary>
        public async Task<List<string>> GetAllSiteNamesAsync(CancellationToken cancellationToken = default)
        {
            var names = new List<string>();

            if (string.IsNullOrWhiteSpace(_ytDlpPath) || !File.Exists(_ytDlpPath)) return names;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                startInfo.ArgumentList.Add("--list-extractors");

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                names = output
                    .Split(NewLine)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"Could not list supported sites: {ex.Message}");
            }

            return names;
        }

        /// <summary>
        /// Recognises yt-dlp reporting that it has no extractor for a URL.
        /// </summary>
        public static bool IsUnsupportedUrlMessage(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            return line.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase);
        }
    }
}
