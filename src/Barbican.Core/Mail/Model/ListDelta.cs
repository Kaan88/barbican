// Mirrors: Rust jmap.rs:1522 ListDelta; RFC 8620 §5.6 (/queryChanges). Model stub from F2; owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>What changed in a list (Email/queryChanges plus the fresh copies of changed messages).</summary>
public sealed record ListDelta
{
    /// <summary>Email/queryChanges <c>removed</c> (may include ids that are re-added, RFC 8620 §5.6).</summary>
    public IReadOnlyList<string> Removed { get; init; } = [];

    /// <summary>Email/queryChanges <c>added</c>: (id, index in the new list), ascending.</summary>
    public IReadOnlyList<(string Id, int Index)> Added { get; init; } = [];

    /// <summary>Fresh copies of the added and the updated messages.</summary>
    public IReadOnlyDictionary<string, EmailSummary> Emails { get; init; } = new Dictionary<string, EmailSummary>(StringComparer.Ordinal);

    /// <summary>Thread sizes by thread id.</summary>
    public IReadOnlyDictionary<string, int> Threads { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyList<string> DestroyedThreads { get; init; } = [];

    public int? Total { get; init; }

    public string QueryState { get; init; } = "";

    public string EmailState { get; init; } = "";

    public string ThreadState { get; init; } = "";
}
