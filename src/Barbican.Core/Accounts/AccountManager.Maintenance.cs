// Mirrors: Bulwark components/layout/account-switcher.tsx:442-675 (sign out of one account, sign out of all, set as default, reorder),
// stores/account-store.ts:200 removeAccount (the next account becomes active; none left shows the sign-in form), :220 setDefaultAccount,
// :240 reorderAccounts, lib/sign-out-cleanup.ts (the caches of the account go), lib/plugin-hook-catalog.ts:280-:283 (onAccountRemove,
// onBeforeLogout, onAfterLogout, each { accountId }); Rust oauth.rs:398 forget_account (only this app's own entry; ADR 0008: the Rust
// app's credentials are never touched). spec I-013, I-014, I-015, I-031. M2 plan 4.6, 6.5.
// Order of a sign-out: onBeforeLogout, the account leaves the list (the next one becomes active, so the sync engine and its push stream
// detach from it), its session is disposed (its token source and sync engine stop), the body cache is cleared, its remembered entry is
// removed from the vault, then onAfterLogout and onAccountRemove. The unread timer stops with the last account.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Accounts;

/// <summary>Sign-out, default account and order (IAccountMaintenance).</summary>
public sealed partial class AccountManager : IAccountMaintenance
{
    private readonly SemaphoreSlim _accountLifecycle = new(1, 1);
    private IReadOnlyList<string> _pluginSignOutKeys = [];
    private bool _pluginSignOutAll;

    /// <inheritdoc/>
    public Task<Result<Unit>> SignOutAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        object identity;
        lock (_lock)
        {
            if (!_entries.TryGetValue(accountKey, out var entry))
            {
                return Task.FromResult(Result<Unit>.Fail(new ValidationError("No such account.")));
            }
            identity = entry.LoginIdentity;
        }

        return SignOutManyAsync(accountKey, identity, ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> SignOutAllAsync(CancellationToken ct) => SignOutManyAsync(null, null, ct);

    /// <inheritdoc/>
    public async Task<Result<Unit>> SetDefaultAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        lock (_lock)
        {
            if (!_entries.ContainsKey(accountKey))
            {
                return Result<Unit>.Fail(new ValidationError("No such account."));
            }
        }

        var saved = await _store.SetDefaultAsync(accountKey, ct).ConfigureAwait(false);
        if (!saved.IsOk)
        {
            return saved;
        }

        lock (_lock)
        {
            foreach (var e in _entries.Values)
            {
                e.IsDefault = string.Equals(e.Info.Key, accountKey, StringComparison.Ordinal);
            }
        }

        Changed();
        return saved;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> MoveAsync(string accountKey, int newIndex, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        lock (_lock)
        {
            if (!_order.Remove(accountKey))
            {
                return Result<Unit>.Fail(new ValidationError("No such account."));
            }

            _order.Insert(Math.Clamp(newIndex, 0, _order.Count), accountKey);
        }

        Changed();
        return await _store.MoveAsync(accountKey, newIndex, ct).ConfigureAwait(false);
    }

    private async Task<Result<Unit>> SignOutManyAsync(string? accountKey, object? identity, CancellationToken ct)
    {
        await _accountLifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<string> keys;
            bool allAccounts;
            lock (_lock)
            {
                if (accountKey is not null &&
                    (!_entries.TryGetValue(accountKey, out var entry) || !ReferenceEquals(entry.LoginIdentity, identity)))
                    return Result<Unit>.Fail(new ValidationError("The account changed. Review it before signing out."));
                keys = accountKey is null ? [.. _order] : [accountKey];
                allAccounts = accountKey is null || _entries.Count == 1;
            }
            JmapError? vaultError = null;
            if (_signOutCleanup is not null)
            {
                if (!await TryHoldAddOnStorageAsync(ct).ConfigureAwait(false))
                    return Result<Unit>.Fail(new ValidationError("Could not get access to add-on data. Try signing out again."));
                if (allAccounts) (keys, allAccounts, vaultError) = await ResolveFullSignOutAsync(keys, accountKey, ct).ConfigureAwait(false);
            }
            foreach (var key in keys)
            {
                await NotifyAsync(HookNames.OnBeforeLogout, new AccountHookPayload(key), ct).ConfigureAwait(false);
            }
            if (_signOutCleanup is not null)
            {
                lock (_lock)
                {
                    _pluginSignOutKeys = keys;
                    _pluginSignOutAll = allAccounts;
                    if (_activeKey is { } key && IsPluginSignOutPending(key)) _selectionGeneration++;
                }
                var erased = await _signOutCleanup.PrepareAsync(keys, allAccounts, ct).ConfigureAwait(false);
                if (!erased.IsOk) return erased;
            }
            var signedOut = await CompleteSignOutAsync(keys, ct).ConfigureAwait(false);
            return signedOut.IsOk && vaultError is not null ? Result<Unit>.Fail(vaultError) : signedOut;
        }
        finally
        {
            lock (_lock)
            {
                _pluginSignOutKeys = [];
                _pluginSignOutAll = false;
            }
            _accountLifecycle.Release();
        }
    }

