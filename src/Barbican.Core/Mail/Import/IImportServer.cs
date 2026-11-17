// Mirrors: Rust mailimport.rs:Server trait (near :240) Server (account, max_set, max_upload, upload, call, message_ids_in); RFC 8620 section 6.1 (upload), RFC 8621 section 4.8
// (Email/import); Stalwart crates/jmap/src/{blob/upload,email/import}.rs. Stub from stage F of M2; owner unified.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Import;

/// <summary>What the import loop needs from a JMAP account (a seam so the loop is tested without a server).</summary>
public interface IImportServer
{
    /// <summary>The account id.</summary>
    string Account { get; }

    /// <summary>maxObjectsInSet.</summary>
    int MaxSet { get; }

    /// <summary>maxSizeUpload in bytes.</summary>
    int MaxUpload { get; }

    /// <summary>Uploads a message and returns its blob id.</summary>
    Task<Result<string>> UploadAsync(byte[] data, CancellationToken ct);

    /// <summary>Runs a method-call array and returns the responses.</summary>
    Task<Result<IReadOnlyList<JsonNode>>> CallAsync(JsonNode calls, CancellationToken ct);

    /// <summary>The Message-IDs already in a folder, so a retried import does not file messages twice.</summary>
    Task<IReadOnlySet<string>> MessageIdsInAsync(string mailboxId, CancellationToken ct);
}
