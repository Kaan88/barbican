// Mirrors: Bulwark lib/plugin-hook-catalog.ts:159 onSyncStateChange payload `{ state: "syncing" | "idle" | "error", error? }`,
// stores/email-store.ts:1371 (overlapping runs share one syncing/idle cycle), :7529, :7654. Owner jmap.

namespace Barbican.Core.Sync;

/// <summary>The payload of onSyncStateChange.</summary>
/// <param name="State">"syncing", "idle" or "error".</param>
/// <param name="Error">The error message when <paramref name="State"/> is "error".</param>
public sealed record SyncStatePayload(string State, string? Error = null)
{
    /// <summary>A fetch of changes started.</summary>
    public static SyncStatePayload Syncing { get; } = new("syncing");

    /// <summary>Every running fetch finished.</summary>
    public static SyncStatePayload Idle { get; } = new("idle");
}
