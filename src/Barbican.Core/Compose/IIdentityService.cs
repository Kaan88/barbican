// Mirrors: Rust jmap.rs identities, compose.rs From select; Bulwark lib/jmap/client.ts getIdentities, stores/identity-store.ts, email-composer.tsx From select; RFC 8621 section 6. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The sender identities of an account for the From select (cached per account, refreshed on an Identity state change).</summary>
public interface IIdentityService
{
    Task<Result<IReadOnlyList<ComposeIdentity>>> ListAsync(string accountId, CancellationToken ct);
}
