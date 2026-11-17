// Mirrors: Rust oauth.rs:373 remember, :393 accounts, :398 forget_account (the remembered accounts in the OS credential store); ADR 0008
// (the C# app's own entries, Barbican.Next/<profile>/accounts). Implemented over ISecretStore in Barbican.Platform (SecretAccountVault);
// Core cannot see the platform layer.
// Fable review 2 R2-06: two instances share the vault, so a change writes or removes one account against what the vault holds now
// (UpsertAsync, RemoveAsync); replacing the whole list (SaveAsync) from a view read earlier would delete what the other instance added.

using Barbican.Core.Common;

namespace Barbican.Core.Accounts;

/// <summary>Where the remembered accounts are kept (with their secrets).</summary>
public interface IAccountVault
{
    /// <summary>The remembered accounts, most recently used first (empty when none).</summary>
    Task<Result<IReadOnlyList<StoredAccount>>> LoadAsync(CancellationToken ct);

    /// <summary>Replaces the remembered accounts (the one-time import and migration only; a change goes through <see cref="UpsertAsync"/>).</summary>
    Task<Result<Unit>> SaveAsync(IReadOnlyList<StoredAccount> accounts, CancellationToken ct);

    /// <summary>
    /// Writes one account. <paramref name="position"/> moves it to that place in the order (clamped); null keeps its place, and a new account
    /// goes last. Nothing else is touched.
    /// </summary>
    async Task<Result<Unit>> UpsertAsync(StoredAccount account, int? position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);
        var loaded = await LoadAsync(ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return Result<Unit>.Fail(loaded.Error!);
        }

        var list = loaded.Value!.ToList();
        var at = list.FindIndex(a => string.Equals(a.Key, account.Key, StringComparison.Ordinal));
        if (at >= 0)
        {
            list.RemoveAt(at);
        }

        var to = position is { } p ? Math.Clamp(p, 0, list.Count) : at >= 0 ? Math.Min(at, list.Count) : list.Count;
        list.Insert(to, account);
        return await SaveAsync(list, ct).ConfigureAwait(false);
    }

    /// <summary>Forgets one account and its secret; nothing else is touched. Forgetting an unknown account succeeds.</summary>
    async Task<Result<Unit>> RemoveAsync(string accountKey, CancellationToken ct)
    {
        var loaded = await LoadAsync(ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return Result<Unit>.Fail(loaded.Error!);
        }

        var list = loaded.Value!.Where(a => !string.Equals(a.Key, accountKey, StringComparison.Ordinal)).ToList();
        return list.Count == loaded.Value!.Count ? Result<Unit>.Ok(default) : await SaveAsync(list, ct).ConfigureAwait(false);
    }
}
