// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:96-111 doTemplatesUpdate (templates:write, host-api.ts:326): a non-empty patch of name, subject, body, isHTML,
// category on an existing template; a body is cleaned when the template is or becomes HTML (turning an existing body into HTML cleans it too); returns the
// template. A missing id is Unavailable (Bulwark throws "no template"). Written through ITemplateAdmin.UpdateAsync (ifInState, onTemplateUpdate hook).
// Deviation: 30 writes a minute for each add-on, shared with templates.create and templates.delete.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Plugins.Api.Templates;

internal sealed class PluginTemplatesUpdate(ITemplateAdmin admin, IPluginMailRateLimiter limiter) : PluginScopedMethod("templates.update", "templates:write", mayMutate: true, limiter: limiter)
{
    protected override string? RateKind => "templates.write";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginTemplateFields.TryParse(PluginMailArgs.At(arguments, 1), nameRequired: false, out var fields) && !fields.IsEmpty
            ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginTemplateFields.TryParse(PluginMailArgs.At(arguments, 1), nameRequired: false, out var fields);
        var key = capture!.LocalAccountKey;
        var id = arguments.Item(0).StringValue;
        var list = await admin.ListAsync(key, ct).ConfigureAwait(false);
        if (!list.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var existing = list.Value!.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        if (existing is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var isHtml = fields.IsHtml ?? existing.IsHtml;
        var body = fields.Body is { } given ? PluginTemplateFields.Clean(given, isHtml)
            : isHtml && !existing.IsHtml ? PluginTemplateFields.Clean(existing.Body, true) : existing.Body;
        var edited = existing with
        {
            Name = fields.Name ?? existing.Name, Subject = fields.Subject ?? existing.Subject, Body = body, IsHtml = isHtml, Category = fields.Category ?? existing.Category,
        };
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var saved = await admin.UpdateAsync(key, edited, ct).ConfigureAwait(false);
        if (!saved.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        return Success(b, PluginTemplateFields.Project(b, saved.Value!));
    }
}
