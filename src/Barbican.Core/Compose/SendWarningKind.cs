// Mirrors: Rust compose.rs:795 pre_send_warning; Bulwark components/email/send-warnings-dialog.tsx, external-recipient-confirm.tsx (G-054). Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>What the "before you send" dialog can warn about, in the order it lists them.</summary>
public enum SendWarningKind
{
    EmptySubject,
    UnfilledPlaceholders,
    MissingAttachment,

    /// <summary>Recipients outside the sender's own domains (Bulwark's external-recipient confirm; Rust had none, G-054).</summary>
    ExternalRecipients,
}
