// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>An address with an optional display name (RFC 8621 §4.1.2.3 EmailAddress).</summary>
public sealed record EmailAddress(string? Name, string Email);
