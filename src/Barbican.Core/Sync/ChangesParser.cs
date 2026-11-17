// Mirrors: Rust jmap.rs:1374 parse_changes; RFC 8620 §5.2 (/changes: oldState, newState, hasMoreChanges, created, updated,
// destroyed), RFC 8621 §2.2 (Mailbox/changes updatedProperties); Bulwark lib/jmap/client.ts:2915 getEmailChanges, :2920
// getMailboxChanges (null on cannotCalculateChanges); Stalwart crates/jmap/src/changes/get.rs. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.State;

namespace Barbican.Core.Sync;

/// <summary>Reads a <c>/changes</c> method response.</summary>
public static class ChangesParser
{
    /// <summary>The changes, or null for a method error (cannotCalculateChanges...) or a Stalwart state that went backwards.</summary>
    public static Changes? Parse(JsonArray response, string since, bool stalwart)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(since);
        if (response.Count < 2 || response[0].AsString() is not { } name || !name.EndsWith("/changes", StringComparison.Ordinal))
        {
            return null;
        }

        var r = response[1];
        if (r.Str("newState") is not { } newState || (stalwart && StalwartState.WentBack(since, newState)))
        {
            return null;
        }

        return new Changes
        {
            OldState = r.Str("oldState") ?? since,
            NewState = newState,
            HasMore = r.True("hasMoreChanges"),
            Created = r.Strings("created"),
            Updated = r.Strings("updated"),
            Destroyed = r.Strings("destroyed"),
            UpdatedProperties = r.Arr("updatedProperties") is null ? null : r.Strings("updatedProperties"),
        };
    }
}
