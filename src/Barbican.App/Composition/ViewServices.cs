using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
        services.TryAddSingleton<StyleState>();
        services.TryAddSingleton<ShellLifecycle>();
        services.TryAddSingleton<ThemeController>();

        // the sign-in view and the account popover bind to Presentation's SignInViewModel and AccountsViewModel (AddBarbicanAccountsPresentation; R2-03)

        // Host.Build resolves the logger providers, which hands the finished provider to the views (see ViewHost.CaptureProvider).
        services.AddSingleton<ILoggerProvider>(ViewHost.CaptureProvider);

#if AUTOMATION
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            services.Replace(ServiceDescriptor.Singleton<Barbican.Presentation.Mail.IActiveMail, Shell.Automation.SampleActiveMail>());
        }

        services.AddSingleton<Barbican.Automation.IAutomationCommand, Shell.Automation.KeyCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Shell.Automation.UiCommand>();
#endif
        return services;
    }
}
