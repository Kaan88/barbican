using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
#if WINDOWS && AUTOMATION
using Barbican.Core.Accounts;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;
using Barbican.App.Shell.Automation;
#endif

namespace Barbican.App.Composition;

/// <summary>
/// Registers the views' services and the App's IDispatcher (docs/milestones/M1-plan.md §3.3); views get their
/// ViewModels from here through <see cref="ViewHost"/>, never with <c>new</c> in code-behind. Created empty by
/// stage F1; owned by the shell-ui stream.
/// </summary>
public static class ViewServices
{
    public static IServiceCollection AddBarbicanViews(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Replaces the inline dispatcher mailcore registers with TryAdd: the real one posts to the window's DispatcherQueue.
        services.Replace(ServiceDescriptor.Singleton<IDispatcher>(_ => new DispatcherQueueDispatcher(ViewHost.UiQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("No UI queue: the composition root must be built on the UI thread."))));
        services.TryAddSingleton<Views.Accounts.AccountFlyoutKeys>();
        services.AddSingleton<Barbican.Presentation.Input.IModalSurface>(sp => sp.GetRequiredService<Views.Accounts.AccountFlyoutKeys>());
        services.TryAddSingleton<StyleState>();
        services.TryAddSingleton<ShellLifecycle>();
        services.TryAddSingleton<WindowCloseController>();
        services.TryAddSingleton<ThemeController>();

        // the sign-in view and the account popover bind to Presentation's SignInViewModel and AccountsViewModel (AddBarbicanAccountsPresentation; R2-03)

        // Host.Build resolves the logger providers, which hands the finished provider to the views (see ViewHost.CaptureProvider).
        services.AddSingleton<ILoggerProvider>(ViewHost.CaptureProvider);

#if AUTOMATION
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            services.Replace(ServiceDescriptor.Singleton<Barbican.Presentation.Mail.IActiveMail, Shell.Automation.SampleActiveMail>());
#if WINDOWS
            if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_ADDON_ACCOUNT"), "1", StringComparison.Ordinal))
            {
                services.AddSingleton(sp => new SampleAddOnAccount((SampleActiveMail)sp.GetRequiredService<IActiveMail>()));
                if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_ORGANIZATION_POLICY"), "1", StringComparison.Ordinal))
                {
                    services.Replace(ServiceDescriptor.Singleton<Barbican.Core.OrganizationPolicy.OrganizationPolicyTransport>(sp =>
                        SamplePolicyFixture.Enabled(sp.GetRequiredService<IProfilePaths>()) ? SamplePolicyFixture.Transport() : new Barbican.Core.OrganizationPolicy.OrganizationPolicyTransport()));
                }
                if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_SEND_PIPELINE"), "1", StringComparison.Ordinal))
                {
                    services.Replace(ServiceDescriptor.Singleton<IAccountManager>(sp =>
                        ProfilePaths.IsAutomationProfile(sp.GetRequiredService<IProfilePaths>().Profile)
                            ? sp.GetRequiredService<SampleAddOnAccount>() : sp.GetRequiredService<AccountManager>()));
                    services.AddSingleton<Barbican.Automation.IAutomationCommand>(sp => new SampleSentReadbackCommand((SampleActiveMail)sp.GetRequiredService<IActiveMail>(), sp.GetRequiredService<IProfilePaths>()));
                }
                services.Replace(ServiceDescriptor.Singleton<IAccountOverview>(sp =>
                    ProfilePaths.IsAutomationProfile(sp.GetRequiredService<IProfilePaths>().Profile)
                        ? sp.GetRequiredService<SampleAddOnAccount>() : sp.GetRequiredService<AccountManager>()));
                services.Replace(ServiceDescriptor.Singleton<IAccountSessionSnapshots>(sp =>
                    ProfilePaths.IsAutomationProfile(sp.GetRequiredService<IProfilePaths>().Profile)
                        ? sp.GetRequiredService<SampleAddOnAccount>() : sp.GetRequiredService<AccountManager>()));
            }
#endif
        }

        services.AddSingleton<Barbican.Automation.IAutomationCommand, Shell.Automation.KeyCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Shell.Automation.UiCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Shell.Automation.DialogProbeCommand>();
#if WINDOWS
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Shell.Automation.CaptionCommand>();
#endif
#endif
        return services;
    }
}
