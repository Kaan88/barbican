// Mirrors: Bulwark lib/plugin-types.ts:1276-1359 and lib/plugin-sandbox/hook-payload.ts:81-105.
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.App.Plugins;

/// <summary>Projects outgoing mail into SDK values and accepts only supported, bounded changes.</summary>
internal static class PluginOutgoingPayload
{
    private const int MaxSubject = 998;
    private const int MaxBody = 16 * 1024 * 1024;
    private const int MaxRecipient = 4096;
    private const int MaxRecipients = 200;

    private static readonly PluginValueLimits SendLimits = new(MaxWireBytes: 40 * 1024 * 1024, MaxStringBytes: 50 * 1024 * 1024);

    internal static PluginValue? TryEncode(OutgoingEmailPayload payload, bool beforeSend)
    {
        try { return Encode(payload, beforeSend); }
        catch (PluginValueFormatException) { return null; }
    }

    private static PluginValue Encode(OutgoingEmailPayload payload, bool beforeSend)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var b = new PluginValueBuilder();
        int Strings(IEnumerable<string> values) => b.Array([.. values.Select(b.String)]);
        int Recipients(string value) => Strings(AddressParser.Parse(value).Select(a => a.Name is null ? a.Email : "\"" + a.Name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\" <" + a.Email + ">"));
        var attachments = payload.Attachments.Select(a =>
        {
            var fields = new List<(string, int)> { ("name", b.String(a.Name)), ("type", b.String(a.Type)), ("size", b.Number(a.Size)) };
            if (beforeSend && a.BlobId is not null) fields.Add(("blobId", b.String(a.BlobId)));
            if (beforeSend && a.Cid is not null) fields.Add(("inline", b.Boolean(true)));
            return b.Object([.. fields]);
        }).ToArray();
        var properties = new List<(string, int)>
        {
            ("to", Recipients(payload.To)), ("cc", Recipients(payload.Cc)), ("bcc", Recipients(payload.Bcc)),
            ("subject", b.String(payload.Subject)), ("htmlBody", b.String(payload.Html)), ("textBody", b.String(payload.Text)),
            ("identityId", b.String(payload.IdentityId)), ("fromEmail", b.String(payload.From)), ("attachments", b.Array(attachments)),
        };
        if (payload.InReplyTo is not null) properties.Add(("inReplyTo", b.String(payload.InReplyTo)));
        if (payload.ComposerId is not null) properties.Add(("composerId", b.String(payload.ComposerId)));
        if (beforeSend)
        {
            properties.Add(("surface", b.String(payload.IsQuickReply ? "quick-reply" : "composer")));
            properties.Add(("mode", b.String(payload.Kind switch { ComposeKind.Reply => "reply", ComposeKind.ReplyAll => "replyAll", ComposeKind.Forward or ComposeKind.ForwardAsAttachment => "forward", _ => "compose" })));
            if (payload.AuthoredText is not null) properties.Add(("authoredText", b.String(payload.AuthoredText)));
            properties.Add(("externalRecipients", Strings(payload.ExternalRecipients)));
        }
        else properties.Add(("keywords", Strings(payload.Keywords)));
        return b.Build(b.Array(b.Object([.. properties])), SendLimits);
    }

    internal static OutgoingEmailPayload Mediate(OutgoingEmailPayload current, PluginValue result, bool permitKeywords)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Kind != PluginValueKind.Object ||
            !TryRecipients(result, "to", out var to) || !TryRecipients(result, "cc", out var cc) || !TryRecipients(result, "bcc", out var bcc) ||
            !TryText(result, "subject", MaxSubject, out var subject) || subject.Contains('\r', StringComparison.Ordinal) || subject.Contains('\n', StringComparison.Ordinal) ||
            !TryText(result, "htmlBody", MaxBody, out var html) || !TryText(result, "textBody", MaxBody, out var text)) return current;
        if (AddressParser.Parse(to).Count + AddressParser.Parse(cc).Count + AddressParser.Parse(bcc).Count > MaxRecipients)
            return current;
        var keywords = current.Keywords;
        if (permitKeywords && result.PropertyNames.Contains("keywords", StringComparer.Ordinal))
        {
            var value = result.Property("keywords");
            var items = new List<string>();
            if (value.Kind == PluginValueKind.Array)
            {
                for (var i = 0; i < value.Count; i++) if (value.Item(i).Kind == PluginValueKind.String) items.Add(value.Item(i).StringValue);
            }
            keywords = OutgoingKeywords.Clean(items);
        }
        if (string.Equals(to, current.To, StringComparison.Ordinal) && string.Equals(cc, current.Cc, StringComparison.Ordinal) &&
            string.Equals(bcc, current.Bcc, StringComparison.Ordinal) && string.Equals(subject, current.Subject, StringComparison.Ordinal) &&
            string.Equals(html, current.Html, StringComparison.Ordinal) && string.Equals(text, current.Text, StringComparison.Ordinal) &&
            keywords.SequenceEqual(current.Keywords, StringComparer.Ordinal)) return current;
        return current with { To = to, Cc = cc, Bcc = bcc, Subject = subject, Html = html, Text = text, Keywords = keywords };
    }

    private static bool TryText(PluginValue value, string name, int max, out string text)
    {
        text = string.Empty;
        if (!value.PropertyNames.Contains(name, StringComparer.Ordinal)) return false;
        var property = value.Property(name);
        if (property.Kind != PluginValueKind.String || property.StringValue.Length > max || property.StringValue.Contains('\0', StringComparison.Ordinal)) return false;
        text = property.StringValue;
        return true;
    }

    private static bool TryRecipients(PluginValue value, string name, out string result)
    {
        result = string.Empty;
        if (!value.PropertyNames.Contains(name, StringComparer.Ordinal)) return false;
        var array = value.Property(name);
        if (array.Kind != PluginValueKind.Array || array.Count > MaxRecipients) return false;
        var recipients = new List<string>();
        for (var i = 0; i < array.Count; i++)
        {
            var item = array.Item(i);
            if (item.Kind != PluginValueKind.String || item.StringValue.Length > MaxRecipient ||
                item.StringValue.IndexOfAny(['\r', '\n', '\0']) >= 0 || AddressParser.Parse(item.StringValue).Count != 1 ||
                RecipientText.InvalidAddresses(RecipientText.ParseList(item.StringValue)).Count > 0) return false;
            recipients.Add(item.StringValue);
        }
        result = string.Join(", ", recipients);
        return result.Length <= MaxRecipient;
    }
}
