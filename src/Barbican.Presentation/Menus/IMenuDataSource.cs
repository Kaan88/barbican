// Mirrors: Rust ctxmenu.rs mail_items :516 / folder_items :551 / submenu :575 (they read the window's folders, tags and settings when the menu opens);
// Bulwark components/email/mail-list-menus.tsx. Owner menus. The providers are pure over this seam so tests give them fixed folders and tags.

namespace Barbican.Presentation.Menus;

/// <summary>The folders, tags and settings the menu providers read; evaluated when a menu opens (no stale state).</summary>
public interface IMenuDataSource
{
    /// <summary>The folders of an account in display order, children after their parent.</summary>
    IReadOnlyList<MenuFolder> Folders(string? accountKey);

    /// <summary>One folder, or null when it is not known.</summary>
    MenuFolder? Folder(string? accountKey, string? mailboxId);

    /// <summary>The user's tags in display order.</summary>
    IReadOnlyList<MenuTag> Tags();

    /// <summary>Nested tags (tag ids with "/") are on.</summary>
    bool NestedTags { get; }

    /// <summary>"Ask before deleting several messages" (Bulwark confirmBulkDelete, default on).</summary>
    bool ConfirmBulkDelete { get; }

    /// <summary>The open message is shown as plain text (MENU-BODY offers Copy and Select all there).</summary>
    bool BodyIsPlainText { get; }
}
