using AutoDownloader.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Stores the list of series to keep an eye on.
    ///
    /// Kept beside settings.json so the desktop app and the CLI share one list: a series added
    /// from the window is picked up by a scheduled `autodl --watch-run`, which is the whole
    /// point of having both.
    ///
    /// Saving is atomic - written to a temporary file and moved into place - because a
    /// scheduled task interrupted mid-write should not be able to leave the list truncated.
    /// </summary>
    public class WatchListService
    {
        private readonly string _path;
        private List<WatchListEntry> _entries = new List<WatchListEntry>();

        public event Action<string>? OnDiagnostic;

        public IReadOnlyList<WatchListEntry> Entries => _entries;

        public string FilePath => _path;

        public WatchListService(string? path = null)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _path = path!;
            }
            else
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AutoDownloader");

                Directory.CreateDirectory(folder);
                _path = Path.Combine(folder, "watchlist.json");
            }

            Load();
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _entries = new List<WatchListEntry>();
                    return;
                }

                string json = File.ReadAllText(_path);

                _entries = JsonSerializer.Deserialize<List<WatchListEntry>>(json)
                           ?? new List<WatchListEntry>();
            }
            catch (Exception ex)
            {
                // A corrupt list must not take the app down, but it must not be silently
                // replaced either - the file is kept so it can be recovered by hand.
                OnDiagnostic?.Invoke($"Could not read the watch list ({ex.Message}). Starting empty; "
                                   + $"the existing file is left at {_path}");
                _entries = new List<WatchListEntry>();
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, json);

                // Move over the original only once the new content is safely on disk.
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                OnDiagnostic?.Invoke($"Could not save the watch list: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a series, or returns the existing entry when the URL is already watched.
        /// </summary>
        public WatchListEntry Add(string url, string? showName = null, int? season = null, string? outputFolder = null)
        {
            var existing = _entries.FirstOrDefault(e =>
                string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase)
                && e.Season == season);

            if (existing != null) return existing;

            var entry = new WatchListEntry
            {
                Url = url.Trim(),
                ShowName = string.IsNullOrWhiteSpace(showName) ? null : showName.Trim(),
                Season = season,
                OutputFolder = string.IsNullOrWhiteSpace(outputFolder) ? null : outputFolder.Trim()
            };

            _entries.Add(entry);
            Save();

            return entry;
        }

        /// <summary>
        /// Removes by id, or by URL when the id is not recognised.
        /// </summary>
        public bool Remove(string idOrUrl)
        {
            var match = Find(idOrUrl);
            if (match == null) return false;

            _entries.Remove(match);
            Save();
            return true;
        }

        public WatchListEntry? Find(string idOrUrl)
        {
            if (string.IsNullOrWhiteSpace(idOrUrl)) return null;

            return _entries.FirstOrDefault(e => string.Equals(e.Id, idOrUrl, StringComparison.OrdinalIgnoreCase))
                ?? _entries.FirstOrDefault(e => string.Equals(e.Url, idOrUrl, StringComparison.OrdinalIgnoreCase));
        }

        public bool SetEnabled(string idOrUrl, bool enabled)
        {
            var match = Find(idOrUrl);
            if (match == null) return false;

            match.Enabled = enabled;
            Save();
            return true;
        }

        /// <summary>
        /// Records the outcome of a check.
        /// </summary>
        public void RecordCheck(WatchListEntry entry, bool succeeded, int episodesPresent, string? result, string? resolvedTitle)
        {
            entry.LastCheckedUtc = DateTime.UtcNow;
            entry.LastEpisodeCount = episodesPresent;
            entry.LastResult = result;

            if (!string.IsNullOrWhiteSpace(resolvedTitle)) entry.ResolvedTitle = resolvedTitle;

            entry.ConsecutiveFailures = succeeded ? 0 : entry.ConsecutiveFailures + 1;

            Save();
        }

        /// <summary>
        /// The entries a run should check, in a stable order.
        /// </summary>
        public IReadOnlyList<WatchListEntry> GetDueEntries() =>
            _entries.Where(e => e.Enabled)
                    .OrderBy(e => e.LastCheckedUtc ?? DateTime.MinValue)
                    .ToList();
    }
}
