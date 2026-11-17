// Mirrors: Bulwark lib/plugin-hook-catalog.ts:91 (onComposerOpen: ComposerContext). Owner composer-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The composer that just opened.</summary>
public sealed record ComposerContextPayload(string ComposerId, string AccountKey, string Kind, string? DraftId);
