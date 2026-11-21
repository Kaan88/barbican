// Synthetic-only tests for the attachment rename rules (Bulwark lib/attachment-name.ts splitAttachmentName, lib/file-name-rules.ts
// plainFileNameProblem, lib/download-filename.ts renamedAttachmentFilename).

using Barbican.Core.Mail.Attachments;

namespace Barbican.Core.Tests.Mail.Attachments;

public sealed class AttachmentNamesTests
{
    [Fact]
    public void Split_keeps_the_extension_apart_and_a_dot_name_whole()
    {
        Assert.Equal(("report", ".pdf"), AttachmentNames.Split("report.pdf"));
        Assert.Equal(("archive.tar", ".gz"), AttachmentNames.Split("archive.tar.gz"));
        Assert.Equal(("README", string.Empty), AttachmentNames.Split("README"));
        Assert.Equal((".bashrc", string.Empty), AttachmentNames.Split(".bashrc"));
        Assert.Equal(("name.", string.Empty), AttachmentNames.Split("name."));
        Assert.Equal(("übersicht", ".txt"), AttachmentNames.Split("übersicht.txt"));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("no extension")]
    [InlineData("emoji 😀.png")]
    public void A_plain_name_has_no_problem(string name) =>
        Assert.Equal(AttachmentNameProblem.None, AttachmentNames.Problem(name));

    [Fact]
    public void A_name_over_255_characters_is_too_long()
    {
        Assert.Equal(AttachmentNameProblem.None, AttachmentNames.Problem(new string('a', 255)));
        Assert.Equal(AttachmentNameProblem.TooLong, AttachmentNames.Problem(new string('a', 256)));
    }

    [Theory]
    [InlineData("a/b.pdf")]
    [InlineData("a\\b.pdf")]
    [InlineData("tab\there.pdf")]
    [InlineData("null\0.pdf")]
    public void A_separator_or_control_character_is_refused(string name) =>
        Assert.Equal(AttachmentNameProblem.InvalidCharacters, AttachmentNames.Problem(name));

    [Fact]
    public void Display_falls_back_to_attachment()
    {
        Assert.Equal("report.pdf", AttachmentNames.Display("report.pdf"));
        Assert.Equal("attachment", AttachmentNames.Display(null));
        Assert.Equal("attachment", AttachmentNames.Display(string.Empty));
    }

    [Fact]
    public void Download_name_is_the_typed_name_with_only_what_a_file_system_cannot_take_replaced()
    {
        // the typed name as it is: spaces and dots survive (no template, no transforms)
        Assert.Equal("quarterly report v2.pdf", AttachmentNames.DownloadName("quarterly report v2.pdf"));
        Assert.Equal("a_b_c_d_e_f_g_h_i.pdf", AttachmentNames.DownloadName("a<b>c:d\"e\\f|g?h*i.pdf"));
        // directional overrides are dropped, not replaced: "report\u202Efdp.exe" must not read as "reportexe.pdf"
        Assert.Equal("reportfdp.exe", AttachmentNames.DownloadName("report\u202Efdp.exe"));
        Assert.Equal("attachment", AttachmentNames.DownloadName(" attachment "));
        Assert.Equal("attachment", AttachmentNames.DownloadName("   "));
        Assert.Equal("attachment", AttachmentNames.DownloadName("\u202E"));
    }
}
