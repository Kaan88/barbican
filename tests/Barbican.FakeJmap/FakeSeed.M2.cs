// The M2 seed (stage F of M2, then frozen: M2 plan section 4 F step 4), added on top of the M1 seed by FakeJmapOptions.SeedM2 so every M1 test keeps its data. Synthetic,
// example.org / example.net / .example addresses only. The streams read these messages by subject (the constants below); they add data of their own in their test code, never here.
// Tags: the seed carries the keyword ids ($label:work, $label:home, $label:work-q4 as the nested one); names, colours and nesting live in the prefs, not on the server.

using System.Text;

namespace Barbican.FakeJmap;

public static partial class FakeSeed
{
    /// <summary>A newsletter with List-Unsubscribe, List-Unsubscribe-Post and a DKIM pass for news.example from the server's own Authentication-Results.</summary>
    public const string NewsletterSubject = "Weekly digest";

    /// <summary>Disposition-Notification-To equals the Return-Path (a receipt may go out without asking).</summary>
    public const string ReceiptOwnSubject = "Please confirm receipt";

    /// <summary>Disposition-Notification-To is a third party (the user is asked).</summary>
    public const string ReceiptThirdPartySubject = "Read me (tracked)";

    /// <summary>A text/calendar invitation part.</summary>
    public const string InvitationSubject = "Invitation: Design review";

    /// <summary>A verification code in the subject (Rust otp.rs finds_codes).</summary>
    public const string CodeSubject = "Your verification code is 177945";

    /// <summary>A promo code, which is not a verification code.</summary>
    public const string PromoSubject = "Promo code SAVE20 for 20% off";

    /// <summary>A message/rfc822 attachment.</summary>
    public const string ForwardedSubject = "Fwd: as attachment";

    /// <summary>An .eml file attached as application/octet-stream.</summary>
    public const string EmlAttachedSubject = "Saved message attached";

    /// <summary>A winmail.dat (TNEF) attachment holding hello.txt and sheet.csv.</summary>
    public const string TnefSubject = "Winmail from a legacy client";

    /// <summary>Three attachments, two with the same name (zip name uniqueness, I-226).</summary>
    public const string ThreeAttachmentsSubject = "Scans and notes";

    /// <summary>X-Spam-Status: Yes, score=7.5 required=5.0 (the spam-score plugin, T-206).</summary>
    public const string SpamHeadersSubject = "Spam-scored message";

    /// <summary>A 1.6 MB HTML body (L-014: under maxBodyValueBytes, a heavy page).</summary>
    public const string HeavyHtmlSubject = "Heavy HTML";

    /// <summary>The size of the heavy HTML body in bytes.</summary>
    public const int HeavyHtmlBytes = 1_600_000;

    /// <summary>Adds the M2 folders, messages and tags' keywords to a store that already has the M1 seed.</summary>
    public static void FillM2(FakeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var archive = store.MailboxId("archive")!;

        // a nested folder tree beside the role folders: Work > Clients > Acme (three levels), and 2025 under the Archive role folder
        var work = store.AddMailbox("Work", null);
        var clients = store.AddMailbox("Clients", null, work);
        store.AddMailbox("Acme", null, clients);
        store.AddMailbox("2025", null, archive);

        var t0 = new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

        store.Deliver(new FakeMail
        {
            Subject = NewsletterSubject,
            FromName = "News",
            FromEmail = "list@news.example",
            ReceivedAt = t0,
            Text = "Five links from this week.",
            Html = "<p>Five links from this week.</p>",
            AuthenticationResults = "mx.example.org; dkim=pass header.d=news.example; spf=pass smtp.mailfrom=list@news.example; dmarc=pass header.from=news.example",
            Headers =
            [
                ("List-Id", "Weekly digest <digest.news.example>"),
                ("List-Unsubscribe", "<https://news.example/unsub?u=4f2a9c>, <mailto:unsubscribe@news.example?subject=unsubscribe>"),
                ("List-Unsubscribe-Post", "List-Unsubscribe=One-Click"),
            ],
        });
        store.Deliver(new FakeMail
        {
            Subject = ReceiptOwnSubject,
            FromName = "Boss",
            FromEmail = "boss@example.org",
            ReceivedAt = t0.AddHours(1),
            Text = "Please acknowledge.",
            Headers = [("Return-Path", "<boss@example.org>"), ("Disposition-Notification-To", "Boss <boss@example.org>")],
        });
        store.Deliver(new FakeMail
        {
            Subject = ReceiptThirdPartySubject,
            FromName = "Boss",
            FromEmail = "boss@example.org",
            ReceivedAt = t0.AddHours(2),
            Text = "Tracked.",
            Headers = [("Return-Path", "<boss@example.org>"), ("Disposition-Notification-To", "tracker@spy.example")],
        });
        store.Deliver(new FakeMail { Subject = CodeSubject, FromName = "Accounts", FromEmail = "no-reply@example.net", ReceivedAt = t0.AddHours(4), Text = "Use it within 10 minutes." });
        store.Deliver(new FakeMail { Subject = PromoSubject, FromName = "Shop", FromEmail = "offers@example.net", ReceivedAt = t0.AddHours(5), Text = "Order 123456 ships soon." });
        FillM2Attachments(store, t0);
        FillM2Containers(store, t0);
        FillM2Tagged(store, t0);
        FillM2Folders(store, t0);
    }

