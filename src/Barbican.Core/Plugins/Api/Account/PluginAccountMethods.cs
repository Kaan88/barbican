// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:188-200,2217-2225 (the user.* permission table and routing).
// Not provided (they stay unknown methods and fail closed): user.logout and user.returnToOwnAccount would sign the user out or switch the session
// and are not read-only; user.findPeople (directory:read) needs the server directory with the administrator's directory scope and its own rate
// limit, which has no Barbican service yet.
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Api.Account;

/// <summary>Factory for the read-only account, identity and time-zone methods.</summary>
public static class PluginAccountMethods
{
    public static IReadOnlyList<IPluginApiMethod> Create(IAccountManager accounts, IPrefsStore prefs, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(time);
        return [new PluginUserGetSelf(), new PluginUserGetAccounts(accounts), new PluginUserGetMailAccounts(), new PluginUserGetIdentities(), new PluginUserGetTimeZone(prefs, time)];
    }
}
