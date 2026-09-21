using System;
using System.Reflection;

namespace AutoDownloader.Core
{
    /// <summary>
    /// The application version, read from the assembly rather than hardcoded.
    ///
    /// The title bar, the welcome text and the CLI each used to carry their own literal and
    /// had drifted to three different answers. The version now comes from
    /// Directory.Build.props via the build, so there is one place to change it.
    /// </summary>
    public static class AppInfo
    {
        private static readonly Lazy<string> _version = new Lazy<string>(Resolve);

        /// <summary>The display version, e.g. "v1.11.0-beta".</summary>
        public static string Version => _version.Value;

        private static string Resolve()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

                string? informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                if (!string.IsNullOrWhiteSpace(informational))
                {
                    // Strip the source-control metadata the SDK appends ("1.11.0-beta+abc123").
                    int plus = informational.IndexOf('+');
                    if (plus > 0) informational = informational.Substring(0, plus);
                    return "v" + informational;
                }

                var version = assembly.GetName().Version;
                return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v0.0.0";
            }
            catch
            {
                return "v0.0.0";
            }
        }
    }
}
