// Mirrors: Bulwark lib/plugin-hook-catalog.ts:245-250 IdentityView (onIdentitiesLoaded, onIdentityCreate, onIdentityUpdate, onIdentitySelect). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>An identity as a plug-in sees it.</summary>
public sealed record IdentityViewPayload(string Id, string Name, string Email, string? ReplyTo, string? Bcc, string TextSignature, string HtmlSignature);
