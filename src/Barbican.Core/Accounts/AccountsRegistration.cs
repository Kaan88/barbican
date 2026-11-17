// Mirrors: nothing in Bulwark or Stalwart; this is the accounts feature's DI module (docs/milestones/M1-plan.md §3.3). Created empty by
// stage F1; owned by the accounts stream. mailcore registers EmptyPrefsStore with TryAdd before this module runs, so the real prefs store
// replaces it. The platform module (App/Composition/PlatformServices.cs) adds what needs the OS: IAccountVault (over ISecretStore) and
// IAuthorizationLauncher (the system browser, or the redirect-following stand-in in AUTOMATION builds).

using Barbican.Core.Auth;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Cache;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Accounts;

/// <summary>Registers accounts, sign-in, profiles, prefs and the body cache.</summary>
public static class AccountsRegistration
{
    public static IServiceCollection AddBarbicanAccounts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProfilePaths>(_ => ProfilePaths.FromEnvironment());

        services.TryAddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<IProfilePaths>();

            // the dev profile starts from a copy of the Rust app's prefs, panes and search history (PORT-PLAN.md §3.5); read-only on that side
            var rust = ProfilePaths.RustConfigDir(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable, ProfilePaths.HomeDir());
            return new PrefsStore(paths, sp.GetRequiredService<TimeProvider>(), () => RustProfileCopy.Run(paths, rust));
        });
        services.Replace(ServiceDescriptor.Singleton<IPrefsStore>(sp => sp.GetRequiredService<PrefsStore>()));
        services.TryAddSingleton<IPrefsEditor>(sp => sp.GetRequiredService<PrefsStore>());

        services.TryAddSingleton<IBodyCache>(_ => new BodyCache());

        services.TryAddSingleton(sp => new OAuthFlowOptions
        {
            // an automation run never holds the fixed port another sign-in may need
            LoopbackPort = ProfilePaths.IsAutomationProfile(sp.GetRequiredService<IProfilePaths>().Profile) ? 0 : LoopbackListener.PreferredPort,
        });
        services.TryAddSingleton<OAuthHttp>();
        services.TryAddSingleton<OAuthDiscovery>();
        services.TryAddSingleton<OAuthClientRegistry>();
        services.TryAddSingleton<OAuthFlow>();
        services.TryAddSingleton<ServerDiscovery>();

        services.TryAddSingleton(_ => ReadOnlyPolicy.FromEnvironment());
        services.TryAddSingleton(_ => new JmapClientOptions());
        services.TryAddSingleton<AccountStore>();
        services.TryAddSingleton(sp => new AccountManager(
            sp.GetRequiredService<IJmapClientFactory>(),
            sp.GetRequiredService<Mail.Service.IMailAccountFactory>(),
            sp.GetRequiredService<AccountStore>(),
            sp.GetRequiredService<OAuthFlow>(),
            sp.GetRequiredService<OAuthHttp>(),
            sp.GetRequiredService<IProfilePaths>(),
            sp.GetRequiredService<ReadOnlyPolicy>(),
            sp.GetRequiredService<Plugins.Hooks.IHookBus>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<JmapClientOptions>()));
        services.TryAddSingleton<IAccountManager>(sp => sp.GetRequiredService<AccountManager>());
        services.TryAddSingleton<IAccountOverview>(sp => sp.GetRequiredService<AccountManager>());
        return services;
    }
}
