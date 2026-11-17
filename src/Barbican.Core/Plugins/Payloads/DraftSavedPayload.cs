// Mirrors: Bulwark lib/plugin-hook-catalog.ts:95 onDraftAutoSave ({ draftId, subject }). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A draft was saved.</summary>
public sealed record DraftSavedPayload(string DraftId, string Subject);
