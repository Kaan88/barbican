// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.filters.ts:231-246 doFiltersUndo (filters:write, c.ts:29,90) over c.undo.ts:46-65 withUndoToken (only the add-on that got the token, in the same login, for the same kind, within 30 minutes;
// one undo at a time per token; spent only when the undo went through). Bulwark gives the script its earlier bytes back and refuses with "the filters changed since" when the script is not what its own save wrote. Barbican keeps
// the id of the rule and the script state its save produced: when the script is still at that state the rule is taken out through IFilterService.SaveAsync (the same hooks as any filter save); when anything changed since
// (another add-on's rule, a Settings edit, a hand edit) the undo is a Conflict, nothing is written and the token stays. An unknown, spent, expired or foreign token is Unavailable. Resolves undefined.
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginFiltersUndo(IFilterService filters, PluginUndoTokens undo, IPluginMailRateLimiter limiter)
    : PluginScopedMethod("filters.undo", "filters:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "filters.write";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (!PluginRulesAccess.SieveAvailable(capture!.Session)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var claim = undo.Begin(arguments.Item(0).StringValue, context.Binding.Package.Id, capture.LocalAccountKey, PluginRulesAccess.UndoKind);
        if (claim is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        try
        {
            var loaded = await filters.LoadAsync(capture.LocalAccountKey, ct).ConfigureAwait(false);
            if (!loaded.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
            var state = loaded.Value!;
            var ruleId = claim.Data.Str("ruleId");
            if (state.Locked || !string.Equals(state.State, claim.Data.Str("state"), StringComparison.Ordinal)
                || !state.Rules.Any(r => string.Equals(r.Str("id"), ruleId, StringComparison.Ordinal)))
            {
                return PluginCallOutcome.Failed(PluginErrorCode.Conflict);
            }

            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
            if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
            var saved = await filters.SaveAsync(capture.LocalAccountKey, [.. state.Rules.Where(r => !string.Equals(r.Str("id"), ruleId, StringComparison.Ordinal))], state.State, ct).ConfigureAwait(false);
            if (!saved.IsOk) return PluginRulesAccess.Fail(saved.Error);
            claim.Spend();
            claim = null;
            var b = new PluginValueBuilder();
            return Success(b, b.Undefined());
        }
        finally
        {
            claim?.Release();
        }
    }
}
