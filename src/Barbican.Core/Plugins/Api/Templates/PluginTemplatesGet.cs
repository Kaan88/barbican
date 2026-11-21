// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:51-56 doTemplatesGet (templates:read, host-api.ts:324): one template by id, or null when there is none.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Plugins.Api.Templates;

internal sealed class PluginTemplatesGet(ITemplateAdmin admin) : PluginScopedMethod("templates.get", "templates:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var list = await admin.ListAsync(capture!.LocalAccountKey, ct).ConfigureAwait(false);
        if (!list.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var id = arguments.Item(0).StringValue;
        var found = list.Value!.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        var b = new PluginValueBuilder();
        return Success(b, found is null ? b.Null() : PluginTemplateFields.Project(b, found));
    }
}
