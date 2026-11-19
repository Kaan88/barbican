using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>
/// Registers the mail ViewModels, the key router and the hook bus (docs/milestones/M1-plan.md §3.3). Owned by the mailcore stream.
/// Services another stream completes are registered with TryAdd, so the owner's registration (which runs later in the composition root) wins:
/// <c>IPrefsStore</c>, <c>IDispatcher</c>, <c>IMessageOpener</c>, <c>ISystemAccessibilitySource</c>, <c>IActiveMail</c>.
/// </summary>
public static class MailPresentationRegistration
{
    public static IServiceCollection AddBarbicanMailCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // the hook bus: one instance, no plugin host yet (M6b adds handlers)
        services.TryAddSingleton<HookBus>();
        services.TryAddSingleton<IHookBus>(sp => sp.GetRequiredService<HookBus>());

        // replaceable defaults
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPrefsStore, EmptyPrefsStore>();
        services.TryAddSingleton<ISystemAccessibilitySource, NoSystemAccessibility>();
        services.TryAddSingleton<IDispatcher, InlineDispatcher>();
        services.TryAddSingleton<IActiveMail>(sp => sp.GetService<IAccountManager>() is { } accounts ? new ActiveMailSource(accounts, sp.GetRequiredService<IDispatcher>()) : new NoActiveMail());

        // state shared by the ViewModels
        services.TryAddSingleton<MotionState>();
        services.TryAddSingleton<MailEventHub>();
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<MailEventHub>());

        // ViewModels
        services.TryAddSingleton<ShellViewModel>();
        services.TryAddSingleton<ReaderViewModel>();
        services.TryAddSingleton<MessageListViewModel>();
        services.TryAddSingleton<SidebarViewModel>();
        services.TryAddSingleton<RailViewModel>();
        services.TryAddSingleton<GlanceTilesViewModel>();
        services.TryAddSingleton<ReaderRowFollower>();
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<ReaderRowFollower>());
        services.TryAddSingleton<IMessageOpener>(sp =>
        {
            var prefs = sp.GetRequiredService<IPrefsStore>();
            var system = sp.GetRequiredService<ISystemAccessibilitySource>();
            var time = sp.GetRequiredService<TimeProvider>();
            return new HeaderOnlyMessageOpener(
                sp.GetRequiredService<ReaderViewModel>(),
                () => MailListSettings.Read(prefs.Current, system.Current).ReaderOptionsAt(time.GetUtcNow(), Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(prefs.Current.Str("timeZone", "auto"), time.LocalTimeZone)));
        });

        // input and start-up
        services.TryAddSingleton<IKeyRouter, KeyRouter>();
        services.TryAddSingleton<MailShellStartup>();
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<MailShellStartup>());
        return services;
    }
}
