// Mirrors: M5 plan section 4.10 (DI). Owner con-ui: the contacts view model, its account source, the actions, the commands, the menus.

using Barbican.Core.Accounts;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M5 stream con-ui.</summary>
public static class ContactsUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanContactsUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IContactsAccountSource>(sp => new ContactsActiveAccount(sp.GetRequiredService<IAccountManager>()));
        services.TryAddSingleton(sp => new ContactsViewModel(
            sp.GetRequiredService<IContactStore>(),
            sp.GetRequiredService<IContactWriter>(),
            sp.GetRequiredService<IPrefsStore>(),
            sp.GetRequiredService<IHookBus>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IContactsAccountSource>(),
            sp.GetRequiredService<IDispatcher>(),
            sp.GetService<IContactDirectory>()));
        services.TryAddSingleton<IContactsViewModel>(sp => sp.GetRequiredService<ContactsViewModel>());
        services.TryAddSingleton<IContactsEditors, NoContactsEditors>();
        services.TryAddSingleton<IContactsChrome, NoContactsChrome>();
        services.TryAddSingleton<ContactsFilePicker>(_ => new ContactsFilePicker());
        services.TryAddSingleton<IContactsFilePicker>(sp => sp.GetRequiredService<ContactsFilePicker>());
        services.TryAddSingleton(sp => new ContactsActions(new ContactsActionDeps(
            sp.GetRequiredService<IContactsViewModel>(),
            sp.GetRequiredService<IContactWriter>(),
            sp.GetRequiredService<IAddressBooks>(),
            sp.GetRequiredService<IContactsFilePicker>(),
            sp.GetRequiredService<IContactsEditors>(),
            sp.GetRequiredService<IContactsChrome>(),
            sp.GetRequiredService<TimeProvider>())
        {
            Dialogs = sp.GetService<IDialogService>(),
            Composer = sp.GetService<IComposerService>(),
            Clipboard = sp.GetService<IClipboardService>(),
            Toasts = sp.GetService<IToastService>(),
            Directory = sp.GetService<IContactDirectory>(),
            Lookup = sp.GetService<IContactLookup>(),
            Shell = sp.GetService<ShellViewModel>(),
        }));
        services.TryAddSingleton(sp => new ContactsCommandSet(ContactsCommands.Create(sp.GetRequiredService<ContactsActions>(), sp.GetRequiredService<IContactsViewModel>(), sp.GetRequiredService<IContactsChrome>())));
        for (var i = 0; i < ContactsCommands.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<ContactsCommandSet>().Commands[index]);
        }

        for (var i = 0; i < ContactsMenus.Count; i++)
        {
            var index = i;
            services.AddSingleton<IMenuProvider>(sp => ContactsMenus.Create(sp.GetRequiredService<IContactsViewModel>())[index]);
        }

        return services;
    }
}
