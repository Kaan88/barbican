// Mirrors: Bulwark onSidebarToggle (lib/plugin-hook-catalog.ts). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The sidebar opened or closed.</summary>
public sealed record SidebarTogglePayload(bool Open);
