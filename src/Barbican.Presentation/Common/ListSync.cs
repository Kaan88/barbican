// Mirrors: Rust listsync.rs (sync_by :42, sync_model :107, sync_into :125, rows_eq :155); design/MOTION.md "Lists".
// The Slint VecModel becomes ObservableCollection<T>; a model that is a BulkObservableCollection<T> resets with ONE notification,
// any other IList<T> gets Clear plus Adds. Owner mailcore.

using System.Collections.ObjectModel;

namespace Barbican.Presentation.Common;

/// <summary>
/// Turns the collection that is already shown into the new rows with the fewest notifications: rows are matched by a key, an unchanged
/// row is not touched, a changed row is set in place, a new one inserted, a gone one removed; a mostly-new list resets once.
/// </summary>
public static class ListSync
{
    /// <summary>Moves allowed before a reorder is treated as a new list (one reset instead of many moves).</summary>
    private const int MaxMoves = 8;

    // MA0051: a faithful port of one algorithm (Rust listsync.rs sync_by); splitting it would hide the order of its steps.
#pragma warning disable MA0051
    /// <summary>Makes <paramref name="model"/> hold <paramref name="rows"/>, matching by <paramref name="key"/> and comparing with <paramref name="same"/>.</summary>
    public static SyncStats SyncBy<T, TKey>(IList<T> model, IReadOnlyList<T> rows, Func<T, TKey> key, Func<T, T, bool> same)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(same);

        var newKeys = new TKey[rows.Count];
        var wanted = new HashSet<TKey>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            newKeys[i] = key(rows[i]);
            wanted.Add(newKeys[i]);
        }

        if (wanted.Count != newKeys.Length)
        {
            return Reset(model, rows);
        }

        // the keys now shown, kept in step with every remove / insert below
        var cur = new List<TKey>(model.Count);
        foreach (var row in model)
        {
            cur.Add(key(row));
        }

        if (cur.Count == 0 && rows.Count == 0)
        {
            return default;
        }

        var kept = cur.Count(wanted.Contains);

        // mostly new content (another folder, a new search, the first fill, a clear): one reset is cheaper than a notification per row
        if (kept * 2 < Math.Max(rows.Count, 1))
        {
            return Reset(model, rows);
        }

        // mostly gone (a filter that keeps a few rows): one reset instead of a remove per row
        var gone = cur.Count - kept;
        if (gone > MaxMoves && gone > kept)
        {
            return Reset(model, rows);
        }

        var set = 0;
        var inserted = 0;
        var removed = 0;
        var moved = 0;

        // 1. drop the rows that are gone, from the end so indices stay valid
        for (var i = cur.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(cur[i]))
            {
                model.RemoveAt(i);
                cur.RemoveAt(i);
                removed++;
            }
        }

        // 2. same key here: update if changed; key further down: move; else insert
        var present = new HashSet<TKey>(cur);
        var comparer = EqualityComparer<TKey>.Default;
        for (var i = 0; i < rows.Count; i++)
        {
            var k = newKeys[i];
            if (i < cur.Count && comparer.Equals(cur[i], k))
            {
                if (!same(model[i], rows[i]))
                {
                    model[i] = rows[i];
                    set++;
                }

                continue;
            }

            if (present.Contains(k))
            {
                var j = -1;
                for (var x = i + 1; x < cur.Count; x++)
                {
                    if (comparer.Equals(cur[x], k))
                    {
                        j = x;
                        break;
                    }
                }

                if (j < 0)
                {
                    return Reset(model, rows);
                }

                moved++;
                if (moved > MaxMoves)
                {
                    return Reset(model, rows);
                }

                model.RemoveAt(j);
                cur.RemoveAt(j);
                model.Insert(i, rows[i]);
                cur.Insert(i, k);
            }
            else
            {
                model.Insert(i, rows[i]);
                cur.Insert(i, k);
                inserted++;
            }
        }

        // every kept key was matched above, so nothing should be left over; be safe anyway
        while (cur.Count > rows.Count)
        {
            model.RemoveAt(cur.Count - 1);
            cur.RemoveAt(cur.Count - 1);
            removed++;
        }

        return new SyncStats(set, inserted, removed, moved);
    }
#pragma warning restore MA0051

    /// <summary><see cref="SyncBy{T, TKey}"/> for rows that compare with equality.</summary>
    public static SyncStats SyncModel<T, TKey>(IList<T> model, IReadOnlyList<T> rows, Func<T, TKey> key)
        where TKey : notnull =>
        SyncBy(model, rows, key, EqualityComparer<T>.Default.Equals);

    /// <summary>Syncs the collection behind a property in place; when there is none yet one is created and handed to <paramref name="install"/>, once.</summary>
    public static SyncStats SyncInto<T, TKey>(
        ObservableCollection<T>? current,
        IReadOnlyList<T> rows,
        Func<T, TKey> key,
        Func<T, T, bool> same,
        Action<ObservableCollection<T>> install)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(install);
        if (current is not null)
        {
            return SyncBy(current, rows, key, same);
        }

        install(new BulkObservableCollection<T>(rows));
        return new SyncStats(Reset: true);
    }

    /// <summary>Two nested collections hold the same rows (by content, not identity).</summary>
    public static bool RowsEqual<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Count == b.Count && a.SequenceEqual(b);
    }

    private static SyncStats Reset<T>(IList<T> model, IReadOnlyList<T> rows)
    {
        if (model is BulkObservableCollection<T> bulk)
        {
            bulk.ReplaceAll(rows);
        }
        else
        {
            model.Clear();
            foreach (var row in rows)
            {
                model.Add(row);
            }
        }

        return new SyncStats(Reset: true);
    }
}
