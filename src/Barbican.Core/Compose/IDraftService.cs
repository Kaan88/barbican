// Mirrors: Rust compose.rs autosave_now/save, discard (:1120); Bulwark lib/jmap/client.ts createDraft :6560 area, updateDraft (create the new draft, destroy the old one in the same request), email-composer.tsx autosave; RFC 8621 section 4.6.
// Contract from the M3 plan section 4.3; owner send-pipeline. Additive (reported): the autosave overload, because onBeforeDraftAutoSave may skip an autosave but never an explicit save.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>Saves and removes drafts. A save is one Email/set that creates the new draft (keyword <c>$draft</c>, Drafts mailbox) and destroys the previous one, so two saves never leave two drafts.</summary>
public interface IDraftService
{
    /// <summary>An explicit save (Ctrl+S, close, before send): always runs.</summary>
    Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, CancellationToken ct);

    /// <summary>The debounced autosave: a plugin's onBeforeDraftAutoSave may skip it (the result keeps the old draft id).</summary>
    Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, bool autosave, CancellationToken ct) => SaveAsync(draft, ct);

    Task<Result<Unit>> DiscardAsync(string accountId, string draftId, CancellationToken ct);
}
