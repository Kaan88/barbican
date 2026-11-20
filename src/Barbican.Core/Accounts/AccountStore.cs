// Mirrors: Rust oauth.rs:373 remember (insert at the front, one entry per user and server), :393 accounts, :398 forget_account, :299
// stored_refresh (re-read the store: another instance may have rotated the token), :305 store_rotated (write a rotated token back; never
// create or delete an entry); Bulwark stores/account-store.ts:162 (the first account added is the default), :220 setDefaultAccount,
// :240 reorderAccounts. spec B-OAUTH-015, B-OAUTH-018.
// Fable review 2 R2-06: every change re-reads the vault inside the cross-process refresh lock and writes or removes the one account it is
// about (IAccountVault.UpsertAsync/RemoveAsync), so a second instance's accounts and rotated tokens are never overwritten or deleted from a
// stale view. The lock is taken before the in-process gate: a token refresh holds the lock and then writes through WriteAsync.

using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Storage;

namespace Barbican.Core.Accounts;

/// <summary>The remembered accounts, cached in memory over an <see cref="IAccountVault"/>.</summary>
/// <param name="vault">The vault.</param>
/// <param name="paths">The profile whose refresh lock guards changes; null (tests) changes without the cross-process lock.</param>
/// <param name="time">The clock of the lock's wait.</param>
public sealed class AccountStore(IAccountVault vault, IProfilePaths? paths = null, TimeProvider? time = null) : IRefreshTokenStore, IDisposable
{
    // what a change answers when there is nothing to write (no write, no reload)
    private static readonly Task<Result<Unit>> Unchanged = Task.FromResult(Result<Unit>.Ok(default));

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
            if (_list is null)
            {
                var r = await vault.LoadAsync(ct).ConfigureAwait(false);
                LastError = r.IsOk ? null : r.Error!.Message;
                _list = r.IsOk ? [.. r.Value!] : [];
            }

            return [.. _list];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the authoritative vault again without treating an older cache as current.</summary>
    internal async Task<Result<IReadOnlyList<StoredAccount>>> ReadFreshAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fresh = await vault.LoadAsync(ct).ConfigureAwait(false);
            LastError = fresh.IsOk ? null : fresh.Error!.Message;
            if (fresh.IsOk) _list = [.. fresh.Value!];
            return fresh;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Remembers <paramref name="account"/> at the front (replacing its old entry; the default flag is kept, or set on the first one).</summary>
    public Task<Result<Unit>> RememberAsync(StoredAccount account, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);
        return ChangeAsync(
            (list, t) =>
            {
                var old = list.Find(a => string.Equals(a.Key, account.Key, StringComparison.Ordinal));
                var isDefault = old?.IsDefault ?? list.TrueForAll(a => !a.IsDefault);
                return vault.UpsertAsync(account with { IsDefault = isDefault }, 0, t);
            },
            ct);
    }

    /// <summary>Moves a remembered account to the front (it was switched to).</summary>
    public Task<Result<Unit>> TouchAsync(string key, CancellationToken ct) => MoveAsync(key, 0, ct);

    /// <summary>Moves a remembered account to <paramref name="index"/> in the order (clamped).</summary>
    public Task<Result<Unit>> MoveAsync(string key, int index, CancellationToken ct) => ChangeAsync(
        (list, t) =>
        {
            var i = list.FindIndex(a => string.Equals(a.Key, key, StringComparison.Ordinal));
            return i < 0 || i == Math.Clamp(index, 0, list.Count - 1) ? Unchanged : vault.UpsertAsync(list[i], index, t);
        },
        ct);

    /// <summary>Makes <paramref name="key"/> the default account: it is marked first, then the old default is cleared, so one is always marked.</summary>
    public Task<Result<Unit>> SetDefaultAsync(string key, CancellationToken ct) => ChangeAsync(
        async (list, t) =>
        {
            var target = list.Find(a => string.Equals(a.Key, key, StringComparison.Ordinal));
            if (target is null)
            {
                return Result<Unit>.Fail(new ValidationError("That account is not remembered."));
            }

            if (!target.IsDefault)
            {
                var marked = await vault.UpsertAsync(target with { IsDefault = true }, null, t).ConfigureAwait(false);
                if (!marked.IsOk)
                {
                    return marked;
                }
            }

            foreach (var other in list.Where(a => a.IsDefault && !string.Equals(a.Key, key, StringComparison.Ordinal)))
            {
                var cleared = await vault.UpsertAsync(other with { IsDefault = false }, null, t).ConfigureAwait(false);
                if (!cleared.IsOk)
                {
                    return cleared;
                }
            }

            return Result<Unit>.Ok(default);
        },
        ct);

    /// <summary>
    /// Forgets one account and its secret (only this app's entry: the Rust app's credentials are never touched, ADR 0008). When it was the
    /// default, the first remaining account becomes the default.
    /// </summary>
    public Task<Result<Unit>> ForgetAsync(string key, CancellationToken ct) => ChangeAsync(
        async (list, t) =>
        {
            var gone = list.Find(a => string.Equals(a.Key, key, StringComparison.Ordinal));
            if (gone is null)
            {
                return Result<Unit>.Ok(default);
            }

            var removed = await vault.RemoveAsync(key, t).ConfigureAwait(false);
            if (!removed.IsOk || !gone.IsDefault)
            {
                return removed;
            }

            return list.Find(a => !string.Equals(a.Key, key, StringComparison.Ordinal)) is { } next
                ? await vault.UpsertAsync(next with { IsDefault = true }, null, t).ConfigureAwait(false)
                : removed;
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
    /// <remarks>Called by a token refresh, which already holds the cross-process lock (taking it again here would wait on ourselves).</remarks>
    public async Task WriteAsync(string accountKey, string refresh, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ApplyAsync(
                (list, t) =>
                {
                    var i = list.FindIndex(a => a.IsOAuth && string.Equals(a.Key, accountKey, StringComparison.Ordinal));
                    return i < 0 || string.Equals(list[i].Refresh, refresh, StringComparison.Ordinal) ? Unchanged : vault.UpsertAsync(list[i] with { Refresh = refresh }, null, t);
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<Result<Unit>> ChangeAsync(Func<List<StoredAccount>, CancellationToken, Task<Result<Unit>>> write, CancellationToken ct)
    {
        using var crossProcess = paths is null ? null : await RefreshLock.AcquireAsync(paths, time ?? TimeProvider.System, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ApplyAsync(write, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // under the gate: the vault as it is now, one write, then the vault as it is after it (the cache follows the other instance too)
    private async Task<Result<Unit>> ApplyAsync(Func<List<StoredAccount>, CancellationToken, Task<Result<Unit>>> write, CancellationToken ct)
    {
        var fresh = await vault.LoadAsync(ct).ConfigureAwait(false);
        if (!fresh.IsOk)
        {
            LastError = fresh.Error!.Message;
            return Result<Unit>.Fail(fresh.Error!);
        }

        var list = fresh.Value!.ToList();
        _list = list;
        var writing = write(list, ct);
        if (ReferenceEquals(writing, Unchanged))
        {
            return Result<Unit>.Ok(default);
        }

        var written = await writing.ConfigureAwait(false);
        LastError = written.IsOk ? null : written.Error!.Message;
        var after = await vault.LoadAsync(ct).ConfigureAwait(false);
        if (after.IsOk)
        {
            _list = [.. after.Value!];
        }

        return written;
    }
}
