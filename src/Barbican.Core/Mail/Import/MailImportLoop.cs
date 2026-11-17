// Mirrors: Rust mailimport.rs:281 import (batches of the server's maxObjectsInSet clamped to 1..50; skip Message-IDs already in the folder; refuse a message over
// maxSizeUpload before uploading; upload then Email/import; report after each batch); RFC 8621 section 4.8; Bulwark lib/mail-import/engine.ts. Stub from stage F of M2; owner unified.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.Import;

/// <summary>The import loop over an <see cref="IImportServer"/>.</summary>
public static class MailImportLoop
{
    /// <summary>Imports the items into a mailbox a batch at a time; <paramref name="progress"/> gets the count of messages handled after each batch.</summary>
    public static Task<ImportOutcome> ImportAsync(IImportServer server, string mailboxId, IEnumerator<ImportItem> items, Action<int> progress, CancellationToken ct)
    {
        throw new PortPendingException("mailimport.rs:281 import");
    }
}
