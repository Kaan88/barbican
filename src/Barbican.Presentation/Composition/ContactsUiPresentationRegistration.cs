// Mirrors: M5 plan section 4.10 (DI). Owner con-ui: the contacts view model, its account source, the actions, the commands, the menus.

using Barbican.Core.Accounts;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Contacts;
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
        return services;
    }
}
