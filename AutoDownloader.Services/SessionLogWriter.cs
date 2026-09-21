using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AutoDownloader.Services
{
    /// <summary>
    /// Writes everything that happens in a run to a file, automatically.
    ///
    /// The app already had an "export the developer log" menu item, but that requires
    /// knowing something went wrong, remembering to export, and choosing a location. A log
    /// that only exists if you thought to save it is not much use when diagnosing a run that
    /// has already finished. This writes continuously to a predictable path instead, so the
    /// record exists whether or not anyone asked for it.
    /// </summary>
    public static class SessionLogWriter
    {
        private static readonly object _lock = new object();
        private static StreamWriter? _writer;
        private static string? _path;

        /// <summary>How many past session logs to keep before pruning the oldest.</summary>
        /// <summary>
        /// Logs are kept by age rather than by count, so "what happened last month" is still
        /// answerable after a busy week. Twenty sessions was under a day of real use.
        /// </summary>
        private const int KeepDays = 30;

        /// <summary>
        /// A backstop, not the usual limit. Real sessions run a few KB to a few hundred KB,
        /// so thirty days of ordinary use lands nowhere near this; it exists only to stop a
        /// runaway log filling the disk.
        /// </summary>
        private const long MaxTotalBytes = 200L * 1024 * 1024;

        /// <summary>
        /// Never prune below this, however old. The last few runs are the ones somebody is
        /// actually about to ask about.
        /// </summary>
        private const int AlwaysKeep = 5;

        /// <summary>The file this session is being written to, or null before Start().</summary>
        public static string? CurrentPath
        {
            get { lock (_lock) { return _path; } }
        }

        /// <summary>The folder holding session logs.</summary>
        public static string LogFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoDownloader",
            "logs");

        /// <summary>
        /// Opens a log file for this run. Safe to call more than once; later calls are
        /// ignored so the whole run lands in one file.
        /// </summary>
        /// <param name="host">A short label for who is running, e.g. "ui" or "cli".</param>
        public static void Start(string host)
        {
            lock (_lock)
            {
                if (_writer != null) return;

                try
                {
                    Directory.CreateDirectory(LogFolder);

                    string name = $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{host}.log";
                    _path = Path.Combine(LogFolder, name);

                    // AutoFlush matters: a run that crashes or is killed must still leave a
                    // readable log behind, which is precisely when it is most wanted.
                    _writer = new StreamWriter(_path, append: true, Encoding.UTF8) { AutoFlush = true };

                    _writer.WriteLine($"=== AutoDownloader session log ===");
                    _writer.WriteLine($"started : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    _writer.WriteLine($"host    : {host}");
                    _writer.WriteLine($"version : {AutoDownloader.Core.AppInfo.Version}");
                    _writer.WriteLine(new string('-', 70));

                    PruneOldLogs();
                }
                catch
                {
                    // Logging must never be the reason a run fails.
                    _writer = null;
                    _path = null;
                }
            }
        }

        /// <summary>
        /// Appends one line. Timestamped, because the gap between two lines is often the
        /// clue - a twelve second pause is a headless browser, a thirty second one is a
        /// network timeout.
        /// </summary>
        public static void Append(string? line)
        {
            if (line == null) return;

            lock (_lock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
                }
                catch
                {
                    // Disk full, file locked - not worth taking the run down for.
                }
            }
        }

        /// <summary>
        /// Writes a closing marker. The file stays open for anything that follows.
        /// </summary>
        public static void NoteRunFinished(string summary)
        {
            lock (_lock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine(new string('-', 70));
                    _writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] RUN FINISHED: {summary}");
                    _writer.WriteLine(new string('-', 70));
                }
                catch { }
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                try
                {
                    _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss}] === session ended ===");
                    _writer?.Dispose();
                }
                catch { }

                _writer = null;
            }
        }

        /// <summary>A log file, reduced to what the retention rules care about.</summary>
        public readonly struct LogFileInfo
        {
            public LogFileInfo(string path, long length, DateTime lastWriteUtc)
            {
                Path = path;
                Length = length;
                LastWriteUtc = lastWriteUtc;
            }

            public string Path { get; }
            public long Length { get; }
            public DateTime LastWriteUtc { get; }
        }

        /// <summary>
        /// Decides which logs have expired. Separated from the deleting so the rules can be
        /// tested without a test that has to be trusted not to delete the wrong thing.
        ///
        /// Files are considered newest first, so whatever survives is always a contiguous
        /// run of the most recent sessions.
        /// </summary>
        public static List<string> SelectExpiredLogs(IEnumerable<LogFileInfo> logs, DateTime nowUtc)
        {
            var ordered = logs.OrderByDescending(f => f.LastWriteUtc).ToList();
            var cutoff = nowUtc.AddDays(-KeepDays);
            var expired = new List<string>();

            long runningTotal = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                runningTotal += ordered[i].Length;

                if (i < AlwaysKeep) continue;

                if (ordered[i].LastWriteUtc < cutoff || runningTotal > MaxTotalBytes)
                {
                    expired.Add(ordered[i].Path);
                }
            }

            return expired;
        }

        /// <summary>
        /// Keeps the folder from growing without limit.
        /// </summary>
        private static void PruneOldLogs()
        {
            try
            {
                var logs = new DirectoryInfo(LogFolder).GetFiles("session-*.log");

                var doomed = SelectExpiredLogs(
                    logs.Select(f => new LogFileInfo(f.FullName, f.Length, f.LastWriteTimeUtc)),
                    DateTime.UtcNow);

                foreach (var path in doomed)
                {
                    try { File.Delete(path); } catch { }
                }
            }
            catch { }
        }
    }
}
