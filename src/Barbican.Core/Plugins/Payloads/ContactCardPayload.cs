// Mirrors: Bulwark lib/plugin-hook-catalog.ts:191-194 (onBeforeContactCreate, onAfterContactCreate, onAfterContactUpdate: ContactView). Owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A contact as the hooks see it: its id (null before it exists) and the JSContact card.</summary>
public sealed record ContactCardPayload(string? ContactId, JsonNode Card);
