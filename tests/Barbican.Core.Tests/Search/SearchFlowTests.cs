// M2 exit tests of stream search (plan 6.4) over FakeJmap: an operator search sends the right Email/query filter and then SearchSnippet/get; history persists; trash and junk are left out of a global search.
// No Rust test is ported here: these cover the glue (shaper, snippet source, global mail search) the Rust app had inline in main.rs and search_ui.rs.

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Search;
using Barbican.Core.Tests.Jmap;

namespace Barbican.Core.Tests.Search;

public sealed class SearchFlowTests
{
    private static async Task<(Fake Fake, MailService Mail, string Inbox)> StartAsync()
    {
        var f = await Fake.StartAsync();
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
    public void History_persists_ten_newest_without_duplicates()
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
            var b = new SearchHistoryStore(dir);
            var list = b.Load();
            Assert.Equal(SearchHistory.MaxRecent, list.Count);
            Assert.Equal("QUERY 5", list[0]);
            Assert.Single(list, x => x.Equals("query 5", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(dir, SearchHistoryStore.FileName)));
            b.Remove("QUERY 5");
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
}
