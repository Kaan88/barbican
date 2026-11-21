// Bulwark thread-list-item.tsx:978-997 (replied and forwarded icons, both for a message that is both), :720-745 (the open row takes the selection fill and drops the unread wash).
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class ListRowStateTests
{
    [Fact]
    public void A_row_knows_when_the_message_was_answered_or_forwarded()
    {
        var plain = new MessageRowViewModel(Mails.Make("a"));
        Assert.False(plain.Answered);
        Assert.False(plain.Forwarded);
        var replied = new MessageRowViewModel(Mails.Make("b") with { Keywords = Keywords.Of("$seen", "$answered") });
        Assert.True(replied.Answered);
        Assert.False(replied.Forwarded);
        var both = new MessageRowViewModel(Mails.Make("c") with { Keywords = Keywords.Of("$seen", "$answered", "$forwarded") });
        Assert.True(both.Answered);
        Assert.True(both.Forwarded);
    }

    [Fact]
    public void A_change_of_the_reply_or_forward_flag_replaces_the_row()
    {
        var before = new MessageRowViewModel(Mails.Make("a"));
        var after = new MessageRowViewModel(Mails.Make("a") with { Keywords = Keywords.Of("$forwarded") });
        Assert.False(before.SameAs(after));
        Assert.False(after.SameAs(new MessageRowViewModel(Mails.Make("a") with { Keywords = Keywords.Of("$answered") })));
    }

    [Fact]
    public async Task The_open_row_is_marked_selected_and_only_one_row_is()
    {
        using var h = new ListHarness("""{"groupListByDate":true}""");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(Mails.Page(0, 5, 5, false)));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.All(h.List.Messages, r => Assert.False(r.IsSelected));
        var first = h.List.Messages.First();
        h.List.Selected = first;
        Assert.True(first.IsSelected);
        Assert.Single(h.List.Messages, r => r.IsSelected);
        h.List.Selected = h.List.Messages.Last();
        Assert.False(first.IsSelected);
        Assert.True(h.List.Messages.Last().IsSelected);
        h.List.Selected = null;
        Assert.All(h.List.Messages, r => Assert.False(r.IsSelected));
    }
}
