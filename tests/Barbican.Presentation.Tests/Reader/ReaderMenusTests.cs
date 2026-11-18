// spec/menus.md MENU-READER (the reader's More menu) and MENU-ATT (an attachment chip); I-200, I-226, I-227. The rows are data: what is on, what is off, what the rules
// section adds, and that a row whose command is not registered shows disabled. Owner reader.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Reader;
using static Barbican.Presentation.Tests.Menus.MenuKit;

namespace Barbican.Presentation.Tests.Reader;

public sealed class ReaderMenusTests
{
    private static EmailBody Body(params string[] attachmentNames) => new()
    {
        Id = "m1",
        Attachments = [.. attachmentNames.Select((n, i) => new Attachment { BlobId = "b" + i, Name = n, Type = "application/pdf", Size = 10 })],
    };

    private static MenuContext Reader(MailTarget[] targets, int? attachment = null, string menu = MenuIds.Reader) =>
        new(menu, targets, null, null, null, attachment, CommandSurface.ContextMenu);

    [Fact]
    public void The_reader_menu_lists_the_message_actions_then_the_rules()
    {
        var rows = ReaderMenus.Reader(Reader([Target("m1")]), Body("a.pdf"), _ => true);
        Assert.Equal(
            [
                "View source", "Forward as attachment", "Download (.eml)", "Download all attachments (.zip)", "—", "# Rules", "Always move messages from the sender ›",
                "Always move messages from this domain ›", "Always move messages from this list ›", "Always mark as read from the sender", "Always tag with ›", "Block sender", "Manage rules",
            ],
            Labels(rows));
        Assert.True(rows.Single(r => r.Label.StartsWith("Download all", StringComparison.Ordinal)).Enabled);
    }

    [Fact]
    public void Download_all_is_off_without_attachments_and_block_sender_needs_a_junk_folder()
    {
        var rows = ReaderMenus.Reader(Reader([Target("m1")]), Body(), _ => false);
        Assert.False(rows.Single(r => r.Label.StartsWith("Download all", StringComparison.Ordinal)).Enabled);
        Assert.False(rows.Single(r => string.Equals(r.Label, "Block sender", StringComparison.Ordinal)).Enabled);
    }

    [Fact]
    public void No_open_message_means_no_menu()
    {
        Assert.Empty(ReaderMenus.Reader(Reader([Target("m1")]), null, _ => true));
        Assert.Empty(ReaderMenus.Reader(Reader([]), Body(), _ => true));
    }

    [Fact]
    public void An_attachment_menu_saves_copies_the_name_and_offers_the_zip_only_for_several()
    {
        var one = ReaderMenus.Attachment(Reader([Target("m1")], 0, MenuIds.Attachment), Body("a.pdf"));
        Assert.Equal(["Save as…", "—", "Copy file name"], Labels(one));
        Assert.Equal("0", one[0].Argument);

        var two = ReaderMenus.Attachment(Reader([Target("m1")], 1, MenuIds.Attachment), Body("a.pdf", "b.pdf"));
        Assert.Equal(["Save as…", "Download all (.zip)", "—", "Copy file name"], Labels(two));
        Assert.Equal("1", two[0].Argument);
    }

    [Fact]
    public void A_picture_or_text_attachment_offers_a_preview_first_and_a_pdf_does_not()
    {
        var body = new EmailBody
        {
            Id = "m1",
            Attachments =
            [
                new Attachment { BlobId = "b0", Name = "p.png", Type = "image/png", Size = 10 },
                new Attachment { BlobId = "b1", Name = "n.txt", Type = "text/plain", Size = 10 },
                new Attachment { BlobId = null, Name = "gone.png", Type = "image/png", Size = 10 },
            ],
        };
        var image = ReaderMenus.Attachment(Reader([Target("m1")], 0, MenuIds.Attachment), body);
        Assert.Equal("Preview", image[0].Label);
        Assert.Equal(CommandIds.OpenAttachment, image[0].CommandId);
        Assert.Equal("0", image[0].Argument);
        Assert.Equal("Preview", ReaderMenus.Attachment(Reader([Target("m1")], 1, MenuIds.Attachment), body)[0].Label);
        Assert.DoesNotContain(ReaderMenus.Attachment(Reader([Target("m1")], 2, MenuIds.Attachment), body), r => string.Equals(r.Label, "Preview", StringComparison.Ordinal));
        Assert.DoesNotContain(ReaderMenus.Attachment(Reader([Target("m1")], 0, MenuIds.Attachment), Body("a.pdf")), r => string.Equals(r.Label, "Preview", StringComparison.Ordinal));
    }

    [Fact]
    public void An_attachment_index_that_is_not_there_has_no_menu()
    {
        Assert.Empty(ReaderMenus.Attachment(Reader([Target("m1")], 3, MenuIds.Attachment), Body("a.pdf")));
        Assert.Empty(ReaderMenus.Attachment(Reader([Target("m1")], null, MenuIds.Attachment), Body("a.pdf")));
    }

    [Fact]
    public void Through_the_menu_builder_only_registered_commands_are_on()
    {
        var all = AllCommands();
        var builder = new MenuModelBuilder(ReaderMenus.Providers(() => Body("a.pdf"), _ => true), new CommandRegistry([all[CommandIds.ViewSource], all[CommandIds.DownloadEml]]));
        var rows = builder.Build(Reader([Target("m1")]));
        Assert.True(rows.Single(r => string.Equals(r.Label, "View source", StringComparison.Ordinal)).Enabled);
        Assert.True(rows.Single(r => string.Equals(r.Label, "Download (.eml)", StringComparison.Ordinal)).Enabled);
        Assert.False(rows.Single(r => string.Equals(r.Label, "Forward as attachment", StringComparison.Ordinal)).Enabled);
    }
}
