// Mirrors: Rust jmap.rs:1540 apply_list_delta; RFC 8620 §5.6 (/queryChanges: remove every `removed` id, then insert the `added`
// ones by ascending index); Bulwark stores/email-store.ts (list delta). Owner jmap.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Sync;

/// <summary>Applies a list delta to the loaded part of a list.</summary>
public static class ListDeltaApplier
{
    /// <summary>
    /// Removes every <c>removed</c> id, inserts the <c>added</c> ones by ascending index (additions past the loaded rows are left to
    /// paging), replaces rows updated in place, and updates the total and the thread sizes.
    /// </summary>
    public static void Apply(IList<EmailSummary> emails, ref int total, IDictionary<string, int> threads, ListDelta delta)
    {
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(delta);
        var had = emails.Count;
        var removed = delta.Removed.ToHashSet(StringComparer.Ordinal);
        for (var i = emails.Count - 1; i >= 0; i--)
        {
            if (removed.Contains(emails[i].Id))
            {
                emails.RemoveAt(i);
            }
        }

        foreach (var (id, index) in delta.Added.OrderBy(a => a.Index))
        {
            if (!delta.Emails.TryGetValue(id, out var row))
            {
                continue;
            }

            // already present (a server may report an unmoved message as added)
            var at = IndexOf(emails, id);
            if (at >= 0)
            {
                emails.RemoveAt(at);
            }

            // what is loaded is a prefix of the new list: an index past it is left to paging
            if (index <= emails.Count)
            {
                emails.Insert(index, row);
            }
        }

        // messages updated in place (keywords) take their fresh copy
        for (var i = 0; i < emails.Count; i++)
        {
            if (delta.Emails.TryGetValue(emails[i].Id, out var fresh) && !ReferenceEquals(fresh, emails[i]))
            {
                emails[i] = fresh;
            }
        }

        foreach (var t in delta.DestroyedThreads)
        {
            threads.Remove(t);
        }

        foreach (var (t, n) in delta.Threads)
        {
            threads[t] = n;
        }

        CarryThreadCounts(emails, threads);
        total = delta.Total ?? Math.Max(0, total + emails.Count - had);
    }

    /// <summary>The rows carry their conversation size (a fresh row of an unchanged thread must not fall back to 1).</summary>
    private static void CarryThreadCounts(IList<EmailSummary> emails, IDictionary<string, int> threads)
    {
        for (var i = 0; i < emails.Count; i++)
        {
            if (emails[i].ThreadId is { } t && threads.TryGetValue(t, out var n) && emails[i].ThreadCount != n)
            {
                emails[i] = emails[i] with { ThreadCount = n };
            }
        }
    }

    private static int IndexOf(IList<EmailSummary> emails, string id)
    {
        for (var i = 0; i < emails.Count; i++)
        {
            if (string.Equals(emails[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
