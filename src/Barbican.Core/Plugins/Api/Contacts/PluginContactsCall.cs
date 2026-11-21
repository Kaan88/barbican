// Mirrors: Bulwark host-api-input.ts:25-30 sessionClient (the session a call runs on) with Barbican's per-call capture of the active login.
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Contacts;

/// <summary>What one contact call runs on: the add-on, the login captured at call time and its session.</summary>
internal sealed class PluginContactsCall(IPluginInvocationContext context, IAccountSession session)
{
    internal IPluginInvocationContext Context { get; } = context;

    internal IAccountSession Session { get; } = session;

    /// <summary>The login's key, the store's and the writers' account argument.</summary>
    internal string AccountKey => Session.Account.Key;

    /// <summary>Revalidates the grant and the session and then marks the write as issued: nothing mutates without both, and a later stale check reports an unknown outcome.</summary>
    internal async Task<bool> BeginMutationAsync(CancellationToken ct)
    {
        if (!await Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return false;
        Context.MarkMutationIssued();
        return true;
    }
}
