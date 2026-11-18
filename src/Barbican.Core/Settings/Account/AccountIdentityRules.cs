// Mirrors: see AccountIdentity; Bulwark identity-settings.tsx (validation), identity-store.ts (order). Contract from the M6 plan section 4.5; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>The pure rules of the identities page.</summary>
public static class AccountIdentityRules
{
    /// <summary>What is wrong with an identity before it is sent, or null: a name is optional, the email must have one @ with text each side and a dot in the domain (a wildcard local part "*@domain" is allowed), Bcc and Reply-To must each be a comma-separated list of valid addresses.</summary>
    public static string? Problem(AccountIdentityEdit edit, bool isCreate) => throw new PortPendingException("AccountIdentityRules.Problem");

    /// <summary>The identities in the order of <c>preferredIdentityIds</c>; ids not in the list follow in server order; ids in the list that no longer exist are ignored.</summary>
    public static IReadOnlyList<AccountIdentity> Ordered(IReadOnlyList<AccountIdentity> identities, IReadOnlyList<string> preferredIds) => throw new PortPendingException("AccountIdentityRules.Ordered");
}
