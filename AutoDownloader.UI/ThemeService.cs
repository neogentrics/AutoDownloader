using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AutoDownloader.UI
{
    /// <summary>
    /// Switches the application's look while it is running.
    ///
    /// A theme is one palette dictionary. The control styles are shared and reach the palette
    /// by DynamicResource, so swapping the palette is enough - no window needs rebuilding and
    /// nothing has to be reopened.
    ///
    /// The palette is found by its source path rather than by index, because the position of
    /// a merged dictionary is exactly the sort of thing that changes when someone adds another
    /// resource file and does not notice they have broken theming.
    /// </summary>
    public static class ThemeService
    {
        public sealed class ThemeOption
        {
            public string Name { get; init; } = string.Empty;

            public string Description { get; init; } = string.Empty;

            /// <summary>The palette file, relative to the UI assembly.</summary>
            public string Source { get; init; } = string.Empty;
        }

        /// <summary>Marks which merged dictionary is the palette.</summary>
        private const string PaletteMarker = "Theme/Palette.";

        public static IReadOnlyList<ThemeOption> Available { get; } = new[]
        {
            new ThemeOption
            {
                Name = "Dark",
                Description = "The original look.",
                Source = "Theme/Palette.Dark.xaml",
            },
            new ThemeOption
            {
                Name = "Light",
                Description = "For a bright room.",
                Source = "Theme/Palette.Light.xaml",
            },
            new ThemeOption
            {
                Name = "High Contrast",
                Description = "Maximum legibility.",
                Source = "Theme/Palette.HighContrast.xaml",
            },
        };

        /// <summary>The theme currently applied.</summary>
        public static string Current { get; private set; } = "Dark";

        /// <summary>Raised after a theme is applied, so menus can tick the right entry.</summary>
        public static event Action<string>? ThemeChanged;

        /// <summary>Raised when a theme could not be applied, with the reason.</summary>
        public static event Action<string>? OnDiagnostic;

        /// <summary>
        /// The assembly-qualified form of a theme's location.
        ///
        /// A bare relative Uri only resolves when the resolving code happens to be running in
        /// the application that owns the resource, so it silently failed the first time this
        /// was tested outside one - and because the failure was swallowed, all three themes
        /// appeared to apply while none of them did.
        /// </summary>
        private static Uri PackUriFor(string source) =>
            new Uri("pack://application:,,,/AutoDownloader.UI;component/" + source, UriKind.Absolute);

        public static ThemeOption Resolve(string? name) =>
            Available.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? Available[0];

        /// <summary>
        /// Applies a theme by name. An unknown name falls back to the first rather than
        /// throwing, since this is driven by a settings file a person can edit.
        /// </summary>
        public static bool Apply(string? name)
        {
            var theme = Resolve(name);

            var app = Application.Current;
            if (app == null) return false;

            try
            {
                var merged = app.Resources.MergedDictionaries;

                var replacement = new ResourceDictionary { Source = PackUriFor(theme.Source) };

                var existing = merged.FirstOrDefault(d =>
                    d.Source != null &&
                    d.Source.OriginalString.IndexOf(PaletteMarker, StringComparison.OrdinalIgnoreCase) >= 0);

                if (existing != null)
                {
                    // Replaced in place so it keeps its position ahead of the control styles.
                    merged[merged.IndexOf(existing)] = replacement;
                }
                else
                {
                    // No palette merged yet: it has to go first, because the styles that
                    // follow resolve their colours out of it.
                    merged.Insert(0, replacement);
                }

                Current = theme.Name;
                ThemeChanged?.Invoke(Current);
                return true;
            }
            catch (Exception ex)
            {
                // A theme that will not load is not worth taking the application down for,
                // but it must not fail silently either: doing so once made three themes look
                // like they were applying when the palette never changed at all.
                OnDiagnostic?.Invoke($"Could not apply the {theme.Name} theme: {ex.Message}");
                return false;
            }
        }
    }
}
