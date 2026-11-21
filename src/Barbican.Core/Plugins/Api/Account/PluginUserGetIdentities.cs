// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:497-499 doUserGetIdentities (identity:read, host-api.ts:198) over the Identity shape of
// lib/jmap/types.ts:320-335 and RFC 8621 section 6: { id, name, email, replyTo?, bcc?, textSignature?, htmlSignature?, mayDelete }.
// The fields are named one by one (a server property Barbican does not model never reaches the add-on). Deviation: Bulwark's localAccountId and
// accountName (multi-account aggregation) are not produced; only the active login's identities are listed.
using System.Text.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Account;

internal sealed class PluginUserGetIdentities() : PluginScopedMethod("user.getIdentities", "identity:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var list = await capture!.Session.Mail.GetIdentitiesAsync(ct).ConfigureAwait(false);
        if (!list.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. list.Value!.Select(i => Project(b, i))]));
    }

    private static int Project(PluginValueBuilder b, Identity identity)
    {
        var fields = new List<(string, int)> { ("id", b.String(identity.Id)), ("name", b.String(identity.Name ?? "")), ("email", b.String(identity.Email)) };
        if (identity.ReplyTo is { Count: > 0 } replyTo) fields.Add(("replyTo", Addresses(b, replyTo)));
        if (Extra(identity, "bcc") is { ValueKind: JsonValueKind.Array } bcc) fields.Add(("bcc", Addresses(b, FromJson(bcc))));
        if (Extra(identity, "textSignature") is { ValueKind: JsonValueKind.String } text) fields.Add(("textSignature", b.String(text.GetString() ?? "")));
        if (Extra(identity, "htmlSignature") is { ValueKind: JsonValueKind.String } html) fields.Add(("htmlSignature", b.String(html.GetString() ?? "")));
        fields.Add(("mayDelete", b.Boolean(Extra(identity, "mayDelete") is { ValueKind: JsonValueKind.True })));
        return b.Object([.. fields]);
    }

    private static JsonElement? Extra(Identity identity, string name) => identity.Extra is not null && identity.Extra.TryGetValue(name, out var value) ? value : null;

    private static int Addresses(PluginValueBuilder b, IReadOnlyList<EmailAddress> list) =>
        b.Array([.. list.Select(a => b.Object(("name", a.Name is null ? b.Null() : b.String(a.Name)), ("email", b.String(a.Email))))]);

    private static List<EmailAddress> FromJson(JsonElement array) => [.. array.EnumerateArray()
        .Where(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String)
        .Select(a => new EmailAddress(a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null, a.GetProperty("email").GetString() ?? ""))];
}
