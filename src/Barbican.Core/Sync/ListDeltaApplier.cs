// Mirrors: Rust jmap.rs:1540 apply_list_delta; RFC 8620 §5.6 (/queryChanges). Stub from F2; owner jmap.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Sync;

/// <summary>Applies a list delta to the loaded part of a list.</summary>
public static class ListDeltaApplier
{
    /// <summary>
    /// Removes every <c>removed</c> id, inserts the <c>added</c> ones by ascending index (additions past the loaded rows are left to
    /// paging), replaces rows updated in place, and updates the total and the thread sizes.
    /// </summary>
    public static void Apply(IList<EmailSummary> emails, ref int total, IDictionary<string, int> threads, ListDelta delta) =>
        throw new PortPendingException("ListDeltaApplier.Apply: jmap stream");
}
