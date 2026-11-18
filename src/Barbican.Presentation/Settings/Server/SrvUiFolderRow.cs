// Mirrors: Bulwark components/settings/filter-settings.tsx, folder-settings.tsx, vacation-settings.tsx, M6 plan section 4.11. Contract from the M6 plan; owner srv-ui.

using Barbican.Core.Settings.Folders;

namespace Barbican.Presentation.Settings.Server;

/// <summary>A folder in the Folders page tree, flattened.</summary>
public sealed record SrvUiFolderRow(FolderAdminNode Node, int Depth, bool HasChildren, bool Collapsed);
