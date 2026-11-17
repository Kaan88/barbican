// New tests: the hover strip of a row (spec I-131..I-136): the buttons come from the hoverActions setting, markRead swaps its icon and title, archive reads "Move to Inbox" in Archive,
// spam reads "Not spam" in Junk, tag opens the picker. Bulwark components/email/email-hover-actions.tsx:150-290. Owner actions (m2-close-a).

#pragma warning disable MA0002, MA0006 // asserts compare ids ordinally by nature

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Tests.Mail;

public sealed class HoverStripTests
{
    [Fact]
    public void The_default_strip_is_delete_star_read_archive()
    {
        var strip = HoverStrip.For(unread: true, flagged: false, MailboxRole.Inbox, false, HoverStrip.Configured(null));
        Assert.Equal(["delete", "star", "markRead", "archive"], strip.Select(b => b.Id));
        Assert.Equal([CommandIds.Delete, CommandIds.ToggleStar, CommandIds.ToggleRead, CommandIds.Archive], strip.Select(b => b.CommandId));
        Assert.Equal(["Delete", "Star", "Mark as read", "Archive"], strip.Select(b => b.Tooltip));
    }

    [Fact]
    public void Mark_read_swaps_its_icon_and_title_with_the_message()
    {
        var unread = HoverStrip.For(true, false, MailboxRole.Inbox, false, ["markRead"]).Single();
        var read = HoverStrip.For(false, false, MailboxRole.Inbox, false, ["markRead"]).Single();
        Assert.Equal(("Check", "Mark as read"), (unread.Icon, unread.Tooltip));
        Assert.Equal(("Mail", "Mark as unread"), (read.Icon, read.Tooltip));
    }

    [Fact]
    public void A_starred_message_shows_the_star_on_and_offers_to_unstar()
    {
        var star = HoverStrip.For(false, true, MailboxRole.Inbox, false, ["star"]).Single();
        Assert.True(star.Active);
        Assert.Equal("Unstar", star.Tooltip);
    }

    [Fact]
    public void In_Archive_the_archive_button_moves_to_the_inbox()
    {
        var button = HoverStrip.For(false, false, MailboxRole.Archive, true, ["archive"]).Single();
        Assert.Equal((CommandIds.MoveToInbox, "Inbox", "Move to Inbox"), (button.CommandId, button.Icon, button.Tooltip));
    }

    [Fact]
    public void Spam_becomes_not_spam_in_junk_and_is_left_out_of_sent_and_drafts()
    {
        Assert.Equal(CommandIds.ReportSpam, HoverStrip.For(false, false, MailboxRole.Inbox, false, ["spam"]).Single().CommandId);
        var junk = HoverStrip.For(false, false, MailboxRole.Junk, false, ["spam"]).Single();
        Assert.Equal((CommandIds.NotSpam, "Not spam"), (junk.CommandId, junk.Tooltip));
        Assert.Empty(HoverStrip.For(false, false, MailboxRole.Sent, false, ["spam"]));
        Assert.Empty(HoverStrip.For(false, false, MailboxRole.Drafts, false, ["spam"]));
    }

    [Fact]
    public void The_tag_button_opens_the_picker()
    {
        Assert.True(HoverStrip.For(false, false, MailboxRole.Inbox, false, ["tag"]).Single().OpensTagPicker);
    }

    [Fact]
    public void The_setting_picks_and_orders_the_buttons_and_unknown_ids_fall_back_to_the_default()
    {
        Assert.Equal(["spam", "tag"], HoverStrip.Configured(JsonNode.Parse("""["spam", "bogus", "tag", "spam"]""")));
        Assert.Equal(HoverStrip.Defaults, HoverStrip.Configured(JsonNode.Parse("""["bogus"]""")));
        Assert.Empty(HoverStrip.Configured(JsonNode.Parse("[]")));
    }
}
