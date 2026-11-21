// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:305,1973-1982,2412-2415 doSieveRegenerate (filters:write): re-generates and re-uploads the account's active Sieve script from the filters through the filter store, which runs the
// onSieveScriptGenerate transform, "the supported way for a plugin to install or update its managed section without clobbering user filters". The add-on never supplies script text. Through IFilterService (LoadAsync, then SaveAsync with
// the state it loaded: its onBeforeFiltersSave, onSieveScriptGenerate, onFiltersSave and onSieveScriptChange hooks run exactly as for a save in Settings > Filters). No filters on the server or a script edited by hand (Locked: a save
// is refused) is Unavailable; a script that changed in between is a Conflict. Resolves undefined. Deviation: 10 regenerations a minute for each add-on (Bulwark has none; each is an upload, a validation and a set).
// Stalwart crates/jmap/src/sieve/set.rs.
using Barbican.Core.Filters.Rules;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginSieveRegenerate(IFilterService filters, IPluginMailRateLimiter limiter)
    : PluginScopedMethod("sieve.regenerate", "filters:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "sieve.write";

    protected override int RateLimit => 10;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (!PluginRulesAccess.SieveAvailable(capture!.Session)) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var loaded = await filters.LoadAsync(capture.LocalAccountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (loaded.Value!.Locked) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var saved = await filters.SaveAsync(capture.LocalAccountKey, loaded.Value.Rules, loaded.Value.State, ct).ConfigureAwait(false);
        if (!saved.IsOk) return PluginRulesAccess.Fail(saved.Error);
        var b = new PluginValueBuilder();
        return Success(b, b.Undefined());
    }
}
