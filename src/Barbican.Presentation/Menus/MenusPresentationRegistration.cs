// Mirrors: M2 plan section 3.3 (one Add<Feature>() per stream; a helper in the stream's own Presentation folder so tests compose without the App). Owner menus.

using Barbican.Core.Accounts;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Palette;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Menus;

/// <summary>Registers the command shell, menus, dialogs, palette and shortcut sheet of the menus stream.</summary>
public static class MenusPresentationRegistration
{
    public static IServiceCollection AddBarbicanMenusPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<MailboxMenuData>();
        services.TryAddSingleton<IMenuDataSource>(sp => sp.GetRequiredService<MailboxMenuData>());
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<MailboxMenuData>());
        services.TryAddSingleton<MenuModelBuilder>(sp => new MenuModelBuilder(sp.GetServices<IMenuProvider>(), sp.GetRequiredService<ICommandRegistry>()));
        foreach (var index in Enumerable.Range(0, MenuProviders.Count))
        {
            var i = index;
            services.AddSingleton<IMenuProvider>(sp => MenuProviders.Create(sp.GetRequiredService<IMenuDataSource>())[i]);
        }

        // every surface runs commands through one runner: an exception becomes an error toast and a crash-log line (Fable review 3 R3-05)
        services.TryAddSingleton(sp => new CommandRunner(
            sp.GetService<IToastService>(),
            sp.GetService<Barbican.Core.Storage.IProfilePaths>() is { } paths ? Barbican.Core.Storage.CrashLog.FolderOf(paths) : null,
            sp.GetService<TimeProvider>()));
        services.TryAddSingleton<ContextMenuController>(sp => new ContextMenuController(sp.GetRequiredService<MenuModelBuilder>(), sp.GetRequiredService<ICommandRegistry>(), sp.GetService<IToastService>(), sp.GetRequiredService<CommandRunner>()));
        services.Replace(ServiceDescriptor.Singleton<IContextMenuService>(sp => sp.GetRequiredService<ContextMenuController>()));
        services.AddSingleton<IModalSurface>(sp => sp.GetRequiredService<ContextMenuController>());

        services.TryAddSingleton<DialogService>();
        services.TryAddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<IModalSurface>(sp => sp.GetRequiredService<DialogService>());

        services.TryAddSingleton<IPaletteContextSource>(sp => new PaletteContextSource(sp.GetRequiredService<ShellViewModel>(), sp.GetRequiredService<IMailSelection>(), sp.GetService<IAccountManager>()));
        services.TryAddSingleton<PaletteController>(sp => new PaletteController(sp.GetRequiredService<ICommandRegistry>(), sp.GetRequiredService<IPaletteContextSource>(), sp.GetServices<IPaletteProvider>(), sp.GetService<IToastService>(), sp.GetRequiredService<CommandRunner>()));
        services.AddSingleton<IModalSurface>(sp => sp.GetRequiredService<PaletteController>());
        services.TryAddSingleton<ShortcutSheetController>();
        services.AddSingleton<IModalSurface>(sp => sp.GetRequiredService<ShortcutSheetController>());
        services.TryAddSingleton<ShortcutCommands>(sp => new ShortcutCommands(sp.GetRequiredService<ICommandRegistry>(), sp.GetRequiredService<IMailSelection>(), sp.GetRequiredService<CommandRunner>(), sp.GetService<Barbican.Presentation.Calendar.Commands.ICalendarKeyContext>()));

        services.TryAddSingleton<ShellCommandSet>(sp => new ShellCommandSet(ShellCommands.Create(
            sp.GetRequiredService<ShellViewModel>(),
            sp.GetRequiredService<SidebarViewModel>(),
            sp.GetRequiredService<MessageListViewModel>(),
            new Lazy<PaletteController>(sp.GetRequiredService<PaletteController>),
            sp.GetRequiredService<ShortcutSheetController>(),
            sp.GetService<IAccountManager>(),
            sp.GetService<IAccountMaintenance>())));
        foreach (var index in Enumerable.Range(0, ShellCommands.Count))
        {
            var i = index;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<ShellCommandSet>().Commands[i]);
        }

        return services;
    }
}