    private static void FillM2Attachments(FakeStore store, DateTimeOffset t0)
    {
        store.Deliver(new FakeMail
        {
            Subject = InvitationSubject,
            FromName = "Olga",
            FromEmail = "olga@acme.example",
            ReceivedAt = t0.AddHours(3),
            Text = "Join us for the design review.",
            Attachments = [new FakeAttachment("invite.ics", "text/calendar", Encoding.ASCII.GetBytes(Invitation))],
        });
        store.Deliver(new FakeMail
        {
            Subject = ForwardedSubject,
            FromName = "Frank",
            FromEmail = "frank@example.org",
            ReceivedAt = t0.AddHours(6),
            Text = "See the attached message.",
            Attachments = [new FakeAttachment("Original.eml", "message/rfc822", Encoding.ASCII.GetBytes("From: carol@example.org\r\nTo: user@example.org\r\nSubject: The original\r\n\r\nOriginal body.\r\n"))],
        });
        store.Deliver(new FakeMail
        {
            Subject = EmlAttachedSubject,
            FromName = "Frank",
            FromEmail = "frank@example.org",
            ReceivedAt = t0.AddHours(7),
            Text = "Saved from another client.",
            Attachments = [new FakeAttachment("Saved.EML", "application/octet-stream", Encoding.ASCII.GetBytes("From: dave@example.org\r\nSubject: Saved\r\n\r\nSaved body.\r\n"))],
        });
    }

