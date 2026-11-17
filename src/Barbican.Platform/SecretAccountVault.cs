// Mirrors: Rust oauth.rs:373 remember (the remembered accounts, most recent first), :393 accounts (an entry that is not a JSON array reads as no
// accounts); ADR 0008 (UTF-8 JSON in the C# app's own entries).
// Deliberate change (Fable review 1 F-05, ADR 0008 amended): Rust and the first C# vault kept every account in one "accounts" credential, but
// Windows caps a credential blob at 2560 bytes, so a handful of OAuth accounts would make the next sign-in fail. Now each account is its own
// entry, "account/<id>" (id: the first 16 bytes of SHA-256 of the account key, hex, so no address appears in the target name), and
// "account-index" holds only the ordered ids. The old single "accounts" entry (and the "session" copy of its first account) is read once,
// split, and removed.
// Fable review 2 R2-06: UpsertAsync and RemoveAsync change one entry and re-read the index just before writing it, so another instance's
// accounts stay named; SaveAsync (import, migration) is the only path that deletes entries the index no longer names.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;

namespace Barbican.Platform;

/// <summary>The remembered accounts in the secret store: one entry per account and an index of their ids.</summary>
public sealed class SecretAccountVault(ISecretStore store) : IAccountVault
{
    /// <summary>The ordered ids of the remembered accounts (a JSON array of strings; no secret, no address).</summary>
    public static readonly SecretName Index = new("account-index");

    /// <summary>The entry of one account.</summary>
    public static SecretName EntryOf(string accountKey) => new("account", IdOf(accountKey));

    /// <summary>The id of an account in entry names: 32 hex digits of SHA-256 over its key.</summary>
    public static string IdOf(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accountKey)).AsSpan(0, 16));
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<StoredAccount>>> LoadAsync(CancellationToken ct)
    {
        var index = await store.ReadAsync(Index, ct).ConfigureAwait(false);
        if (!index.IsOk)
        {
            return Result<IReadOnlyList<StoredAccount>>.Fail(index.Error!);
        }

        if (index.Value is null)
        {
            return await MigrateAsync(ct).ConfigureAwait(false);
        }

        var accounts = new List<StoredAccount>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in Ids(index.Value))
        {
            var entry = await store.ReadAsync(new SecretName("account", id), ct).ConfigureAwait(false);
            if (!entry.IsOk)
            {
                return Result<IReadOnlyList<StoredAccount>>.Fail(entry.Error!);
            }

            if (ParseOne(entry.Value) is { } account && seen.Add(account.Key))
            {
                accounts.Add(account);
            }
        }

        return Result<IReadOnlyList<StoredAccount>>.Ok(accounts);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> SaveAsync(IReadOnlyList<StoredAccount> accounts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var before = await store.ReadAsync(Index, ct).ConfigureAwait(false);
        var old = before is { IsOk: true, Value: { } text } ? Ids(text) : [];

        // the entries first, then the index that names them, then the entries nothing names any more: a crash in between leaves at worst
        // an unnamed entry, never a named one that is missing
        var ids = new List<string>(accounts.Count);
        foreach (var account in accounts)
        {
            var id = IdOf(account.Key);
            if (ids.Contains(id, StringComparer.Ordinal))
            {
                continue;
            }

            var written = await store.WriteAsync(new SecretName("account", id), account.ToJson().ToJsonString(), ct).ConfigureAwait(false);
            if (!written.IsOk)
            {
                return written;
            }

            ids.Add(id);
        }

        var indexed = await WriteIndexAsync(ids, ct).ConfigureAwait(false);
        if (!indexed.IsOk)
        {
            return indexed;
        }

        foreach (var gone in old.Except(ids, StringComparer.Ordinal))
        {
            _ = await store.DeleteAsync(new SecretName("account", gone), ct).ConfigureAwait(false);
        }

        // the single-entry layout of the first C# builds: removed once the split layout is written
        _ = await store.DeleteAsync(SecretTarget.Accounts, ct).ConfigureAwait(false);
        _ = await store.DeleteAsync(SecretTarget.Session, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> UpsertAsync(StoredAccount account, int? position, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(account);
        var id = IdOf(account.Key);

        // the entry first, then the index that names it (a crash in between leaves an unnamed entry, never a named one that is missing)
        var written = await store.WriteAsync(new SecretName("account", id), account.ToJson().ToJsonString(), ct).ConfigureAwait(false);
        if (!written.IsOk)
        {
            return written;
        }

        var index = await store.ReadAsync(Index, ct).ConfigureAwait(false);
        if (!index.IsOk)
        {
            return Result<Unit>.Fail(index.Error!);
        }

        var ids = index.Value is { } text ? Ids(text) : [];
        var at = ids.IndexOf(id);
        if (at >= 0)
        {
            ids.RemoveAt(at);
        }

        var to = position is { } p ? Math.Clamp(p, 0, ids.Count) : at >= 0 ? Math.Min(at, ids.Count) : ids.Count;
        ids.Insert(to, id);
        return at == to && index.Value is not null ? Result<Unit>.Ok(default) : await WriteIndexAsync(ids, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> RemoveAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var id = IdOf(accountKey);
        var index = await store.ReadAsync(Index, ct).ConfigureAwait(false);
        if (!index.IsOk)
        {
            return Result<Unit>.Fail(index.Error!);
        }

        // the name goes first, then the entry (a crash in between leaves an unnamed entry, which SaveAsync cleans up)
        var ids = index.Value is { } text ? Ids(text) : [];
        if (ids.Remove(id))
        {
            var indexed = await WriteIndexAsync(ids, ct).ConfigureAwait(false);
            if (!indexed.IsOk)
            {
                return indexed;
            }
        }

        return await store.DeleteAsync(new SecretName("account", id), ct).ConfigureAwait(false);
    }

    /// <summary>The accounts in an old single <c>accounts</c> entry; entries that are not accounts are skipped, duplicates keep the first.</summary>
    public static IReadOnlyList<StoredAccount> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(json) is not JsonArray array)
            {
                return [];
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            return [.. array.Select(StoredAccount.FromJson).OfType<StoredAccount>().Where(a => seen.Add(a.Key))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private Task<Result<Unit>> WriteIndexAsync(List<string> ids, CancellationToken ct) =>
        store.WriteAsync(Index, new JsonArray([.. ids.Select(i => (JsonNode)i)]).ToJsonString(), ct);

    private static StoredAccount? ParseOne(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return StoredAccount.FromJson(JsonNode.Parse(json));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> Ids(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonArray a
                ? [.. a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().Where(IsId).Distinct(StringComparer.Ordinal)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsId(string s) => s.Length == 32 && s.All(char.IsAsciiHexDigitLower);

    // no index yet: the single "accounts" entry of the first C# builds, split into one entry per account
    private async Task<Result<IReadOnlyList<StoredAccount>>> MigrateAsync(CancellationToken ct)
    {
        var legacy = await store.ReadAsync(SecretTarget.Accounts, ct).ConfigureAwait(false);
        if (!legacy.IsOk)
        {
            return Result<IReadOnlyList<StoredAccount>>.Fail(legacy.Error!);
        }

        var accounts = Parse(legacy.Value);
        if (accounts.Count > 0)
        {
            var saved = await SaveAsync(accounts, ct).ConfigureAwait(false);
            if (!saved.IsOk)
            {
                return Result<IReadOnlyList<StoredAccount>>.Fail(saved.Error!);
            }
        }

        return Result<IReadOnlyList<StoredAccount>>.Ok(accounts);
    }
}
