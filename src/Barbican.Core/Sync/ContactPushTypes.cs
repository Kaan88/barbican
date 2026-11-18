// Mirrors: PORT-PLAN section 3.4 (push types for AddressBook and ContactCard through the generic /changes engine), M5 plan 3.1 (the push type list line of the M1 code), RFC 8620 7.1 (StateChange types), RFC 9610. G-044. Owner con-data.

using Barbican.Core.Contacts;

namespace Barbican.Core.Sync;

/// <summary>The contact data types the event stream is asked for besides the mail ones (a login with the contacts capability only).</summary>
public static class ContactPushTypes
{
    /// <summary>The capability a login needs for these types.</summary>
    public const string Capability = ContactOptions.Capability;

    public static readonly IReadOnlyList<string> Types = ["AddressBook", "ContactCard"];
}
