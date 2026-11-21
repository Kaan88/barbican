// M2 exit tests of stream search (plan 6.4) over FakeJmap: an operator search sends the right Email/query filter and then SearchSnippet/get; history persists; trash and junk are left out of a global search.
// No Rust test is ported here: these cover the glue (shaper, snippet source, global mail search) the Rust app had inline in main.rs and search_ui.rs.

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Search;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Search;

public sealed class SearchFlowTests
{
    private static async Task<(Fake Fake, MailService Mail, string Inbox)> StartAsync(FakeJmapOptions? options = null)
    {
        var f = await Fake.StartAsync(options);
        var mail = new MailService(f.Client, TimeProvider.System);
        var tree = await mail.GetMailboxesAsync(TestContext.Current.CancellationToken);
        var inbox = tree.Value!.All.First(m => m.Role == MailboxRole.Inbox).Id;
        return (f, mail, inbox);
    }

    private static SearchListQueryShaper ShaperFor(MailService mail, SearchSnippetSource? snippets = null) =>
        new(() => mail.LastMailboxes?.List, snippets);

    [Fact]
    public async Task An_operator_search_sends_the_filter_then_asks_for_snippets()
    {
        var (f, mail, inbox) = await StartAsync();
        var snippets = new SearchSnippetSource(() => f.Client);
        var shaper = ShaperFor(mail, snippets);
        var query = shaper.Shape(new ListQuery { MailboxId = inbox, SearchText = "from:carol has:attachment report" });
        var page = (await mail.QueryAsync(query, null, TestContext.Current.CancellationToken)).Value!;

        Assert.Equal(["Report with attachments"], page.Items.Select(m => m.Subject).ToArray());
        var asked = f.Server.ApiCalls.Last(c => c.Contains("Email/query", StringComparison.Ordinal));
        Assert.Contains("Email/query", asked, StringComparison.Ordinal);

        // the list asks for the row; the source collects it and asks the server once
        var arrived = new TaskCompletionSource<SnippetsArrivedEventArgs>();
        snippets.Arrived += (_, e) => arrived.TrySetResult(e);
        var id = page.Items[0].Id;
        Assert.Null(snippets.SubjectFor(id));
        var got = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains(id, got.EmailIds, StringComparer.Ordinal);
        Assert.Contains(f.Server.ApiCalls, c => c.Contains("SearchSnippet/get", StringComparison.Ordinal));
        var marked = snippets.SubjectFor(id);
        Assert.NotNull(marked);
        Assert.Contains(marked!, s => s.Marked && s.Text.Equals("Report", StringComparison.OrdinalIgnoreCase));
    }

