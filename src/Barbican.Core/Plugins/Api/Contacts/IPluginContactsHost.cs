// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1021-1090 (contact.* and addressbook.* call the signed-in client; Barbican calls the store and writers the Contacts view uses, so hooks run and the
// open view updates). Stalwart crates/jmap/src/contact/{get,set}.rs, addressbook/{get,set}.rs. Nothing here is raw JMAP: reads come from the store the views show, writes go through the writers the views use.
using Barbican.Core.Contacts;

namespace Barbican.Core.Plugins.Api.Contacts;

/// <summary>The contact services the add-on contact and address-book methods run on.</summary>
public interface IPluginContactsHost
{
    IContactStore Store { get; }

    IContactWriter Writer { get; }

    IAddressBooks Books { get; }
}
