// Mirrors: Rust mailimport.rs:276 Outcome; M2 plan section 4.6 (IMailImport.ImportOutcome). Stub from stage F of M2; owner unified.

namespace Barbican.Core.Mail.Import;

/// <summary>What an import did.</summary>
/// <param name="Imported">Messages created.</param>
/// <param name="Already">Messages skipped because the folder already had that Message-ID.</param>
/// <param name="Errors">One message per failure (a refused message is not retried).</param>
public sealed record ImportOutcome(int Imported, int Already, IReadOnlyList<string> Errors);
