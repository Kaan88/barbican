// Mirrors: Rust unified.rs:361 fetch (every source in parallel, per-source position and limit, errors only when nothing came back, the
// rows merged by parsed receivedAt), :349 positions (each source's next page starts after the rows it has given), mail.rs on_unified_push
// (a push re-reads the loaded window). Bulwark lib/unified-mailbox.ts (the same merge). M2 plan 4.4 (IListSource), 6.5 (a push inserts a
// row without a reset). Owner unified.
// The list engine of one account cannot follow the unified list (its query names no real mailbox), so this source keeps what it served and
// RefreshAsync turns a fresh read of the same window into a ListDelta (removed rows, added rows at their index, fresh copies).

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Unified;

/// <summary>The unified inbox as a list source.</summary>
/// <param name="sources">The sources to read now (the settings and the signed-in logins decide).</param>
public sealed class UnifiedListSource(Func<IReadOnlyList<UnifiedSource>> sources) : IListSource
{
    private const string CursorPrefix = "unified:";

    // the extra rows read per source on a refresh, so new mail at the top does not push loaded rows out of the window
    private const int Slack = 20;

    private readonly Lock _lock = new();
    private ListQuery? _query;
    private List<EmailSummary> _rows = [];
    private Dictionary<string, int> _served = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _unread = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public System.Threading.Channels.ChannelReader<MailSyncEvent>? Events => null;

    /// <summary>The query of the window this source serves, or null before the first page.</summary>
    public ListQuery? Query
    {
        get
        {
            lock (_lock)
            {
                return _query;
            }
        }
    }

    /// <summary>The inbox unread of each source at the last read (source key to count).</summary>
    public IReadOnlyDictionary<string, long> Unread
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, long>(_unread, StringComparer.Ordinal);
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<ListPage>> QueryAsync(ListQuery query, ListCursor? after, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var from = Positions(after);
        var read = await ReadAsync(query, s => (from.GetValueOrDefault(s.Key), query.PageSize), ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<ListPage>.Fail(read.Error!);
        }

        var (pages, rows) = read.Value;
        lock (_lock)
        {
            if (after is null || _query != query)
            {
                _query = query;
                _rows = [];
                _served = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            _rows.AddRange(rows.Where(r => !_rows.Exists(x => string.Equals(x.Id, r.Id, StringComparison.Ordinal))));
            foreach (var p in pages)
            {
                _served[p.Key] = from.GetValueOrDefault(p.Key) + p.Rows.Count;
                _unread[p.Key] = p.Unread;
            }

            var more = pages.Any(p => _served[p.Key] < p.Total);
            return Result<ListPage>.Ok(new ListPage
            {
                Items = rows,
                Total = pages.Sum(p => p.Total),
                Next = more ? Cursor(_served) : null,
                Threads = Threads(pages),
                Position = from.Values.Sum(),
            });
        }
    }

    /// <summary>
    /// Reads the served window again and returns how it changed, or null when nothing was served or nothing changed. A source that fails
    /// keeps its rows (a refresh never empties the list because one server is slow).
    /// </summary>
    public async Task<Result<ListDelta?>> RefreshAsync(CancellationToken ct)
    {
        ListQuery? query;
        Dictionary<string, int> served;
        List<EmailSummary> before;
        lock (_lock)
        {
            (query, served, before) = (_query, new Dictionary<string, int>(_served, StringComparer.Ordinal), [.. _rows]);
        }

        if (query is null)
        {
            return Result<ListDelta?>.Ok(null);
        }

        var read = await ReadAsync(query, s => (0, served.GetValueOrDefault(s.Key) + Slack), ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<ListDelta?>.Fail(read.Error!);
        }

        var (pages, _) = read.Value;
        var after = Window(pages, before, served);
        var delta = Diff(before, after, Threads(pages), pages.Sum(p => p.Total));
        lock (_lock)
        {
            if (_query != query)
            {
                return Result<ListDelta?>.Ok(null);
            }

            _rows = after;
            foreach (var p in pages)
            {
                _served[p.Key] = after.Count(r => string.Equals(UnifiedRows.ParseRow(r.Id) is { } id ? UnifiedRows.SourceKey(id.Login, id.SharedAccount) : null, p.Key, StringComparison.Ordinal));
                _unread[p.Key] = p.Unread;
            }
        }

        return Result<ListDelta?>.Ok(delta);
    }

    /// <summary>Forgets the served window (the list shows something else now).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _query = null;
            _rows = [];
            _served = new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    /// <summary>The changes from <paramref name="before"/> to <paramref name="after"/> as a list delta (null when nothing changed).</summary>
    public static ListDelta? Diff(IReadOnlyList<EmailSummary> before, IReadOnlyList<EmailSummary> after, IReadOnlyDictionary<string, int> threads, int total)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var old = before.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var now = after.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var removed = before.Where(e => !now.Contains(e.Id)).Select(e => e.Id).ToList();
        var added = after.Select((e, i) => (e.Id, i)).Where(x => !old.ContainsKey(x.Id)).ToList();
        var changed = after.Where(e => !old.TryGetValue(e.Id, out var o) || !Same(o, e)).ToDictionary(e => e.Id, StringComparer.Ordinal);
        var moved = removed.Count == 0 && added.Count == 0 && !before.Select(e => e.Id).SequenceEqual(after.Select(e => e.Id), StringComparer.Ordinal);
        if (removed.Count == 0 && added.Count == 0 && changed.Count == 0 && !moved)
        {
            return null;
        }

        return new ListDelta { Removed = removed, Added = added, Emails = changed, Threads = threads, Total = total };
    }

