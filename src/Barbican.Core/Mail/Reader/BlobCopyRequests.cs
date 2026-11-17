// Mirrors: Rust reader.rs:907 blob_copy_request, :912 copied_blob; RFC 8620 section 6.3 (Blob/copy); Stalwart crates/jmap/src/blob/copy.rs. Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Reader;

/// <summary>The Blob/copy that brings a blob from the mail account into the Files account (Save to Files from a shared mailbox).</summary>
public static class BlobCopyRequests
{
    /// <summary>The method-call array for the copy, or null when both accounts are the same.</summary>
    public static JsonNode? Request(string fromAccountId, string toAccountId, string blobId)
    {
        throw new PortPendingException("reader.rs:907 blob_copy_request");
    }

    /// <summary>The new blob id from a Blob/copy response; a method error or notCopied is a failure.</summary>
    public static Result<string> Copied(JsonNode response, string blobId)
    {
        throw new PortPendingException("reader.rs:912 copied_blob");
    }
}
