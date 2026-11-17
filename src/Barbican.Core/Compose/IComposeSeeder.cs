// Mirrors: Rust compose.rs:134 open; Bulwark components/email/email-composer.tsx; Bulwark lib/reply-identity.ts. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>
/// Prepares a composer: for a reply, reply-all or forward the recipients, "Re:"/"Fwd:" subject, quoted original (cleaned, with <c>cid:</c> images resent as inline
/// attachments), the identity (<see cref="ReplyIdentity"/>) and the signature; for a draft the stored fields. Reads the message through the mail service.
/// </summary>
public interface IComposeSeeder
{
    Task<Result<ComposeSeed>> SeedAsync(ComposeRequest request, CancellationToken ct);
}
