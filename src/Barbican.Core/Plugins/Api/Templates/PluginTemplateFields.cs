// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:29-72 (MAX_PLUGIN_TEMPLATES 500, name 1-200 after trimming, subject at most 998, category at most 100 after
// trimming, body at most 100,000, isHTML a boolean, only the fields name, subject, body, isHTML, category) and lib/plugin-projection.ts:224-233 templateToView.
using Barbican.Core.Compose;
using Barbican.Core.Mail.Body;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Templates;

/// <summary>A validated template patch or definition; a null member was not given.</summary>
internal sealed record PluginTemplateFields(string? Name, string? Subject, string? Body, bool? IsHtml, string? Category)
{
    internal const int MaxTemplates = 500;
    internal const int MaxName = TemplateText.MaxNameLength;
    internal const int MaxCategory = 100;
    internal const int MaxSubject = 998;
    internal const int MaxBody = 100_000;
    private static readonly string[] Allowed = ["name", "subject", "body", "isHTML", "category"];

    internal bool IsEmpty => Name is null && Subject is null && Body is null && IsHtml is null && Category is null;

    internal static bool TryParse(PluginValue? value, bool nameRequired, out PluginTemplateFields fields)
    {
        fields = null!;
        if (value is null || value.Kind != PluginValueKind.Object || !PluginMailArgs.TryOptions(value, Allowed, required: true, out var o)) return false;
        string? name = null, subject = null, body = null, category = null;
        bool? html = null;
        if (PluginMailArgs.Prop(o, "name") is { } n)
        {
            if (!PluginMailArgs.TryString(n, int.MaxValue, out var raw) || raw.Trim() is not { Length: > 0 and <= MaxName } trimmed) return false;
            name = trimmed;
        }
        else if (nameRequired)
        {
            return false;
        }

        if (PluginMailArgs.Prop(o, "subject") is { } s)
        {
            if (!PluginMailArgs.TryString(s, MaxSubject, out var text)) return false;
            subject = text;
        }

        if (PluginMailArgs.Prop(o, "category") is { } c)
        {
            if (!PluginMailArgs.TryString(c, int.MaxValue, out var raw) || raw.Trim() is not { Length: <= MaxCategory } trimmed) return false;
            category = trimmed;
        }

        if (PluginMailArgs.Prop(o, "isHTML") is { } h)
        {
            if (h.Kind != PluginValueKind.Boolean) return false;
            html = h.BooleanValue;
        }

        if (PluginMailArgs.Prop(o, "body") is { } bd)
        {
            if (!PluginMailArgs.TryString(bd, MaxBody, out var text)) return false;
            body = text;
        }

        fields = new PluginTemplateFields(name, subject, body, html, category);
        return true;
    }

    /// <summary>An HTML body is cleaned like any add-on-supplied HTML before it is kept (Bulwark sanitizePluginBodyHtml); plain text is kept as given.</summary>
    internal static string Clean(string body, bool isHtml) => isHtml ? MailSanitizer.Clean(body, out _) : body;

    internal static int Project(PluginValueBuilder b, MailTemplate t) => b.Object(
        ("id", b.String(t.Id)), ("name", b.String(t.Name)), ("category", b.String(t.Category)), ("subject", b.String(t.Subject)),
        ("body", b.String(t.Body)), ("isHTML", b.Boolean(t.IsHtml)));
}
