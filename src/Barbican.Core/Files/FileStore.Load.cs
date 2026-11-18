// Mirrors: Rust files.rs:204 open (the primary account, then the accounts shared with the login that have the filenode capability; FileNode/query of the whole account then FileNode/get in chunks of maxObjectsInGet; a shared account that fails
// is skipped, the user's own failing is an error), :18 draft18, :213 legacy; Bulwark lib/jmap/client.ts (getFileNodes: client-side parent filter, J-075); RFC 8620 5.5 paged queries; Stalwart crates/jmap/src/file/{query,get}.rs. Owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Files;

public sealed partial class FileStore
{
    /// <inheritdoc/>
    public async Task<Result<FileSnapshot>> LoadAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);

        // favourites and recent are read from files.json here, off the calling (UI) thread, so the getters never touch the disk on it
        await Task.Run(PreloadLocal, CancellationToken.None).ConfigureAwait(false);
        if (_accounts.SessionFor(accountKey) is not { } session)
        {
            return Result<FileSnapshot>.Fail(new ValidationError("This account is not signed in."));
        }

        if (!session.Client.Session.Has(FileAccounts.Capability))
        {
            lock (_lock)
            {
                _ = _held.Remove(accountKey);
            }

            return Result<FileSnapshot>.Ok(new FileSnapshot(accountKey, [], null, null));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadLockedAsync(accountKey, session.Client, ct).ConfigureAwait(false);
            if (loaded.IsOk)
            {
                EnsureWatching(accountKey, session.Client);
            }

            return loaded;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<Result<FileSnapshot>> LoadLockedAsync(string key, IJmapClient client, CancellationToken ct)
    {
        var js = client.Session;
        var primary = FileAccounts.Primary(js);
        var held = new Held(primary) { Rules = FileNameChecks.RulesFrom(FileAccounts.CapabilityOf(js, primary)) };
        var entries = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
        var draft = FileAccounts.IsDraft(js, primary);
        foreach (var account in FileAccounts.All(js))
        {
            var shared = !string.Equals(account, primary, StringComparison.Ordinal);
            var one = await LoadAccountAsync(client, key, account, shared, ct).ConfigureAwait(false);
            if (!one.IsOk)
            {
                // a shared account that fails must not hide the user's own files
                if (!shared)
                {
                    return Result<FileSnapshot>.Fail(one.Error!);
                }

                continue;
            }

            foreach (var e in one.Value!.Entries)
            {
                entries[e.Key] = e;
            }

            draft |= one.Value.Draft;
            held.States[account] = one.Value.State;
        }

        held.Entries = [.. entries.Values];
        held.Draft18 = draft;
        held.Build(key);
        lock (_lock)
        {
            _held[key] = held;
        }

        Raise(new FileChangedEventArgs(key, []));
        return Result<FileSnapshot>.Ok(held.Snapshot);
    }

    /// <summary>The nodes of one server account: the state first (the baseline for /changes, read before the nodes so a change made while they load is not missed), every id (paged), the nodes in maxObjectsInGet chunks.</summary>
    private static async Task<Result<AccountLoad>> LoadAccountAsync(IJmapClient client, string key, string account, bool shared, CancellationToken ct)
    {
        var baseline = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("FileNode/get", new JsonObject { ["accountId"] = account, ["ids"] = new JsonArray(), ["properties"] = new JsonArray("id") }, "s")), ct).ConfigureAwait(false);
        if (!baseline.IsOk)
        {
            return Result<AccountLoad>.Fail(baseline.Error!);
        }

        var state = baseline.Value!.MethodResponses[0][1].Str("state");
        var ids = await client.QueryAllAsync("FileNode/query", account, [], ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<AccountLoad>.Fail(ids.Error!);
        }

        var got = await client.GetAllAsync("FileNode/get", account, ids.Value!, new JsonArray([.. Props.Select(p => (JsonNode)p)]), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<AccountLoad>.Fail(got.Error!);
        }

        return Result<AccountLoad>.Ok(Normalise(got.Value!, key, account, shared, state));
    }

    private static AccountLoad Normalise(IReadOnlyList<JsonObject> nodes, string key, string account, bool shared, string? state)
    {
        var entries = new List<FileEntry>(nodes.Count);
        var draft = false;
        foreach (var n in nodes)
        {
            if (FileEntry.FromJmap(n, key, account, shared) is { IsOk: true } e)
            {
                entries.Add(e.Value!);
                draft |= n["@type"] is JsonValue;
            }
        }

        return new AccountLoad(entries, draft, state);
    }

    private sealed record AccountLoad(List<FileEntry> Entries, bool Draft, string? State);
}
