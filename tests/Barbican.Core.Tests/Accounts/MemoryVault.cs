// The account vault in memory (the real one is the OS secret store, tested in Platform.Tests).

using Barbican.Core.Accounts;
using Barbican.Core.Common;

namespace Barbican.Core.Tests.Accounts;

internal sealed class MemoryVault : IAccountVault
{
    private List<StoredAccount> _accounts = [];

    public IReadOnlyList<StoredAccount> Accounts
    {
        get
        {
            lock (this)
            {
                return [.. _accounts];
            }
        }
    }

    public Task<Result<IReadOnlyList<StoredAccount>>> LoadAsync(CancellationToken ct)
    {
        lock (this)
        {
            return Task.FromResult(Result<IReadOnlyList<StoredAccount>>.Ok([.. _accounts]));
        }
    }

    public Task<Result<Unit>> SaveAsync(IReadOnlyList<StoredAccount> accounts, CancellationToken ct)
    {
        lock (this)
        {
            _accounts = [.. accounts];
        }

        return Task.FromResult(Result<Unit>.Ok(default));
    }
}
