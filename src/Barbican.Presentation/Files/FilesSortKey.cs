// Mirrors: Bulwark components/files/file-views.tsx, files-settings.ts (Default sort, Sort direction); G-015. Contract from the M5 plan section 4.11; owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>How the file list is sorted.</summary>
public enum FilesSortKey
{
    Name,
    Modified,
    Size,
    Type,
}
