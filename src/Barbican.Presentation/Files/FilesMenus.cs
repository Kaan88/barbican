// Mirrors: spec/menus.md MENU-FILE, MENU-FILE-TRASH, MENU-FILE-FAV, MENU-FILE-RECENT, MENU-FILES-BG item by item (Rust ctxmenu.rs); Bulwark components/files/file-context-menu.tsx. The subject is MenuContext.File (typed, D-M5-08).
// Deviations (D-M5-D04): "Share" is left out until files-data resolves principals (the rows would only fail); "Move to" is the submenu MENU-FILE>move (the move dialog is the keyboard and drag route). Owner files-ui.

using Barbican.Core.Files;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Files;

/// <summary>The Files menu providers.</summary>
public static class FilesMenus
{
    /// <summary>How many providers <see cref="Create"/> returns.</summary>
    public const int Count = 7;

    public static IReadOnlyList<IMenuProvider> Create(FilesActions a) =>
    [
        new DelegateMenuProvider(FilesMenuIds.File, c => File(c, a)),
        new DelegateMenuProvider(FilesMenuIds.Trash, c => Trash(c, a)),
        new DelegateMenuProvider(FilesMenuIds.Favourite, c => Favourite(c, a)),
        new DelegateMenuProvider(FilesMenuIds.Recent, c => Recent(c, a)),
        new DelegateMenuProvider(FilesMenuIds.Background, c => Background(c, a)),
        new DelegateMenuProvider(FilesMenuIds.MoveTo, c => MoveTo(c, a)),
        new DelegateMenuProvider(FilesMenuIds.Sort, _ => Sort(a)),
    ];

    // Bulwark files-header.tsx:431-452: radio rows Name, Modified, Size; a separator; Ascending, Descending. Barbican keeps Type (a sort key since M5) after Size.
    private static List<MenuItemSpec> Sort(FilesActions a) =>
    [
        Item(MenuGlyphs.Select, "Name", FilesCommandIds.Sort, argument: "name", check: a.View.SortKey == FilesSortKey.Name),
        Item(MenuGlyphs.Select, "Modified", FilesCommandIds.Sort, argument: "modified", check: a.View.SortKey == FilesSortKey.Modified),
        Item(MenuGlyphs.Select, "Size", FilesCommandIds.Sort, argument: "size", check: a.View.SortKey == FilesSortKey.Size),
        Item(MenuGlyphs.Select, "Type", FilesCommandIds.Sort, argument: "type", check: a.View.SortKey == FilesSortKey.Type),
        Sep(),
        Item(MenuGlyphs.Select, "Ascending", FilesCommandIds.Sort, argument: "asc", check: !a.View.SortDescending),
        Item(MenuGlyphs.Select, "Descending", FilesCommandIds.Sort, argument: "desc", check: a.View.SortDescending),
    ];

    /// <summary>The argument of a command about several keys: the keys, one per line.</summary>
    public static string Keys(IEnumerable<string> keys) => string.Join('\n', keys);

    public static IReadOnlyList<string> KeysOf(string? argument) => string.IsNullOrEmpty(argument) ? [] : argument.Split('\t')[0].Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static List<MenuItemSpec> File(MenuContext c, FilesActions a)
    {
        if (c.File is not { Keys.Count: > 0 } t)
        {
            return [];
        }

        var keys = Keys(t.Keys);
        var one = t.Keys.Count == 1 ? a.Find(t.Keys[0]) : null;
        var entries = t.Keys.Select(a.Find).OfType<FileEntry>().ToList();
        var shared = entries.Any(e => e.Shared);
        var items = new List<MenuItemSpec> { Header(one?.Name ?? $"{t.Keys.Count} items") };
        if (one is not null)
        {
            items.Add(Item(MenuGlyphs.Open, "Open", FilesCommandIds.Open, argument: keys));
        }

        if (entries.Any(e => !e.IsDirectory) || t.Keys.Count > 1)
        {
            items.Add(Item(MenuGlyphs.Download, "Download", FilesCommandIds.Download, argument: keys));
        }

        if (one is { IsDirectory: true, Shared: false })
        {
            items.Add(Sep());
            items.Add(Item(MenuGlyphs.Star, a.IsStarred(one.Key) ? "Unstar" : "Star", FilesCommandIds.Star, argument: keys));
        }

        items.Add(Sep());
        if (one is not null)
        {
            items.Add(Item(MenuGlyphs.Rename, "Rename", FilesCommandIds.Rename, hint: "F2", argument: keys));
            items.Add(Item(MenuGlyphs.Copy, "Duplicate", FilesCommandIds.Duplicate, argument: keys));
        }

        items.Add(Sub(MenuGlyphs.Move, "Move to", FilesMenuIds.MoveTo));
        items.Add(Item(MenuGlyphs.Copy, "Cut", FilesCommandIds.Cut, hint: "Ctrl+X", argument: keys));
        items.Add(Item(MenuGlyphs.Copy, "Copy", FilesCommandIds.Copy, hint: "Ctrl+C", argument: keys));
        items.Add(Sep());
        items.Add(shared
            ? Danger(MenuGlyphs.Delete, "Delete", FilesCommandIds.Purge, hint: "Del", argument: keys)
            : Danger(MenuGlyphs.Delete, "Delete", FilesCommandIds.Delete, hint: "Del", confirm: "Move to trash", argument: keys));
        return items;
    }