    private async Task<(IReadOnlyList<string> Keys, bool All, JmapError? VaultError)> ResolveFullSignOutAsync(
        IReadOnlyList<string> localKeys, string? accountKey, CancellationToken ct)
    {
        var remembered = await _store.ReadFreshAsync(ct).ConfigureAwait(false);
        if (accountKey is not null)
        {
            var last = remembered.IsOk && remembered.Value!.All(account => string.Equals(account.Key, accountKey, StringComparison.Ordinal));
            return (localKeys, last, null);
        }
        if (!remembered.IsOk) return (localKeys, true, remembered.Error);
        return ([.. localKeys.Union(remembered.Value!.Select(account => account.Key), StringComparer.Ordinal)], true, null);
    }

    private bool IsPluginSignOutPending(string login) => _pluginSignOutAll || _pluginSignOutKeys.Contains(login, StringComparer.Ordinal);

    private async Task<Result<Unit>> CompleteSignOutAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        var (sessions, previous, next) = Detach(keys);
        Changed();
        if (previous is not null)
        {
            // the hub moves the sync engine (and its push stream) to the next account, or the shell shows the sign-in form
            ActiveChanged?.Invoke(this, new ActiveAccountChangedEventArgs(previous, next));
            if (next is not null)
            {
                await NotifyAsync(HookNames.OnAccountSwitch, new AccountSwitchHookPayload(next.Key, previous.Key), ct).ConfigureAwait(false);
            }
        }

        foreach (var s in sessions)
        {
            s.Dispose();
        }

        _bodies?.Clear();
        var failed = new List<string>();
        foreach (var key in keys)
        {
            var forgot = await _store.ForgetAsync(key, ct).ConfigureAwait(false);
            if (!forgot.IsOk)
            {
                failed.Add(forgot.Error!.Message);
            }

            await NotifyAsync(HookNames.OnAfterLogout, new AccountHookPayload(key), ct).ConfigureAwait(false);
            await NotifyAsync(HookNames.OnAccountRemove, new AccountHookPayload(key), ct).ConfigureAwait(false);
        }

        // the next account may still be connecting (or unreachable): the retry loop brings it up
        StartRetry();
        return failed.Count == 0
            ? Result<Unit>.Ok(default)
            : Result<Unit>.Fail(new ValidationError($"Signed out, but the saved sign-in could not be removed: {failed[0]}"));
    }

    // takes the accounts out of the list; the active one hands over to the first remaining account with a session, else the first remaining
    private (List<AccountSession> Sessions, AccountInfo? Previous, AccountInfo? Next) Detach(IReadOnlyList<string> keys)
    {
        lock (_lock)
        {
            var sessions = new List<AccountSession>();
            AccountInfo? previous = null;
            foreach (var key in keys)
            {
                if (!_entries.Remove(key, out var e))
                {
                    continue;
                }

                _order.Remove(key);
                if (e.Session is { } s)
                {
                    sessions.Add(s);
                }

                if (string.Equals(key, _activeKey, StringComparison.Ordinal))
                {
                    previous = e.Info;
                }
            }

            if (_entries.Count > 0 && !_entries.Values.Any(e => e.IsDefault))
            {
                _entries[_order[0]].IsDefault = true;
            }

            AccountInfo? next = null;
            if (previous is not null)
            {
                _activeKey = _order.FirstOrDefault(k => _entries[k].Session is not null) ?? _order.FirstOrDefault();
                _selectionGeneration++;
                next = _activeKey is { } k ? _entries[k].Info : null;
            }

            if (_entries.Count == 0)
            {
                _unreadTimer?.Dispose();
                _unreadTimer = null;
            }

            return (sessions, previous, next);
        }
    }
}
