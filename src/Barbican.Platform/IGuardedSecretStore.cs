// Host-managed index pointer writes must check authority at the actual credential-store operation.
using Barbican.Core.Common;

namespace Barbican.Platform;

public interface IGuardedSecretStore : ISecretStore
{
    Task<Result<Unit>> WriteWhileCurrentAsync(SecretName name, string value, Func<Func<bool>, bool> publishWhileCurrent, CancellationToken ct);
}
