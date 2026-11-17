// Mirrors: Bulwark onEmailClose payload { emailId } (lib/plugin-hook-catalog.ts:79; stores/email-store.ts:3983). Owner body.

namespace Barbican.Core.Mail.Body.Hooks;

/// <summary>The message that was closed or replaced.</summary>
public sealed record EmailIdPayload(string EmailId);
