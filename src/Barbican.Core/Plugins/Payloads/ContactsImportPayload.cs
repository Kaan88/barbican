// Mirrors: Bulwark lib/plugin-hook-catalog.ts:197 (onContactsImport: { addressBookId, imported }). Owner con-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>Contacts were imported into an address book: how many were created.</summary>
public sealed record ContactsImportPayload(string AddressBookId, int Imported);
