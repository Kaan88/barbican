// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:188-200,2217-2225 (the user.* permission table and routing).
// user.findPeople (directory:read, over the contacts directory service) and user.logout (auth:emit, behind the host's confirmation dialog) have their own factories below, because they need services these methods do not.
// Not provided (it stays an unknown method and fails closed): user.returnToOwnAccount (auth:impersonation-return) only means something in a session an administrator opened through an impersonation handoff, and Barbican has no such
// session state, no server endpoint to return through and no impersonation banner.
using Barbican.Core.Accounts;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Api.Account;

/// <summary>Factory for the read-only account, identity and time-zone methods.</summary>
public static class PluginAccountMethods
{
    /// <summary>user.findPeople: the server directory, 20 people at most, 10 lookups a minute per add-on.</summary>
    public static IPluginApiMethod FindPeople(IContactDirectory directory, IPluginMailRateLimiter limiter, TimeProvider time) =>
        new PluginUserFindPeople(directory, new PluginDirectoryCache(time ?? throw new ArgumentNullException(nameof(time))), limiter);

    /// <summary>user.logout: signs the active login out after the user accepts <paramref name="confirm"/> (true accepted, false declined, null could not be asked).</summary>
    public static IPluginApiMethod Logout(IAccountMaintenance maintenance, Func<string, IPluginInvocationContext, CancellationToken, Task<bool?>> confirm) => new PluginUserLogout(maintenance, confirm);

    public static IReadOnlyList<IPluginApiMethod> Create(IAccountManager accounts, IPrefsStore prefs, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(time);
        return [new PluginUserGetSelf(), new PluginUserGetAccounts(accounts), new PluginUserGetMailAccounts(), new PluginUserGetIdentities(), new PluginUserGetTimeZone(prefs, time)];
    }
}
