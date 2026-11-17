// Mirrors: Rust compose.rs upload (:1120 area), upload_inline; Bulwark lib/jmap/client.ts uploadBlob :6662, components/email/email-composer.tsx attachment limits (maxSizeUpload of the account, maxSizeAttachmentsPerEmail of the mail capability),
// lib/plugin-hook-catalog.ts:150-152 (onBeforeAttachmentUpload intercept, onBeforeBlobUpload transform that may offload the file, onAfterAttachmentUpload); RFC 8620 section 6.1 (upload; the percent-encoded account id in the URL template, J-034),
// RFC 8621 section 1.3.1 (maxSizeAttachmentsPerEmail). Stalwart crates/jmap/src/blob/upload.rs (maxSizeUpload, 413 / tooLarge).

using System.Globalization;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Compose;

/// <summary>Uploads attachments after the gates: no request is made for a file over the limit.</summary>
public sealed class AttachmentUploader(IAccountManager accounts, IHookBus hooks) : IAttachmentUploader
{
    public async Task<Result<DraftAttachment>> UploadAsync(string accountId, AttachmentUpload upload, IReadOnlyList<DraftAttachment> existing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(existing);
        var ctx = ComposeContext.For(accounts, accountId);
        if (!ctx.IsOk)
        {
            return Result<DraftAttachment>.Fail(ctx.Error!);
        }

        var size = upload.Data.Length;
        var type = string.IsNullOrWhiteSpace(upload.Type) ? "application/octet-stream" : upload.Type;
        var limit = ctx.Value!.Client.Session.Limits.MaxSizeUpload;
        if (size > limit)
        {
            return Fail($"{upload.Name} is too large to attach (the server takes up to {Mb(limit)}).");
        }

        if (PerMessageCap(ctx.Value) is { } cap && existing.Sum(a => a.Size) + size > cap)
        {
            return Fail($"{upload.Name} would make the message too large (the server takes up to {Mb(cap)} of attachments per message).");
        }

        var info = new AttachmentUploadInfo(upload.Name, type, size);
        if (!await hooks.AllowAsync(HookNames.OnBeforeAttachmentUpload, () => info, ct).ConfigureAwait(false))
        {
            return Fail($"A plugin refused {upload.Name}.");
        }

        var blobId = await UploadBlobAsync(ctx.Value, upload, type, ct).ConfigureAwait(false);
        if (!blobId.IsOk)
        {
            return Result<DraftAttachment>.Fail(blobId.Error!);
        }

        var id = blobId.Value!;
        if (hooks.HasHandlers(HookNames.OnAfterAttachmentUpload))
        {
            hooks.Fire(HookNames.OnAfterAttachmentUpload, () => info with { BlobId = id });
        }

        return Result<DraftAttachment>.Ok(new DraftAttachment { Name = upload.Name, Type = type, Size = size, BlobId = id, Cid = upload.Cid });
    }

    private static string Mb(long bytes) => (bytes / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";

    private static Result<DraftAttachment> Fail(string text) => Result<DraftAttachment>.Fail(new ValidationError(text));

    private static long? PerMessageCap(ComposeContext ctx)
    {
        var mail = ctx.Client.Session.CapabilityObject(ctx.Account, JmapCapabilities.Mail);
        return mail is { } m && m.TryGetProperty("maxSizeAttachmentsPerEmail", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt64(out var n) && n > 0 ? n : null;
    }

    private async Task<Result<string>> UploadBlobAsync(ComposeContext ctx, AttachmentUpload upload, string type, CancellationToken ct)
    {
        // a plugin may offload the file (Bulwark onBeforeBlobUpload) and hand back the blob id it made: nothing is uploaded then
        var offer = new BlobUploadPayload(upload.Name, type, upload.Data.Length);
        var offloaded = await hooks.TransformAsync(HookNames.OnBeforeBlobUpload, offer, ct).ConfigureAwait(false);
        if (offloaded.BlobId is { Length: > 0 } made)
        {
            return Result<string>.Ok(made);
        }

        var up = await new MailWriter(ctx.Client, ctx.Account).UploadAsync(upload.Data, type, ct).ConfigureAwait(false);
        return up.IsOk ? Result<string>.Ok(up.Value!.BlobId) : Result<string>.Fail(up.Error!);
    }
}
