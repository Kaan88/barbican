// Mirrors: Rust server_settings.rs:72 save_script (upload, SieveScript/validate, SieveScript/set with onSuccessActivateScript), filters_x.rs:565 run_rule (the Email state first, Inbox query narrowed by the rule, Email/get in chunks, then the plan), filters_x.rs:451 apply_with;
// Bulwark stores/filter-store.ts (fetchFilters, saveFilters: generate, applyScriptTransforms, update), lib/jmap/client.ts:7533-7708 (the Sieve methods), lib/filters/retroactive.ts; RFC 9661 (Sieve scripts over JMAP), RFC 8620 section 5.3 (ifInState); Stalwart crates/jmap/src/sieve/ and crates/jmap/src/email/set.rs.
// Hooks (plugin-hook-catalog.ts:255-260): onFiltersLoaded, onBeforeFiltersSave (intercept), onSieveScriptGenerate (transform), onFiltersSave, onSieveScriptChange, onFilterRuleChange (derived from the rules that changed). B-SIEVE-023, B-SIEVE-028, J-073. Owner srv-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Sieve;

namespace Barbican.Core.Filters.Rules;

/// <summary>The default <see cref="IFilterService"/>: the rules are the metadata block of the account's Sieve script; every call runs on the pool (D-M6-00).</summary>
public sealed class FilterService(IAccountManager accounts, IHookBus hooks) : IFilterService
{
    private const int QueryPage = 500;
    private const int MaxScanned = 20_000;
    private const int ApplyChunk = 100;

    /// <inheritdoc />
    public event EventHandler<FilterChangedEventArgs>? Changed;

    /// <inheritdoc />
    public Task<Result<FilterState>> LoadAsync(string accountKey, CancellationToken ct) => Task.Run(() => LoadCoreAsync(accountKey, ct), ct);

    /// <inheritdoc />
    public Task<Result<FilterState>> SaveAsync(string accountKey, IReadOnlyList<JsonNode> rules, string? expectedState, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return Task.Run(() => SaveCoreAsync(accountKey, rules, expectedState, ct), ct);
    }

    /// <inheritdoc />
    public Task<Result<FilterScan>> ScanAsync(string accountKey, JsonNode rule, long generation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Task.Run(() => ScanCoreAsync(accountKey, rule, generation, ct), ct);
    }

    /// <inheritdoc />
    public Task<Result<FilterApplied>> ApplyAsync(string accountKey, FilterScan scan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return Task.Run(() => ApplyCoreAsync(accountKey, scan, ct), ct);
    }

    /// <summary>What changed between two lists of rules: added, updated, toggled, deleted, and reordered (one entry for the first rule whose place changed).</summary>
    internal static IReadOnlyList<(string Id, string Change)> Changes(IReadOnlyList<JsonNode> before, IReadOnlyList<JsonNode> after)
    {
        var old = before.Where(r => FilterRules.Str(r, "id") is not null).ToDictionary(r => FilterRules.Str(r, "id")!, StringComparer.Ordinal);
        var changes = new List<(string, string)>();
        foreach (var rule in after)
        {
            var id = FilterRules.Str(rule, "id");
            if (id is null)
            {
                continue;
            }

            if (!old.TryGetValue(id, out var was))
            {
                changes.Add((id, "added"));
            }
            else if (!JsonNode.DeepEquals(was, rule))
            {
                changes.Add((id, JsonNode.DeepEquals(was["enabled"], rule["enabled"]) ? "updated" : "toggled"));
            }
        }

        var kept = after.Select(r => FilterRules.Str(r, "id")).OfType<string>().Where(old.ContainsKey).ToList();
        var keptBefore = before.Select(r => FilterRules.Str(r, "id")).OfType<string>().Where(i => kept.Contains(i, StringComparer.Ordinal)).ToList();
        var moved = kept.Where((id, i) => !string.Equals(id, keptBefore[i], StringComparison.Ordinal)).FirstOrDefault();
        if (moved is not null)
        {
            changes.Add((moved, "reordered"));
        }

        var remaining = after.Select(r => FilterRules.Str(r, "id")).ToHashSet(StringComparer.Ordinal);
        changes.AddRange(before.Select(r => FilterRules.Str(r, "id")).OfType<string>().Where(i => !remaining.Contains(i)).Select(i => (i, "deleted")));
        return changes;
    }

