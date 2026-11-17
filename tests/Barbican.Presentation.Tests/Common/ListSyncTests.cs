// Ported from Rust src/listsync.rs tests (:175-:300).
// The Slint VecModel becomes ObservableCollection<T>; "not a VecModel yet" (the default empty model) becomes a null collection.

using System.Collections.ObjectModel;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Tests.Common;

public sealed class ListSyncTests
{
    private sealed record R(int Id, string V);

    private static ObservableCollection<R> Model(params R[] rows) => new(rows);

    private static SyncStats Sync(ObservableCollection<R> m, params R[] rows) => ListSync.SyncModel(m, rows, x => x.Id);

    [Fact]
    [RustTest("listsync::unchanged_rows_are_not_touched")]
    public void Unchanged_rows_are_not_touched()
    {
        var m = Model(new(1, "a"), new(2, "b"), new(3, "c"));
        var st = Sync(m, new(1, "a"), new(2, "b"), new(3, "c"));
        Assert.True(st.Untouched, st.ToString());
    }

    [Fact]
    [RustTest("listsync::changed_row_is_set_in_place")]
    public void Changed_row_is_set_in_place()
    {
        var m = Model(new(1, "a"), new(2, "b"), new(3, "c"));
        var st = Sync(m, new(1, "a"), new(2, "B"), new(3, "c"));
        Assert.Equal(new SyncStats(Set: 1), st);
        Assert.Equal([new R(1, "a"), new R(2, "B"), new R(3, "c")], m);
    }

    [Fact]
    [RustTest("listsync::new_mail_on_top_is_one_insert")]
    public void New_mail_on_top_is_one_insert()
    {
        var m = Model(new(1, "a"), new(2, "b"), new(3, "c"));
        var st = Sync(m, new(9, "new"), new(1, "a"), new(2, "b"), new(3, "c"));
        Assert.Equal(new SyncStats(Inserted: 1), st);
        Assert.Equal(new R(9, "new"), m[0]);
    }

    [Fact]
    [RustTest("listsync::deleted_row_is_one_remove")]
    public void Deleted_row_is_one_remove()
    {
        var m = Model(new(1, "a"), new(2, "b"), new(3, "c"), new(4, "d"));
        var st = Sync(m, new(1, "a"), new(3, "c"), new(4, "d"));
        Assert.Equal(new SyncStats(Removed: 1), st);
        Assert.Equal([new R(1, "a"), new R(3, "c"), new R(4, "d")], m);
    }

    [Fact]
    [RustTest("listsync::load_more_appends")]
    public void Load_more_appends()
    {
        var m = Model(new(1, "a"), new(2, "b"));
        var st = Sync(m, new(1, "a"), new(2, "b"), new(3, "c"), new(4, "d"));
        Assert.Equal(new SyncStats(Inserted: 2), st);
        Assert.Equal(4, m.Count);
    }

    [Fact]
    [RustTest("listsync::mixed_insert_remove_update_and_move")]
    public void Mixed_insert_remove_update_and_move()
    {
        var m = Model(new(1, "a"), new(2, "b"), new(3, "c"), new(4, "d"), new(5, "e"));

        // 2 deleted, 6 new on top, 4 moved above 3, 5 changed in place
        R[] rows = [new(6, "f"), new(1, "a"), new(4, "d"), new(3, "c"), new(5, "E")];
        var st = Sync(m, rows);
        Assert.Equal(rows, m);
        Assert.Equal(1, st.Removed);
        Assert.Equal(1, st.Inserted);
        Assert.Equal(1, st.Moved);
        Assert.Equal(1, st.Set);
        Assert.False(st.Reset);
    }

    [Fact]
    [RustTest("listsync::mostly_new_list_resets_once")]
    public void Mostly_new_list_resets_once()
    {
        var m = Model(new(1, "a"), new(2, "b"), new(3, "c"));
        R[] rows = [new(7, "x"), new(8, "y"), new(9, "z"), new(1, "a")];
        var st = Sync(m, rows);
        Assert.True(st.Reset);
        Assert.Equal(rows, m);
    }

    [Fact]
    [RustTest("listsync::big_reorder_resets")]
    public void Big_reorder_resets()
    {
        var old = Enumerable.Range(0, 40).Select(i => new R(i, "x")).ToArray();
        var m = Model(old);
        var rows = old.Reverse().ToArray();
        var st = Sync(m, rows);
        Assert.True(st.Reset);
        Assert.Equal(rows, m);
    }

    [Fact]
    [RustTest("listsync::filter_keeping_few_rows_resets")]
    public void Filter_keeping_few_rows_resets()
    {
        var m = Model(Enumerable.Range(0, 40).Select(i => new R(i, "x")).ToArray());
        var rows = Enumerable.Range(0, 5).Select(i => new R(i * 8, "x")).ToArray();
        var st = Sync(m, rows);
        Assert.True(st.Reset);
        Assert.Equal(rows, m);
    }

    [Fact]
    [RustTest("listsync::duplicate_keys_reset")]
    public void Duplicate_keys_reset()
    {
        var m = Model(new(1, "a"), new(2, "b"));
        var st = Sync(m, new(1, "a"), new(1, "b"));
        Assert.True(st.Reset);
        Assert.Equal([new R(1, "a"), new R(1, "b")], m);
    }

    [Fact]
    [RustTest("listsync::empty_and_clear")]
    public void Empty_and_clear()
    {
        var m = Model();
        Assert.True(Sync(m).Untouched);
        var st = Sync(m, new R(1, "a"));
        Assert.Equal([new R(1, "a")], m);
        Assert.True(st.Reset);
        st = Sync(m);
        Assert.True(st.Reset || st.Removed == 1);
        Assert.Empty(m);
    }

    [Fact]
    [RustTest("listsync::sync_into_installs_once_then_syncs_in_place")]
    public void Sync_into_installs_once_then_syncs_in_place()
    {
        ObservableCollection<R>? installed = null;
        var st = ListSync.SyncInto<R, int>(null, [new R(1, "a")], x => x.Id, (a, b) => a == b, m => installed = m);
        Assert.True(st.Reset);
        var cur = installed ?? throw new Xunit.Sdk.XunitException("installed");
        st = ListSync.SyncInto(cur, [new R(1, "a"), new R(2, "b")], x => x.Id, (a, b) => a == b, _ => throw new InvalidOperationException("must sync in place"));
        Assert.Equal(new SyncStats(Inserted: 1), st);
        Assert.Equal(2, cur.Count);
    }

    [Fact]
    [RustTest("listsync::nested_rows_compare_by_content")]
    public void Nested_rows_compare_by_content()
    {
        // Slint's == on ModelRc only compares identity; here the rule is the same: rows rebuilt from data compare by content.
        Assert.True(ListSync.RowsEqual([1, 2], [1, 2]));
        Assert.False(ListSync.RowsEqual([1, 2], [1, 3]));
    }
}
