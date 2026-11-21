// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.filters.ts:172-229 doFiltersCreate (filters:write, c.ts:28,89): adds a rule at the top of the account's filters (quick-rules.ts:296 insertRuleAtTop) and resolves { id, undoToken }.
// The rule is checked like the rule editor checks one (PluginFilterRuleInput): no forward, redirect, reject, discard or keep. A move or copy goes only to a folder of the filters' own account that nobody else reads: the folder
// must exist, not be the Trash and not be shared with anyone (it or a folder above it, c.filters.ts:150-160 and mailbox-shared-out.ts), and an add_label names one of the user's tags. A script edited by hand is Unavailable
// ("add the rule in Settings > Filters"); another add-on's onBeforeFiltersSave stopping the save is a plain failure.
// The rule is saved through IFilterService.SaveAsync, the service Settings > Filters saves through (its onBeforeFiltersSave / onSieveScriptGenerate / onFiltersSave hooks run, the open page reloads on Changed) with the state
// it was loaded at, so a script that changed in between is a Conflict and nothing is written. The undo token (c.undo.ts, 30 minutes, 50 per add-on) keeps the rule id and the script state the save produced.
// Deviation: 30 writes a minute for each add-on (Bulwark has none; each is an upload, a validation and a set). Rule ids are Barbican's "bw-" ids, not UUIDs. Stalwart crates/jmap/src/sieve/set.rs.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginFiltersCreate(IFilterService filters, PluginTagDefinitions tags, PluginUndoTokens undo, IPluginMailRateLimiter limiter)
    : PluginScopedMethod("filters.create", "filters:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "filters.write";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginFilterRuleInput.TryRead(PluginMailArgs.At(arguments, 0)) is null ? PluginErrorCode.InvalidArguments : null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var input = PluginFilterRuleInput.TryRead(PluginMailArgs.At(arguments, 0))!;
        var session = capture!.Session;
        if (!PluginRulesAccess.SieveAvailable(session)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var boxes = await FilterAccess.MailboxesAsync(session.Client, ct).ConfigureAwait(false);
        if (!boxes.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var paths = FilterRules.FolderPaths(boxes.Value!).ToDictionary(f => f.Id, f => f.Path, StringComparer.Ordinal);
        var known = tags.Ids();
        foreach (var action in input.Actions)
        {
            if (action.TagId is { } tag && !known.Contains(tag, StringComparer.Ordinal)) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            if (action.MailboxId is not { } id) continue;
            var box = boxes.Value!.FirstOrDefault(m => string.Equals(m["id"]?.GetValue<string>(), id, StringComparison.Ordinal));
            if (box is null || string.Equals(box["role"]?.GetValue<string>(), "trash", StringComparison.Ordinal)) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
            // the share map is not in the folder list: ask the server for the folder and those above it, and refuse one someone else reads
            var shared = await PluginRulesAccess.IsSharedOutAsync(session.Client, boxes.Value!, id, ct).ConfigureAwait(false);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
            if (shared) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        }

        var loaded = await filters.LoadAsync(capture.LocalAccountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (loaded.Value!.Locked) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var rule = input.ToRule("bw-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6)), paths);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var saved = await filters.SaveAsync(capture.LocalAccountKey, [rule, .. loaded.Value.Rules], loaded.Value.State, ct).ConfigureAwait(false);
        if (!saved.IsOk) return PluginRulesAccess.Fail(saved.Error);
        var token = undo.Issue(context.Binding.Package.Id, capture.LocalAccountKey, PluginRulesAccess.UndoKind,
            new JsonObject { ["ruleId"] = rule["id"]!.GetValue<string>(), ["state"] = saved.Value!.State });
        var b = new PluginValueBuilder();
        return Success(b, b.Object(("id", b.String(rule["id"]!.GetValue<string>())), ("undoToken", b.String(token))));
    }
}
