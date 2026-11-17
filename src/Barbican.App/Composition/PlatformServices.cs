// Mirrors: PORT-PLAN.md §3.9 (platform services per OS), ADR 0008 (secrets under Barbican.Next/<profile>/), ADR 0007 and M1-plan §1 rule 2
// (AUTOMATION builds never open a real browser: OAuth follows the redirect itself; an automation profile keeps its secrets in memory and
// never writes the OS credential store). Owned by the accounts stream.

using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Storage;
using Barbican.Platform;
using Barbican.Presentation.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if WINDOWS
using Barbican.Platform.Windows;
#else
using Barbican.Platform.Linux;
#endif

namespace Barbican.App.Composition;

/// <summary>
/// Registers the platform services of the running head (docs/milestones/M1-plan.md §3.3): on the
/// WinUI head the Barbican.Platform.Windows implementations, on the desktop head the
/// Barbican.Platform.Linux ones.
/// </summary>
public static class PlatformServices
{
    public static IServiceCollection AddBarbicanPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // profile file IO on the UI thread is counted and asserted in Debug builds (M1-plan §6.3); the composition runs on the UI thread
        if (Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() is { } ui)
        {
            IoThreadCheck.Install(() => ui.HasThreadAccess);
        }

        services.TryAddSingleton<ISecretStore>(sp =>
        {
            var profile = sp.GetRequiredService<IProfilePaths>().Profile;
            if (ProfilePaths.IsAutomationProfile(profile))
            {
                return new InMemorySecretStore();
            }
#if WINDOWS
            return new WindowsSecretStore(profile);
#else
            return new SecretServiceStore(profile);
#endif
        });
        services.TryAddSingleton<IAccountVault>(sp => new SecretAccountVault(sp.GetRequiredService<ISecretStore>()));
#if WINDOWS
        services.TryAddSingleton<ILegacyCredentialSource, WindowsLegacyCredentialSource>();
        // M3 editor-core: the spell checker behind ISpellEngine is Windows' ISpellChecker (replaces the empty default of AddBarbicanEditorCore; Linux keeps it until M9)
        services.Replace(ServiceDescriptor.Singleton<Barbican.Core.Editor.Spell.ISpellCheckerFactory, Barbican.Platform.Windows.Spelling.WindowsSpellCheckerFactory>());
        services.TryAddSingleton<ILegacyAccountImport>(sp => new RustCredentialImport(
            sp.GetRequiredService<ILegacyCredentialSource>(), sp.GetRequiredService<ISecretStore>(), sp.GetRequiredService<IProfilePaths>()));
#endif

#if AUTOMATION
        services.TryAddSingleton<IBrowserLauncher, RefusingBrowserLauncher>();
        services.TryAddSingleton<IAuthorizationLauncher>(sp => new RedirectFollowingLauncher(sp.GetRequiredService<Barbican.Core.Jmap.Transport.IJmapHttp>()));
#else
#if WINDOWS
        services.TryAddSingleton<IBrowserLauncher, WindowsBrowserLauncher>();
#else
        services.TryAddSingleton<IBrowserLauncher, LinuxBrowserLauncher>();
#endif
        services.TryAddSingleton<IAuthorizationLauncher>(sp => new BrowserAuthorizationLauncher(sp.GetRequiredService<IBrowserLauncher>()));
#endif
        services.AddBarbicanAccountsPresentation();
        return services;
    }
}
