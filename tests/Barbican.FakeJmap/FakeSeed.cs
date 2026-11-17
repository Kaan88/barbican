// The fake server's seed mail (M1-plan §6.1): synthetic, example.org addresses only. A conversation of 5, attachments (a file and an
// inline cid: image), a dark-native HTML message, a 3 MB body (truncated by maxBodyValueBytes 2,000,000, so isTruncated), and a few
// plain messages across the role folders. Owner jmap.

using System.Text;

namespace Barbican.FakeJmap;

/// <summary>Fills a store with the seed account.</summary>
public static partial class FakeSeed
{
    /// <summary>The subject of the 5-message conversation.</summary>
    public const string ThreadSubject = "Quarterly planning";

    /// <summary>The subject of the message with the 3 MB body.</summary>
    public const string LargeSubject = "Large export";

    /// <summary>The size of the large body, in bytes.</summary>
    public const int LargeBodyBytes = 3_000_000;

    private static readonly DateTimeOffset Start = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Adds the folders and the messages.</summary>
    public static void Fill(FakeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var inbox = store.AddMailbox("Inbox", "inbox");
        store.AddMailbox("Drafts", "drafts");
        var sent = store.AddMailbox("Sent", "sent");
        store.AddMailbox("Trash", "trash");
        var junk = store.AddMailbox("Junk", "junk");
        var archive = store.AddMailbox("Archive", "archive");
        store.AddMailbox("Projects", null, inbox);

        Conversation(store);
        store.Deliver(new FakeMail
        {
            Subject = "Report with attachments",
            FromName = "Carol",
            FromEmail = "carol@example.org",
            ReceivedAt = Start.AddHours(30),
            Text = "The report is attached; the chart is inline.",
            Html = "<p>The report is attached; the chart is inline.</p><img src=\"cid:chart1@example.org\">",
            Attachments =
            [
                new FakeAttachment("report.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 synthetic")),
                new FakeAttachment("chart.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "chart1@example.org"),
            ],
        });
        store.Deliver(new FakeMail
        {
            Subject = "Dark mode newsletter",
            FromName = "Newsletter",
            FromEmail = "news@example.org",
            ReceivedAt = Start.AddHours(31),
            Text = "This month in dark mode.",
            Html = "<html><head><meta name=\"color-scheme\" content=\"light dark\"><style>@media (prefers-color-scheme: dark){body{background:#111;color:#eee}}</style></head><body><h1>This month</h1><p>In dark mode.</p></body></html>",
            Keywords = ["$seen"],
        });
        store.Deliver(new FakeMail
        {
            Subject = LargeSubject,
            FromName = "Exporter",
            FromEmail = "export@example.org",
            ReceivedAt = Start.AddHours(32),
            Text = new string('x', LargeBodyBytes),
        });
        store.Deliver(new FakeMail { Subject = "Flagged reminder", FromEmail = "dave@example.org", FromName = "Dave", ReceivedAt = Start.AddHours(33), Text = "Remember the meeting.", Keywords = ["$flagged"] });
        store.Deliver(new FakeMail { Subject = "Old receipt", FromEmail = "shop@example.org", FromName = "Shop", ReceivedAt = Start.AddHours(-48), Text = "Thank you.", Keywords = ["$seen"] }, archive);
        store.Deliver(new FakeMail { Subject = "Win a prize", FromEmail = "spam@example.org", FromName = "Prize", ReceivedAt = Start.AddHours(34), Text = "Click here." }, junk);
        store.Deliver(new FakeMail { Subject = "My note", FromEmail = "user@example.org", FromName = "Test User", To = "erin@example.org", ReceivedAt = Start.AddHours(35), Text = "Sent by me.", Keywords = ["$seen"] }, sent);
    }

    private static void Conversation(FakeStore store)
    {
        string? last = null;
        string[] people = ["alice", "user", "alice", "bob", "user"];
        for (var i = 0; i < people.Length; i++)
        {
            last = store.Deliver(new FakeMail
            {
                Subject = i == 0 ? ThreadSubject : "Re: " + ThreadSubject,
                FromName = char.ToUpperInvariant(people[i][0]) + people[i][1..],
                FromEmail = people[i] + "@example.org",
                ReceivedAt = Start.AddHours(i * 6),
                Text = $"Message {i + 1} of the planning thread.",
                Keywords = i < 3 ? ["$seen"] : [],
                ReplyTo = last,
            });
        }
    }
}
