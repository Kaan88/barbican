// Mirrors: Rust jmap.rs:1178 Outgoing.from_override; Bulwark "Edit sender" / catch-all reply rewrite. Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Submission;

/// <summary>The header From a user typed instead of the identity's (name may be empty).</summary>
public sealed record FromOverride(string Name, string Email);
