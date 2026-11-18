// Mirrors: M5 plan section 4.10 (DI). Owner con-data: the store, writer, address books, directory and lookup. Defaults use TryAddSingleton; the composer's IRecipientSources (M3) is decorated with the contacts and groups (D-M5-06);
// the M4 IContactCardReader (cal-edit, registered with TryAdd) takes the contact store as its source because the store is registered here (its constructor has the store as an optional parameter).

using Barbican.Core.Accounts;
using Barbican.Core.Compose.Recipients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Contacts;

/// <summary>Registers the contacts data layer (M5 stream con-data).</summary>
public static class ContactsDataRegistration
{
    public static IServiceCollection AddBarbicanContactsData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IContactStore, ContactStore>();
        services.TryAddSingleton<IAddressBooks, AddressBooks>();
        services.TryAddSingleton<IContactWriter, ContactWriter>();
        services.TryAddSingleton<IContactPhotos, ContactPhotos>();
        services.TryAddSingleton<IContactDirectory, ContactDirectory>();
        services.TryAddSingleton<IContactLookup, ContactLookup>();
        DecorateRecipientSources(services);
        return services;
    }

    /// <summary>Puts <see cref="ContactRecipientSources"/> over whatever <see cref="IRecipientSources"/> is registered now (the Sent-based source of the composer, else none), once.</summary>
    private static void DecorateRecipientSources(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(ContactRecipientSourcesMarker)))
        {
            return;
        }

        services.AddSingleton(new ContactRecipientSourcesMarker());
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IRecipientSources));
        services.Replace(ServiceDescriptor.Singleton<IRecipientSources>(sp => new ContactRecipientSources(
            inner is null ? new EmptyRecipientSources() : Build(inner, sp),
            sp.GetRequiredService<IContactStore>(),
            sp.GetRequiredService<IAccountManager>())));
    }

    private static IRecipientSources Build(ServiceDescriptor d, IServiceProvider sp) =>
        d.ImplementationInstance as IRecipientSources
        ?? (d.ImplementationFactory is { } factory ? (IRecipientSources)factory(sp) : (IRecipientSources)ActivatorUtilities.CreateInstance(sp, d.ImplementationType!));
}
