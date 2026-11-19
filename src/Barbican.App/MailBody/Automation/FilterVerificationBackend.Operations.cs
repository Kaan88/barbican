#if AUTOMATION_WRITES
using System.Text.Json.Nodes;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Sieve;

namespace Barbican.App.MailBody.Automation;

internal sealed partial class FilterVerificationBackend
{
    public async Task EnsureUniqueAsync(string token, CancellationToken ct)
    {
        Guard();
        if ((await MessageCandidatesAsync(token, ct).ConfigureAwait(false)).Count != 0
            || (await FolderCandidatesAsync(token, ct).ConfigureAwait(false)).Count != 0)
            throw new InvalidOperationException("The test token is already in use.");
    }

    public async Task<string> ImportAsync(string token, CancellationToken ct)
    {
        Guard();
        return Need(await WriteTestMessages.ImportAsync(_client, _mail, token, _time.GetUtcNow(), ct).ConfigureAwait(false));
    }

    public async Task<string> CreateFolderAsync(string token, CancellationToken ct)
    {
        Guard();
        return Need(await _writer.CreateMailboxAsync(FolderName(token), null, null, ct).ConfigureAwait(false)).Id;
    }

    public async Task<string?> DiscoverOwnMessageAsync(string token, CancellationToken ct)
    {
        Guard();
        var candidates = await MessageCandidatesAsync(token, ct).ConfigureAwait(false);
        if (candidates.Count != 1) return null;
        var marked = Need(await WriteTestMessages.FindAsync(_client, _mail, token, ct).ConfigureAwait(false));
        return marked.Count == 1 && string.Equals(marked[0], candidates[0], StringComparison.Ordinal) ? marked[0] : null;
    }

