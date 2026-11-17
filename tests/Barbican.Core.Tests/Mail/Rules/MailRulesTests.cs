// Ported from Rust src/mail.rs tests (:1142-:1167).

using Barbican.Core.Mail.Rules;

namespace Barbican.Core.Tests.Mail.Rules;

public sealed class MailRulesTests
{
    [Fact]
    [RustTest("mail::forced_read_never_toggles")]
    public void Forced_read_never_toggles()
    {
        Assert.Equal("read", MailRules.Forced("read!"));
        Assert.Equal("unread", MailRules.Forced("unread!"));
        Assert.Null(MailRules.Forced("read"));
        Assert.Equal("read", MailRules.ResolveToggle(["nope"], "read!", _ => null));
        Assert.Equal("unread", MailRules.ResolveToggle(["nope"], "unread!", _ => null));
    }

    [Fact]
    [RustTest("mail::unread_badge_title")]
    public void Unread_badge_title()
    {
        Assert.Equal("(5) Barbican", MailRules.BadgeTitle(true, 5));
        Assert.Equal("Barbican", MailRules.BadgeTitle(true, 0));
        Assert.Equal("Barbican", MailRules.BadgeTitle(false, 5));
    }

    [Fact]
    [RustTest("mail::archive_paths")]
    public void Archive_paths()
    {
        // local time of the message: mid-year dates are safe in any time zone
        Assert.Equal(["2024"], MailRules.ArchivePath("2024-06-15T12:00:00Z", "year"));
        Assert.Equal(["2024", "06"], MailRules.ArchivePath("2024-06-15T12:00:00Z", "month"));
        Assert.Empty(MailRules.ArchivePath("2024-06-15T12:00:00Z", "single"));
        Assert.Empty(MailRules.ArchivePath("nonsense", "year"));
        Assert.Equal(50, MailRules.UnreadSectionSize(50));
        Assert.Equal(25, MailRules.UnreadSectionSize(7));
    }
}
