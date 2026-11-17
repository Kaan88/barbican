// Mirrors: Rust main.rs:229 page() result (doc, has_script, bg; tag rust-final). Owner body.

namespace Barbican.Core.Mail.Body;

/// <summary>A message page for the body shell.</summary>
/// <param name="Html">The whole frame document (srcdoc), CSP first.</param>
/// <param name="HasScript">The page carries our own hashed script: its frame gets <c>allow-scripts</c> (<see cref="MailSandbox.For"/>).</param>
/// <param name="Background">The colour the page paints (CSS hex): shell, host and frame agree on it, so nothing flashes at the swap.</param>
/// <param name="HasRemoteContent">The HTML refers to remote images, backgrounds or fonts (the "Load external content" offer).</param>
public sealed record MailPage(string Html, bool HasScript, string Background, bool HasRemoteContent);