    // I-232: a row that asked under one search must not stay unmarked, or marked for a search that is gone, when the filter changes meanwhile
    [Fact]
    public async Task A_changed_filter_tells_the_rows_to_ask_again_and_a_batch_in_flight_is_asked_again()
    {
        var (f, mail, inbox) = await StartAsync();
        var snippets = new SearchSnippetSource(() => f.Client);
        var first = ShaperFor(mail, snippets).Shape(new ListQuery { MailboxId = inbox, SearchText = "from:carol has:attachment report" });
        var page = (await mail.QueryAsync(first, null, TestContext.Current.CancellationToken)).Value!;
        var id = page.Items[0].Id;
        Assert.Null(snippets.SubjectFor(id));

        // the search changes before the batch answers: the rows are told to draw again
        var told = new TaskCompletionSource<SnippetsArrivedEventArgs>();
        snippets.Arrived += (_, e) => told.TrySetResult(e);
        _ = ShaperFor(mail, snippets).Shape(new ListQuery { MailboxId = inbox, SearchText = "from:carol has:attachment attached" });
        Assert.Contains(id, (await told.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).EmailIds, StringComparer.Ordinal);

        // the redraw asks again under the new filter and the marks of the new search arrive
        var marks = new TaskCompletionSource();
        snippets.Arrived += (_, _) =>
        {
            if (snippets.PreviewFor(id) is not null)
            {
                marks.TrySetResult();
            }
        };
        _ = snippets.PreviewFor(id);
        await marks.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains(snippets.PreviewFor(id)!, s => s.Marked && s.Text.Equals("attached", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_shaper_puts_the_folder_and_the_operators_in_one_filter()
    {
        var (_, mail, inbox) = await StartAsync();
        var shaped = ShaperFor(mail).Shape(new ListQuery { MailboxId = inbox, SearchText = "from:carol is:unread" });
        var expected = JsonNode.Parse($$"""{"operator": "AND", "conditions": [{"inMailbox": "{{inbox}}"}, {"from": "carol"}, {"notKeyword": "$seen"}]}""")!;
        Assert.True(JsonNode.DeepEquals(expected, shaped.Filter), shaped.Filter?.ToJsonString());

        // the list's own conditions (the unread-first split) are added to it, the folder and the text only once
        var filter = ListFilters.For(shaped with { NotKeyword = "$seen" });
        Assert.Equal(2, ((JsonArray)filter["conditions"]!).Count);

        // no text: the query is unchanged (M1 behaviour)
        var plain = new ListQuery { MailboxId = inbox };
        Assert.Same(plain, ShaperFor(mail).Shape(plain));
    }

    [Fact]
    public async Task Has_the_words_and_the_size_bounds_filter_on_the_server()
    {
        var (_, mail, inbox) = await StartAsync(new FakeJmapOptions { SeedM2 = true });
        var shaper = ShaperFor(mail);
        var shaped = shaper.Shape(new ListQuery { MailboxId = inbox, SearchText = "body:winmail larger:100" });
        var expected = JsonNode.Parse($$"""{"operator": "AND", "conditions": [{"inMailbox": "{{inbox}}"}, {"body": "winmail"}, {"minSize": 102400}]}""")!;
        Assert.True(JsonNode.DeepEquals(expected, shaped.Filter), shaped.Filter?.ToJsonString());

        // the word is in the body (not in the subject), and the server applies it
        var page = (await mail.QueryAsync(shaper.Shape(new ListQuery { MailboxId = inbox, SearchText = "body:winmail" }), null, TestContext.Current.CancellationToken)).Value!;
        Assert.Equal([FakeSeed.TnefSubject], page.Items.Select(m => m.Subject).ToArray());

        // minSize is inclusive, maxSize exclusive (RFC 8621 §4.4.1): the 1.6 MB message falls out of maxSize but not of minSize
        var smaller = shaper.Shape(new ListQuery { MailboxId = inbox, SearchText = "smaller:1000" });
        var small = (await mail.QueryAsync(smaller, null, TestContext.Current.CancellationToken)).Value!;
        Assert.DoesNotContain(small.Items, m => string.Equals(m.Subject, FakeSeed.HeavyHtmlSubject, StringComparison.Ordinal));
        Assert.Contains(small.Items, m => string.Equals(m.Subject, FakeSeed.TnefSubject, StringComparison.Ordinal));
        var larger = shaper.Shape(new ListQuery { MailboxId = inbox, SearchText = "larger:1000" });
        var big = (await mail.QueryAsync(larger, null, TestContext.Current.CancellationToken)).Value!;
        Assert.Contains(big.Items, m => string.Equals(m.Subject, FakeSeed.HeavyHtmlSubject, StringComparison.Ordinal));
        Assert.DoesNotContain(big.Items, m => string.Equals(m.Subject, FakeSeed.TnefSubject, StringComparison.Ordinal));
    }

    [Fact]
    public async Task In_trash_searches_the_trash_folder_instead_of_the_open_one()
    {
        var (_, mail, inbox) = await StartAsync();
        var trash = (await mail.GetMailboxesAsync(TestContext.Current.CancellationToken)).Value!.All.First(m => m.Role == MailboxRole.Trash).Id;
        var shaped = ShaperFor(mail).Shape(new ListQuery { MailboxId = inbox, SearchText = "in:trash old" });
        Assert.Equal(trash, (string)shaped.Filter!["conditions"]![0]!["inMailbox"]!);
    }

    [Fact]
    public async Task A_global_mail_search_leaves_out_trash_and_junk()
    {
        var (f, mail, _) = await StartAsync();
        var roles = (await mail.GetRoleIdsAsync(TestContext.Current.CancellationToken)).Value!;
        var all = await GlobalMailSearch.RunAsync(f.Client, GlobalSearch.Parse("example"), roles, [], _ => string.Empty, TestContext.Current.CancellationToken);
        Assert.True(all.IsOk, all.Error?.Message);
        Assert.DoesNotContain(all.Value!, h => h.Title is "Win a prize");
        var junk = await GlobalMailSearch.RunAsync(f.Client, GlobalSearch.Parse("in:junk prize"), roles, [], _ => string.Empty, TestContext.Current.CancellationToken);
        Assert.Contains(junk.Value!, h => h.Title is "Win a prize");
    }

    [Fact]
    public async Task History_persists_ten_newest_without_duplicates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "barbican-history-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = new SearchHistoryStore(dir);
            for (var i = 0; i < 12; i++)
            {
                a.Add($"query {i}");
            }

            a.Add("QUERY 5");
            await a.FlushAsync();
            var b = new SearchHistoryStore(dir);
            var list = b.Load();
            Assert.Equal(SearchHistory.MaxRecent, list.Count);
            Assert.Equal("QUERY 5", list[0]);
            Assert.Single(list, x => x.Equals("query 5", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(dir, SearchHistoryStore.FileName)));
            b.Remove("QUERY 5");
            await b.FlushAsync();
            Assert.DoesNotContain("QUERY 5", new SearchHistoryStore(dir).Load(), StringComparer.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void A_list_query_without_the_shaper_reads_the_same_grammar_for_the_sync_delta()
    {
        var q = new ListQuery { MailboxId = "m1", SearchText = "from:alice has:attachment report" };
        var f = ListFilters.For(q);
        var json = f.ToJsonString();
        Assert.Contains("\"inMailbox\":\"m1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"from\":\"alice\"", json, StringComparison.Ordinal);
        Assert.Contains("\"hasAttachment\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"report\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("from:alice", json, StringComparison.Ordinal);
    }
}
