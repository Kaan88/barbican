// Mirrors: Rust jmap.rs role_ids (folders found by role: drafts, sent, inbox). Owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>The Drafts, Sent and Inbox folders of an account, by role.</summary>
internal sealed record MailboxRoles(string Drafts, string? Sent, string? Inbox = null);
