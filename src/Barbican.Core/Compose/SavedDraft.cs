// Mirrors: Bulwark lib/jmap/client.ts createDraft/updateDraft; RFC 8621 section 4.6. Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>A draft as the server holds it after a save.</summary>
public sealed record SavedDraft(string DraftId);
