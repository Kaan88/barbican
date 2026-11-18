// Mirrors: Bulwark lib/plugin-hook-catalog.ts:251 { html, text }, { identityId } (onSignatureRender, a transform: a plug-in returns the signature to insert). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The signature about to go into the composer, with the identity it belongs to.</summary>
public sealed record SignatureRenderPayload(string Html, string Text, string IdentityId);
