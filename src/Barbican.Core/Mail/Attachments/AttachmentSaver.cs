// Mirrors: Rust reader.rs:803 save_eml, :824 download_all (one stored zip, unique names, "subject (attachments).zip"); Bulwark
// components/email/reader-attachments.tsx (download, download all), lib/files-zip.ts, lib/plugin-hook-catalog.ts:153 onAttachmentDownload (told after
// a download). The blob comes through the account's own client; where it goes is asked through the injected picker (the app's save dialog, or a
// queued answer in a hidden automation run). Owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Mail.Reader.Hooks;
using Barbican.Core.Mail.Zip;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Mail.Attachments;

/// <summary>Saves one attachment, all of them as a zip, or the whole message as .eml.</summary>
/// <param name="accounts">The signed-in accounts (each blob comes through its own client).</param>
/// <param name="hooks">onAttachmentDownload is told after a download.</param>
/// <param name="time">The clock for the zip entry times.</param>
/// <param name="pick">Asks where to save a file of the suggested name; null when the user cancelled.</param>
public sealed class AttachmentSaver(IAccountManager accounts, IHookBus hooks, TimeProvider time, Func<string, CancellationToken, Task<Stream?>> pick) : IAttachmentSaver
{
    /// <summary>The most the zip may hold (everything is read into memory once).</summary>
    public const long MaxZipBytes = 512L * 1024 * 1024;

    public async Task<Result<Unit>> SaveAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(attachment);
        var a = attachment.Attachment;
        if (a.BlobId is null)
        {
            return Fail("This attachment has no content to save.");
        }

        var bytes = await DownloadAsync(message, a.BlobId, a.Name ?? "attachment", a.Type ?? "application/octet-stream", ct).ConfigureAwait(false);
        if (!bytes.IsOk)
        {
            return Result<Unit>.Fail(bytes.Error!);
        }

        var written = await WriteAsync(ReaderNames.SanitizePart(a.Name ?? "attachment", 120), bytes.Value!, ct).ConfigureAwait(false);
        if (written.IsOk && hooks.HasHandlers(HookNames.OnAttachmentDownload))
        {
            await hooks.NotifyAsync(HookNames.OnAttachmentDownload, new AttachmentInfo(message.ServerId, a.BlobId, a.Name ?? "attachment", a.Type, a.Size), ct).ConfigureAwait(false);
        }

