// Mirrors: Rust jmap.rs:1396 stalwart_change_id, :1419 stalwart_state, :1443 went_back; Bulwark lib/jmap/stalwart-state.ts;
// Stalwart crates/jmap-proto/src/types/state.rs. Stub from F2; owner jmap.

using Barbican.Core.Common;

namespace Barbican.Core.Jmap.State;

/// <summary>Stalwart's state strings carry the change id (<c>s</c> plus a base-32-like varint), so a state that went backwards is detectable.</summary>
public static class StalwartState
{
    /// <summary>The state string Stalwart writes for a change id.</summary>
    public static string Encode(ulong changeId) => throw new PortPendingException("StalwartState.Encode: jmap stream");

    /// <summary>The change id of a Stalwart state, or null for an opaque or non-canonical one.</summary>
    public static ulong? ChangeId(string state) => throw new PortPendingException("StalwartState.ChangeId: jmap stream");

    /// <summary>True only when both are Stalwart states and the new one has a lower id.</summary>
    public static bool WentBack(string since, string current) => throw new PortPendingException("StalwartState.WentBack: jmap stream");
}
