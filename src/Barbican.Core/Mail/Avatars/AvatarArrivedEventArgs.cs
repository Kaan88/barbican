// Mirrors: M2 plan section 4.5 (IAvatarService.Arrived); Rust avatars.rs:147-172 (the worker tells the list a picture arrived). Stub from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Avatars;

/// <summary>A picture for a sender is now available.</summary>
public sealed class AvatarArrivedEventArgs(string email) : EventArgs
{
    /// <summary>The sender address the picture belongs to.</summary>
    public string Email { get; } = email;
}
