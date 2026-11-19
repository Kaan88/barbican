// Verification codes use synthetic fixtures only. Bulwark verification-code-chip.tsx and verification-code.ts:284-301.
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.VerificationCode;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Mail;

public sealed class VerificationCodePreferencesTests
{
    private static EmailBody Body(DateTimeOffset received) => new()
    {
        Id = "code-message", Subject = "Your sign-in code", ReceivedAt = received,
        TextBody = [new BodyPart { PartId = "text", Type = "text/plain" }],
        BodyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal) { ["text"] = new("Your verification code is 482913.") },
    };

    [Fact]
    public void Reader_live_preferences_hide_and_restore_code_and_reject_late_banner()
    {
        var prefs = new TestPrefs();
        var time = new FakeTimeProvider(Mails.Now);
        using var reader = new ReaderViewModel(prefs, new VerificationCodeFinder(), time, new InlineDispatcher());
        reader.Apply(ReaderSnapshots.FromBody(Body(Mails.Now), null, new ReaderOptions()));
        var banner = Assert.Single(reader.Banners, item => string.Equals(item.Id, VerificationCodeBannerSource.Id, StringComparison.Ordinal));
        prefs.Set("{\"showVerificationCodes\":false}");
        Assert.Empty(reader.Banners);
        reader.AddBanners(reader.EmailId, [banner]);
        Assert.Empty(reader.Banners);
        prefs.Set("{\"showVerificationCodes\":true}");
        Assert.Single(reader.Banners);
        time.Advance(TimeSpan.FromHours(25));
        Assert.Empty(reader.Banners);
        reader.AddBanners(reader.EmailId, [banner]);
        Assert.Empty(reader.Banners);
    }

    [Fact]
    public void Production_composition_injects_preferences_and_finder_into_reader()
    {
        var prefs = new TestPrefs("{\"showVerificationCodes\":false}");
        var time = new FakeTimeProvider(Mails.Now);
        var services = new ServiceCollection();
        services.AddSingleton<IPrefsStore>(prefs);
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IVerificationCodeFinder, VerificationCodeFinder>();
        services.AddBarbicanMailCore();
        using var provider = services.BuildServiceProvider();
        var reader = provider.GetRequiredService<ReaderViewModel>();
        reader.Apply(ReaderSnapshots.FromBody(Body(Mails.Now), null, new ReaderOptions()));
        Assert.Empty(reader.Banners);
        prefs.Set("{\"showVerificationCodes\":true}");
        Assert.Single(reader.Banners);
    }

    [Fact]
    public async Task List_pref_live_toggle_and_clipboard_are_scoped_to_fresh_current_row()
    {
        using var h = new ListHarness("{\"showPreview\":false,\"groupListByDate\":false}");
        var clipboard = new Clip();
        using var list = new MessageListViewModel(h.Active, h.Hub, h.Bus, h.Opener, h.Prefs, h.System, h.Motion, h.Time, clipboard: clipboard);
        var fresh = Mails.Make("fresh", subject: "Your login code") with { Preview = "Your verification code is 482913." };
        var old = Mails.Make("old", hoursAgo: 25, subject: "Your login code 482913");
        var future = Mails.Make("future", hoursAgo: -1, subject: "Your login code 482913");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(new ListPage { Items = [fresh, old, future], Total = 3 }));
        await list.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        var rows = list.Messages.ToList();
        Assert.True(rows[0].ShowVerificationCode);
        Assert.Empty(rows[0].Preview);
        Assert.False(rows[1].ShowVerificationCode);
        Assert.False(rows[2].ShowVerificationCode);
        Assert.True(list.CopyVerificationCode(rows[0]));
        Assert.Equal("482913", clipboard.Text);
        h.Prefs.Set("{\"showVerificationCodes\":false,\"showPreview\":false,\"groupListByDate\":false}");
        Assert.All(list.Messages, row => Assert.False(row.ShowVerificationCode));
        Assert.False(list.CopyVerificationCode(rows[0]));
        h.Prefs.Set("{\"showVerificationCodes\":true,\"showPreview\":false,\"groupListByDate\":false}");
        Assert.True(list.Messages.First().ShowVerificationCode);
        h.Time.Advance(TimeSpan.FromHours(25));
        Assert.False(list.Messages.First().ShowVerificationCode);
        Assert.False(list.CopyVerificationCode(list.Messages.First()));
    }

    private sealed class Clip : IClipboardService
    {
        public string? Text { get; private set; }
        public void SetText(string text) => Text = text;
    }
}
