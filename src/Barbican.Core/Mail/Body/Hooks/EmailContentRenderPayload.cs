// Mirrors: Bulwark onEmailContentRender { emailId, hasHtml, hasExternalContent } (lib/plugin-hook-catalog.ts:81;
// components/email/email-viewer.tsx:3936-3958: once per opened message, after its body is on screen). Owner body.

namespace Barbican.Core.Mail.Body.Hooks;

/// <summary>A message's body is on screen.</summary>
public sealed record EmailContentRenderPayload(string EmailId, bool HasHtml, bool HasExternalContent);
