// Mirrors: Bulwark onSidebarCollapse (lib/plugin-hook-catalog.ts). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The sidebar collapsed to the rail or expanded.</summary>
public sealed record SidebarCollapsePayload(bool Collapsed);
