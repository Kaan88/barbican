// Mirrors: Rust compose.rs:210 ReplyFrom; Bulwark lib/reply-identity.ts resolveReplyFrom. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Jmap.Submission;

namespace Barbican.Core.Compose;

/// <summary>What decides a reply's From.</summary>
public sealed record ReplyFrom
{
    /// <summary>The original's sender, when the user sent it (keep sending as that identity).</summary>
    public string? SelfSent { get; init; }

    /// <summary>To, Cc and Bcc of the original.</summary>
    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>Delivered-To and friends (<see cref="ComposeRules.DeliveryAddresses"/>).</summary>
    public IReadOnlyList<string> Delivered { get; init; } = [];

    /// <summary>Forwards never take the catch-all match.</summary>
    public bool Forward { get; init; }

    /// <summary>The display names the original gave its recipients (address, name): the name a catch-all From override takes.</summary>
    public IReadOnlyList<(string Address, string Name)> Names { get; init; } = [];

    /// <summary>A reopened draft's From: its identity, or a From override when no identity has the address.</summary>
    public FromOverride? DraftFrom { get; init; }
}
