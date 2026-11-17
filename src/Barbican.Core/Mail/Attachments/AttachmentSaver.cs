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
            await hooks.NotifyAsync(HookNames.OnAttachmentDownload, new AttachmentInfo(message.EmailId, a.BlobId, a.Name ?? "attachment", a.Type, a.Size), ct).ConfigureAwait(false);
        }

        return written;
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

        var names = ZipArchiveBuilder.UniqueNames([.. parts.Select(p => p.Str("name") ?? string.Empty)], "attachment");
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
        ArgumentNullException.ThrowIfNull(message);
        if (Client(message) is not { } client)
        {
            return Fail("The account is not signed in.");
        }

        var got = await ReaderCalls.CallAsync(client, "Email/get", new JsonObject { ["accountId"] = message.AccountId, ["ids"] = new JsonArray(message.EmailId), ["properties"] = new JsonArray("blobId", "subject", "receivedAt") }, ct).ConfigureAwait(false);
        if (!got.IsOk || got.Value!["list"]?[0] is not { } email || email.Str("blobId") is not { } blob)
        {
            return got.IsOk ? Fail("The message is gone.") : Result<Unit>.Fail(got.Error!);
        }

        var bytes = await DownloadAsync(message, blob, "message.eml", "message/rfc822", ct).ConfigureAwait(false);
        return bytes.IsOk ? await WriteAsync(ReaderNames.EmlExportName(email), bytes.Value!, ct).ConfigureAwait(false) : Result<Unit>.Fail(bytes.Error!);
    }

    private IJmapClient? Client(MailTarget message) => accounts.SessionFor(message.AccountKey)?.Client;

    private async Task<Result<(string Subject, IReadOnlyList<JsonNode> Parts)>> AttachmentsAsync(MailTarget message, CancellationToken ct)
    {
        if (Client(message) is not { } client)
        {
            return Result<(string, IReadOnlyList<JsonNode>)>.Fail(new ValidationError("The account is not signed in."));
        }

        var got = await ReaderCalls.CallAsync(client, "Email/get", new JsonObject { ["accountId"] = message.AccountId, ["ids"] = new JsonArray(message.EmailId), ["properties"] = new JsonArray("subject", "attachments") }, ct).ConfigureAwait(false);
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

        var r = await client.DownloadAsync(message.AccountId, blobId, name, type, ct).ConfigureAwait(false);
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
        var target = await pick(suggested, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Fail("Not saved.");
        }

        await using (target.ConfigureAwait(false))
        {
            await target.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        return Result<Unit>.Ok(default);
    }

    private static Result<Unit> Fail(string text) => Result<Unit>.Fail(new ValidationError(text));
}
