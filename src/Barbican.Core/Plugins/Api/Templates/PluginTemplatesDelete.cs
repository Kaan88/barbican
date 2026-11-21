// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:113-121 doTemplatesDelete (templates:write, host-api.ts:327): removes a template by id, result undefined;
// a missing id is Unavailable (Bulwark throws "no template"). Deleted through ITemplateAdmin.DeleteAsync (tombstone with ifInState, onTemplateDelete hook).
// Deviation: 30 writes a minute for each add-on, shared with templates.create and templates.update.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Plugins.Api.Templates;

internal sealed class PluginTemplatesDelete(ITemplateAdmin admin, IPluginMailRateLimiter limiter) : PluginScopedMethod("templates.delete", "templates:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "templates.write";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var key = capture!.LocalAccountKey;
        var id = arguments.Item(0).StringValue;
        var list = await admin.ListAsync(key, ct).ConfigureAwait(false);
        if (!list.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!list.Value!.Any(t => string.Equals(t.Id, id, StringComparison.Ordinal))) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var done = await admin.DeleteAsync(key, id, ct).ConfigureAwait(false);
        return done.IsOk ? PluginCallOutcome.Succeeded() : PluginCallOutcome.Failed(PluginErrorCode.Failed);
    }
}
