// Tests the wiring of Bulwark lib/desktop/mail-notifications.ts:180-219 (3 s batch, one notice per account, bell entry, tone once) and hooks/use-app-badge.ts (badge count and its settings). Owner notifications.

using Barbican.Core.Mail.Model;
using Barbican.Core.Notifications;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Notifications;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Notifications;

public sealed class NewMailNotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class Folders(IReadOnlySet<string> muted) : INotificationFolderInfo
    {
        public Task<(IReadOnlyDictionary<string, string> RoleIds, IReadOnlySet<string> Muted)> GetAsync(string accountKey, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyDictionary<string, string>, IReadOnlySet<string>)>((new Dictionary<string, string>(StringComparer.Ordinal) { ["inbox"] = "I", ["junk"] = "J" }, muted));

        public void Invalidate(string? accountKey = null)
        {
        }
    }

    private sealed class Rig : IDisposable
    {
        public Rig(string prefs = "{}", IReadOnlySet<string>? muted = null)
        {
            Time = new FakeTimeProvider(Now);
            Prefs = new TestPrefs(prefs);
            Hub = new MailEventHub(new FakeActiveMail(), new InlineDispatcher());
            Center = new NotificationCenter(Time);
            Notifier = new NullDesktopNotifier();
            Opener = new RecordingOpener();
            Service = new NewMailNotificationService(Hub, Prefs, new Folders(muted ?? new HashSet<string>(StringComparer.Ordinal)), Center, Notifier, Time, opener: Opener);
            _ = Service.StartAsync(CancellationToken.None);
        }

        public FakeTimeProvider Time { get; }

        public TestPrefs Prefs { get; }

        public MailEventHub Hub { get; }

        public NotificationCenter Center { get; }

        public NullDesktopNotifier Notifier { get; }

        public RecordingOpener Opener { get; }

        public NewMailNotificationService Service { get; }

        public void Arrive(string account, params EmailSummary[] mail) => Hub.Publish(new NewMailEvent(mail), account);

        public void Dispose()
        {
            Service.Dispose();
            Hub.Dispose();
        }
    }

    private static EmailSummary Mail(string id, string folder = "I", string from = "Ada") =>
        new() { Id = id, ThreadId = "t" + id, MailboxIds = new Dictionary<string, bool>(StringComparer.Ordinal) { [folder] = true }, Subject = "S" + id, ReceivedAt = Now.AddMinutes(-1), From = [new EmailAddress(from, "x@a.example.com")] };

    [Fact]
    public void MailThatArrivesTogetherIsToldOnceAfterTheBatchWait()
    {
        using var rig = new Rig();
        rig.Arrive("acct", Mail("m1"));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.Arrive("acct", Mail("m2"));
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(rig.Notifier.Shown);

        rig.Time.Advance(TimeSpan.FromSeconds(2));

        var notice = Assert.Single(rig.Notifier.Shown);
        Assert.Equal("2 new messages", notice.Title);
        Assert.Single(rig.Center.Entries);
        Assert.Equal(1, rig.Center.Unread);
    }

    [Fact]
    public void ADisabledSettingAndAMutedFolderShowNothing()
    {
        using (var off = new Rig("{\"emailNotificationsEnabled\":false}"))
        {
            off.Arrive("acct", Mail("m1"));
            off.Time.Advance(TimeSpan.FromSeconds(4));
            Assert.Empty(off.Notifier.Shown);
            Assert.Empty(off.Center.Entries);
        }

        using var muted = new Rig(muted: new HashSet<string>(StringComparer.Ordinal) { "I" });
        muted.Arrive("acct", Mail("m1"));
        muted.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Empty(muted.Notifier.Shown);
    }

    [Fact]
    public void AClickOnTheNotificationOpensTheMessage()
    {
        using var rig = new Rig();
        rig.Arrive("acct", Mail("m1"));
        rig.Time.Advance(TimeSpan.FromSeconds(4));
        var notice = Assert.Single(rig.Notifier.Shown);

        rig.Notifier.Click(notice);

        Assert.Equal("m1", Assert.Single(rig.Opener.Opened).Id);
    }

    [Fact]
    public void AClickOnTheBellEntryReadsItAndOpensTheMessage()
    {
        using var rig = new Rig();
        using var bell = new NotificationCenterViewModel(rig.Center, rig.Time, rig.Service);
        rig.Arrive("acct", Mail("m1"));
        rig.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal("1", bell.BadgeText);

        bell.OpenEntry(bell.Rows[0].Id);

        Assert.Equal("m1", Assert.Single(rig.Opener.Opened).Id);
        Assert.Equal(string.Empty, bell.BadgeText);
        Assert.Equal("Notifications", bell.BellLabel);
    }

    [Fact]
    public void EachAccountGetsItsOwnNotice()
    {
        using var rig = new Rig();
        rig.Arrive("one", Mail("a"));
        rig.Arrive("two", Mail("a"));
        rig.Time.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(2, rig.Notifier.Shown.Count);
        Assert.Equal("one,two", string.Join(",", rig.Notifier.Shown.Select(n => n.AccountKey).Order(StringComparer.Ordinal)));
    }

    private sealed class Others(long total) : Barbican.Core.Mail.Unified.IUnifiedUnread
    {
        public long Total { get; set; } = total;

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static NewMailNotificationService Badge(string prefs, Others others, NullDesktopNotifier notifier)
    {
        var time = new FakeTimeProvider(Now);
        var hub = new MailEventHub(new FakeActiveMail(), new InlineDispatcher());
        return new NewMailNotificationService(hub, new TestPrefs(prefs), new Folders(new HashSet<string>(StringComparer.Ordinal)), new NotificationCenter(time), notifier, time, others: others);
    }

    [Fact]
    public void TheBadgeCountsTheOtherAccountsOnlyWhenAllAccountsIsOnAndClearsWhenItsSettingIsOff()
    {
        var notifier = new NullDesktopNotifier();
        var others = new Others(5);
        using var all = Badge("{}", others, notifier);
        Assert.Equal(5, all.BadgeCount());
        _ = all.StartAsync(CancellationToken.None);
        Assert.Equal(5, notifier.Badge);
        others.Total = 7;
        others.Raise();
        Assert.Equal(7, notifier.Badge);

        using var own = Badge("{\"unreadBadgeAllAccounts\":false}", others, notifier);
        Assert.Equal(0, own.BadgeCount());
        using var off = Badge("{\"faviconUnreadBadge\":false}", others, notifier);
        Assert.Equal(0, off.BadgeCount());
    }
}
