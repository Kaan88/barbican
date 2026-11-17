// Mirrors: RFC 8621 section 4.8 (Email/import: blobId, mailboxIds, keywords, receivedAt); Rust mailimport.rs:232 import_entry. Contract from stage F of M2; owner actions (the call; the loop is unified's).

namespace Barbican.Core.Mail.Write;

/// <summary>One message to file with Email/import.</summary>
public sealed record EmailImportEntry(string BlobId, IReadOnlyList<string> MailboxIds, IReadOnlyList<string> Keywords, string? ReceivedAt);
