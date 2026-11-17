// Mirrors: Rust msgload.rs prefetch :320 (the list rows) and prefetch_top :351 ("a list was loaded: warm the cache with its first
// messages"; tag rust-final). Owner body.
//
// The message list depends on the opener (the loader), so the list is looked up lazily: at start-up (IStartupTask, when every singleton
// can be resolved) and on first use, never while the loader itself is being built.

using System.Collections.Specialized;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Mail.Loading;

/// <summary><see cref="IMessageNeighbours"/> over the message list; raises <see cref="ListLoaded"/> when the top of the list changes.</summary>
public sealed class ListNeighbours(IServiceProvider services) : IMessageNeighbours, IStartupTask, IDisposable
{
    private MessageListViewModel? _list;
    private string? _top;

    public event EventHandler? ListLoaded;

    public IReadOnlyList<MessageRowViewModel> Rows => List() is { } l ? [.. l.Messages] : [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = List();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_list is not null)
        {
            _list.Rows.CollectionChanged -= OnRowsChanged;
        }
    }

    private MessageListViewModel? List()
    {
        if (_list is null)
        {
            _list = services.GetService<MessageListViewModel>();
            if (_list is not null)
            {
                _list.Rows.CollectionChanged += OnRowsChanged;
            }
        }

        return _list;
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var top = _list?.Messages.FirstOrDefault()?.Id;
        if (top is null || string.Equals(top, _top, StringComparison.Ordinal))
        {
            return;
        }

        _top = top;
        ListLoaded?.Invoke(this, EventArgs.Empty);
    }
}
