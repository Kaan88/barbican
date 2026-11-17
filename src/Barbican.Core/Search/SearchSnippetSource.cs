// Mirrors: M2 plan 4.4 (ISnippetSource: search provides, actions renders the marks, I-232); Rust search_ui.rs:145 fetch_snippets; Bulwark lib/search-snippet.ts. J-056.
// The page is not known to this class (the list asks it row by row), so rows are collected for a few milliseconds and asked for in one SearchSnippet/get.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;

namespace Barbican.Core.Search;

/// <summary>Snippets of the open search: the list asks per row, the server is asked once per batch of rows.</summary>
public sealed class SearchSnippetSource : ISnippetSource
{
    private static readonly TimeSpan Batch = TimeSpan.FromMilliseconds(25);
    private readonly Func<IJmapClient?> _client;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, SnippetEntry?> _byId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private JsonNode? _filter;
    private string _filterKey = string.Empty;
    private int _generation;
    private bool _scheduled;

    /// <summary>A source asking <paramref name="client"/> (the active account's client at the time of the call).</summary>
    public SearchSnippetSource(Func<IJmapClient?> client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public event EventHandler<SnippetsArrivedEventArgs>? Arrived;

    /// <summary>The filter of the search now open; null when the list shows a folder. A different filter forgets the snippets.</summary>
    public void SetFilter(JsonNode? filter)
    {
        var key = filter?.ToJsonString() ?? string.Empty;
        lock (_lock)
        {
            if (string.Equals(key, _filterKey, StringComparison.Ordinal))
            {
                return;
            }

            _filterKey = key;
            _filter = filter?.DeepClone();
            _generation++;
            _byId.Clear();
            _pending.Clear();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TextSegment>? SubjectFor(string emailId) => Segments(emailId, e => e.Subject);

    /// <inheritdoc />
    public IReadOnlyList<TextSegment>? PreviewFor(string emailId) => Segments(emailId, e => e.Preview);

    private IReadOnlyList<TextSegment>? Segments(string emailId, Func<SnippetEntry, string?> part)
    {
        SnippetEntry? entry;
        lock (_lock)
        {
            var known = _byId.TryGetValue(emailId, out entry);
            if (!known && _filter is not null && GlobalSearch.HasSnippetTerms(_filter) && _pending.Add(emailId) && !_scheduled)
            {
                _scheduled = true;
                _ = Task.Run(RunBatchAsync);
            }
        }

        if (entry is null || part(entry) is not { } raw)
        {
            return null;
        }

        var segments = GlobalSearch.SnippetSegments(raw);
        return GlobalSearch.HasMarked(segments) ? segments : null;
    }

    private async Task RunBatchAsync()
    {
        await Task.Delay(Batch).ConfigureAwait(false);
        List<string> ids;
        JsonNode? filter;
        int generation;
        lock (_lock)
        {
            ids = [.. _pending];
            filter = _filter;
            generation = _generation;
            _pending.Clear();
            _scheduled = false;
        }

        if (filter is null || ids.Count == 0 || _client() is not { } client)
        {
            return;
        }

        var r = await SearchSnippetFetcher.FetchAsync(client, filter, ids, CancellationToken.None).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return;
        }

        lock (_lock)
        {
            if (generation != _generation)
            {
                return;
            }

            foreach (var id in ids)
            {
                _byId[id] = null;
            }

            foreach (var e in r.Value!)
            {
                _byId[e.EmailId] = e;
            }
        }

        Arrived?.Invoke(this, new SnippetsArrivedEventArgs(ids));
    }
}
