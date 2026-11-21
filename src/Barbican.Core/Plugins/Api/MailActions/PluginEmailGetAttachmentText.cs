// Mirrors: Bulwark lib/plugin-sandbox/assistant/b.ts:59-98 doEmailGetAttachmentText (options maxChars 1 to 100 000, default 20 000, and accountId; the blob has to be an attachment of the message; at most 30 reads a minute;
// an attachment over 20 MB answers too_big without being read), b.types.ts:38-59 (name, type, size, format, status, text, truncated), lib/attachment-text/index.ts and plain.ts (the decoding, shared with files.getText).
// Deviation: as for files.getText, Barbican has no PDF, Office, HTML, calendar or address-card text reader, so those formats answer status "unsupported" (format named); only plain text and CSV give text.
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Api.Files;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailGetAttachmentText(PluginMailActionHost host) : PluginMailActionMethod("email.getAttachmentText", "email:blob-read", host, false, slow: true)
{
    private const int ReadsPerMinute = 30;
    private const long MaxSourceBytes = 20L * 1024 * 1024;
    private static readonly string[] Allowed = ["maxChars", "accountId"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryOptions(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    private static bool TryOptions(PluginValue arguments, out int maxChars, out string? account)
    {
        (maxChars, account) = (PluginFilesGetText.DefaultChars, null);
        if (!PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) || !PluginMailArgs.IsId(PluginMailArgs.At(arguments, 1))) return false;
        return PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 2), Allowed, false, out var options)
            && PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "maxChars"), 1, PluginFilesGetText.MaxChars, PluginFilesGetText.DefaultChars, out maxChars)
            && PluginMailArgs.TryAccount(PluginMailArgs.Prop(options, "accountId"), out account);
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (!TryOptions(arguments, out var maxChars, out var requested)) return Fail(PluginErrorCode.InvalidArguments);
        if (!Within(context, Descriptor.Name, ReadsPerMinute)) return Fail(PluginErrorCode.RateLimited);
        var session = capture!.Session;
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return Fail(PluginErrorCode.Unavailable);
        var blobId = arguments.Item(1).StringValue;
        var found = await Host.Readers.Reader(session, route.SharedAccountId).GetBodyAsync(arguments.Item(0).StringValue, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!found.IsOk) return Fail(Code(found.Error));
        var part = found.Value!.Attachments.FirstOrDefault(a => string.Equals(a.BlobId, blobId, StringComparison.Ordinal));
        if (part is null) return Fail(PluginErrorCode.Unavailable);
        var name = string.IsNullOrEmpty(part.Name) ? "attachment" : part.Name;
        var type = string.IsNullOrEmpty(part.Type) ? "application/octet-stream" : part.Type;
        var format = PluginFilesGetText.Format(name, type);
        var b = new PluginValueBuilder();
        if (part.Size > MaxSourceBytes) return Success(b, View(b, name, type, part.Size, format, "too_big", "", false));
        if (format is not ("text" or "csv")) return Success(b, View(b, name, type, part.Size, format, "unsupported", "", false));
        var data = await PluginMailBlobs.ReadAsync(session, route.AccountId, blobId, name, type, MaxSourceBytes, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!data.IsOk) return Fail(PluginErrorCode.Failed);
        var bytes = data.Value!;
        if (!PluginFilesGetText.LooksLikeText(bytes)) return Success(b, View(b, name, type, bytes.Length, format, "unsupported", "", false));
        var (text, truncated) = PluginFilesGetText.Clip(PluginFilesGetText.Decode(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), maxChars);
        return Success(b, View(b, name, type, bytes.Length, format, "ok", text, truncated));
    }

    private static int View(PluginValueBuilder b, string name, string type, long size, string format, string status, string text, bool truncated) => b.Object(
        ("name", b.String(name)), ("type", b.String(type)), ("size", b.Number(size)), ("format", b.String(format)), ("status", b.String(status)), ("text", b.String(text)), ("truncated", b.Boolean(truncated)));
}
