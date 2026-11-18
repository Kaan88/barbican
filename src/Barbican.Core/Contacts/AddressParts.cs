// Mirrors: Rust vcard.rs:519 address_parts (street, locality, region, postcode, country; flat keys win over the components). Contract from the M5 plan section 4.1; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>The five lines of a JSContact address.</summary>
public sealed record AddressParts(string Street, string Locality, string Region, string Postcode, string Country);
