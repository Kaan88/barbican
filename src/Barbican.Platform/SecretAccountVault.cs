// Mirrors: Rust oauth.rs:373 remember (the "session" entry holds the last signed-in account, "accounts" the list, most recent first),
// :393 accounts (an entry that is not a JSON array reads as no accounts); ADR 0008 (UTF-8 JSON in the C# app's own entries).

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;

namespace Barbican.Platform;

/// <summary>The remembered accounts in the secret store: <c>accounts</c> (the list) and <c>session</c> (its first entry).</summary>
public sealed class SecretAccountVault(ISecretStore store) : IAccountVault
{
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<StoredAccount>>> LoadAsync(CancellationToken ct)
    {
        var r = await store.ReadAsync(SecretTarget.Accounts, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<IReadOnlyList<StoredAccount>>.Fail(r.Error!);
        }

        return Result<IReadOnlyList<StoredAccount>>.Ok(Parse(r.Value));
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> SaveAsync(IReadOnlyList<StoredAccount> accounts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var list = new JsonArray([.. accounts.Select(a => (JsonNode)a.ToJson())]);
        var saved = await store.WriteAsync(SecretTarget.Accounts, list.ToJsonString(), ct).ConfigureAwait(false);
        if (!saved.IsOk)
        {
            return saved;
        }

        return accounts.Count > 0
            ? await store.WriteAsync(SecretTarget.Session, accounts[0].ToJson().ToJsonString(), ct).ConfigureAwait(false)
            : await store.DeleteAsync(SecretTarget.Session, ct).ConfigureAwait(false);
    }

    /// <summary>The accounts in an <c>accounts</c> entry; entries that are not accounts are skipped, duplicates keep the first.</summary>
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
}
