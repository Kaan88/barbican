// Mirrors: nothing in Bulwark or Stalwart; this is the accounts feature's DI module
// (docs/milestones/M1-plan.md §3.3). Created empty by stage F1 so the composition root compiles
// before the accounts stream exists; owned by the accounts stream.
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Core.Accounts;

/// <summary>Registers accounts, sign-in, profiles, prefs and the body cache.</summary>
public static class AccountsRegistration
{
    public static IServiceCollection AddBarbicanAccounts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
