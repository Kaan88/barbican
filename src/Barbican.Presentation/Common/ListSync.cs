// Mirrors: Rust listsync.rs (sync_by :42, sync_model :107, sync_into :125, rows_eq :155); design/MOTION.md "Lists".
// The Slint VecModel becomes ObservableCollection<T>. Contract stub from F2; owner mailcore.

using Barbican.Core.Common;
using System.Collections.ObjectModel;

namespace Barbican.Presentation.Common;

/// <summary>
/// Turns the collection that is already shown into the new rows with the fewest notifications: rows are matched by a key, an unchanged
/// row is not touched, a changed row is set in place, a new one inserted, a gone one removed; a mostly-new list resets once.
/// </summary>
public static class ListSync
{
    /// <summary>Makes <paramref name="model"/> hold <paramref name="rows"/>, matching by <paramref name="key"/> and comparing with <paramref name="same"/>.</summary>
    public static SyncStats SyncBy<T, TKey>(IList<T> model, IReadOnlyList<T> rows, Func<T, TKey> key, Func<T, T, bool> same)
        where TKey : notnull =>
        throw new PortPendingException("ListSync.SyncBy: mailcore stream");

    /// <summary><see cref="SyncBy{T, TKey}"/> for rows that compare with equality.</summary>
    public static SyncStats SyncModel<T, TKey>(IList<T> model, IReadOnlyList<T> rows, Func<T, TKey> key)
        where TKey : notnull =>
        throw new PortPendingException("ListSync.SyncModel: mailcore stream");

    /// <summary>Syncs the collection behind a property in place; when there is none yet one is created and handed to <paramref name="install"/>, once.</summary>
    public static SyncStats SyncInto<T, TKey>(
        ObservableCollection<T>? current,
        IReadOnlyList<T> rows,
        Func<T, TKey> key,
        Func<T, T, bool> same,
        Action<ObservableCollection<T>> install)
        where TKey : notnull =>
        throw new PortPendingException("ListSync.SyncInto: mailcore stream");

    /// <summary>Two nested collections hold the same rows (by content, not identity).</summary>
    public static bool RowsEqual<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) => throw new PortPendingException("ListSync.RowsEqual: mailcore stream");
}
