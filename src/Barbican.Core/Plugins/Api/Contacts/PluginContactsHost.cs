// Mirrors: see IPluginContactsHost. The production host: the app's contact store, writer and address-book service.
using Barbican.Core.Contacts;

namespace Barbican.Core.Plugins.Api.Contacts;

/// <summary>The contact services the Contacts view uses, handed to the add-on methods.</summary>
public sealed class PluginContactsHost(IContactStore store, IContactWriter writer, IAddressBooks books) : IPluginContactsHost
{
    public IContactStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    public IContactWriter Writer { get; } = writer ?? throw new ArgumentNullException(nameof(writer));

    public IAddressBooks Books { get; } = books ?? throw new ArgumentNullException(nameof(books));
}