    private static FilterState StateOf(SieveScriptState read)
    {
        var parsed = SieveScript.Parse(read.Text);
        var rules = (parsed.Metadata["rules"] as JsonArray ?? []).OfType<JsonNode>().Select(r => r.DeepClone()).ToList();
        return new FilterState(rules, parsed.External, parsed.Locked, read.Extensions, read.Extensions.Contains("reject", StringComparer.Ordinal)) { State = read.State };
    }

    private static async Task<Result<IReadOnlyList<string>>> QueryAllAsync(IJmapClient client, string account, JsonNode filter, CancellationToken ct)
    {
        var all = new List<string>();
        while (all.Count < MaxScanned)
        {
            var args = new JsonObject { ["accountId"] = account, ["filter"] = filter.DeepClone(), ["position"] = all.Count, ["limit"] = QueryPage };
            var q = await ReaderCalls.CallAsync(client, "Email/query", args, ct).ConfigureAwait(false);
            if (!q.IsOk)
            {
                return Result<IReadOnlyList<string>>.Fail(q.Error!);
            }

            var page = ReaderCalls.Ids(q.Value!);
            all.AddRange(page);
            if (page.Count < QueryPage)
            {
                break;
            }
        }

        return Result<IReadOnlyList<string>>.Ok(all);
    }

    private async Task<Result<FilterState>> LoadCoreAsync(string accountKey, CancellationToken ct)
    {
        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<FilterState>.Fail(client.Error!);
        }

        var read = await new SieveScriptStore(client.Value!).ReadAsync(ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<FilterState>.Fail(read.Error!);
        }

