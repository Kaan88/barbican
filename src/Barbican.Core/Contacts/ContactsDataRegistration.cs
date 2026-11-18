// Mirrors: M5 plan section 4.8 (DI). Owner con-data: the store, writer, address books, directory and lookup. Defaults use TryAddSingleton; the M4 IContactCardReader (cal-edit) is replaced by a store-backed reader by con-data in its own step (D-M5-06).

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
        services.TryAddSingleton<IContactDirectory, ContactDirectory>();
        services.TryAddSingleton<IContactLookup, ContactLookup>();
        return services;
    }
}
