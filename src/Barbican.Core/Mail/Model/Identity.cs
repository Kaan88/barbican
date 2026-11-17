// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>A sending identity (RFC 8621 §6). A wildcard identity has an email such as <c>*@example.org</c>.</summary>
public sealed record Identity(string Id, string? Name, string Email);
