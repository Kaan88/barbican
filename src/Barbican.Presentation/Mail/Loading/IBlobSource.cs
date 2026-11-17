// Mirrors: Rust main.rs:617 inline_cid_images (c.download(blob, name, type); tag rust-final); RFC 8620 §6.2 (downloadUrl). Owner body.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Downloads a part of the active account's messages (inline images for the reader).</summary>
public interface IBlobSource
{
    /// <summary>The part's bytes, or null when it cannot be had (no blob id, too large, a failed download).</summary>
    Task<byte[]?> DownloadAsync(Attachment part, long maxBytes, CancellationToken ct);
}
