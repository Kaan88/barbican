// Mirrors: Bulwark lib/plugin-sandbox/host-api-account.ts:47-58 doUserGetSelf (account:read, host-api.ts:191): { accountId, email, name }, where
// name is the account's display name, else the first named identity, else the address. Barbican has no separate display name, so the identity
// name (or the address) is used. Stalwart uses the account id as the principal id, so accountId is the login's primary mail account id.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Account;

internal sealed class PluginUserGetSelf() : PluginScopedMethod("user.getSelf", "account:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session;
        var email = string.IsNullOrEmpty(session.Client.Session.Username) ? session.Account.User : session.Client.Session.Username;
        var identities = await session.Mail.GetIdentitiesAsync(ct).ConfigureAwait(false);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var name = identities.IsOk ? identities.Value!.Select(i => i.Name?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n)) : null;
        var b = new PluginValueBuilder();
        return Success(b, b.Object(("accountId", b.String(session.Client.Session.MailAccountId)), ("email", b.String(email)), ("name", b.String(name ?? email))));
    }
}
