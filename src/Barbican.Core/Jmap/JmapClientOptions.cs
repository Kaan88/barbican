// Mirrors: PORT-PLAN.md §3.4 / §4.6 (transport options: read-only mode, limits, retry). Stub from F2; owner jmap.

namespace Barbican.Core.Jmap;

/// <summary>Per-connection options.</summary>
public sealed record JmapClientOptions
{
    /// <summary>The read-only account: refuse every method outside the read allow-list (PORT-PLAN.md §4.6).</summary>
    public bool ReadOnly { get; init; }
}
