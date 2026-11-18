// The answer of the move dialog: the chosen destination folder (Key null: the root, My files). Owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>The destination of a move.</summary>
public sealed record FileMoveAnswer(string? Key);
