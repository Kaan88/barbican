// Mirrors: Bulwark folder-settings.tsx (the tree: role folders first in their usual order, then the others by name, children under their parent; a collapsed parent hides its subtree), Core folder ordering of the sidebar (M1). I-430. Contract from the M6 plan section 4.11; owner srv-ui.

using Barbican.Core.Settings.Folders;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The pure rules of the Folders page tree.</summary>
public static class SrvUiFolderTreeRules
{
    private static readonly string[] RoleOrder = ["inbox", "drafts", "sent", "archive", "junk", "trash"];

    /// <summary>The rows in display order: top-level folders with a role first (inbox, drafts, sent, archive, junk, trash, then other roles), then the rest by name ignoring case; each folder is followed by its children, ordered by name, one level deeper; the children of a collapsed id are left out.</summary>
    public static IReadOnlyList<SrvUiFolderRow> Flatten(IReadOnlyList<FolderAdminNode> folders, IReadOnlySet<string> collapsed)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(collapsed);
        var ids = folders.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var children = folders.Where(f => f.ParentId is not null && ids.Contains(f.ParentId)).ToLookup(f => f.ParentId!, StringComparer.Ordinal);
        var top = folders.Where(f => f.ParentId is null || !ids.Contains(f.ParentId)).ToList();
        var ordered = top.Where(f => !string.IsNullOrEmpty(f.Role)).OrderBy(f => RoleRank(f.Role!)).ThenBy(f => f.Role, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(top.Where(f => string.IsNullOrEmpty(f.Role)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase));
        var rows = new List<SrvUiFolderRow>();
        foreach (var node in ordered)
        {
            Add(node, 0);
        }

        return rows;

        void Add(FolderAdminNode node, int depth)
        {
            var kids = children[node.Id].OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var isCollapsed = collapsed.Contains(node.Id);
            rows.Add(new SrvUiFolderRow(node, depth, kids.Count > 0, isCollapsed));
            if (isCollapsed)
            {
                return;
            }

            foreach (var kid in kids)
            {
                Add(kid, depth + 1);
            }
        }
    }

    private static int RoleRank(string role)
    {
        var at = Array.FindIndex(RoleOrder, r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        return at < 0 ? RoleOrder.Length : at;
    }
}
