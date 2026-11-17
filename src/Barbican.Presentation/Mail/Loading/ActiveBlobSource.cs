// Mirrors: Rust main.rs:617 inline_cid_images (c.download; tag rust-final); Bulwark lib/jmap/client.ts:6876 getBlobDownloadUrl; RFC 8620 §6.2.
// The download goes through the active account's client (MailService.Client), which also covers the sample-data run. Owner body.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Downloads parts through the active account's JMAP client.</summary>
public sealed class ActiveBlobSource(IActiveMail active) : IBlobSource
{
    public Task<byte[]?> DownloadAsync(Attachment part, long maxBytes, CancellationToken ct) =>
        active.Mail is MailService { Client: var client } service
            ? new ClientBlobSource(client, service.AccountId).DownloadAsync(part, maxBytes, ct)
            : Task.FromResult<byte[]?>(null);
}
