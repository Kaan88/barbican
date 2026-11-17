// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:330 (payload of onDragEnd); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onDragEnd.</summary>
public sealed record DragEndPayload(string Kind, IReadOnlyList<string> Ids, bool Dropped);
