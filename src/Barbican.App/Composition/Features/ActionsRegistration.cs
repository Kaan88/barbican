// Mirrors: M2 plan section 3.3 (one Add<Feature>() per stream; stage F creates it, the owner fills it). Owner actions: IMailWriterFactory, IUndoService, IToastService, IMailSelection (Replace), the action
// and list IAppCommands, the toast host IShellOverlay, the selection bar slot and the real IMailActions.

using Barbican.App.Shell;
using Barbican.App.Views.Actions;
using Barbican.Core.Accounts;
using Barbican.Core.Layout;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.DragDrop;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

/// <summary>Mail and folder actions, undo and toasts, multi-select, drag and drop, panes, tags (M2 stream actions).</summary>
public static class ActionsRegistration
{
    public static IServiceCollection AddBarbicanMailActions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // the replaceable defaults of every M2 seam (TryAdd; the owners Replace them), registered once, here, because this is the first of the five calls
        services.AddBarbicanM2Defaults();
        return services.AddActionServices().AddActionCommands().AddActionViews();
    }

    private static IServiceCollection AddActionServices(this IServiceCollection services)
    {
        // writes
        services.TryAddSingleton<IMailWriterFactory, MailWriterFactory>();
        services.TryAddSingleton(WriterSource);
        services.TryAddSingleton<IMailTransfer>(sp => new SessionMailTransfer(sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<IMailWriterFactory>()));

        // toasts and undo
        services.TryAddSingleton(sp => new ToastService(sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IDispatcher>()));
        services.TryAddSingleton<IToastService>(sp => sp.GetRequiredService<ToastService>());
        services.TryAddSingleton(sp => new UndoService(sp.GetRequiredService<IToastService>()));
        services.TryAddSingleton<IUndoService>(sp => sp.GetRequiredService<UndoService>());

        // the list as the editor and the selection of the actions
        services.TryAddSingleton<IListEditor>(sp => sp.GetRequiredService<MessageListViewModel>());
        services.Replace(ServiceDescriptor.Singleton<IMailSelection>(sp => sp.GetRequiredService<MessageListViewModel>()));
        services.TryAddSingleton<IMailboxDirectory>(sp => sp.GetRequiredService<SidebarViewModel>());
        services.TryAddSingleton<IActionConfirmer>(sp => new DialogActionConfirmer(() => sp.GetService<IDialogService>()));
        services.TryAddSingleton(sp => new MailActionService(
            sp.GetRequiredService<IMailWriterSource>(),
            sp.GetRequiredService<IMailboxDirectory>(),
            sp.GetRequiredService<IListEditor>(),
            sp.GetRequiredService<IHookBus>(),
            sp.GetRequiredService<IUndoService>(),
            sp.GetRequiredService<IToastService>(),
            sp.GetRequiredService<IPrefsStore>(),
            sp.GetRequiredService<IActionConfirmer>(),
            sp.GetRequiredService<IActiveMail>(),
            sp.GetRequiredService<IMailTransfer>(),
            sp.GetRequiredService<TimeProvider>()));

        // the real mark-as-read of the reader, drag and drop, the panes
        services.Replace(ServiceDescriptor.Singleton<IMailActions>(sp => new LiveMailActions(
            () => sp.GetRequiredService<MailActionService>(),
            () => sp.GetRequiredService<IListEditor>(),
            sp.GetRequiredService<IMailWriterSource>(),
            sp.GetRequiredService<IActiveMail>())));
        services.TryAddSingleton<DropController>();
        services.TryAddSingleton(sp => new PanesViewModel(Path.Combine(sp.GetRequiredService<IProfilePaths>().ConfigDir, PanesFile.FileName), sp.GetRequiredService<IHookBus>()));
        return services;
    }

    private static IMailWriterSource WriterSource(IServiceProvider sp)
    {
#if AUTOMATION
        // a sample-data run writes to the in-process fake server only
        if (sp.GetService<IActiveMail>() is IMailWriterSource sample)
        {
            return sample;
        }
#endif
        return new SessionMailWriters(sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<IMailWriterFactory>());
    }

    private static IServiceCollection AddActionCommands(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => ActionCommandSet.Build(
            sp.GetRequiredService<MailActionService>(),
            sp.GetRequiredService<MessageListViewModel>(),
            sp.GetRequiredService<IMailWriterSource>(),
            sp.GetRequiredService<IMailboxDirectory>(),
            sp.GetRequiredService<IUndoService>(),
            sp.GetService<Barbican.Presentation.Dialogs.IDialogService>(),
            sp.GetService<Barbican.Core.Storage.IPrefsStore>(),
            sp.GetService<Barbican.Core.Storage.IPrefsEditor>()).ToDictionary(c => c.Id, StringComparer.Ordinal));
        foreach (var id in ActionCommandSet.Ids)
        {
            var key = id;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<Dictionary<string, IAppCommand>>()[key]);
        }

        // the tag menu (MENU-TAG): create, rename, recolour, delete, show, mark read
        services.TryAddSingleton(sp => new Barbican.Presentation.Tags.TagService(sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<Barbican.Core.Storage.IPrefsEditor>()));
        services.TryAddSingleton(sp => new Barbican.Presentation.Tags.TagCommandSet(Barbican.Presentation.Tags.TagCommands.Create(
            sp.GetRequiredService<Barbican.Presentation.Tags.TagService>(),
            sp.GetRequiredService<SidebarViewModel>(),
            sp.GetRequiredService<MailActionService>(),
            sp.GetRequiredService<IMailWriterSource>(),
            sp.GetRequiredService<IActiveMail>(),
            sp.GetService<IDialogService>()).ToDictionary(c => c.Id, StringComparer.Ordinal)));
        foreach (var id in Barbican.Presentation.Tags.TagCommands.Ids)
        {
            var key = id;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<Barbican.Presentation.Tags.TagCommandSet>().Commands[key]);
        }

        return services;
    }

    private static IServiceCollection AddActionViews(this IServiceCollection services)
    {
        // the toast host overlay and the selection bar slot
        services.AddSingleton<IShellOverlay, ToastOverlay>();
        services.AddSingleton<ISlotContent>(new SlotContent(SlotNames.ListSelectionBar, () => new SelectionBarView()));
        // the copy picker (G-048): the other signed-in accounts' folders
        services.AddSingleton<Barbican.App.Views.Dialogs.IDialogContent, Views.Actions.CopyToAccountDialogContent>();
#if AUTOMATION
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Actions.Automation.ActCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Actions.Automation.SelectCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Actions.Automation.ToastLastCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Actions.Automation.PaneSetCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Actions.Automation.HoverSimCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Views.Actions.Automation.DndSimCommand>();
#endif
        return services;
    }
}
