// Mirrors: Rust jmap.rs:1361 Changes; RFC 8620 §5.2 (/changes). Stub from F2; owner jmap.

namespace Barbican.Core.Sync;

/// <summary>A parsed <c>/changes</c> response.</summary>
public sealed record Changes
{
    public string OldState { get; init; } = "";

    public string NewState { get; init; } = "";

    public bool HasMore { get; init; }

    public IReadOnlyList<string> Created { get; init; } = [];

    public IReadOnlyList<string> Updated { get; init; } = [];

    public IReadOnlyList<string> Destroyed { get; init; } = [];

    /// <summary>Mailbox/changes: only these properties changed (RFC 8621 §2.2), e.g. the counts.</summary>
    public IReadOnlyList<string>? UpdatedProperties { get; init; }
}
