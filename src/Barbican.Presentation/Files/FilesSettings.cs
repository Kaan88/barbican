// Mirrors: Bulwark lib/files-settings.ts (settings key filesSettings: defaultViewMode, showIcons, coloredIcons, defaultSortKey, defaultSortDir, showHiddenFiles, showThumbnails); D-M5-11. Owner files-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Files;

/// <summary>The Files preferences, known keys with valid values, the rest default.</summary>
public sealed record FilesSettings(FilesViewMode ViewMode, bool ShowIcons, bool ColoredIcons, FilesSortKey SortKey, bool SortDescending, bool ShowHidden, bool ShowThumbnails)
{
    public static FilesSettings Default { get; } = new(FilesViewMode.List, true, true, FilesSortKey.Name, false, false, true);

    public static FilesSettings Read(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        if (prefs.Values["filesSettings"] is not JsonObject o)
        {
            return Default;
        }

        return new FilesSettings(
            string.Equals(Text(o, "defaultViewMode"), "grid", StringComparison.Ordinal) ? FilesViewMode.Grid : FilesViewMode.List,
            Flag(o, "showIcons", true),
            Flag(o, "coloredIcons", true),
            Text(o, "defaultSortKey") switch { "size" => FilesSortKey.Size, "modified" => FilesSortKey.Modified, _ => FilesSortKey.Name },
            string.Equals(Text(o, "defaultSortDir"), "desc", StringComparison.Ordinal),
            Flag(o, "showHiddenFiles", false),
            Flag(o, "showThumbnails", true));
    }

    private static string? Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool Flag(JsonObject o, string key, bool fallback) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
}
