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
    /// <inheritdoc/>
    public Task<Result<Unit>> SignOutAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        lock (_lock)
        {
            if (!_entries.ContainsKey(accountKey))
            {
                return Task.FromResult(Result<Unit>.Fail(new ValidationError("No such account.")));
            }
        }

        return SignOutManyAsync([accountKey], ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> SignOutAllAsync(CancellationToken ct)
    {
        List<string> keys;
        lock (_lock)
        {
            keys = [.. _order];
        }

        return SignOutManyAsync(keys, ct);
    }

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

    private async Task<Result<Unit>> SignOutManyAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        foreach (var key in keys)
        {
            await NotifyAsync(HookNames.OnBeforeLogout, new AccountHookPayload(key), ct).ConfigureAwait(false);
        }

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
