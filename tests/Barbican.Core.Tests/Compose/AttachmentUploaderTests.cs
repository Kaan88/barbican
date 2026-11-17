#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (as the ported Rust asserts)
using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Compose;

/// <summary>Attachment upload: the gates (account upload limit, per-message total) refuse before any request, the three plugin hooks, inline images with their cid, and the percent-encoded account id in the upload URL (J-034, J-036). FakeJmap-based.</summary>
public sealed class AttachmentUploaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int Uploads(PipelineRig rig) => rig.Fake.Server.Requests.Count(r => r.StartsWith("POST /jmap/upload", StringComparison.Ordinal));

    [Fact]
    public async Task An_upload_returns_the_blob_and_an_inline_image_keeps_its_cid()
    {
        var rig = await PipelineRig.StartAsync();
        var r = await rig.Uploader.UploadAsync(PipelineRig.Account, new AttachmentUpload("pic.png", "image/png", FakeSeed.InlineImage) { Cid = "pic@x" }, [], Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("pic@x", r.Value!.Cid);
        Assert.Equal(FakeSeed.InlineImage.Length, r.Value.Size);
        Assert.Equal(FakeSeed.InlineImage, rig.Store.Blob(r.Value.BlobId!));
    }

    [Fact]
    public async Task A_file_over_the_account_limit_is_refused_before_any_request()
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options() with { MaxSizeUpload = 10 });
        var r = await rig.Uploader.UploadAsync(PipelineRig.Account, new AttachmentUpload("big.bin", "application/octet-stream", new byte[11]), [], Ct);
        Assert.False(r.IsOk);
        Assert.Contains("too large", r.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(0, Uploads(rig));
    }

    [Fact]
    public async Task The_per_message_total_counts_what_the_draft_already_holds()
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options() with { MaxSizeAttachmentsPerEmail = 1000 });
        var existing = new[] { new DraftAttachment { Name = "a", Size = 950, BlobId = "x" } };
        var before = Uploads(rig);
        var over = await rig.Uploader.UploadAsync(PipelineRig.Account, new AttachmentUpload("b", "text/plain", new byte[100]), existing, Ct);
        Assert.False(over.IsOk);
        Assert.Contains("per message", over.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(before, Uploads(rig));
        Assert.True((await rig.Uploader.UploadAsync(PipelineRig.Account, new AttachmentUpload("c", "text/plain", new byte[50]), existing, Ct)).IsOk);
    }

    [Fact]
    public async Task Before_upload_can_refuse_a_file_and_after_upload_hears_the_blob_id()
    {
        var rig = await PipelineRig.StartAsync();
        var after = new TaskCompletionSource<AttachmentUploadInfo>();
        rig.Hooks.Observe<AttachmentUploadInfo>(HookNames.OnAfterAttachmentUpload, (p, _) =>
        {
            after.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        var ok = await rig.Uploader.UploadAsync(PipelineRig.Account, new AttachmentUpload("ok.txt", "text/plain", new byte[] { 1, 2, 3 }), [], Ct);
        var told = await after.Task.WaitAsync(TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.Equal(ok.Value!.BlobId, told.BlobId);
        Assert.Equal("ok.txt", told.Name);

        rig.Hooks.Intercept<AttachmentUploadInfo>(HookNames.OnBeforeAttachmentUpload, (p, _) => ValueTask.FromResult(!p.Name.EndsWith(".exe", StringComparison.Ordinal)));
        var before = Uploads(rig);
        var refused = await rig.Uploader.UploadAsync(PipelineRig.Account, new AttachmentUpload("bad.exe", "application/x-msdownload", new byte[] { 1 }), [], Ct);
        Assert.False(refused.IsOk);
        Assert.Equal(before, Uploads(rig));
    }

    [Fact]
    public async Task A_plugin_can_offload_the_file_and_nothing_is_uploaded()
    {
        var rig = await PipelineRig.StartAsync();
        rig.Hooks.Transform<BlobUploadPayload>(HookNames.OnBeforeBlobUpload, (p, _) => ValueTask.FromResult(p with { BlobId = "offloaded-1" }));
        var r = await rig.Uploader.UploadAsync(PipelineRig.Account, new AttachmentUpload("far.bin", "application/octet-stream", new byte[] { 1, 2 }), [], Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("offloaded-1", r.Value!.BlobId);
        Assert.Equal(0, Uploads(rig));
    }

    [Fact]
    public void The_account_id_in_the_upload_url_is_percent_encoded()
    {
        Assert.Equal("https://x.example/upload/a%20b%2Fc/", UriTemplates.Upload("https://x.example/upload/{accountId}/", "a b/c"));
        _ = (JsonNode?)null;
    }
}
