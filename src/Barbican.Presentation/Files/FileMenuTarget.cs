// Mirrors: spec/menus.md MENU-FILE, MENU-FILE-TRASH, MENU-FILE-FAV, MENU-FILE-RECENT, MENU-FILES-BG (Rust ctxmenu.rs); M5 plan section 4.11. Additive contract of the plan commit (frozen): MenuContext.File. Owner files-ui provides the items; the menu ids are files-ui's own FilesMenuIds.

namespace Barbican.Presentation.Files;

/// <summary>What a Files menu was opened on: the entries (keys; empty for the background), and the folder the background belongs to (null: the root, or a place that has no folder).</summary>
public sealed record FileMenuTarget(string AccountKey, IReadOnlyList<string> Keys, string? FolderKey = null);