        var state = StateOf(read.Value!);
        hooks.Fire(HookNames.OnFiltersLoaded, () => new FilterRulesPayload(accountKey, state.Rules));
        return Result<FilterState>.Ok(state);
    }

    private async Task<Result<FilterState>> SaveCoreAsync(string accountKey, IReadOnlyList<JsonNode> rules, string? expectedState, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(expectedState))
            return Result<FilterState>.Fail(new ValidationError("The filter state is unavailable; reload before saving."));
        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<FilterState>.Fail(client.Error!);
        }

        var store = new SieveScriptStore(client.Value!);
        var read = await store.ReadAsync(ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<FilterState>.Fail(read.Error!);
        }

        var current = read.Value!;
        if (!string.Equals(current.State, expectedState, StringComparison.Ordinal))
            return Result<FilterState>.Fail(MethodErrors.From("SieveScript/set", new JsonObject { ["type"] = "stateMismatch" }));
        var parsed = SieveScript.Parse(current.Text);
        if (parsed.Locked)
        {
            return Result<FilterState>.Fail(new ValidationError("The filter script was edited by hand, so it cannot be changed here. Edit it in the Sieve view or reset it."));
        }

        if (!await hooks.AllowAsync(HookNames.OnBeforeFiltersSave, () => new FilterRulesPayload(accountKey, rules), ct).ConfigureAwait(false))
        {
            return Result<FilterState>.Fail(FilterAccess.StoppedByPlugin);
        }

        var script = SieveScript.GenerateMeta(SieveScript.WithRules(parsed.Metadata, rules), parsed.External, current.Extensions);
        var shaped = await hooks.ShapeAsync(HookNames.OnSieveScriptGenerate, new FilterScriptPayload(script, accountKey, rules.Count), ct).ConfigureAwait(false);
        var saved = await store.SaveWithStateAsync(shaped.Script, current, ct).ConfigureAwait(false);
        if (!saved.IsOk)
        {
            // A verification failure can follow a successful write; the error tells the caller to reload.
            return Result<FilterState>.Fail(saved.Error!);
        }

        var before = (parsed.Metadata["rules"] as JsonArray ?? []).OfType<JsonNode>().ToList();
        var state = new FilterState([.. rules.Select(r => r.DeepClone())], parsed.External, false, current.Extensions, current.Extensions.Contains("reject", StringComparer.Ordinal)) { State = saved.Value };
        foreach (var (id, change) in Changes(before, rules))
        {
            hooks.Fire(HookNames.OnFilterRuleChange, () => new FilterRuleChangePayload(id, change));
        }

        hooks.Fire(HookNames.OnFiltersSave, () => new FilterAccountPayload(accountKey));
        hooks.Fire(HookNames.OnSieveScriptChange, () => new FilterAccountPayload(accountKey));
        Changed?.Invoke(this, new FilterChangedEventArgs(accountKey));
        return Result<FilterState>.Ok(state);
    }

    private async Task<Result<FilterScan>> ScanCoreAsync(string accountKey, JsonNode rule, long generation, CancellationToken ct)
    {
        if (FilterRules.RetroUnsupported(rule) is { } why)
        {
            return Result<FilterScan>.Ok(new FilterScan(rule, [], 0, why, generation, string.Empty));
        }

        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<FilterScan>.Fail(client.Error!);
        }

        var http = client.Value!;
        var account = http.Session.MailAccountId;

        // the Email state before looking: Apply sends it as ifInState, so nothing changed in between
        var st = await ReaderCalls.CallAsync(http, "Email/get", new JsonObject { ["accountId"] = account, ["ids"] = new JsonArray() }, ct).ConfigureAwait(false);
        var boxes = st.IsOk ? await FilterAccess.MailboxesAsync(http, ct).ConfigureAwait(false) : Result<IReadOnlyList<JsonNode>>.Fail(st.Error!);
        if (!boxes.IsOk)
        {
            return Result<FilterScan>.Fail(boxes.Error!);
        }

        if (boxes.Value!.FirstOrDefault(b => string.Equals(FilterRules.Str(b, "role"), "inbox", StringComparison.Ordinal)) is not { } inbox)
        {
            return Result<FilterScan>.Fail(new ValidationError("No Inbox."));
        }

        var ids = await QueryAllAsync(http, account, FilterRules.QueryFilter(rule, FilterRules.Str(inbox, "id")!), ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<FilterScan>.Fail(ids.Error!);
        }

        var props = FilterRules.RetroProperties(rule).Append("id").ToList();
        var messages = new List<JsonNode>();
        foreach (var chunk in ids.Value!.Chunk(Math.Clamp(http.Session.Limits.MaxObjectsInGet, 1, ApplyChunk)))
        {
            var args = new JsonObject
            {
                ["accountId"] = account,
                ["ids"] = new JsonArray([.. chunk.Select(i => (JsonNode?)JsonValue.Create(i))]),
                ["properties"] = new JsonArray([.. props.Select(p => (JsonNode?)JsonValue.Create(p))]),
            };
            var got = await ReaderCalls.CallAsync(http, "Email/get", args, ct).ConfigureAwait(false);
            if (!got.IsOk)
            {
                return Result<FilterScan>.Fail(got.Error!);
            }

            messages.AddRange((got.Value!["list"] as JsonArray ?? []).OfType<JsonNode>().Select(FilterRules.RetroMessage));
        }

        var steps = FilterRules.Plan(rule, messages);
        return Result<FilterScan>.Ok(new FilterScan(rule, steps, FilterRules.PlanCount(steps), null, generation, st.Value!.Str("state") ?? string.Empty));
    }

    private async Task<Result<FilterApplied>> ApplyCoreAsync(string accountKey, FilterScan scan, CancellationToken ct)
    {
        var client = FilterAccess.ClientFor(accounts, accountKey);
        if (!client.IsOk)
        {
            return Result<FilterApplied>.Fail(client.Error!);
        }

        if (scan.Unsupported is not null || scan.Steps.Count == 0)
        {
            return Result<FilterApplied>.Ok(new FilterApplied(FilterAppliedKind.Done, 0, null));
        }

        var http = client.Value!;
        async Task<Result<JsonArray>> SendAsync(JsonArray call)
        {
            var r = await http.SendAsync(JmapRequest.Of(call), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<JsonArray>.Fail(r.Error!);
            }

            return r.Value!.MethodResponses.Count > 0 ? Result<JsonArray>.Ok(r.Value.MethodResponses[0]) : Result<JsonArray>.Fail(new ValidationError("The server did not answer."));
        }

        var max = Math.Clamp(http.Session.Limits.MaxObjectsInSet, 1, ApplyChunk);
        var applied = await FilterRules.ApplyAsync(SendAsync, http.Session.MailAccountId, scan.State, scan.Steps, max, ct).ConfigureAwait(false);
        return Result<FilterApplied>.Ok(applied);
    }
}
