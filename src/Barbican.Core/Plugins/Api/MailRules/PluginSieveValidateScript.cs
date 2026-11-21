// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:304,2408-2411 (sieve.validateScript, filters:write; the content must be a string) over lib/jmap/client.ts:7693-7718 validateSieveScript: the text is uploaded as a blob and the server's
// SieveScript/validate answers; resolves { isValid: true } or { isValid: false, errors: [description] }. Nothing is saved or activated. RFC 9661 section 2.5; Stalwart crates/jmap/src/sieve/validate.rs.
// Deviation: 30 validations a minute for each add-on (Bulwark has none; each is a blob upload).
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Net;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Sieve;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginSieveValidateScript(IPluginMailRateLimiter limiter)
    : PluginScopedMethod("sieve.validateScript", "filters:write", limiter: limiter, argumentLimits: ScriptLimits)
{
    // host-api.ts:2408 takes any string and uploads it as a blob, so no smaller cap exists; the
    // bridge's ceiling for an add-on-supplied body is GuardedHttp.PluginBodyLimit, counted at two
    // bytes per UTF-16 unit on the graph.
    private static readonly PluginValueLimits ScriptLimits = new(
        MaxWireBytes: 4 * GuardedHttp.PluginBodyLimit, MaxStringBytes: 2 * GuardedHttp.PluginBodyLimit);

    protected override string? RateKind => "sieve.validate";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.At(arguments, 0) is { Kind: PluginValueKind.String } ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session;
        if (!PluginRulesAccess.SieveAvailable(session) || session.Client is not JmapClient http) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var account = http.Session.AccountFor(SieveScriptStore.Capability);
        var up = await http.UploadAsync(account, Encoding.UTF8.GetBytes(arguments.Item(0).StringValue), "application/sieve", ct).ConfigureAwait(false);
        if (!up.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        var sent = await session.Client.CallAsync(JmapRequest.Of(JmapRequest.Call("SieveScript/validate", new JsonObject { ["accountId"] = account, ["blobId"] = up.Value!.BlobId }, "v")), ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var b = new PluginValueBuilder();
        if (!sent.IsOk) return Success(b, Invalid(b, "Validation failed"));
        var answer = sent.Value!.MethodResponses[0][1] as JsonObject;
        return answer?["error"] is JsonObject error
            ? Success(b, Invalid(b, error.Str("description") is { Length: > 0 } text ? text : "Validation failed"))
            : Success(b, b.Object(("isValid", b.Boolean(true))));
    }

    private static int Invalid(PluginValueBuilder b, string description) =>
        b.Object(("isValid", b.Boolean(false)), ("errors", b.Array(b.String(description))));
}
