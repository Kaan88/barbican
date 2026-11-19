// Bulwark thread-list-item.tsx:573-590: account tint outranks the first resolved tag color.
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class TagRowTintTests
{
    private const string Tags = """[{"id":"work","label":"Work","color":"blue"},{"id":"home","label":"Home","color":"red"}]""";

    private static RowContext Context(bool enabled, bool cvd = false) => MailListSettings.Read(PrefsSnapshot.FromJson(new JsonObject
    {
        ["emailKeywords"] = JsonNode.Parse(Tags), ["tintListRowsByTag"] = enabled, ["colorBlindPalette"] = cvd,
        ["showTagChips"] = false,
    }), default(SystemAccessibility)).RowContextAt(Mails.Now, TimeZoneInfo.Utc);

    private static EmailSummary Tagged() => Mails.Make("m1") with { Keywords = Keywords.Of("$label:home", "$label:work") };

    [Fact]
    public void First_configured_tag_uses_the_same_accessibility_adjusted_color_as_the_tag_chip()
    {
        var normal = new MessageRowViewModel(Tagged(), Context(true));
        var accessible = new MessageRowViewModel(Tagged(), Context(true, cvd: true));
        Assert.True(normal.ShowTagTint);
        Assert.False(normal.ShowTags); // hiding chips does not hide the row tint
        Assert.Equal("work", normal.Tags[0].Id);
        Assert.Equal(normal.Tags[0].Color, normal.TagTintColor);
        Assert.Equal(accessible.Tags[0].Color, accessible.TagTintColor);
        Assert.Equal(A11y.CvdColor(normal.TagTintColor), accessible.TagTintColor);
        Assert.False(new MessageRowViewModel(Tagged(), Context(false)).ShowTagTint);
        Assert.False(new MessageRowViewModel(Mails.Make("none"), Context(true)).ShowTagTint);
    }

    [Fact]
    public void Account_tint_takes_precedence_and_higher_contrast_suppresses_fill()
    {
        var context = Context(true) with { TintListRowsByAccount = true, AccountOfRow = _ => ("Account", 2) };
        var row = new MessageRowViewModel(Tagged(), context);
        Assert.True(row.ShowAccountTint);
        Assert.False(row.ShowTagTint);
        Assert.Equal(row.Tags[0].Color, row.TagTintColor);
        var contrast = new MessageRowViewModel(Tagged(), context with { MoreContrast = true });
        Assert.False(contrast.ShowAccountTint);
        Assert.False(contrast.ShowTagTint);
        var noAccount = new MessageRowViewModel(Tagged(), context with { AccountOfRow = _ => null });
        Assert.True(noAccount.ShowTagTint);
        Assert.False(row.SameAs(noAccount));
    }

    [Fact]
    public async Task Changing_the_setting_rebuilds_existing_rows()
    {
        using var h = new ListHarness("{\"groupListByDate\":false}");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Ok(new ListPage { Items = [Tagged()], Total = 1 }));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.False(h.List.Messages.Single().ShowTagTint);
        h.Prefs.Set("{\"groupListByDate\":false,\"tintListRowsByTag\":true}");
        Assert.True(h.List.Messages.Single().ShowTagTint);
        h.Prefs.Set("{\"groupListByDate\":false,\"tintListRowsByTag\":false}");
        Assert.False(h.List.Messages.Single().ShowTagTint);
    }
}
