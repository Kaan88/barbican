// Mirrors: Rust oauth.rs (system browser for the OAuth loopback flow); ADR 0007 (a fake in AUTOMATION builds). Contract stub from F2; owner accounts.

using Barbican.Core.Common;

namespace Barbican.Platform;

/// <summary>Opens a URL in the system browser.</summary>
public interface IBrowserLauncher
{
    Task<Result<Unit>> OpenAsync(Uri uri, CancellationToken ct);
}
