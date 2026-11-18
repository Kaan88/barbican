// Mirrors: Bulwark folder-settings.tsx (name checks, role choices, "move under" choices), Stalwart crates/jmap/src/mailbox/set.rs (one folder per role; a role folder is not renamed or moved; a folder cannot become its own descendant). Contract from the M6 plan section 4.8; owner srv-data.

namespace Barbican.Core.Settings.Folders;

/// <summary>The pure rules of folder management.</summary>
public static class FolderAdminRules
{
    /// <summary>The longest folder name Stalwart accepts.</summary>
    public const int MaxNameLength = 255;

    /// <summary>The roles a folder can be given, in the order the page lists them (archive, drafts, junk, sent, trash, important).</summary>
    public static IReadOnlyList<string> AssignableRoles { get; } = ["archive", "drafts", "junk", "sent", "trash", "important"];

    /// <summary>What is wrong with a folder name among its siblings, or null: empty or only spaces, longer than 255 characters, containing "/" or a control character, or the same as a sibling's name (case-insensitive).</summary>
    public static string? NameProblem(string name, IReadOnlyList<string> siblingNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(siblingNames);
        var t = name.Trim();
        if (t.Length == 0)
        {
            return "Give the folder a name.";
        }

        if (t.Length > MaxNameLength)
        {
            return "The folder name is too long.";
        }

        if (t.Contains('/', StringComparison.Ordinal) || t.Any(char.IsControl))
        {
            return "A folder name cannot contain \"/\".";
        }

        return siblingNames.Any(s => string.Equals(s.Trim(), t, StringComparison.OrdinalIgnoreCase)) ? "A folder with this name already exists here." : null;
    }

    /// <summary>The folder that already has the role, other than <paramref name="exceptId"/>, or null (a role belongs to one folder; assigning it elsewhere moves it).</summary>
    public static FolderAdminNode? RoleHolder(IReadOnlyList<FolderAdminNode> folders, string role, string? exceptId)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(role);
        return folders.FirstOrDefault(f => string.Equals(f.Role, role, StringComparison.Ordinal) && !string.Equals(f.Id, exceptId, StringComparison.Ordinal));
    }

    /// <summary>Whether <paramref name="id"/> may be moved under <paramref name="newParentId"/> (null: the top): not under itself or one of its own descendants, and the role folders (inbox, trash, ...) stay where they are.</summary>
    public static bool CanMoveUnder(IReadOnlyList<FolderAdminNode> folders, string id, string? newParentId)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(id);
        var byId = folders.ToDictionary(f => f.Id, StringComparer.Ordinal);
        if (!byId.TryGetValue(id, out var moving) || !string.IsNullOrEmpty(moving.Role))
        {
            return false;
        }

        if (newParentId is null)
        {
            return true;
        }

        for (var p = newParentId; p is not null && byId.TryGetValue(p, out var node); p = node.ParentId)
        {
            if (string.Equals(p, id, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return byId.ContainsKey(newParentId);
    }
}
