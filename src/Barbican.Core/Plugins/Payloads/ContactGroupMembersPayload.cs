// Mirrors: Bulwark lib/plugin-hook-catalog.ts:200 (onContactGroupMemberChange: { groupId, added, removed }). Owner con-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>Members were added to or removed from a group (the member keys, uids).</summary>
public sealed record ContactGroupMembersPayload(string GroupId, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);
