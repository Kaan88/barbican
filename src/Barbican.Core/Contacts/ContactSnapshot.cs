// Mirrors: Rust contacts.rs (load: AddressBook/get, ContactCard/query + get_all in chunks); Bulwark stores/contacts-store.ts. Contract from the M5 plan section 4.2; owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Contacts;

/// <summary>Everything the contacts app knows of one login: address books and cards (own and shared accounts with the contacts capability), as JSContact objects, with the state strings for /changes.</summary>
public sealed record ContactSnapshot(string AccountKey, IReadOnlyList<JsonNode> Books, IReadOnlyList<JsonNode> Cards, string? BookState, string? CardState);
