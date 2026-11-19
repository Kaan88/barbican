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

    /// <summary>
    /// Optimistic removal (a delete, move or archive the server has not answered yet): the messages leave at once and the total drops by what
    /// left. Returns each removed message with its index, so a failed write puts it back where it was. A push delta that names the same ids
    /// afterwards finds nothing to remove and, without a total of its own, leaves the count alone: it does not apply twice.
    /// </summary>
    public IReadOnlyList<(int Index, EmailSummary Email)> Remove(IReadOnlyCollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var gone = new HashSet<string>(ids, StringComparer.Ordinal);
        var removed = new List<(int, EmailSummary)>();
        for (var i = 0; i < _emails.Count; i++)
        {
            if (gone.Contains(_emails[i].Id))
            {
                removed.Add((i, _emails[i]));
            }
        }

        for (var i = removed.Count - 1; i >= 0; i--)
        {
            _emails.RemoveAt(removed[i].Item1);
        }

        Total = Math.Max(0, Total - removed.Count);
        return removed;
    }

    /// <summary>Puts messages back at the indices <see cref="Remove"/> returned, and the total with them.</summary>
    public void Restore(IReadOnlyList<(int Index, EmailSummary Email)> removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        foreach (var (index, email) in removed.OrderBy(r => r.Index))
        {
            if (_emails.Exists(e => string.Equals(e.Id, email.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            _emails.Insert(Math.Min(index, _emails.Count), email);
            Total++;
        }
    }

    /// <summary>Replaces each loaded message among <paramref name="ids"/> by <paramref name="change"/> of it; returns what each one was.</summary>
    public IReadOnlyList<EmailSummary> Patch(IReadOnlyCollection<string> ids, Func<EmailSummary, EmailSummary> change)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(change);
        var before = new List<EmailSummary>();
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
        for (var i = 0; i < _emails.Count; i++)
        {
            if (wanted.Contains(_emails[i].Id))
            {
                before.Add(_emails[i]);
                _emails[i] = change(_emails[i]);
            }
        }

        return before;
    }

    /// <summary>Puts earlier copies of messages back (a failed write): each takes the place of the loaded message with its id.</summary>
    public void Put(IReadOnlyList<EmailSummary> copies)
    {
        ArgumentNullException.ThrowIfNull(copies);
        foreach (var copy in copies)
        {
            var at = _emails.FindIndex(e => string.Equals(e.Id, copy.Id, StringComparison.Ordinal));
            if (at >= 0)
            {
                _emails[at] = copy;
            }
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
            if (!changed.Fresh.TryGetValue(_emails[i].Id, out var fresh)) continue;
            if (fresh.ThreadId is { } thread && _threads.TryGetValue(thread, out var count) && fresh.ThreadCount != count)
            {
                fresh = fresh with { ThreadCount = count };
            }

            if (!Equals(_emails[i], fresh))
            {
                _emails[i] = fresh;
                touched = true;
            }
        }

        return touched;
    }
}
