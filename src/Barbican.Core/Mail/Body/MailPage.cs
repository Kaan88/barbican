// Mirrors: Rust main.rs:229 page() result (doc, has_script, bg; tag rust-final), with Fable review 1 F-03: no script goes into the
// page; the shell runs the dark pass and the text sizing against the frame document. Owner body.

using Barbican.Core.Settings.Accessibility;

namespace Barbican.Core.Mail.Body;

/// <summary>A message page for the body shell.</summary>
/// <param name="Html">The whole frame document (srcdoc), CSP first.</param>
/// <param name="Dark">The shell runs the dark pass on the frame before it reports the page ready (the page starts dark-pending).</param>
/// <param name="Background">The colour the page paints (CSS hex): shell, host and frame agree on it, so nothing flashes at the swap.</param>
/// <param name="HasRemoteContent">The HTML refers to remote images, backgrounds or fonts (the "Load external content" offer).</param>
public sealed record MailPage(string Html, bool Dark, string Background, bool HasRemoteContent)
{
    /// <summary>The text sizing the shell applies before the dark pass (Settings › Accessibility), or null.</summary>
    public MailSizing? Sizing { get; init; }
}
