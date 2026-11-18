// Mirrors: Bulwark lib/plugin-hook-catalog.ts:201 (onContactMove: { contactId, toAddressBookId }). Owner con-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A contact moved to another address book.</summary>
public sealed record ContactMovePayload(string ContactId, string ToAddressBookId);
