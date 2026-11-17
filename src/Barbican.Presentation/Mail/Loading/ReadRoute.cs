// Mirrors: Rust main.rs:426-427 (mail::owner, mail::shared_account: a read goes to the account that holds the row; tag rust-final); Bulwark
// lib/unified-mailbox.ts (each unified email keeps its account). Fable review 3 R3-01. Owner body.

using Barbican.Core.Mail;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Where the reader reads one list row from (the server id and cache key come from the row id itself, UnifiedRows.ParseRow).</summary>
/// <param name="Mail">The read service of that account.</param>
/// <param name="Blobs">Its inline-image source, or null for the fetcher's own (the active account's).</param>
public sealed record ReadRoute(IMailService Mail, IBlobSource? Blobs);
