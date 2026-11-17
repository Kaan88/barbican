// Mirrors: Rust compose.rs:134 ComposeKind (+ forward as attachment); Bulwark components/email/email-composer.tsx. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>What a composer is for.</summary>
public enum ComposeKind
{
    New,
    Reply,
    ReplyAll,
    Forward,
    ForwardAsAttachment,

    /// <summary>Reopening a saved draft (also "Cancel and edit" of a scheduled or undone send).</summary>
    EditDraft,
}
