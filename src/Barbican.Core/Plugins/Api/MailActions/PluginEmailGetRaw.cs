// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:232-248 doEmailGetRaw (emailId, optional accountId; the message as the server stores it, RFC 5322, 25 MB at most, an ArrayBuffer) and host-api.ts:342 (email:blob-read).
// Stalwart crates/jmap/src/email/get.rs (blobId, size), crates/jmap/src/blob/download.rs. Deviation: a missing message or an account this login does not show is Unavailable.
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailGetRaw(PluginMailActionHost host) : PluginMailActionMethod("email.getRaw", "email:blob-read", host, false, slow: true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 1), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session;
        _ = PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 1), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return Fail(PluginErrorCode.Unavailable);
        var args = new JsonObject { ["accountId"] = route.AccountId, ["ids"] = new JsonArray(arguments.Item(0).StringValue), ["properties"] = new JsonArray("blobId", "size") };
        var got = await ReaderCalls.CallAsync(session.Client, "Email/get", args, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!got.IsOk) return Fail(Code(got.Error));
        if ((got.Value!["list"] as JsonArray)?.FirstOrDefault() is not JsonObject email || email.Str("blobId") is not { Length: > 0 } blob) return Fail(PluginErrorCode.Unavailable);
        if (email["size"] is JsonValue size && size.TryGetValue<long>(out var bytes) && bytes > PluginMailBlobs.MaxFetchBytes) return Fail(PluginErrorCode.Failed);
        var data = await PluginMailBlobs.ReadAsync(session, route.AccountId, blob, "message.eml", "message/rfc822", PluginMailBlobs.MaxFetchBytes, ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        if (!data.IsOk) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        return Success(b, b.Buffer(data.Value!));
    }
}
