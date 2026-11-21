// Tests the new-mail rules of Bulwark lib/desktop/new-mail.ts:37-48 and lib/desktop/mail-notifications.ts:117-200 and the bell's list (notification-center-store.ts). Owner notifications.

using Barbican.Core.Mail.Model;
using Barbican.Core.Notifications;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Notifications;

public sealed class NewMailNotifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Dictionary<string, string> Roles = new(StringComparer.Ordinal) { ["inbox"] = "I", ["junk"] = "J", ["trash"] = "T", ["sent"] = "S", ["drafts"] = "D", ["archive"] = "A" };
    private static readonly HashSet<string> NoMutes = new(StringComparer.Ordinal);

    private readonly FakeTimeProvider _time = new(Now);

    private static EmailSummary Mail(string id, string folder = "I", string? from = "Ada", string? subject = null, bool seen = false, bool draft = false, double hoursAgo = 0.1, params string[] more)
    {
        var boxes = new Dictionary<string, bool>(StringComparer.Ordinal) { [folder] = true };
        foreach (var m in more)
        {
            boxes[m] = true;
        }

        return new EmailSummary
        {
            Id = id,
            ThreadId = "t" + id,
            MailboxIds = boxes,
            Subject = subject ?? "Subject " + id,
            ReceivedAt = Now.AddHours(-hoursAgo),
            From = from is null ? [] : [new EmailAddress(from, "ada@a.example.com")],
            Keywords = seen && draft ? Keywords.Of("$seen", "$draft") : seen ? Keywords.Of("$seen") : draft ? Keywords.Of("$draft") : Keywords.None,
        };
    }

    private NewMailNotice? Decide(IReadOnlyList<EmailSummary> mail, NewMailNotificationSettings? settings = null, HashSet<string>? muted = null, NewMailNotifier? notifier = null) =>
        (notifier ?? new NewMailNotifier(_time)).Decide("acct", mail, Roles, muted ?? NoMutes, settings ?? new NewMailNotificationSettings());

    [Fact]
    public void OneInboxMessageNotifiesWithSenderSubjectAndTheMessageToOpen()
    {
        var notice = Decide([Mail("m1", subject: "Lunch?")]);

        Assert.NotNull(notice);
        Assert.Equal("Ada", notice.Title);
        Assert.Equal("Lunch?", notice.Body);
        Assert.Equal("m1", notice.EmailId);
        Assert.Equal("tm1", notice.ThreadId);
        Assert.Equal(1, notice.Count);
        Assert.True(notice.PlaySound);
        Assert.Equal("involved", notice.SoundChoice);
    }

    [Fact]
    public void SeveralMessagesAreToldOnceWithTheNewestAndAClickOpensTheInbox()
    {
        var notice = Decide([Mail("m1", hoursAgo: 0.5), Mail("m2", from: "Bob", subject: "Newest", hoursAgo: 0.1), Mail("m3", hoursAgo: 0.9)]);

        Assert.NotNull(notice);
        Assert.Equal("3 new messages", notice.Title);
        Assert.Equal("Bob: Newest", notice.Body);
        Assert.Null(notice.EmailId);
    }

    [Fact]
    public void SeenDraftAndOldMessagesAreSilent()
    {
        Assert.Null(Decide([Mail("a", seen: true)]));
        Assert.Null(Decide([Mail("b", draft: true)]));
        Assert.Null(Decide([Mail("c", hoursAgo: 25)]));
    }

    [Theory]
    [InlineData("J")]
    [InlineData("T")]
    [InlineData("S")]
    [InlineData("D")]
    [InlineData("A")]
    public void FoldersMailDoesNotArriveInAreSilentByDefault(string folder) => Assert.Null(Decide([Mail("m", folder)]));

    [Fact]
    public void OtherFoldersNotifyUnlessInboxOnlyIsOn()
    {
        Assert.NotNull(Decide([Mail("m", "Work")]));
        Assert.Null(Decide([Mail("m", "Work")], new NewMailNotificationSettings { InboxOnly = true }));
        Assert.NotNull(Decide([Mail("n", "I")], new NewMailNotificationSettings { InboxOnly = true }));
    }

    [Fact]
    public void AMutedFolderIsSilentUnlessTheMessageIsAlsoInAnUnmutedOne()
    {
        Assert.Null(Decide([Mail("m", "I")], muted: ["I"]));
        Assert.NotNull(Decide([Mail("n", "I", more: "Work")], muted: ["I"]));
        Assert.Null(Decide([Mail("o", "I", more: "Work")], muted: ["I", "Work"]));
    }

    [Fact]
    public void NotificationsOffAndDoNotDisturbSilenceEverything()
    {
        Assert.Null(Decide([Mail("m")], new NewMailNotificationSettings { Enabled = false }));
        Assert.Null(Decide([Mail("n")], new NewMailNotificationSettings { DoNotDisturb = true }));
    }

    [Fact]
    public void SoundFollowsTheSettingsAndTheChosenTone()
    {
        var notice = Decide([Mail("m")], new NewMailNotificationSettings { Sound = false, SoundChoice = "swift" });

        Assert.NotNull(notice);
        Assert.False(notice.PlaySound);
        Assert.Equal("swift", notice.SoundChoice);
    }

    [Fact]
    public void AMessageIsToldOnlyOnce()
    {
        var notifier = new NewMailNotifier(_time);
        var mail = Mail("m1");

        Assert.NotNull(Decide([mail], notifier: notifier));
        Assert.Null(Decide([mail], notifier: notifier));
    }

    [Fact]
    public void ContentStaysOffTheNotificationWhenHidden()
    {
        var notice = new NewMailNotifier(_time).Decide("acct", [Mail("m1")], Roles, NoMutes, new NewMailNotificationSettings(), hideContent: true);

        Assert.NotNull(notice);
        Assert.Equal("New mail", notice.Title);
        Assert.Equal(string.Empty, notice.Body);
    }

    [Fact]
    public void TheCentreKeepsTheNewest50NewestFirstAndCountsUnread()
    {
        var center = new NotificationCenter(_time);
        for (var i = 0; i < 60; i++)
        {
            center.Add("info", "n" + i, string.Empty, "app");
        }

        Assert.Equal(NotificationCenter.MaxEntries, center.Entries.Count);
        Assert.Equal("n59", center.Entries[0].Title);
        Assert.Equal(50, center.Unread);

        center.MarkRead(center.Entries[0].Id);
        Assert.Equal(49, center.Unread);
        center.MarkAllRead();
        Assert.Equal(0, center.Unread);
        center.Clear();
        Assert.Empty(center.Entries);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(5, "5")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void TheBellBadgeSaysNinetyNinePlus(int count, string text) => Assert.Equal(text, NotificationCenter.BadgeText(count));
}
