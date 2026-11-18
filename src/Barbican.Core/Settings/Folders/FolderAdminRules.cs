// Mirrors: Bulwark folder-settings.tsx (name checks, role choices, "move under" choices), Stalwart mailbox/set.rs (one folder per role; a name with "/" or a leading dot is refused). Contract from the M6 plan section 4.8; owner srv-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Folders;

/// <summary>The pure rules of folder management.</summary>
public static class FolderAdminRules
{
    /// <summary>The roles a folder can be given, in the order the page lists them (archive, drafts, junk, sent, trash, important).</summary>
    public static IReadOnlyList<string> AssignableRoles { get; } = ["archive", "drafts", "junk", "sent", "trash", "important"];

    /// <summary>What is wrong with a folder name among its siblings, or null: empty or only spaces, longer than 255 characters, containing "/" or a control character, or the same as a sibling's name (case-insensitive).</summary>
    public static string? NameProblem(string name, IReadOnlyList<string> siblingNames) => throw new PortPendingException("FolderAdminRules.NameProblem");

    /// <summary>The folder that already has the role, other than <paramref name="exceptId"/>, or null (a role belongs to one folder; assigning it elsewhere moves it).</summary>
    public static FolderAdminNode? RoleHolder(IReadOnlyList<FolderAdminNode> folders, string role, string? exceptId) => throw new PortPendingException("FolderAdminRules.RoleHolder");

    /// <summary>Whether <paramref name="id"/> may be moved under <paramref name="newParentId"/> (null: the top): not under itself or one of its own descendants, and the role folders (inbox, trash, ...) stay where they are.</summary>
    public static bool CanMoveUnder(IReadOnlyList<FolderAdminNode> folders, string id, string? newParentId) => throw new PortPendingException("FolderAdminRules.CanMoveUnder");
}
