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
        private const int KeepSessions = 20;

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

        /// <summary>
        /// Keeps the folder from growing without limit.
        /// </summary>
        private static void PruneOldLogs()
        {
            try
            {
                var logs = new DirectoryInfo(LogFolder)
                    .GetFiles("session-*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(KeepSessions)
                    .ToList();

                foreach (var old in logs)
                {
                    try { old.Delete(); } catch { }
                }
            }
            catch { }
        }
    }
}
