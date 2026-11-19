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
    private string? _effectiveSort;
    private Dictionary<string, int> _served = new(StringComparer.Ordinal);
    private Dictionary<string, int> _totals = new(StringComparer.Ordinal);
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
        lock (_lock)
        {
            if (after is not null && _query == query)
            {
                from = new Dictionary<string, int>(_served, StringComparer.Ordinal);
            }
        }
        var read = await ReadAsync(query, s => (from.GetValueOrDefault(s.Key), Math.Max(1, query.PageSize)), ct).ConfigureAwait(false);
        if (!read.IsOk)
        {
            return Result<ListPage>.Fail(read.Error!);
        }

        var (pages, fetched, _, effectiveSort, warning) = read.Value;
        var rows = fetched.Take(Math.Max(1, query.PageSize)).ToList();
        lock (_lock)
        {
            var sortKey = MessageListOrder.ComparatorsJson(effectiveSort).ToJsonString();
            if (after is not null && _query == query && !string.Equals(_effectiveSort, sortKey, StringComparison.Ordinal))
            {
                return Result<ListPage>.Fail(new ValidationError("Account ordering changed; refresh the list before loading more."));
            }

            if (after is null || _query != query)
            {
                _query = query;
                _rows = [];
                _effectiveSort = sortKey;
                _served = new Dictionary<string, int>(StringComparer.Ordinal);
                _totals = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            _rows.AddRange(rows.Where(r => !_rows.Exists(x => string.Equals(x.Id, r.Id, StringComparison.Ordinal))));
            foreach (var p in pages)
            {
                _served[p.Key] = from.GetValueOrDefault(p.Key) + rows.Count(row => string.Equals(KeyOf(row), p.Key, StringComparison.Ordinal));
                _unread[p.Key] = p.Unread;
                _totals[p.Key] = p.Total;
            }

            var more = pages.Any(p => _served[p.Key] < p.Total);
            return Result<ListPage>.Ok(new ListPage
            {
                Items = rows,
                Warning = warning,
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
        Dictionary<string, int> totals;
        List<EmailSummary> before;
        lock (_lock)
        {
            (query, served, totals, before) = (_query, new Dictionary<string, int>(_served, StringComparer.Ordinal), new Dictionary<string, int>(_totals, StringComparer.Ordinal), [.. _rows]);
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

        var (pages, _, failed, effectiveSort, _) = read.Value;
        var after = Window(pages, before, served, failed, effectiveSort);
        var retainedTotals = totals.Where(pair => failed.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var page in pages)
        {
            retainedTotals[page.Key] = page.Total;
        }

        var delta = Diff(before, after, Threads(pages), retainedTotals.Values.Sum());
        lock (_lock)
        {
            if (_query != query)
            {
                return Result<ListDelta?>.Ok(null);
            }

            _rows = after;
            _effectiveSort = MessageListOrder.ComparatorsJson(effectiveSort).ToJsonString();
            _totals = retainedTotals;
            _served = after.Where(row => KeyOf(row) is not null).GroupBy(row => KeyOf(row)!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var p in pages)
            {
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
            _totals.Clear();
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
        var commonBefore = before.Where(e => now.Contains(e.Id)).Select(e => e.Id);
        var commonAfter = after.Where(e => old.ContainsKey(e.Id)).Select(e => e.Id);
        if (!commonBefore.SequenceEqual(commonAfter, StringComparer.Ordinal))
        {
            removed = before.Select(e => e.Id).ToList();
            added = after.Select((e, i) => (e.Id, i)).ToList();
            changed = after.ToDictionary(e => e.Id, StringComparer.Ordinal);
        }

        if (removed.Count == 0 && added.Count == 0 && changed.Count == 0)
        {
            return null;
        }

        return new ListDelta { Removed = removed, Added = added, Emails = changed, Threads = threads, Total = total };
    }

    // records holding dictionaries compare by reference: compare what the server sent
    private static bool Same(EmailSummary a, EmailSummary b) =>
        string.Equals(JsonSerializer.Serialize(a, Jmap.Json.JmapJsonContext.Default.EmailSummary), JsonSerializer.Serialize(b, Jmap.Json.JmapJsonContext.Default.EmailSummary), StringComparison.Ordinal);

    // the refreshed window: per source, the rows down to the last row it had served (or as many as it had served when that row is gone). A
    // source that is still read but failed this time keeps the rows it had (a slow server never empties its part of the list); a source that
    // is no longer read at all (signed out, setting turned off) loses its rows. M2 unified review: the failed source's rows used to go.
    private static List<EmailSummary> Window(IReadOnlyList<UnifiedSourcePage> pages, List<EmailSummary> before, Dictionary<string, int> served, HashSet<string> failed, IReadOnlyList<SortComparator>? sort)
    {
        var rows = new List<EmailSummary>(before.Where(r => KeyOf(r) is { } k && failed.Contains(k)));
        foreach (var p in pages)
        {
            var had = served.GetValueOrDefault(p.Key);
            var last = before.LastOrDefault(r => string.Equals(KeyOf(r), p.Key, StringComparison.Ordinal));
            var at = last is null ? -1 : p.Rows.ToList().FindIndex(r => string.Equals(r.Id, last.Id, StringComparison.Ordinal));
            rows.AddRange(p.Rows.Take(at >= 0 ? at + 1 : had));
        }

        return Merge(rows, sort);
    }

    private static string? KeyOf(EmailSummary row) =>
        UnifiedRows.ParseRow(row.Id) is { } id ? UnifiedRows.SourceKey(id.Login, id.SharedAccount) : null;

    private static List<EmailSummary> Merge(IEnumerable<EmailSummary> rows, IReadOnlyList<SortComparator>? sort)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. rows.Where(r => seen.Add(r.Id)).OrderBy(r => r, Comparer<EmailSummary>.Create((a, b) => MessageListOrder.Compare(a, b, sort)))];
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

    private async Task<Result<(List<UnifiedSourcePage> Pages, List<EmailSummary> Rows, HashSet<string> Failed, IReadOnlyList<SortComparator>? Sort, string? Warning)>> ReadAsync(ListQuery query, Func<UnifiedSource, (int Position, int Limit)> window, CancellationToken ct)
    {
        var all = sources();
        if (all.Count == 0)
        {
            return Result<(List<UnifiedSourcePage>, List<EmailSummary>, HashSet<string>, IReadOnlyList<SortComparator>?, string?)>.Fail(new ValidationError("No account is connected."));
        }

        var results = await Task.WhenAll(all.Select(s =>
        {
            var (position, limit) = window(s);
            return FetchWindowAsync(s, query, position, limit, ct);
        })).ConfigureAwait(false);
        var pages = results.Where(r => r.IsOk).Select(r => r.Value!).ToList();
        if (pages.Count == 0)
        {
            return Result<(List<UnifiedSourcePage>, List<EmailSummary>, HashSet<string>, IReadOnlyList<SortComparator>?, string?)>.Fail(new ValidationError(string.Join("; ", results.Select(r => r.Error!.Message))));
        }

        if (query.Sort?.Any(item => item.Keyword is not null) == true && pages.Any(page => page.Warning is not null))
        {
            var fallback = query.Sort.Where(item => item.Keyword is null).ToList();
            if (fallback.Count == 0)
            {
                fallback.Add(new SortComparator("receivedAt", false));
            }

            var reread = await ReadAsync(query with { Sort = fallback }, window, ct).ConfigureAwait(false);
            if (!reread.IsOk)
            {
                return reread;
            }

            var value = reread.Value;
            return Result<(List<UnifiedSourcePage>, List<EmailSummary>, HashSet<string>, IReadOnlyList<SortComparator>?, string?)>.Ok(
                (value.Pages, value.Rows, value.Failed, fallback, "An account does not support keyword ordering; value ordering is used for all accounts."));
        }

        var failed = all.Where((_, i) => !results[i].IsOk).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        return Result<(List<UnifiedSourcePage>, List<EmailSummary>, HashSet<string>, IReadOnlyList<SortComparator>?, string?)>.Ok((pages, Merge(pages.SelectMany(p => p.Rows), query.Sort), failed, query.Sort, null));
    }

    // Servers can cap each query below the requested window. Fill it before merging, otherwise an unseen
    // row from one source can incorrectly follow older rows from another source on the same page.
    private static async Task<Result<UnifiedSourcePage>> FetchWindowAsync(UnifiedSource source, ListQuery query, int position, int limit, CancellationToken ct)
    {
        UnifiedSourcePage? last = null;
        var rows = new List<EmailSummary>();
        var threads = new Dictionary<string, int>(StringComparer.Ordinal);
        string? warning = null;
        while (rows.Count < limit)
        {
            var result = await UnifiedFetcher.PageAsync(source, query, position + rows.Count, limit - rows.Count, ct).ConfigureAwait(false);
            if (!result.IsOk)
            {
                return result;
            }

            last = result.Value!;
            warning ??= last.Warning;
            rows.AddRange(last.Rows);
            foreach (var (id, count) in last.Threads)
            {
                threads[id] = count;
            }

            if (last.Rows.Count == 0 || position + rows.Count >= last.Total)
            {
                break;
            }
        }

        return Result<UnifiedSourcePage>.Ok(last! with { Rows = rows, Threads = threads, Warning = warning });
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
