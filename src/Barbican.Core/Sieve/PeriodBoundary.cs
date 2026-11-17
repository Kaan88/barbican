// Mirrors: Rust sieve.rs:399 period_boundary (the UTC (date, time) of an ISO timestamp); Bulwark lib/sieve/period.ts. Stub from stage F of M2; owner search.

namespace Barbican.Core.Sieve;

/// <summary>An activeFrom or activeUntil instant as the UTC date and time the generated Sieve tests compare.</summary>
public sealed record PeriodBoundary(string Date, string Time);
