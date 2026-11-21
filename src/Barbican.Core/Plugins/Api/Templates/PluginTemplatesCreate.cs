// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:74-94 doTemplatesCreate (templates:write, host-api.ts:325): name required, at most 500 templates, an HTML
// body cleaned first; returns the new template. Created through ITemplateAdmin (the Templates settings page's service: Inbox metadata with ifInState, and the
// onTemplateCreate hook). ITemplateAdmin.CreateAsync takes no category, so a category is set with a second ITemplateAdmin.UpdateAsync; if that fails the template
// exists without it and the call reports UnknownOutcome. Deviation: 30 writes a minute for each add-on (Bulwark has no limit; each write is a server call).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Plugins.Api.Templates;

internal sealed class PluginTemplatesCreate(ITemplateAdmin admin, IPluginMailRateLimiter limiter) : PluginScopedMethod("templates.create", "templates:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "templates.write";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginTemplateFields.TryParse(PluginMailArgs.At(arguments, 0), nameRequired: true, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginTemplateFields.TryParse(PluginMailArgs.At(arguments, 0), nameRequired: true, out var fields);
        var key = capture!.LocalAccountKey;
        var existing = await admin.ListAsync(key, ct).ConfigureAwait(false);
        if (!existing.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (existing.Value!.Count >= PluginTemplateFields.MaxTemplates) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        var isHtml = fields.IsHtml ?? false;
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var created = await admin.CreateAsync(key, fields.Name!, fields.Subject ?? "", PluginTemplateFields.Clean(fields.Body ?? "", isHtml), isHtml, ct).ConfigureAwait(false);
        if (!created.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        var template = created.Value!;
        if (!string.IsNullOrEmpty(fields.Category))
        {
            var categorised = await admin.UpdateAsync(key, template with { Category = fields.Category }, ct).ConfigureAwait(false);
            if (!categorised.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.UnknownOutcome);
            template = categorised.Value!;
        }

        var b = new PluginValueBuilder();
        return Success(b, PluginTemplateFields.Project(b, template));
    }
}
