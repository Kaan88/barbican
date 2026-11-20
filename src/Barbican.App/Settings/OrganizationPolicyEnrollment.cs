#if WINDOWS
using Barbican.Core.Accounts;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.App.Settings;

internal sealed class OrganizationPolicyEnrollment(IAccountSessionSnapshots accounts, OrganizationPolicyService policy)
{
    internal ProviderEnrollmentAccount? Capture() =>
        accounts.CaptureActiveSession().Value is { } selection && policy.CanConfigureProvider(selection)
            ? new ProviderEnrollmentAccount(selection) : null;

    internal bool IsCurrent(ProviderEnrollmentAccount account) =>
        policy.CanConfigureProvider(account.Selection);

    internal Uri? CurrentProvider(ProviderEnrollmentAccount account) =>
        policy.ProviderFor(account.Selection);

    internal Task<OrganizationPolicyLookup> ConnectAsync(ProviderEnrollmentAccount account, string origin, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var address) || !OrganizationPolicyService.IsProviderOrigin(address))
            return Task.FromResult(new OrganizationPolicyLookup(OrganizationPolicyTransportStatus.Untrusted));
        return policy.VerifyProviderAsync(account.Selection, address, ct);
    }

    internal bool Disconnect(ProviderEnrollmentAccount account) =>
        policy.RemoveProvider(account.Selection);

    internal sealed record ProviderEnrollmentAccount(AccountSessionSnapshot Selection)
    {
        internal string DisplayName => Selection.Session.Account.User;
    }
}
#endif
