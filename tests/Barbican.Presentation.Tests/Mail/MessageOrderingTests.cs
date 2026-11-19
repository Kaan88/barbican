// Message ordering changes refetch the full server list; other preferences only rebuild rows.
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class MessageOrderingTests
{
    [Fact]
    public async Task Order_scope_and_live_changes_requery_and_preserve_server_order()
    {
        using var h = new ListHarness("""{"messageListOrder":[{"criterion":"subject","direction":"asc"}],"messageListOrderScope":"inbox"}""");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(new ListPage
        {
            Items = [Mails.Make("old", hoursAgo: 72), Mails.Make("new", hoursAgo: 1)], Total = 2,
        }));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.Equal(new SortComparator("subject", true), h.Service.Queries[^1].Query.Sort![0]);
        Assert.DoesNotContain(h.List.Rows, row => row is GroupHeaderViewModel);
        Assert.Equal(["old", "new"], h.List.Messages.Select(row => row.Id), StringComparer.Ordinal);
        Assert.Equal(h.Service.Queries[^1].Query.Sort, h.Sync.Watched[^1].Sort);
        await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.Equal([new SortComparator("receivedAt", false)], h.Service.Queries[^1].Query.Sort);
        var before = h.Service.Queries.Count;
        h.Prefs.Set("""{"messageListOrder":[{"criterion":"size","direction":"desc"}],"messageListOrderScope":"all"}""");
        Assert.Equal(before + 1, h.Service.Queries.Count);
        Assert.Equal(new SortComparator("size", false), h.Service.Queries[^1].Query.Sort![0]);
        h.Prefs.Set("""{"messageListOrder":[{"criterion":"size","direction":"desc"}],"messageListOrderScope":"all","showPreview":false}""");
        Assert.Equal(before + 1, h.Service.Queries.Count);
    }
}
