// Mirrors: Bulwark onRenderEmailBody (lib/plugin-hook-catalog.ts:80; components/email/email-viewer.tsx:2712-2790): a transform that may
// take the body over (decrypted PGP or S/MIME). The value starts empty; a plugin that handles the message sets HandledBy and returns its
// HTML or text. The context of Bulwark's second argument rides along in Message. Owner body.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Body.Hooks;

/// <summary>The body a plugin may hand back instead of the message's own (transform).</summary>
/// <param name="Message">The message being rendered (Bulwark MessageContext: id, bodyValues, attachments, from).</param>
/// <param name="Html">Replacement HTML, or null.</param>
/// <param name="Text">Replacement text, or null.</param>
/// <param name="HandledBy">The plugin that took the body over; null keeps the message's own body.</param>
/// <param name="BlockExternalContent">The replacement must not load remote content even when the user allows it.</param>
public sealed record RenderableBody(EmailBody Message, string? Html = null, string? Text = null, string? HandledBy = null, bool BlockExternalContent = false);
