// Mirrors: Rust oauth.rs:373 remember (insert at the front, one entry per user and server), :393 accounts, :299 stored_refresh (re-read the
// store: another instance may have rotated the token), :305 store_rotated (write a rotated token back; never create or delete an entry);
// Bulwark stores/account-store.ts:162 (the first account added is the default). spec B-OAUTH-015, B-OAUTH-018.

using Barbican.Core.Auth;
using Barbican.Core.Common;

namespace Barbican.Core.Accounts;

/// <summary>The remembered accounts, cached in memory over an <see cref="IAccountVault"/>.</summary>
public sealed class AccountStore(IAccountVault vault) : IRefreshTokenStore, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<StoredAccount>? _list;

    /// <summary>The last error of the vault, or null (the list then reads as what was known).</summary>
    public string? LastError { get; private set; }

    /// <summary>The remembered accounts, most recently used first.</summary>
    public async Task<IReadOnlyList<StoredAccount>> AllAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return [.. await LoadedAsync(ct).ConfigureAwait(false)];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Remembers <paramref name="account"/> at the front (replacing its old entry; the default flag is kept, or set on the first one).</summary>
    public Task RememberAsync(StoredAccount account, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);
        return ChangeAsync(
            list =>
            {
                var old = list.FindIndex(a => string.Equals(a.Key, account.Key, StringComparison.Ordinal));
                var isDefault = old >= 0 ? list[old].IsDefault : list.TrueForAll(a => !a.IsDefault);
                if (old >= 0)
                {
                    list.RemoveAt(old);
                }

                list.Insert(0, account with { IsDefault = isDefault });
                return true;
            },
            ct);
    }

    /// <summary>Moves a remembered account to the front (it was switched to).</summary>
    public Task TouchAsync(string key, CancellationToken ct) => ChangeAsync(
        list =>
        {
            var i = list.FindIndex(a => string.Equals(a.Key, key, StringComparison.Ordinal));
            if (i <= 0)
            {
                return false;
            }

            var a = list[i];
            list.RemoveAt(i);
            list.Insert(0, a);
            return true;
        },
        ct);

    /// <inheritdoc/>
    public async Task<string?> ReadAsync(string accountKey, CancellationToken ct)
    {
        // always from the vault: another instance may have rotated it since
        var fresh = await vault.LoadAsync(ct).ConfigureAwait(false);
        var list = fresh.IsOk ? fresh.Value! : (IReadOnlyList<StoredAccount>)(_list ?? []);
        return list.FirstOrDefault(a => a.IsOAuth && string.Equals(a.Key, accountKey, StringComparison.Ordinal))?.Refresh;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string accountKey, string refresh, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fresh = await vault.LoadAsync(ct).ConfigureAwait(false);
            if (!fresh.IsOk)
            {
                LastError = fresh.Error!.Message;
                return;
            }

            var list = fresh.Value!.ToList();
            var i = list.FindIndex(a => a.IsOAuth && string.Equals(a.Key, accountKey, StringComparison.Ordinal));
            if (i < 0 || string.Equals(list[i].Refresh, refresh, StringComparison.Ordinal))
            {
                _list = list;
                return;
            }

            list[i] = list[i] with { Refresh = refresh };
            _list = list;
            await SaveAsync(list, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<List<StoredAccount>> LoadedAsync(CancellationToken ct)
    {
        if (_list is null)
        {
            var r = await vault.LoadAsync(ct).ConfigureAwait(false);
            LastError = r.IsOk ? null : r.Error!.Message;
            _list = r.IsOk ? [.. r.Value!] : [];
        }

        return _list;
    }

    private async Task ChangeAsync(Func<List<StoredAccount>, bool> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = await LoadedAsync(ct).ConfigureAwait(false);
            if (change(list))
            {
                await SaveAsync(list, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(List<StoredAccount> list, CancellationToken ct)
    {
        Result<Unit> saved = await vault.SaveAsync([.. list], ct).ConfigureAwait(false);
        LastError = saved.IsOk ? null : saved.Error!.Message;
    }
}
