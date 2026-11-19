// Mirrors: Rust compose.rs:134 open; Bulwark email-composer.tsx props (mode, replyTo, draft). Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>Asks the pipeline to prepare a composer (<see cref="IComposeSeeder"/>).</summary>
public sealed record ComposeRequest(string AccountId, ComposeKind Kind)
{
    /// <summary>The message answered or forwarded, or the draft to reopen (<see cref="ComposeKind.EditDraft"/>).</summary>
    public string? SourceEmailId { get; init; }

    public ComposePrefill? Prefill { get; init; }

    /// <summary>Optional plain-text handoff inserted into replies and reply-all only.</summary>
    public string? DraftText { get; init; }

    /// <summary>Rich text unless the plain-text setting (or the account) says otherwise.</summary>
    public bool Plain { get; init; }
}
