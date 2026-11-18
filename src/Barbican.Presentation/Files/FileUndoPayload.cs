// Payload of the onFileUndo hook (spec/plugins/hooks.csv). Owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>Payload of onFileUndo: which action was undone.</summary>
public sealed record FileUndoPayload(string Action);
