// Mirrors: RFC 8620 5.2 (FileNode/changes from a state, hasMoreChanges, cannotCalculateChanges), Rust mail.rs:254 load_mailboxes (a delta from the kept state, else a full read), M4 CalendarStore.Refresh (the same rule), PORT-PLAN section 3.4;
// Stalwart crates/jmap/src/file/changes.rs. G-044 (files live). Owner files-data.

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
    public async Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        Held? held;
        lock (_lock)
        {
            _ = _held.TryGetValue(accountKey, out held);
        }

        if (held is null || _accounts.SessionFor(accountKey) is not { } session)
        {
            var loaded = await LoadAsync(accountKey, ct).ConfigureAwait(false);
            return loaded.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(loaded.Error!);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await ApplyChangesAsync(accountKey, held, session.Client, ct).ConfigureAwait(false);

            // the server cannot say what changed from this state: read everything again (a fresh Held replaces this one)
            return !result.IsOk && result.Error is MethodError { Type: "cannotCalculateChanges" } ? await ReloadAsync(accountKey, session.Client, ct).ConfigureAwait(false) : result;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task<Result<Unit>> ReloadAsync(string accountKey, IJmapClient client, CancellationToken ct)
    {
        var loaded = await LoadLockedAsync(accountKey, client, ct).ConfigureAwait(false);
        return loaded.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(loaded.Error!);
    }

    private async Task<Result<Unit>> ApplyChangesAsync(string key, Held held, IJmapClient client, CancellationToken ct)
    {
        var touched = new List<string>();
        foreach (var account in held.States.Keys.ToList())
        {
            var one = await ApplyAccountAsync(key, held, client, account, ct).ConfigureAwait(false);
            if (!one.IsOk)
            {
                return Result<Unit>.Fail(one.Error!);
            }

            touched.AddRange(one.Value!);
        }

        if (touched.Count > 0)
        {
            Raise(new FileChangedEventArgs(key, [.. touched.Distinct(StringComparer.Ordinal)]));
        }

        return Result<Unit>.Ok(default);
    }

    /// <summary>/changes of one server account put into the held nodes; the keys that were added, changed or removed.</summary>
    private async Task<Result<List<string>>> ApplyAccountAsync(string key, Held held, IJmapClient client, string account, CancellationToken ct)
    {
        var shared = !string.Equals(account, held.Primary, StringComparison.Ordinal);
        if (held.States[account] is not { } since)
        {
            return Result<List<string>>.Fail(new MethodError("FileNode/changes", "cannotCalculateChanges", "no state kept"));
        }

        var changes = await ChangesAsync(client, account, since, ct).ConfigureAwait(false);
        if (!changes.IsOk)
        {
            return Result<List<string>>.Fail(changes.Error!);
        }

        AccountLoad? fetched = null;
        if (changes.Value!.Changed.Count > 0)
        {
            var got = await client.GetAllAsync("FileNode/get", account, changes.Value.Changed, new JsonArray([.. Props.Select(p => (JsonNode)p)]), ct).ConfigureAwait(false);
            if (!got.IsOk)
            {
                return Result<List<string>>.Fail(got.Error!);
            }

            fetched = Normalise(got.Value!, key, account, shared, null);
        }

        var gone = changes.Value.Destroyed.Select(i => FileEntry.MakeKey(account, i, shared)).ToHashSet(StringComparer.Ordinal);
        lock (_lock)
        {
            var list = held.Entries.Where(e => !gone.Contains(e.Key)).ToList();
            foreach (var e in fetched?.Entries ?? [])
            {
                var at = list.FindIndex(x => string.Equals(x.Key, e.Key, StringComparison.Ordinal));
                if (at >= 0)
                {
                    list[at] = e;
                }
                else
                {
                    list.Add(e);
                }
            }

            held.Entries = list;
            held.Draft18 |= fetched?.Draft ?? false;
            held.States[account] = changes.Value.NewState;
            held.Build(key);
        }

        return Result<List<string>>.Ok([.. gone, .. (fetched?.Entries ?? []).Select(e => e.Key)]);
    }

    /// <summary>The created, updated and destroyed ids since a state, all pages of it (hasMoreChanges).</summary>
    private static async Task<Result<ChangeSet>> ChangesAsync(IJmapClient client, string account, string since, CancellationToken ct)
    {
        var changed = new List<string>();
        var destroyed = new List<string>();
        var state = since;
        while (true)
        {
            var args = new JsonObject { ["accountId"] = account, ["sinceState"] = state };
            var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("FileNode/changes", args, "h")), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<ChangeSet>.Fail(r.Error!);
            }

            var res = r.Value!.MethodResponses[0][1];
            changed.AddRange(res.Strings("created"));
            changed.AddRange(res.Strings("updated"));
            destroyed.AddRange(res.Strings("destroyed"));
            var next = res.Str("newState") ?? state;
            if (!res.True("hasMoreChanges") || string.Equals(next, state, StringComparison.Ordinal))
            {
                return Result<ChangeSet>.Ok(new ChangeSet([.. changed.Distinct(StringComparer.Ordinal)], destroyed, next));
            }

            state = next;
        }
    }

    private sealed record ChangeSet(List<string> Changed, List<string> Destroyed, string NewState);
}
