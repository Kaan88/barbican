// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:329 (payload of onDragStart); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onDragStart.</summary>
public sealed record DragStartPayload(string Kind, IReadOnlyList<string> Ids);
