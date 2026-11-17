// Mirrors: Rust reader.rs:907 blob_copy_request, :912 copied_blob; RFC 8620 section 6.3 (Blob/copy: fromAccountId, accountId, blobIds; copied,
// notCopied); Stalwart crates/jmap/src/blob/copy.rs. J-036 (partial: Blob/upload is M3).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Mail.Reader;

/// <summary>The Blob/copy that brings a blob from the mail account into the Files account (Save to Files from a shared mailbox).</summary>
public static class BlobCopyRequests
{
    /// <summary>The method-call array for the copy, or null when both accounts are the same.</summary>
    public static JsonArray? Request(string fromAccountId, string toAccountId, string blobId)
    {
        ArgumentNullException.ThrowIfNull(fromAccountId);
        ArgumentNullException.ThrowIfNull(toAccountId);
        ArgumentNullException.ThrowIfNull(blobId);
        if (string.Equals(fromAccountId, toAccountId, StringComparison.Ordinal))
        {
            return null;
        }

        return new JsonArray(new JsonArray("Blob/copy", Args(fromAccountId, toAccountId, blobId), "b"));
    }

    /// <summary>The arguments of the copy.</summary>
    public static JsonObject Args(string fromAccountId, string toAccountId, string blobId) =>
        new() { ["fromAccountId"] = fromAccountId, ["accountId"] = toAccountId, ["blobIds"] = new JsonArray(blobId) };

    /// <summary>The new blob id from a Blob/copy response; a method error or notCopied is a failure.</summary>
    public static Result<string> Copied(JsonNode response, string blobId)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(blobId);
        var call = response[0];
        if (string.Equals(call?[0].AsString(), "error", StringComparison.Ordinal))
        {
            return Result<string>.Fail(MethodErrors.From("Blob/copy", call?[1]));
        }

        return Copied(call?[1] as JsonObject ?? [], blobId);
    }

    /// <summary>The new blob id from the arguments of a Blob/copy answer.</summary>
    public static Result<string> Copied(JsonObject args, string blobId)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(blobId);
        if (args["copied"]?[blobId].AsString() is { } copied)
        {
            return Result<string>.Ok(copied);
        }

        return Result<string>.Fail(SetErrors.From(blobId, args["notCopied"]?[blobId]));
    }
}
