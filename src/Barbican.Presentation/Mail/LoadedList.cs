// Mirrors: Rust jmap.rs apply_list_delta :1540 (RFC 8620 §5.6), bodycache.rs on_sync :180 (fresh copies, removed ids), mail.rs ListState :21.
// The jmap stream's Core/Sync/ListDeltaApplier does the same; this copy exists so the list works against fakes before that stream lands
// (docs/FABLE-REVIEW.md "M1 stream: mailcore"). Owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>The part of a list that is loaded: the messages in order, the total and the paging cursor.</summary>
public sealed class LoadedList
{
    private readonly List<EmailSummary> _emails = [];

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
    }

    /// <summary>Adds the next page (anchor paging): messages already shown are not added twice.</summary>
    public void Append(ListPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var have = new HashSet<string>(_emails.Select(e => e.Id), StringComparer.Ordinal);
        _emails.AddRange(page.Items.Where(e => have.Add(e.Id)));
        Total = page.Total ?? Total;
        Next = page.Next;
    }

    /// <summary>Applies a delta: remove every removed id, insert the added ones by ascending index, replace rows updated in place, update sizes.</summary>
    public void Apply(ListDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        var had = _emails.Count;
        var removed = new HashSet<string>(delta.Removed, StringComparer.Ordinal);
        _emails.RemoveAll(e => removed.Contains(e.Id));
        foreach (var (id, index) in delta.Added.OrderBy(a => a.Index))
        {
            if (!delta.Emails.TryGetValue(id, out var fresh))
            {
                continue;
            }

            // already present (a server may report an unmoved message as added)
            var at = _emails.FindIndex(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (at >= 0)
            {
                _emails.RemoveAt(at);
            }

            // what is loaded is a prefix of the new list: an index past it is left to paging
            if (index <= _emails.Count)
            {
                _emails.Insert(index, fresh);
            }
        }

        for (var i = 0; i < _emails.Count; i++)
        {
            if (delta.Emails.TryGetValue(_emails[i].Id, out var fresh))
            {
                _emails[i] = fresh;
            }
        }

        ApplyThreadSizes(delta.Threads);
        Total = delta.Total ?? Math.Max((Total + _emails.Count) - had, 0);
        if (delta.QueryState.Length > 0)
        {
            QueryState = delta.QueryState;
        }
    }

    /// <summary>Takes fresh copies of changed messages (keywords, mailboxes) and drops the ones that left.</summary>
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

        var gone = new HashSet<string>(changed.Removed, StringComparer.Ordinal);
        if (gone.Count > 0 && _emails.RemoveAll(e => gone.Contains(e.Id)) > 0)
        {
            Total = Math.Max(Total - 1, 0);
            touched = true;
        }

        return touched;
    }

    private void ApplyThreadSizes(IReadOnlyDictionary<string, int> sizes)
    {
        if (sizes.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _emails.Count; i++)
        {
            if (_emails[i].ThreadId is { } t && sizes.TryGetValue(t, out var size) && size != _emails[i].ThreadCount)
            {
                _emails[i] = _emails[i] with { ThreadCount = size };
            }
        }
    }
}
