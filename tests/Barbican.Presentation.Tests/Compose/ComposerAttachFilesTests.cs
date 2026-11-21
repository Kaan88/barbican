// Not Rust rows: G-076 (the composer attaches Files nodes by blob id). Bulwark components/email/email-composer.tsx:3182-3225, :6467-6475, :6735-6741.
// M6 stream composer-ui.

#pragma warning disable MA0006, MA0002 // assertions compare string literals by nature

using Barbican.Core.Files;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Tests.Files;

namespace Barbican.Presentation.Tests.Compose;

public sealed class ComposerAttachFilesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FileEntry F(string key, string name, long size = 12, string? type = "text/plain", string? blob = null, bool dir = false) =>
        new(key, "a", false, key, null, name, dir, dir ? null : (blob ?? "blob-" + key), size, null, dir ? null : type, null);

    [Fact]
    public async Task Picked_files_become_ready_chips_by_blob_id_with_no_upload()
    {
        var h = new ComposerHarness(files: new FakeFileStore([]));
        var vm = h.Open(filesAvailable: true);

        await vm.AttachStoredFilesAsync([F("f1", "report.pdf", 2048, "application/pdf"), F("f2", "notes.txt", 12)]);

        Assert.Equal(2, vm.Attachments.Count);
        Assert.All(vm.Attachments, a => Assert.Equal(AttachmentState.Done, a.State));
        Assert.Equal(["report.pdf", "notes.txt"], vm.Attachments.Select(a => a.Name));
        Assert.Equal(["blob-f1", "blob-f2"], vm.Attachments.Select(a => a.Attachment.BlobId));
        Assert.Equal("application/pdf", vm.Attachments[0].Attachment.Type);
        Assert.False(vm.HasPendingUploads);
        Assert.Equal(["blob-f1", "blob-f2"], h.Uploader.AttachedExisting.Select(a => a.BlobId));
    }

    [Fact]
    public async Task A_file_the_server_does_not_hold_is_skipped()
    {
        var h = new ComposerHarness(files: new FakeFileStore([]));
        var vm = h.Open(filesAvailable: true);

        await vm.AttachStoredFilesAsync([F("d1", "Folder", dir: true), F("gone", "gone.txt", blob: string.Empty)]);

        Assert.Empty(vm.Attachments);
        Assert.Empty(h.Uploader.AttachedExisting);
    }

    [Fact]
    public async Task A_refused_file_says_why_and_adds_no_chip()
    {
        var h = new ComposerHarness(files: new FakeFileStore([]));
        h.Uploader.RefuseExisting = "report.pdf would make the message too large.";
        var vm = h.Open(filesAvailable: true);

        await vm.AttachStoredFilesAsync([F("f1", "report.pdf")]);

        Assert.Empty(vm.Attachments);
        var toast = Assert.Single(h.Toasts.Shown);
        Assert.Contains("too large", toast.Message, StringComparison.Ordinal);
        Assert.True(toast.IsError);
    }

    [Fact]
    public async Task The_picker_opens_for_the_composers_account_and_attaches_what_was_picked()
    {
        var store = new FakeFileStore([F("f1", "report.pdf")]);
        var dialogs = new PickFiles { Answer = [F("f1", "report.pdf")] };
        var h = new ComposerHarness(files: store, dialogs: dialogs);
        var vm = h.Open(filesAvailable: true);

        await vm.OpenFilesPickerAsync();

        Assert.NotNull(dialogs.Asked);
        Assert.Equal("acc", dialogs.Asked.Picker.AccountKey);
        Assert.Same(store, h.Files);
        Assert.Single(vm.Attachments);
        Assert.Equal("blob-f1", vm.Attachments[0].Attachment.BlobId);
    }

    [Fact]
    public async Task Cancel_attaches_nothing()
    {
        var dialogs = new PickFiles { Answer = null };
        var h = new ComposerHarness(files: new FakeFileStore([]), dialogs: dialogs);
        var vm = h.Open(filesAvailable: true);

        await vm.OpenFilesPickerAsync();

        Assert.NotNull(dialogs.Asked);
        Assert.Empty(vm.Attachments);
    }

    [Fact]
    public async Task Without_files_on_the_server_the_entry_does_nothing()
    {
        var dialogs = new PickFiles { Answer = [F("f1", "report.pdf")] };
        var h = new ComposerHarness(files: new FakeFileStore([]), dialogs: dialogs);
        var vm = h.Open(filesAvailable: false);

        await vm.OpenFilesPickerAsync();

        Assert.Null(dialogs.Asked);
        Assert.Empty(vm.Attachments);
    }

    private sealed class PickFiles : IDialogService
    {
        public ComposerFilesRequest? Asked { get; private set; }

        public IReadOnlyList<FileEntry>? Answer { get; set; }

        public bool IsOpen => false;

        public Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct)
        {
            Asked = request as ComposerFilesRequest;
            return Task.FromResult((TResult?)(object?)Answer);
        }
    }
}
