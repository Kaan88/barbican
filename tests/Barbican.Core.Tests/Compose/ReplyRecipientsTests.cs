#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (as the ported Rust asserts)
using Barbican.Core.Compose;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Compose;

/// <summary>Bulwark lib/reply-recipients.ts buildReplyRecipients and the quote header words (the reply and reply-all tables of the plan).</summary>
public sealed class ReplyRecipientsTests
{
    private static readonly string[] Own = ["me@a.example", "info@b.example"];

    private static EmailAddress A(string email, string? name = null) => new(name, email);

    private static EmailBody Mail(EmailAddress from, EmailAddress[]? to = null, EmailAddress[]? cc = null, EmailAddress[]? replyTo = null) =>
        new() { Id = "m", From = [from], To = to ?? [], Cc = cc ?? [], ReplyTo = replyTo ?? [] };

    private static string Join(IReadOnlyList<EmailAddress> l) => string.Join(",", l.Select(a => a.Email));

    [Fact]
    public void Reply_goes_to_reply_to_else_from()
    {
        var (to, cc) = ReplyRecipients.Build(Mail(A("x@o.example"), replyTo: [A("rt@o.example")]), false, Own);
        Assert.Equal("rt@o.example", Join(to));
        Assert.Empty(cc);
        Assert.Equal("x@o.example", Join(ReplyRecipients.Build(Mail(A("x@o.example")), false, Own).To));
    }

    [Fact]
    public void Reply_all_adds_the_others_minus_own_addresses_with_tags_and_without_duplicates()
    {
        var m = Mail(A("x@o.example"), [A("ME+news@a.example"), A("y@o.example"), A("Y@O.example")], [A("z@o.example"), A("x@o.example"), A("info@b.example")]);
        var (to, cc) = ReplyRecipients.Build(m, true, Own);
        Assert.Equal("x@o.example,y@o.example", Join(to));
        Assert.Equal("z@o.example", Join(cc));
    }

    [Fact]
    public void A_self_sent_message_replies_to_its_original_recipients_and_ignores_own_reply_to()
    {
        var m = Mail(A("me@a.example"), [A("p@o.example")], [A("q@o.example")], [A("me@a.example")]);
        Assert.Equal("p@o.example", Join(ReplyRecipients.Build(m, false, Own).To));
        var (to, cc) = ReplyRecipients.Build(m, true, Own);
        Assert.Equal("p@o.example", Join(to));
        Assert.Equal("q@o.example", Join(cc));

        // nothing visible to continue to (bcc only): the normal behaviour
        Assert.Equal("me@a.example", Join(ReplyRecipients.Build(Mail(A("me@a.example")), false, Own).To));
    }

    [Fact]
    public void Recipient_formatting_follows_bulwark()
    {
        Assert.Equal("a@b.example", QuoteHeader.FormatRecipient("", "a@b.example"));
        Assert.Equal("a@b.example", QuoteHeader.FormatRecipient("A@B.example", "a@b.example"));
        Assert.Equal("Ada Lovelace <a@b.example>", QuoteHeader.FormatRecipient("Ada Lovelace", "a@b.example"));
        Assert.Equal("\"Lovelace, Ada\" <a@b.example>", QuoteHeader.FormatRecipient("Lovelace, Ada", "a@b.example"));
        Assert.Equal("\"Ada \\\"AL\\\" L\" <a@b.example>", QuoteHeader.FormatRecipient("Ada \"AL\" L", "a@b.example"));
    }
}
