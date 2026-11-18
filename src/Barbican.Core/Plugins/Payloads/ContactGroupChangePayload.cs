// Mirrors: Bulwark lib/plugin-hook-catalog.ts:199 (onContactGroupChange: { groupId, change: "created" | "updated" | "deleted" }). Owner con-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A contact group changed: <paramref name="Change"/> is "created", "updated" or "deleted".</summary>
public sealed record ContactGroupChangePayload(string GroupId, string Change);