    private static IReadOnlyList<MenuItemSpec> Trash(MenuContext c, FilesActions a)
    {
        if (c.File is not { Keys.Count: > 0 } t)
        {
            return [];
        }

        var keys = Keys(t.Keys);
        return
        [
            Header(t.Keys.Count == 1 ? a.Find(t.Keys[0])?.Name ?? "Item" : $"{t.Keys.Count} items"),
            Item(MenuGlyphs.Restore, "Restore", FilesCommandIds.Restore, argument: keys),
            Sep(),
            Danger(MenuGlyphs.Delete, "Delete forever", FilesCommandIds.Purge, hint: "Del", argument: keys),
        ];
    }

    private static IReadOnlyList<MenuItemSpec> Favourite(MenuContext c, FilesActions a)
    {
        if (c.File is not { Keys.Count: > 0 } t)
        {
            return [];
        }

        var keys = Keys(t.Keys);
        return [Header(a.Find(t.Keys[0])?.Name ?? "Favourite"), Item(MenuGlyphs.Open, "Open", FilesCommandIds.Open, argument: keys), Sep(), Item(MenuGlyphs.Star, "Unstar", FilesCommandIds.Star, argument: keys)];
    }

    private static List<MenuItemSpec> Recent(MenuContext c, FilesActions a)
    {
        if (c.File is not { Keys.Count: > 0 } t)
        {
            return [];
        }

        var keys = Keys(t.Keys);
        var items = new List<MenuItemSpec>
        {
            Header(a.Find(t.Keys[0])?.Name ?? "Recent"),
            Item(MenuGlyphs.Open, "Open", FilesCommandIds.Open, argument: keys),
            Item(MenuGlyphs.Folder, "Show in folder", FilesCommandIds.ShowInFolder, argument: keys),
        };
        if (a.Find(t.Keys[0]) is { IsDirectory: false })
        {
            items.Add(Item(MenuGlyphs.Download, "Download", FilesCommandIds.Download, argument: keys));
        }

        items.Add(Sep());
        items.Add(Item(MenuGlyphs.Cancel, "Remove from recent", FilesCommandIds.RemoveRecent, argument: keys));
        return items;
    }

    private static IReadOnlyList<MenuItemSpec> Background(MenuContext c, FilesActions a)
    {
        if (a.View.InTrash)
        {
            return [Danger(MenuGlyphs.Delete, "Empty trash", FilesCommandIds.EmptyTrash, enabled: a.View.Entries.Count > 0), Item(MenuGlyphs.Refresh, "Refresh", FilesCommandIds.Refresh)];
        }

        return
        [
            Item(MenuGlyphs.NewFolder, "New folder", FilesCommandIds.NewFolder),
            Item(MenuGlyphs.Upload, "Upload files", FilesCommandIds.Upload),
            Item(MenuGlyphs.Upload, "Upload folder", FilesCommandIds.UploadFolder),
            Sep(),
            Item(MenuGlyphs.Copy, "Paste", FilesCommandIds.Paste, hint: "Ctrl+V", enabled: a.CanPaste),
            Item(MenuGlyphs.Refresh, "Refresh", FilesCommandIds.Refresh),
        ];
    }

    private static List<MenuItemSpec> MoveTo(MenuContext c, FilesActions a)
    {
        if (c.File is not { Keys.Count: > 0 } t)
        {
            return [];
        }

        var keys = Keys(t.Keys);
        var items = new List<MenuItemSpec> { Item(MenuGlyphs.Folder, "My files", FilesCommandIds.MoveTo, argument: keys + "\t") };
        items.AddRange(a.MoveTargets(t.Keys).Select(m => Item(MenuGlyphs.Folder, m.Folder.Name, FilesCommandIds.MoveTo, argument: keys + "\t" + m.Folder.Key, indent: m.Depth + 1)));
        return items.Count == 1 ? [.. items, Note("No other folders")] : items;
    }
}
