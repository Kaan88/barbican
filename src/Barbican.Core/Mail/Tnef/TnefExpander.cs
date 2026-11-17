// Mirrors: MS-OXTNEF (the TNEF stream: attAttachTitle, attAttachData, attAttachment props), read with MimeKit's TnefReader through
// TnefPart.ExtractAttachments; Bulwark lib/tnef (winmail.dat shows the files inside it). M2 plan 4.5 (IAttachmentExpander), spec G-047.
// Owner unified.
// The files inside are not on the server, so they are not uploaded (that would be a write, and some accounts are read-only): each one gets
// a local blob id "tnef:<parent blob>/<index>" and its bytes are kept here for this run, for the reader's save and open (OpenLocal).

using System.Collections.Concurrent;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using MimeKit;
using MimeKit.Tnef;

namespace Barbican.Core.Mail.Tnef;

/// <summary>Replaces a winmail.dat attachment by the files it carries.</summary>
/// <param name="clientOf">The client of an account key, or null when it is not signed in.</param>
public sealed class TnefExpander(Func<string, IJmapClient?> clientOf) : IAttachmentExpander
{
    /// <summary>The prefix of the blob id of a file taken out of a TNEF attachment.</summary>
    public const string LocalPrefix = "tnef:";

    // a winmail.dat over this size is not expanded (the files inside are kept in memory)
    private const long MaxTnefBytes = 50_000_000;

    private static readonly ConcurrentDictionary<string, byte[]> Local = new(StringComparer.Ordinal);

    /// <summary>The bytes of a file taken out of a TNEF attachment in this run, or null.</summary>
    public static byte[]? OpenLocal(string blobId) => Local.TryGetValue(blobId, out var b) ? b : null;

    /// <inheritdoc />
    public bool CanHandle(AttachmentRef attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        var a = attachment.Attachment;
        return string.Equals(a.Name, "winmail.dat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Type, "application/ms-tnef", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Type, "application/vnd.ms-tnef", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<AttachmentRef>>> ExpandAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(attachment);
        var a = attachment.Attachment;
        if (a.BlobId is not { Length: > 0 } blobId || a.Size > MaxTnefBytes)
        {
            return Result<IReadOnlyList<AttachmentRef>>.Fail(new ValidationError("This attachment cannot be opened."));
        }

        if (clientOf(message.Login) is not { } client)
        {
            return Result<IReadOnlyList<AttachmentRef>>.Fail(new ValidationError("That account is not signed in."));
        }

        var account = message.JmapAccountId(client.Session.MailAccountId);
        var download = await client.DownloadAsync(account, blobId, a.Name ?? "winmail.dat", a.Type ?? "application/ms-tnef", ct).ConfigureAwait(false);
        if (!download.IsOk)
        {
            return Result<IReadOnlyList<AttachmentRef>>.Fail(download.Error!);
        }

        using var buffer = new MemoryStream();
        await using (download.Value!.ConfigureAwait(false))
        {
            await download.Value!.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }

        return Result<IReadOnlyList<AttachmentRef>>.Ok(Expand(blobId, buffer.ToArray()));
    }

    /// <summary>The files inside a TNEF stream, numbered from 0, with local blob ids; empty when the stream holds none or is not TNEF.</summary>
    public static IReadOnlyList<AttachmentRef> Expand(string parentBlobId, byte[] tnef)
    {
        ArgumentNullException.ThrowIfNull(parentBlobId);
        ArgumentNullException.ThrowIfNull(tnef);

        // TNEF strings are in the sender's ANSI code page (MimeKit's property reader opens 1252 when it loads); .NET has only the Unicode
        // ones built in. Registering the provider is idempotent and process-wide.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var part = new TnefPart { Content = new MimeContent(new MemoryStream(tnef, writable: false)) };
        var output = new List<AttachmentRef>();
        try
        {
            foreach (var file in part.ExtractAttachments().OfType<MimePart>())
            {
                using var data = new MemoryStream();
                file.Content?.DecodeTo(data);
                var id = $"{LocalPrefix}{parentBlobId}/{output.Count}";
                Local[id] = data.ToArray();
                output.Add(new AttachmentRef(output.Count, new Attachment
                {
                    BlobId = id,
                    Name = file.FileName ?? $"attachment-{output.Count + 1}",
                    Type = file.ContentType.MimeType,
                    Size = data.Length,
                    Disposition = "attachment",
                }));
            }
        }
        catch (TnefException)
        {
            return [];
        }

        return output;
    }
}
