// Mirrors: ADR 0008 decision 2 ("skip that entry and report which one (no values in logs)").

namespace Barbican.Core.Accounts;

/// <summary>What an import did. The messages name entries and reasons, never a value.</summary>
/// <param name="Imported">Accounts written as new entries.</param>
/// <param name="AlreadyPresent">Accounts the profile already had (left as they were).</param>
/// <param name="Skipped">One line per entry not imported, with the reason.</param>
public sealed record ImportReport(int Imported, int AlreadyPresent, IReadOnlyList<string> Skipped);
