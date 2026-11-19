// Synthetic-only tests for the mailAttachmentAction preference and account-scoped attachment target.
using Barbican.Core.Common;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Reader;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Reader;

public sealed class AttachmentActionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_and_download_follow_the_live_preference_and_explicit_save_always_saves()
    {
        var body = Body("m1", "text/plain", "note.txt");
        var prefs = new TestPrefs();
        var saver = new Saver();
        var dialogs = new Dialogs();
        var commands = new ReaderCommands(saver, () => body, new Clipboard(), dialogs, prefs: prefs).Create();
        var open = commands.Single(command => string.Equals(command.Id, CommandIds.OpenAttachment, StringComparison.Ordinal));
        var save = commands.Single(command => string.Equals(command.Id, CommandIds.SaveAttachment, StringComparison.Ordinal));
        var context = ReaderCommands.AttachmentContext("login", body, 0);
        Assert.True(open.CanExecute(context));
        Assert.True((await open.ExecuteAsync(context, Ct)).IsOk);
        Assert.Equal(1, saver.Previews);
        Assert.Equal(0, saver.Saves);
        Assert.IsType<AttachmentPreviewDialogRequest>(Assert.Single(dialogs.Seen));
        prefs.Set("{\"mailAttachmentAction\":\"download\"}");
        Assert.True((await open.ExecuteAsync(context, Ct)).IsOk);
        Assert.Equal(1, saver.Saves);
        Assert.True((await save.ExecuteAsync(context, Ct)).IsOk);
        Assert.Equal(2, saver.Saves);
        prefs.Set("{\"mailAttachmentAction\":\"preview\"}");
        Assert.True((await open.ExecuteAsync(context, Ct)).IsOk);
        Assert.Equal(2, saver.Previews);
    }

    [Theory]
    [InlineData("application/pdf", "report.pdf", 10)]
    [InlineData("application/octet-stream", "program.exe", 10)]
    [InlineData("image/png", "large.png", AttachmentSaver.MaxPreviewBytes + 1)]
    public async Task Unsupported_or_too_large_previews_fall_back_to_save(string type, string name, long size)
    {
        var body = Body("m1", type, name, size);
        var saver = new Saver();
        var dialogs = new Dialogs();
        var open = new ReaderCommands(saver, () => body, new Clipboard(), dialogs).Create().Single(c => string.Equals(c.Id, CommandIds.OpenAttachment, StringComparison.Ordinal));
        Assert.True((await open.ExecuteAsync(ReaderCommands.AttachmentContext("login", body, 0), Ct)).IsOk);
        Assert.Equal(1, saver.Saves);
        Assert.Equal(0, saver.Previews);
        Assert.Empty(dialogs.Seen);
    }

    [Fact]
    public async Task Unified_shared_target_and_stale_reader_are_handled_safely()
    {
        var id = UnifiedRows.RowId("source-login", "shared-id", "server-id");
        var body = Body(id, "text/plain", "note.txt");
        var current = body;
        var saver = new Saver();
        var open = new ReaderCommands(saver, () => current, new Clipboard(), new Dialogs(), prefs: new TestPrefs("{\"mailAttachmentAction\":\"download\"}"))
            .Create().Single(c => string.Equals(c.Id, CommandIds.OpenAttachment, StringComparison.Ordinal));
        var context = ReaderCommands.AttachmentContext("visible-login", body, 0);
        Assert.True((await open.ExecuteAsync(context, Ct)).IsOk);
        Assert.Equal("source-login", saver.Target!.Login);
        Assert.Equal("shared-id", saver.Target.JmapAccountId("own-id"));
        Assert.Equal("server-id", saver.Target.ServerId);
        current = Body("other", "text/plain", "other.txt");
        Assert.False(open.CanExecute(context));
        Assert.False((await open.ExecuteAsync(context, Ct)).IsOk);
        Assert.Equal(1, saver.Saves);
        Assert.False(open.CanExecute(ReaderCommands.AttachmentContext("visible-login", current, -1)));
    }

    [Fact]
    public async Task Switching_accounts_with_the_same_plain_message_id_rejects_the_stale_target()
    {
        var original = Body("same-id", "text/plain", "first.txt");
        var current = Body("same-id", "text/plain", "second.txt");
        var account = "account-b";
        var saver = new Saver();
        var command = new ReaderCommands(saver, () => current, new Clipboard(), new Dialogs(), activeAccount: () => account)
            .Create().Single(c => string.Equals(c.Id, CommandIds.OpenAttachment, StringComparison.Ordinal));
        var stale = ReaderCommands.AttachmentContext("account-a", original, 0);
        Assert.False(command.CanExecute(stale));
        Assert.False((await command.ExecuteAsync(stale, Ct)).IsOk);
        Assert.Equal(0, saver.Previews);
        Assert.Equal(0, saver.Saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delayed_preview_is_discarded_when_reader_or_account_changes(bool switchAccount)
    {
        var current = Body("same-id", "text/plain", "first.txt");
        var account = "account-a";
        var saver = new Saver { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var dialogs = new Dialogs();
        var command = new ReaderCommands(saver, () => current, new Clipboard(), dialogs, activeAccount: () => account)
            .Create().Single(c => string.Equals(c.Id, CommandIds.OpenAttachment, StringComparison.Ordinal));
        var context = ReaderCommands.AttachmentContext(account, current, 0);
        var request = command.ExecuteAsync(context, Ct);
        Assert.Equal(1, saver.Previews);
        if (switchAccount) account = "account-b";
        else current = Body("same-id", "text/plain", "other.txt");
        saver.Pending!.SetResult(Result<byte[]?>.Ok([1, 2]));
        Assert.True((await request).IsOk);
        Assert.Empty(dialogs.Seen);
    }

    [Fact]
    public async Task Open_preview_save_keeps_its_captured_original_target()
    {
        var current = Body("same-id", "text/plain", "first.txt");
        var account = "account-a";
        var saver = new Saver();
        var dialogs = new Dialogs();
        var command = new ReaderCommands(saver, () => current, new Clipboard(), dialogs, activeAccount: () => account)
            .Create().Single(c => string.Equals(c.Id, CommandIds.OpenAttachment, StringComparison.Ordinal));
        Assert.True((await command.ExecuteAsync(ReaderCommands.AttachmentContext(account, current, 0), Ct)).IsOk);
        var preview = Assert.IsType<AttachmentPreviewDialogRequest>(Assert.Single(dialogs.Seen));
        account = "account-b";
        current = Body("same-id", "text/plain", "other.txt");
        await preview.Save(Ct);
        Assert.Equal("account-a", saver.Target!.AccountKey);
        Assert.Equal(1, saver.Saves);
    }

    private static EmailBody Body(string id, string type, string name, long size = 10) => new()
    {
        Id = id, Attachments = [new Attachment { BlobId = "blob", Name = name, Type = type, Size = size }],
    };

    private sealed class Saver : IAttachmentSaver
    {
        public int Saves { get; private set; }
        public int Previews { get; private set; }
        public MailTarget? Target { get; private set; }
        public TaskCompletionSource<Result<byte[]?>>? Pending { get; init; }
        public Task<Result<Unit>> SaveAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct) { Saves++; Target = message; return Task.FromResult(Result<Unit>.Ok(default)); }
        public Task<Result<byte[]?>> ReadForPreviewAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct) { Previews++; Target = message; return Pending?.Task ?? Task.FromResult(Result<byte[]?>.Ok([1, 2])); }
        public Task<Result<Unit>> SaveAllAsZipAsync(MailTarget message, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
        public Task<Result<Unit>> SaveEmlAsync(MailTarget message, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
        public Task<Result<(byte[] Bytes, string Name)>> RawAsync(MailTarget message, CancellationToken ct) => Task.FromResult(Result<(byte[], string)>.Ok(([], "message.eml")));
    }

    private sealed class Clipboard : IClipboardService { public void SetText(string text) => throw new InvalidOperationException("No clipboard in this test."); }
    private sealed class Dialogs : IDialogService
    {
        public List<object> Seen { get; } = [];
        public bool IsOpen => false;
        public Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct) { Seen.Add(request); return Task.FromResult(default(TResult)); }
    }
}
