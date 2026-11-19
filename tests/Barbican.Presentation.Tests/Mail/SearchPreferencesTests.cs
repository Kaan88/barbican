// Bulwark stores/settings-store.ts defaults false/all and lib/search-default-scope.ts.
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Lists;
using Barbican.Presentation.Mail;
using System.Text.Json.Nodes;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class SearchPreferencesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changing_folder_or_tag_closes_the_previous_message(bool tag)
    {
        using var h = new ListHarness();
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Barbican.Core.Common.Result<ListPage>.Ok(Mails.Page(0, 3, 3, false)));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.True(h.List.SelectFirst());
        var closed = h.Opener.Closed;
        if (tag) await h.List.OpenTagAsync("red", "Red", []);
        else await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.Equal(closed + 1, h.Opener.Closed);
        Assert.Null(h.List.Selected);
    }

    [Fact]
    public async Task New_search_uses_all_folders_by_default_and_clearing_returns_to_browsing_folder()
    {
        using var h = new ListHarness();
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        await h.List.SearchAsync("hello");
        Assert.Null(h.Service.Queries[^1].Query.MailboxId);
        Assert.Equal("hello", h.Service.Queries[^1].Query.SearchText);
        await h.List.SearchAsync("");
        Assert.Equal("inbox", h.Service.Queries[^1].Query.MailboxId);
        Assert.Null(h.Service.Queries[^1].Query.SearchText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Folder_scope_and_folder_switch_clearing_follow_preferences(bool clear)
    {
        using var h = new ListHarness("{\"searchDefaultScope\":\"folder\",\"clearSearchOnFolderChange\":" + (clear ? "true" : "false") + "}");
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        await h.List.SearchAsync("hello");
        Assert.Equal("inbox", h.Service.Queries[^1].Query.MailboxId);
        await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.Equal("sent", h.Service.Queries[^1].Query.MailboxId);
        Assert.Equal(clear ? null : "hello", h.Service.Queries[^1].Query.SearchText);
        Assert.Equal(clear ? "" : "hello", h.List.SearchText);
    }
    [Fact]
    public async Task Global_search_page_and_push_watch_share_the_same_shaped_filter()
    {
        using var h = new ListHarness();
        var shaper = new SearchListQueryShaper(() =>
        [
            new JsonObject { ["id"] = "trash", ["role"] = "trash" },
            new JsonObject { ["id"] = "junk", ["role"] = "junk" },
        ]);
        using var list = new MessageListViewModel(h.Active, h.Hub, h.Bus, h.Opener, h.Prefs, h.System, h.Motion, h.Time, shaper: shaper);
        await list.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        await list.SearchAsync("hello");
        var requested = h.Service.Queries[^1].Query;
        Assert.Null(requested.MailboxId);
        Assert.NotNull(requested.Filter);
        Assert.Contains("inMailboxOtherThan", requested.Filter.ToJsonString(), StringComparison.Ordinal);
        Assert.Same(requested.Filter, h.Sync.Watched[^1].Filter);
    }

}
