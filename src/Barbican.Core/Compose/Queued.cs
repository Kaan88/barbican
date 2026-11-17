// Mirrors: Rust autosave.rs:17 Queued; Bulwark email-composer.tsx (send/save queued behind a save in flight). Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>What waits for the save in flight (the last request wins): a send, a scheduled send, an explicit save (and close), or a discard.</summary>
public abstract record Queued
{
    private Queued()
    {
    }

    public sealed record Send : Queued;

    /// <summary>A scheduled send at the given Unix milliseconds.</summary>
    public sealed record Schedule(long AtMs) : Queued;

    public sealed record Save(bool Close) : Queued;

    public sealed record Discard : Queued;
}
