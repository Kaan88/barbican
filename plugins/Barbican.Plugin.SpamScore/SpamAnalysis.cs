// Mirrors: Rust spam.rs:4 Spam (verdict "clean" | "suspicious" | "spam", detail) and tests/fixtures/plugins/spam-score/src/spam.rs (identical); Bulwark's
// spam-score plugin. Owner unified (M2 wave 2).

namespace Barbican.Plugin.SpamScore;

/// <summary>The verdict of the spam headers and the detail text.</summary>
/// <param name="Verdict">"clean", "suspicious" or "spam".</param>
/// <param name="Detail">"Score 7.50 / 5.00 threshold" and the stars, action and AI parts, joined by a spaced middle dot.</param>
public sealed record SpamAnalysis(string Verdict, string Detail);
