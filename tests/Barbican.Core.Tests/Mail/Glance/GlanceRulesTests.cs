// New tests for glance.rs (the Rust module had none; spec "Gaps and notes" of mail.md asks for tests of each tile). Owner mailcore.

using Barbican.Core.Mail.Glance;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Rules;

namespace Barbican.Core.Tests.Mail.Glance;

public sealed class GlanceRulesTests
{
    [Fact]
    public void The_greeting_follows_the_hour_and_names_the_user()
    {
        Assert.Equal("Good morning, Ada.", GlanceRules.Greeting(5, "Ada"));
        Assert.Equal("Good morning.", GlanceRules.Greeting(11, string.Empty));
        Assert.Equal("Good afternoon.", GlanceRules.Greeting(12, string.Empty));
        Assert.Equal("Good afternoon.", GlanceRules.Greeting(17, string.Empty));
        Assert.Equal("Good evening.", GlanceRules.Greeting(18, string.Empty));
        Assert.Equal("Good evening.", GlanceRules.Greeting(4, string.Empty));
    }

    [Fact]
    public void The_first_name_skips_identities_without_a_real_name()
    {
        Assert.Equal("Ada", GlanceRules.FirstName([new Identity("1", null, "x@a.example.com"), new Identity("2", "ada@a.example.com", "ada@a.example.com"), new Identity("3", "Ada Lovelace", "y@a.example.com")]));
        Assert.Equal(string.Empty, GlanceRules.FirstName([]));
    }

    [Fact]
    public void The_subtitle_counts_unread_messages()
    {
        Assert.Equal("Hi. Nothing unread here.", GlanceRules.ListSubtitle("Hi.", 0));
        Assert.Equal("Hi. 1 unread message.", GlanceRules.ListSubtitle("Hi.", 1));
        Assert.Equal("Hi. 12 unread messages.", GlanceRules.ListSubtitle("Hi.", 12));
    }

    [Fact]
    public void The_storage_meter_text()
    {
        Assert.Equal("12 GB", GlanceRules.Gigabytes(12_300_000_000));
        Assert.Equal("1.2 GB", GlanceRules.Gigabytes(1_230_000_000));
        Assert.Equal("0.4 GB", GlanceRules.Gigabytes(350_000_000));
        Assert.Equal("50 MB", GlanceRules.Gigabytes(50_000_000));
        Assert.Equal("1.2 GB of 15 GB", GlanceRules.QuotaText(new QuotaInfo(1_200_000_000, 15_000_000_000)));
        Assert.Equal("3.4 GB used", GlanceRules.QuotaText(new QuotaInfo(3_400_000_000, 0)));
        Assert.Equal(string.Empty, GlanceRules.QuotaText(null));
        Assert.Equal(1, GlanceRules.QuotaFraction(new QuotaInfo(20, 10)));
        Assert.Equal(-1, GlanceRules.QuotaFraction(new QuotaInfo(20, 0)));
    }

    [Fact]
    public void The_flagged_tile_names_up_to_two_senders_by_first_name()
    {
        EmailSummary From(string? name, string email) => new() { Id = email, From = [new EmailAddress(name, email)] };
        Assert.Equal(
            "Ada, cy",
            GlanceRules.FlaggedWho([From("Ada Lovelace", "a@a.example.com"), From(null, "cy@a.example.com"), From("Ada L", "a2@a.example.com"), From("Dee", "d@a.example.com")]));
        Assert.Equal(string.Empty, GlanceRules.FlaggedWho([]));
    }

    [Fact]
    public void Row_text_helpers()
    {
        Assert.Equal("AD", RowText.Initials("Ada Lovelace"));
        Assert.Equal("?", RowText.Initials("!!"));
        Assert.Equal("(unknown)", RowText.Sender(new EmailSummary { Id = "x" }));
        Assert.Equal("No preview available", RowText.CleanPreview("​ ­"));
        Assert.Equal("a b c", RowText.CleanPreview("a​ \n b\t\tc  "));
        Assert.Equal(RowText.TintIndex("Ada"), RowText.TintIndex("Ada"));
        Assert.InRange(RowText.TintIndex("Zed"), 0, 3);
    }

    [Fact]
    public void Time_labels_follow_the_date_format_setting()
    {
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var today = now.AddHours(-3);
        var lastWeekday = now.AddDays(-3);
        var old = now.AddDays(-30);
        Assert.Equal("09:00", RowText.TimeLabel(today, now, TimeZoneInfo.Utc, "smart", false));
        Assert.Equal("9:00 AM", RowText.TimeLabel(today, now, TimeZoneInfo.Utc, "smart", true));
        Assert.Equal("Sat 12:00", RowText.TimeLabel(lastWeekday, now, TimeZoneInfo.Utc, "smart", false));
        Assert.Equal("18 Oct", RowText.TimeLabel(old, now, TimeZoneInfo.Utc, "smart", false));
        Assert.Equal("3 days ago", RowText.TimeLabel(lastWeekday, now, TimeZoneInfo.Utc, "relative", false));
        Assert.Equal("Yesterday", RowText.TimeLabel(now.AddDays(-1), now, TimeZoneInfo.Utc, "relative", false));
        Assert.Equal("17/11/2026 09:00", RowText.TimeLabel(today, now, TimeZoneInfo.Utc, "full", false));
    }

    [Fact]
    public void The_attachment_chips_hide_inline_images_when_asked()
    {
        var mail = new EmailSummary
        {
            Id = "m",
            Attachments = [new Attachment { Name = "a.pdf", Type = "application/pdf" }, new Attachment { Name = "logo.png", Type = "image/png", Cid = "c" }, new Attachment { Type = "text/plain" }],
        };
        Assert.Equal(["a.pdf", "attachment"], RowText.AttachmentNames(mail, true));
        Assert.Equal(["a.pdf", "logo.png", "attachment"], RowText.AttachmentNames(mail, false));
    }
}
