// Mirrors: see IAccountIdentities. Stub from the M6 plan section 4.5; owner set-data replaces the class (constructor dependencies: IAccountManager, IHookBus, ITemplateStore-style metadata access).

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The default <see cref="IAccountIdentities"/>.</summary>
public sealed class AccountIdentities : IAccountIdentities
{
    public event EventHandler<AccountIdentityChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<IReadOnlyList<AccountIdentity>>> ListAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("AccountIdentities.ListAsync");

    public Task<Result<AccountIdentity>> CreateAsync(string accountKey, AccountIdentityEdit edit, CancellationToken ct) => throw new PortPendingException("AccountIdentities.CreateAsync");

    public Task<Result<AccountIdentity>> UpdateAsync(string accountKey, string id, AccountIdentityEdit edit, CancellationToken ct) => throw new PortPendingException("AccountIdentities.UpdateAsync");

    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct) => throw new PortPendingException("AccountIdentities.DeleteAsync");

    public Task<Result<Unit>> SetOrderAsync(string accountKey, IReadOnlyList<string> orderedIds, CancellationToken ct) => throw new PortPendingException("AccountIdentities.SetOrderAsync");
}
