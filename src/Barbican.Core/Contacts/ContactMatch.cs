// Mirrors: Rust contacts.rs (recipient suggestions from the contact list); Bulwark components/email/recipient-popover.tsx. Contract from the M5 plan section 4.5; owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Contacts;

/// <summary>A contact found by address or text: the account it came from, the card and the matching address.</summary>
public sealed record ContactMatch(string AccountKey, JsonNode Card, string Email);
