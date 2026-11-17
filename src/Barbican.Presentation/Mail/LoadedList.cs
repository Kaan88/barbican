// Mirrors: Rust jmap.rs apply_list_delta :1540 (RFC 8620 §5.6), bodycache.rs on_sync :180 (fresh copies, removed ids), mail.rs ListState :21.
// The delta itself is applied by Core/Sync/ListDeltaApplier, the one implementation of RFC 8620 §5.6 (Fable F-14). Owner mailcore.

using Barbican.Core.Mail.Model;
using Barbican.Core.Sync;

namespace Barbican.Presentation.Mail;

/// <summary>The part of a list that is loaded: the messages in order, the total and the paging cursor.</summary>
public sealed class LoadedList
{
    private readonly List<EmailSummary> _emails = [];
    private readonly Dictionary<string, int> _threads = new(StringComparer.Ordinal);

    public IReadOnlyList<EmailSummary> Emails => _emails;

    /// <summary>How many messages the whole list holds (the server's count).</summary>
    public int Total { get; private set; }

    public ListCursor? Next { get; private set; }

    public string QueryState { get; private set; } = string.Empty;

    /// <summary>Starts over with a first page.</summary>
    public void Replace(ListPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _emails.Clear();
        _emails.AddRange(page.Items);
        Total = page.Total ?? page.Items.Count;
        Next = page.Next;
        QueryState = page.QueryState;
        _threads.Clear();
        foreach (var (t, n) in page.Threads)
        {
            _threads[t] = n;
        }
    }

    /// <summary>Adds the next page (anchor paging): messages already shown are not added twice.</summary>
    public void Append(ListPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var have = new HashSet<string>(_emails.Select(e => e.Id), StringComparer.Ordinal);
        _emails.AddRange(page.Items.Where(e => have.Add(e.Id)));
        Total = page.Total ?? Total;
        Next = page.Next;
        foreach (var (t, n) in page.Threads)
        {
            _threads[t] = n;
        }
    }

    /// <summary>Applies a delta: remove every removed id, insert the added ones by ascending index, replace rows updated in place, update sizes.</summary>
    public void Apply(ListDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        var total = Total;
        ListDeltaApplier.Apply(_emails, ref total, _threads, delta);
        Total = total;
        if (delta.QueryState.Length > 0)
        {
            QueryState = delta.QueryState;
        }
    }

    /// <summary>
    /// Takes fresh copies of changed messages (keywords, mailboxes). Its <c>Removed</c> ids are not applied: they are the removals of the
    /// query that was watched, and after a folder switch one of them may be a message moved into the list now shown; the list's own
    /// <see cref="ListDeltaEvent"/> carries the removals for the right query (Fable F-14).
    /// </summary>
    public bool Apply(EmailsChangedEvent changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        var touched = false;
        for (var i = 0; i < _emails.Count; i++)
        {
            if (changed.Fresh.TryGetValue(_emails[i].Id, out var fresh) && !Equals(_emails[i], fresh))
            {
                _emails[i] = fresh;
                touched = true;
            }
        }

        return touched;
    }
}
