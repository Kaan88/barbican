// Mirrors: Rust jmap.rs:1178 Outgoing (fields M1's pure functions use; M3 adds the rest). Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Submission;

/// <summary>A message being sent (the M3 compose pipeline fills it).</summary>
public sealed record Outgoing
{
    public string From { get; init; } = "";

    public string To { get; init; } = "";

    public string Cc { get; init; } = "";

    public string Bcc { get; init; } = "";

    public string Subject { get; init; } = "";

    public string Text { get; init; } = "";

    public FromOverride? FromOverride { get; init; }
}
