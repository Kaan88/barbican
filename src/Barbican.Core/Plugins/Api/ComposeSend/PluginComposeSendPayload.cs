// Mirrors Bulwark components/email/email-composer.tsx:4652-4671 and lib/plugin-sandbox/runtime.tsx hook arguments.
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.ComposeSend;

/// <summary>Projects a prepared takeover request into the SDK's single-argument graph.</summary>
public static class PluginComposeSendPayload
{
    private const int MaxRecipients = 200;
    private const int MaxAttachments = 200;
    private const int MaxBody = 16 * 1024 * 1024;
    private const long MaxExactInteger = 9_007_199_254_740_991;
    private static readonly PluginValueLimits Limits = new(MaxWireBytes: 40 * 1024 * 1024, MaxStringBytes: 50 * 1024 * 1024);

    /// <summary>Encodes the exact request; invalid or oversized values throw before crossing into a sandbox.</summary>
    public static PluginValue Encode(PluginComposeSendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var b = new PluginValueBuilder();
        int Optional(string? value) => value is null ? b.Undefined() : b.String(value);
        int Strings(IReadOnlyList<string> values) => b.Array([.. values.Select(b.String)]);
        var attachments = b.Array([.. request.Attachments.Select(a => Attachment(b, a))]);
        var record = b.Object(
            ("to", Strings(request.To)), ("cc", Strings(request.Cc)), ("bcc", Strings(request.Bcc)),
            ("subject", b.String(request.Subject)), ("htmlBody", b.String(request.HtmlBody)), ("textBody", b.String(request.TextBody)),
            ("identityId", b.String(request.IdentityId)), ("fromEmail", b.String(request.FromEmail)), ("fromName", b.String(request.FromName)),
            ("inReplyTo", Optional(request.InReplyTo)), ("references", request.References is null ? b.Undefined() : Strings(request.References)),
            ("delayedUntil", Optional(request.DelayedUntil)), ("labelToken", Optional(request.LabelToken)), ("attachments", attachments));
        return b.Build(b.Array(record), Limits);
    }

    private static int Attachment(PluginValueBuilder b, DraftAttachment a) => b.Object(
        ("name", b.String(a.Name)), ("type", b.String(a.Type)), ("size", b.Number(a.Size)),
        ("blobId", b.String(a.BlobId!)), ("cid", a.Cid is null ? b.Undefined() : b.String(a.Cid)));

    private static void Validate(PluginComposeSendRequest request)
    {
        if (request.To is null || request.Cc is null || request.Bcc is null || request.Attachments is null ||
            request.To.Count + (long)request.Cc.Count + request.Bcc.Count > MaxRecipients || request.Attachments.Count > MaxAttachments ||
            !ValidList(request.To, 4096) || !ValidList(request.Cc, 4096) || !ValidList(request.Bcc, 4096) ||
            !Valid(request.Subject, 998) || !Valid(request.HtmlBody, MaxBody) || !Valid(request.TextBody, MaxBody) ||
            !Valid(request.IdentityId, 256) || !Valid(request.FromEmail, 4096) || !Valid(request.FromName, 4096) ||
            !OptionalValid(request.InReplyTo, 4096) || !OptionalValid(request.DelayedUntil, 256) || !OptionalValid(request.LabelToken, 256) ||
            (request.References is { } references && (references.Count > 200 || !ValidList(references, 4096))) ||
            request.Attachments.Any(a => a is null || !ValidAttachment(a)))
            throw new PluginValueFormatException("Invalid or oversized composer takeover payload.");
    }
    private static bool ValidAttachment(DraftAttachment a) => Valid(a.Name, 4096) && Valid(a.Type, 1024) &&
        !string.IsNullOrWhiteSpace(a.BlobId) && Valid(a.BlobId, 4096) && OptionalValid(a.Cid, 4096) && a.Size is >= 0 and <= MaxExactInteger;
    private static bool ValidList(IReadOnlyList<string> list, int max) => list.All(value => Valid(value, max));
    private static bool OptionalValid(string? value, int max) => value is null || Valid(value, max);
    private static bool Valid(string? value, int max) => value is not null && value.Length <= max && !value.Contains('\0', StringComparison.Ordinal);
}
