// Mirrors: PORT-PLAN.md §3.4 / §4.6 (transport options: read-only mode, retries, trace); Rust jmap.rs:82 trace
// (BARBICAN_JMAP_TRACE), :141 the 30 s request timeout. Owner jmap.

namespace Barbican.Core.Jmap;

/// <summary>Per-connection options.</summary>
public sealed record JmapClientOptions
{
    /// <summary>The read-only account: refuse every method outside the read allow-list (PORT-PLAN.md §4.6).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Where to append the method-name trace; null reads <c>BARBICAN_JMAP_TRACE</c>. The trace holds method names and call order
    /// only: never arguments, ids, addresses or bodies.
    /// </summary>
    public string? TracePath { get; init; }

    /// <summary>The deadline for one API request or session fetch (Rust: 30 s).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The longest <c>Retry-After</c> a read-only batch waits before its one retry (PORT-PLAN.md §3.4: 10 s).</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(10);
}