        return written;
    }

    /// <summary>The most an in-app preview downloads (Bulwark file-preview: 12 MB).</summary>
    public const long MaxPreviewBytes = 12L * 1024 * 1024;

    /// <summary>
    /// The bytes of an attachment for the preview: refused over <see cref="MaxPreviewBytes"/> or without a blob; the onAttachmentPreview transform runs first and a plugin that took the preview over
    /// (Handled) gets a null value (nothing to show). Bulwark lib/plugin-hook-catalog.ts:154; I-228.
    /// </summary>
    public async Task<Result<byte[]?>> ReadForPreviewAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(attachment);
        var a = attachment.Attachment;
        if (a.BlobId is null)
        {
            return Result<byte[]?>.Fail(new ValidationError("This attachment has no content to show."));
        }

        if (a.Size > MaxPreviewBytes)
        {
            return Result<byte[]?>.Fail(new ValidationError("This attachment is too large to preview; save it instead."));
        }

        var info = new AttachmentInfo(message.ServerId, a.BlobId, a.Name ?? "attachment", a.Type, a.Size);
        if ((await hooks.TransformAsync(HookNames.OnAttachmentPreview, new AttachmentPreview(info, false), ct).ConfigureAwait(false)).Handled)
        {
            return Result<byte[]?>.Ok(null);
        }

        var bytes = await DownloadAsync(message, a.BlobId, a.Name ?? "attachment", a.Type ?? "application/octet-stream", ct).ConfigureAwait(false);
        return bytes.IsOk ? Result<byte[]?>.Ok(bytes.Value) : Result<byte[]?>.Fail(bytes.Error!);
    }

    public async Task<Result<Unit>> SaveAllAsZipAsync(MailTarget message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var fetched = await AttachmentsAsync(message, ct).ConfigureAwait(false);
        if (!fetched.IsOk)
        {
            return Result<Unit>.Fail(fetched.Error!);
        }

        var (subject, parts) = fetched.Value;
        if (parts.Count == 0)
        {
            return Fail("The message has no attachments.");
        }

        if (parts.Aggregate(0UL, (sum, p) => sum + p.Number("size")) > (ulong)MaxZipBytes)
        {
            return Fail("The attachments are too large to save as one zip.");
        }

        // the names come from the sender: sanitized first (Rust reader.rs:833 sanitize_name), so no entry carries "../" or a drive path (zip slip)
        var names = ZipArchiveBuilder.UniqueNames([.. parts.Select(p => ReaderNames.SanitizePart(p.Str("name") ?? string.Empty, 120))], "attachment");
        var entries = new List<ZipEntryData>();
        for (var i = 0; i < parts.Count; i++)
        {
            var bytes = await DownloadAsync(message, parts[i].Str("blobId")!, names[i], parts[i].Str("type") ?? "application/octet-stream", ct).ConfigureAwait(false);
            if (!bytes.IsOk)
            {
                return Result<Unit>.Fail(bytes.Error!);
            }

            entries.Add(new ZipEntryData(names[i], bytes.Value!));
        }

        var zip = ZipArchiveBuilder.Build(entries, TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone).DateTime);
        return zip.IsOk ? await WriteAsync(ReaderNames.BundleName(subject), zip.Value!, ct).ConfigureAwait(false) : Result<Unit>.Fail(zip.Error!);
    }

    /// <summary>Saves the raw message (its blob) as an .eml file named from the subject and date.</summary>
    public async Task<Result<Unit>> SaveEmlAsync(MailTarget message, CancellationToken ct)
    {
        var raw = await RawAsync(message, ct).ConfigureAwait(false);
        return raw.IsOk ? await WriteAsync(raw.Value.Name, raw.Value.Bytes, ct).ConfigureAwait(false) : Result<Unit>.Fail(raw.Error!);
    }

    /// <summary>The raw message (its blob, RFC 5322 bytes) and the .eml name it would be saved under (View source, Download .eml).</summary>
    public async Task<Result<(byte[] Bytes, string Name)>> RawAsync(MailTarget message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Client(message) is not { } client)
        {
            return Result<(byte[], string)>.Fail(new ValidationError("The account is not signed in."));
        }

        var got = await ReaderCalls.CallAsync(client, "Email/get", new JsonObject { ["accountId"] = message.JmapAccountId(client.Session.MailAccountId), ["ids"] = new JsonArray(message.ServerId), ["properties"] = new JsonArray("blobId", "subject", "receivedAt") }, ct).ConfigureAwait(false);
        if (!got.IsOk || got.Value!["list"]?[0] is not { } email || email.Str("blobId") is not { } blob)
        {
            return Result<(byte[], string)>.Fail(got.IsOk ? new ValidationError("The message is gone.") : got.Error!);
        }

        var bytes = await DownloadAsync(message, blob, "message.eml", "message/rfc822", ct).ConfigureAwait(false);
        return bytes.IsOk ? Result<(byte[], string)>.Ok((bytes.Value!, ReaderNames.EmlExportName(email))) : Result<(byte[], string)>.Fail(bytes.Error!);
    }

    // the login that holds the message (a unified row's own), Fable review 3 R3-02
    private IJmapClient? Client(MailTarget message) => accounts.SessionFor(message.Login)?.Client;

    private async Task<Result<(string Subject, IReadOnlyList<JsonNode> Parts)>> AttachmentsAsync(MailTarget message, CancellationToken ct)
    {
        if (Client(message) is not { } client)
        {
            return Result<(string, IReadOnlyList<JsonNode>)>.Fail(new ValidationError("The account is not signed in."));
        }

        var got = await ReaderCalls.CallAsync(client, "Email/get", new JsonObject { ["accountId"] = message.JmapAccountId(client.Session.MailAccountId), ["ids"] = new JsonArray(message.ServerId), ["properties"] = new JsonArray("subject", "attachments") }, ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<(string, IReadOnlyList<JsonNode>)>.Fail(got.Error!);
        }

        var email = got.Value!["list"]?[0];
        IReadOnlyList<JsonNode> parts = [.. (email?["attachments"] as JsonArray ?? []).OfType<JsonNode>().Where(p => p.Str("blobId") is not null)];
        return Result<(string, IReadOnlyList<JsonNode>)>.Ok((email.Str("subject") ?? string.Empty, parts));
    }

    private async Task<Result<byte[]>> DownloadAsync(MailTarget message, string blobId, string name, string type, CancellationToken ct)
    {
        if (Client(message) is not { } client)
        {
            return Result<byte[]>.Fail(new ValidationError("The account is not signed in."));
        }

        var r = await client.DownloadAsync(message.JmapAccountId(client.Session.MailAccountId), blobId, name, type, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<byte[]>.Fail(r.Error!);
        }

        var stream = r.Value!;
        await using (stream.ConfigureAwait(false))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct).ConfigureAwait(false);
            return Result<byte[]>.Ok(copy.ToArray());
        }
    }

    private async Task<Result<Unit>> WriteAsync(string suggested, byte[] bytes, CancellationToken ct)
    {
        // a full disk, a file in use or a folder that went away is a failed save with its reason, never an exception for the command runner
        // (Fable review 3 R3-05)
        try
        {
            var target = await pick(suggested, ct).ConfigureAwait(false);
            if (target is null)
            {
                return Fail("Not saved.");
            }

            await using (target.ConfigureAwait(false))
            {
                await target.WriteAsync(bytes, ct).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail("Could not save: " + e.Message);
        }

        return Result<Unit>.Ok(default);
    }

    private static Result<Unit> Fail(string text) => Result<Unit>.Fail(new ValidationError(text));
}
