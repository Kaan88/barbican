// Mirrors: Rust compose.rs:835-880 send_at (the order of its checks); Bulwark message-actions.ts. Owner send-pipeline.

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>How a run of the send flow ended.</summary>
public enum SendOutcomeStatus
{
    /// <summary>The server accepted it (with an undo window when the receipt says so).</summary>
    Sent,

    /// <summary>Held for a release time.</summary>
    Scheduled,

    /// <summary>The user answered the warnings dialog with Cancel.</summary>
    Cancelled,

    /// <summary>The server or a plugin refused; <see cref="SendOutcome.Message"/> says why.</summary>
    Failed,

    /// <summary>An inline image has no blob id yet; nothing was sent.</summary>
    PendingImages,

    /// <summary>A recipient entry is no address; nothing was sent.</summary>
    InvalidAddresses,

    /// <summary>Another send is still running.</summary>
    Busy,
}
