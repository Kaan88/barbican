// Mirrors: Rust avatars.rs:40 Slot. Owner reader.

namespace Barbican.Core.Mail.Avatars;

/// <summary>One candidate's state and, when ready, its picture.</summary>
public readonly record struct AvatarSlot(AvatarSlotState State, AvatarImage? Image)
{
    /// <summary>Not asked for yet.</summary>
    public static AvatarSlot Unknown => new(AvatarSlotState.Unknown, null);

    /// <summary>Being fetched.</summary>
    public static AvatarSlot Pending => new(AvatarSlotState.Pending, null);

    /// <summary>There is none.</summary>
    public static AvatarSlot Missing => new(AvatarSlotState.Missing, null);
}
