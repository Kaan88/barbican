// The answer of the upload conflict dialog (D-M5-14: the default is Keep both; Apply to all remembers it for the rest of the upload). Owner files-ui.

using Barbican.Core.Files;

namespace Barbican.Presentation.Files;

/// <summary>What to do with a name that is taken, and whether to ask again.</summary>
public sealed record FileConflictAnswer(FileConflictChoice Choice, bool ApplyToAll);
