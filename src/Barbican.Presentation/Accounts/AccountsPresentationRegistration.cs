// Mirrors: nothing in Bulwark or Stalwart; the accounts feature's Presentation registrations (docs/milestones/M1-plan.md §3.3). Called from
// App/Composition/PlatformServices.cs (accounts-owned), because Core's AddBarbicanAccounts cannot see Presentation.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Cache;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Accounts;

/// <summary>Registers the sign-in and account ViewModels and the accounts start-up work.</summary>
public static class AccountsPresentationRegistration
{
    public static IServiceCollection AddBarbicanAccountsPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(sp => new SignInViewModel(sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<ServerDiscovery>(), sp.GetRequiredService<IDispatcher>()));
        services.TryAddSingleton(sp => new AccountsViewModel(sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<IAccountOverview>(), sp.GetRequiredService<IDispatcher>()));
        services.TryAddSingleton(sp => new AccountsStartup(sp.GetRequiredService<AccountManager>(), sp.GetRequiredService<PrefsStore>(), sp.GetService<ILegacyAccountImport>()));
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<AccountsStartup>());
        services.TryAddSingleton(sp => new BodyCacheSync(sp.GetRequiredService<MailEventHub>(), sp.GetRequiredService<IBodyCache>()));
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<BodyCacheSync>());
        return services;
    }
}
