// The ids of the Files commands the menus, keys, palette and automation run (spec/menus.md MENU-FILE, MENU-FILES-BG; Rust shortcuts.rs files keys; B-FILE-012). Owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>Command ids of the Files app.</summary>
public static class FilesCommandIds
{
    public const string Open = "files.open";
    public const string Download = "files.download";
    public const string Star = "files.star";
    public const string Rename = "files.rename";
    public const string Duplicate = "files.duplicate";
    public const string MoveTo = "files.move";
    public const string MoveDialog = "files.move-dialog";
    public const string Cut = "files.cut";
    public const string Copy = "files.copy";
    public const string Paste = "files.paste";
    public const string Delete = "files.delete";
    public const string Restore = "files.restore";
    public const string Purge = "files.purge";
    public const string EmptyTrash = "files.empty-trash";
    public const string NewFolder = "files.new-folder";
    public const string NewFile = "files.new-file";
    public const string Upload = "files.upload";
    public const string UploadFolder = "files.upload-folder";
    public const string Refresh = "files.refresh";
    public const string Undo = "files.undo";
    public const string ShowInFolder = "files.show-in-folder";
    public const string RemoveRecent = "files.remove-recent";
    public const string ToggleView = "files.toggle-view";
    public const string SelectAll = "files.select-all";
    public const string Details = "files.details";
}
