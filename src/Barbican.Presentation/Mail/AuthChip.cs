// Mirrors: Rust auth.rs (AuthResult), Bulwark lib/server-auth-status.ts. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>One check of the receiving server (dkim, spf, dmarc) as a chip in the reader.</summary>
/// <param name="Method">dkim, spf or dmarc.</param>
/// <param name="Result">pass, fail, softfail, none, ...</param>
/// <param name="Aligned">For a pass: whether it ties the message to the From domain.</param>
public sealed record AuthChip(string Method, string Result, bool Aligned);
