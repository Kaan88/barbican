// Mirrors: Rust mailimport.rs:10 Item (a message to import: bytes, keywords, arrival time); Bulwark lib/mail-import/engine.ts. Stub from stage F of M2; owner unified.

namespace Barbican.Core.Mail.Import;

/// <summary>One message read from an .eml or .mbox file.</summary>
/// <param name="Bytes">The raw RFC 5322 message.</param>
/// <param name="Keywords">The JMAP keywords from the mbox status headers.</param>
/// <param name="ReceivedAt">The arrival time from the Date header as RFC 3339 UTC, or null.</param>
#pragma warning disable CA1819 // raw message bytes
public sealed record ImportItem(byte[] Bytes, IReadOnlyList<string> Keywords, string? ReceivedAt);
#pragma warning restore CA1819
