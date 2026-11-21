// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:46-49 doTemplatesList (templates:read, host-api.ts:323): the active account's own templates as
// [{ id, name, category, subject, body, isHTML }], read through the same ITemplateAdmin the Templates settings page uses (Inbox metadata, shared with Bulwark).
// Deviation: Bulwark returns none when the administrator hid personal templates (policy `personalTemplates`); Barbican has no such policy field yet.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Plugins.Api.Templates;

internal sealed class PluginTemplatesList(ITemplateAdmin admin) : PluginScopedMethod("templates.list", "templates:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var list = await admin.ListAsync(capture!.LocalAccountKey, ct).ConfigureAwait(false);
        if (!list.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. list.Value!.Select(t => PluginTemplateFields.Project(b, t))]));
    }
}
