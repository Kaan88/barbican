// Mirrors: Rust reader.rs:117 unsubscribe_mailto (recipient, one-line subject capped, body capped at 500); RFC 6068 (mailto URIs). Stub from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Reader;

/// <summary>The message a mailto: unsubscribe link asks to send.</summary>
public sealed record UnsubscribeMailto(string To, string? Subject, string? Body);
