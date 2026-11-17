// Mirrors: Rust autosave.rs:42 Leftover. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>What the save still in flight from a composer that was just replaced must do once it finishes.</summary>
public enum Leftover
{
    /// <summary>Save the old composer's last content (snapshotted at the reset) over the new draft.</summary>
    Save,

    /// <summary>Remove the draft that save creates (a discard was queued behind it).</summary>
    Discard,
}
