// The M6 srv-data write side of the fake store: a mailbox role (RFC 8621 section 2.1 "role"; Stalwart crates/jmap/src/mailbox/set.rs: a role belongs to one folder, so giving it to another while a folder still holds it is invalidProperties, and the previous holder is cleared in the same
// Mailbox/set). Kept in its own file so FakeStore.Write.cs (the M2 file) is not edited. Owner srv-data.

namespace Barbican.FakeJmap;

public sealed partial class FakeStore
{
    /// <summary>Sets or clears (null) a folder's role; the SetError type when it is refused, else null. A role another folder holds is invalidProperties until that folder lost it.</summary>
    public string? SetMailboxRole(string id, string? role)
    {
        lock (_lock)
        {
            var m = FindMailboxOrNull(id);
            if (m is null)
            {
                return "notFound";
            }

            if (role is not null && _mailboxes.Exists(x => !ReferenceEquals(x, m) && string.Equals((string?)x["role"], role, StringComparison.Ordinal)))
            {
                return "invalidProperties";
            }

            m["role"] = role;
            Commit([new("Mailbox", id, "updated")]);
            return null;
        }
    }
}