    public async Task<string?> DiscoverOwnFolderAsync(string token, CancellationToken ct)
    {
        Guard();
        var candidates = await FolderCandidatesAsync(token, ct).ConfigureAwait(false);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public async Task<(string State, JsonNode Rule)> SaveRuleAsync(FilterWriteSnapshot original, string token, string folderId, CancellationToken ct)
    {
        Guard();
        var loaded = Need(await _filters.LoadAsync(original.AccountKey, ct).ConfigureAwait(false));
        if (loaded.Locked || !string.Equals(loaded.State, original.State, StringComparison.Ordinal)) throw new InvalidOperationException("The filter state changed.");
        var name = FolderName(token);
        var built = FilterRules.BuildRule(null, null, name, false,
            [new FilterCond("subject", "is", WriteTestMessages.Subject(token), ""), new FilterCond("from", "address_is", "rx@example.org", "")],
            [new FilterAct("move", name, folderId, false)], [new FilterFolder(folderId, name)], false, false, "", "");
        var rule = built.Rule ?? throw new InvalidOperationException("Cannot build the synthetic filter rule.");
        var rules = loaded.Rules.Select(x => x.DeepClone()).Append(rule.DeepClone()).ToList();
        Guard();
        var saved = Need(await _filters.SaveAsync(original.AccountKey, rules, original.State, ct).ConfigureAwait(false));
        return (saved.State is { Length: > 0 } state ? state : throw new InvalidOperationException("The script state was not acknowledged."), rule);
    }

    public async Task<FilterScan> ScanAsync(JsonNode rule, CancellationToken ct)
    {
        Guard();
        return Need(await _filters.ScanAsync(_session.Account.Key, rule, 1, ct).ConfigureAwait(false));
    }

    public async Task ApplyAndVerifyAsync(FilterScan scan, string emailId, string folderId, CancellationToken ct)
    {
        Guard();
        if (!FilterWriteSafety.IsExactPlan(scan, emailId, folderId)) throw new InvalidOperationException("The scan includes unexpected mail.");
        var applied = Need(await _filters.ApplyAsync(_session.Account.Key, scan, ct).ConfigureAwait(false));
        if (applied.Kind != FilterAppliedKind.Done || applied.Count != 1) throw new InvalidOperationException("The move was not acknowledged.");
        var got = await GetAsync("Email/get", emailId, new JsonArray("id", "mailboxIds"), ct).ConfigureAwait(false);
        var (folders, _) = await ReadFoldersAsync(ct).ConfigureAwait(false);
        var inboxes = folders.OfType<JsonObject>().Where(x => string.Equals(x.Str("role"), "inbox", StringComparison.Ordinal)).Select(x => x.Str("id")).OfType<string>().ToList();
        if (inboxes.Count != 1 || got?["mailboxIds"] is not JsonObject boxes || boxes.ContainsKey(inboxes[0])
            || boxes[folderId] is not JsonValue present || !present.TryGetValue<bool>(out var yes) || !yes)
            throw new InvalidOperationException("The synthetic move was not verified.");
    }

    public async Task<bool> DestroyOwnMessageAsync(string token, string emailId, CancellationToken ct)
    {
        Guard();
        var result = await WriteTestMessages.DestroyAsync(_client, _mail, token, [emailId], ct).ConfigureAwait(false);
        if (!result.IsOk || result.Value != 1) return false;
        return await GetAsync("Email/get", emailId, new JsonArray("id"), ct).ConfigureAwait(false) is null;
    }

    public async Task<bool> DestroyOwnEmptyFolderAsync(string token, string folderId, CancellationToken ct)
    {
        Guard();
        var (folders, state) = await ReadFoldersAsync(ct).ConfigureAwait(false);
        var box = folders.OfType<JsonObject>().SingleOrDefault(x => string.Equals(x.Str("id"), folderId, StringComparison.Ordinal));
        if (box is null || !string.Equals(box.Str("name"), FolderName(token), StringComparison.Ordinal) || box["parentId"] is not null || box["role"] is not null
            || folders.OfType<JsonObject>().Any(x => string.Equals(x.Str("parentId"), folderId, StringComparison.Ordinal))) return false;
        Guard();
        var response = Need(await ReaderCalls.CallAsync(_client, "Mailbox/set", new JsonObject { ["accountId"] = _mail, ["ifInState"] = state, ["destroy"] = new JsonArray(folderId) }, ct).ConfigureAwait(false));
        if (response["destroyed"] is not JsonArray destroyed || destroyed.Count != 1 || !string.Equals(destroyed[0].AsString(), folderId, StringComparison.Ordinal)) return false;
        return await GetAsync("Mailbox/get", folderId, new JsonArray("id"), ct).ConfigureAwait(false) is null;
    }

    public async Task<bool> RestoreAsync(FilterWriteSnapshot original, string expectedState, CancellationToken ct)
    {
        Guard();
        var store = new SieveScriptStore(_client);
        var current = Need(await store.ReadAsync(ct).ConfigureAwait(false));
        if (!string.Equals(current.State, expectedState, StringComparison.Ordinal) || current.Target is not { IsActive: true } target || !string.Equals(target.Id, original.ScriptId, StringComparison.Ordinal)) return false;
        Guard();
        var saved = await store.SaveWithStateAsync(original.ScriptText, current, ct).ConfigureAwait(false);
        if (!saved.IsOk || string.IsNullOrEmpty(saved.Value)) return false;
        var verified = Need(await store.ReadAsync(ct).ConfigureAwait(false));
        if (!string.Equals(verified.State, saved.Value, StringComparison.Ordinal) || !string.Equals(verified.Text, original.ScriptText, StringComparison.Ordinal)
            || verified.Target is not { IsActive: true } active || !string.Equals(active.Id, original.ScriptId, StringComparison.Ordinal)) return false;
        var raw = await ReadScriptBytesAsync(active.BlobId, ct).ConfigureAwait(false);
        if (!FilterWriteSafety.CanRestore(verified, raw)) return false;
        var final = Need(await store.ReadAsync(ct).ConfigureAwait(false));
        return string.Equals(final.State, verified.State, StringComparison.Ordinal) && final.Target is { IsActive: true } last
            && string.Equals(last.Id, active.Id, StringComparison.Ordinal) && string.Equals(last.BlobId, active.BlobId, StringComparison.Ordinal);
    }

    private async Task<JsonObject?> GetAsync(string method, string id, JsonArray properties, CancellationToken ct)
    {
        var response = Need(await ReaderCalls.CallAsync(_client, method, new JsonObject { ["accountId"] = _mail, ["ids"] = new JsonArray(id), ["properties"] = properties }, ct).ConfigureAwait(false));
        if (response["list"] is not JsonArray list || response["notFound"] is not JsonArray missing) throw new InvalidOperationException("The server returned an incomplete verification response.");
        if (list.Count == 1 && string.Equals(list[0].Str("id"), id, StringComparison.Ordinal)) return list[0] as JsonObject;
        if (list.Count == 0 && missing.Count == 1 && string.Equals(missing[0].AsString(), id, StringComparison.Ordinal)) return null;
        throw new InvalidOperationException("The server returned an ambiguous verification response.");
    }

    private async Task<IReadOnlyList<string>> MessageCandidatesAsync(string token, CancellationToken ct)
    {
        var query = Need(await ReaderCalls.CallAsync(_client, "Email/query", new JsonObject
        {
            ["accountId"] = _mail, ["filter"] = new JsonObject { ["subject"] = $"rx:{token}" }, ["limit"] = 2, ["calculateTotal"] = true,
        }, ct).ConfigureAwait(false));
        if (query["total"] is not JsonValue v || !v.TryGetValue<int>(out var total) || total < 0 || total > 1)
            throw new InvalidOperationException("The synthetic message search is ambiguous.");
        var ids = ReaderCalls.Ids(query);
        if (ids.Count != total) throw new InvalidOperationException("The synthetic message search is incomplete.");
        return ids;
    }

    private async Task<IReadOnlyList<string>> FolderCandidatesAsync(string token, CancellationToken ct)
    {
        var (list, _) = await ReadFoldersAsync(ct).ConfigureAwait(false);
        return [.. list.OfType<JsonObject>().Where(box => string.Equals(box.Str("name"), FolderName(token), StringComparison.Ordinal)
            && box["parentId"] is null && box["role"] is null).Select(box => box.Str("id")).OfType<string>()];
    }

    private async Task<(JsonArray Folders, string State)> ReadFoldersAsync(CancellationToken ct)
    {
        var response = Need(await ReaderCalls.CallAsync(_client, "Mailbox/get", new JsonObject
        {
            ["accountId"] = _mail, ["ids"] = null, ["properties"] = new JsonArray("id", "name", "parentId", "role"),
        }, ct).ConfigureAwait(false));
        if (response["list"] is not JsonArray list || response.Str("state") is not { Length: > 0 } state
            || list.Count >= _client.Session.Limits.MaxObjectsInGet)
            throw new InvalidOperationException("The folder snapshot may be incomplete.");
        return (list, state);
    }

    private static string FolderName(string token) => "Barbican filter test " + token;
}
#endif
