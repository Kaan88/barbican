// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:209-229 doEmailGetAttachment (emailId, blobId, optional accountId; the blob has to be an attachment of that message, "a plugin can't use this to read arbitrary blobs";
// 25 MB at most; name, type, size and the bytes) and host-api.ts:341 (email:blob-read). A message of another account is read only through the login (a shared account it shows), as email.get reads it.
// Deviation: a message or blob that is not there, or an account this login does not show, is Unavailable (Bulwark rejects with a message that names it).
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailGetAttachment(PluginMailActionHost host) : PluginMailActionMethod("email.getAttachment", "email:blob-read", host, false, slow: true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginMailArgs.IsId(PluginMailArgs.At(arguments, 1)) && PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 2), out _)
            ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session;
        _ = PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 2), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return Fail(PluginErrorCode.Unavailable);
        var blobId = arguments.Item(1).StringValue;
        var found = await Host.Readers.Reader(session, route.SharedAccountId).GetBodyAsync(arguments.Item(0).StringValue, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!found.IsOk) return Fail(Code(found.Error));
        var part = found.Value!.Attachments.FirstOrDefault(a => string.Equals(a.BlobId, blobId, StringComparison.Ordinal));
        if (part is null) return Fail(PluginErrorCode.Unavailable);
        if (part.Size > PluginMailBlobs.MaxFetchBytes) return Fail(PluginErrorCode.Failed);
        var name = string.IsNullOrEmpty(part.Name) ? "attachment" : part.Name;
        var type = string.IsNullOrEmpty(part.Type) ? "application/octet-stream" : part.Type;
        var data = await PluginMailBlobs.ReadAsync(session, route.AccountId, blobId, name, type, PluginMailBlobs.MaxFetchBytes, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!data.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        return Success(b, b.Object(("name", b.String(name)), ("type", b.String(type)), ("size", b.Number(data.Value!.Length)), ("data", b.Buffer(data.Value))));
    }
}
