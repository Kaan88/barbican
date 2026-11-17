// Mirrors: Rust oauth.rs remembered sessions (keyring entries "Barbican"/"session" and "accounts"); PORT-PLAN.md §3.5, §3.9. Contract stub from F2; owner accounts.

using Barbican.Core.Common;

namespace Barbican.Platform;

/// <summary>Where secrets live: Windows Credential Manager, the Secret Service on Linux, a fake in tests.</summary>
public interface ISecretStore
{
    Task<Result<string?>> ReadAsync(SecretName name, CancellationToken ct);

    Task<Result<Unit>> WriteAsync(SecretName name, string value, CancellationToken ct);

    Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct);
}
