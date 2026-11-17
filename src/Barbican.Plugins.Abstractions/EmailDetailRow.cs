// Mirrors: the Rust email.details rows ({label, value, tone}); M2 plan 3.2 (EmailDetailRow) and 4.5 (IEmailDetailsSource, reader-below-subject slot). Stub from stage F of M2; owner unified.

namespace Barbican.Plugins;

/// <summary>A row a plugin adds below the subject of an open message.</summary>
/// <param name="Label">The left text ("Spam analysis").</param>
/// <param name="Value">The right text ("Spam  ·  score 7.50 / 5.00").</param>
/// <param name="Tone">"danger", "warning", "success" or "neutral".</param>
public sealed record EmailDetailRow(string Label, string Value, string Tone);
