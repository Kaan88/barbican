// Mirrors: Rust main.rs:617 inline_cid_images (c.download; tag rust-final); Bulwark lib/jmap/client.ts:6876 getBlobDownloadUrl; RFC 8620 §6.2.
// Fable review 3 R3-01: a unified row of another login (or of a shared account) downloads its inline images through its own client and
// JMAP account, not the active one's. Owner body.

using Barbican.Core.Jmap;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Downloads parts of one JMAP account through one client.</summary>
/// <param name="client">The login's client.</param>
/// <param name="accountId">The JMAP account that holds the blobs.</param>
public sealed class ClientBlobSource(IJmapClient client, string accountId) : IBlobSource
{
    public async Task<byte[]?> DownloadAsync(Attachment part, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (part.BlobId is not { } blob || part.Size > maxBytes)
        {
            return null;
        }

        var r = await client.DownloadAsync(accountId, blob, part.Name ?? "image", part.Type ?? "application/octet-stream", ct).ConfigureAwait(false);
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
