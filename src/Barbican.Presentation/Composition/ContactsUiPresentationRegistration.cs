// Mirrors: M5 plan section 4.10 (DI). Owner con-ui: the contacts view model.

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
        services.TryAddSingleton<IContactsViewModel, ContactsViewModel>();
        return services;
    }
}
