// Synthetic-only tests for RD-06: renaming an attachment in the reader (Bulwark components/email/rename-attachment-dialog.tsx and
// email-viewer.tsx:3101-3123: the tile and every save follow the name, nothing is written back, another message shows the original names).

using Barbican.Core.Common;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Tests.Reader;

public sealed class AttachmentRenameTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rename_updates_the_tile_and_the_name_a_save_uses()
    {
        var body = Body("m1", "report.pdf");
        var reader = Open(body);
        var saver = new Saver();
        var dialogs = new Dialogs("quarterly report.pdf");
        var rename = Command(reader, body, saver, dialogs, CommandIds.RenameAttachment);
        var save = Command(reader, body, saver, dialogs, CommandIds.SaveAttachment);

        Assert.True((await rename.ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        var request = Assert.IsType<RenameAttachmentDialogRequest>(Assert.Single(dialogs.Seen));
        Assert.Equal("report", request.Stem);
        Assert.Equal(".pdf", request.Extension);
        Assert.Equal("quarterly report", reader.Attachments[0].Stem);
        Assert.Equal("quarterly report.pdf", reader.RenamedAttachment(0));

        Assert.True((await save.ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        Assert.Equal("quarterly report.pdf", saver.Last!.Attachment.Name);
    }

    [Fact]
    public async Task Cancel_leaves_the_original_name()
    {
        var body = Body("m1", "report.pdf");
        var reader = Open(body);
        var saver = new Saver();
        var dialogs = new Dialogs(null);
        var rename = Command(reader, body, saver, dialogs, CommandIds.RenameAttachment);

        Assert.True((await rename.ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        Assert.Equal("report", reader.Attachments[0].Stem);
        Assert.Null(reader.RenamedAttachment(0));

        Assert.True((await Command(reader, body, saver, dialogs, CommandIds.SaveAttachment).ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        Assert.Equal("report.pdf", saver.Last!.Attachment.Name);
    }

    [Fact]
    public async Task The_same_name_changes_nothing_and_a_save_sanitizes_only_what_a_file_system_cannot_take()
    {
        var body = Body("m1", "report.pdf");
        var reader = Open(body);
        var saver = new Saver();
        var dialogs = new Dialogs("report.pdf");
        var rename = Command(reader, body, saver, dialogs, CommandIds.RenameAttachment);

        Assert.True((await rename.ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        Assert.Null(reader.RenamedAttachment(0));

        dialogs.Answer = "a<b>.pdf";
        Assert.True((await rename.ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        Assert.Equal("a<b>", reader.Attachments[0].Stem);
        Assert.True((await Command(reader, body, saver, dialogs, CommandIds.SaveAttachment).ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        Assert.Equal("a_b_.pdf", saver.Last!.Attachment.Name);
    }

    [Fact]
    public async Task Another_message_shows_the_original_names_again()
    {
        var first = Body("m1", "report.pdf");
        var reader = Open(first);
        var saver = new Saver();
        var dialogs = new Dialogs("renamed.pdf");
        var rename = Command(reader, first, saver, dialogs, CommandIds.RenameAttachment);
        Assert.True((await rename.ExecuteAsync(ReaderCommands.AttachmentContext("login", first, 0), Ct)).IsOk);
        Assert.Equal("renamed", reader.Attachments[0].Stem);

        var second = Body("m2", "notes.txt");
        reader.Apply(ReaderSnapshots.FromBody(second, null, new ReaderOptions()));
        Assert.Equal("notes", reader.Attachments[0].Stem);
        Assert.Null(reader.RenamedAttachment(0));
    }

    [Fact]
    public void The_name_survives_another_snapshot_of_the_same_message()
    {
        var body = Body("m1", "report.pdf");
        var reader = Open(body);
        reader.RenameAttachment(0, "quarterly report.pdf");

        // a thumbnail arriving or the body loading again rebuilds the cards from the snapshot; the name stays
        reader.Apply(ReaderSnapshots.FromBody(body, null, new ReaderOptions()));
        Assert.Equal("quarterly report", reader.Attachments[0].Stem);
        Assert.Equal("quarterly report.pdf", reader.RenamedAttachment(0));
    }

    [Fact]
    public async Task A_stale_attachment_cannot_be_renamed()
    {
        var body = Body("m1", "report.pdf");
        var current = body;
        var reader = Open(body);
        var dialogs = new Dialogs("x.pdf");
        var command = new ReaderCommands(new Saver(), () => current, new Clipboard(), dialogs, reader: reader)
            .Create().Single(c => string.Equals(c.Id, CommandIds.RenameAttachment, StringComparison.Ordinal));
        var stale = ReaderCommands.AttachmentContext("login", body, 0);
        current = Body("m2", "other.txt");
        Assert.False(command.CanExecute(stale));
        Assert.False((await command.ExecuteAsync(stale, Ct)).IsOk);
        Assert.Empty(dialogs.Seen);
        Assert.Null(reader.RenamedAttachment(0));
    }

    private static ReaderViewModel Open(EmailBody body)
    {
        var reader = new ReaderViewModel();
        reader.Apply(ReaderSnapshots.FromBody(body, null, new ReaderOptions()));
        return reader;
    }

    private static IAppCommand Command(ReaderViewModel reader, EmailBody body, Saver saver, Dialogs dialogs, string id) =>
        new ReaderCommands(saver, () => body, new Clipboard(), dialogs, reader: reader).Create().Single(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    private static EmailBody Body(string id, string name) => new()
    {
        Id = id, Attachments = [new Attachment { BlobId = "blob", Name = name, Type = "application/pdf", Size = 10 }],
    };

    private sealed class Saver : IAttachmentSaver
    {
        public AttachmentRef? Last { get; private set; }
        public Task<Result<Unit>> SaveAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct) { Last = attachment; return Task.FromResult(Result<Unit>.Ok(default)); }
        public Task<Result<byte[]?>> ReadForPreviewAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct) => Task.FromResult(Result<byte[]?>.Ok([1, 2]));
        public Task<Result<Unit>> SaveAllAsZipAsync(MailTarget message, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
        public Task<Result<Unit>> SaveEmlAsync(MailTarget message, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
        public Task<Result<(byte[] Bytes, string Name)>> RawAsync(MailTarget message, CancellationToken ct) => Task.FromResult(Result<(byte[], string)>.Ok(([], "message.eml")));
    }

    private sealed class Clipboard : IClipboardService { public void SetText(string text) { } }

    private sealed class Dialogs(string? answer) : IDialogService
    {
        public string? Answer { get; set; } = answer;
        public List<object> Seen { get; } = [];
        public bool IsOpen => false;
        public Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct)
        {
            Seen.Add(request);
            return Task.FromResult(Answer is TResult typed ? typed : default);
        }
    }
}
