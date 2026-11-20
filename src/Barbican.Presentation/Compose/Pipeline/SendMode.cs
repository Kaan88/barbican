// Mirrors: Rust compose.rs:818-826 send / send_scheduled; Bulwark components/email/email-composer.tsx (Send, Schedule send). M3 plan section 4.3 (SendCoordinator.RunAsync(ComposeDraft, SendMode)). Owner send-pipeline.

using Barbican.Core.Compose;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>Send now (with the undo window of the settings) or release at a time.</summary>
public sealed record SendMode(DateTimeOffset? ScheduledAt = null)
{
    public ComposerClassificationChoice? ClassificationChoice { get; init; }
    /// <summary>Send now.</summary>
    public static SendMode Now { get; } = new();

    /// <summary>Release at <paramref name="at"/>.</summary>
    public static SendMode At(DateTimeOffset at) => new(at);
}
