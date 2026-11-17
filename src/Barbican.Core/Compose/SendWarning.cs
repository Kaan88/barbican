// Mirrors: Rust compose.rs:795 pre_send_warning; Bulwark components/email/send-warnings-dialog.tsx. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>One warning with the items to show (the placeholders, the external addresses); empty for the other kinds.</summary>
public sealed record SendWarning(SendWarningKind Kind, IReadOnlyList<string> Items);