    // records holding dictionaries compare by reference: compare what the server sent
    private static bool Same(EmailSummary a, EmailSummary b) =>
        string.Equals(JsonSerializer.Serialize(a, Jmap.Json.JmapJsonContext.Default.EmailSummary), JsonSerializer.Serialize(b, Jmap.Json.JmapJsonContext.Default.EmailSummary), StringComparison.Ordinal);

    // the refreshed window: per source, the rows down to the last row it had served (or as many as it had served when that row is gone)
    private static List<EmailSummary> Window(IReadOnlyList<UnifiedSourcePage> pages, List<EmailSummary> before, Dictionary<string, int> served)
    {
        var rows = new List<EmailSummary>();
        foreach (var p in pages)
        {
            var had = served.GetValueOrDefault(p.Key);
            var last = before.LastOrDefault(r => UnifiedRows.ParseRow(r.Id) is { } id && string.Equals(UnifiedRows.SourceKey(id.Login, id.SharedAccount), p.Key, StringComparison.Ordinal));
            var at = last is null ? -1 : p.Rows.ToList().FindIndex(r => string.Equals(r.Id, last.Id, StringComparison.Ordinal));
            rows.AddRange(p.Rows.Take(at >= 0 ? at + 1 : had));
        }

        return Merge(rows);
    }

    private static List<EmailSummary> Merge(IEnumerable<EmailSummary> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. rows.Where(r => seen.Add(r.Id)).OrderByDescending(r => r.ReceivedAt ?? DateTimeOffset.MinValue)];
    }

    private static Dictionary<string, int> Threads(IEnumerable<UnifiedSourcePage> pages)
    {
        var threads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (k, v) in pages.SelectMany(p => p.Threads))
        {
            threads[k] = v;
        }

        return threads;
    }

    private async Task<Result<(List<UnifiedSourcePage> Pages, List<EmailSummary> Rows)>> ReadAsync(ListQuery query, Func<UnifiedSource, (int Position, int Limit)> window, CancellationToken ct)
    {
        var all = sources();
        if (all.Count == 0)
        {
            return Result<(List<UnifiedSourcePage>, List<EmailSummary>)>.Fail(new ValidationError("No account is connected."));
        }

        var results = await Task.WhenAll(all.Select(s =>
        {
            var (position, limit) = window(s);
            return UnifiedFetcher.PageAsync(s, query, position, limit, ct);
        })).ConfigureAwait(false);
        var pages = results.Where(r => r.IsOk).Select(r => r.Value!).ToList();
        if (pages.Count == 0)
        {
            return Result<(List<UnifiedSourcePage>, List<EmailSummary>)>.Fail(new ValidationError(string.Join("; ", results.Select(r => r.Error!.Message))));
        }

        return Result<(List<UnifiedSourcePage>, List<EmailSummary>)>.Ok((pages, Merge(pages.SelectMany(p => p.Rows))));
    }

    private static Dictionary<string, int> Positions(ListCursor? after)
    {
        if (after?.AnchorId is not { } anchor || !anchor.StartsWith(CursorPrefix, StringComparison.Ordinal))
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        try
        {
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            if (JsonNode.Parse(anchor[CursorPrefix.Length..]) is JsonObject o)
            {
                foreach (var (k, v) in o)
                {
                    positions[k] = v is JsonValue n && n.TryGetValue<int>(out var i) ? i : 0;
                }
            }

            return positions;
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    private static ListCursor Cursor(Dictionary<string, int> served)
    {
        var o = new JsonObject();
        foreach (var (k, v) in served)
        {
            o[k] = v;
        }

        return new ListCursor(CursorPrefix + o.ToJsonString()) { Position = served.Values.Sum() };
    }
}
