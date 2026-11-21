// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:19-62 readMessage (to, cc and bcc of at most 100 addresses in all; a subject of one line up to 998 characters; a body up to 512 KB, plain text, or HTML when isHtml is true,
// sanitised by sanitizePluginBodyHtml and turned to text by htmlToPlainText; at most 20 attachment references { blobId, name, type, size }) and host-api-input.ts:156-182 readBlobRefs. The sanitiser is the one composer.replaceBody uses
// (PluginBodyHtml). Unknown properties are refused; wrong types are not coerced.
using System.Globalization;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal static class PluginComposeInput
{
    internal const int MaxRecipients = 100;
    internal const int MaxAttachments = 20;
    internal const int MaxSubject = 998;
    internal const int MaxBody = 512 * 1024;
    private const long MaxAttachmentSize = 1L << 31;
    // The body is the largest argument (host-api-compose.ts:26 MAX_BODY characters); the recipient,
    // subject and blob fields add only a few KB on top of its two bytes per UTF-16 unit.
    internal static readonly PluginValueLimits Limits = new(
        MaxWireBytes: (4 * MaxBody) + (1024 * 1024), MaxStringBytes: (2 * MaxBody) + (64 * 1024));
    private static readonly string[] Allowed = ["to", "cc", "bcc", "subject", "body", "isHtml", "attachments"];
    private static readonly string[] AllowedBlob = ["blobId", "name", "type", "size"];

    /// <summary>The message object (optional: no argument is an empty message), checked and cleaned; false when anything is wrong.</summary>
    internal static bool TryRead(PluginValue? value, out PluginComposeMessage message)
    {
        message = new([], [], [], "", "", null, []);
        if (!PluginMailArgs.TryOptions(value, Allowed, false, out var data)) return false;
        if (!PluginMailActionArgs.TryAddressList(PluginMailArgs.Prop(data, "to"), MaxRecipients, out var to)
            || !PluginMailActionArgs.TryAddressList(PluginMailArgs.Prop(data, "cc"), MaxRecipients, out var cc)
            || !PluginMailActionArgs.TryAddressList(PluginMailArgs.Prop(data, "bcc"), MaxRecipients, out var bcc)
            || to.Count + cc.Count + bcc.Count > MaxRecipients)
        {
            return false;
        }

        var subject = "";
        if (PluginMailArgs.Prop(data, "subject") is { } rawSubject)
        {
            if (!PluginMailArgs.TryString(rawSubject, MaxSubject, out subject) || subject.Any(c => c < ' ' || c == '\u007f')) return false;
        }

        var body = "";
        if (PluginMailArgs.Prop(data, "body") is { } rawBody && !PluginMailArgs.TryString(rawBody, MaxBody, out body)) return false;
        var isHtml = false;
        if (PluginMailArgs.Prop(data, "isHtml") is { } rawHtml)
        {
            if (rawHtml.Kind != PluginValueKind.Boolean) return false;
            isHtml = rawHtml.BooleanValue;
        }

        if (!TryBlobs(PluginMailArgs.Prop(data, "attachments"), out var attachments)) return false;
        var html = isHtml ? PluginBodyHtml.Sanitize(body) : null;
        message = new(to, cc, bcc, subject, html is null ? body : DocHtml.HtmlText(html), html, attachments);
        return true;
    }

    private static bool TryBlobs(PluginValue? value, out IReadOnlyList<PluginBlobRef> refs)
    {
        refs = [];
        if (value is null) return true;
        if (value.Kind != PluginValueKind.Array || value.Count > MaxAttachments) return false;
        var list = new List<PluginBlobRef>();
        for (var i = 0; i < value.Count; i++)
        {
            if (!PluginMailArgs.TryOptions(value.Item(i), AllowedBlob, true, out var entry) || entry is null) return false;
            var blobId = PluginMailArgs.Prop(entry, "blobId");
            if (!PluginMailArgs.IsId(blobId) || !PluginMailArgs.TryString(PluginMailArgs.Prop(entry, "type"), 255, out var type) || !PluginMailActionArgs.IsMediaType(type)) return false;
            if (!PluginMailArgs.TryString(PluginMailArgs.Prop(entry, "name"), 255, out var rawName)) return false;
            var name = new string([.. rawName.Where(c => c >= ' ' && c != '\u007f')]);
            if (name.Length == 0 || string.IsNullOrWhiteSpace(name)) return false;
            var size = PluginMailArgs.Prop(entry, "size");
            if (size is not { Kind: PluginValueKind.Number } || !double.TryParse(size.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var bytes)
                || bytes != Math.Floor(bytes) || bytes < 0 || bytes > MaxAttachmentSize)
            {
                return false;
            }

            list.Add(new(blobId!.StringValue, name, type, (long)bytes));
        }

        refs = list;
        return true;
    }
}
