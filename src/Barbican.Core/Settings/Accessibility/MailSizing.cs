// Mirrors: Bulwark lib/mail-reading.ts applyMailTextSize (scale message text, a minimum size) and the fit-to-pane shrink of Rust a11y.rs
// mail_script. Fable review 1 F-03: the body shell applies these to the frame document itself (no script runs inside a message frame), so
// the settings travel as data instead of a hashed script. Owner mailcore (fix-security stream).

namespace Barbican.Core.Settings.Accessibility;

/// <summary>What the body shell does to a message's text after it is laid out.</summary>
/// <param name="Factor">Every font size is multiplied by this (1: unchanged).</param>
/// <param name="MinPx">No text smaller than this many px (0: no minimum).</param>
/// <param name="Fit">A message wider than the pane is zoomed out to fit.</param>
public sealed record MailSizing(double Factor, double MinPx, bool Fit);
