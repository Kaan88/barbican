// Mirrors: Rust msgload.rs (loading, ready, failed) and J-053 (truncated body); spec B-SYNC-022..029. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>Where the body of the open message is.</summary>
public enum BodyState
{
    /// <summary>Nothing is open.</summary>
    Empty,

    /// <summary>The header is shown from the list row; the body is still on its way.</summary>
    Loading,

    /// <summary>The body is on screen.</summary>
    Ready,

    /// <summary>The body is on screen but the server cut it short.</summary>
    Truncated,

    /// <summary>The message could not be loaded.</summary>
    Failed,
}
