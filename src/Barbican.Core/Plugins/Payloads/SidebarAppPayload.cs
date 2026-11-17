// Mirrors: Bulwark onSidebarAppOpen / onSidebarAppClose (lib/plugin-hook-catalog.ts). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>An app of the rail was opened or closed.</summary>
public sealed record SidebarAppPayload(string AppId);
