// The M3 seed (send pipeline), added on top of the M1 seed by FakeJmapOptions.SeedM3: a reply thread with an inline image and a file, a message with Reply-To, a message to an alias on the user's domain, and a self-sent message.
// Synthetic, example.org / example.net / .example addresses only. The streams read these messages by subject (the constants below). Owner send-pipeline.

using System.Text;

namespace Barbican.FakeJmap;

public static partial class FakeSeed
{
    /// <summary>A message with an inline cid: image, an attached pdf and an HTML body (reply, forward and reopen tests).</summary>
    public const string InlineThreadSubject = "Design sketch";

    /// <summary>A message with a Reply-To header, a Cc and several recipients (reply-all table).</summary>
    public const string ReplyToSubject = "Budget thread";

    /// <summary>A message addressed to an alias on the user's own domain (catch-all identity).</summary>
    public const string AliasSubject = "Question for sales";

    /// <summary>A message the user sent to two people (self-sent reply).</summary>
    public const string SelfSentSubject = "My update";

    /// <summary>The bytes of the inline image (a PNG signature).</summary>
    public static readonly byte[] InlineImage = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    /// <summary>The identities the M3 tests use: the login with a text and an HTML signature, a second identity on another domain, and a wildcard.</summary>
    public static IReadOnlyList<FakeIdentity> IdentitiesM3 { get; } =
    [
        new FakeIdentity("i1", "Test User", "user@example.org") { TextSignature = "Test User\nExample Ltd", HtmlSignature = "<p>Test User<br>Example Ltd</p>" },
        new FakeIdentity("i2", "Test Info", "info@example.net") { TextSignature = "Info desk" },
        new FakeIdentity("i3", "Wildcard", "*@example.org"),
    ];

    /// <summary>Adds the M3 messages to a store that already has the M1 seed.</summary>
    public static void FillM3(FakeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var t0 = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        store.Deliver(new FakeMail
        {
            Subject = InlineThreadSubject,
            FromName = "Dora",
            FromEmail = "dora@example.net",
            ReceivedAt = t0,
            MessageId = "sketch-1@example.net",
            Text = "The sketch is below.",
            Html = "<p>The sketch is below.</p><img src=\"cid:sketch@example.net\">",
            Attachments =
            [
                new FakeAttachment("sketch.png", "image/png", InlineImage, "sketch@example.net"),
                new FakeAttachment("notes.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 synthetic notes")),
            ],
        });
        store.Deliver(new FakeMail
        {
            Subject = ReplyToSubject,
            FromName = "Finance",
            FromEmail = "finance@example.net",
            To = "user@example.org",
            ReceivedAt = t0.AddHours(1),
            MessageId = "budget-1@example.net",
            Text = "Numbers attached soon.",
            MoreTo = ["other@example.net", "USER@example.org"],
            Cc = ["carl@example.net", "info@example.net"],
            ReplyToEmail = "boss@example.net",
        });
        store.Deliver(new FakeMail
        {
            Subject = AliasSubject,
            FromName = "Customer",
            FromEmail = "customer@example.net",
            To = "sales@example.org",
            ReceivedAt = t0.AddHours(2),
            MessageId = "alias-1@example.net",
            Text = "Do you ship to Norway?",
            Headers = [("Delivered-To", "sales@example.org")],
        });
        store.Deliver(new FakeMail
        {
            Subject = SelfSentSubject,
            FromName = "Test User",
            FromEmail = "user@example.org",
            To = "erin@example.net",
            ReceivedAt = t0.AddHours(3),
            MessageId = "update-1@example.org",
            Text = "Status update.",
            Keywords = ["$seen"],
        }, store.MailboxId("sent"));
    }
}
