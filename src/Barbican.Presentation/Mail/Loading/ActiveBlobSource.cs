// Mirrors: Rust main.rs:617 inline_cid_images (c.download; tag rust-final); Bulwark lib/jmap/client.ts:6876 getBlobDownloadUrl; RFC 8620 §6.2.
// The download goes through the active account's client (MailService.Client), which also covers the sample-data run. Owner body.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Downloads parts through the active account's JMAP client.</summary>
public sealed class ActiveBlobSource(IActiveMail active) : IBlobSource
{
    public async Task<byte[]?> DownloadAsync(Attachment part, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (part.BlobId is not { } blob || part.Size > maxBytes || active.Mail is not MailService { Client: var client })
        {
            return null;
        }

        var r = await client.DownloadAsync(client.Session.MailAccountId, blob, part.Name ?? "image", part.Type ?? "application/octet-stream", ct).ConfigureAwait(false);
        if (!r.IsOk || r.Value is null)
        {
            return null;
        }

        var stream = r.Value;
        await using (stream.ConfigureAwait(false))
        {
            return await CopyAsync(stream, maxBytes, ct).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]?> CopyAsync(Stream stream, long maxBytes, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (copy.Length + n > maxBytes)
            {
                return null;
            }

            await copy.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
        }

        return copy.ToArray();
    }
}
