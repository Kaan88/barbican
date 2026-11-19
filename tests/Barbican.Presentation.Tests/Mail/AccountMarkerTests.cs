// Bulwark components/email/thread-list-item.tsx:117-125, 551-590: account tint takes precedence over account dots.
using System.Text.Json.Nodes;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.FakeJmap;
using Barbican.Presentation.Unified;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class AccountMarkerTests
{
    private static MessageRowViewModel Row(bool tint, bool dot, bool contrast = false, bool multiple = true, bool known = true)
    {
        var prefs = PrefsSnapshot.FromJson(new JsonObject { ["tintListRowsByAccount"] = tint, ["showAccountDot"] = dot });
        var settings = MailListSettings.Read(prefs, default(SystemAccessibility));
        var context = settings.RowContextAt(Mails.Now, TimeZoneInfo.Utc) with
        {
            MoreContrast = contrast,
            MultipleAccountSources = multiple,
            AccountOfRow = _ => known ? ("Work account", 2) : null,
        };
        return new MessageRowViewModel(Mails.Make("m1", subject: "Hello", from: "Ada"), context);
    }

    [Fact]
    public void Preferences_choose_dot_or_tint_and_keep_the_account_accessible()
    {
        var dot = Row(false, true);
        Assert.True(dot.ShowAccountDot);
        Assert.False(dot.ShowAccountTint);
        Assert.Equal(2, dot.AccountColorIndex);
        Assert.Contains("Work account", dot.AccessibleName);
        var tint = Row(true, true);
        Assert.True(tint.ShowAccountTint);
        Assert.False(tint.ShowAccountDot);
        Assert.False(dot.SameAs(tint)); // a preference change must replace the displayed row
        Assert.False(Row(false, false).ShowAccountDot);
        Assert.False(Row(false, true, multiple: false).ShowAccountDot);
        Assert.False(Row(true, true, known: false).ShowAccountTint);
    }

    [Fact]
    public void Higher_contrast_uses_a_dot_instead_of_a_subtle_tint()
    {
        var row = Row(true, false, contrast: true);
        Assert.False(row.ShowAccountTint);
        Assert.True(row.ShowAccountDot);
    }
    [Fact]
    public async Task Unified_rows_follow_their_source_and_refresh_when_preferences_change()
    {
        var a = await FakeJmapHarness.ConnectAsync(new FakeJmapServer(new FakeJmapOptions { Origin = "https://a.example.org", AccountId = "a", Username = "a@example.org" }), TimeProvider.System, ct: TestContext.Current.CancellationToken);
        var b = await FakeJmapHarness.ConnectAsync(new FakeJmapServer(new FakeJmapOptions { Origin = "https://b.example.org", AccountId = "b", Username = "b@example.org" }), TimeProvider.System, ct: TestContext.Current.CancellationToken);
        using var h = new ListHarness("{\"unifiedCrossAccount\":true,\"showAccountDot\":true,\"groupListByDate\":false}");
        using var inbox = new UnifiedInbox(new TestLogins(a, b), null, h.Prefs, h.Hub);
        using var list = new MessageListViewModel(h.Active, h.Hub, h.Bus, h.Opener, h.Prefs, h.System, h.Motion, h.Time, unified: inbox);
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(new ListPage
        {
            Items = [Mails.Make(UnifiedRows.RowId("A", null, "1")), Mails.Make(UnifiedRows.RowId("B", null, "1"))],
            Total = 2,
        }));
        await list.OpenFolderAsync(UnifiedRows.FolderId, MailboxRole.Inbox, "All inboxes");
        var rows = list.Messages.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("a@example.org", rows[0].AccountDescription);
        Assert.Contains("b@example.org", rows[1].AccountDescription);
        Assert.All(list.Messages, r => Assert.True(r.ShowAccountDot));
        h.Prefs.Set("{\"unifiedCrossAccount\":true,\"tintListRowsByAccount\":true,\"groupListByDate\":false}");
        Assert.All(list.Messages, r => { Assert.True(r.ShowAccountTint); Assert.False(r.ShowAccountDot); });
    }

    private sealed class TestLogins(IJmapClient a, IJmapClient b) : IUnifiedLogins
    {
        public string? Active => "A";
        public IReadOnlyList<(string Login, IJmapClient? Client)> Logins => [("A", a), ("B", b)];
        public IJmapClient? ClientOf(string login) => string.Equals(login, "A", StringComparison.Ordinal) ? a : b;
        public bool IsTestServer(string login) => true;
    }

}
