// New tests: the reader applies a snapshot as one step. Spec B-SYNC-022 (header and body change together), B-SYNC-027 (header from the list row),
// B-SYNC-029 (a failed fetch leaves nothing of the previous message), B-MAIL-019..024 (attachment cards). Owner mailcore.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature; fakes hand back TaskCompletionSource tasks on purpose
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class ReaderViewModelTests
{
    // R2-07: the server's results count only inside its own trace block, which starts at the message's first Received
    private static readonly EmailHeader Received = new("Received", "from relay.example.net by mx.example.org with ESMTPS; Tue, 17 Nov 2026 09:00:00 +0000");

    private static ReaderOptions Options() => new() { Now = Mails.Now, Zone = TimeZoneInfo.Utc, ServerHost = "jmap.example.org" };

    private static EmailBody Body(string id = "b1", params Attachment[] attachments) =>
        new()
        {
            Id = id,
            ThreadId = "t" + id,
            Subject = "Hello",
            From = [new EmailAddress("Ada Lovelace", "ada@shop.example.com")],
            To = [new EmailAddress(null, "me@a.example.com"), new EmailAddress("Bob", "bob@a.example.com")],
            ReceivedAt = new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero),
            Keywords = Keywords.Of("$flagged"),
            Attachments = attachments,
            Headers = [Received, new EmailHeader("Authentication-Results", "mx.example.org; dkim=pass header.d=mail.shop.example.com;\r\n spf=fail smtp.mailfrom=x@evil.example.net; dmarc=pass")],
        };

    [Fact]
    public void Prefill_takes_the_header_from_the_list_row_while_the_body_loads()
    {
        var row = new MessageRowViewModel(Mails.Make("m1", subject: "Lunch?", from: "Ada Lovelace") with { To = [new EmailAddress("Bob", "bob@a.example.com")] });
        var s = ReaderSnapshots.Prefill(row, Options());
        Assert.Equal(BodyState.Loading, s.BodyState);
        Assert.Equal("Lunch?", s.Header.Subject);
        Assert.Equal("Ada Lovelace", s.Header.From);
        Assert.Equal("AD", s.Header.Initials);
        Assert.Equal("Bob", s.Header.To);
        Assert.Equal("Tue 17 Nov 2026, 11:00", s.Header.Date);
        Assert.Null(s.Body);
    }

    [Fact]
    public void Apply_assigns_everything_before_the_first_notification()
    {
        var reader = new ReaderViewModel();
        reader.Apply(ReaderSnapshots.FromBody(Body(), null, Options()));
        var seen = new List<(string Name, string Subject, BodyState State, int Chips)>();
        reader.PropertyChanged += (_, e) => seen.Add((e.PropertyName ?? string.Empty, reader.Header.Subject, reader.BodyState, reader.AuthChips.Count));
        reader.Apply(ReaderSnapshots.Failed("x", "gone"));
        Assert.NotEmpty(seen);
        Assert.All(seen, s => Assert.Equal((string.Empty, BodyState.Failed, 0), (string.Empty, s.State, s.Chips)));
        Assert.Equal("gone", reader.Error);
    }

    [Fact]
    public void A_failed_fetch_leaves_nothing_of_the_previous_message()
    {
        var reader = new ReaderViewModel();
        reader.Apply(ReaderSnapshots.FromBody(Body(attachments: new Attachment { Name = "a.pdf", Size = 10, BlobId = "x", Type = "application/pdf" }), null, Options()));
        Assert.Equal("Hello", reader.Header.Subject);
        Assert.Single(reader.Attachments);
        reader.Apply(ReaderSnapshots.Failed("b2", "Could not load message"));
        Assert.Equal(string.Empty, reader.Header.Subject);
        Assert.Empty(reader.Attachments);
        Assert.Empty(reader.Conversation);
        Assert.Equal(BodyState.Failed, reader.BodyState);
        reader.Clear();
        Assert.True(reader.IsEmpty);
    }

    [Fact]
    public void The_server_checks_become_chips_and_a_verdict_that_needs_alignment()
    {
        var s = ReaderSnapshots.FromBody(Body(), null, Options());
        Assert.Equal(AuthVerdict.Verified, s.AuthVerdict);
        Assert.Equal([("dkim", "pass", true), ("spf", "fail", false), ("dmarc", "pass", true)], s.AuthChips.Select(c => (c.Method, c.Result, c.Aligned)));
        var other = ReaderSnapshots.FromBody(Body() with { From = [new EmailAddress(null, "ceo@bank.example.com")], Headers = [Received, new EmailHeader("Authentication-Results", "mx.example.org; dkim=pass header.d=evil.example.net")] }, null, Options());
        Assert.Equal(AuthVerdict.Unknown, other.AuthVerdict);
        var failed = ReaderSnapshots.FromBody(Body() with { Headers = [Received, new EmailHeader("Authentication-Results", "mx.example.org; dmarc=fail")] }, null, Options());
        Assert.Equal(AuthVerdict.Failed, failed.AuthVerdict);
    }

    [Fact]
    public void Only_the_account_servers_own_authentication_results_count()
    {
        // F-07 / D-9: the server added no header (a sent copy, an import): the sender's own header claims dmarc=pass under its own id
        var forged = new EmailHeader("Authentication-Results", "mail.shop.example.com; dkim=pass header.d=shop.example.com; dmarc=pass");
        var alone = ReaderSnapshots.FromBody(Body() with { Headers = [forged] }, null, Options());
        Assert.Equal(AuthVerdict.Unknown, alone.AuthVerdict);
        Assert.Empty(alone.AuthChips);

        // the forged header sits above the server's (a relay kept it): the server's verdict is the one shown
        var server = new EmailHeader("Authentication-Results", "mx.example.org (comment); dmarc=fail");
        var both = ReaderSnapshots.FromBody(Body() with { Headers = [Received, forged, server] }, null, Options());
        Assert.Equal(AuthVerdict.Failed, both.AuthVerdict);
        Assert.Equal([("dmarc", "fail")], both.AuthChips.Select(c => (c.Method, c.Result)));

        // no known server: nothing is trusted
        Assert.Equal(AuthVerdict.Unknown, ReaderSnapshots.FromBody(Body(), null, Options() with { ServerHost = null }).AuthVerdict);
    }

    [Theory]
    [InlineData("jmap.example.org", "jmap.example.org", true)]
    [InlineData("jmap.example.org", "mx.example.org", true)]
    [InlineData("jmap.example.org", "example.org", true)]
    [InlineData("jmap.example.org", "mx.example.org.", true)]
    [InlineData("jmap.example.org", "example.org.evil.example.net", false)]
    [InlineData("jmap.example.org", "evilexample.org", false)]
    [InlineData("jmap.example.org", "org", false)]
    [InlineData("example.org", "mx.example.org", true)]
    [InlineData("example.org", "other.org", false)]
    [InlineData("localhost", "localhost", true)]
    [InlineData(null, "mx.example.org", false)]
    public void An_authserv_id_is_trusted_when_it_is_the_servers_domain(string? host, string id, bool trusted) =>
        Assert.Equal(trusted, AuthServTrust.Trusts(host, id));

    [Fact]
    public void The_authserv_id_is_read_from_the_header()
    {
        Assert.Equal("mx.example.org", AuthenticationResults.AuthServId("mx.example.org 1; dkim=pass"));
        Assert.Equal("mx.example.org", AuthenticationResults.AuthServId(" (c) mx.example.org;\r\n spf=pass"));
        Assert.Null(AuthenticationResults.AuthServId("dkim=pass"));
        Assert.Equal("example.org", AuthServTrust.ServerHostOf("https://Example.org:8443\nana@example.org"));
        Assert.Null(AuthServTrust.ServerHostOf(null));
    }

    [Fact]
    public void Attachment_cards_show_when_an_image_is_among_the_files_and_the_setting_is_on()
    {
        var files = new[]
        {
            new Attachment { Name = "report.final.pdf", Size = 1_500_000, BlobId = "p", Type = "application/pdf" },
            new Attachment { Name = "photo.png", Size = 2000, BlobId = "i", Type = "image/png" },
            new Attachment { Name = "logo.png", Size = 10, BlobId = "l", Type = "image/png", Cid = "logo" },
        };
        var s = ReaderSnapshots.FromBody(Body(attachments: files), null, Options());
        Assert.True(s.ShowAttachmentCards);
        Assert.Equal("2 attachments · 1.5 MB", s.AttachmentSummary);
        Assert.Equal([("report.final", "PDF", false), ("photo", "PNG", true)], s.Attachments.Select(a => (a.Stem, a.Kind, a.Previewable)));
        var off = ReaderSnapshots.FromBody(Body(attachments: files), null, Options() with { AttachmentImagePreviews = false });
        Assert.False(off.ShowAttachmentCards);
        Assert.Equal(string.Empty, off.AttachmentSummary);
    }

    [Fact]
    public void A_thumbnail_arriving_touches_only_its_tile()
    {
        var reader = new ReaderViewModel();
        var files = new[] { new Attachment { Name = "a.png", Size = 10, BlobId = "a", Type = "image/png" }, new Attachment { Name = "b.png", Size = 10, BlobId = "b", Type = "image/png" } };
        reader.Apply(ReaderSnapshots.FromBody(Body(attachments: files), null, Options()));
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        reader.Attachments.CollectionChanged += (_, e) => changes.Add(e.Action);
        reader.SetThumbnail("b1", 1, new Core.Mail.Attachments.ThumbnailImage(1, 1, [0, 0, 0, 255]));
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Replace], changes);
        Assert.True(reader.Attachments[1].HasThumbnail);
        Assert.False(reader.Attachments[0].HasThumbnail);
        reader.SetThumbnail("another", 0, new Core.Mail.Attachments.ThumbnailImage(1, 1, [0, 0, 0, 255]));
        Assert.False(reader.Attachments[0].HasThumbnail);
    }

    [Fact]
    public void The_conversation_strip_marks_the_open_message_and_updates_in_place()
    {
        var thread = new ConversationThread("tb1", [Mails.Make("b0", hoursAgo: 30), Mails.Make("b1", unread: true, hoursAgo: 2)]);
        var reader = new ReaderViewModel();
        reader.Apply(ReaderSnapshots.FromBody(Body(), thread, Options()));
        Assert.Equal([false, true], reader.Conversation.Select(c => c.IsCurrent));
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        reader.Conversation.CollectionChanged += (_, e) => changes.Add(e.Action);
        reader.Apply(ReaderSnapshots.FromBody(Body(), thread, Options()));
        Assert.Empty(changes);
        Assert.Empty(ReaderSnapshots.FromBody(Body(), new ConversationThread("t", [Mails.Make("only")]), Options()).Conversation);
    }

    [Fact]
    public void Conversation_shows_two_earlier_headers_and_can_reveal_the_rest()
    {
        using var reader = new ReaderViewModel();
        var items = Enumerable.Range(0, 8).Select(i => new ConversationItem($"m{i}", $"Sender {i}", "S", 0, "Today", false, i == 7)).ToArray();
        var snapshot = new ReaderSnapshot { EmailId = "m7", Conversation = items };
        reader.Apply(snapshot);
        Assert.Equal(5, reader.HiddenConversationCount);
        Assert.Equal(["m0", "m6"], reader.ConversationHeaders.Select(x => x.Id));
        Assert.Null(reader.ConversationHeaders[0].OlderMessageDisclosure);
        Assert.Equal("Show 5 older messages", reader.ConversationHeaders[1].OlderMessageDisclosure);
        reader.ShowOlderConversationHeaders();
        Assert.Equal(0, reader.HiddenConversationCount);
        Assert.Equal(items.Take(7), reader.ConversationHeaders);
        reader.Apply(snapshot);
        Assert.Equal(7, reader.ConversationHeaders.Count);
        reader.Clear();
        Assert.Empty(reader.ConversationHeaders);
        Assert.Equal(0, reader.HiddenConversationCount);
    }

    [Fact]
    public void Cards_expand_independently_and_revoke_old_bodies_on_close_or_account_change()
    {
        using var reader = new ReaderViewModel();
        var items = Enumerable.Range(0, 8).Select(i => new ConversationItem($"m{i}", $"Sender {i}", "S", 0, "", false, i == 7)).ToArray();
        var snapshot = new ReaderSnapshot { EmailId = "m7", ListAccountKey = "first", Conversation = items };
        reader.Apply(snapshot);
        Assert.Null(reader.ToggleConversationCard("m3"));
        reader.ShowOlderConversationHeaders();
        var older = Assert.IsType<ConversationExpansion>(reader.ToggleConversationCard("m3"));
        var first = Assert.IsType<ConversationExpansion>(reader.ToggleConversationCard("m0"));
        reader.Apply(snapshot);
        Assert.True(reader.Owns(older));
        Assert.True(reader.Owns(first));
        Assert.Equal("m7", reader.EmailId);
        Assert.Null(reader.ToggleConversationCard("m3"));
        Assert.False(reader.Owns(older));
        Assert.True(reader.Owns(first));
        reader.Apply(snapshot with { ListAccountKey = "second" });
        Assert.False(reader.Owns(first));
        Assert.All(reader.ConversationHeaders, header => Assert.Null(header.Expansion));
    }

    [Fact]
    public void Opening_an_earlier_message_keeps_later_headers_available()
    {
        using var reader = new ReaderViewModel();
        var items = Enumerable.Range(0, 8).Select(i => new ConversationItem($"m{i}", $"Sender {i}", "S", 0, "Today", false, i == 4)).ToArray();
        reader.Apply(new ReaderSnapshot { EmailId = "m4", Conversation = items });
        Assert.Equal(2, reader.HiddenConversationCount);
        Assert.Equal(["m0", "m3", "m5", "m6", "m7"], reader.ConversationHeaders.Select(x => x.Id));
        Assert.Equal("Show 2 older messages", reader.ConversationHeaders[1].OlderMessageDisclosure);
        reader.ShowOlderConversationHeaders();
        reader.Apply(new ReaderSnapshot { EmailId = "new", Conversation = [] });
        Assert.Empty(reader.ConversationHeaders);
        Assert.Equal(0, reader.HiddenConversationCount);
    }

    [Fact]
    public void The_header_only_opener_shows_the_row_and_closing_empties_the_reader()
    {
        var reader = new ReaderViewModel();
        var opener = new HeaderOnlyMessageOpener(reader, Options);
        opener.Open(new MessageRowViewModel(Mails.Make("m1", subject: "Hi")), OpenReason.Key, ListDirection.Down);
        Assert.Equal("Hi", reader.Header.Subject);
        Assert.Equal(BodyState.Loading, reader.BodyState);
        opener.Close();
        Assert.True(reader.IsEmpty);
    }
}
