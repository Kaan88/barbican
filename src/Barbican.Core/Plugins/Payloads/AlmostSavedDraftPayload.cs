// Mirrors: Bulwark lib/plugin-hook-catalog.ts:94 onBeforeDraftAutoSave (AlmostSavedDraft: a plugin may change the draft or return false to skip a save while typing; saves on close and before send always run). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A draft about to be saved by the autosave; a transform handler returns it changed, or with <see cref="Skip"/> set to skip this save.</summary>
public sealed record AlmostSavedDraftPayload(string AccountId, string? DraftId, string To, string Cc, string Bcc, string Subject, string Html, string Text)
{
    /// <summary>Set by a handler to skip this save (honoured for an autosave only).</summary>
    public bool Skip { get; init; }
}
