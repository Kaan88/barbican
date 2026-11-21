// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:475-495 doUserGetAccounts (account:read, host-api.ts:188): the signed-in accounts without secrets,
// { id, label, serverUrl, username, displayName, email, avatarColor, isConnected, isDefault, isActive }.
// Deviations: Bulwark lists every account of the browser; Barbican lists only the logins this add-on's grant names (a grant is per login, so an
// add-on never learns of another account's server or user name). Barbican has no label, display name, avatar colour or default account: label is
// the login name, displayName, email (unless the login name is an address) and avatarColor are empty strings and isDefault is false.
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Account;

internal sealed class PluginUserGetAccounts(IAccountManager accounts) : PluginScopedMethod("user.getAccounts", "account:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var active = accounts.Active?.Key;
        var b = new PluginValueBuilder();
        var rows = accounts.Accounts
            .Where(a => context.Demand("account:read", a.Key).IsOk)
            .Select(a => b.Object(
                ("id", b.String(a.Key)), ("label", b.String(a.User)), ("serverUrl", b.String(a.Server)), ("username", b.String(a.User)),
                ("displayName", b.String("")), ("email", b.String(a.User.Contains('@', StringComparison.Ordinal) ? a.User : "")), ("avatarColor", b.String("")),
                ("isConnected", b.Boolean(true)), ("isDefault", b.Boolean(false)), ("isActive", b.Boolean(string.Equals(a.Key, active, StringComparison.Ordinal)))))
            .ToArray();
        return Task.FromResult(Success(b, b.Array(rows)));
    }
}
