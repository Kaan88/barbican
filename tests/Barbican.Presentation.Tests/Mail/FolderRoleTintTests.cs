// Bulwark components/layout/sidebar.tsx:263-284 (ROLE_ICON_COLOR: the chart tokens per role, muted Trash, the colour-blind palette swap; a plain folder takes the text colour). SB-04.
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Tests.Mail;

public sealed class FolderRoleTintTests
{
    private static IReadOnlyList<FolderRowViewModel> Rows(FolderRowOptions options) => FolderRows.Build(
        new MailboxTree(
        [
            new Mailbox { Id = "i", Name = "Inbox", Role = MailboxRole.Inbox },
            new Mailbox { Id = "d", Name = "Drafts", Role = MailboxRole.Drafts },
            new Mailbox { Id = "s", Name = "Sent", Role = MailboxRole.Sent },
            new Mailbox { Id = "a", Name = "Archive", Role = MailboxRole.Archive },
            new Mailbox { Id = "j", Name = "Junk", Role = MailboxRole.Junk },
            new Mailbox { Id = "t", Name = "Trash", Role = MailboxRole.Trash },
            new Mailbox { Id = "w", Name = "Work" },
        ]),
        new HashSet<string>(StringComparer.Ordinal),
        options);

    [Fact]
    public void Colourful_icons_take_the_chart_token_of_their_role()
    {
        var byId = Rows(new FolderRowOptions { Colorful = true }).ToDictionary(r => r.Id, StringComparer.Ordinal);
        Assert.Equal("Chart1", byId["i"].RoleTint);
        Assert.Equal("Chart2", byId["s"].RoleTint);
        Assert.Equal("Chart5", byId["d"].RoleTint);
        Assert.Equal("Chart4", byId["j"].RoleTint);
        Assert.Equal("Chart3", byId["a"].RoleTint);
        Assert.Equal("Dim", byId["t"].RoleTint);
        Assert.Null(byId["w"].RoleTint);
        Assert.All(byId.Values, r => Assert.Equal(0u, r.RoleCvdRgb));
    }

    [Fact]
    public void The_colour_blind_palette_swaps_the_tokens_for_its_own_colours()
    {
        var byId = Rows(new FolderRowOptions { Colorful = true, Cvd = true }).ToDictionary(r => r.Id, StringComparer.Ordinal);
        Assert.Null(byId["i"].RoleTint);
        Assert.Equal(0x0072B2u, byId["i"].RoleCvdRgb);
        Assert.Equal(0x009E73u, byId["s"].RoleCvdRgb);
        Assert.Equal(0xCC79A7u, byId["d"].RoleCvdRgb);
        Assert.Equal(0xD55E00u, byId["j"].RoleCvdRgb);
        Assert.Equal(0x56B4E9u, byId["a"].RoleCvdRgb);
        Assert.Equal("Dim", byId["t"].RoleTint);
        Assert.Equal(0u, byId["w"].RoleCvdRgb);
    }

    [Fact]
    public void Plain_icons_carry_no_tint()
    {
        Assert.All(Rows(new FolderRowOptions { Colorful = false }), r =>
        {
            Assert.Null(r.RoleTint);
            Assert.Equal(0u, r.RoleCvdRgb);
        });
    }
}
