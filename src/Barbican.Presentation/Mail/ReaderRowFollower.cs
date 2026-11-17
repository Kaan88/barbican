// Mirrors: Bulwark email-viewer.tsx:2132 (the header's tags come from the live email in the store, so a tag change shows at once). Owner mailcore.
// M2 hotfix 2: the reader header kept the tags of the moment the message opened.

using System.Collections.Specialized;
using Barbican.Presentation.Composition;

namespace Barbican.Presentation.Mail;

/// <summary>When the list puts a new row in place of the open message's row, the reader header takes its tags and star.</summary>
public sealed class ReaderRowFollower : IStartupTask, IDisposable
{
    private readonly MessageListViewModel _list;
    private readonly ReaderViewModel _reader;

    public ReaderRowFollower(MessageListViewModel list, ReaderViewModel reader)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(reader);
        _list = list;
        _reader = reader;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _list.Rows.CollectionChanged += OnRowsChanged;
        return Task.CompletedTask;
    }

    public void Dispose() => _list.Rows.CollectionChanged -= OnRowsChanged;

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var open = _reader.EmailId;
        if (open.Length == 0)
        {
            return;
        }

        // a replace names the new row; anything else (a reset after a bulk change) is looked up
        var row = e.Action == NotifyCollectionChangedAction.Replace
            ? e.NewItems?.OfType<MessageRowViewModel>().FirstOrDefault(r => string.Equals(r.Id, open, StringComparison.Ordinal))
            : _list.Messages.FirstOrDefault(r => string.Equals(r.Id, open, StringComparison.Ordinal));
        if (row is not null)
        {
            _reader.FollowRow(row);
        }
    }
}
