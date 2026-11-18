// Mirrors: Bulwark folder-settings.tsx (the tree: role folders first in their usual order, then the others by name, children under their parent; a collapsed parent hides its subtree), Core folder ordering of the sidebar (M1). I-430. Contract from the M6 plan section 4.11; owner srv-ui.

using Barbican.Core.Common;
using Barbican.Core.Settings.Folders;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The pure rules of the Folders page tree.</summary>
public static class SrvUiFolderTreeRules
{
    /// <summary>The rows in display order: top-level folders with a role first (inbox, drafts, sent, archive, junk, trash, then other roles), then the rest by name ignoring case; each folder is followed by its children, ordered by name, one level deeper; the children of a collapsed id are left out.</summary>
    public static IReadOnlyList<SrvUiFolderRow> Flatten(IReadOnlyList<FolderAdminNode> folders, IReadOnlySet<string> collapsed) => throw new PortPendingException("SrvUiFolderTreeRules.Flatten");
}