    private static void FillM2Containers(FakeStore store, DateTimeOffset t0)
    {
        store.Deliver(new FakeMail
        {
            Subject = TnefSubject,
            FromName = "Legacy",
            FromEmail = "legacy@example.net",
            ReceivedAt = t0.AddHours(8),
            Text = "The attachments are in winmail.dat.",
            Attachments = [new FakeAttachment("winmail.dat", "application/ms-tnef", FakeTnef.Build([("hello.txt", Encoding.ASCII.GetBytes("hello")), ("sheet.csv", Encoding.ASCII.GetBytes("a,b\r\n1,2\r\n"))]))],
        });
        store.Deliver(new FakeMail
        {
            Subject = ThreeAttachmentsSubject,
            FromName = "Scanner",
            FromEmail = "scan@example.net",
            ReceivedAt = t0.AddHours(9),
            Text = "Two scans with the same name and a note.",
            Attachments =
            [
                new FakeAttachment("scan.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 first")),
                new FakeAttachment("scan.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 second")),
                new FakeAttachment("notes.txt", "text/plain", Encoding.ASCII.GetBytes("notes")),
            ],
        });
        store.Deliver(new FakeMail
        {
            Subject = SpamHeadersSubject,
            FromName = "Prize",
            FromEmail = "win@example.net",
            ReceivedAt = t0.AddHours(10),
            Text = "You won.",
            Headers = [("X-Spam-Status", "Yes, score=7.5 required=5.0"), ("X-Spam-Score", "7.5")],
        });
        store.Deliver(new FakeMail
        {
            Subject = HeavyHtmlSubject,
            FromName = "Builder",
            FromEmail = "build@example.net",
            ReceivedAt = t0.AddHours(11),
            Text = "A heavy page.",
            Html = "<html><body>" + string.Concat(Enumerable.Repeat(HeavyParagraph, HeavyHtmlBytes / HeavyParagraph.Length)) + "</body></html>",
        });

    }

    private static void FillM2Tagged(FakeStore store, DateTimeOffset t0)
    {
        // tagged messages: two tags and a nested one
        store.Deliver(new FakeMail { Subject = "Tagged: work", FromEmail = "alice@example.org", FromName = "Alice", ReceivedAt = t0.AddHours(12), Text = "Work item.", Keywords = ["$label:work"] });
        store.Deliver(new FakeMail { Subject = "Tagged: home", FromEmail = "bob@example.org", FromName = "Bob", ReceivedAt = t0.AddHours(13), Text = "Home item.", Keywords = ["$label:home"] });
        store.Deliver(new FakeMail { Subject = "Tagged: work q4", FromEmail = "alice@example.org", FromName = "Alice", ReceivedAt = t0.AddHours(14), Text = "Nested tag.", Keywords = ["$label:work", "$label:work-q4"] });

    }

    private static void FillM2Folders(FakeStore store, DateTimeOffset t0)
    {
        var trash = store.MailboxId("trash")!;
        var drafts = store.MailboxId("drafts")!;
        var archive = store.MailboxId("archive")!;
        var sent = store.MailboxId("sent")!;
        var junk = store.MailboxId("junk")!;

        // the role folders with messages
        store.Deliver(new FakeMail { Subject = "In the trash", FromEmail = "dave@example.org", FromName = "Dave", ReceivedAt = t0.AddHours(-24), Text = "Deleted.", Keywords = ["$seen"] }, trash);
        store.Deliver(new FakeMail { Subject = "A draft", FromEmail = "user@example.org", FromName = "Test User", To = "erin@example.org", ReceivedAt = t0.AddHours(-12), Text = "Unfinished.", Keywords = ["$draft", "$seen"] }, drafts);
        store.Deliver(new FakeMail { Subject = "Archived thread", FromEmail = "carol@example.org", FromName = "Carol", ReceivedAt = t0.AddHours(-72), Text = "Done.", Keywords = ["$seen"] }, archive);
        store.Deliver(new FakeMail { Subject = "Sent last week", FromEmail = "user@example.org", FromName = "Test User", To = "frank@example.org", ReceivedAt = t0.AddHours(-96), Text = "Sent.", Keywords = ["$seen"] }, sent);
        store.Deliver(new FakeMail { Subject = "Junk offer", FromEmail = "offers@example.net", FromName = "Offers", ReceivedAt = t0.AddHours(-6), Text = "Buy now." }, junk);
    }

    /// <summary>Data for a second signed-in account (the unified inbox, account order; the session side is stream unified's in wave 2): a small inbox and an Archive.</summary>
    public static void FillSecondAccount(FakeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.AddMailbox("Inbox", "inbox");
        var archive = store.AddMailbox("Archive", "archive");
        store.AddMailbox("Receipts", null);
        var t0 = new DateTimeOffset(2026, 11, 3, 9, 30, 0, TimeSpan.Zero);
        store.Deliver(new FakeMail { Subject = "Second account hello", FromEmail = "gina@example.net", FromName = "Gina", To = "second@example.net", ReceivedAt = t0, Text = "Hello from the second account." });
        store.Deliver(new FakeMail { Subject = "Second account receipt", FromEmail = "shop@example.net", FromName = "Shop", To = "second@example.net", ReceivedAt = t0.AddHours(2), Text = "Receipt.", Keywords = ["$seen"] });
        store.Deliver(new FakeMail { Subject = "Second account old", FromEmail = "gina@example.net", FromName = "Gina", To = "second@example.net", ReceivedAt = t0.AddDays(-30), Text = "Old.", Keywords = ["$seen"] }, archive);
    }

    /// <summary>Data for a shared (group) mail account: a mailbox the user may read but not change, with two messages (the session side is stream unified's in wave 2).</summary>
    public static void FillSharedAccount(FakeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.AddMailbox("Inbox", "inbox");
        store.AddMailbox("Support", null);
        var t0 = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        store.Deliver(new FakeMail { Subject = "Shared: ticket 1", FromEmail = "customer@example.net", FromName = "Customer", To = "team@example.org", ReceivedAt = t0, Text = "Please help." });
        store.Deliver(new FakeMail { Subject = "Shared: ticket 2", FromEmail = "customer@example.net", FromName = "Customer", To = "team@example.org", ReceivedAt = t0.AddHours(1), Text = "Me too.", Keywords = ["$seen"] });
    }

    private const string HeavyParagraph = "<p>Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p>";

    private const string Invitation =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Example//Synthetic//EN\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nUID:design-review-1@example.org\r\n"
        + "DTSTAMP:20261103T080000Z\r\nDTSTART;TZID=Europe/Berlin:20261117T100000\r\nDTEND;TZID=Europe/Berlin:20261117T113000\r\nSUMMARY:Design review\r\nLOCATION:Room 4\r\n"
        + "ORGANIZER;CN=Olga:mailto:olga@acme.example\r\nATTENDEE;CN=Me;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:user@example.org\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
}
