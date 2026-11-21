// Bulwark thread-list-item.tsx:912-923 (pinned), email-list.tsx:421-466 (folded date groups), search-result-count.tsx:14-31 (the results line), sidebar.tsx:263-284 (role tints) and
// focus-rescue.tsx (AX-03): the list and sidebar rows the M6b parity pass added (ML-01, ML-05, SR-04, SB-04, AX-03).
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class ListParityTests
{
    private static ListPage ThreeDays(bool more = false) => new()
    {
        Items = [Mails.Make("a1", hoursAgo: 1), Mails.Make("a2", hoursAgo: 2), Mails.Make("b1", hoursAgo: 30), Mails.Make("c1", hoursAgo: 24 * 10)],
        Total = 4,
        Next = more ? new ListCursor("c1") : null,
    };

    [Fact]
    public void A_pinned_message_shows_the_pin_and_a_change_replaces_the_row()
    {
        var plain = new MessageRowViewModel(Mails.Make("a"));
        var pinned = new MessageRowViewModel(Mails.Make("a") with { Keywords = Keywords.Of("$seen", "$pinned") });
        Assert.False(plain.Pinned);
        Assert.True(pinned.Pinned);
        Assert.False(plain.SameAs(pinned));
    }

    [Theory]
    [InlineData(0, 0, false, true, "No results found")]
    [InlineData(1, 1, false, true, "1 conversation found")]
    [InlineData(3, 3, false, true, "3 conversations found")]
    [InlineData(3, 10, false, true, "3 conversations (10 messages)")]
    [InlineData(1, 2, false, true, "1 conversation (2 messages)")]
    [InlineData(25, 40, true, true, "25+ conversations")]
    [InlineData(1, 1, false, false, "1 result found")]
    [InlineData(7, 7, false, false, "7 results found")]
    [InlineData(100, 100, true, false, "100+ results found")]
    public void The_results_line_counts_conversations_and_messages_as_Bulwark_does(int conversations, int messages, bool more, bool threading, string expected) =>
        Assert.Equal(expected, SearchResultCount.Text(conversations, messages, more, threading));

    [Fact]
    public async Task The_search_line_shows_only_for_a_search_that_has_loaded()
    {
        using var h = new ListHarness();
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(ThreeDays()));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.Null(h.List.SearchCountText);
        await h.List.SearchAsync("subject");
        Assert.Equal("4 conversations found", h.List.SearchCountText);
        await h.List.SearchAsync(string.Empty);
        Assert.Null(h.List.SearchCountText);
    }

    [Fact]
    public async Task A_search_with_more_pages_says_so()
    {
        using var h = new ListHarness();
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(ThreeDays(more: true)));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        await h.List.SearchAsync("subject");
        Assert.Equal("4+ conversations", h.List.SearchCountText);
    }

    [Fact]
    public async Task Folding_a_date_group_drops_its_rows_and_opening_it_brings_them_back()
    {
        using var h = new ListHarness("""{"groupListByDate":true}""");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(ThreeDays()));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        var headers = h.List.Rows.OfType<GroupHeaderViewModel>().ToList();
        Assert.Equal(3, headers.Count);
        Assert.All(headers, header => Assert.True(header.Foldable));
        Assert.Equal(7, h.List.Rows.Count);

        h.List.ToggleGroup(headers[0].Key);
        Assert.Equal(5, h.List.Rows.Count);
        Assert.True(h.List.Rows.OfType<GroupHeaderViewModel>().First().Collapsed);
        Assert.DoesNotContain(h.List.Messages, row => row.Id is "a1" or "a2");

        h.List.ToggleGroup(headers[0].Key);
        Assert.Equal(7, h.List.Rows.Count);
        Assert.False(h.List.Rows.OfType<GroupHeaderViewModel>().First().Collapsed);
    }

    [Fact]
    public async Task A_folded_group_keeps_the_open_and_the_ticked_rows()
    {
        using var h = new ListHarness("""{"groupListByDate":true}""");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(ThreeDays()));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        h.List.Selected = h.List.Messages.First(r => string.Equals(r.Id, "a1", StringComparison.Ordinal));
        h.List.ToggleCheck(h.List.Messages.First(r => string.Equals(r.Id, "a2", StringComparison.Ordinal)));
        h.List.ToggleGroup(h.List.Rows.OfType<GroupHeaderViewModel>().First().Key);
        Assert.Contains(h.List.Messages, row => string.Equals(row.Id, "a1", StringComparison.Ordinal));
        Assert.Contains(h.List.Messages, row => string.Equals(row.Id, "a2", StringComparison.Ordinal));
        Assert.Same(h.List.Selected, h.List.Messages.First(r => string.Equals(r.Id, "a1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Another_folder_opens_with_every_group_open()
    {
        using var h = new ListHarness("""{"groupListByDate":true}""");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(ThreeDays()));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        h.List.ToggleGroup(h.List.Rows.OfType<GroupHeaderViewModel>().First().Key);
        Assert.Equal(5, h.List.Rows.Count);
        await h.List.OpenFolderAsync("work", MailboxRole.None, "Work");
        Assert.Equal(7, h.List.Rows.Count);
        Assert.DoesNotContain(h.List.Rows.OfType<GroupHeaderViewModel>(), header => header.Collapsed);
    }

    [Fact]
    public void The_focus_goes_back_to_the_list_only_when_it_was_there_and_nothing_else_has_it()
    {
        Assert.True(FocusRescue.ShouldReturnToList(focusWasInList: true, focusIsLost: true, listShown: true));
        Assert.False(FocusRescue.ShouldReturnToList(focusWasInList: false, focusIsLost: true, listShown: true));
        Assert.False(FocusRescue.ShouldReturnToList(focusWasInList: true, focusIsLost: false, listShown: true));
        Assert.False(FocusRescue.ShouldReturnToList(focusWasInList: true, focusIsLost: true, listShown: false));
    }
}
