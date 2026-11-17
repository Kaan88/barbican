// Mirrors: Rust avatars.rs:40 Slot (Pending, Missing, Ready) plus Unknown (not asked yet). Owner reader.

namespace Barbican.Core.Mail.Avatars;

/// <summary>What is known about one avatar candidate.</summary>
public enum AvatarSlotState
{
    /// <summary>Not asked for yet.</summary>
    Unknown,

    /// <summary>Being fetched; the initials stay.</summary>
    Pending,

    /// <summary>There is none (the next candidate is tried).</summary>
    Missing,

    /// <summary>A picture is ready.</summary>
    Ready,
}
