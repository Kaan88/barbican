// The ids of the Files menus (spec/menus.md MENU-FILE, MENU-FILE-TRASH, MENU-FILE-FAV, MENU-FILE-RECENT, MENU-FILES-BG, and the Move to submenu). Owner files-ui; MenuIds belongs to menus.

namespace Barbican.Presentation.Files;

/// <summary>Menu ids of the Files app.</summary>
public static class FilesMenuIds
{
    public const string File = "MENU-FILE";
    public const string Trash = "MENU-FILE-TRASH";
    public const string Favourite = "MENU-FILE-FAV";
    public const string Recent = "MENU-FILE-RECENT";
    public const string Background = "MENU-FILES-BG";
    public const string MoveTo = "MENU-FILE>move";

    /// <summary>The header's sort menu (files-header.tsx: Name, Modified, Size, then Ascending, Descending).</summary>
    public const string Sort = "MENU-FILES-SORT";
}
