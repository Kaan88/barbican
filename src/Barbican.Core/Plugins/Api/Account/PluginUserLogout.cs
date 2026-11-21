// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:207,501-503 doUserLogout (auth:emit; signs the active login out through the auth store, whose logout runs onBeforeLogout, onAfterLogout and the sign-out cleanup).
// Barbican runs it through IAccountMaintenance.SignOutAsync for the login captured at call time (the same call the account menu makes; hooks and the add-on sign-out cleanup run there).
// Deviation, on purpose: Bulwark signs out at once. Signing out erases the login's stored credentials, so Barbican first asks the user in the host dialog (the add-on is named in it, the text is the host's): only an accepted
// dialog signs out; a declined dialog is Cancelled and a dialog that cannot be shown (no window, one already open for this add-on, 20 a minute) is Unavailable, and neither touches the login. The add-on's own grant ends with the
// sign-out, so a call that went through reports UnknownOutcome (the dispatcher cannot hand an answer to an add-on whose authority is gone), exactly as for any write whose call went stale during it.
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Account;

internal sealed class PluginUserLogout(IAccountMaintenance maintenance, Func<string, IPluginInvocationContext, CancellationToken, Task<bool?>> confirm)
    : PluginScopedMethod("user.logout", "auth:emit", mayMutate: true, slow: true)
{
    private readonly IAccountMaintenance _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
    private readonly Func<string, IPluginInvocationContext, CancellationToken, Task<bool?>> _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var login = capture!.Session.Account;
        var accepted = await _confirm(string.IsNullOrEmpty(login.User) ? login.Key : login.User, context, ct).ConfigureAwait(false);
        if (accepted is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        if (accepted is false) return PluginCallOutcome.Failed(PluginErrorCode.Cancelled);
        // the grant, the add-on and the login must still be the ones the user was asked about
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var signedOut = await _maintenance.SignOutAsync(login.Key, ct).ConfigureAwait(false);
        if (!signedOut.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        return Success(b, b.Undefined());
    }
}
