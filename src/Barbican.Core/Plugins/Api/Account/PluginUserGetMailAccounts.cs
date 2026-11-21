// Mirrors: Bulwark lib/plugin-sandbox/host-api-account.ts:70-91 doUserGetMailAccounts and account-kind.ts (account:read, host-api.ts:192):
// [{ accountId, name, kind: personal | group | shared, isReadOnly, isOpen }] for the mail accounts of the login (RFC 8620 section 2 accounts).
// Deviations: Bulwark asks the server (Principal/get) which shared accounts are groups; Barbican does not, so every other account is "shared", never
// "group" (Bulwark also says "shared" while unconfirmed). isOpen is true for the login's own account: shared accounts are not openable folders here.
using System.Text.Json;
using Barbican.Core.Jmap;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Account;

internal sealed class PluginUserGetMailAccounts() : PluginScopedMethod("user.getMailAccounts", "account:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session.Client.Session;
        var own = session.MailAccountId;
        var b = new PluginValueBuilder();
        JsonElement? map = session.Raw.ValueKind == JsonValueKind.Object && session.Raw.TryGetProperty("accounts", out var all) && all.ValueKind == JsonValueKind.Object ? all : null;
        var ids = new List<string> { own };
        if (map is { } accounts)
        {
            ids.AddRange(accounts.EnumerateObject().Where(a => !string.Equals(a.Name, own, StringComparison.Ordinal) && HasMail(a.Value)).Select(a => a.Name));
        }

        var rows = new List<int>();
        foreach (var id in ids)
        {
            string? name = null;
            var readOnly = false;
            if (map is { } m && m.TryGetProperty(id, out var info) && info.ValueKind == JsonValueKind.Object)
            {
                name = info.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                readOnly = info.TryGetProperty("isReadOnly", out var ro) && ro.ValueKind == JsonValueKind.True;
            }

            var isOwn = string.Equals(id, own, StringComparison.Ordinal);
            rows.Add(b.Object(
                ("accountId", b.String(id)), ("name", b.String(!string.IsNullOrEmpty(name) ? name : isOwn ? session.Username : id)),
                ("kind", b.String(isOwn ? "personal" : "shared")), ("isReadOnly", b.Boolean(readOnly)), ("isOpen", b.Boolean(isOwn))));
        }

        return Task.FromResult(Success(b, b.Array([.. rows])));
    }

    private static bool HasMail(JsonElement account) =>
        account.ValueKind == JsonValueKind.Object && account.TryGetProperty("accountCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty(JmapCapabilities.Mail, out _);
}
