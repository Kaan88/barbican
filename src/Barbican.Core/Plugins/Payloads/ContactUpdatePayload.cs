// Mirrors: Bulwark lib/plugin-hook-catalog.ts:193 (onBeforeContactUpdate: { contactId, patch }). Owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A contact is about to change: its id and the patch (JSON pointers to values, null removes).</summary>
public sealed record ContactUpdatePayload(string ContactId, JsonNode Patch);
