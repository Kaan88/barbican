// Mirrors: M2 plan section 3.3 (one Add<Feature>() per stream; stage F creates it, the owner fills it). Owner actions: IMailWriterFactory, IUndoService, IToastService, IMailSelection (Replace), the action
// and list IAppCommands, the toast host IShellOverlay, the selection bar slot and the real IMailActions.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
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

        // writes
        services.TryAddSingleton<IMailWriterFactory, MailWriterFactory>();
        services.TryAddSingleton<IMailWriterSource>(sp => new SessionMailWriters(sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<IMailWriterFactory>()));

        // toasts and undo
        services.TryAddSingleton<ToastService>(sp => new ToastService(sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IToastService>(sp => sp.GetRequiredService<ToastService>());
        services.TryAddSingleton<UndoService>(sp => new UndoService(sp.GetRequiredService<IToastService>()));
        services.TryAddSingleton<IUndoService>(sp => sp.GetRequiredService<UndoService>());

        // the list as the editor and the selection of the actions
        services.TryAddSingleton<IListEditor>(sp => sp.GetRequiredService<MessageListViewModel>());
        services.Replace(ServiceDescriptor.Singleton<IMailSelection>(sp => sp.GetRequiredService<MessageListViewModel>()));
        services.TryAddSingleton<IMailboxDirectory>(sp => sp.GetRequiredService<SidebarViewModel>());
        services.TryAddSingleton<IActionConfirmer>(sp => new DialogActionConfirmer(() => sp.GetService<IDialogService>()));

        // the actions, the commands, the real mark-as-read
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
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => ActionCommandSet.Build(
            sp.GetRequiredService<MailActionService>(),
            sp.GetRequiredService<MessageListViewModel>(),
            sp.GetRequiredService<IMailWriterSource>(),
            sp.GetRequiredService<IMailboxDirectory>(),
            sp.GetRequiredService<IUndoService>()).ToDictionary(c => c.Id, StringComparer.Ordinal));
        foreach (var id in ActionCommandSet.Ids)
        {
            var key = id;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<Dictionary<string, IAppCommand>>()[key]);
        }

        services.Replace(ServiceDescriptor.Singleton<IMailActions>(sp => new LiveMailActions(
            sp.GetRequiredService<MailActionService>(),
            sp.GetRequiredService<IListEditor>(),
            sp.GetRequiredService<IMailWriterSource>(),
            sp.GetRequiredService<IActiveMail>())));

        // drag and drop and the panes
        services.TryAddSingleton<DropController>();
        services.TryAddSingleton(sp => new PanesViewModel(Path.Combine(sp.GetRequiredService<IProfilePaths>().ConfigDir, Barbican.Core.Layout.PanesFile.FileName), sp.GetRequiredService<IHookBus>()));
        return services;
    }
}
