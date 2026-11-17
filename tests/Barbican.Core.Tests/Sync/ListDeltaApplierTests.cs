// Ported from Rust src/jmap.rs tests list_delta_inserts_removes_and_updates (:1761), list_delta_leaves_far_additions_to_paging (:1784)
// and list_delta_refreshes_rows_updated_in_place (:1799).

using Barbican.Core.Mail.Model;
using Barbican.Core.Sync;

namespace Barbican.Core.Tests.Sync;

public sealed class ListDeltaApplierTests
{
    private static EmailSummary Em(string id) => new() { Id = id, ThreadId = $"t{id}" };

    private static Dictionary<string, EmailSummary> Fresh(params EmailSummary[] rows) => rows.ToDictionary(r => r.Id, StringComparer.Ordinal);

    [Fact]
    [RustTest("jmap::list_delta_inserts_removes_and_updates")]
    public void List_delta_inserts_removes_and_updates()
    {
        List<EmailSummary> emails = [Em("1"), Em("2"), Em("3"), Em("4")];
        var total = 10;
        var threads = new Dictionary<string, int>(StringComparer.Ordinal) { ["t2"] = 2 };
        var fresh3 = Em("3") with { Keywords = Keywords.Of("$seen") };
        var d = new ListDelta
        {
            // 2 left the list; 3 was updated (re-added at the same place); n is new at the top
            Removed = ["2", "3"],
            Added = [("3", 2), ("n", 0)],
            Emails = Fresh(Em("n"), fresh3),
            Threads = new Dictionary<string, int>(StringComparer.Ordinal) { ["tn"] = 1 },
            DestroyedThreads = ["t2"],
            Total = 10,
        };
        ListDeltaApplier.Apply(emails, ref total, threads, d);
        Assert.Equal(["n", "1", "3", "4"], emails.Select(e => e.Id), StringComparer.Ordinal);
        Assert.True(emails[2].Keywords.IsSet("$seen"));
        Assert.Equal(10, total);
        Assert.False(threads.ContainsKey("t2"));
        Assert.Equal(1, threads["tn"]);
    }

    [Fact]
    [RustTest("jmap::list_delta_leaves_far_additions_to_paging")]
    public void List_delta_leaves_far_additions_to_paging()
    {
        List<EmailSummary> emails = [Em("1"), Em("2")];
        var total = 50;
        var threads = new Dictionary<string, int>(StringComparer.Ordinal);
        var d = new ListDelta
        {
            Added = [("x", 2), ("y", 40)],
            Emails = Fresh(Em("x"), Em("y")),
            Total = 52,
        };
        ListDeltaApplier.Apply(emails, ref total, threads, d);
        Assert.Equal(["1", "2", "x"], emails.Select(e => e.Id), StringComparer.Ordinal);
        Assert.Equal(52, total);
    }

    [Fact]
    [RustTest("jmap::list_delta_refreshes_rows_updated_in_place")]
    public void List_delta_refreshes_rows_updated_in_place()
    {
        // immutable filters: queryChanges reports nothing, Email/changes the keyword change
        List<EmailSummary> emails = [Em("1")];
        var fresh = Em("1") with { Keywords = Keywords.Of("$flagged") };
        var d = new ListDelta { Emails = Fresh(fresh) };
        var total = 1;
        var threads = new Dictionary<string, int>(StringComparer.Ordinal);
        ListDeltaApplier.Apply(emails, ref total, threads, d);
        Assert.True(emails[0].Keywords.IsSet("$flagged"));
        Assert.Equal(1, total);
    }
}
